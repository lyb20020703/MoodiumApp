using System;
using System.Linq;
using Moodium;
using Moodium.Interaction;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class MoodiumChocolateCapsuleInteractionSetup
{
    const string FbxPath = "Assets/Model/Chocolate_Capsule.fbx";
    const string PrefabPath = "Assets/prefab/Chocolate_Capsule.prefab";
    const string CombinedClipPath = "Assets/Animations/ChocolateCrack_Combined.anim";
    const string ControllerPath = "Assets/Animations/ChocolateCapsule.controller";

    [MenuItem("Moodium/Setup Chocolate Capsule Interaction")]
    public static void Setup()
    {
        EnsureFolder("Assets/Animations");
        var combinedClip = BuildCombinedClip();
        var controller = ConfigureController(combinedClip);
        ConfigurePrefab(controller);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log(
            $"MOODIUM_CHOCOLATE_INTERACTION_READY={PrefabPath}; " +
            $"clip={CombinedClipPath}; controller={ControllerPath}; pieces=20");
    }

    static AnimationClip BuildCombinedClip()
    {
        var importedClips = AssetDatabase.LoadAllAssetsAtPath(FbxPath)
            .OfType<AnimationClip>()
            .Where(clip => !clip.name.StartsWith("__preview__", StringComparison.Ordinal))
            .ToArray();

        var combined = AssetDatabase.LoadAssetAtPath<AnimationClip>(CombinedClipPath);
        if (combined == null)
        {
            combined = new AnimationClip { name = "ChocolateCrack_Combined" };
            AssetDatabase.CreateAsset(combined, CombinedClipPath);
        }
        else
        {
            combined.ClearCurves();
        }

        combined.frameRate = 30f;
        var copiedBindings = 0;
        for (var i = 1; i <= 20; i++)
        {
            var pieceName = $"CrackPiece_{i:00}";
            var expectedClipName = $"{pieceName}|ChocolateCrack_{pieceName}";
            var source = importedClips.FirstOrDefault(clip => clip.name == expectedClipName);
            if (source == null)
                throw new InvalidOperationException($"Required piece clip is missing: {expectedClipName}");

            var expectedPathSuffix = $"Shell_Crack_Pieces_New/{pieceName}";
            foreach (var binding in AnimationUtility.GetCurveBindings(source))
            {
                if (!binding.path.EndsWith(expectedPathSuffix, StringComparison.Ordinal))
                    continue;
                var curve = AnimationUtility.GetEditorCurve(source, binding);
                AnimationUtility.SetEditorCurve(combined, binding, curve);
                copiedBindings++;
            }

            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(source))
            {
                if (!binding.path.EndsWith(expectedPathSuffix, StringComparison.Ordinal))
                    continue;
                var keys = AnimationUtility.GetObjectReferenceCurve(source, binding);
                AnimationUtility.SetObjectReferenceCurve(combined, binding, keys);
                copiedBindings++;
            }
        }

        if (copiedBindings < 200)
            throw new InvalidOperationException($"Combined crack clip has too few bindings: {copiedBindings}; expected at least 200.");

        var settings = AnimationUtility.GetAnimationClipSettings(combined);
        settings.loopTime = false;
        settings.loopBlend = false;
        settings.stopTime = 1f;
        AnimationUtility.SetAnimationClipSettings(combined, settings);
        EditorUtility.SetDirty(combined);
        Debug.Log($"MOODIUM_CHOCOLATE_COMBINED_CLIP={combined.name}; bindings={copiedBindings}; length={combined.length:F3}s");
        return combined;
    }

    static AnimatorController ConfigureController(AnimationClip clip)
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
            controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

        var stateMachine = controller.layers[0].stateMachine;
        var state = stateMachine.states
            .Select(item => item.state)
            .FirstOrDefault(item => item.name == "ChocolateCrack");
        if (state == null)
            state = stateMachine.AddState("ChocolateCrack");
        state.motion = clip;
        state.speed = 1f;
        state.writeDefaultValues = true;
        stateMachine.defaultState = state;
        EditorUtility.SetDirty(state);
        EditorUtility.SetDirty(controller);
        return controller;
    }

    static void ConfigurePrefab(RuntimeAnimatorController controller)
    {
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var animator = root.GetComponentInChildren<Animator>(true);
            var normalShell = FindChild(root.transform, "Shell_Normal");
            var crackRoot = FindChild(root.transform, "Shell_Crack_Pieces_New");
            if (animator == null || normalShell == null || crackRoot == null)
                throw new InvalidOperationException(
                    $"Chocolate prefab structure is incomplete. Animator={animator != null}, " +
                    $"Shell_Normal={normalShell != null}, CrackRoot={crackRoot != null}");

            var pieces = crackRoot.GetComponentsInChildren<Transform>(true)
                .Count(item => item.name.StartsWith("CrackPiece_", StringComparison.Ordinal));
            if (pieces != 20)
                throw new InvalidOperationException($"Expected 20 crack pieces, found {pieces}.");

            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.speed = 0f;

            var interaction = root.GetComponent<ChocolateCapsuleInteraction>();
            if (interaction == null)
                interaction = root.AddComponent<ChocolateCapsuleInteraction>();
            interaction.Configure(animator, normalShell.gameObject, crackRoot.gameObject);
            interaction.SetInteractionEnabled(false);
            interaction.SetSpatialPointerEnabled(true);

            var oldPinch = root.GetComponent<ChocolatePinchCrackController>();
            if (oldPinch != null)
                UnityEngine.Object.DestroyImmediate(oldPinch);
            var autoLoop = root.GetComponent<ChocolateCrackAutoLoopDebug>();
            if (autoLoop != null)
                UnityEngine.Object.DestroyImmediate(autoLoop);

            normalShell.gameObject.SetActive(true);
            crackRoot.gameObject.SetActive(false);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static Transform FindChild(Transform root, string childName)
    {
        foreach (var item in root.GetComponentsInChildren<Transform>(true))
        {
            if (item.name == childName)
                return item;
        }
        return null;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;
        var slash = path.LastIndexOf('/');
        AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
    }
}
