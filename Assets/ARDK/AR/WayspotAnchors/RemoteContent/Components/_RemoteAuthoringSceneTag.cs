using System;

using UnityEngine;

namespace Niantic.ARDK.AR.WayspotAnchors
{
  // Class is not in EditModeOnlyBehaviour because it stays in the scene
  internal class _RemoteAuthoringSceneTag: MonoBehaviour
  {
    private void Reset()
    {
      gameObject.hideFlags = HideFlags.HideInHierarchy;
    }
  }
}
