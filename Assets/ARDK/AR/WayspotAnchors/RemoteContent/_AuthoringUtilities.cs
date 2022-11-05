using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

using Niantic.ARDK.Configuration;
using Niantic.ARDK.Configuration.Authentication;
using Niantic.ARDK.Configuration.Internal;
using Niantic.ARDK.Internals;
using Niantic.ARDK.Utilities;
using Niantic.ARDK.Utilities.Logging;
using Niantic.ARDK.VPSCoverage;

using UnityEngine;

namespace Niantic.ARDK.AR.WayspotAnchors
{
  internal class _AuthoringUtilities
  {
    // TODO (kcho): Send multiple poses in single create request to reduce latency when creating
    // multiple anchors

    // @param pose Transform from the node origin to the pose
    // @param node_id
    // @returns (anchorIdentifier, anchorPayload)
    public static async Task<(string, string)> Create(Matrix4x4 pose, string node_id)
    {
      var apiKey = ArdkGlobalConfig._Internal.GetApiKey();
      if (string.IsNullOrEmpty(apiKey))
      {
        ARLog._Error($"An API key must be set in order to create WayspotAnchors.");
      }

      HttpClient client = new HttpClient();
      client.BaseAddress = new Uri("https://vps-frontend.nianticlabs.com/web/vps_frontend.protogen.Localizer/");
      client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(apiKey);

      // Mock an identity localization to the node id passed in to the function.
      // We do this because we already know the offset between node and virtual object.
      // So the local tracking system does not matter and can be eliminated from the equation by making it Identity
      _VpsDefinitions.Transform serLocalizationTransform = new _VpsDefinitions.Transform(Matrix4x4.identity);
      _VpsDefinitions.Localization[] serLocalizations = new _VpsDefinitions.Localization[1];
      serLocalizations[0] = new _VpsDefinitions.Localization(node_id, 0.7F, serLocalizationTransform);

      // Convert from unity coordinates to narwhal coordinates
      var narPose = NARConversions.FromUnityToNAR(pose);

      // Serialize transform and set it in the API as the requested pose for the wayspot anchor
      _VpsDefinitions.Transform serManagedPoseTransform = new _VpsDefinitions.Transform(narPose);
      _VpsDefinitions.CreationInput[] serCreationInputs = new _VpsDefinitions.CreationInput[1];
      serCreationInputs[0] = new _VpsDefinitions.CreationInput(Guid.NewGuid().ToString(), serManagedPoseTransform);

      // Create the request
      var anchorIdentifier = Guid.NewGuid().ToString();
      var createRequest =
        new _VpsDefinitions.CreateManagedPosesRequest(anchorIdentifier, serLocalizations, serCreationInputs);

      var requestString = JsonUtility.ToJson(createRequest, true);
      HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "CreateManagedPoses");
      request.Content = new StringContent
      (
        requestString,
        Encoding.UTF8,
        "application/json"
      );

      var response = await client.SendAsync(request);

      // Check success
      if (!response.IsSuccessStatusCode)
      {
        ARLog._Error($"Request to create WayspotAnchor failed with HTTP error code {response.StatusCode}.");
        return (null, null);
      }

      // Get JSON response
      string content = await response.Content.ReadAsStringAsync();
      var createResponse = JsonUtility.FromJson<_VpsDefinitions.CreateManagedPosesResponse>(content);

      // Code below assumes only a single anchor was created, which is true above
      // So we access the first element in the response array to get the anchor blob

      // Check status of anchors
      // TODO (kcho): what is the overall status vs each anchor's status?
      var status = _ResponseStatusTranslator.FromString(createResponse.statusCode);
      if (status != _VpsDefinitions.StatusCode.STATUS_CODE_SUCCESS)
      {
        ARLog._Error($"Request to create WayspotAnchor failed due to {status}.");
        return (null, null);
      }

      // Save B64 encoded anchor
      string managedPoseB64 = createResponse.creations[0].managedPose.data;
      return (anchorIdentifier, managedPoseB64);
    }
  }
}
