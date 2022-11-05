using System.Linq;
using System.Threading.Tasks;

using Niantic.ARDK.Utilities.Logging;

using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;

using UnityEngine;
using UnityEngine.SceneManagement;

using RemoteAuthoringAssistant = Niantic.ARDK.AR.WayspotAnchors.EditModeOnlyBehaviour.RemoteAuthoringAssistant;

namespace Niantic.ARDK.AR.WayspotAnchors.Editor
{
  [InitializeOnLoad]
  internal class _RemoteAuthoringPresenceManager: IPreprocessBuildWithReport, IPostprocessBuildWithReport
  {
    private static _IContentVisualizer[] _visualizers;

    private const string SCENE_NAME = "Remote Authoring (Editor Only)";
    private const string INIT_KEY = "ARDK_RA_Initialized";

    private static Scene GetAuthoringScene()
    {
      return EditorSceneManager.GetSceneByName(SCENE_NAME);
    }

    static _RemoteAuthoringPresenceManager()
    {
      _visualizers =
        new _IContentVisualizer[]
        {
          new _WayspotMeshVisualizer(),
          new _AnchorPrefabVisualizer()
        };

      EditorSceneManager.sceneOpened += OnSceneOpened;
      EditorSceneManager.sceneClosing += OnSceneClosing;

      EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

      RemoteAuthoringAssistant.ActiveManifestChanged += UpdateDisplay;

      BuildPlayerWindow.RegisterBuildPlayerHandler(OnBuild);

      // Have to delay call because no scenes are loaded on Editor launch frame
      EditorApplication.delayCall += () =>
      {
        if (!SessionState.GetBool(INIT_KEY, false))
        {
          SessionState.SetBool(INIT_KEY, true);
          PausePresence(true);
        }
      };
    }

    public static T GetVisualizer<T>() where T: _IContentVisualizer
    {
      return (T)_visualizers.FirstOrDefault(v => v.GetType() == typeof(T));
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange change)
    {
      switch (change)
      {
        case PlayModeStateChange.EnteredEditMode:
          ReinstatePresence();
          break;
        case PlayModeStateChange.ExitingEditMode:
          PausePresence();
          break;

        case PlayModeStateChange.EnteredPlayMode:
          break;

        case PlayModeStateChange.ExitingPlayMode:
          break;
      }
    }

    private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
    {
      if (_isBuilding)
        return;

      if (scene.name != SCENE_NAME)
      {
        if (!GetAuthoringScene().isLoaded)
          ReinstatePresence();
      }
      else
      {
        _HierarchyMonitor.Enable(SceneManager.GetActiveScene());
      }
    }

    private static void OnSceneClosing(Scene scene, bool removingScene)
    {
      if (scene.name == SCENE_NAME)
        _HierarchyMonitor.Disable();
    }

    // Must check for and unload Authoring scene before build, because leaving it be results in it
    // being reloaded with missing scripts after the build completes. Must be done in this method
    // instead of OnPreprocessBuild, because scene will not fully unload(?) in latter
    private static void OnBuild(BuildPlayerOptions options)
    {
      var scene = GetAuthoringScene();
      if (scene.isLoaded)
      {
        _HierarchyMonitor.Disable();

        var asyncOperation = SceneManager.UnloadSceneAsync(scene);
        asyncOperation.completed += (o) => BuildPlayerWindow.DefaultBuildMethods.BuildPlayer(options);
      }
      else
      {
        BuildPlayerWindow.DefaultBuildMethods.BuildPlayer(options);
      }
    }

    private static bool _isBuilding;
    public int callbackOrder { get; }
    public void OnPreprocessBuild(BuildReport report)
    {
      _isBuilding = true;
      WaitForBuildCompletion(report);
    }

    static async void WaitForBuildCompletion(BuildReport report)
    {
      while (BuildPipeline.isBuildingPlayer)
      {
        //some arbitrary about of time meanwhile we wait for the build to complete, can't be bothered to find a better solution.
        await Task.Delay(1000);
      }

      ReinstatePresence();
    }

    public void OnPostprocessBuild(BuildReport report)
    {
      _isBuilding = false;

      // Cannot call ReinstatePresence here, because this callback is invoked
      // when scenes still haven't been reloaded
    }

    [MenuItem("Lightship/ARDK/Remote Authoring Assistant/Open", false, 0)]
    public static void AddPresence()
    {
      var authoringScene = GetAuthoringScene();

      if (authoringScene.isLoaded)
      {
        ARLog._WarnRelease
        (
          "Remote Authoring is already active. To refresh the state of the " +
          "Remote Authoring Assistant, first close (Lightship > ARDK > Remote Authoring Assistant > Close)" +
          "and then re-open the assistant."
        );

        return;
      }

      // Add scene tag, avoiding marking the current scene as dirty if it's not already
      var activeScene = SceneManager.GetActiveScene();
      var activeSceneIsDirty = activeScene.isDirty;

      var sceneTagCount = GameObject.FindObjectsOfType<_RemoteAuthoringSceneTag>().Length;
      if (sceneTagCount == 0)
      {
        var sceneTag = new GameObject("RemoteAuthoringSceneTag");
        sceneTag.AddComponent<_RemoteAuthoringSceneTag>();
      }

      if (!activeSceneIsDirty)
        EditorSceneManager.SaveScene(activeScene);

      // Case where scene has been unloaded but not removed. This happens if dev manually unloads
      // scene, and sometimes when exiting Play Mode.
      if (authoringScene.IsValid())
      {
        // Need to close scene, because scene has not been saved as an asset and thus cannot be
        // opened.
        EditorSceneManager.CloseScene(authoringScene, true);
      }

      authoringScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
      RemoteAuthoringAssistant._Create(authoringScene);
      EditorSceneManager.SaveScene(authoringScene, $"{Application.temporaryCachePath}/{SCENE_NAME}.unity");
      SceneManager.SetActiveScene(activeScene);

      EditorApplication.delayCall += () => _HierarchyMonitor.Enable(activeScene);
    }

    [MenuItem("Lightship/ARDK/Remote Authoring Assistant/Close", false, 0)]
    public static void RemovePresence()
    {
      PausePresence();

      var sceneTag = GameObject.FindObjectOfType<_RemoteAuthoringSceneTag>();
      if (sceneTag != null)
        GameObject.DestroyImmediate(sceneTag.gameObject);

      var activeScene = SceneManager.GetActiveScene();
      var activeSceneIsDirty = activeScene.isDirty;

      if (!activeSceneIsDirty)
        EditorSceneManager.SaveScene(activeScene);
    }

    private static void ReinstatePresence()
    {
      if (GameObject.FindObjectOfType<_RemoteAuthoringSceneTag>() != null)
        AddPresence();
    }

    private static void PausePresence(bool reload = false)
    {
      var scene = GetAuthoringScene();
      if (scene.isLoaded)
      {
        _HierarchyMonitor.Disable();

        var asyncOperation = SceneManager.UnloadSceneAsync(scene);
        if (reload)
          asyncOperation.completed += (o) => AddPresence();
      }

      // Need to set the active scene to one other than the AuthoringScene,
      // else new scene will be opened by AddPresence
      SceneManager.SetActiveScene(SceneManager.GetSceneAt(0));
    }

    private static void UpdateDisplay(VPSLocationManifest prev, VPSLocationManifest curr)
    {
      foreach (var viewer in _visualizers)
        viewer.UpdateDisplay(prev, curr);
    }
  }
}
