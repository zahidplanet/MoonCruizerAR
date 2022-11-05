using System;
using System.Linq;
using Niantic.ARDK.Utilities.Editor;
using UnityEditor;
using UnityEngine;

namespace Niantic.ARDK.AR.WayspotAnchors
{
  [Serializable]

  public sealed class TinyVPSLocationManifest
  {
    public string LocationName;
    public TinyAuthoredWayspotAnchorData[] AuthoredAnchors;

#if UNITY_EDITOR
    public TinyVPSLocationManifest(VPSLocationManifest manifest)
    {
      LocationName = manifest.LocationName;
      //AuthoredAnchors = manifest.AuthoredAnchorsData.ToArray();
      AuthoredAnchors = manifest.AuthoredAnchorsData.Select(a => new TinyAuthoredWayspotAnchorData(a)).ToArray();
   }
#endif

    public string ToJson()
    {
      return JsonUtility.ToJson(this);
    }

    public override string ToString()
    {
      return ToJson();
    }
  }
  
}
