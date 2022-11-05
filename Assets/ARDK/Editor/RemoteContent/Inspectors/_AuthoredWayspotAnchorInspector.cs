using System;
using System.Collections.Generic;
using System.Linq;

using Niantic.ARDK.Editor;
using Niantic.ARDK.Utilities.Logging;

using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

using RemoteAuthoringAssistant = Niantic.ARDK.AR.WayspotAnchors.EditModeOnlyBehaviour.RemoteAuthoringAssistant;
using AuthoredWayspotAnchor = Niantic.ARDK.AR.WayspotAnchors.EditModeOnlyBehaviour.AuthoredWayspotAnchor;

namespace Niantic.ARDK.AR.WayspotAnchors.Editor
{
  // Code for AuthoredWayspotAnchor Tags commented out until feature is more thought out
  [CustomEditor(typeof(AuthoredWayspotAnchor))]
  internal class _AuthoredWayspotAnchorInspector: UnityEditor.Editor
  {
    private AuthoredWayspotAnchor Target { get { return (AuthoredWayspotAnchor)target; } }

    private float _fullWidth;
    private float _thirdWidth;
    private float _colOneWidth;
    private float _colTwoWidth;

    private void RecalculateWidths()
    {
      _fullWidth = GUILayoutUtility.GetLastRect().width;
      _thirdWidth = _fullWidth * 0.33f;
      _colOneWidth = _fullWidth * 0.25f;
      _colTwoWidth = _fullWidth - _colOneWidth;
    }

    private RemoteAuthoringAssistant _raAssistant;

    private RemoteAuthoringAssistant SafeRemoteAuthoringAssistant
    {
      get
      {
        if (_raAssistant == null)
          _raAssistant = RemoteAuthoringAssistant.FindSceneInstance();

        return _raAssistant;
      }
    }

    private bool _showPrefabs;

    private Dictionary<AuthoredWayspotAnchorData.PrefabData, bool> _prefabFoldoutStates;

    private Dictionary<AuthoredWayspotAnchorData.PrefabData, bool> SafePrefabFoldoutStates
    {
      get
      {
        if (!ArePrefabDictionariesValid())
          RebuildPrefabDictionaries();

        return _prefabFoldoutStates;
      }
    }

    private Dictionary<AuthoredWayspotAnchorData.PrefabData, GameObject> _prefabAssets;

    private Dictionary<AuthoredWayspotAnchorData.PrefabData, GameObject> SafePrefabAssets
    {
      get
      {
        if (!ArePrefabDictionariesValid())
          RebuildPrefabDictionaries();

        return _prefabAssets;
      }
    }

    private _AnchorPrefabVisualizer _prefabVisualizer;

    private _AnchorPrefabVisualizer SafePrefabVisualizer
    {
      get
      {
        if (_prefabVisualizer == null)
          _prefabVisualizer = _RemoteAuthoringPresenceManager.GetVisualizer<_AnchorPrefabVisualizer>();

        return _prefabVisualizer;
      }
    }

    [SerializeField]
    private string _cachedWayspotIdentifier;

    private bool ArePrefabDictionariesValid()
    {
      // If one of these dictionaries is null, they'll all be null, because they're null due to the
      // scripts being reloaded or this being a new instance of the Inspector.
      return _prefabAssets != null && string.Equals(_cachedWayspotIdentifier, Target._AnchorManifestIdentifier);
    }

    private void RebuildPrefabDictionaries()
    {
      _cachedWayspotIdentifier = Target._AnchorManifestIdentifier;

      _prefabFoldoutStates = new Dictionary<AuthoredWayspotAnchorData.PrefabData, bool>();
      _prefabAssets = new Dictionary<AuthoredWayspotAnchorData.PrefabData, GameObject>();

      if (Target._Prefabs != null)
      {
        foreach (var prefabData in Target._Prefabs)
        {
          _prefabFoldoutStates.Add(prefabData, false);
          _prefabAssets.Add(prefabData, prefabData.Asset);
        }
      }
    }

    public override void OnInspectorGUI()
    {
      base.OnInspectorGUI();

      // Invisible label so width can be fetched
      GUILayout.Label("");

      if (Event.current.type == EventType.Repaint)
        RecalculateWidths();

      var isSerialized =
        SafeRemoteAuthoringAssistant.ActiveManifest._GetAnchorData
        (
          Target._AnchorManifestIdentifier,
          out AuthoredWayspotAnchorData serializedAnchor
        );

      Target.GetDifferences(serializedAnchor, out bool isBackingAnchorInvalid, out bool isManifestInvalid);

      var transform = Target.transform;
      var currPos = transform.position;
      var currRot = transform.rotation;
      var currScale = transform.localScale;

      bool isAnchorNameDirty = !isSerialized;
      bool isAnchorPositionDirty = !isSerialized;
      bool isAnchorRotationDirty = !isSerialized;
      bool isAnchorScaleDirty = !isSerialized;
      bool arePrefabsDirty = !isSerialized;

      if (isSerialized)
      {
        isAnchorNameDirty = !string.Equals(serializedAnchor.Name, Target._AnchorName);
        //areAnchorTagsDirty = !string.Equals(serializedAnchor.Tags, Target._Tags);

        isAnchorPositionDirty = currPos != serializedAnchor.Position;
        isAnchorRotationDirty = currRot.eulerAngles != serializedAnchor.Rotation;
        isAnchorScaleDirty = currScale != serializedAnchor.Scale;

        var serializedPrefabs = serializedAnchor.AssociatedPrefabs;
        var livePrefabs = Target._Prefabs;
        arePrefabsDirty = serializedPrefabs.Count != livePrefabs.Length;
        if (!arePrefabsDirty)
        {
          for (var i = 0; i < serializedPrefabs.Count; i++)
          {
            if (serializedPrefabs[i].ValuesDifferFrom(livePrefabs[i]))
            {
              arePrefabsDirty = true;
              break;
            }
          }
        }
      }

      using (var scope = new GUILayout.HorizontalScope())
      {
        GUILayout.Label
        (
          "Anchor Name",
          isAnchorNameDirty ? CommonStyles.BoldLabelStyle : EditorStyles.label,
          GUILayout.Width(_colOneWidth)
        );

        Target._AnchorName =
          GUILayout.TextField
          (
            Target._AnchorName,
            isAnchorNameDirty ? CommonStyles.BoldTextFieldStyle : EditorStyles.textField,
            GUILayout.Width(_colTwoWidth)
          );
      }

      // using (var scope = new GUILayout.HorizontalScope())
      // {
      //   GUILayout.Label
      //   (
      //     "Anchor Tags",
      //     areAnchorTagsDirty ? CommonStyles.BoldLabelStyle : EditorStyles.label,
      //     GUILayout.Width(_colOneWidth)
      //   );
      //
      //   Target._Tags =
      //     GUILayout.TextField
      //     (
      //       Target._Tags,
      //       areAnchorTagsDirty ? CommonStyles.BoldTextFieldStyle : EditorStyles.textField,
      //       GUILayout.Width(_colTwoWidth)
      //     );
      // }

      GUILayout.Space(10);

      using (var scope = new GUILayout.HorizontalScope())
      {
        var style = isAnchorPositionDirty ? CommonStyles.BoldLabelStyle : EditorStyles.label;

        GUILayout.Label("Relative Position", style, GUILayout.Width(_colOneWidth));
        GUILayout.Label(currPos.ToString("F2"), style);
      }

      using (var scope = new GUILayout.HorizontalScope())
      {
        var style = isAnchorRotationDirty ? CommonStyles.BoldLabelStyle : EditorStyles.label;

        GUILayout.Label("Relative Rotation", style, GUILayout.Width(_colOneWidth));
        GUILayout.Label(currRot.eulerAngles.ToString("F2"), style);
      }

      using (var scope = new GUILayout.HorizontalScope())
      {
        var style = isAnchorScaleDirty ? CommonStyles.BoldLabelStyle : EditorStyles.label;

        GUILayout.Label("Prefabs Scale", style, GUILayout.Width(_colOneWidth));
        GUILayout.Label(currScale.ToString("F2"), style);
      }

      GUILayout.Space(10);
      using (var scope = new GUILayout.HorizontalScope())
      {
        GUILayout.Label
        (
          "Payload",
          GUILayout.Width(_colOneWidth)
        );

        if (!isBackingAnchorInvalid)
          DrawAnchorPayloadGUI(serializedAnchor.Payload);
        else
          GUILayout.Label("Invalid");

      }

      GUILayout.Space(10);

      DrawPrefabsArrayGUI(arePrefabsDirty);

      GUILayout.Space(30);

      using (var scope = new GUILayout.HorizontalScope())
      {
        if (isBackingAnchorInvalid || isManifestInvalid)
        {

          if (GUILayout.Button("Save", GUILayout.Width(_thirdWidth)))
            RemoteAuthoringAssistant.FindSceneInstance().UpdateAnchor(Target, isBackingAnchorInvalid);

          if (GUILayout.Button("Discard Changes", GUILayout.Width(_thirdWidth)))
            Target._ResetToData(serializedAnchor);
        }

        DrawDeleteAnchorGUI();
      }
    }

    private float _timeout;

    private void DrawAnchorPayloadGUI(string payload)
    {
      GUILayout.BeginVertical();
      var payloadHint = payload.Substring(0, 20) + "...";
      if (GUILayout.Button(payloadHint, _VPSLocationManifestInspector.PayloadStyle))
      {
        GUIUtility.systemCopyBuffer = payload;
        _timeout = Time.realtimeSinceStartup + 1;
      }

      if (Time.realtimeSinceStartup < _timeout)
        GUILayout.Label("Copied!", CommonStyles.CenteredLabelStyle);
      else
        GUILayout.Label("Click to copy", CommonStyles.CenteredLabelStyle);

      GUILayout.EndVertical();
    }

    private void AddEmptyPrefab()
    {
      var newPrefab = new AuthoredWayspotAnchorData.PrefabData(null);
      SafePrefabFoldoutStates.Add(newPrefab, true);
      SafePrefabAssets.Add(newPrefab, null);
      Target.AddPrefab(newPrefab);
      _showPrefabs = true;

      UnselectPrefab();
    }

    private void RemovePrefabAtIndex(int index)
    {
      var prefabData = Target._Prefabs[index];
      SafePrefabAssets.Remove(prefabData);
      SafePrefabFoldoutStates.Remove(prefabData);
      Target.RemovePrefab(index);

      SafePrefabVisualizer.RemovePrefab(Target, prefabData.Identifier);

      UnselectPrefab();
    }

    private int _selectedPrefabIndex = -1;

    private void UnselectPrefab()
    {
      _selectedPrefabIndex = -1;
    }

    private void DrawPrefabsArrayGUI(bool arePrefabsDirty)
    {
      var prefabsCount = Target._Prefabs.Length;
      using (var scope = new GUILayout.HorizontalScope())
      {
        _showPrefabs =
          EditorGUILayout.Foldout
          (
            _showPrefabs,
            "Associated Prefabs",
            arePrefabsDirty ? CommonStyles.BoldFoldoutStyle : EditorStyles.foldout
          );

        GUILayout.FlexibleSpace();
        var newPrefabsCount = EditorGUILayout.DelayedIntField(prefabsCount, GUILayout.MaxWidth(40));
        if (newPrefabsCount >= 0 && newPrefabsCount != prefabsCount)
        {
          if (prefabsCount < newPrefabsCount) // added
          {
            for (var i = prefabsCount; i < newPrefabsCount; i++)
              AddEmptyPrefab();
          }
          else
          {
            while (Target._Prefabs.Length != newPrefabsCount)
              RemovePrefabAtIndex(Target._Prefabs.Length - 1);
          }
        }
      }

      if (_showPrefabs)
      {
        EditorGUI.indentLevel++;

        var clickedThisFrame = false;
        var selectedThisFrame = false;
        for (var i = 0; i < Target._Prefabs.Length; i++)
        {
          var rect = DrawPrefabGUI(Target._Prefabs[i], i == _selectedPrefabIndex);

          if (Event.current.type == EventType.MouseDown)
          {
            clickedThisFrame = true;
            if (rect.Contains(Event.current.mousePosition))
            {
              _selectedPrefabIndex = i;
              selectedThisFrame = true;
            }
          }
        }

        if (clickedThisFrame && !selectedThisFrame)
          UnselectPrefab();

        EditorGUI.indentLevel--;
      }
      else
      {
        UnselectPrefab();
      }


      GUILayout.Space(5);
      using (var scope = new GUILayout.HorizontalScope())
      {
        GUILayout.FlexibleSpace();

        if (GUILayout.Button("-", PrefabButtonStyle, GUILayout.MaxWidth(40)))
        {
          RemovePrefabAtIndex(_selectedPrefabIndex >= 0 ? _selectedPrefabIndex : Target._Prefabs.Length - 1);
        }

        if (GUILayout.Button("+", PrefabButtonStyle, GUILayout.MaxWidth(40)))
          AddEmptyPrefab();
      }
    }

    private Rect DrawPrefabGUI(AuthoredWayspotAnchorData.PrefabData prefabData, bool isSelected)
    {
      var currColor = GUI.backgroundColor;

      var prefabBox = new GUIStyle(GUI.skin.box);
      if (isSelected)
      {
        GUI.backgroundColor = _HierarchyColors.ObjectSelectedWindowFocusedBackground;
        prefabBox.normal.background = Texture2D.whiteTexture;
      }

      using (var scope = new EditorGUILayout.VerticalScope(prefabBox))
      {
        GUI.backgroundColor = currColor;
        var asset = SafePrefabAssets[prefabData];
        var assetName = asset != null ? asset.name : "None";

        SafePrefabFoldoutStates[prefabData] =
          EditorGUILayout.Foldout
          (
            SafePrefabFoldoutStates[prefabData],
            assetName
          );

        if (SafePrefabFoldoutStates[prefabData])
        {
          EditorGUI.indentLevel++;

          GUI.backgroundColor = currColor;
          DrawPrefabAssetGUI(prefabData, asset);

          EditorGUI.indentLevel--;
        }

        return scope.rect;
      }
    }

    private void DrawPrefabAssetGUI(AuthoredWayspotAnchorData.PrefabData prefabData, GameObject oldAsset)
    {
      var currAsset = EditorGUILayout.ObjectField("Asset", oldAsset, typeof(GameObject), false) as GameObject;

      if (currAsset == oldAsset)
        return;

      if (oldAsset != null)
        SafePrefabVisualizer.RemovePrefab(Target, prefabData.Identifier);

      prefabData.Reset();
      if (currAsset != null)
      {
        prefabData.Asset = currAsset;
        SafePrefabAssets[prefabData] = currAsset;
        SafePrefabVisualizer.AddPrefab(Target, prefabData);
      }
    }

    private void DrawDeleteAnchorGUI()
    {
      using (var scope = new GUILayout.HorizontalScope())
      {
        GUILayout.FlexibleSpace();

        if (GUILayout.Button("Delete Anchor"))
        {
          var verified =
            EditorUtility.DisplayDialog
            (
              RemoteAuthoringAssistant.DIALOG_TITLE,
              "Are you sure you want to delete this anchor?",
              "Yes",
              "Cancel"
            );

          if (verified)
            RemoteAuthoringAssistant.FindSceneInstance().RemoveAnchor(Target);
        }
      }
    }

    private static GUIStyle _prefabButtonStyle;

    public static GUIStyle PrefabButtonStyle
    {
      get
      {
        if (_prefabButtonStyle == null)
        {
          _prefabButtonStyle = new GUIStyle(GUI.skin.button);

          //_prefabButtonStyle.active.background = _prefabButtonStyle.normal.background;
          _prefabButtonStyle.margin = new RectOffset(0, 0, 0, 0);
        }

        return _prefabButtonStyle;
      }
    }
  }
}
