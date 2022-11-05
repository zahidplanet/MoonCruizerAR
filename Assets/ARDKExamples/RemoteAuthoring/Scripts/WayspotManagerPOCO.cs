using System.Collections;
using System.Collections.Generic;
using Niantic.ARDK.AR;
using Niantic.ARDK.AR.ARSessionEventArgs;
using Niantic.ARDK.AR.WayspotAnchors;
using Niantic.ARDK.LocationService;
using UnityEngine;

namespace Niantic.ARDKExamples.RemoteAuthoring
{
    public delegate void StatusLogChanged(string statusMessage);
    public delegate void LocalizationStatusChanged(string localizationMessage);
    public class WayspotManagerPOCO
    {
        public event StatusLogChanged StatusLogChangeEvent;
        public event LocalizationStatusChanged LocalizationStatusChangeEvent;
        
        private WayspotAnchorService _wayspotAnchorService;
        private IARSession _arSession;
        
        private IWayspotAnchorsConfiguration _config;

        public WayspotManagerPOCO()
        {
            StartUpWayspotManager();
        }
        private void StartUpWayspotManager()
        {
            // This is necessary for setting the user id associated with the current user.
            // We strongly recommend generating and using User IDs. Accurate user information allows
            //  Niantic to support you in maintaining data privacy best practices and allows you to
            //  understand usage patterns of features among your users.
            // ARDK has no strict format or length requirements for User IDs, although the User ID string
            //  must be a UTF8 string. We recommend avoiding using an ID that maps back directly to the
            //  user. So, for example, don’t use email addresses, or login IDs. Instead, you should
            //  generate a unique ID for each user. We recommend generating a GUID.
            // When the user logs out, clear ARDK's user id with ArdkGlobalConfig.ClearUserIdOnLogout

            //  Sample code:
            //  // GetCurrentUserId() is your code that gets a user ID string from your login service
            //  var userId = GetCurrentUserId();
            //  ArdkGlobalConfig.SetUserIdOnLogin(userId);
            
            StatusLogChangeEvent?.Invoke("Initializing Session.");
            ARSessionFactory.SessionInitialized += HandleSessionInitialized;
        }
        
        private WayspotAnchorService CreateWayspotAnchorService()
        {
            var locationService = LocationServiceFactory.Create(_arSession.RuntimeEnvironment);
            locationService.Start();

            if (_config == null)
                _config = WayspotAnchorsConfigurationFactory.Create();

            var wayspotAnchorService =
                new WayspotAnchorService
                (
                    _arSession,
                    locationService,
                    _config
                );

            wayspotAnchorService.LocalizationStateUpdated += OnLocalizationStateUpdated;

            return wayspotAnchorService;
        }

        public bool RestoreAnchorsWithPayload(out IWayspotAnchor[] anchors, params WayspotAnchorPayload[] anchorPayloads)
        {
            anchors = _wayspotAnchorService.RestoreWayspotAnchors(anchorPayloads);
            if (anchors.Length == 0)
            {
                Debug.LogError("anchor(s) was not created for some reason: ");
                return false; // error raised in CreateWayspotAnchors
            }

            return true;
        }

        public void DestroyAnchors(params IWayspotAnchor[] anchors)
        {
            _wayspotAnchorService.DestroyWayspotAnchors(anchors);
        }

        public void ShutDown()
        {
            ARSessionFactory.SessionInitialized -= HandleSessionInitialized;
            if (_wayspotAnchorService != null)
            {
                _wayspotAnchorService.LocalizationStateUpdated -= OnLocalizationStateUpdated;
                _wayspotAnchorService.Dispose();
            }
        }
        public void RestartWayspotAnchorService()
        {
            _wayspotAnchorService.Restart();
        }

        private void HandleSessionInitialized(AnyARSessionInitializedArgs args)
        {
            StatusLogChangeEvent?.Invoke("Session initialized");
            _arSession = args.Session;
            _arSession.Ran += HandleSessionRan;
        }

        private void HandleSessionRan(ARSessionRanArgs args)
        {
            _arSession.Ran -= HandleSessionRan;
            _wayspotAnchorService = CreateWayspotAnchorService();
            _wayspotAnchorService.LocalizationStateUpdated += OnLocalizationStateUpdated;
            StatusLogChangeEvent?.Invoke("Session running");
        }
        
        private void OnLocalizationStateUpdated(LocalizationStateUpdatedArgs args)
        {
            LocalizationStatusChangeEvent?.Invoke(args.State.ToString());
        }
    }
}
