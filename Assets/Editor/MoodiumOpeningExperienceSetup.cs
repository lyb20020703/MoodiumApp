#if UNITY_EDITOR
using System;
using System.Linq;
using Moodium.Opening;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

[InitializeOnLoad]
public static class MoodiumOpeningExperienceSetup
{
    const string CandyFbx = "Assets/Model/Moodium_Opening/FBX/Candy_Animation.fbx";
    const string SpriteFbx = "Assets/Model/Moodium_Opening/FBX/Sprite_Animation.fbx";
    const string LogoFbx = "Assets/Model/Moodium_Opening/FBX/Moodium_Logo.fbx";
    const string OutputFolder = "Assets/Prefab/MoodiumOpening";
    const string AnimationFolder = "Assets/Resources/MoodiumOpening";
    const string PrefabPath = OutputFolder + "/MoodiumOpening.prefab";
    const string LogoTextPrefabPath = OutputFolder + "/MoodiumLogoText.prefab";
    const string LogoTextMaterialPath = OutputFolder + "/MoodiumLogoText_Glow.mat";
    const string ScenePath = "Assets/Samples/PolySpatial/Scenes/Meshing 1.unity";
    const string ChineseFontSource = "Assets/TextMesh Pro/Fonts/AlibabaPuHuiTi-2-35-Thin.ttf";
    const string ChineseFontAsset = "Assets/UI/Moodium/Fonts/AlibabaPuHuiTi-2-35-Thin SDF.asset";
    const string EnglishFontAsset = "Assets/UI/Moodium/Fonts/Inter-Regular SDF.asset";
    const string IntroVideoAsset = "Assets/Video/VideoPLUS.mp4";
    const string OpeningVideoSequenceConfigAsset = "Assets/Resources/MoodiumOpening/OpeningVideoSequenceConfig.asset";
    const string LeftHandGuideAsset = "Assets/prefab/UI-LeftHand.prefab";
    const string RightHandGuideAsset = "Assets/prefab/UI-RightHand.prefab";
    const string IntroPromptFontAsset = "Assets/UI/Moodium/Fonts/AlibabaPuHuiTi Moodium SDF.asset";
    const string VersionKey = "Moodium.OpeningExperience.Setup.v22";

    static MoodiumOpeningExperienceSetup()
    {
        EditorApplication.delayCall += TryAutomaticSetup;
    }

    [MenuItem("Moodium/Build Opening Experience")]
    public static void BuildOpeningExperience()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[Moodium Opening] Asset setup is disabled during Play Mode.");
            return;
        }
        EnsureFolder("Assets/Prefab");
        EnsureFolder(OutputFolder);
        EnsureFolder("Assets/Resources");
        EnsureFolder(AnimationFolder);
        var openingVideoSequence = EnsureOpeningVideoSequenceConfig();

        var candyIdleClip = FindClip(CandyFbx, "Candy_Idle");
        var candySourcePath = AnimationFolder + "/Candy_Transform_Source.anim";
        var candySource = MergeClips(candySourcePath,
            FindExactClip(CandyFbx, "Candy_Core|Candy_Open_Core"),
            FindExactClip(CandyFbx, "Wrapper_Left|Candy_Open_Wrapper_Left"),
            FindExactClip(CandyFbx, "Wrapper_Right|Candy_Open_Wrapper_Right"));
        var candyClip = RebaseClip(AnimationFolder + "/Candy_Transform.anim", candySource);

        var candyController = CreateController(
            OutputFolder + "/Candy_Opening.controller",
            // Idle is procedural on the outer Candy container. Do not let the
            // Blender root-transform action override head-relative placement.
            ("Candy_Idle", null, true),
            ("Candy_Transform", candyClip, false));
        var logoTextPrefab = CreateLogoTextPrefab();

        var root = new GameObject("MoodiumOpening");
        try
        {
            var candy = CreateModelRoot(root.transform, "Candy", CandyFbx, candyController, 0.42f);
            // Keep the Sprite asset in the hierarchy for future extensions, but it
            // has no active animation/controller in this Opening revision.
            var sprite = CreateModelRoot(root.transform, "Sprite_Future", SpriteFbx, null, 0.14f);
            sprite.transform.localScale = Vector3.one * 0.1f;
            sprite.SetActive(false);
            var logoText = (GameObject)PrefabUtility.InstantiatePrefab(logoTextPrefab);
            logoText.transform.SetParent(root.transform, false);
            logoText.transform.localPosition = Vector3.zero;
            logoText.transform.localRotation = Quaternion.identity;
            logoText.transform.localScale = Vector3.one;
            var logoTextView = logoText.GetComponent<OpeningLogoTextView>();

            var prompt = CreateTouchPrompt(root.transform);

            var collider = candy.AddComponent<BoxCollider>();
            ConfigureCollider(collider, candy);
            collider.isTrigger = true;
            var rigidbody = candy.AddComponent<Rigidbody>();
            rigidbody.isKinematic = true;
            rigidbody.useGravity = false;

            var interaction = candy.AddComponent<CandyInteraction>();
            var animations = root.AddComponent<OpeningAnimationController>();
            var manager = root.AddComponent<OpeningManager>();
            Assign(animations,
                ("m_CandyAnimator", candy.GetComponentInChildren<Animator>(true)),
                ("m_CandyRoot", candy),
                ("m_SpriteRoot", sprite),
                ("m_LegacyLogoRoot", null),
                ("m_LogoTextView", logoTextView),
                ("m_CandyTransformClip", candyClip));
            AssignFloat(animations,
                ("m_CandyTransformDuration", Duration(candyClip, 2.4f)));
            Assign(manager,
                ("m_AnimationController", animations),
                ("m_CandyInteraction", interaction),
                ("m_TouchPrompt", prompt),
                ("m_VideoSequenceConfig", openingVideoSequence),
                ("m_PreOpeningVideo", AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(IntroVideoAsset)),
                ("m_LeftHandGuidePrefab", AssetDatabase.LoadAssetAtPath<GameObject>(LeftHandGuideAsset)),
                ("m_RightHandGuidePrefab", AssetDatabase.LoadAssetAtPath<GameObject>(RightHandGuideAsset)),
                ("m_HandPromptFont", AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(IntroPromptFontAsset)));
            AssignFloat(manager,
                ("m_PreOpeningVideoHeightOffset", 0.63f),
                ("m_PreOpeningVideoDistanceFromUser", 1.15f),
                ("m_InteractiveVideoPreviewDuration", 3.08f));

            ValidateClipBindings(candy.GetComponentInChildren<Animator>(true), candyClip);

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }

        AssetDatabase.DeleteAsset(candySourcePath);

        InjectIntoMeshingScene();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        foreach (var clipPath in new[] { AnimationFolder + "/Candy_Transform.anim" })
            AssetDatabase.ImportAsset(clipPath, ImportAssetOptions.ForceSynchronousImport);
        VerifyRuntimeClipResources();
        Debug.Log($"[Moodium Opening] Built prefab and injected Meshing1. " +
                  $"Candy Idle={ClipName(candyIdleClip)}, Transform={ClipName(candyClip)}; " +
                  $"Logo=TextMeshPro alpha fade. Legacy Sprite/Logo assets retained but inactive. Available clips: " +
                  $"Candy[{ClipList(CandyFbx)}], Sprite[{ClipList(SpriteFbx)}], Logo[{ClipList(LogoFbx)}]");
    }

    [MenuItem("Moodium/Test Opening Sequence In Play Mode")]
    public static void TestOpeningSequence()
    {
        if (!EditorApplication.isPlaying)
        {
            Debug.LogWarning("[Moodium Opening] Enter Play Mode before running the sequence test.");
            return;
        }
        var manager = UnityEngine.Object.FindFirstObjectByType<OpeningManager>();
        if (manager == null)
        {
            Debug.LogError("[Moodium Opening] OpeningManager was not found in Play Mode.");
            return;
        }
        manager.BeginOpening();
        Debug.Log("[Moodium Opening] Editor validation trigger sent.");
    }

    static void TryAutomaticSetup()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += TryAutomaticSetup;
            return;
        }
        if (EditorPrefs.GetBool(VersionKey, false))
            return;
        if (AssetDatabase.LoadAssetAtPath<GameObject>(CandyFbx) == null ||
            AssetDatabase.LoadAssetAtPath<GameObject>(SpriteFbx) == null ||
            AssetDatabase.LoadAssetAtPath<GameObject>(LogoFbx) == null)
            return;
        EditorPrefs.SetBool(VersionKey, true);
        BuildOpeningExperience();
    }

    static OpeningVideoSequenceConfig EnsureOpeningVideoSequenceConfig()
    {
        var config = AssetDatabase.LoadAssetAtPath<OpeningVideoSequenceConfig>(OpeningVideoSequenceConfigAsset);
        if (config == null)
        {
            config = ScriptableObject.CreateInstance<OpeningVideoSequenceConfig>();
            AssetDatabase.CreateAsset(config, OpeningVideoSequenceConfigAsset);
        }
        var serialized = new SerializedObject(config);
        serialized.FindProperty("m_VideoClip").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<UnityEngine.Video.VideoClip>(IntroVideoAsset);
        serialized.FindProperty("m_IntroLoopStartTime").floatValue = 0f;
        serialized.FindProperty("m_IntroLoopEndTime").floatValue = 3.08f;
        serialized.FindProperty("m_LanguageLoopStartTime").floatValue =
            (float)OpeningVideoTimeline.FrameToSeconds(OpeningVideoTimeline.LanguageLoop.StartFrame);
        serialized.FindProperty("m_LanguageLoopEndTime").floatValue =
            (float)OpeningVideoTimeline.FrameToSeconds(OpeningVideoTimeline.LanguageLoop.EndFrame);
        serialized.FindProperty("m_DistanceFromUser").floatValue = 1.35f;
        serialized.FindProperty("m_HeightOffset").floatValue = -0.05f;
        serialized.FindProperty("m_WorldPositionY").floatValue = 1f;
        serialized.FindProperty("m_ChineseTutorialClip").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<UnityEngine.Video.VideoClip>(IntroVideoAsset);
        serialized.FindProperty("m_EnglishTutorialClip").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<UnityEngine.Video.VideoClip>(IntroVideoAsset);
        serialized.FindProperty("m_ChineseHardwareQuestionTime").floatValue = 9.04f;
        serialized.FindProperty("m_ChineseHardwareLoopEndTime").floatValue = 15.03f;
        serialized.FindProperty("m_ChineseHardwareYesEndTime").floatValue = 30.06f;
        serialized.FindProperty("m_ChineseHardwareNoStartTime").floatValue = 36.05f;
        serialized.FindProperty("m_ChineseHardwareNoEndTime").floatValue = 51.08f;
        serialized.FindProperty("m_EnglishHardwareQuestionTime").floatValue = 9.23f;
        serialized.FindProperty("m_EnglishHardwareLoopEndTime").floatValue = 15.03f;
        serialized.FindProperty("m_EnglishHardwareYesEndTime").floatValue = 30.11f;
        serialized.FindProperty("m_EnglishHardwareNoStartTime").floatValue = 35.16f;
        serialized.FindProperty("m_EnglishHardwareNoEndTime").floatValue = 50.20f;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return config;
    }

    static void VerifyRuntimeClipResources()
    {
        foreach (var name in new[] { "Candy_Transform" })
        {
            var clip = Resources.Load<AnimationClip>($"MoodiumOpening/{name}");
            if (clip == null)
                Debug.LogError($"[Moodium Opening] Resources validation failed: {name} is not loadable.");
            else
                Debug.Log($"[Moodium Opening] Resources validation passed: {name} ({clip.length:0.###}s).");
        }
    }

    static GameObject CreateModelRoot(Transform parent, string name, string path,
        RuntimeAnimatorController controller, float targetWidth)
    {
        var wrapper = new GameObject(name);
        wrapper.transform.SetParent(parent, false);
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        var model = (GameObject)PrefabUtility.InstantiatePrefab(asset);
        model.name = name + "_Model";
        model.transform.SetParent(wrapper.transform, false);

        var animator = model.GetComponent<Animator>();
        if (animator == null)
            animator = model.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        NormalizeWidth(model, targetWidth);
        CenterModel(model);
        return wrapper;
    }

    static GameObject CreateTouchPrompt(Transform parent)
    {
        var chineseFont = EnsureChineseFont();
        var englishFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(EnglishFontAsset);
        var prompt = new GameObject("OpeningPrompt");
        prompt.transform.SetParent(parent, false);
        prompt.transform.localPosition = new Vector3(0f, -0.18f, -0.025f);

        var chinese = CreatePromptText(prompt.transform, "Chinese - 触摸我", "触摸我",
            new Vector3(0f, 0.026f, 0f), chineseFont, 8.5f);
        var english = CreatePromptText(prompt.transform, "English - Touch Me", "Touch Me",
            new Vector3(0f, -0.034f, 0f), englishFont, 6.8f);
        var view = prompt.AddComponent<OpeningPromptView>();
        var serialized = new SerializedObject(view);
        var texts = serialized.FindProperty("m_Texts");
        texts.arraySize = 2;
        texts.GetArrayElementAtIndex(0).objectReferenceValue = chinese;
        texts.GetArrayElementAtIndex(1).objectReferenceValue = english;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return prompt;
    }

    static GameObject CreateLogoTextPrefab()
    {
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(EnglishFontAsset);
        if (font == null)
            throw new InvalidOperationException($"Moodium logo font missing: {EnglishFontAsset}");
        var material = CreateLogoTextMaterial(font);
        var root = new GameObject("MoodiumLogoText");
        try
        {
            var title = CreateLogoText(root.transform, "Moodium", "Moodium",
                new Vector3(0f, 0.025f, 0f), font, material, 15f, new Vector2(60f, 12f));
            title.enableVertexGradient = true;
            title.colorGradient = new VertexGradient(
                new Color(0.76f, 0.50f, 1f), new Color(1f, 0.58f, 0.82f),
                new Color(0.62f, 0.38f, 1f), new Color(1f, 0.72f, 0.84f));
            var subtitle = CreateLogoText(root.transform, "Tagline", "Digital Sensory Playground",
                new Vector3(0f, -0.065f, 0f), font, material, 4.6f, new Vector2(72f, 8f));
            subtitle.color = new Color(0.93f, 0.78f, 1f, 1f);

            var view = root.AddComponent<OpeningLogoTextView>();
            var serialized = new SerializedObject(view);
            var texts = serialized.FindProperty("m_Texts");
            texts.arraySize = 2;
            texts.GetArrayElementAtIndex(0).objectReferenceValue = title;
            texts.GetArrayElementAtIndex(1).objectReferenceValue = subtitle;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return PrefabUtility.SaveAsPrefabAsset(root, LogoTextPrefabPath);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    static TextMeshPro CreateLogoText(Transform parent, string name, string value,
        Vector3 localPosition, TMP_FontAsset font, Material material, float fontSize, Vector2 size)
    {
        var target = new GameObject(name);
        target.transform.SetParent(parent, false);
        target.transform.localPosition = localPosition;
        target.transform.localRotation = Quaternion.identity;
        target.transform.localScale = Vector3.one * 0.01f;
        var text = target.AddComponent<TextMeshPro>();
        text.text = value;
        text.font = font;
        text.fontSharedMaterial = material;
        text.fontSize = fontSize;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.rectTransform.sizeDelta = size;
        text.renderer.sortingOrder = 60;
        return text;
    }

    static Material CreateLogoTextMaterial(TMP_FontAsset font)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(LogoTextMaterialPath);
        if (material == null)
        {
            material = new Material(font.material) { name = "MoodiumLogoText_Glow" };
            AssetDatabase.CreateAsset(material, LogoTextMaterialPath);
        }
        if (material.HasProperty("_FaceColor"))
            material.SetColor("_FaceColor", Color.white);
        if (material.HasProperty("_OutlineColor"))
            material.SetColor("_OutlineColor", new Color(0.72f, 0.38f, 1f, 0.7f));
        if (material.HasProperty("_OutlineWidth"))
            material.SetFloat("_OutlineWidth", 0.08f);
        if (material.HasProperty("_GlowColor"))
            material.SetColor("_GlowColor", new Color(1f, 0.35f, 0.86f, 0.65f));
        if (material.HasProperty("_GlowPower"))
            material.SetFloat("_GlowPower", 0.65f);
        if (material.HasProperty("_GlowOuter"))
            material.SetFloat("_GlowOuter", 0.35f);
        EditorUtility.SetDirty(material);
        return material;
    }

    static TextMeshPro CreatePromptText(Transform parent, string name, string value,
        Vector3 localPosition, TMP_FontAsset font, float fontSize)
    {
        var target = new GameObject(name);
        target.transform.SetParent(parent, false);
        target.transform.localPosition = localPosition;
        target.transform.localScale = Vector3.one * 0.01f;
        var text = target.AddComponent<TextMeshPro>();
        text.text = value;
        text.font = font;
        text.fontSize = fontSize;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.rectTransform.sizeDelta = new Vector2(40f, 8f);
        text.color = new Color(1f, 0.82f, 1f, 1f);
        text.renderer.sortingOrder = 50;
        return text;
    }

    static TMP_FontAsset EnsureChineseFont()
    {
        var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(ChineseFontAsset);
        if (existing != null)
            return existing;
        var source = AssetDatabase.LoadAssetAtPath<Font>(ChineseFontSource);
        if (source == null)
            throw new InvalidOperationException($"Chinese font source missing: {ChineseFontSource}");
        var asset = TMP_FontAsset.CreateFontAsset(source);
        asset.name = "AlibabaPuHuiTi-2-35-Thin SDF";
        asset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
        AssetDatabase.CreateAsset(asset, ChineseFontAsset);
        asset.TryAddCharacters("触摸我", out _);
        EditorUtility.SetDirty(asset);
        return asset;
    }

    static void NormalizeWidth(GameObject model, float targetWidth)
    {
        if (!TryBounds(model, out var bounds) || bounds.size.x < 0.0001f)
            return;
        var scale = targetWidth / bounds.size.x;
        model.transform.localScale *= scale;
    }

    static void CenterModel(GameObject model)
    {
        if (!TryBounds(model, out var bounds))
            return;
        model.transform.position -= bounds.center;
    }

    static void ConfigureCollider(BoxCollider collider, GameObject root)
    {
        if (!TryBounds(root, out var bounds))
            return;
        collider.center = root.transform.InverseTransformPoint(bounds.center);
        var scale = root.transform.lossyScale;
        collider.size = new Vector3(
            bounds.size.x / Mathf.Max(Mathf.Abs(scale.x), 0.0001f),
            bounds.size.y / Mathf.Max(Mathf.Abs(scale.y), 0.0001f),
            bounds.size.z / Mathf.Max(Mathf.Abs(scale.z), 0.0001f));
    }

    static bool TryBounds(GameObject root, out Bounds bounds)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            bounds = default;
            return false;
        }
        bounds = renderers[0].bounds;
        for (var i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        return true;
    }

    static AnimatorController CreateController(string path,
        params (string name, AnimationClip clip, bool defaultState)[] states)
    {
        AssetDatabase.DeleteAsset(path);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
        var machine = controller.layers[0].stateMachine;
        foreach (var definition in states)
        {
            var state = machine.AddState(definition.name);
            state.motion = definition.clip;
            state.writeDefaultValues = true;
            if (definition.defaultState)
                machine.defaultState = state;
        }
        if (machine.defaultState == null && states.Length > 0)
            machine.defaultState = machine.states[0].state;
        EditorUtility.SetDirty(controller);
        return controller;
    }

    static AnimationClip FindClip(string path, params string[] preferredNames)
    {
        var clips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
            .Where(clip => !clip.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        foreach (var preferred in preferredNames)
        {
            var match = clips.FirstOrDefault(clip =>
                clip.name.IndexOf(preferred, StringComparison.OrdinalIgnoreCase) >= 0);
            if (match != null)
                return match;
        }
        return clips.FirstOrDefault();
    }

    static AnimationClip FindExactClip(string path, string exactName) =>
        AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
            .FirstOrDefault(clip => string.Equals(clip.name, exactName, StringComparison.OrdinalIgnoreCase));

    static AnimationClip MergeClips(string outputPath, params AnimationClip[] sources)
    {
        AssetDatabase.DeleteAsset(outputPath);
        var merged = new AnimationClip { name = System.IO.Path.GetFileNameWithoutExtension(outputPath) };
        foreach (var source in sources.Where(source => source != null))
        {
            foreach (var binding in AnimationUtility.GetCurveBindings(source))
                AnimationUtility.SetEditorCurve(merged, binding, AnimationUtility.GetEditorCurve(source, binding));
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(source))
                AnimationUtility.SetObjectReferenceCurve(merged, binding,
                    AnimationUtility.GetObjectReferenceCurve(source, binding));
            merged.frameRate = Mathf.Max(merged.frameRate, source.frameRate);
        }
        AssetDatabase.CreateAsset(merged, outputPath);
        return merged;
    }

    static AnimationClip RebaseClip(string outputPath, AnimationClip source)
    {
        if (source == null)
            return null;

        var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(outputPath);

        var bindings = AnimationUtility.GetCurveBindings(source);
        var start = float.PositiveInfinity;
        foreach (var binding in bindings)
        {
            var curve = AnimationUtility.GetEditorCurve(source, binding);
            for (var i = 1; i < curve.length; i++)
            {
                if (Mathf.Abs(curve.keys[i].value - curve.keys[i - 1].value) > 0.00001f)
                    start = Mathf.Min(start, curve.keys[i - 1].time);
            }
        }
        if (float.IsInfinity(start))
            start = 0f;

        var result = new AnimationClip
        {
            name = System.IO.Path.GetFileNameWithoutExtension(outputPath),
            frameRate = source.frameRate
        };
        foreach (var binding in bindings)
        {
            var sourceCurve = AnimationUtility.GetEditorCurve(source, binding);
            var keys = sourceCurve.keys
                .Where(key => key.time + 0.0001f >= start)
                .Select(key =>
                {
                    key.time = Mathf.Max(0f, key.time - start);
                    return key;
                }).ToArray();
            if (keys.Length == 0)
                keys = new[] { new Keyframe(0f, sourceCurve.Evaluate(start)) };
            AnimationUtility.SetEditorCurve(result, binding, new AnimationCurve(keys));
        }
        foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(source))
        {
            var keys = AnimationUtility.GetObjectReferenceCurve(source, binding)
                .Where(key => key.time + 0.0001f >= start)
                .Select(key =>
                {
                    key.time = Mathf.Max(0f, key.time - start);
                    return key;
                }).ToArray();
            AnimationUtility.SetObjectReferenceCurve(result, binding, keys);
        }
        AnimationClip output;
        if (existing != null)
        {
            EditorUtility.CopySerialized(result, existing);
            existing.name = System.IO.Path.GetFileNameWithoutExtension(outputPath);
            EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(result);
            output = existing;
        }
        else
        {
            AssetDatabase.CreateAsset(result, outputPath);
            output = result;
        }
        Debug.Log($"[Moodium Opening] Rebased {source.name} by {start:0.###}s -> {output.name} ({output.length:0.###}s).");
        return output;
    }

    static void TuneSpriteSpawn(AnimationClip clip)
    {
        if (clip == null)
            return;
        var duration = clip.length;
        foreach (var axis in new[] { "x", "y", "z" })
        {
            var binding = new EditorCurveBinding
            {
                path = "Sprite_Idle/Sprite_Runtime",
                type = typeof(Transform),
                propertyName = $"m_LocalScale.{axis}"
            };
            var curve = new AnimationCurve(
                new Keyframe(0f, 0.01f),
                new Keyframe(duration * 0.25f, 0.01f),
                new Keyframe(duration * 0.58f, 0.169753f),
                new Keyframe(duration, 0.169753f));
            SmoothCurve(curve);
            SetClipCurve(clip, binding, curve);
        }
        EditorUtility.SetDirty(clip);
        Debug.Log($"[Moodium Opening] Tuned Sprite Spawn: 0.01 -> 0.169753, {duration:0.###}s.");
    }

    static void TuneSpriteFly(AnimationClip clip)
    {
        if (clip == null)
            return;
        // The Blender fly action has a root Scale=1 key. Remove it so it cannot
        // override the imported FBX normalization; animate Sprite_Runtime instead.
        foreach (var binding in AnimationUtility.GetCurveBindings(clip)
                     .Where(binding => binding.propertyName.StartsWith("m_LocalScale", StringComparison.Ordinal)))
            SetClipCurve(clip, binding, null);

        ConstrainSpriteFlyPosition(clip);

        var duration = clip.length;
        foreach (var axis in new[] { "x", "y", "z" })
        {
            var binding = new EditorCurveBinding
            {
                path = "Sprite_Idle/Sprite_Runtime",
                type = typeof(Transform),
                propertyName = $"m_LocalScale.{axis}"
            };
            var curve = new AnimationCurve(
                new Keyframe(0f, 0.169753f),
                new Keyframe(duration * 0.82f, 0.169753f),
                new Keyframe(duration, 0.03f));
            SmoothCurve(curve);
            SetClipCurve(clip, binding, curve);
        }
        EditorUtility.SetDirty(clip);
        Debug.Log($"[Moodium Opening] Tuned Sprite Fly: hold 0.169753 -> 0.03, {duration:0.###}s.");
    }

    static void ConstrainSpriteFlyPosition(AnimationClip clip)
    {
        const float wrapperScale = 0.1f;
        // Logo wrapper is at (0, -0.03, 0.02) in MoodiumOpening space.
        // Convert that target and the desired world-space limits into Sprite's
        // local coordinates because its non-animated wrapper scale is 0.1.
        var targetLocal = new Vector3(0f, -0.03f, 0.02f) / wrapperScale;
        // Clamp the authored root-motion values themselves. Although a 0.1 wrapper
        // exists, Playables/PolySpatial can present animated root translation closer
        // to these authored values on device, so do not expand limits by 10x.
        var limitLocal = new Vector3(0.30f, 0.22f, 0.08f);
        var originalMin = Vector3.zero;
        var originalMax = Vector3.zero;
        var revisedMin = Vector3.zero;
        var revisedMax = Vector3.zero;

        var positionCurves = AnimationUtility.GetCurveBindings(clip)
            .Where(binding => binding.type == typeof(Transform) &&
                              binding.propertyName.StartsWith("m_LocalPosition", StringComparison.Ordinal))
            .Select(binding => (binding, curve: new AnimationCurve(
                AnimationUtility.GetEditorCurve(clip, binding).keys))).ToArray();

        foreach (var item in positionCurves)
        {
            var axis = item.binding.propertyName.EndsWith(".x", StringComparison.Ordinal) ? 0 :
                item.binding.propertyName.EndsWith(".y", StringComparison.Ordinal) ? 1 : 2;
            var keys = item.curve.keys;
            if (keys.Length == 0)
                continue;
            var min = keys.Min(key => key.value);
            var max = keys.Max(key => key.value);
            var final = keys[keys.Length - 1].value;
            originalMin[axis] = min;
            originalMax[axis] = max;

            var maxDeviation = Mathf.Max(Mathf.Abs(min - final), Mathf.Abs(max - final));
            var factor = maxDeviation > 0.00001f ? limitLocal[axis] / maxDeviation : 1f;
            for (var i = 0; i < keys.Length; i++)
                keys[i].value = targetLocal[axis] + (keys[i].value - final) * factor;
            var curve = new AnimationCurve(keys);
            SmoothCurve(curve);
            SetClipCurve(clip, item.binding, curve);
            revisedMin[axis] = keys.Min(key => key.value);
            revisedMax[axis] = keys.Max(key => key.value);
        }

        EditorUtility.SetDirty(clip);
        Debug.Log($"[Moodium Opening] Sprite Fly authored local position range: " +
                  $"original X[{originalMin.x:0.###},{originalMax.x:0.###}] " +
                  $"Y[{originalMin.y:0.###},{originalMax.y:0.###}] " +
                  $"Z[{originalMin.z:0.###},{originalMax.z:0.###}] -> " +
                  $"revised X[{revisedMin.x:0.###},{revisedMax.x:0.###}] " +
                  $"Y[{revisedMin.y:0.###},{revisedMax.y:0.###}] " +
                  $"Z[{revisedMin.z:0.###},{revisedMax.z:0.###}], " +
                  $"final local={targetLocal}; final Opening-space=(0,-0.03,0.02)m " +
                  $"through wrapper scale {wrapperScale}.");
    }

    static void TuneLogoReveal(AnimationClip clip)
    {
        if (clip == null)
            return;
        var duration = clip.length;
        foreach (var axis in new[] { "x", "y", "z" })
        {
            var binding = new EditorCurveBinding
            {
                path = "Logo_Appear",
                type = typeof(Transform),
                propertyName = $"m_LocalScale.{axis}"
            };
            var curve = new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(duration * 0.18f, 0f),
                new Keyframe(duration, 0.2f));
            SmoothCurve(curve);
            SetClipCurve(clip, binding, curve);
        }
        EditorUtility.SetDirty(clip);
        Debug.Log($"[Moodium Opening] Tuned Logo Reveal: 0 -> 0.2, {duration:0.###}s + 2.8s hold.");
    }

    static void TrimClip(AnimationClip clip, float endTime)
    {
        var curves = AnimationUtility.GetCurveBindings(clip)
            .Select(binding => (binding, curve: new AnimationCurve(
                AnimationUtility.GetEditorCurve(clip, binding).keys))).ToArray();
        foreach (var item in curves)
        {
            var binding = item.binding;
            var source = item.curve;
            var keys = source.keys.Where(key => key.time < endTime - 0.0001f).ToList();
            keys.Add(new Keyframe(endTime, source.Evaluate(endTime)));
            var curve = new AnimationCurve(keys.ToArray());
            SmoothCurve(curve);
            SetClipCurve(clip, binding, curve);
        }
    }

    static void RetimeClip(AnimationClip clip, float factor)
    {
        var curves = AnimationUtility.GetCurveBindings(clip)
            .Select(binding => (binding, curve: new AnimationCurve(
                AnimationUtility.GetEditorCurve(clip, binding).keys))).ToArray();
        foreach (var item in curves)
        {
            var binding = item.binding;
            var curve = item.curve;
            var keys = curve.keys;
            for (var i = 0; i < keys.Length; i++)
                keys[i].time *= factor;
            curve.keys = keys;
            SmoothCurve(curve);
            SetClipCurve(clip, binding, curve);
        }
        foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
        {
            var keys = AnimationUtility.GetObjectReferenceCurve(clip, binding);
            for (var i = 0; i < keys.Length; i++)
                keys[i].time *= factor;
            AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);
        }
    }

    static void SmoothCurve(AnimationCurve curve)
    {
        for (var i = 0; i < curve.length; i++)
        {
            AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
            AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
        }
    }

    static void SetClipCurve(AnimationClip clip, EditorCurveBinding binding, AnimationCurve curve)
    {
        if (binding.type == typeof(Transform))
        {
            var property = binding.propertyName
                .Replace("m_LocalPosition", "localPosition")
                .Replace("m_LocalRotation", "localRotation")
                .Replace("m_LocalScale", "localScale");
            clip.SetCurve(binding.path, typeof(Transform), property, curve);
        }
        else
        {
            AnimationUtility.SetEditorCurve(clip, binding, curve);
        }
    }

    static float Duration(AnimationClip clip, float fallback) =>
        clip != null && clip.length > 0.01f ? clip.length : fallback;

    static void ValidateClipBindings(Animator animator, params AnimationClip[] clips)
    {
        if (animator == null)
            throw new InvalidOperationException("Opening Animator is missing.");
        foreach (var clip in clips.Where(clip => clip != null))
        {
            var missing = AnimationUtility.GetCurveBindings(clip)
                .Select(binding => binding.path)
                .Where(path => !string.IsNullOrEmpty(path) && animator.transform.Find(path) == null)
                .Distinct().ToArray();
            if (missing.Length > 0)
                Debug.LogError($"[Moodium Opening] {clip.name} has unresolved paths under {animator.name}: " +
                               string.Join(", ", missing));
            else
                Debug.Log($"[Moodium Opening] Validated {clip.name}: all curve paths resolve under {animator.name}.");
        }
    }

    static string ClipList(string path) => string.Join(", ",
        AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
            .Where(clip => !clip.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase))
            .Select(clip => clip.name));

    static string ClipName(AnimationClip clip) => clip == null ? "NONE (fallback timing)" : clip.name;

    static void InjectIntoMeshingScene()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
            throw new InvalidOperationException("Opening prefab was not created.");

        var previousScenePath = SceneManager.GetActiveScene().path;
        if (!string.IsNullOrEmpty(previousScenePath) && SceneManager.GetActiveScene().isDirty)
            EditorSceneManager.SaveOpenScenes();

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var existing = scene.GetRootGameObjects().FirstOrDefault(go => go.name == "MoodiumOpening");
        if (existing != null)
            UnityEngine.Object.DestroyImmediate(existing);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        instance.name = "MoodiumOpening";
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    static void Assign(UnityEngine.Object target, params (string name, UnityEngine.Object value)[] values)
    {
        var serialized = new SerializedObject(target);
        foreach (var (name, value) in values)
            serialized.FindProperty(name).objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    static void AssignFloat(UnityEngine.Object target, params (string name, float value)[] values)
    {
        var serialized = new SerializedObject(target);
        foreach (var (name, value) in values)
            serialized.FindProperty(name).floatValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;
        var parent = path.Substring(0, path.LastIndexOf('/'));
        var name = path.Substring(path.LastIndexOf('/') + 1);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, name);
    }
}

public sealed class MoodiumOpeningBuildValidator : IPreprocessBuildWithReport
{
    public int callbackOrder => -1000;

    public void OnPreprocessBuild(BuildReport report)
    {
        const string prefabPath = "Assets/prefab/MoodiumOpening/MoodiumOpening.prefab";
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        var manager = prefab != null ? prefab.GetComponent<OpeningManager>() : null;
        if (manager == null)
            throw new BuildFailedException($"Moodium opening prefab or OpeningManager is missing: {prefabPath}");

        var serialized = new SerializedObject(manager);
        foreach (var propertyName in new[]
                 {
                     "m_PreOpeningVideo", "m_LeftHandGuidePrefab", "m_RightHandGuidePrefab", "m_HandPromptFont"
                 })
        {
            if (serialized.FindProperty(propertyName)?.objectReferenceValue == null)
                throw new BuildFailedException(
                    $"Moodium opening asset '{propertyName}' is missing. Rebuild the Opening Experience before building the app.");
        }

        var sequence = serialized.FindProperty("m_VideoSequenceConfig")?.objectReferenceValue
            as OpeningVideoSequenceConfig;
        if (sequence == null || sequence.VideoClip == null)
            throw new BuildFailedException(
                "Moodium combined opening video is missing. Assign VideoPLUS.mp4 before building the app.");
    }
}
#endif
