using System;
using System.Linq;
using Moodium;
using Moodium.Interaction;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEditor.XR.ARSubsystems;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public static class MoodiumStageTwoSetup
{
    const string ScenePath = "Assets/Samples/PolySpatial/Scenes/Meshing 1.unity";
    const string ReferenceObjectPath = "Assets/Model/tracking/tissue.referenceobject";
    const string LibraryPath = "Assets/Model/tracking/TissueReferenceObjectLibrary.asset";
    const string FbxPath = "Assets/Model/Chocolate_Capsule.fbx";
    const string PrefabPath = "Assets/prefab/Chocolate_Capsule.prefab";
    const string ControllerPath = "Assets/Animations/ChocolateCapsule.controller";

    [MenuItem("Moodium/Configure Stage 2")]
    public static void Configure()
    {
        EnsureFolder("Assets/Animations");
        var library = CreateReferenceObjectLibrary();
        var controller = CreateAnimatorController();
        var prefab = ConfigureChocolatePrefab(controller);
        ConfigureScene(library, prefab);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("MOODIUM_STAGE_TWO_SETUP_COMPLETE");
    }

    static XRReferenceObjectLibrary CreateReferenceObjectLibrary()
    {
        var library = AssetDatabase.LoadAssetAtPath<XRReferenceObjectLibrary>(LibraryPath);
        if (library == null)
        {
            library = ScriptableObject.CreateInstance<XRReferenceObjectLibrary>();
            AssetDatabase.CreateAsset(library, LibraryPath);
        }

        while (library.count > 0)
            library.RemoveAt(library.count - 1);

        var entry = AssetDatabase.LoadAllAssetsAtPath(ReferenceObjectPath)
            .OfType<XRReferenceObjectEntry>()
            .FirstOrDefault();
        if (entry == null)
            throw new InvalidOperationException("No visionOS reference object entry was imported.");

        var index = library.Add();
        library.SetReferenceObjectName(index, "Tissue");
        library.SetReferenceObjectEntry(index, entry.GetType(), entry);
        EditorUtility.SetDirty(library);
        return library;
    }

    static RuntimeAnimatorController CreateAnimatorController()
    {
        var clips = AssetDatabase.LoadAllAssetsAtPath(FbxPath).OfType<AnimationClip>().ToArray();
        var clip = clips.FirstOrDefault(c => c.name == "ChocolateCapsule|ChocolateCrack")
            ?? clips.FirstOrDefault(c => c.name.EndsWith("|ChocolateCrack", StringComparison.Ordinal))
            ?? clips.FirstOrDefault(c => c.name.Contains("ChocolateCrack"));

        if (clip == null)
            throw new InvalidOperationException("ChocolateCrack AnimationClip was not found in the FBX.");

        AssetDatabase.DeleteAsset(ControllerPath);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        var stateMachine = controller.layers[0].stateMachine;
        var state = stateMachine.AddState("ChocolateCrack");
        state.motion = clip;
        state.writeDefaultValues = true;
        stateMachine.defaultState = state;
        EditorUtility.SetDirty(controller);
        Debug.Log($"MOODIUM_CRACK_CLIP={clip.name}; length={clip.length:F3}s");
        return controller;
    }

    static GameObject ConfigureChocolatePrefab(RuntimeAnimatorController controller)
    {
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var animator = root.GetComponentInChildren<Animator>(true);
            if (animator == null)
            {
                var animationRoot = root.transform.childCount > 0
                    ? root.transform.GetChild(0).gameObject
                    : root;
                animator = animationRoot.AddComponent<Animator>();
            }

            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            var interaction = root.GetComponent<ChocolateCapsuleInteraction>();
            if (interaction == null)
                interaction = root.AddComponent<ChocolateCapsuleInteraction>();
            interaction.SetInteractionEnabled(false);
            interaction.SetSpatialPointerEnabled(true);

            var oldPinch = root.GetComponent<ChocolatePinchCrackController>();
            if (oldPinch != null)
                UnityEngine.Object.DestroyImmediate(oldPinch);

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        return AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
    }

    static void ConfigureScene(XRReferenceObjectLibrary library, GameObject prefab)
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var origin = UnityEngine.Object.FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
        if (origin == null)
            throw new InvalidOperationException("XR Origin was not found in Meshing 1.");

        var trackedObjectManager = origin.GetComponent<ARTrackedObjectManager>();
        if (trackedObjectManager == null)
            trackedObjectManager = origin.gameObject.AddComponent<ARTrackedObjectManager>();
        trackedObjectManager.referenceLibrary = library;
        trackedObjectManager.enabled = true;

        var managerObject = scene.GetRootGameObjects().FirstOrDefault(go => go.name == "Manager");
        if (managerObject == null)
            managerObject = new GameObject("Manager");

        var spawner = managerObject.GetComponent<TissueObjectTrackingSpawner>();
        if (spawner == null)
            spawner = managerObject.AddComponent<TissueObjectTrackingSpawner>();
        spawner.Configure(trackedObjectManager, prefab);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;

        var slash = path.LastIndexOf('/');
        var parent = path.Substring(0, slash);
        var name = path.Substring(slash + 1);
        AssetDatabase.CreateFolder(parent, name);
    }
}
