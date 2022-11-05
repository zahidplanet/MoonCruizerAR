// Copyright 2022 Niantic, Inc. All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using Niantic.ARDK.AR.WayspotAnchors;
using Niantic.ARDK.Extensions;
using UnityEngine;

namespace Niantic.ARDKExamples.RemoteAuthoring
{
  public class LocationManifestManager : MonoBehaviour
  {
    public event StatusLogChanged StatusLogChangeEvent;
    
    [SerializeField, HideInInspector] private TinyVPSLocationManifest[] _manifests;

    private static LocationManifestManager _instance;
    public static LocationManifestManager Instance => _instance;

    public WayspotManagerPOCO _wayspotManager;

    public TinyVPSLocationManifest[] Manifests
    {
      get => _manifests;
      set => _manifests = value;
    }

    [SerializeField] private AnchoredContent[] _anchoredContent;

    private readonly HashSet<AnchorStatusTracker> _anchorStatusTrackers = new HashSet<AnchorStatusTracker>();
    
    private int _resolvedCount = 0;

    private void Awake()
    {
      _instance = this;
      _wayspotManager = new WayspotManagerPOCO();
      AddWayspotManagerStatusListener(WayspotManagerOnStatusLogChangeEvent);

      AddLocalizationStatusListener(OnLocalized);
    }

    private void Update()
    {
      UpdateResolvedStatusLog(false);
    }

    private void OnDestroy()
    {
      _wayspotManager.ShutDown();
    }

    private void WayspotManagerOnStatusLogChangeEvent(string statusmessage)
    {
      StatusLogChangeEvent?.Invoke(statusmessage);
    }

    /// Loads all of the saved wayspot anchors
    public void LoadWayspotAnchors(int locationID)
    {
      ClearAnchorGameObjects();
      _wayspotManager.RestartWayspotAnchorService();

      //get content from anchor content list
      AnchoredContent[] filteredContent = GetFilteredAnchorContentFromLocation(locationID);
      if (filteredContent.Length > 0)
      {
        foreach (var anchoredContent in filteredContent)
        {
          var payload = GetPayloadFromAnchorData(locationID, anchoredContent);
          if (!_wayspotManager.RestoreAnchorsWithPayload(out var anchors, payload))
          {
            continue;
          }
          
          var prefab = GetGameObjectFromAnchorData(anchoredContent);
          if (prefab != null)
            CreateWayspotAnchorGameObject(anchors[0], prefab, Vector3.zero, Quaternion.identity);
        }

        StatusLogChangeEvent?.Invoke($"Loaded {_anchorStatusTrackers.Count} anchors.");
      }
      else
      {
        StatusLogChangeEvent?.Invoke("No anchors to load.");
      }
    }
    
    public AnchoredContent[] GetFilteredAnchorContentFromLocation(int locationID)
    {
      List<AnchoredContent> anchorContentList = new List<AnchoredContent>();
      foreach (var anchorContent in _anchoredContent)
      {
        if (anchorContent.ManifestID == locationID)
          anchorContentList.Add(anchorContent);
      }

      return anchorContentList.ToArray();
    }

    public WayspotAnchorPayload GetPayloadFromAnchorData(int locationID, AnchoredContent anchoredContent)
    {
      if (!string.IsNullOrEmpty(anchoredContent.AnchorDataIdentifier))
      {
        var authoredAnchorData = FindAnchorDataFromName(_manifests[locationID], anchoredContent.AnchorDataIdentifier);
        if (authoredAnchorData != null)
        {
          return WayspotAnchorPayload.Deserialize(authoredAnchorData.Payload);
        }
      }

      Debug.LogError("Error retrieving anchored Content payload");
      return null;
    }
    
    public TinyAuthoredWayspotAnchorData FindAnchorDataFromName(TinyVPSLocationManifest manifest, string name)
    {
      foreach (var authoredWayspotAnchorData in manifest.AuthoredAnchors)
      {
        if (authoredWayspotAnchorData.Name == name)
        {
          return authoredWayspotAnchorData;
        }
      }
      Debug.LogError("Error: This ID does not exist in this location Manifest anymore!");
      return null;
    }

    public GameObject GetGameObjectFromAnchorData(AnchoredContent anchoredContent)
    {
      if (!string.IsNullOrEmpty(anchoredContent.AnchorDataIdentifier) && anchoredContent.Content != null)
        return anchoredContent.Content;

      Debug.LogError("Error retrieving anchored Content gameobject");
      return null;
    }

    private void RemoveAllContent()
    {
      _manifests = Array.Empty<TinyVPSLocationManifest>();
      _anchoredContent = Array.Empty<AnchoredContent>();
    }


    /// Clears all of the active wayspot anchors
    public void ClearAnchorGameObjects()
    {
      if (_anchorStatusTrackers.Count == 0)
      {
        StatusLogChangeEvent?.Invoke("No anchors to clear.");
        return;
      }

      foreach (var anchor in _anchorStatusTrackers)
        Destroy(anchor.gameObject);

      var wayspotAnchors = _anchorStatusTrackers.Select(go => go.WayspotAnchor).ToArray();
      _wayspotManager.DestroyAnchors(wayspotAnchors);

      _anchorStatusTrackers.Clear();
      StatusLogChangeEvent?.Invoke("Cleared Wayspot Anchors.");
    }

    private void OnLocalized(string state)
    {
      if (state == LocalizationState.Localized.ToString())
      {
        // Start showing the resolved status once we are localized.
        UpdateResolvedStatusLog(true);
      }
    }

    private void UpdateResolvedStatusLog(bool forceRefresh)
    {
      // Updat the status message for the tracked anchors.
      if (_anchorStatusTrackers.Count > 0)
      {
        int resolvedCount = 0;
        foreach (var wayspotAnchorTracker in _anchorStatusTrackers)
        {
          if (wayspotAnchorTracker.AnchorIsResolved)
          {
            ++resolvedCount;
          }
        }

        if (forceRefresh || resolvedCount != _resolvedCount)
        {
          _resolvedCount = resolvedCount;
          StatusLogChangeEvent?.Invoke(
            $"Resolved {_resolvedCount} of {_anchorStatusTrackers.Count} anchors.");
        }
      }
    }
    
    private GameObject CreateWayspotAnchorGameObject
    (
      IWayspotAnchor anchor,
      GameObject anchorPrefab,
      Vector3 position,
      Quaternion rotation
    )
    {
      var go = Instantiate(anchorPrefab, position, rotation);

      var tracker = go.GetComponent<AnchorStatusTracker>();
      if (tracker == null)
      {
        Debug.Log("Anchor prefab was missing WayspotAnchorTracker, so one will be added.");
        tracker = go.AddComponent<AnchorStatusTracker>();
      }

      tracker.AttachAnchor(anchor);
      _anchorStatusTrackers.Add(tracker);

      return go;
    }

    public string[] GetLocationNames()
    {
      string[] locationNames = new string[_manifests.Length];
      for (int i = 0; i < _manifests.Length; i++)
      {
        locationNames[i] = _manifests[i].LocationName;
      }

      return locationNames;
    }

    public void AddWayspotManagerStatusListener(StatusLogChanged listener)
    {
      _wayspotManager.StatusLogChangeEvent += listener;
    }
    public void AddLocalizationStatusListener(LocalizationStatusChanged listener)
    {
      _wayspotManager.LocalizationStatusChangeEvent += listener;
    }
#if UNITY_EDITOR
    public TinyVPSLocationManifest[] SyncManifests(TinyVPSLocationManifest[] manifests)
    {
      var updatedManifests = manifests.ToList();
      
      //find a copy of the real Manifest locally
      //if it exists, replace it!
      var guids = UnityEditor.AssetDatabase.FindAssets("t:VPSLocationManifest");

      for (int i = 0; i < guids.Length; i++)
      {
        var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[i]);
        var manifest = UnityEditor.AssetDatabase.LoadMainAssetAtPath(path) as VPSLocationManifest;

        if (manifest)
        {
          var index = updatedManifests.FindIndex(p => p.LocationName == manifest.LocationName);
          if (index >= 0)
          {
            updatedManifests[index] = new TinyVPSLocationManifest(manifest);
          } 
        }
      }

      return updatedManifests.ToArray();
    }
    public void PopulateAnchoredContent(bool withVisuals)
    {
      List<AnchoredContent> anchoredContents = new List<AnchoredContent>();
      _manifests = SyncManifests(_manifests);
      int locID = 0;
      for (int i = 0; i < _manifests.Length; i++)
      {
        var vpsLocationManifest = _manifests[i];
        foreach (var wayspotAnchorData in vpsLocationManifest.AuthoredAnchors)
        {
          GameObject obj = null;
          if (withVisuals)
          {
            //you can only read from associated prefabs in editor
            //if trying to do this in a build, you'll likely want to create your own lookup dictionary 
            //using anchor identifier as a reference
            obj = wayspotAnchorData.GetAssociatedEditorPrefab(vpsLocationManifest.LocationName);
          }

          var newContent = new AnchoredContent(wayspotAnchorData.Name + "-" + vpsLocationManifest.LocationName, locID,
            wayspotAnchorData.Name, obj);
          anchoredContents.Add(newContent);
        }

        locID++;
      }

      _anchoredContent = anchoredContents.ToArray();
    }
#endif
  }
}