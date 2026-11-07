using System.IO;
using System.Linq;
using Moodium.Characters;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// One-click setup for the rigged, animated Moodi.
///
/// Moodium ▸ Moodi ▸ Setup Animated Moodi (All Steps) does three things:
/// 1. Configures the import of Assets/Model/Moodi_Rigged.fbx (Generic rig, short clip names, loops).
/// 2. Builds Assets/Animations/Moodi/MoodiCompanion.controller (Idle / Walk + Touched / Happy / Wave).
/// 3. Upgrades Assets/prefab/Moodi meshy.prefab in place: adds the rigged model exactly where the
///    static one was, hides (does not delete) the static child, adds Animator + MoodiCompanionAnimator.
///    Every prefab that nests "Moodi meshy" (CandyWorldUI, NatureWorldUI, …) is upgraded automatically.
/// </summary>
public static class MoodiAnimatedSetup
{
    const string ModelPath = "Assets/Model/Moodi_Rigged.fbx";
    const string ControllerPath = "Assets/Animations/Moodi/MoodiCompanion.controller";
    const string PrefabPath = "Assets/prefab/Moodi meshy.prefab";
    const string StaticChildName = "moodi";
    const string RiggedChildName = "moodi_rigged";
    static readonly string[] Clips = { "Idle", "Walk", "Touched", "Happy", "Wave" };
    static readonly string[] LoopClips = { "Idle", "Walk", "Peek" };

    // Landmarks measured on the Meshy mesh in Blender space (front = -Y, up = +Z).
    // The static prefab maps Blender (x, y, z) to its root space as (x, z, y) * unitsPerBlender,
    // i.e. Moodi faces the root's -Z with +Y up. The rig is placed to land on the same points.
    static readonly Vector3 BlenderBodyBottom = new(-0.01f, 0f, -0.86f);
    static readonly Vector3 BlenderBodyMid = new(-0.04f, 0f, -0.19f);
    static readonly Vector3 BlenderStemBase = new(-0.17f, 0.02f, 0.46f);
    static readonly Vector3 BlenderEyeL = new(-0.37f, -0.64f, -0.185f);
    static readonly Vector3 BlenderEyeR = new(0.285f, -0.64f, -0.01f);
    const float BlenderWidthX = 1.902f;
    const float BlenderBodyRadius = 0.72f;

    public static string ShortClipName(string takeName)
    {
        if (string.IsNullOrEmpty(takeName)) return takeName;
        var i = takeName.LastIndexOf('|');
        return i >= 0 ? takeName.Substring(i + 1) : takeName;
    }

    static Vector3 BlenderToRoot(Vector3 b) => new(b.x, b.z, b.y);

    [MenuItem("Moodium/Moodi/Setup Animated Moodi (All Steps)")]
    public static void SetupAll()
    {
        if (!ConfigureImport()) return;
        var controller = BuildController();
        if (controller == null) return;
        UpgradePrefab(controller);
    }

    [MenuItem("Moodium/Moodi/1. Configure Rigged Model Import")]
    public static void ConfigureImportMenu()=>ConfigureImport();

    public static bool ConfigureImport()
    {
        if (AssetImporter.GetAtPath(ModelPath) is not ModelImporter importer)
        {
            Debug.LogError($"[Moodi Setup] {ModelPath} not found. Copy Moodi_Rigged.fbx there first.");
            return false;
        }
        importer.globalScale = 1f;
        importer.useFileScale = true;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.importCameras = false;
        importer.importLights = false;
        importer.importBlendShapes = false;
        // Smooth normals and 4 influences per vertex are baked into the FBX (rig.py); keep them as-is.
        importer.importNormals = ModelImporterNormals.Import;
        importer.skinWeights = ModelImporterSkinWeights.Standard;
        importer.isReadable = false;
        importer.optimizeGameObjects = false;
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
        importer.importAnimation = true;
        importer.animationCompression = ModelImporterAnimationCompression.KeyframeReduction;
        importer.SaveAndReimport();

        var clips = importer.defaultClipAnimations;
        foreach (var clip in clips)
        {
            clip.name = ShortClipName(clip.takeName);
            clip.loopTime = LoopClips.Contains(clip.name);
            clip.loopPose = false;
            if(clip.name=="Strain"){clip.hasAdditiveReferencePose=true;clip.additiveReferencePoseFrame=0;}
        }
        importer.clipAnimations = clips;
        importer.SaveAndReimport();
        Debug.Log($"[Moodi Setup] Import configured: {string.Join(", ", clips.Select(c => c.name))}");
        return true;
    }

    [MenuItem("Moodium/Moodi/2. Build Animator Controller")]
    public static void BuildControllerMenu()=>BuildController();

    public static AnimatorController BuildController(string outputPath=null)
    {
        var controllerPath=outputPath??ControllerPath;
        var clips = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>()
            .Where(c => !c.name.StartsWith("__preview__")).ToDictionary(c => c.name);
        MoodiEmergenceSetup.UseOriginalClips(clips);
        var missing = Clips.Where(n => !clips.ContainsKey(n)).ToArray();
        if (missing.Length > 0)
        {
            Debug.LogError($"[Moodi Setup] Missing clips {string.Join(", ", missing)}. Run step 1 first.");
            return null;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(controllerPath)!);
        // Reuse the asset so existing prefab/controller references retain their GUID.
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        if(controller==null)controller=AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        // Set atomically: temporarily removing parameters invalidates live Animator bindings.
        controller.parameters=new[]{
            new AnimatorControllerParameter{name="Speed",type=AnimatorControllerParameterType.Float},
            new AnimatorControllerParameter{name="Touched",type=AnimatorControllerParameterType.Trigger},
            new AnimatorControllerParameter{name="Happy",type=AnimatorControllerParameterType.Trigger},
            new AnimatorControllerParameter{name="Wave",type=AnimatorControllerParameterType.Trigger}};

        var sm = controller.layers[0].stateMachine;
        foreach(var transition in sm.anyStateTransitions)sm.RemoveAnyStateTransition(transition);
        AnimatorState State(string name, Vector2 pos)
        {
            var s = sm.states.FirstOrDefault(child=>child.state.name==name).state;
            if(s==null)s=sm.AddState(name,pos);
            s.transitions=System.Array.Empty<AnimatorStateTransition>();
            s.motion = clips[name];
            return s;
        }
        var idle = State("Idle", new Vector2(300, 0));
        var walk = State("Walk", new Vector2(560, -80));
        var touched = State("Touched", new Vector2(300, 220));
        var happy = State("Happy", new Vector2(80, 220));
        var wave = State("Wave", new Vector2(520, 220));
        sm.defaultState = idle;

        void Link(AnimatorState from, AnimatorState to, float duration, params (AnimatorConditionMode mode, float threshold, string parameter)[] conditions)
        {
            var t = from.AddTransition(to);
            t.hasExitTime = false;
            t.duration = duration;
            foreach (var c in conditions) t.AddCondition(c.mode, c.threshold, c.parameter);
        }
        Link(idle, walk, .12f, (AnimatorConditionMode.Greater, .08f, "Speed"));
        Link(walk, idle, .18f, (AnimatorConditionMode.Less, .04f, "Speed"));

        foreach (var (state, trigger) in new[] { (touched, "Touched"), (happy, "Happy"), (wave, "Wave") })
        {
            var any = sm.AddAnyStateTransition(state);
            any.hasExitTime = false;
            any.duration = .06f;
            any.canTransitionToSelf = true;
            any.AddCondition(AnimatorConditionMode.If, 0, trigger);
            var back = state.AddTransition(idle);
            back.hasExitTime = true;
            back.exitTime = .95f;
            back.duration = .15f;
        }
        MoodiEmergenceSetup.AugmentMoodi(controller, clips);
        // Changes to reused sub-assets must be persisted before forcing a reimport.
        foreach(var child in sm.states)
        {
            EditorUtility.SetDirty(child.state);
            foreach(var transition in child.state.transitions)EditorUtility.SetDirty(transition);
        }
        foreach(var transition in sm.anyStateTransitions)EditorUtility.SetDirty(transition);
        EditorUtility.SetDirty(sm);EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Moodi Setup] Animator controller written to {controllerPath}");
        return controller;
    }

    [MenuItem("Moodium/Moodi/3. Upgrade 'Moodi meshy' Prefab")]
    public static void UpgradePrefabMenu()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null) controller = BuildController();
        if (controller != null) UpgradePrefab(controller);
    }

    static void UpgradePrefab(AnimatorController controller)
    {
        var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (modelAsset == null) { Debug.LogError($"[Moodi Setup] {ModelPath} missing."); return; }
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var staticChild = root.transform.Find(StaticChildName);
            var staticFilter = staticChild != null ? staticChild.GetComponentInChildren<MeshFilter>(true) : null;
            if (staticFilter == null || staticFilter.sharedMesh == null)
            {
                Debug.LogError($"[Moodi Setup] Could not find the static child '{StaticChildName}' with a mesh in {PrefabPath}.");
                return;
            }
            var old = root.transform.Find(RiggedChildName);
            // Re-running the setup must not move the model: on the v2 rig the computed alignment came out
            // 180° turned, 39% larger and 6 cm higher (Moodi showed its back in the world selection UI).
            // Keep an existing, already-approved transform; compute one only for a first-time upgrade.
            var keep = old != null;
            var keptPosition = keep ? old.localPosition : Vector3.zero;
            var keptRotation = keep ? old.localRotation : Quaternion.identity;
            var keptScale = keep ? old.localScale : Vector3.one;
            if (old != null) Object.DestroyImmediate(old.gameObject);

            // Units of the prefab root per Blender unit, from the static mesh width (X maps to X).
            var staticScaleX = Mathf.Abs(staticFilter.transform.lossyScale.x / root.transform.lossyScale.x);
            var unitsPerBlender = staticFilter.sharedMesh.bounds.size.x * staticScaleX / BlenderWidthX;

            var rigged = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, root.transform);
            rigged.name = RiggedChildName;
            rigged.transform.localPosition = Vector3.zero;
            rigged.transform.localRotation = Quaternion.identity;
            rigged.transform.localScale = Vector3.one;

            Transform Bone(string name) => rigged.GetComponentsInChildren<Transform>(true).First(t => t.name == name);
            Vector3 InRoot(Transform t) => root.transform.InverseTransformPoint(t.position);
            var body = Bone("Body"); var stem = Bone("Sprout_Stem"); var eyeL = Bone("Eye_L"); var eyeR = Bone("Eye_R");

            // Rotation: align (body → stem) and (left eye → right eye) with the static frame.
            var srcUp = InRoot(stem) - InRoot(body);
            var srcSide = InRoot(eyeR) - InRoot(eyeL);
            var tgtUp = BlenderToRoot(BlenderStemBase - BlenderBodyBottom);
            var tgtSide = BlenderToRoot(BlenderEyeR - BlenderEyeL);
            rigged.transform.localRotation = Quaternion.LookRotation(tgtUp, tgtSide) *
                                             Quaternion.Inverse(Quaternion.LookRotation(srcUp, srcSide));

            // Scale: eye distance must match.
            var srcEyes = (InRoot(eyeR) - InRoot(eyeL)).magnitude;
            var tgtEyes = (BlenderEyeR - BlenderEyeL).magnitude * unitsPerBlender;
            rigged.transform.localScale = Vector3.one * (tgtEyes / Mathf.Max(srcEyes, 1e-6f));

            // Position: the Body bone lands on the static body bottom.
            var tgtBody = BlenderToRoot(BlenderBodyBottom) * unitsPerBlender;
            rigged.transform.localPosition += tgtBody - InRoot(body);
            if (keep)
            {
                rigged.transform.localPosition = keptPosition;
                rigged.transform.localRotation = keptRotation;
                rigged.transform.localScale = keptScale;
            }

            var smr = rigged.GetComponentInChildren<SkinnedMeshRenderer>(true);
            var staticRenderer = staticChild.GetComponentInChildren<Renderer>(true);
            if (smr != null && staticRenderer != null)
            {
                smr.sharedMaterials = staticRenderer.sharedMaterials;
                smr.updateWhenOffscreen = true;
            }

            var animator = rigged.GetComponent<Animator>();
            if (animator == null) animator = rigged.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            staticChild.gameObject.SetActive(false);

            var companion = root.GetComponent<MoodiCompanionAnimator>();
            if (companion == null) companion = root.AddComponent<MoodiCompanionAnimator>();
            companion.Configure(BlenderToRoot(BlenderBodyMid) * unitsPerBlender, BlenderBodyRadius * unitsPerBlender);
            var so = new SerializedObject(companion);
            so.FindProperty("m_Animator").objectReferenceValue = animator;
            so.ApplyModifiedPropertiesWithoutUndo();

            // The Idle clip already breathes; keep the existing float but drop its scale pulse.
            var breathing = root.GetComponent("MoodiBreathingFloat");
            if (breathing != null)
            {
                var bso = new SerializedObject(breathing);
                var amp = bso.FindProperty("m_ScaleAmplitude");
                if (amp != null) { amp.floatValue = 0f; bso.ApplyModifiedPropertiesWithoutUndo(); }
            }

            var eyesMid = (InRoot(eyeL) + InRoot(eyeR)) * .5f;
            var facesMinusZ = eyesMid.z < InRoot(Bone("Body_Upper")).z;
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log($"[Moodi Setup] Upgraded {PrefabPath}: unitsPerBlender={unitsPerBlender:0.####}, " +
                      $"rigScale={rigged.transform.localScale.x:0.####}, rot={rigged.transform.localEulerAngles}, " +
                      $"facesMinusZ={facesMinusZ}. Static child '{StaticChildName}' is hidden, not deleted.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    [MenuItem("Moodium/Moodi/Fix: Turn Animated Moodi 180°")]
    public static void TurnAround()
    {
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var rigged = root.transform.Find(RiggedChildName);
            if (rigged == null) { Debug.LogError("[Moodi Setup] Run the upgrade first."); return; }
            var companion = root.GetComponent<MoodiCompanionAnimator>();
            var pivot = root.transform.TransformPoint(companion != null ? companion.BodyCenterLocal : Vector3.zero);
            rigged.RotateAround(pivot, root.transform.up, 180f);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log("[Moodi Setup] Turned the animated Moodi 180° around its vertical axis.");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    [MenuItem("Moodium/Moodi/Revert 'Moodi meshy' To Static")]
    public static void Revert()
    {
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var rigged = root.transform.Find(RiggedChildName);
            if (rigged != null) Object.DestroyImmediate(rigged.gameObject);
            var staticChild = root.transform.Find(StaticChildName);
            if (staticChild != null) staticChild.gameObject.SetActive(true);
            var companion = root.GetComponent<MoodiCompanionAnimator>();
            if (companion != null) Object.DestroyImmediate(companion);
            var breathing = root.GetComponent("MoodiBreathingFloat");
            if (breathing != null)
            {
                var bso = new SerializedObject(breathing);
                var amp = bso.FindProperty("m_ScaleAmplitude");
                if (amp != null) { amp.floatValue = .01f; bso.ApplyModifiedPropertiesWithoutUndo(); }
            }
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log("[Moodi Setup] 'Moodi meshy' reverted to the static model.");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
}
