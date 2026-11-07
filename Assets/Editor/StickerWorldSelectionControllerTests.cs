using Moodium.Flow;
using Moodium.Opening;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Video;
using UnityEngine.XR.ARSubsystems;
using UnityEditor;
using Moodium.MoodCheck;

public sealed class StickerWorldSelectionControllerTests
{
    static readonly TrackableId CandyId = new(1, 2);
    static readonly TrackableId NatureId = new(3, 4);
    GameObject m_Root;
    StickerWorldSelectionController m_Controller;
    MoodiumWorldDefinition m_CandyWorld;
    MoodiumWorldDefinition m_NatureWorld;

    [SetUp]
    public void SetUp()
    {
        m_Root = new GameObject("Sticker World Selection Test Root");
        m_Controller = m_Root.AddComponent<StickerWorldSelectionController>();
        m_CandyWorld = ScriptableObject.CreateInstance<MoodiumWorldDefinition>();
        m_NatureWorld = ScriptableObject.CreateInstance<MoodiumWorldDefinition>();
        var candySerialized = new SerializedObject(m_CandyWorld);
        candySerialized.FindProperty("m_WorldId").stringValue = "candy";
        candySerialized.FindProperty("m_DisplayName").stringValue = "糖果世界";
        candySerialized.ApplyModifiedPropertiesWithoutUndo();
        var natureSerialized = new SerializedObject(m_NatureWorld);
        natureSerialized.FindProperty("m_WorldId").stringValue = "nature";
        natureSerialized.FindProperty("m_DisplayName").stringValue = "自然世界";
        natureSerialized.ApplyModifiedPropertiesWithoutUndo();
        m_Controller.ConfigureForTests(
            new GameObject("Candy UI Prefab"),
            new GameObject("Nature UI Prefab"),
            m_CandyWorld,
            m_NatureWorld);
        m_Controller.Begin(new OpeningVideoSession(m_Root.AddComponent<VideoPlayer>(), m_Root.transform, null, false, () => { }));
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(m_CandyWorld);
        Object.DestroyImmediate(m_NatureWorld);
        Object.DestroyImmediate(m_Root);
    }

    [Test]
    public void MoodPanelSuspensionHidesEntriesAndBlocksTrackingAndSelection()
    {
        var anchor = new GameObject("Suspension anchor").transform;
        anchor.SetParent(m_Root.transform);
        m_Controller.HandleTrackedImageUpdate(CandyId, "CandyWorldPhoneTracking", anchor, TrackingState.Tracking);
        var selected = 0;
        m_Controller.WorldEntrySelected += _ => selected++;
        m_Controller.SetMoodCheckSuspended(true);
        Assert.That(m_Controller.GetEntry(CandyId).Root.activeSelf, Is.False);
        m_Controller.HandleTrackedImageUpdate(NatureId, "NatureWorldPhoneTracking", anchor, TrackingState.Tracking);
        Assert.That(m_Controller.ActiveEntryCount, Is.EqualTo(1));
        m_Controller.SelectEntry(CandyId);
        Assert.That(selected, Is.Zero);
        m_Controller.SetMoodCheckSuspended(false);
        Assert.That(m_Controller.GetEntry(CandyId).Root.activeSelf, Is.True);
        m_Controller.SelectEntry(CandyId);
        Assert.That(selected, Is.EqualTo(1));
    }

    [Test]
    public void TrackedCandyAndNatureImages_CreateIndependentMatchingEntries()
    {
        var candyAnchor = new GameObject("Candy Anchor").transform;
        var natureAnchor = new GameObject("Nature Anchor").transform;
        candyAnchor.SetParent(m_Root.transform);
        natureAnchor.SetParent(m_Root.transform);

        m_Controller.HandleTrackedImageUpdate(CandyId, "CandyWorldPhoneTracking", candyAnchor, TrackingState.Tracking);
        m_Controller.HandleTrackedImageUpdate(NatureId, "NatureWorldPhoneTracking", natureAnchor, TrackingState.Tracking);

        Assert.That(m_Controller.ActiveEntryCount, Is.EqualTo(2));
        Assert.That(m_Controller.GetEntry(CandyId).World, Is.SameAs(m_CandyWorld));
        Assert.That(m_Controller.GetEntry(NatureId).World, Is.SameAs(m_NatureWorld));
    }

    [Test]
    public void VisionOSUuidSuffixedReferenceImage_CreatesMatchingNatureEntry()
    {
        var anchor = new GameObject("Nature Anchor").transform;
        anchor.SetParent(m_Root.transform);

        m_Controller.HandleTrackedImageUpdate(
            NatureId,
            "NatureWorldPhoneTracking_8FEF101E-8126-4652-B006-F4793992AFA9",
            anchor,
            TrackingState.Tracking);

        Assert.That(m_Controller.ActiveEntryCount, Is.EqualTo(1));
        Assert.That(m_Controller.GetEntry(NatureId).World, Is.SameAs(m_NatureWorld));
    }

    [Test]
    public void WorldEntry_UsesGlassSliderWithArrowInsteadOfTextOnlyButton()
    {
        var anchor = new GameObject("Candy Anchor").transform;
        anchor.SetParent(m_Root.transform);
        m_Controller.HandleTrackedImageUpdate(CandyId, "CandyWorldPhoneTracking", anchor, TrackingState.Tracking);

        var button = m_Controller.GetEntry(CandyId).Root.transform.Find("Enter World Button");
        Assert.That(button.Find("Glass Track"), Is.Not.Null);
        Assert.That(button.Find("Slide Handle"), Is.Not.Null);
        Assert.That(button.Find("Slide Handle/Arrow"), Is.Not.Null);
        Assert.That(button.Find("Button Label").GetComponent<TMPro.TMP_Text>().text, Is.EqualTo("进入糖果世界"));
    }

    [Test]
    public void TrackingLoss_HidesOnlyAffectedEntry_AndRecoveryReusesIt()
    {
        var candyAnchor = new GameObject("Candy Anchor").transform;
        var natureAnchor = new GameObject("Nature Anchor").transform;
        candyAnchor.SetParent(m_Root.transform);
        natureAnchor.SetParent(m_Root.transform);
        m_Controller.HandleTrackedImageUpdate(CandyId, "CandyWorldPhoneTracking", candyAnchor, TrackingState.Tracking);
        m_Controller.HandleTrackedImageUpdate(NatureId, "NatureWorldPhoneTracking", natureAnchor, TrackingState.Tracking);
        var originalCandyRoot = m_Controller.GetEntry(CandyId).Root;

        m_Controller.HandleTrackedImageUpdate(CandyId, "CandyWorldPhoneTracking", candyAnchor, TrackingState.None);

        Assert.That(originalCandyRoot.activeSelf, Is.False);
        Assert.That(m_Controller.GetEntry(NatureId).Root.activeSelf, Is.True);

        m_Controller.HandleTrackedImageUpdate(CandyId, "CandyWorldPhoneTracking", candyAnchor, TrackingState.Tracking);

        Assert.That(m_Controller.ActiveEntryCount, Is.EqualTo(2));
        Assert.That(m_Controller.GetEntry(CandyId).Root, Is.SameAs(originalCandyRoot));
        Assert.That(originalCandyRoot.activeSelf, Is.True);
    }

    [Test]
    public void UnsupportedReferenceImage_DoesNotCreateEntry()
    {
        var anchor = new GameObject("Unsupported Anchor").transform;
        anchor.SetParent(m_Root.transform);

        m_Controller.HandleTrackedImageUpdate(new TrackableId(7, 8), "NotMoodium", anchor, TrackingState.Tracking);

        Assert.That(m_Controller.ActiveEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void CompensatedTextScale_CancelsNonUniformParentScale()
    {
        var result = StickerWorldSelectionController.GetCompensatedLocalScale(
            .045f, new Vector3(1.28f, .72f, 1f));

        Assert.That(result, Is.EqualTo(new Vector3(.045f / 1.28f, .045f / .72f, .045f)));
    }

    [Test]
    public void DefaultEntryOffset_IsRaisedAndRecessedFromSticker()
    {
        Assert.That(m_Controller.EntryLocalPosition.y, Is.GreaterThanOrEqualTo(.12f));
        Assert.That(m_Controller.EntryLocalPosition.z, Is.GreaterThanOrEqualTo(.10f));
    }

    [Test]
    public void MoodEntry_ChangesSummaryAndRaisesRequest()
    {
        var requests = 0;
        m_Controller.MoodCheckRequested += () => requests++;
        m_Controller.SetMoodCheckSummary(new MoodCheckSample
        {
            mood = MoodKind.Tense,
            recommendedWorldId = "nature"
        });

        Assert.That(m_Controller.MoodCheckButton.Label.text, Does.Contain("烦躁"));
        Assert.That(m_Controller.MoodCheckButton.Label.text, Does.Contain("自然世界"));
        m_Controller.MoodCheckButton.Press();
        Assert.That(requests, Is.EqualTo(1));
    }

    [Test]
    public void MoodEntry_UsesAuthoredGlassSpriteAndReadableHitArea()
    {
        var root = m_Controller.MoodCheckButton.transform;
        var renderer = root.GetComponent<SpriteRenderer>();
        Assert.That(renderer, Is.Not.Null);
        Assert.That(renderer.sprite, Is.Not.Null);
        Assert.That(root.GetComponent<BoxCollider>().size.y, Is.EqualTo(.056f).Within(.0001f));
        Assert.That(m_Controller.MoodCheckButton.Label.GetComponent<Renderer>().enabled, Is.False);
    }

    [Test]
    public void RecommendationTag_AttachesOnlyToMatchingEntryWithoutDisablingButtons()
    {
        m_Controller.SetRecommendedWorld("nature");
        var candyAnchor = new GameObject("Candy Anchor").transform;
        var natureAnchor = new GameObject("Nature Anchor").transform;
        candyAnchor.SetParent(m_Root.transform);
        natureAnchor.SetParent(m_Root.transform);
        m_Controller.HandleTrackedImageUpdate(CandyId, "CandyWorldPhoneTracking", candyAnchor, TrackingState.Tracking);
        m_Controller.HandleTrackedImageUpdate(NatureId, "NatureWorldPhoneTracking", natureAnchor, TrackingState.Tracking);

        Assert.That(m_Controller.GetEntry(CandyId).Root.transform.Find("Moodi Recommendation Tag"), Is.Null);
        var tag = m_Controller.GetEntry(NatureId).Root.transform.Find("Moodi Recommendation Tag");
        Assert.That(tag, Is.Not.Null);
        Assert.That(tag.localScale.x, Is.InRange(1f, 1.05f));
        Assert.That(m_Controller.GetEntry(CandyId).Root.transform.Find("Enter World Button").gameObject.activeSelf, Is.True);
        Assert.That(m_Controller.GetEntry(NatureId).Root.transform.Find("Enter World Button").gameObject.activeSelf, Is.True);
    }

    [Test]
    public void UprightEntryRotation_KeepsWorldUp_WhenStickerIsVertical()
    {
        var rotation = StickerWorldEntryUprightFollower.CalculateUprightRotation(
            Vector3.zero, new Vector3(1f, 1f, 2f));

        Assert.That(Vector3.Angle(rotation * Vector3.up, Vector3.up), Is.LessThan(0.01f));
        Assert.That(Vector3.Dot(rotation * Vector3.forward, Vector3.up), Is.EqualTo(0f).Within(0.001f));
        Assert.That(Vector3.Dot(rotation * Vector3.back, new Vector3(1f, 0f, 2f).normalized),
            Is.GreaterThan(0.99f));
    }
}
