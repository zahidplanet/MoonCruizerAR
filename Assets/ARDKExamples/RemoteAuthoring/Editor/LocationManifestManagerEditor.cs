using System;
using System.Collections.Generic;
using Niantic.ARDK.AR.WayspotAnchors;
using Niantic.ARDKExamples.RemoteAuthoring;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Niantic.ARDKExamples.RemoteAuthoring.Editor
{
    [CustomEditor(typeof(LocationManifestManager))]
    public class LocationManifestManagerEditor : UnityEditor.Editor
    {
        private const string USE_PREFABS_KEY = "UsePrefabs";

        private bool prefabSelection = true;

        private SerializedProperty spManifests;
        private SerializedProperty spAnchoredContent;
        private void OnEnable()
        {
            prefabSelection = EditorPrefs.GetBool(USE_PREFABS_KEY, true);
            spManifests = serializedObject.FindProperty("_manifests");
            spAnchoredContent = serializedObject.FindProperty("_anchoredContent");
        }

        public override void OnInspectorGUI()
        {
            // This gets the current values from all serialized fields into the serialized "clone"
            serializedObject.Update();
            
            DropAreaGUI ();
            EditorGUILayout.Space ();
            var soEditor = new SerializedObject(this);
            GUI.enabled = false;
            if (EditorGUILayout.PropertyField(spManifests, true)) {
                soEditor.ApplyModifiedProperties();
            }
            GUI.enabled = true;
        
            DrawDefaultInspector();

            EditorGUILayout.Space ();
            GUILayout.Label("Step 2: After adding in Manifests, Populate Anchors in build: ");
            EditorGUILayout.BeginHorizontal();
            //GUILayout.FlexibleSpace();
            
            var locationManifestManager = target as LocationManifestManager;
            var guiStyle = EditorStyles.toggle;
            guiStyle.alignment = TextAnchor.MiddleLeft;
            prefabSelection = GUILayout.Toggle(prefabSelection,
                new GUIContent("Include Associated Prefabs",
                    "Populated anchors should use associated prefab content from the Remote Authoring Assistant"), guiStyle);
            EditorPrefs.SetBool(USE_PREFABS_KEY, prefabSelection);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            
            if (GUILayout.Button(new GUIContent("Populate Anchors", "Press this to populate anchors in the manager")))
            {
                locationManifestManager.PopulateAnchoredContent(prefabSelection);
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Clear Manifests and Anchor Content"))
            {
                //Remove All Content;
                spManifests.arraySize = 0;
                spAnchoredContent.arraySize = 0;
                serializedObject.ApplyModifiedProperties();
            }
            EditorGUILayout.EndHorizontal();
            
        }
    
        private void DropAreaGUI ()
        {
            var locationManifestManager = target as LocationManifestManager;
            Event evt = Event.current;
            Rect droppingArea = GUILayoutUtility.GetRect (0.0f, 50.0f, GUILayout.ExpandWidth (true));
            GUI.Box (droppingArea, "Step 1: Drag Manifests (or JSON Manifest) Here");
     
            switch (evt.type) {
                case EventType.DragUpdated:
                case EventType.DragPerform:
                    if (!droppingArea.Contains (evt.mousePosition))
                        return;
             
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
         
                    if (evt.type == EventType.DragPerform) {
                        DragAndDrop.AcceptDrag ();
             
                        if(DragAndDrop.objectReferences != null) {
                            locationManifestManager.Manifests = 
                                AddContentAsManifests(DragAndDrop.objectReferences, locationManifestManager.Manifests);
                            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                        }
                    }
                    break;
            }
        }
        public TinyVPSLocationManifest[] AddContentAsManifests(Object[] draggedContent, TinyVPSLocationManifest[] existingManifests)
        {
            List<TinyVPSLocationManifest> manifestList = new List<TinyVPSLocationManifest>();
            if (existingManifests != null)
            {
                foreach (var vpsLocationManifest in existingManifests)
                {
                    if (vpsLocationManifest != null)
                    {
                        AddManifest(ref manifestList, vpsLocationManifest);
                    }
                }
            }

            foreach (var obj in draggedContent)
            {
                if (obj is VPSLocationManifest)
                {
                    VPSLocationManifest manifest = obj as VPSLocationManifest;
                    TinyVPSLocationManifest tinyManifest = new TinyVPSLocationManifest(manifest);
                    AddManifest(ref manifestList, tinyManifest);
                }
                else if (obj is TextAsset)
                {
                    var jsonString = (obj as TextAsset).text;
                    var manifest = JsonUtility.FromJson<TinyVPSLocationManifest>(jsonString);
                    AddManifest(ref manifestList, manifest);
                }
                else{ Debug.LogError("object " + obj.name + "is not a supported VPS Manifest");}
            }

            return manifestList.ToArray();
        }

        private void AddManifest(ref List<TinyVPSLocationManifest> manifestList, TinyVPSLocationManifest manifest)
        {
            if (manifestList.FindIndex(f => f.LocationName == manifest.LocationName) < 0)
            {
                manifestList.Add(manifest);
            }
            else
            {
                Debug.LogWarning("Duplicate detected");
            }
        }
    }
}