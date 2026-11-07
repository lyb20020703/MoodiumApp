using System.Linq;
using System.Reflection;
using FIMSpace.BonesStimulation;
using Moodium.Flow;
using Moodium.Opening;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public sealed class MoodiumPortalSceneValidationTests
{
    const string ScenePath = "Assets/Samples/PolySpatial/Scenes/Meshing 1.unity";

    [TearDown]
    public void TearDown()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
    }

    [Test]
    public void MeshingSceneContainsOneConnectedPortalEntrance()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var portals = Object.FindObjectsByType<MoodiumPortalEntranceController>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        Assert.That(portals, Has.Length.EqualTo(1));
        Assert.That(portals[0].gameObject.activeSelf, Is.False);

        var portalSerialized = new SerializedObject(portals[0]);
        Assert.That(portalSerialized.FindProperty("m_VisualRoot").objectReferenceValue, Is.Not.Null);
        Assert.That(portalSerialized.FindProperty("m_EntryModelRoot").objectReferenceValue, Is.Not.Null);
        Assert.That(portalSerialized.FindProperty("m_ManualContactCollider").objectReferenceValue, Is.Not.Null);

        var flow = Object.FindFirstObjectByType<MoodiumAppFlowController>(FindObjectsInactive.Include);
        Assert.That(flow, Is.Not.Null);
        var flowSerialized = new SerializedObject(flow);
        Assert.That(flowSerialized.FindProperty("m_PortalEntrance").objectReferenceValue, Is.EqualTo(portals[0]));
    }

    [Test]
    public void PortalPrefabContainsCutSphereWithSampleSkyboxMaterial()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/prefab/MoodiumOpening/MoodiumPortalEntrance.prefab");
        var skyboxMaterial = AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/Samples/PolySpatial/Portal/Materials/Skybox.mat");

        Assert.That(prefab, Is.Not.Null);
        Assert.That(skyboxMaterial, Is.Not.Null);
        var cutSphere = prefab.transform.Find("PortalVisualRoot/PortalContentRoot/CutSphereBackground");
        Assert.That(cutSphere, Is.Not.Null);
        var renderer = cutSphere.GetComponentInChildren<Renderer>(true);
        Assert.That(renderer, Is.Not.Null);
        Assert.That(renderer.sharedMaterial, Is.EqualTo(skyboxMaterial));
    }

    [Test]
    public void PortalPrefabRemovesTheLegacyHaloAndConfiguresTheBagSequence()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/prefab/MoodiumOpening/MoodiumPortalEntrance.prefab");
        var controller = prefab.GetComponent<MoodiumPortalEntranceController>();
        var serialized = new SerializedObject(controller);

        Assert.That(prefab.transform.Find("PortalVisualRoot/PortalContentRoot/PortalHalo"), Is.Null);
        Assert.That(serialized.FindProperty("m_BagRain").objectReferenceValue, Is.Not.Null);
    }

    [Test]
    public void PortalEntryPromptMatchesTheApprovedLayoutAndIntroHold()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/prefab/MoodiumOpening/MoodiumPortalEntrance.prefab");
        var prompt = prefab.transform.Find("PortalVisualRoot/EntryPrompt").GetComponent<RectTransform>();
        var text = prompt.GetComponent<TMPro.TMP_Text>();
        var serialized = new SerializedObject(prefab.GetComponent<MoodiumPortalEntranceController>());

        Assert.That(prompt.anchoredPosition.x, Is.EqualTo(0f).Within(0.001f));
        Assert.That(prompt.anchoredPosition.y, Is.EqualTo(-0.161f).Within(0.001f));
        Assert.That(prompt.localPosition.z, Is.EqualTo(-0.769f).Within(0.001f));
        Assert.That(prompt.sizeDelta, Is.EqualTo(new Vector2(1.2f, 0.18f)));
        Assert.That(prompt.localEulerAngles.y, Is.EqualTo(180f).Within(0.01f));
        Assert.That(prompt.localScale, Is.EqualTo(new Vector3(-1f, 1f, 1f)));
        Assert.That(text.fontSize, Is.EqualTo(0.3f).Within(0.001f));
        Assert.That(prompt.GetComponent<Renderer>().allowOcclusionWhenDynamic, Is.False,
            "The portal subtitle must not be dynamically occluded on device.");
        Assert.That(serialized.FindProperty("m_MoodiIntroMinimumDuration").floatValue,
            Is.EqualTo(3f).Within(0.001f));
    }

    [Test]
    public void PortalLanguageButtonsUseDarkRoundedGlassStyle()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/prefab/MoodiumOpening/MoodiumPortalEntrance.prefab");
        var instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
        instance.SetActive(true);
        var controller = instance.GetComponent<MoodiumPortalEntranceController>();
        typeof(MoodiumPortalEntranceController)
            .GetMethod("ConfigureLanguageChoice", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(controller, null);
        var button = instance.transform.Find(
            "PortalVisualRoot/EntryPrompt/Moodi Language Choice/Language Choice Buttons/Language Button - Chinese");

        Assert.That(button, Is.Not.Null);
        var image = button.GetComponent<Image>();
        Assert.That(image.type, Is.EqualTo(Image.Type.Sliced));
        Assert.That(image.color.r, Is.LessThan(0.2f));
        Assert.That(image.color.g, Is.LessThan(0.25f));
        Assert.That(image.color.b, Is.GreaterThan(image.color.r));
        Assert.That(button.GetComponent<Outline>(), Is.Not.Null);
        Object.DestroyImmediate(instance);
    }

    [Test]
    public void MoodiTrailUsesAShortSparseParticleWake()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/prefab/MoodiumOpening/MoodiumPortalEntrance.prefab");
        var instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
        instance.SetActive(true);
        var trail = instance.GetComponentInChildren<ParticleSystem>(true);
        foreach (var candidate in instance.GetComponentsInChildren<ParticleSystem>(true))
            if (candidate.name == "Moodi Loose Trail Particles") trail = candidate;

        Assert.That(trail, Is.Not.Null);
        Assert.That(trail.emission.rateOverTime.constant, Is.LessThanOrEqualTo(8f));
        Assert.That(trail.main.startLifetime.constantMax, Is.LessThanOrEqualTo(1.35f));
        Object.DestroyImmediate(instance);
    }

    [Test]
    public void PortalPrefabUsesMoodiWithATriggerContactVolume()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/prefab/MoodiumOpening/MoodiumPortalEntrance.prefab");
        var moodi = prefab.transform.Find("PortalVisualRoot/PortalContentRoot/EntryModelRoot_Moodi");

        Assert.That(moodi, Is.Not.Null);
        Assert.That(moodi.GetComponentInChildren<Renderer>(true), Is.Not.Null);
        var contact = moodi.GetComponent<BoxCollider>();
        Assert.That(contact, Is.Not.Null);
        Assert.That(contact.isTrigger, Is.True);
        Assert.That(moodi.GetComponent<Rigidbody>(), Is.Null);
    }

    [Test]
    public void MoodiEmergesToTheUserFacingLocalZPosition()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/prefab/MoodiumOpening/MoodiumPortalEntrance.prefab");
        var moodi = prefab.transform.Find("PortalVisualRoot/PortalContentRoot/EntryModelRoot_Moodi");

        Assert.That(moodi.localPosition.z, Is.EqualTo(-1f));
    }

    [Test]
    public void PortalPrefabUsesAnimatedMoodiWithLoopingBlinkAndWinkTrigger()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/prefab/MoodiumOpening/MoodiumPortalEntrance.prefab");
        var moodi = prefab.transform.Find("PortalVisualRoot/PortalContentRoot/EntryModelRoot_Moodi");
        var controller = prefab.GetComponent<MoodiumPortalEntranceController>();
        var serialized = new SerializedObject(controller);
        var animationRoot = moodi.Find("IP_Unity_3Clips");
        var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>(
            "Assets/Animations/MoodiumPortal/Moodi_Idle_Blink_Loop.anim");
        var animatorController = AssetDatabase.LoadAssetAtPath<AnimatorController>(
            "Assets/Animations/MoodiumPortal/MoodiPortal.controller");

        Assert.That(moodi, Is.Not.Null);
        Assert.That(moodi.GetComponent<Animator>(), Is.Null);
        Assert.That(animationRoot, Is.Not.Null);
        Assert.That(animationRoot.GetComponent<Animator>(), Is.Not.Null);
        Assert.That(serialized.FindProperty("m_EntryAnimator").objectReferenceValue,
            Is.EqualTo(animationRoot.GetComponent<Animator>()));
        Assert.That(idle, Is.Not.Null);
        Assert.That(idle.isLooping, Is.True);
        Assert.That(animatorController, Is.Not.Null);
        Assert.That(animatorController.parameters, Has.Some.Matches<AnimatorControllerParameter>(p =>
            p.name == "Wink" && p.type == AnimatorControllerParameterType.Trigger));
    }

    [Test]
    public void PortalPrefabConfiguresSlowSwayingMoodiEntrance()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/prefab/MoodiumOpening/MoodiumPortalEntrance.prefab");
        var serialized = new SerializedObject(prefab.GetComponent<MoodiumPortalEntranceController>());

        Assert.That(serialized.FindProperty("m_EntryEmergeDuration").floatValue, Is.EqualTo(2.4f));
        var sway = serialized.FindProperty("m_EntrySwayAmplitude");
        var cycles = serialized.FindProperty("m_EntrySwayCycles");
        Assert.That(sway, Is.Not.Null);
        Assert.That(cycles, Is.Not.Null);
        Assert.That(sway.floatValue, Is.EqualTo(0.08f));
        Assert.That(cycles.intValue, Is.EqualTo(2));
    }

    [Test]
    public void PortalPrefabAddsGentleBonesStimulationOnlyToMoodiSproutAndTail()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/prefab/MoodiumOpening/MoodiumPortalEntrance.prefab");
        var moodi = prefab.transform.Find("PortalVisualRoot/PortalContentRoot/EntryModelRoot_Moodi");
        var body = moodi.Find("IP_Unity_3Clips/Lilac_Sprout/RIG_Sprout/ROOT/Body");

        Assert.That(body, Is.Not.Null);
        var sprout = body.Find("Sprout");
        var tail = body.Find("Tail");
        Assert.That(sprout, Is.Not.Null);
        Assert.That(tail, Is.Not.Null);

        var stimulators = moodi.GetComponentsInChildren<BonesStimulator>(true);
        Assert.That(stimulators, Has.Length.EqualTo(2));
        foreach (var stimulator in stimulators)
        {
            Assert.That(stimulator.CompensationTransform, Is.EqualTo(moodi));
            Assert.That(stimulator.MovementMuscles, Is.EqualTo(0f));
            Assert.That(stimulator.RotationSpaceMuscles, Is.EqualTo(0.12f));
            Assert.That(stimulator.VibrateAmount, Is.EqualTo(0.15f));
            Assert.That(stimulator.VibrateSpeed, Is.EqualTo(1.2f));
            Assert.That(stimulator.VibratePosition, Is.EqualTo(0f));
            Assert.That(stimulator.VibrateScale, Is.EqualTo(0f));
            Assert.That(stimulator.UseCollisions, Is.False);
        }

        Assert.That(sprout.GetComponent<BonesStimulator>(), Is.Not.Null);
        Assert.That(tail.GetComponent<BonesStimulator>(), Is.Not.Null);
    }

    [Test]
    public void PortalPrefabUsesASeparateSoftEmissiveMoodiSkinMaterial()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/prefab/MoodiumOpening/MoodiumPortalEntrance.prefab");
        var material = AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/Materials/MoodiumPortal/MoodiPortalSkin_Emission.mat");
        var moodi = prefab.transform.Find("PortalVisualRoot/PortalContentRoot/EntryModelRoot_Moodi");
        var body = moodi.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .First(renderer => renderer.name.StartsWith("Body"));

        Assert.That(material, Is.Not.Null);
        Assert.That(material.shader.name, Is.EqualTo("Universal Render Pipeline/Lit"));
        Assert.That(material.IsKeywordEnabled("_EMISSION"), Is.True);
        Assert.That(material.GetColor("_EmissionColor").maxColorComponent, Is.GreaterThan(0.1f));
        Assert.That(body.sharedMaterial, Is.EqualTo(material));
    }

    [Test]
    public void PortalAnimationOpensTheSidePlanesToTheWiderPositions()
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(
            "Assets/Animations/MoodiumPortal/MoodiumPortalOpen.anim");
        var leftBinding = AnimationUtility.GetCurveBindings(clip)
            .First(binding => binding.path == "PortalPlaneLeft" && binding.propertyName == "m_LocalPosition.x");
        var rightBinding = AnimationUtility.GetCurveBindings(clip)
            .First(binding => binding.path == "PortalPlaneRight" && binding.propertyName == "m_LocalPosition.x");

        Assert.That(AnimationUtility.GetEditorCurve(clip, leftBinding).Evaluate(1f), Is.EqualTo(-1.15f));
        Assert.That(AnimationUtility.GetEditorCurve(clip, rightBinding).Evaluate(1f), Is.EqualTo(1.15f));
    }

    [Test]
    public void PortalTopAndBottomPlanesSpanTheWiderOpening()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/prefab/MoodiumOpening/MoodiumPortalEntrance.prefab");
        var visual = prefab.transform.Find("PortalVisualRoot");

        Assert.That(visual.Find("PortalPlaneTop").localScale.x, Is.EqualTo(2f));
        Assert.That(visual.Find("PortalPlaneBottom").localScale.x, Is.EqualTo(2f));
    }

    [Test]
    public void PortalPrefabAddsAKinematicBagPusherForMoodiEmergence()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/prefab/MoodiumOpening/MoodiumPortalEntrance.prefab");
        var moodi = prefab.transform.Find("PortalVisualRoot/PortalContentRoot/EntryModelRoot_Moodi");
        var pusher = moodi.Find("MoodiBagPusher");
        var serialized = new SerializedObject(prefab.GetComponent<MoodiumPortalEntranceController>());

        Assert.That(pusher, Is.Not.Null);
        Assert.That(pusher.GetComponent<BoxCollider>().isTrigger, Is.False);
        Assert.That(pusher.GetComponent<Rigidbody>().isKinematic, Is.True);
        Assert.That(serialized.FindProperty("m_EntryBagPusher").objectReferenceValue,
            Is.EqualTo(pusher.GetComponent<Collider>()));
    }
}
