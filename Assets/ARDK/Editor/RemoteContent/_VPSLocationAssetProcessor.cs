using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

using Niantic.ARDK.Utilities.Editor;
using Niantic.ARDK.Utilities.Logging;

using UnityEditor;
using UnityEngine;

using RemoteAuthoringAssistant = Niantic.ARDK.AR.WayspotAnchors.EditModeOnlyBehaviour.RemoteAuthoringAssistant;

namespace Niantic.ARDK.AR.WayspotAnchors.Editor
{
  internal class _VPSLocationAssetProcessor: AssetPostprocessor
  {
    [Serializable]
    private struct WayspotData
    {
      public string NodeIdentifier;
      public string AnchorPayload;
      public string LocalizationTargetName;
    }

    private static void OnPostprocessAllAssets
    (
      string[] importedAssets,
      string[] deletedAssets,
      string[] movedAssets,
      string[] movedFromAssetPaths
    )
    {
      if (importedAssets.Length == 0)
        return;

      var zips = importedAssets.Where(a => string.Equals(Path.GetExtension(a), ".zip")).ToArray();
      if (zips.Length == 0)
        return;

      // Have to delay it a frame in order for all imports to work synchronously
      EditorApplication.delayCall += () => ProcessAllImports(zips);
    }

    private static void ProcessAllImports(string[] zips)
    {
      var allManifests = new List<VPSLocationManifest>();
      foreach (var path in zips)
      {
        var manifest = CreateAssetsIfValid(path);
        if (manifest != null)
          allManifests.Add(manifest);
      }

      if (allManifests.Count == 0)
        return;

      AssetDatabase.SaveAssets();

      var ra = RemoteAuthoringAssistant.FindSceneInstance();
      if (ra == null)
      {
        var create =
          EditorUtility.DisplayDialog
          (
            RemoteAuthoringAssistant.DIALOG_TITLE,
            "No RemoteAuthoringAssistant was found in the open scene. Would you like to create one?",
            "Yes",
            "No"
          );

        if (create)
        {
          _RemoteAuthoringPresenceManager.AddPresence();
          ra = RemoteAuthoringAssistant.FindSceneInstance();
          ra.OpenLocation(allManifests[0]);
          return;
        }
      }
      else
      {
        ra.LoadAllManifestsInProject();
        ra.OpenLocation(allManifests[0]);
      }
    }

    private static VPSLocationManifest CreateAssetsIfValid(string zipPath)
    {
      ARLog._Debug("Importing: " + zipPath);

      UnityEngine.Mesh mesh = null;
      Texture2D tex = null;

      VPSLocationManifest manifest = null;
      try
      {
        var isValidZip =
          FindArchivedFiles
          (
            zipPath,
            out mesh,
            out tex,
            out WayspotData wayspotData
          );

        if (isValidZip)
        {
          var dir = Path.GetDirectoryName(zipPath);

          var locationName = wayspotData.LocalizationTargetName;
          if (string.IsNullOrEmpty(locationName))
            locationName = "Unnamed";

          var manifestPath = _ProjectBrowserUtilities.BuildAssetPath(locationName + ".asset", dir);
          manifest = CreateManifest(wayspotData, manifestPath);

          try
          {
            AssetDatabase.StartAssetEditing();

            // Need to create a copy in order to organize as sub-asset of the manifest
            var meshCopy = UnityEngine.Object.Instantiate(mesh);
            meshCopy.name = "Mesh";
            AssetDatabase.AddObjectToAsset(meshCopy, manifest);

            if (tex != null)
            {
              var texCopy = UnityEngine.Object.Instantiate(tex);
              texCopy.name = "Texture";
              AssetDatabase.AddObjectToAsset(texCopy, manifest);
            }

            // Create the material asset
            var mat = new Material(Shader.Find("Standard"));
            mat.name = "Material";

            AssetDatabase.AddObjectToAsset(mat, manifest);
            Selection.activeObject = manifest;

            // When pinged without delay, project browser window is displayed for a moment
            // before elements are alphabetically sorted, potentially leading to objects moving around.
            EditorApplication.delayCall += () => EditorGUIUtility.PingObject(manifest);
          }
          finally
          {
            AssetDatabase.StopAssetEditing();
          }
        }
      }
      finally
      {
        // Cleanup
        AssetDatabase.DeleteAsset(zipPath);

        if (mesh != null)
          AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(mesh));

        if (tex != null)
          AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(tex));
      }

      return manifest;
    }

    private static bool FindArchivedFiles
    (
      string zipPath,
      out UnityEngine.Mesh mesh,
      out Texture2D tex,
      out WayspotData wayspotData
      )
    {
      mesh = null;
      tex = null;
      wayspotData = new WayspotData();

      using (var file = File.OpenRead(zipPath))
      {
        using (var zip = new ZipArchive(file, ZipArchiveMode.Read))
        {
          var validEntries = zip.Entries.Where(e => !e.Name.StartsWith("._"));
          var meshEntries = validEntries.Where(e => Path.GetExtension(e.Name).Equals(".fbx"));
          var texEntries = validEntries.Where(e => Path.GetExtension(e.Name).Equals(".jpeg"));
          var wayspotEntries = validEntries.Where(e => Path.GetExtension(e.Name).Equals(".json"));

          if (!(meshEntries.Any() && wayspotEntries.Any()))
            return false;

          mesh = ImportMesh(meshEntries.First());
          wayspotData = ParseWayspotData(wayspotEntries.First());

          // Some nodes do not have textures
          if (texEntries.Any())
            tex = ImportTexture(texEntries.First());

          return !(mesh == null || string.IsNullOrEmpty(wayspotData.AnchorPayload));
        }
      }
    }

    private static WayspotData ParseWayspotData(ZipArchiveEntry entry)
    {
      using (var stream = entry.Open())
      {
        using (var reader = new StreamReader(stream))
        {
          var anchorFileText = reader.ReadToEnd();
          var wayspotData = JsonUtility.FromJson<WayspotData>(anchorFileText);

          return wayspotData;
        }
      }
    }

    private static bool _isImportingMesh;
    private static UnityEngine.Mesh ImportMesh(ZipArchiveEntry entry)
    {
      var absPath = _ProjectBrowserUtilities.BuildAssetPath("VPSLocationMesh.fbx", Application.dataPath);
      var assetPath = FileUtil.GetProjectRelativePath(absPath);

      using (var stream = entry.Open())
        using (var fs = new FileStream(assetPath, FileMode.OpenOrCreate))
          stream.CopyTo(fs);

      _isImportingMesh = true;
      AssetDatabase.ImportAsset(assetPath);
      _isImportingMesh = false;

      return AssetDatabase.LoadAssetAtPath<UnityEngine.Mesh>(assetPath);
    }

    private static bool _isImportingTex;
    private static Texture2D ImportTexture(ZipArchiveEntry entry)
    {
      var absPath = _ProjectBrowserUtilities.BuildAssetPath(entry.Name, Application.dataPath);
      var assetPath = FileUtil.GetProjectRelativePath(absPath);

      using (var stream = entry.Open())
      {
        using (var ms = new MemoryStream())
        {
          stream.CopyTo(ms);
          var data = ms.ToArray();
          File.WriteAllBytes(assetPath, data);
        }
      }

      _isImportingTex = true;
      AssetDatabase.ImportAsset(assetPath);
      _isImportingTex = false;

      return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
    }

    private void OnPreprocessTexture()
    {
      if (!_isImportingTex)
        return;

      var textureImporter = assetImporter as TextureImporter;
      textureImporter.isReadable = true; // Unity takes care of resetting this value
    }

    private void OnPreprocessModel()
    {
      if (!_isImportingMesh)
        return;

      var modelImporter = assetImporter as ModelImporter;
      modelImporter.bakeAxisConversion = true;
    }

    private static VPSLocationManifest CreateManifest(WayspotData wayspotData, string assetPath)
    {
      var manifest = ScriptableObject.CreateInstance<VPSLocationManifest>();
      manifest._NodeIdentifier = wayspotData.NodeIdentifier;
      manifest.LocationName = Path.GetFileNameWithoutExtension(assetPath);

      manifest._AddAnchorData
      (
        "Default",
        payload: wayspotData.AnchorPayload,
        position: Vector3.zero,
        rotation: Vector3.zero
      );

      AssetDatabase.CreateAsset(manifest, assetPath);

      return manifest;
    }
  }
}
