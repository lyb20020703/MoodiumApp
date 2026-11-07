using System.Linq;
using Moodium.Opening;
using Moodium.Reality;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Builds the production Moodi prefab and a visible, interactive proof scene.</summary>
public static class MoodiEmergencePreviewSetup
{
    const string SourcePath = "Assets/Model/Moodi_V2.fbx";
    const string RuntimePrefabPath = "Assets/prefab/MoodiEmergence.prefab";
    const string ConfigPath = "Assets/Resources/MoodiumOpening/OpeningVideoSequenceConfig.asset";
    const string ScenePath = "Assets/Scenes/Moodium Orientation Preview.unity";
    const string CandyPath = "Assets/prefab/CandySoft.prefab";
    const string NaturePath = "Assets/prefab/NatureSoft.prefab";

    [MenuItem("Moodium/Build Moodi Emergence Preview")]
    public static void Build()
    {
        var moodiPrefab = BuildRuntimePrefab();
        var clip = FindEmergenceClip();
        AssignRuntimeAssets(moodiPrefab, clip);
        BuildPreviewScene(moodiPrefab, clip);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[Moodi Emergence] Ready. prefab={RuntimePrefabPath}; clip={clip.name}; preview={ScenePath}");
    }

    static GameObject BuildRuntimePrefab()
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePath);
        if (source == null)
            throw new System.InvalidOperationException($"Moodi source model is missing: {SourcePath}");

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
        try
        {
            instance.name = "MoodiEmergence";
            var animator = instance.GetComponentInChildren<Animator>(true) ?? instance.AddComponent<Animator>();
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            return PrefabUtility.SaveAsPrefabAsset(instance, RuntimePrefabPath);
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    static AnimationClip FindEmergenceClip()
    {
        var clip = AssetDatabase.LoadAllAssetsAtPath(SourcePath).OfType<AnimationClip>()
            .FirstOrDefault(item => item.name == "Moodi_Reference_Performance_InPlace")
            ?? AssetDatabase.LoadAllAssetsAtPath(SourcePath).OfType<AnimationClip>()
                .FirstOrDefault(item => item.name.Contains("Moodi_Reference_Performance"));
        if (clip == null)
            throw new System.InvalidOperationException("Moodi authored emergence clip was not found in Moodi_V2.fbx.");
        return clip;
    }

    static void AssignRuntimeAssets(GameObject moodiPrefab, AnimationClip clip)
    {
        var config = AssetDatabase.LoadAssetAtPath<OpeningVideoSequenceConfig>(ConfigPath);
        if (config == null)
            throw new System.InvalidOperationException($"Opening configuration is missing: {ConfigPath}");
        var serialized = new SerializedObject(config);
        serialized.FindProperty("m_MoodiEmergencePrefab").objectReferenceValue = moodiPrefab;
        serialized.FindProperty("m_MoodiEmergenceClip").objectReferenceValue = clip;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(config);
    }

    static void BuildPreviewScene(GameObject moodiPrefab, AnimationClip clip)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var preview = new GameObject("Moodi Emergence Preview (Press Play)").AddComponent<MoodiEmergencePreviewController>();
        preview.Configure(
            AssetDatabase.LoadAssetAtPath<GameObject>(CandyPath),
            AssetDatabase.LoadAssetAtPath<GameObject>(NaturePath),
            moodiPrefab,
            clip,
            .01f);

        // Keep an actual sampled model in the saved scene as well. The runtime
        // slider demonstrates progress, while this reference makes the authored
        // 50% pose inspectable without entering Play mode.
        CreateVisiblePoseReference(moodiPrefab, clip, AssetDatabase.LoadAssetAtPath<GameObject>(CandyPath));

        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Preview Table Plane";
        ground.transform.localScale = Vector3.one * .12f;
        ground.GetComponent<Renderer>().sharedMaterial = CreateMaterial(new Color(.08f, .09f, .13f));
        Object.DestroyImmediate(ground.GetComponent<Collider>());

        var light = new GameObject("Preview Key Light").AddComponent<Light>();
        light.type = LightType.Directional;
        light.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
        light.intensity = 1.4f;

        var camera = new GameObject("Preview Camera").AddComponent<Camera>();
        camera.transform.position = new Vector3(0f, .34f, -.82f);
        camera.transform.LookAt(new Vector3(0f, 0f, .03f));
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.025f, .03f, .05f);
        camera.nearClipPlane = .01f;

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        // Opening the saved scene unloads the object reference from the scene we
        // just constructed. Resolve it again from the newly opened scene.
        Selection.activeGameObject = GameObject.Find("Moodi Emergence Preview (Press Play)");
    }

    static Material CreateMaterial(Color color)
    {
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.color = color;
        return material;
    }

    static void CreateVisiblePoseReference(GameObject moodiPrefab, AnimationClip clip, GameObject softPrefab)
    {
        var root = new GameObject("Moodi Emergence — Visible 50% Reference").transform;
        root.position = new Vector3(0f, 0.015f, .18f);

        if (softPrefab != null)
        {
            var soft = (GameObject)PrefabUtility.InstantiatePrefab(softPrefab);
            soft.name = "CandySoft — Emergence Container Reference";
            soft.transform.SetParent(root, false);
            // Keep the reference in the exact authored prefab orientation, matching
            // the production VisualRoot contract.
            soft.transform.localRotation = Quaternion.identity;
            soft.transform.localScale = Vector3.one * .12f;
        }

        var moodi = (GameObject)PrefabUtility.InstantiatePrefab(moodiPrefab);
        moodi.name = "Moodi_V2 Emergence — Sampled 50% Pose";
        moodi.transform.SetParent(root, false);
        moodi.transform.localRotation = MoodiEmergenceProgress.AuthoredLocalRotation;
        moodi.transform.localScale = Vector3.one * .01f;
        clip.SampleAnimation(moodi, clip.length * .5f);
    }
}
