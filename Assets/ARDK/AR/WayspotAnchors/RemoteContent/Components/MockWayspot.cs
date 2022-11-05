#if UNITY_EDITOR
using System.Collections.Generic;

using Niantic.ARDK.AR.WayspotAnchors;
using Niantic.ARDK.Utilities.Editor;
using Niantic.ARDK.Utilities.Logging;

using UnityEngine;

namespace Niantic.ARDK.VirtualStudio.AR.Mock
{
  public class MockWayspot: MonoBehaviour
  {
    [SerializeField][_ReadOnly]
    private string _wayspotName;

    [SerializeField][_ReadOnly]
    private GameObject _meshObject;

    internal string _WayspotName
    {
      get
      {
        return _wayspotName;
      }
      set
      {
        _wayspotName = value;
      }
    }

    internal GameObject _MeshObject
    {
      get
      {
        return _meshObject;
      }
      set
      {
        _meshObject = value;
      }
    }

    [SerializeField][HideInInspector]
    private VPSLocationManifest _vpsLocationManifest;

    public VPSLocationManifest _VPSLocationManifest
    {
      get => _vpsLocationManifest;
      set => _vpsLocationManifest = value;
    }

    private Dictionary<string, AuthoredWayspotAnchorData> _allAnchorsMapping;

    private void Awake()
    {
      if (_MeshObject == null)
        ARLog._WarnRelease($"No MeshObject found in the spawned MockWayspot.");
    }

    public bool TryResolve(byte[] payloadBlob, out AuthoredWayspotAnchorData anchorData)
    {
      // Do a null check here, because method might be invoked before Awake
      if (_allAnchorsMapping == null)
      {
        _allAnchorsMapping = new Dictionary<string, AuthoredWayspotAnchorData>();

        foreach (var anchor in _VPSLocationManifest.AuthoredAnchorsData)
          _allAnchorsMapping.Add(anchor.Payload, anchor);
      }

      var payload = new WayspotAnchorPayload(payloadBlob).Serialize();
      return _allAnchorsMapping.TryGetValue(payload, out anchorData);
    }
  }
}
#endif
