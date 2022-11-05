using System;

using Niantic.ARDK.AR.WayspotAnchors;
using Niantic.ARDK.Extensions;

using UnityEngine;

namespace Niantic.ARDKExamples.RemoteAuthoring
{
  // Tracks the status of the associated WayspotAnchor and sets the visibility of the associated
  // game object to match the resolved status of the anchor.
  public class AnchorStatusTracker: WayspotAnchorTracker
  {
    private WayspotAnchorStatusCode _currentStatusCode = WayspotAnchorStatusCode.Pending;

    public bool AnchorIsResolved
    {
      get
      {
        return (_currentStatusCode == WayspotAnchorStatusCode.Success
               || _currentStatusCode == WayspotAnchorStatusCode.Limited);
      }
    }
    
    private void Awake()
    {
      SetCurrentStatusCode(WayspotAnchorStatusCode.Pending);
    }

    protected override void OnAnchorAttached()
    {
      base.OnAnchorAttached();

      // Must update the status code so that the visibility reflects the anchor status.
      SetCurrentStatusCode(WayspotAnchor.Status);
    }

    protected override void OnStatusCodeUpdated(WayspotAnchorStatusUpdate args)
    {
      base.OnStatusCodeUpdated(args);
      SetCurrentStatusCode(args.Code);
    }

    private void SetCurrentStatusCode(WayspotAnchorStatusCode code)
    {
      _currentStatusCode = code;

      gameObject.SetActive(AnchorIsResolved);
    }
  }
}
