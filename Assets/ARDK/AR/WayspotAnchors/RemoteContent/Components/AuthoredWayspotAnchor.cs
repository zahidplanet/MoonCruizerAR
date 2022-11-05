#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;

using UnityEngine;

using Niantic.ARDK.Utilities.Editor;
using Niantic.ARDK.Utilities.Logging;

using UnityEditor.SceneManagement;

namespace Niantic.ARDK.AR.WayspotAnchors
{
  public partial class EditModeOnlyBehaviour
  {
    /// Backing WayspotAnchor is created in Awake.
    /// Once the device is localized (through the WayspotAnchorService or WayspotAnchorController API)
    /// the VPS will attempt to resolve this anchor.
    [ExecuteInEditMode]
    public class AuthoredWayspotAnchor: MonoBehaviour
    {
      // This class maintains a separate serialization of the properties in AuthoredWayspotAnchorData,
      // so that changes made to this class can either be saved back to the VPSLocationManifest or
      // discarded.
      [SerializeField] [HideInInspector]
      private string _anchorManifestIdentifier;

      [SerializeField] [HideInInspector]
      private string _anchorName;

      [SerializeField] [HideInInspector]
      private string _tags;

      [SerializeField] [HideInInspector]
      private AuthoredWayspotAnchorData.PrefabData[] _prefabs;

      internal string _AnchorManifestIdentifier
      {
        get
        {
          return _anchorManifestIdentifier;
        }
      }

      internal string _AnchorName
      {
        get
        {
          return _anchorName;
        }
        set
        {
          _anchorName = value;
        }
      }

      internal string _Tags
      {
        get
        {
          return _tags;
        }
        set
        {
          _tags = value;
        }
      }

      internal AuthoredWayspotAnchorData.PrefabData[] _Prefabs
      {
        get
        {
          return _prefabs;
        }
        private set
        {
          _prefabs = value;
        }
      }

      private Dictionary<string, AuthoredWayspotAnchorData.PrefabData> _prefabsMapping;
      internal Dictionary<string, AuthoredWayspotAnchorData.PrefabData> _SafePrefabsMapping
      {
        get
        {
          if (_prefabsMapping == null)
          {
            _prefabsMapping = new Dictionary<string, AuthoredWayspotAnchorData.PrefabData>();
            foreach (var prefabData in _prefabs)
            {
              _prefabsMapping.Add(prefabData.Identifier, prefabData);
            }
          }

          return _prefabsMapping;
        }
      }

      internal void AddPrefab(AuthoredWayspotAnchorData.PrefabData prefabData)
      {
        var oldPrefabs = _prefabs;
        var numOldPrefabs = oldPrefabs.Length;
        var newPrefabs = new AuthoredWayspotAnchorData.PrefabData[numOldPrefabs + 1];

        if (numOldPrefabs > 0)
          Array.Copy(oldPrefabs, newPrefabs, numOldPrefabs);

        newPrefabs[numOldPrefabs] = prefabData;

        // Populate the mapping before the array, because the mapping may reconstruct from the array
        // and already contain the new prefabData
        _SafePrefabsMapping.Add(prefabData.Identifier, prefabData);
        _prefabs = newPrefabs;
      }

      internal void RemovePrefab(int index)
      {
        if (index > _prefabs.Length - 1)
          throw new IndexOutOfRangeException();

        var newPrefabs = new List<AuthoredWayspotAnchorData.PrefabData>(_prefabs);
        var toBeRemoved = _prefabs[index];
        _SafePrefabsMapping.Remove(toBeRemoved.Identifier);

        newPrefabs.RemoveAt(index);
        _prefabs = newPrefabs.ToArray();
      }


      private void Reset()
      {
        _SceneHierarchyUtilities.ValidateChildOf<RemoteAuthoringAssistant>(this, true);
      }

      internal static GameObject _Create(AuthoredWayspotAnchorData data)
      {
        var go = new GameObject();
        go.transform.SetParent(RemoteAuthoringAssistant.FindSceneInstance().transform);
        go.transform.SetAsLastSibling();

        var anchor = go.AddComponent<AuthoredWayspotAnchor>();
        anchor._ResetToData(data);

        EditorSceneManager.MarkSceneDirty(go.scene);

        return go;
      }

      private bool _isDestroying;
      internal void Destroy()
      {
        _isDestroying = true;
        DestroyImmediate(gameObject);
      }

      private void OnDestroy()
      {
        var ra = RemoteAuthoringAssistant.FindSceneInstance();
        if (!ra)
        {
          // If destroyed because parent RemoteAuthoringAssistant was destroyed
          // do nothing
          return;
        }

        // If destroyed by right-clicking and selecting Delete in hierarchy
        if (!_isDestroying)
        {
          var hasData =
            ra.ActiveManifest._GetAnchorData
            (
              _AnchorManifestIdentifier,
              out AuthoredWayspotAnchorData data
            );

          if (hasData)
          {
            ARLog._WarnRelease
            (
              "AuthoredWayspotAnchor was deleted through the hierarchy, so it was not deleted " +
              "from its VPSLocationManifest. When the location is reloaded, the anchor will " +
              "reappear in the hierarchy. To delete an anchor, inspect the AuthoredWayspotAnchor " +
              "component and press the 'Delete Anchor' button."
            );
          }

          ra.RefreshActiveAnchors();
        }

        _isDestroying = false;
      }

      private const string ANCHOR_NAME_SUFFIX = " (Authored Anchor)";

      internal void _ResetToData(AuthoredWayspotAnchorData data)
      {
        if (data == null)
        {
          _AnchorName = "UnnamedAnchor" + ANCHOR_NAME_SUFFIX;
          _Tags = String.Empty;
          _Prefabs = new AuthoredWayspotAnchorData.PrefabData[0];
          return;
        }

        _anchorManifestIdentifier = data._ManifestIdentifier;

        _AnchorName = data.Name;
        _Tags = data.Tags;

        if (data.AssociatedPrefabs != null)
        {
          _Prefabs = new AuthoredWayspotAnchorData.PrefabData[data.AssociatedPrefabs.Count];
          _Prefabs = data.AssociatedPrefabs.Select(p => p.Copy()).ToArray();
          _prefabsMapping = null;

          var prefabReporters = _SceneHierarchyUtilities.FindComponents<_VisualizedPrefabTag>(null, transform);
          foreach (var reporter in prefabReporters)
          {
            var updatedData = _Prefabs.FirstOrDefault(p => string.Equals(p.Identifier, reporter.PrefabIdentifier));
            if (updatedData == null)
            {
              throw new Exception
              (
                "Mismatch found between serialized and live representations of anchor data. " +
                "Reload this location in order to fix."
              );
            }

            reporter.ResetToData(updatedData);
          }
        }

        gameObject.name = _anchorName + ANCHOR_NAME_SUFFIX;
        transform.SetPositionAndRotation(data.Position, Quaternion.Euler(data.Rotation));
        transform.localScale = data.Scale;
      }

      internal void GetDifferences(AuthoredWayspotAnchorData data, out bool isBackingAnchorInvalid, out bool isManifestInvalid)
      {
        if (data == null)
        {
          isBackingAnchorInvalid = true;
          isManifestInvalid = true;
          return;
        }

        isManifestInvalid =
          !string.Equals(_AnchorManifestIdentifier, data._ManifestIdentifier) ||
          !string.Equals(_AnchorName, data.Name) ||
          transform.localScale != data.Scale ||
          !string.Equals(_Tags, data.Tags);

        if (!isManifestInvalid)
        {
          var serializedPrefabs = data.AssociatedPrefabs;
          isManifestInvalid = serializedPrefabs.Count != _Prefabs.Length;
          if (!isManifestInvalid)
          {
            for (var i = 0; i < serializedPrefabs.Count; i++)
            {
              if (serializedPrefabs[i].ValuesDifferFrom(_Prefabs[i]))
              {
                isManifestInvalid = true;
                break;
              }
            }
          }
        }

        isBackingAnchorInvalid =
          string.IsNullOrEmpty(data.Payload) ||
          transform.position != data.Position ||
          transform.rotation.eulerAngles != data.Rotation;
      }

      private void OnDrawGizmos()
      {
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(transform.position, .1f);
      }
    }
  }
}
#endif
