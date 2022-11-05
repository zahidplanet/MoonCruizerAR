using System;
using Niantic.ARDK.AR.WayspotAnchors;
using Unity.Collections;
using UnityEngine;

namespace Niantic.ARDKExamples.RemoteAuthoring
{
    [Serializable]
    public struct AnchoredContent
    {
        [DisplayWithoutEdit]
        public string AnchorName;
        [HideInInspector]
        public int ManifestID;
        [DisplayWithoutEdit]
        public string AnchorDataIdentifier;
        public GameObject Content;

        public AnchoredContent(string aName, int id, string anchorDataID, GameObject instancePrefab)
        {
            AnchorName = aName;
            ManifestID = id;
            AnchorDataIdentifier = anchorDataID;
            Content = instancePrefab;
        }
    }
}
