using System.Collections.Generic;
using System.IO;
using System.Linq;
using FIMSpace.BonesStimulation;
using Moodium.Flow;
using Moodium.Opening;
using TMPro;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class MoodiumPortalEntranceSetup
{
    const string ScenePath = "Assets/Samples/PolySpatial/Scenes/Meshing 1.unity";
    const string PrefabPath = "Assets/prefab/MoodiumOpening/MoodiumPortalEntrance.prefab";
    const string MaterialFolder = "Assets/Materials/MoodiumPortal";
    const string AnimationFolder = "Assets/Animations/MoodiumPortal";
    const string OcclusionMaterialPath = MaterialFolder + "/MoodiumPortalOcclusion.mat";
    const string EntryMaterialPath = MaterialFolder + "/MoodiumPortalEntry.mat";
    const string MoodiPortalSkinMaterialPath = MaterialFolder + "/MoodiPortalSkin_Emission.mat";
    const string AnimationPath = AnimationFolder + "/MoodiumPortalOpen.anim";
    const string ControllerPath = AnimationFolder + "/MoodiumPortal.controller";
    const string MoodiIdleClipPath = AnimationFolder + "/Moodi_Idle_Blink_Loop.anim";
    const string MoodiControllerPath = AnimationFolder + "/MoodiPortal.controller";
    const string MoodiAnimationSourcePath = "Assets/Model/IP_Unity_3Clips.fbx";
    const string CutSpherePath = "Assets/Samples/PolySpatial/Portal/Meshes/CutSphere.fbx";
    const string SkyboxMaterialPath = "Assets/Samples/PolySpatial/Portal/Materials/Skybox.mat";
    const string CloudPrefabPath = "Assets/prefab/MoodiumCloud.prefab";
    const string DecorativeCloudPrefabPath = "Assets/prefab/Cloud.prefab";
    const string SnowPrefabPath = "Assets/Epic Toon FX/Prefabs/Environment/Weather/Snow/SnowLight.prefab";
    const string RoundedButtonSpritePath = "Assets/UI/Moodium/WristHUD_Rounded.png";

    [MenuItem("Moodium/Setup Portal Entrance")]
    public static void BuildAndConnect()
    {
        EnsureFolder(MaterialFolder);
        EnsureFolder(AnimationFolder);
        var occlusionMaterial = CreateOcclusionMaterial();
        var entryMaterial = CreateEntryMaterial();
        var moodiPortalSkinMaterial = CreateMoodiPortalSkinMaterial();
        var animatorController = CreatePortalAnimation();
        var moodiAnimatorController = CreateMoodiAnimatorController();
        var prefab = CreatePrefab(occlusionMaterial, entryMaterial, moodiPortalSkinMaterial,
            animatorController, moodiAnimatorController);
        ConnectScene(prefab);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Moodium Portal] Entrance prefab built and connected to Meshing 1.unity.");
    }

    static Material CreateOcclusionMaterial()
    {
        var existing = AssetDatabase.LoadAssetAtPath<Material>(OcclusionMaterialPath);
        if (existing != null)
            return existing;
        const string source = "Assets/Samples/PolySpatial/Portal/Materials/OcclusionMat.mat";
        if (!AssetDatabase.CopyAsset(source, OcclusionMaterialPath))
            throw new FileNotFoundException("Could not copy the PolySpatial portal occlusion material.", source);
        return AssetDatabase.LoadAssetAtPath<Material>(OcclusionMaterialPath);
    }

    static Material CreateEntryMaterial()
    {
        var existing = AssetDatabase.LoadAssetAtPath<Material>(EntryMaterialPath);
        if (existing != null)
            return existing;
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var material = new Material(shader) { name = "Moodium Portal Entry" };
        material.color = new Color(0.98f, 0.32f, 0.72f, 1f);
        if (material.HasProperty("_EmissionColor"))
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", new Color(0.55f, 0.08f, 0.35f) * 2f);
        }
        AssetDatabase.CreateAsset(material, EntryMaterialPath);
        return material;
    }

    static Material CreateMoodiPortalSkinMaterial()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(MoodiPortalSkinMaterialPath);
        if (material == null)
        {
            var moodi = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/prefab/Moodi.prefab");
            var source = moodi?.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .FirstOrDefault(renderer => renderer.name.StartsWith("Body"))?.sharedMaterial;
            if (source == null)
                throw new MissingReferenceException("Moodi's original skin material is missing.");
            material = new Material(source) { name = "Moodi Portal Skin Emission" };
            AssetDatabase.CreateAsset(material, MoodiPortalSkinMaterialPath);
        }

        material.EnableKeyword("_EMISSION");
        material.SetColor("_EmissionColor", new Color(0.22f, 0.07f, 0.34f, 1f));
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        return material;
    }

    static AnimatorController CreatePortalAnimation()
    {
        AssetDatabase.DeleteAsset(AnimationPath);
        AssetDatabase.DeleteAsset(ControllerPath);
        var clip = new AnimationClip { name = "MoodiumPortalOpen", frameRate = 60f };
        SetPositionCurve(clip, "PortalPlaneLeft", "m_LocalPosition.x", -0.18f, -1.15f);
        SetPositionCurve(clip, "PortalPlaneRight", "m_LocalPosition.x", 0.18f, 1.15f);
        SetPositionCurve(clip, "PortalPlaneTop", "m_LocalPosition.y", 0.15f, 0.8f);
        SetPositionCurve(clip, "PortalPlaneBottom", "m_LocalPosition.y", -0.15f, -0.8f);
        AssetDatabase.CreateAsset(clip, AnimationPath);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        var state = controller.layers[0].stateMachine.AddState("Open");
        state.motion = clip;
        state.speed = 1f / 1.8f;
        controller.layers[0].stateMachine.defaultState = state;
        return controller;
    }

    static AnimatorController CreateMoodiAnimatorController()
    {
        var idleSource = FindAnimationClip("Idle_Blink");
        var winkClip = FindAnimationClip("Wink");
        if (idleSource == null || winkClip == null)
            throw new MissingReferenceException("Idle_Blink or Wink clip is missing from IP_Unity_3Clips.fbx.");

        AssetDatabase.DeleteAsset(MoodiIdleClipPath);
        var idleLoop = Object.Instantiate(idleSource);
        idleLoop.name = "Moodi_Idle_Blink_Loop";
        AssetDatabase.CreateAsset(idleLoop, MoodiIdleClipPath);
        var idleSettings = AnimationUtility.GetAnimationClipSettings(idleLoop);
        idleSettings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(idleLoop, idleSettings);
        idleLoop.wrapMode = WrapMode.Loop;
        EditorUtility.SetDirty(idleLoop);
        AssetDatabase.SaveAssets();

        AssetDatabase.DeleteAsset(MoodiControllerPath);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(MoodiControllerPath);
        controller.AddParameter("Wink", AnimatorControllerParameterType.Trigger);
        var stateMachine = controller.layers[0].stateMachine;
        var idleState = stateMachine.AddState("Idle_Blink");
        idleState.motion = idleLoop;
        var winkState = stateMachine.AddState("Wink");
        winkState.motion = winkClip;
        stateMachine.defaultState = idleState;

        var toWink = idleState.AddTransition(winkState);
        toWink.hasExitTime = false;
        toWink.duration = 0.05f;
        toWink.AddCondition(AnimatorConditionMode.If, 0f, "Wink");
        var toIdle = winkState.AddTransition(idleState);
        toIdle.hasExitTime = true;
        toIdle.exitTime = 1f;
        toIdle.duration = 0.05f;
        return controller;
    }

    static AnimationClip FindAnimationClip(string name)
    {
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(MoodiAnimationSourcePath))
            if (asset is AnimationClip clip && clip.name == name)
                return clip;
        return null;
    }

    static void SetPositionCurve(AnimationClip clip, string path, string property, float closed, float opened)
    {
        var curve = AnimationCurve.EaseInOut(0f, closed, 1f, opened);
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), property), curve);
    }

    static GameObject CreatePrefab(Material occlusion, Material entryMaterial, Material moodiPortalSkinMaterial,
        RuntimeAnimatorController animatorController,
        RuntimeAnimatorController moodiAnimatorController)
    {
        var root = new GameObject("MoodiumPortalEntrance");
        var visual = new GameObject("PortalVisualRoot");
        visual.transform.SetParent(root.transform, false);
        var animator = visual.AddComponent<Animator>();
        animator.runtimeAnimatorController = animatorController;

        CreateOccluder("PortalPlaneLeft", visual.transform, new Vector3(-1.15f, 0f, 0f), new Vector3(1.15f, 1.65f, 0.05f), occlusion);
        CreateOccluder("PortalPlaneRight", visual.transform, new Vector3(1.15f, 0f, 0f), new Vector3(1.15f, 1.65f, 0.05f), occlusion);
        CreateOccluder("PortalPlaneTop", visual.transform, new Vector3(0f, 0.8f, 0f), new Vector3(2f, 1f, 0.05f), occlusion);
        CreateOccluder("PortalPlaneBottom", visual.transform, new Vector3(0f, -0.8f, 0f), new Vector3(2f, 0.8f, 0.05f), occlusion);

        var content = new GameObject("PortalContentRoot");
        content.transform.SetParent(visual.transform, false);
        content.transform.localPosition = new Vector3(0f, 0f, 0.12f);

        CreateCutSphereBackground(content.transform);
        CreateMoodiumClouds(content.transform);
        CreateDecorativeClouds(content.transform);
        CreatePortalSnow(content.transform);
        CreateBagRain(content.transform);

        var moodiSource = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/prefab/Moodi.prefab");
        if (moodiSource == null)
            throw new MissingReferenceException("Moodi.prefab is missing.");
        var entry = PrefabUtility.InstantiatePrefab(moodiSource) as GameObject;
        if (entry == null)
            throw new MissingReferenceException("Could not instantiate Moodi.prefab.");
        entry.name = "EntryModelRoot_Moodi";
        entry.transform.SetParent(content.transform, false);
        entry.transform.localPosition = new Vector3(0f, 0f, -1f);
        entry.transform.localScale = Vector3.one * 0.08f;
        ApplyMoodiPortalSkin(entry.transform, moodiPortalSkinMaterial);
        var animationRoot = entry.transform.Find("IP_Unity_3Clips");
        if (animationRoot == null)
            throw new MissingReferenceException("Moodi's animation root IP_Unity_3Clips is missing.");
        var entryAnimator = animationRoot.GetComponent<Animator>();
        if (entryAnimator == null)
            entryAnimator = animationRoot.gameObject.AddComponent<Animator>();
        entryAnimator.runtimeAnimatorController = moodiAnimatorController;
        ConfigureMoodiSecondaryMotion(entry.transform);
        var entryCollider = entry.GetComponent<BoxCollider>();
        if (entryCollider == null)
            entryCollider = entry.AddComponent<BoxCollider>();
        FitBoxColliderToRenderers(entry.transform, entryCollider);
        entryCollider.isTrigger = true;
        var pusherObject = new GameObject("MoodiBagPusher");
        pusherObject.transform.SetParent(entry.transform, false);
        pusherObject.transform.localPosition = entryCollider.center;
        var pusherCollider = pusherObject.AddComponent<BoxCollider>();
        pusherCollider.size = Vector3.Scale(entryCollider.size, new Vector3(0.72f, 0.72f, 0.68f));
        pusherCollider.isTrigger = false;
        pusherCollider.enabled = false;
        var pusherBody = pusherObject.AddComponent<Rigidbody>();
        pusherBody.isKinematic = true;
        pusherBody.useGravity = false;
        pusherBody.interpolation = RigidbodyInterpolation.None;
        pusherBody.collisionDetectionMode = CollisionDetectionMode.Discrete;

        var promptObject = new GameObject("EntryPrompt");
        promptObject.transform.SetParent(visual.transform, false);
        promptObject.transform.localPosition = new Vector3(0f, -0.161f, -0.769f);
        promptObject.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        promptObject.transform.localScale = new Vector3(-1f, 1f, 1f);
        var prompt = promptObject.AddComponent<TextMeshPro>();
        prompt.text = "触碰，进入 Moodium 的世界";
        prompt.alignment = TextAlignmentOptions.Center;
        prompt.fontSize = 0.3f;
        prompt.color = Color.white;
        prompt.rectTransform.sizeDelta = new Vector2(1.2f, 0.18f);
        prompt.GetComponent<Renderer>().allowOcclusionWhenDynamic = false;

        var particlesObject = new GameObject("EntryParticles");
        particlesObject.transform.SetParent(content.transform, false);
        var particles = particlesObject.AddComponent<ParticleSystem>();
        var main = particles.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = 0.55f;
        main.startLifetime = 0.45f;
        main.startSpeed = 0.45f;
        main.startSize = 0.025f;
        main.startColor = new Color(1f, 0.48f, 0.82f, 1f);
        var emission = particles.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 28) });
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var controller = root.AddComponent<MoodiumPortalEntranceController>();
        var serialized = new SerializedObject(controller);
        serialized.FindProperty("m_VisualRoot").objectReferenceValue = visual;
        serialized.FindProperty("m_PortalAnimator").objectReferenceValue = animator;
        serialized.FindProperty("m_BagRain").objectReferenceValue = content.GetComponent<PortalFallingBagController>();
        serialized.FindProperty("m_EntryModelRoot").objectReferenceValue = entry.transform;
        serialized.FindProperty("m_EntryAnimator").objectReferenceValue = entryAnimator;
        serialized.FindProperty("m_EntryEmergeDuration").floatValue = 2.4f;
        serialized.FindProperty("m_EntrySwayAmplitude").floatValue = 0.08f;
        serialized.FindProperty("m_EntrySwayCycles").intValue = 2;
        serialized.FindProperty("m_ManualContactCollider").objectReferenceValue = entryCollider;
        serialized.FindProperty("m_EntryBagPusher").objectReferenceValue = pusherCollider;
        serialized.FindProperty("m_EntryPrompt").objectReferenceValue = promptObject;
        serialized.FindProperty("m_MoodiIntroMinimumDuration").floatValue = 3f;
        serialized.FindProperty("m_LanguageButtonRoundedSprite").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<Sprite>(RoundedButtonSpritePath);
        serialized.FindProperty("m_EntryParticles").objectReferenceValue = particles;
        serialized.FindProperty("m_TrailParticleTexture").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Moodium/Textures/MoodiumParticleCircle.png");
        serialized.FindProperty("m_PortalOpenClip").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Music/PortalOpen.wav");
        serialized.FindProperty("m_BagFallingClip").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Music/BagFalling.wav");
        var bagSerialized = new SerializedObject(content.GetComponent<PortalFallingBagController>());
        bagSerialized.FindProperty("m_BagCollisionClip").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Music/Collision.wav");
        bagSerialized.ApplyModifiedPropertiesWithoutUndo();
        serialized.ApplyModifiedPropertiesWithoutUndo();
        content.GetComponent<PortalFallingBagController>().enabled = false;

        root.SetActive(false);
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    static void CreateDecorativeClouds(Transform content)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(DecorativeCloudPrefabPath);
        if (source == null)
            throw new MissingReferenceException("Cloud.prefab is missing.");
        var positions = new[]
        {
            new Vector3(-0.706f, -0.32f, 0.28f),
            new Vector3(-0.41f, 0.1f, 0.33f),
            new Vector3(0.559f, -0.265f, 0.24f)
        };
        for (var i = 0; i < positions.Length; i++)
        {
            var cloud = PrefabUtility.InstantiatePrefab(source) as GameObject;
            if (cloud == null)
                throw new MissingReferenceException("Could not instantiate Cloud.prefab.");
            cloud.name = $"PortalCloud_{i + 1}";
            cloud.transform.SetParent(content, false);
            cloud.transform.localPosition = positions[i];
            cloud.transform.localRotation = Quaternion.identity;
            cloud.transform.localScale = Vector3.one * (i == 1 ? 0.2f : 0.5f);
        }
    }

    static void CreatePortalSnow(Transform content)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(SnowPrefabPath);
        if (source == null)
            throw new MissingReferenceException("SnowLight.prefab is missing.");
        var snow = PrefabUtility.InstantiatePrefab(source) as GameObject;
        snow.name = "PortalSnow";
        snow.transform.SetParent(content, false);
        snow.transform.localPosition = new Vector3(0f, 0.95f, 0.38f);
        snow.transform.localRotation = Quaternion.identity;
        snow.transform.localScale = Vector3.one * 1.35f;
        var system = snow.GetComponent<ParticleSystem>();
        if (system != null)
        {
            var shape = system.shape;
            shape.scale = new Vector3(0.72f, 0.9f, 0f);
        }
    }

    static void CreateMoodiumClouds(Transform content)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(CloudPrefabPath);
        if (source == null)
            throw new MissingReferenceException("MoodiumCloud.prefab is missing.");
        var positions = new[] { new Vector3(-0.39f, -0.37f, 0.52f), new Vector3(0.46f, -0.37f, 0.52f) };
        for (var i = 0; i < positions.Length; i++)
        {
            var cloud = PrefabUtility.InstantiatePrefab(source) as GameObject;
            if (cloud == null)
                throw new MissingReferenceException("Could not instantiate MoodiumCloud.prefab.");
            cloud.name = $"MoodiumCloud_{i + 1}";
            cloud.transform.SetParent(content, false);
            cloud.transform.localPosition = positions[i];
            cloud.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            cloud.transform.localScale = Vector3.one * 0.5f;
        }
    }

    static void ApplyMoodiPortalSkin(Transform moodiRoot, Material skinMaterial)
    {
        foreach (var renderer in moodiRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var name = renderer.name;
            if (name.StartsWith("Body") || name.StartsWith("Flipper") ||
                name.StartsWith("Sprout") || name.StartsWith("Tail"))
                renderer.sharedMaterials = renderer.sharedMaterials.Select(_ => skinMaterial).ToArray();
        }
    }

    static void ConfigureMoodiSecondaryMotion(Transform moodiRoot)
    {
        var body = moodiRoot.Find("IP_Unity_3Clips/Lilac_Sprout/RIG_Sprout/ROOT/Body");
        if (body == null)
            throw new MissingReferenceException("Moodi's Body bone is missing.");

        AddGentleBoneStimulator(body.Find("Sprout"), moodiRoot);
        AddGentleBoneStimulator(body.Find("Tail"), moodiRoot);
    }

    static void AddGentleBoneStimulator(Transform bone, Transform moodiRoot)
    {
        if (bone == null)
            throw new MissingReferenceException("A Moodi secondary-motion bone is missing.");

        var stimulator = bone.gameObject.AddComponent<BonesStimulator>();
        stimulator.Bones = new List<BonesStimulator.Bone>
        {
            new BonesStimulator.Bone { transform = bone }
        };
        stimulator.CompensationTransform = moodiRoot;
        stimulator.StimulatorAmount = 1f;
        stimulator.MovementMuscles = 0f;
        stimulator.RotationSpaceMuscles = 0.12f;
        stimulator.RotationsRapidity = 0.5f;
        stimulator.RotationsDamping = 0.45f;
        stimulator.RotationsSwinginess = 0.35f;
        stimulator.VibrateAmount = 0.15f;
        stimulator.VibrateSpeed = 1.2f;
        stimulator.VibrateRange = 2.5f;
        stimulator.VibrateAxis = new Vector3(0.35f, 0.7f, 0.25f);
        stimulator.VibrateRotation = 1f;
        stimulator.VibratePosition = 0f;
        stimulator.VibrateScale = 0f;
        stimulator.UseCollisions = false;
    }

    static void CreateCutSphereBackground(Transform parent)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(CutSpherePath);
        var skyboxMaterial = AssetDatabase.LoadAssetAtPath<Material>(SkyboxMaterialPath);
        if (source == null)
            throw new FileNotFoundException("PolySpatial CutSphere model is missing.", CutSpherePath);
        if (skyboxMaterial == null)
            throw new FileNotFoundException("PolySpatial portal Skybox material is missing.", SkyboxMaterialPath);

        var background = PrefabUtility.InstantiatePrefab(source) as GameObject;
        if (background == null)
            throw new MissingReferenceException("Could not instantiate the PolySpatial CutSphere model.");
        background.name = "CutSphereBackground";
        background.transform.SetParent(parent, false);
        background.transform.localPosition = new Vector3(0f, 0.05f, 0.72f);
        background.transform.localRotation = Quaternion.Euler(265f, 0f, -90f);
        background.transform.localScale = Vector3.one * 1.2f;
        foreach (var renderer in background.GetComponentsInChildren<Renderer>(true))
            renderer.sharedMaterial = skyboxMaterial;
        foreach (var collider in background.GetComponentsInChildren<Collider>(true))
            Object.DestroyImmediate(collider);
    }

    static void CreateBagRain(Transform content)
    {
        var woodBag = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/prefab/WoodBag.prefab");
        var leafBag = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/prefab/LeafBag.prefab");
        var candyBag = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/prefab/CandyBag.prefab");
        var elasticBall = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/prefab/MoodiumElasticBall.prefab");
        var softBall = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/prefab/MoodiumSoftGelBall.prefab");
        if (woodBag == null || leafBag == null || candyBag == null)
            throw new MissingReferenceException("WoodBag.prefab, LeafBag.prefab, or CandyBag.prefab is missing.");

        var controller = content.gameObject.AddComponent<PortalFallingBagController>();
        var serialized = new SerializedObject(controller);
        var prefabs = serialized.FindProperty("m_BagPrefabs");
        prefabs.arraySize = elasticBall != null && softBall != null ? 41 : 3;
        prefabs.GetArrayElementAtIndex(0).objectReferenceValue = woodBag;
        prefabs.GetArrayElementAtIndex(1).objectReferenceValue = leafBag;
        prefabs.GetArrayElementAtIndex(2).objectReferenceValue = candyBag;
        if (prefabs.arraySize > 3)
        {
            var cursor = 0;
            for (var bag = 0; bag < 15; bag++)
            {
                prefabs.GetArrayElementAtIndex(cursor++).objectReferenceValue =
                    prefabs.GetArrayElementAtIndex(bag % 3).objectReferenceValue;
                prefabs.GetArrayElementAtIndex(cursor++).objectReferenceValue =
                    (bag % 2 == 0) ? elasticBall : softBall;
                if (bag < 11)
                    prefabs.GetArrayElementAtIndex(cursor++).objectReferenceValue =
                        (bag % 2 == 0) ? softBall : elasticBall;
            }
        }
        serialized.FindProperty("m_ConcurrentCount").intValue = prefabs.arraySize > 3 ? 41 : 30;
        serialized.FindProperty("m_SpawnX").vector2Value = new Vector2(-0.75f, 0.75f);
        serialized.FindProperty("m_SpawnY").vector2Value = new Vector2(0.78f, 1.08f);
        serialized.FindProperty("m_UniformScale").vector2Value = new Vector2(0.15f, 0.18f);
        serialized.FindProperty("m_LeafBagScaleMultiplier").floatValue = 1.15f;
        serialized.FindProperty("m_InitialSpawnInterval").floatValue = 0.35f;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        var chamber = new GameObject("BagPhysicsChamber");
        chamber.transform.SetParent(content, false);
        var chamberBody = chamber.AddComponent<Rigidbody>();
        chamberBody.isKinematic = true;
        chamberBody.useGravity = false;
        CreateChamberWall("Ceiling", chamber.transform, new Vector3(0f, 1.18f, 0.35f), new Vector3(1.8f, 0.05f, 0.55f));
        CreateChamberWall("Floor", chamber.transform, new Vector3(0f, -0.74f, 0.35f), new Vector3(1.8f, 0.05f, 0.55f));
        CreateChamberWall("Left", chamber.transform, new Vector3(-0.9f, 0.18f, 0.35f), new Vector3(0.05f, 2f, 0.55f));
        CreateChamberWall("Right", chamber.transform, new Vector3(0.9f, 0.18f, 0.35f), new Vector3(0.05f, 2f, 0.55f));
        CreateChamberWall("Front", chamber.transform, new Vector3(0f, 0.18f, 0.1f), new Vector3(1.82f, 2f, 0.05f));
        CreateChamberWall("Back", chamber.transform, new Vector3(0f, 0.18f, 0.62f), new Vector3(1.82f, 2f, 0.05f));
    }

    static void CreateChamberWall(string name, Transform parent, Vector3 center, Vector3 size)
    {
        var wall = new GameObject(name);
        wall.transform.SetParent(parent, false);
        wall.transform.localPosition = center;
        var collider = wall.AddComponent<BoxCollider>();
        collider.size = size;
    }

    static void FitBoxColliderToRenderers(Transform root, BoxCollider collider)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            collider.center = Vector3.zero;
            collider.size = Vector3.one;
            return;
        }

        var bounds = renderers[0].bounds;
        for (var i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        collider.center = root.InverseTransformPoint(bounds.center);
        var scale = root.lossyScale;
        collider.size = new Vector3(
            bounds.size.x / Mathf.Max(Mathf.Abs(scale.x), 0.0001f),
            bounds.size.y / Mathf.Max(Mathf.Abs(scale.y), 0.0001f),
            bounds.size.z / Mathf.Max(Mathf.Abs(scale.z), 0.0001f));
    }

    static void CreateOccluder(string name, Transform parent, Vector3 position, Vector3 scale, Material material)
    {
        var plane = GameObject.CreatePrimitive(PrimitiveType.Cube);
        plane.name = name;
        plane.transform.SetParent(parent, false);
        plane.transform.localPosition = position;
        plane.transform.localScale = scale;
        plane.GetComponent<Renderer>().sharedMaterial = material;
        Object.DestroyImmediate(plane.GetComponent<Collider>());
    }

    static void ConnectScene(GameObject prefab)
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        foreach (var portal in Object.FindObjectsByType<MoodiumPortalEntranceController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            Object.DestroyImmediate(portal.gameObject);

        var instance = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
        instance.name = "Moodium Portal Entrance";
        instance.SetActive(false);
        var portalController = instance.GetComponent<MoodiumPortalEntranceController>();
        var flow = Object.FindFirstObjectByType<MoodiumAppFlowController>(FindObjectsInactive.Include);
        if (flow == null)
            throw new MissingReferenceException("MoodiumAppFlowController was not found in Meshing 1.unity.");
        var serializedFlow = new SerializedObject(flow);
        serializedFlow.FindProperty("m_PortalEntrance").objectReferenceValue = portalController;
        serializedFlow.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(flow);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    static void EnsureFolder(string path)
    {
        var parts = path.Split('/');
        var current = parts[0];
        for (var i = 1; i < parts.Length; i++)
        {
            var next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
