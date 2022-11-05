// Copyright 2022 Niantic, Inc. All Rights Reserved.
#if UNITY_STANDALONE_OSX || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_WIN
#define UNITY_STANDALONE_DESKTOP
#endif
#if (UNITY_IOS || UNITY_ANDROID || UNITY_STANDALONE_DESKTOP) && !UNITY_EDITOR
#define AR_NATIVE_SUPPORT
#endif

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

using AOT;

using UnityEngine;
using UnityEngine.Rendering;

using Niantic.ARDK.AR.Anchors;
using Niantic.ARDK.AR.ARSessionEventArgs;
using Niantic.ARDK.AR.Awareness;
using Niantic.ARDK.AR.Camera;
using Niantic.ARDK.AR.Configuration;
using Niantic.ARDK.AR.Awareness.Depth.Generators;
using Niantic.ARDK.AR.Awareness.Human;
using Niantic.ARDK.AR.Frame;
using Niantic.ARDK.AR.PointCloud;
using Niantic.ARDK.AR.Mesh;
using Niantic.ARDK.AR.Protobuf;
using Niantic.ARDK.AR.SLAM;
using Niantic.ARDK.Extensions.Meshing;
using Niantic.ARDK.Internals;
using Niantic.ARDK.LocationService;
using Niantic.ARDK.Rendering;
using Niantic.ARDK.Telemetry;
using Niantic.ARDK.Utilities;
using Niantic.ARDK.Utilities.BinarySerialization.ItemSerializers;
using Niantic.ARDK.Utilities.Collections;
using Niantic.ARDK.Utilities.Logging;
using Niantic.ARDK.VirtualStudio;
using Niantic.ARDK.VirtualStudio.AR;



namespace Niantic.ARDK.AR
{
  /// <inheritdoc />
  internal sealed class _NativeARSession:
    _ThreadCheckedObject,
    _IARSession
  {
    /// Indicates whether this is a playback based session
    private bool _playbackEnabled = false;

    RuntimeEnvironment IARSession.RuntimeEnvironment
    {
      get => _playbackEnabled ? RuntimeEnvironment.Playback : RuntimeEnvironment.LiveDevice;
    }

    /// <inheritdoc />
    public IARConfiguration Configuration { get; private set; }

    private IARFrame _currentFrame;
    /// <inheritdoc />
    public IARFrame CurrentFrame
    {
      get => _currentFrame;
      internal set
      {
        _CheckThread();

        _SessionFrameSharedLogic._MakeSessionFrameBecomeNonCurrent(this);
        _currentFrame = value;
      }
    }

    public ARSessionChangesCollector ARSessionChangesCollector { get; private set; }

    /// <inheritdoc />
    public ARFrameDisposalPolicy DefaultFrameDisposalPolicy { get; set; }

#pragma warning disable 0414
    private DepthPointCloudGenerator _depthPointCloudGen;
#pragma warning restore 0414

    private _NativeLocationServiceAdapter _locationServiceAdapter;

    private CommandBuffer _commandBuffer = null;
    private _VirtualCamera _virtualCamera = null;
    private UnityEngine.Camera _updatingCamera = null;

    /// <inheritdoc />
    public float WorldScale { get; set; } = 1;

    public ARSessionRunOptions RunOptions { get; private set; }

    /// <inheritdoc />
    public Guid StageIdentifier { get; }

    static _NativeARSession()
    {
      _Platform.Init();
    }

    /// <inheritdoc />
    public ARSessionState State { get; private set; }

    public IARMesh Mesh
    {
      get => _meshDataParser;
    }

    private _MeshDataParser _meshDataParser = new _MeshDataParser();

    public HandTracker HandTracker
    {
      get
      {
        if (_handTracker == null)
        {
          _handTracker = new HandTracker(this);
        }

        return _handTracker;
      }
    }

    private HandTracker _handTracker;

    public _PlaybackDataset PlaybackDataset { get; private set; }

    /// <inheritdoc />
    internal _NativeARSession(Guid stageIdentifier, bool playbackEnabled = false)
    {
      _FriendTypeAsserter.AssertCallerIs(typeof(ARSessionFactory));

#if UNITY_ANDROID
      if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Vulkan)
        ARLog._Error("AR Core is not compatible with Vulkan. You're going to get a black screen.");
#endif

      StageIdentifier = stageIdentifier;
      _playbackEnabled = playbackEnabled;
      ARSessionChangesCollector = new ARSessionChangesCollector(this);

      if (_NativeAccess.IsNativeAccessValid())
      {
        ARLog._DebugFormat
        (
          "Creating {0} _NativeARSession with stage identifier: {1}",
          false,
          _playbackEnabled ? RuntimeEnvironment.Playback : RuntimeEnvironment.LiveDevice,
          stageIdentifier
        );

        if (playbackEnabled)
        {
          var launcher =
            (_PlaybackModeLauncher)_VirtualStudioLauncher.GetOrCreateModeLauncher(RuntimeEnvironment.Playback);

          PlaybackDataset = new _PlaybackDataset(launcher.DatasetPath);
          _nativeHandle = _NARPlaybackSession_Init(StageIdentifier.ToByteArray());
        }
        else
        {
          _nativeHandle = _NARSession_Init(StageIdentifier.ToByteArray());
        }

        // Inform the GC that this class is holding a large native object, so it gets cleaned up fast
        // TODO(awang): Make an IReleasable interface that handles this for all native-related classes
        GC.AddMemoryPressure(GCPressure);

        ARLog._DebugFormat("Created _NativeARSession with handle: {0}", false, _nativeHandle);
        SubscribeToInternalCallbacks();
      }
      #pragma warning disable 0162
      else
      {
        ARLog._DebugFormat
        (
          "Creating testing _NativeARSession with stage identifier: {0}",
          false,
          stageIdentifier
        );

        _nativeHandle = (IntPtr)1;
      }
      #pragma warning restore 0162
    }

    ~_NativeARSession()
    {
      Dispose(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
      _CheckThread();

      ARLog._Debug($"Dispose called on {nameof(_NativeARSession)}");

      GC.SuppressFinalize(this);
      Dispose(true);
    }

    private void Dispose(bool disposing)
    {
      if (disposing)
      {
        var deinitializing = Deinitialized;
        if (deinitializing != null)
        {
          var args = new ARSessionDeinitializedArgs();
          deinitializing(args);
        }

        DisposeGenerators();

        var cachedAnchors = _cachedAnchors;
        if (cachedAnchors != null)
        {
          _cachedAnchors = null;

          foreach (var anchor in cachedAnchors.Values)
            anchor.Dispose();
        }

        CurrentFrame?.Dispose();
        CurrentFrame = null;
        ARSessionChangesCollector = null;

        _meshDataParser?.Dispose();
        _meshDataParser = null;

        _handTracker = null;

        PlaybackDataset?.Dispose();
        PlaybackDataset = null;

        _updatingCamera = null;
        _virtualCamera?.Dispose();
        _commandBuffer?.Dispose();
      }

      if (_NativeAccess.IsNativeAccessValid())
      {
        _NARSession_Release(_nativeHandle);
        GC.RemoveMemoryPressure(GCPressure);

        ARLog._Debug("Released native ARSession objects");
      }

      _cachedHandle.Free();
      _cachedHandleIntPtr = IntPtr.Zero;

      _nativeHandle = IntPtr.Zero;
      ARLog._Debug("Done disposing _NativeARSession");
    }

    private void DisposeGenerators()
    {
      var depthPointCloudGen = _depthPointCloudGen;
      if (depthPointCloudGen != null)
      {
        _depthPointCloudGen = null;
        depthPointCloudGen.Dispose();
        ARLog._Debug("Disposed depth point cloud generator");
      }
    }

    public bool IsDestroyed
    {
      get => _nativeHandle == IntPtr.Zero;
    }

    /// <inheritdoc />
    public void Run
    (
      IARConfiguration configuration,
      ARSessionRunOptions options = ARSessionRunOptions.None
    )
    {
      _CheckThread();

      if (_nativeHandle == IntPtr.Zero)
      {
        ARLog._Debug("Session was freed before Run()");
        return;
      }

      ARSessionChangesCollector._CollectChanges(configuration, ref options);

      if (!_ARConfigurationValidator.RunAllChecks(this, configuration))
      {
        ARLog._Error("Configuration validation failed, not running session");
        return;
      }
      
      try
      {
        var configForTelemetry = (IARWorldTrackingConfiguration)configuration;
        _TelemetryService.RecordEvent(new EnabledContextualAwarenessEvent()
        {
          Depth = configForTelemetry.IsDepthEnabled,
          Meshing = configForTelemetry.IsMeshingEnabled,
          Semantics = configForTelemetry.IsSemanticSegmentationEnabled
        });
      }
      finally
      { }

      Configuration = configuration;
      RunOptions = options;

      // Need to destroy the generators so they can be recreated once we get new depth data
      DisposeGenerators();

      if ((RunOptions & ARSessionRunOptions.RemoveExistingMesh) != 0)
        _meshDataParser.Clear();

      SetUpdatingCamera(_updatingCamera);

      ARLog._DebugFormat("Running _NativeARSession with options: {0}", false, options);
      
      
      if (_NativeAccess.IsNativeAccessValid())
      {
        var nativeConfiguration = (_NativeARConfiguration)configuration;
        _NARSession_Run(_nativeHandle, nativeConfiguration.NativeHandle, (UInt64)options);
      }

      State = ARSessionState.Running;
      _ran(new ARSessionRanArgs());
    }

    /// <inheritdoc />
    public void Pause()
    {
      _CheckThread();

      CurrentFrame = null;

      if (_nativeHandle == IntPtr.Zero)
      {
        ARLog._Debug("Session was freed before call to _NativeARSession.Pause");
        return;
      }

      if (_NativeAccess.IsNativeAccessValid())
      {
        _NARSession_Pause(_nativeHandle);
        ARLog._Debug("Paused native ARSession.");
      }

      State = ARSessionState.Paused;
      _paused(new ARSessionPausedArgs());
    }

    /// <inheritdoc />
    public IARAnchor AddAnchor(Matrix4x4 transform)
    {
      _CheckThread();

      if (_nativeHandle == IntPtr.Zero)
      {
        ARLog._Debug("Session was freed before AddAnchor()");
        return null;  // TODO: Should we log error and throw here?
      }

      var anchor = _ARAnchorFactory._Create(transform);

      if (_NativeAccess.IsNativeAccessValid())
      {
        var nativeAnchor = anchor as _NativeARAnchor;
        if (nativeAnchor != null)
        {
          _NARSession_AddAnchor(_nativeHandle, nativeAnchor._NativeHandle);

          var identifier = nativeAnchor.Identifier;
          if (!_cachedAnchors.ContainsKey(identifier))
            _cachedAnchors.Add(identifier, nativeAnchor);

          ARLog._DebugFormat("Added native anchor {0}", false, identifier);
        }
        else
        {
          ARLog._Debug("Anchor creation failed, did not add native anchor");
        }
      }

      return anchor;
    }

    /// <inheritdoc />
    public void RemoveAnchor(IARAnchor anchor)
    {
      _CheckThread();

      if (_nativeHandle == IntPtr.Zero)
      {
        ARLog._Debug("Session was freed before RemoveAnchor()");
        return;
      }

      if (_NativeAccess.IsNativeAccessValid())
      {
        ARLog._DebugFormat("Removing native anchor {0}", false, anchor.Identifier);
        var nativeAnchor = anchor as _NativeARAnchor;
        if (nativeAnchor != null)
          _NARSession_RemoveAnchor(_nativeHandle, nativeAnchor._NativeHandle);
      }
    }

    internal bool _IsLocationServiceInitialized { get; private set; }

    public void SetupLocationService(ILocationService locationService)
    {
      _CheckThread();

      if (_locationServiceAdapter != null)
      {
        ARLog._Error("This ARSession is already listening to a LocationService instance.");
        return;
      }

      _IsLocationServiceInitialized = true;

      if (_playbackEnabled)
      {
        // Do nothing, because location updates are fed to required systems c++ side
        return;
      }

      _locationServiceAdapter =
        new _NativeLocationServiceAdapter(StageIdentifier, locationService);

      _locationServiceAdapter.AssignWrapper(locationService);
    }

    private struct AwarenessFeaturesCheckStatus
    {
      public AwarenessInitializationStatus Status;
      public AwarenessInitializationError Error;
      public string Message;
    }

    private AwarenessFeaturesCheckStatus? _awarenessFeaturesCheck;

    public AwarenessInitializationStatus GetAwarenessInitializationStatus
    (
      out AwarenessInitializationError error,
      out string errorMessage
    )
    {
      _CheckThread();

      if (_nativeHandle != IntPtr.Zero)
      {
        if (_NativeAccess.Mode == _NativeAccess.ModeType.Native)
        {
          var code = (_NativeAwarenessInitializationCode)_NARSession_GetAwarenessFeaturesError(_nativeHandle);
          var status = code.ToStatus();
          error = code.ToError();

          var complete =
            status == AwarenessInitializationStatus.Ready ||
            status == AwarenessInitializationStatus.Failed;

          if (complete)
          {
            var ptr = _NARSession_GetAwarenessFeaturesErrorMessage(_nativeHandle);
            if (ptr != IntPtr.Zero)
              errorMessage = Marshal.PtrToStringAnsi(ptr);
            else
              errorMessage = string.Empty;

            _awarenessFeaturesCheck =
              new AwarenessFeaturesCheckStatus
              {
                Status = code.ToStatus(),
                Error = code.ToError(),
                Message = errorMessage
              };
          }
          else
          {
            errorMessage = string.Empty;
          }

          return status;
        }
#pragma warning disable CS0162
        else
        {
          // TODO AR-10906: Fix _NARSession_GetAwarenessFeaturesErrorMessage returning garbage values for playback
          error = AwarenessInitializationError.None;
          errorMessage = string.Empty;
          return AwarenessInitializationStatus.Ready;
        }
#pragma warning restore CS0162
      }
      else
      {
        ARLog._Debug("Session was freed before call to _GetAwarenessFeaturesStatus()");
      }

      // Default values when native was not queried
      error = AwarenessInitializationError.None;
      errorMessage = string.Empty;
      return AwarenessInitializationStatus.Unknown;
    }
    
    [Obsolete
    (
      "Explicit addition of a command to invoke the native render " +
      "event will be removed in favor of handling it through cameras."
    )]
    internal void AddIssueNativeRenderEvent(CommandBuffer commandBuffer)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
      commandBuffer?.IssuePluginEventAndData(GetRenderEventFunc(), 1, _nativeHandle);
#endif
    }

    /// Delegates the responsibility of driving frame updates to the specified camera.
    /// This call is only relevant on Android as ARKit pushes updates automatically.
    /// Calling this method with null will result in assigning the update task to the
    /// AR session.
    /// @param camera The camera that will drive the ARCore frame updates.
    internal void SetUpdatingCamera(UnityEngine.Camera camera)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
      // Only required on Android
      _CheckThread();
      
      if (_commandBuffer == null)
      {
        // Allocate the command buffer that periodically invokes the native ARCore update event
        _commandBuffer = new CommandBuffer {name = "ARCore update driver"};
        _commandBuffer.IssuePluginEventAndData(GetRenderEventFunc(), 1, _nativeHandle);
      }

      // Assign to the internal camera
      if (camera == null)
      {
        // Remove ARCore updates from the current external camera, if any
        if (_updatingCamera != null)
          ARSessionBuffersHelper.RemoveBackgroundBuffer(_updatingCamera, _commandBuffer);
        
        // Assign updating ARCore to the internal camera
        _virtualCamera ??= _VirtualCameraFactory.CreateContinousVirtualCamera(_commandBuffer);
        ARLog._Debug("Fetching ARCore updates are enabled on the _NativeARSession.");
      }
      
      // Assign to a new external camera
      else if (_updatingCamera != camera)
      {
        // Dispose the internal camera, if it exists
        _virtualCamera?.Dispose();
        _virtualCamera = null;

        // Remove ARCore updates from the current external camera, if any
        if (_updatingCamera != null)
          ARSessionBuffersHelper.RemoveBackgroundBuffer(_updatingCamera, _commandBuffer);

        // Assign updating ARCore to the new camera
        ARSessionBuffersHelper.AddAfterRenderingBuffer(camera, _commandBuffer);
        ARLog._Debug("Fetching ARCore updates are disabled on the _NativeARSession.");
      }

      _updatingCamera = camera;
#endif
    }

    /// @name Events
    /// @{
    /// <inheritdoc />
    public event ArdkEventHandler<ARSessionDeinitializedArgs> Deinitialized;

    /// <inheritdoc />
    public event ArdkEventHandler<ARSessionRanArgs> Ran
    {
      add
      {
        _CheckThread();

        _ran += value;

        if (State == ARSessionState.Running)
          value(new ARSessionRanArgs());
      }
      remove => _ran -= value;
    }

    /// <inheritdoc />
    public event ArdkEventHandler<ARSessionPausedArgs> Paused
    {
      add
      {
        _CheckThread();

        _paused += value;

        if (State == ARSessionState.Paused)
          value(new ARSessionPausedArgs());
      }
      remove => _paused -= value;
    }

    private ArdkEventHandler<FrameUpdatedArgs> _frameUpdated;

    /// <inheritdoc />
    public event ArdkEventHandler<FrameUpdatedArgs> FrameUpdated
    {
      add
      {
        _CheckThread();

        SubscribeToDidUpdateFrame();

        _frameUpdated += value;
      }
      remove => _frameUpdated -= value;
    }

    private ArdkEventHandler<AnchorsArgs> _anchorsAdded;

    /// <inheritdoc />
    public event ArdkEventHandler<AnchorsArgs> AnchorsAdded
    {
      add
      {
        _CheckThread();

        SubscribeToDidAddAnchors();

        _anchorsAdded += value;
      }
      remove => _anchorsAdded -= value;
    }


    private ArdkEventHandler<AnchorsArgs> _anchorsUpdated;

    /// <inheritdoc />
    public event ArdkEventHandler<AnchorsArgs> AnchorsUpdated
    {
      add
      {
        _CheckThread();

        SubscribeToDidUpdateAnchors();

        _anchorsUpdated += value;
      }
      remove => _anchorsUpdated -= value;
    }


    private ArdkEventHandler<AnchorsArgs> _anchorsRemoved;

    /// <inheritdoc />
    public event ArdkEventHandler<AnchorsArgs> AnchorsRemoved
    {
      add
      {
        _CheckThread();

        SubscribeToDidRemoveAnchors();

        _anchorsRemoved += value;
      }
      remove => _anchorsRemoved -= value;
    }

    /// <inheritdoc />
    public event ArdkEventHandler<AnchorsMergedArgs> AnchorsMerged
    {
      add
      {
        _CheckThread();

        SubscribeToDidMergeAnchors();

        _anchorsMerged += value;
      }
      remove => _anchorsMerged -= value;
    }

    private ArdkEventHandler<MapsArgs> _mapsAdded;

    /// <inheritdoc />
    public event ArdkEventHandler<MapsArgs> MapsAdded
    {
      add
      {
        _CheckThread();

        SubscribeToDidAddMaps();

        _mapsAdded += value;
      }
      remove => _mapsAdded -= value;
    }


    private ArdkEventHandler<MapsArgs> _mapsUpdated;

    /// <inheritdoc />
    public event ArdkEventHandler<MapsArgs> MapsUpdated
    {
      add
      {
        _CheckThread();

        SubscribeToDidUpdateMaps();

        _mapsUpdated += value;
      }
      remove => _mapsUpdated -= value;
    }

    private ArdkEventHandler<CameraTrackingStateChangedArgs> _cameraTrackingStateChanged;

    /// <inheritdoc />
    public event ArdkEventHandler<CameraTrackingStateChangedArgs> CameraTrackingStateChanged
    {
      add
      {
        _CheckThread();

        SubscribeToCameraDidChangeTrackingState();

        _cameraTrackingStateChanged += value;
      }
      remove => _cameraTrackingStateChanged -= value;
    }

    /// <inheritdoc />
    public event ArdkEventHandler<ARSessionInterruptedArgs> SessionInterrupted
    {
      add
      {
        _CheckThread();

        SubscribeToWasInterrupted();

        _sessionInterrupted += value;
      }
      remove => _sessionInterrupted -= value;
    }

    /// <inheritdoc />
    public event ArdkEventHandler<ARSessionInterruptionEndedArgs> SessionInterruptionEnded
    {
      add
      {
        _CheckThread();

        SubscribeToInterruptionEnded();

        _sessionInterruptionEnded += value;
      }
      remove => _sessionInterruptionEnded -= value;
    }

    /// <inheritdoc />
    public event ArdkEventHandler<QueryingShouldSessionAttemptRelocalizationArgs>
      QueryingShouldSessionAttemptRelocalization
    {
      add
      {
        _CheckThread();

        SubscribeToShouldAttemptRelocalization();

        _queryingShouldSessionAttemptRelocalization.Add(value);
      }
      remove => _queryingShouldSessionAttemptRelocalization.Remove(value);
    }

    /// <inheritdoc />
    public event ArdkEventHandler<ARSessionFailedArgs> SessionFailed
    {
      add
      {
        _CheckThread();

        SubscribeToDidFailWithError();

        _sessionFailed += value;
      }
      remove => _sessionFailed -= value;
    }

    /// @}

    // Private handles and code to deal with native callbacks and initialization
    private IntPtr _nativeHandle;

    // Caching `this` for native device callbacks
    private IntPtr _cachedHandleIntPtr = IntPtr.Zero;
    private SafeGCHandle<_NativeARSession> _cachedHandle;

    // Approx memory consumption of native objects + ARCore/ARKit session
    // Magic number representing 100MB, which is approximately the profiled
    //  consumption on an iPhone 8
    private const long GCPressure = 100L * 1024L * 1024L;

    private IntPtr _handle
    {
      get
      {
        _CheckThread();

        var cachedHandleIntPtr = _cachedHandleIntPtr;
        if (cachedHandleIntPtr != IntPtr.Zero)
          return cachedHandleIntPtr;

        _cachedHandle = SafeGCHandle.Alloc(this);
        cachedHandleIntPtr = _cachedHandle.ToIntPtr();
        _cachedHandleIntPtr = cachedHandleIntPtr;

        return cachedHandleIntPtr;
      }
    }

#region CallbackImplementation
    private bool _updateFrameInitialized;
    private bool _updateMeshInitialized;
    private bool _addAnchorsInitialized;
    private bool _updateAnchorsInitialized;
    private bool _removeAnchorsInitialized;
    private bool _mergeAnchorsInitialized;
    private bool _addMapsInitialized;
    private bool _updateMapsInitialized;

    private bool _cameraDidChangeTrackingStateInitialized;
    private bool _sessionWasInterruptedInitialized;
    private bool _sessionInterruptionEndedInitialized;
    private bool _sessionShouldAttemptRelocalizationInitialized;
    private bool _sessionDidFailWithErrorInitialized;

#region InternalSubscriptions
    // This callback is subscribed to at initialization as it sets data that will be provided by
    // accessors (CurrentFrame and Mesh)
    private void SubscribeToInternalCallbacks()
    {
      SubscribeToDidUpdateFrame();
      SubscribeToDidUpdateMesh();
    }

    private void SubscribeToDidUpdateFrame()
    {
      _CheckThread();

      if (_updateFrameInitialized)
        return;

      if (_NativeAccess.IsNativeAccessValid())
      {
        _NARSession_Set_didUpdateFrameCallback
        (
          _handle,
          _nativeHandle,
          _onDidUpdateFrameNative
        );

        ARLog._Debug("Subscribed to native frame update");
      }

      _updateFrameInitialized = true;
    }

    internal void SubscribeToDidUpdateMesh()
    {
      if (_updateMeshInitialized)
        return;

      lock (this)
      {
        if (_updateMeshInitialized)
          return;

        if (_NativeAccess.IsNativeAccessValid())
        {
          _NARSession_Set_didUpdateMeshCallback(_handle, _nativeHandle, _onDidUpdateMeshNative);
        }

        _updateMeshInitialized = true;
      }
    }

    private void SubscribeToDidAddAnchors()
    {
      _CheckThread();

      if (_addAnchorsInitialized)
        return;

      if (_NativeAccess.IsNativeAccessValid())
      {
        _NARSession_Set_didAddAnchorsCallback
        (
          _handle,
          _nativeHandle,
          _onDidAddAnchorsNative
        );

        ARLog._Debug("Subscribed to native anchors added");
      }

      _addAnchorsInitialized = true;
    }

    private void SubscribeToDidUpdateAnchors()
    {
      _CheckThread();

      if (_updateAnchorsInitialized)
        return;

      if (_NativeAccess.IsNativeAccessValid())
      {
        _NARSession_Set_didUpdateAnchorsCallback
        (
          _handle,
          _nativeHandle,
          _onDidUpdateAnchorsNative
        );

        ARLog._Debug("Subscribed to native anchors updated");
      }

      _updateAnchorsInitialized = true;
    }

    private void SubscribeToDidRemoveAnchors()
    {
      _CheckThread();

      if (_removeAnchorsInitialized)
        return;

      if (_NativeAccess.IsNativeAccessValid())
      {
        _NARSession_Set_didRemoveAnchorsCallback
        (
          _handle,
          _nativeHandle,
          _onDidRemoveAnchorsNative
        );

        ARLog._Debug("Subscribed to native anchors removed");
      }

      _removeAnchorsInitialized = true;
    }

    private unsafe void SubscribeToDidMergeAnchors()
    {
      _CheckThread();

      if (_mergeAnchorsInitialized)
        return;

      if (_NativeAccess.IsNativeAccessValid())
      {
        _NARSession_Set_didMergeAnchorsCallback
        (
          _handle,
          _nativeHandle,
          _onDidMergeAnchorsNative
        );

        ARLog._Debug("Subscribed to native anchors merged");
      }

      _mergeAnchorsInitialized = true;
    }

    private void SubscribeToDidAddMaps()
    {
      _CheckThread();

      if (_addMapsInitialized)
        return;

      if (_NativeAccess.IsNativeAccessValid())
      {
        _NARSession_Set_didAddMapsCallback
        (
          _handle,
          _nativeHandle,
          _onDidAddMapsNative
        );

        ARLog._Debug("Subscribed to native maps added");
      }

      _addMapsInitialized = true;
    }

    private void SubscribeToDidUpdateMaps()
    {
      _CheckThread();

      if (_updateMapsInitialized)
        return;

      if (_NativeAccess.IsNativeAccessValid())
      {
        _NARSession_Set_didUpdateMapsCallback
        (
          _handle,
          _nativeHandle,
          _onDidUpdateMapsNative
        );

        ARLog._Debug("Subscribed to native maps updated");
      }

      _updateMapsInitialized = true;
    }

    private void SubscribeToCameraDidChangeTrackingState()
    {
      _CheckThread();

      if (_cameraDidChangeTrackingStateInitialized)
        return;

      if (_NativeAccess.IsNativeAccessValid())
      {
        _NARSession_Set_cameraDidChangeTrackingStateCallback
        (
          _handle,
          _nativeHandle,
          _onCameraDidChangeTrackingStateNative
        );

        ARLog._Debug("Subscribed to native camera tracking state updates");
      }

      _cameraDidChangeTrackingStateInitialized = true;
    }

    private void SubscribeToWasInterrupted()
    {
      _CheckThread();

      if (_sessionWasInterruptedInitialized)
        return;

      if (_NativeAccess.IsNativeAccessValid())
      {
        _NARSession_Set_wasInterruptedCallback
        (
          _handle,
          _nativeHandle,
          _onSessionWasInterruptedNative
        );

        ARLog._Debug("Subscribed to native session interrupted event");
      }

      _sessionWasInterruptedInitialized = true;
    }

    private void SubscribeToInterruptionEnded()
    {
      _CheckThread();

      if (_sessionInterruptionEndedInitialized)
        return;

      if (_NativeAccess.IsNativeAccessValid())
      {
        _NARSession_Set_interruptionEndedCallback
        (
          _handle,
          _nativeHandle,
          _onSessionInterruptionEndedNative
        );

        ARLog._Debug("Subscribed to native session interruption ended event");
      }

      _sessionInterruptionEndedInitialized = true;
    }

    private void SubscribeToShouldAttemptRelocalization()
    {
      _CheckThread();

      if (_sessionShouldAttemptRelocalizationInitialized)
        return;

      if (_NativeAccess.IsNativeAccessValid())
      {
        _NARSession_Set_shouldAttemptRelocalizationCallback
        (
          _handle,
          _nativeHandle,
          _onSessionShouldAttemptRelocalizationNative
        );

        ARLog._Debug("Subscribed to native should attempt relocalization event");
      }

      _sessionShouldAttemptRelocalizationInitialized = true;
    }

    private void SubscribeToDidFailWithError()
    {
      _CheckThread();

      if (_sessionDidFailWithErrorInitialized)
        return;

      if (_NativeAccess.IsNativeAccessValid())
      {
        _NARSession_Set_didFailWithErrorCallback
        (
          _handle,
          _nativeHandle,
          _onSessionDidFailWithErrorNative
        );

        ARLog._Debug("Subscribed to native session failed event");
      }

      _sessionDidFailWithErrorInitialized = true;
    }

#endregion

#region NativeCallbacks

    private ArdkEventHandler<AnchorsMergedArgs> _anchorsMerged = args => {};

    private ArdkEventHandler<ARSessionInterruptedArgs> _sessionInterrupted = (args) => {};

    private ArdkEventHandler<ARSessionInterruptionEndedArgs> _sessionInterruptionEnded = (args) => {};

    private readonly List<ArdkEventHandler<QueryingShouldSessionAttemptRelocalizationArgs>>
      _queryingShouldSessionAttemptRelocalization =
        new List<ArdkEventHandler<QueryingShouldSessionAttemptRelocalizationArgs>>();

    private ArdkEventHandler<ARSessionFailedArgs> _sessionFailed = args => {};

    private ArdkEventHandler<ARSessionRanArgs> _ran = args => {};

    private ArdkEventHandler<ARSessionPausedArgs> _paused = args => {};

    private IntPtr _newestFramePtr;

    [MonoPInvokeCallback(typeof(_ARSession_Frame_Callback))]
    private static void _onDidUpdateFrameNative(IntPtr context, IntPtr framePtr)
    {
      ARLog._Debug("Got a frame from native", true);
      var session = SafeGCHandle.TryGetInstance<_NativeARSession>(context);
      if (session == null || session.IsDestroyed)
      {
        // session was deallocated
        _NativeARFrame._ReleaseImmediate(framePtr);
        ARLog._Debug("Native session surfaced a frame after the session was released", true);
        return;
      }

      var oldFramePtr = Interlocked.Exchange(ref session._newestFramePtr, framePtr);
      if (oldFramePtr != IntPtr.Zero)
      {
        // We release the old frame, as now framePtr is stored as _newestFrame.
        // The already scheduled callback queue will get our newestFrame to process.
        _NativeARFrame._ReleaseImmediate(oldFramePtr);
        ARLog._Debug("Releasing frame generated before prior one was processed.", true);
        return;
      }

      _CallbackQueue.QueueCallback
      (
        () =>
        {
          var newFramePtr = Interlocked.Exchange(ref session._newestFramePtr, IntPtr.Zero);
          if (session.IsDestroyed)
          {
            // session was deallocated
            _NativeARFrame._ReleaseImmediate(newFramePtr);
            ARLog._Debug("Native session surfaced a frame after the session was released", true);

            return;
          }

          var frame = new _NativeARFrame(newFramePtr, session.WorldScale);
#if AR_NATIVE_SUPPORT
          // We mark the frame as consumed to let the native layer know that
          // this ARFrame was not dropped and it's going to be processed by
          // the application. 
          _NARSession_MarkFrameConsumed(session._nativeHandle, newFramePtr);
#endif
          session.CurrentFrame = frame;
          session.UpdateGenerators(frame);

          var handler = session._frameUpdated;
          if (handler != null)
          {
            var args = new FrameUpdatedArgs(frame);
            handler(args);
          }
        }
      );
    }

    private void UpdateGenerators(IARFrame frame)
    {
      // There's a race condition here if a previous run (with IARWorldTrackingConfiguration)
      // surfaces a frame, then the user re-runs the session with a non-IARWorldTrackingConfiguration
      // before the frame is handled by the CallbackQueue. So we need to do a safe cast.
      if (!(Configuration is IARWorldTrackingConfiguration worldConfig))
        return;

      if (!worldConfig.IsDepthPointCloudEnabled)
        return;

      var depthBuffer = frame.Depth;
      if (depthBuffer == null || !depthBuffer.IsKeyframe)
        return;

      // Create a generator if needed
      if (_depthPointCloudGen == null)
      {
        _depthPointCloudGen = new DepthPointCloudGenerator();
        ARLog._Debug("Created new depth point cloud generator");
      }

      // Generate the point cloud
      var pointCloud = _depthPointCloudGen.GeneratePointCloud(frame.Depth, frame.Camera);
      ARLog._Debug("Updated depth point cloud generator with new keyframe", true);

      // TODO : Add pooling
      var arFrame = (_IARFrame)frame;
      arFrame.DepthPointCloud = pointCloud;
    }

    private static void DestroyFrame(IntPtr framePtr, float worldScale = 1f)
    {
      _NativeARFrame._ReleaseImmediate(framePtr);
    }

    // Caches the anchors we already know of and should dispose in the future.
    // Can only be accessed by the main thread.
    private Dictionary<Guid, _NativeARAnchor> _cachedAnchors =
      new Dictionary<Guid, _NativeARAnchor>();

    [MonoPInvokeCallback(typeof(_ARSession_Anchor_Callback))]
    private static void _onDidAddAnchorsNative
    (
      IntPtr context,
      IntPtr anchorsPtrs,
      UInt64 anchorsCount
    )
    {
      var session = SafeGCHandle.TryGetInstance<_NativeARSession>(context);
      if (session == null || session.IsDestroyed)
      {
        // session was deallocated
        DestroyAnchors(anchorsPtrs, anchorsCount);
        return;
      }

      var anchors = new _NativeARAnchor[anchorsCount];

      for (var i = 0; i < (int)anchorsCount; i++)
      {
        var nativeHandle = Marshal.ReadIntPtr(anchorsPtrs, i * IntPtr.Size);
        anchors[i] = _ARAnchorFactory._FromNativeHandle(nativeHandle);
      }

      _CallbackQueue.QueueCallback
      (
        () =>
        {
          if (session.IsDestroyed)
          {
            foreach (var anchor in anchors)
              anchor.Dispose();

            return;
          }

          ARLog._Debug("Surfacing added anchors");

          for (var i = 0; i < (int)anchorsCount; i++)
          {
            var anchor = anchors[i];

            var id = anchor.Identifier;

            if (!session._cachedAnchors.ContainsKey(id))
              session._cachedAnchors.Add(id, anchor);
          }

          var handler = session._anchorsAdded;
          if (handler != null)
          {
            var args = new AnchorsArgs(anchors);
            handler(args);
          }
        }
      );
    }

    [MonoPInvokeCallback(typeof(_ARSession_Anchor_Callback))]
    private static void _onDidUpdateAnchorsNative
    (
      IntPtr context,
      IntPtr anchorsPtrs,
      UInt64 anchorsCount
    )
    {
      var session = SafeGCHandle.TryGetInstance<_NativeARSession>(context);
      if (session == null || session.IsDestroyed)
      {
        // session was deallocated
        DestroyAnchors(anchorsPtrs, anchorsCount);
        return;
      }

      var anchors = new _NativeARAnchor[anchorsCount];

      for (var i = 0; i < (int)anchorsCount; i++)
      {
        var nativeHandle = Marshal.ReadIntPtr(anchorsPtrs, i * IntPtr.Size);
        anchors[i] = _ARAnchorFactory._FromNativeHandle(nativeHandle);
      }

      _CallbackQueue.QueueCallback
      (
        () =>
        {
          if (session.IsDestroyed)
          {
            foreach (var anchor in anchors)
              anchor.Dispose();

            return;
          }

          foreach (var anchor in anchors)
          {
            var id = anchor.Identifier;

            if (!session._cachedAnchors.ContainsKey(id))
            {
              ARLog._Warn("Updated anchor not found in session cache.");

              session._cachedAnchors.Add(id, anchor);
            }
          }

          var handler = session._anchorsUpdated;
          if (handler != null)
          {
            var args = new AnchorsArgs(anchors);
            handler(args);
          }
        }
      );
    }

    [MonoPInvokeCallback(typeof(_ARSession_Anchor_Callback))]
    private static void _onDidRemoveAnchorsNative
    (
      IntPtr context,
      IntPtr anchorsPtrs,
      UInt64 anchorsCount
    )
    {
      var session = SafeGCHandle.TryGetInstance<_NativeARSession>(context);
      if (session == null || session.IsDestroyed)
      {
        // session was deallocated
        DestroyAnchors(anchorsPtrs, anchorsCount);
        return;
      }

      var anchors = new IARAnchor[anchorsCount];

      for (var i = 0; i < (int)anchorsCount; i++)
      {
        var nativeHandle = Marshal.ReadIntPtr(anchorsPtrs, i * IntPtr.Size);
        anchors[i] = _ARAnchorFactory._FromNativeHandle(nativeHandle);
      }

      _CallbackQueue.QueueCallback
      (
        () =>
        {
          if (session.IsDestroyed)
          {
            for (var i = 0; i < (int)anchorsCount; i++)
              anchors[i].Dispose();

            return;
          }

          ARLog._Debug("Surfacing removed anchors");

          var handler = session._anchorsRemoved;
          if (handler != null)
          {
            var args = new AnchorsArgs(anchors);
            handler(args);
          }

          foreach (var anchor in anchors)
          {
            // Anchor could have been removed by the MergeAnchor operation
            if (anchor.IsDisposed())
              continue;

            var id = anchor.Identifier;
            anchor.Dispose();
            session._cachedAnchors.Remove(id);
          }
        }
      );
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct ArrayOfMergeInfo
    {
      public MergeInfo* array;
      public UInt32 arraySize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct MergeInfo
    {
      public IntPtr parent;
      public IntPtr* children;
      public UInt32 childrenSize;
    }

    [MonoPInvokeCallback(typeof(_ARSession_Merge_Anchor_Callback))]
    private unsafe static void _onDidMergeAnchorsNative
    (
      IntPtr context,
      ArrayOfMergeInfo* anchorsPtrs
    )
    {
      var session = SafeGCHandle.TryGetInstance<_NativeARSession>(context);
      var parentCount = anchorsPtrs->arraySize;
      MergeInfo* mergeInfoArray = anchorsPtrs->array;
      if (session == null || session.IsDestroyed)
      {
        // session was deallocated
        for (int i = 0; i < parentCount; i++)
        {
          var mergeInfo = mergeInfoArray[i];

          var childrenSize = mergeInfo.childrenSize;
          for (int j = 0; j < childrenSize; j++)
            _NativeARAnchor._ReleaseImmediate(mergeInfo.children[j]);

          _NativeARAnchor._ReleaseImmediate(mergeInfo.parent);
        }

        return;
      }

      var mergedAnchors = new Dictionary<IARPlaneAnchor, IARPlaneAnchor[]>();
      for (int i = 0; i < (int)parentCount; i++)
      {
        var mergeInfo = mergeInfoArray[i];
        IntPtr parentNativeHandle = mergeInfo.parent;
        var childrenSize = mergeInfo.childrenSize;
        IARPlaneAnchor[] childrenAsPlanes = new IARPlaneAnchor[childrenSize];

        for (int j = 0; j < childrenSize; j++)
        {
          IntPtr childNativeHandle = mergeInfo.children[j];
          var child = _ARAnchorFactory._FromNativeHandle(childNativeHandle);
          childrenAsPlanes[j] = (IARPlaneAnchor)child;
        }

        var parent = _ARAnchorFactory._FromNativeHandle(parentNativeHandle);
        mergedAnchors.Add((IARPlaneAnchor)parent, childrenAsPlanes);
      }

      _CallbackQueue.QueueCallback
      (
        () =>
        {
          if (session.IsDestroyed)
          {
            foreach (var merge in mergedAnchors)
            {
              foreach (var child in  merge.Value)
                child.Dispose();

              merge.Key.Dispose();
            }

            return;
          }

          ARLog._Debug("Surfacing merged anchors");

          var handler = session._anchorsMerged;
          if (handler != null)
          {
            foreach (var pair in mergedAnchors)
            {
              // TODO ecomas: args should be a read only dictionary, so we can just invoke the event
              // once.
              var args = new AnchorsMergedArgs(pair.Key, pair.Value);
              handler(args);
            }
          }

          foreach (var pair in mergedAnchors)
          {
            var childCollection = pair.Value;
            foreach (var child in childCollection)
            {
              var id = child.Identifier;
              child.Dispose();
              session._cachedAnchors.Remove(id);
            }
          }
        }
      );
    }

    /// <summary>
    /// Destroys the list of anchors received from native without generating objects.
    /// </summary>
    /// <param name="anchorsPtrs">List of native handles to anchors</param>
    /// <param name="anchorsCount">Number of anchors in the list</param>
    private static void DestroyAnchors
    (
      IntPtr anchorsPtrs,
      UInt64 anchorsCount
    )
    {
      ARLog._DebugFormat
      (
        "Releasing {0} native anchors directly, the session was released already",
        false,
        anchorsCount
      );

      for (var i = 0; i < (int)anchorsCount; i++)
      {
        var nativeHandle = Marshal.ReadIntPtr(anchorsPtrs, i * IntPtr.Size);
        _NativeARAnchor._ReleaseImmediate(nativeHandle);
      }
    }

    [MonoPInvokeCallback(typeof(_ARSession_Map_Callback))]
    private static void _onDidAddMapsNative
    (
      IntPtr context,
      IntPtr mapPtrs,
      UInt64 mapsCount
    )
    {
      var session = SafeGCHandle.TryGetInstance<_NativeARSession>(context);
      if (session == null || session.IsDestroyed)
      {
        // session was deallocated
        DestroyMaps(mapPtrs, mapsCount);
        return;
      }

      var maps = new IARMap[mapsCount];

      for (var i = 0; i < (int)mapsCount; i++)
      {
        var nativeHandle = Marshal.ReadIntPtr(mapPtrs, i * IntPtr.Size);
        maps[i] = _NativeARMap._FromNativeHandle(nativeHandle, session.WorldScale);
      }

      _CallbackQueue.QueueCallback
      (
        () =>
        {
          if (session.IsDestroyed)
          {
            // session was deallocated
            for (var i = 0; i < (int)mapsCount; i++)
              maps[i].Dispose();

            return;
          }

          ARLog._Debug("Surfacing added maps");
          var handler = session._mapsAdded;
          if (handler != null)
          {
            var args = new MapsArgs(maps);
            handler(args);
          }
        }
      );
    }

    [MonoPInvokeCallback(typeof(_ARSession_Map_Callback))]
    private static void _onDidUpdateMapsNative
    (
      IntPtr context,
      IntPtr mapPtrs,
      UInt64 mapsCount
    )
    {
      var session = SafeGCHandle.TryGetInstance<_NativeARSession>(context);
      if (session == null || session.IsDestroyed)
      {
        // session was deallocated
        DestroyMaps(mapPtrs, mapsCount);
        return;
      }

      var maps = new IARMap[mapsCount];

      for (var i = 0; i < (int)mapsCount; i++)
      {
        var nativeHandle = Marshal.ReadIntPtr(mapPtrs, i * IntPtr.Size);
        maps[i] = _NativeARMap._FromNativeHandle(nativeHandle, session.WorldScale);
      }

      _CallbackQueue.QueueCallback
      (
        () =>
        {
          if (session.IsDestroyed)
          {
            // session was deallocated
            for (var i = 0; i < (int)mapsCount; i++)
              maps[i].Dispose();

            return;
          }

          ARLog._Debug("Surfacing updated maps");
          var handler = session._mapsUpdated;
          if (handler != null)
          {
            var args = new MapsArgs(maps);
            handler(args);
          }
        }
      );
    }

    private static void DestroyMaps(IntPtr mapPtrs, UInt64 mapsCount, float worldScale = 1f)
    {
      ARLog._DebugFormat
      (
        "Releasing {0} maps directly, the session was released already",
        false,
        mapsCount
      );

      for (var i = 0; i < (int)mapsCount; i++)
      {
        var nativeHandle = Marshal.ReadIntPtr(mapPtrs, i * IntPtr.Size);
        _NativeARMap._ReleaseImmediate(nativeHandle);
      }
    }

    [MonoPInvokeCallback(typeof(_ARSession_Mesh_Callback))]
    private static void _onDidUpdateMeshNative(IntPtr context, IntPtr meshPtr)
    {
      var meshData = new _NativeARMeshData(meshPtr);
      var session = SafeGCHandle.TryGetInstance<_NativeARSession>(context);
      if (session == null || session.IsDestroyed)
      {
        // session was deallocated
        meshData.Dispose();
        return;
      }

      _CallbackQueue.QueueCallback
      (
        () =>
        {
          if (session.IsDestroyed)
          {
            // session was deallocated
            meshData.Dispose();
            return;
          }

          session._meshDataParser.ParseMesh(meshData);
          meshData.Dispose();
        }
      );
    }

    [MonoPInvokeCallback(typeof(_ARSession_Camera_Callback))]
    private static void _onCameraDidChangeTrackingStateNative(IntPtr context, IntPtr cameraPtr)
    {
      var session = SafeGCHandle.TryGetInstance<_NativeARSession>(context);
      if (session == null || session.IsDestroyed)
      {
        // session was deallocated
        _NativeARCamera._ReleaseImmediate(cameraPtr);
        return;
      }

      _CallbackQueue.QueueCallback
      (
        () =>
        {
          if (session.IsDestroyed)
          {
            // session was deallocated
            _NativeARCamera._ReleaseImmediate(cameraPtr);
            return;
          }

          // Using a constructor here instead of caching + reusing objects in _NativeARCamera._FromNativeHandle
          //  We are disposing the camera every frame to prevent a crash on exit. Reintroduce caching
          //  in a way that supports disposing.
          var camera = new _NativeARCamera(cameraPtr, session.WorldScale);

          ARLog._DebugFormat("Surfacing camera tracking state: {0}", false, camera.TrackingState);
          var handler = session._cameraTrackingStateChanged;
          if (handler != null)
          {
            var args = new CameraTrackingStateChangedArgs(camera, camera.TrackingState);
            handler(args);
          }
        }
      );
    }

    [MonoPInvokeCallback(typeof(_ARSession_Void_Callback))]
    private static void _onSessionWasInterruptedNative(IntPtr context)
    {
      var session = SafeGCHandle.TryGetInstance<_NativeARSession>(context);
      if (session == null || session.IsDestroyed)
      {
        // session was deallocated
        return;
      }

      _CallbackQueue.QueueCallback
      (
        () =>
        {
          if (session.IsDestroyed)
          {
            // session was deallocated
            return;
          }

          ARLog._Debug("Surfacing session was interrupted");
          session._sessionInterrupted(new ARSessionInterruptedArgs());
        }
      );
    }

    [MonoPInvokeCallback(typeof(_ARSession_Void_Callback))]
    private static void _onSessionInterruptionEndedNative(IntPtr context)
    {
      var session = SafeGCHandle.TryGetInstance<_NativeARSession>(context);
      if (session == null || session.IsDestroyed)
      {
        // session was deallocated
        return;
      }

      _CallbackQueue.QueueCallback
      (
        () =>
        {
          if (session.IsDestroyed)
          {
            // session was deallocated
            return;
          }

          ARLog._Debug("Surfacing session interruption ended");
          session._sessionInterruptionEnded(new ARSessionInterruptionEndedArgs());
        }
      );
    }

    [MonoPInvokeCallback(typeof(_ARSession_Bool_Callback))]
    private static bool _onSessionShouldAttemptRelocalizationNative(IntPtr context)
    {
      var session = SafeGCHandle.TryGetInstance<_NativeARSession>(context);
      if (session == null || session.IsDestroyed)
      {
        // session was deallocated
        return false;
      }

      var handlers = session._queryingShouldSessionAttemptRelocalization;
      if (handlers.Count == 0)
        return false;

      var args = new QueryingShouldSessionAttemptRelocalizationArgs();
      foreach(var handler in handlers)
      {
        handler(args);

        ARLog._Debug("Surfacing session should attempt relocalization");
        if (args.ShouldSessionAttemptRelocalization)
          return true;
      }

      return false;
    }

    [MonoPInvokeCallback(typeof(_ARSession_Failed_Callback))]
    private static void _onSessionDidFailWithErrorNative(IntPtr context, UInt64 errorNo)
    {
      var session = SafeGCHandle.TryGetInstance<_NativeARSession>(context);
      if (session == null || session.IsDestroyed)
      {
        // session was deallocated
        return;
      }

      var error = (ARError)errorNo;

      _CallbackQueue.QueueCallback
      (
        () =>
        {
          if (session.IsDestroyed)
          {
            // session was deallocated
            return;
          }

          session.State = ARSessionState.Failed;
          ARLog._DebugFormat("Surfacing session failed with error: {0}", false, error);
          var args = new ARSessionFailedArgs(error);
          session._sessionFailed(args);
        }
      );
    }
#endregion
#endregion

#region TestingShim

    internal static class _TestingShim
    {
#pragma warning disable 0162
      // If the session has already been disposed, this method will incidentally recreate the
      // session's _handle. To avoid this, use _GetHandle and the _InvokeDidReceiveFrame override
      // that takes an IntPtr instead of a _NativeARSession.
      internal static void _InvokeDidReceiveFrame(_NativeARSession session, IntPtr framePtr)
      {
        _InvokeDidReceiveFrame(session._handle, framePtr);
      }

      internal static void _InvokeDidReceiveFrame(IntPtr sessionPtr, IntPtr framePtr)
      {
        if (_NativeAccess.Mode == _NativeAccess.ModeType.Testing)
          _onDidUpdateFrameNative(sessionPtr, framePtr);
      }

      internal static IntPtr _GetHandle(_NativeARSession session)
      {
        return session._handle;
      }
#pragma warning restore 0162
    }

#endregion

    [DllImport(_ARDKLibrary.libraryName)]
    private static extern IntPtr _NARSession_Init(byte[] stageIdentifier);

    [DllImport(_ARDKLibrary.libraryName)]
    private static extern IntPtr _NARPlaybackSession_Init(byte[] stageIdentifier);

    [DllImport(_ARDKLibrary.libraryName)]
    private static extern void _NARSession_Release(IntPtr nativeSession);

    [DllImport(_ARDKLibrary.libraryName)]
    private static extern void _NARSession_Run
    (
      IntPtr nativeSession,
      IntPtr nativeConfig,
      UInt64 options
    );

    [DllImport(_ARDKLibrary.libraryName)]
    private static extern void _NARSession_Pause(IntPtr nativeSession);
    
    [DllImport(_ARDKLibrary.libraryName)]
    private static extern void _NARSession_MarkFrameConsumed(IntPtr nativeSession, IntPtr nativeFrame);

    [DllImport(_ARDKLibrary.libraryName)]
    private static extern IntPtr GetRenderEventFunc();

    [DllImport(_ARDKLibrary.libraryName)]
    private static extern void _NARSession_AddAnchor(IntPtr nativeSession, IntPtr nativeAnchor);

    [DllImport(_ARDKLibrary.libraryName)]
    private static extern void _NARSession_RemoveAnchor(IntPtr nativeSession, IntPtr nativeAnchor);

    [DllImport(_ARDKLibrary.libraryName)]
    private static extern UInt32 _NARSession_GetAwarenessFeaturesError(IntPtr nativeSession);

    [DllImport(_ARDKLibrary.libraryName)]
    private static extern IntPtr _NARSession_GetAwarenessFeaturesErrorMessage(IntPtr nativeSession);

    [DllImport(_ARDKLibrary.libraryName)]
    private static extern void _NARSession_Set_didUpdateFrameCallback
    (
      IntPtr applicationSession,
      IntPtr platformSession,
      _ARSession_Frame_Callback callback
    );

    [DllImport(_ARDKLibrary.libraryName)]
    private static extern void _NARSession_Set_didUpdateMeshCallback
    (
      IntPtr applicationSession,
      IntPtr platformSession,
      _ARSession_Mesh_Callback callback
    );

    [DllImport(_ARDKLibrary.libraryName)]
    private static extern void _NARSession_Set_didAddAnchorsCallback
    (
      IntPtr applicationSession,
      IntPtr platformSession,
      _ARSession_Anchor_Callback callback
    );

    [DllImport(_ARDKLibrary.libraryName)]
    private static extern void _NARSession_Set_didUpdateAnchorsCallback
    (
      IntPtr applicationSession,
      IntPtr platformSession,
      _ARSession_Anchor_Callback callback
    );

    [DllImport(_ARDKLibrary.libraryName)]
    private static extern void _NARSession_Set_didRemoveAnchorsCallback
    (
      IntPtr applicationSession,
      IntPtr platformSession,
      _ARSession_Anchor_Callback callback
    );

    [DllImport(_ARDKLibrary.libraryName)]
    private static extern void _NARSession_Set_didMergeAnchorsCallback
    (
      IntPtr applicationSession,
      IntPtr platformSession,
      _ARSession_Merge_Anchor_Callback callback
    );

    [DllImport(_ARDKLibrary.libraryName)]
    private static extern void _NARSession_Set_didAddMapsCallback
    (
      IntPtr applicationSession,
      IntPtr platformSession,
      _ARSession_Map_Callback callback
    );

    [DllImport(_ARDKLibrary.libraryName)]
    private static extern void _NARSession_Set_didUpdateMapsCallback
    (
      IntPtr applicationSession,
      IntPtr platformSession,
      _ARSession_Map_Callback callback
    );

    [DllImport(_ARDKLibrary.libraryName)]
    private static extern void _NARSession_Set_cameraDidChangeTrackingStateCallback
    (
      IntPtr applicationSession,
      IntPtr platformSession,
      _ARSession_Frame_Callback callback
    );

    [DllImport(_ARDKLibrary.libraryName)]
    private static extern void _NARSession_Set_wasInterruptedCallback
    (
      IntPtr applicationSession,
      IntPtr platformSession,
      _ARSession_Void_Callback callback
    );

    [DllImport(_ARDKLibrary.libraryName)]
    private static extern void _NARSession_Set_interruptionEndedCallback
    (
      IntPtr applicationSession,
      IntPtr platformSession,
      _ARSession_Void_Callback callback
    );

    [DllImport(_ARDKLibrary.libraryName)]
    private static extern void _NARSession_Set_shouldAttemptRelocalizationCallback
    (
      IntPtr applicationSession,
      IntPtr platformSession,
      _ARSession_Bool_Callback callback
    );

    [DllImport(_ARDKLibrary.libraryName)]
    private static extern void _NARSession_Set_didFailWithErrorCallback
    (
      IntPtr applicationSession,
      IntPtr platformSession,
      _ARSession_Failed_Callback callback
    );

    private delegate void _ARSession_Frame_Callback(IntPtr context, IntPtr frame);

    private delegate void _ARSession_Mesh_Callback(IntPtr context, IntPtr mesh);

    private delegate void _ARSession_Failed_Callback(IntPtr context, UInt64 error);

    private delegate void _ARSession_Void_Callback(IntPtr context);

    private delegate void _ARSession_Camera_Callback(IntPtr context, IntPtr camera);

    private unsafe delegate void _ARSession_Merge_Anchor_Callback
    (
      IntPtr context,
      ArrayOfMergeInfo* anchorsPtrs
    );

    private delegate void _ARSession_Anchor_Callback
    (
      IntPtr context,
      IntPtr nativeAnchor,
      UInt64 anchorsCount
    );

    private delegate void _ARSession_Map_Callback
    (
      IntPtr context,
      IntPtr nativeMap,
      UInt64 mapsCount
    );

    private delegate bool _ARSession_Bool_Callback(IntPtr context);
  }
}
