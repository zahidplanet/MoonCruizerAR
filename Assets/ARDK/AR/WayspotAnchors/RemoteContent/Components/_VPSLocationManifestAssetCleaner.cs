#if UNITY_EDITOR
using System.IO;
using System.Linq;

using UnityEditor;

namespace Niantic.ARDK.AR.WayspotAnchors
{
  internal class _VPSLocationManifestAssetCleaner: UnityEditor.AssetModificationProcessor
  {
    // This is called by Unity when it is about to delete an asset from disk.
    // It allows you to delete the asset yourself.
    // Deletion of a file can be prevented by returning AssetDeleteResult.FailedDelete.
    // You should not call any Unity AssetDatabase api from within this callback,
    // preferably keep to file operations or VCS apis.
    private static AssetDeleteResult OnWillDeleteAsset(string assetPath, RemoveAssetOptions options)
    {
      if (!string.Equals(Path.GetExtension(assetPath), ".asset"))
        return AssetDeleteResult.DidNotDelete;

      var assetName = Path.GetFileNameWithoutExtension(assetPath);
      var ra = EditModeOnlyBehaviour.RemoteAuthoringAssistant.FindSceneInstance();
      var deletedManifests = ra.AllManifests.Where(m => m.LocationName.Equals(assetName));

      if (deletedManifests.Any())
      {
        if (ra.ActiveManifest != null)
        {
          if (deletedManifests.Any(m => ra.ActiveManifest.LocationName.Equals(assetName)))
            ra.OpenLocation(null, false);
        }

        EditorApplication.delayCall += ra.LoadAllManifestsInProject;
      }

      return AssetDeleteResult.DidNotDelete;
    }
  }
}
#endif
