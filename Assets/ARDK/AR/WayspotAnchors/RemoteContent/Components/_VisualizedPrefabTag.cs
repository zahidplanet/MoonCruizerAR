#if UNITY_EDITOR
using System;
using UnityEngine;

using PrefabData = Niantic.ARDK.AR.WayspotAnchors.AuthoredWayspotAnchorData.PrefabData;

namespace Niantic.ARDK.AR.WayspotAnchors
{
  public partial class EditModeOnlyBehaviour
  {
    [ExecuteInEditMode]
    internal class _VisualizedPrefabTag: MonoBehaviour
    {
      [SerializeField]
      private string _prefabIdentifier;

      [SerializeField]
      private AuthoredWayspotAnchor _owner;

      public string PrefabIdentifier { get => _prefabIdentifier; }

      public void SetOwner(AuthoredWayspotAnchor anchor, PrefabData prefabData)
      {
        _owner = anchor;
        _prefabIdentifier = prefabData.Identifier;

        ResetToData(prefabData);
      }

      public void ResetToData(PrefabData prefabData)
      {
        if (_prefabIdentifier != prefabData.Identifier)
          throw new InvalidOperationException("Cannot reset _PrefabDataReporter to a _PrefabData with a different identifier.");

        gameObject.SetActive(prefabData.IsVisible);
      }

      private PrefabData BackingPrefabData
      {
        get
        {
          if (_owner == null)
            return null;

          if (_owner._SafePrefabsMapping.TryGetValue(_prefabIdentifier, out PrefabData data))
            return data;

          return null;
        }
      }

      private void Reset()
      {
        hideFlags = HideFlags.HideInInspector;
      }

      private void OnEnable()
      {
        if (BackingPrefabData != null)
          BackingPrefabData.IsVisible = true;
      }

      private void OnDisable()
      {
        // Triggered by actually unchecking the GameObject, not by destroying
        if (gameObject.scene.isLoaded)
        {
          var backingData = BackingPrefabData;
          if (backingData != null)
            backingData.IsVisible = false;
        }
      }
    }
  }
}
#endif