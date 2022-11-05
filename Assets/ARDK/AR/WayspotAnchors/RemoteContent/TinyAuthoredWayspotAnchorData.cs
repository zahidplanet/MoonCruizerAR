using System;
using System.Linq;
using Niantic.ARDK.Utilities.Editor;
using Niantic.ARDK.Utilities.Logging;
using UnityEngine;

namespace Niantic.ARDK.AR.WayspotAnchors
{
  [Serializable]
  public class TinyAuthoredWayspotAnchorData
  {
    public string Name;
    public string Payload;

#if UNITY_EDITOR
    public TinyAuthoredWayspotAnchorData(AuthoredWayspotAnchorData data)
    {
      Name = data.Name;
      Payload = data.Payload;
    }

    public GameObject GetAssociatedEditorPrefab(string manifestName)
    {
      //find via manifest name
      VPSLocationManifest[] manifests = _AssetDatabaseUtilities.FindAssets<VPSLocationManifest>();
      foreach (var vpsLocationManifest in manifests)
      {
        if (vpsLocationManifest.LocationName == manifestName)
        {
          AuthoredWayspotAnchorData[] authoredWayspotAnchorDatas = vpsLocationManifest.AuthoredAnchorsData.ToArray();
          foreach (var wayspotAnchorData in authoredWayspotAnchorDatas)
          {
            if (wayspotAnchorData.Name == Name && wayspotAnchorData.AssociatedPrefabs.Count > 0)
            {
              if (wayspotAnchorData.AssociatedPrefabs.Count > 1)
              {
                ARLog._WarnRelease("Multiple AssociatedPrefabs found for '" + Name
                                    + "'. Only the first prefab will be used.");
              }

              //get the anchor name that matches
              //return the gameobject 
              return wayspotAnchorData.AssociatedPrefabs[0].Asset;
            }
          }
        }
      }
      ARLog._WarnRelease("AssociatedPrefab data was not found for "+Name);
      return null;
    }
#endif
  }
}
