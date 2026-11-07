using System.Linq;
using Moodium.MoodCheck;
using NUnit.Framework;
using TMPro;
using Unity.PolySpatial;
using UnityEditor;
using UnityEngine;

public sealed class MoodCheckControllerTests
{
    GameObject m_Host;
    GameObject m_CameraObject;
    MoodCheckController m_Controller;

    [SetUp]
    public void SetUp()
    {
        MoodCheckUISetup.BuildOrUpdate();
        m_Host = new GameObject("Mood Check Host");
        m_Controller = m_Host.AddComponent<MoodCheckController>();
        m_CameraObject = new GameObject("Mood Camera");
        var camera = m_CameraObject.AddComponent<Camera>();
        camera.transform.position = new Vector3(0f, 1.6f, 0f);
        camera.transform.rotation = Quaternion.identity;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/UI/Moodium/MoodCheck/Generated/MoodCheckPanel.prefab");
        m_Controller.Configure(prefab, camera);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(m_Host);
        Object.DestroyImmediate(m_CameraObject);
    }

    [Test]
    public void PreCheck_TransitionsThroughConfirmationRecommendationAndCompletionOnce()
    {
        var completions = 0;
        MoodCheckSample completed = null;
        m_Controller.Completed += sample => { completions++; completed = sample; };
        m_Controller.BeginPreCheck(new MoodCheckAvailability(true, true), false);
        Assert.That(m_Controller.State, Is.EqualTo(MoodCheckState.Dragging));

        m_Controller.QuickSelect(MoodKind.Tense);
        Assert.That(m_Controller.State, Is.EqualTo(MoodCheckState.Confirming));
        Assert.That(m_Controller.Plate.NormalizedPosition, Is.EqualTo(new Vector2(-.6f, .6f)));
        m_Controller.Confirm();
        Assert.That(m_Controller.State, Is.EqualTo(MoodCheckState.Recommending));
        Assert.That(completions, Is.Zero);
        Assert.That(m_Controller.PanelTransform.Find("RecommendationCard").gameObject.activeSelf, Is.True);
        Assert.That(m_Controller.PanelTransform.Find("Footer/ContinueButton").gameObject.activeSelf, Is.False);
        Assert.That(m_Controller.PanelTransform.Find("RecommendationCard/Find Sticker"), Is.Not.Null);
        Assert.That(m_Controller.PanelTransform.Find("RecommendationCard/Check Again"), Is.Not.Null);
        Assert.That(m_Controller.PanelTransform.Find("Footer/RetryButton").gameObject.activeSelf, Is.False);
        m_Controller.AcceptRecommendation();
        m_Controller.AcceptRecommendation();

        Assert.That(m_Controller.State, Is.EqualTo(MoodCheckState.Hidden));
        Assert.That(completions, Is.EqualTo(1));
        Assert.That(completed.inputMethod, Is.EqualTo(MoodInputMethod.Quick));
        Assert.That(completed.recommendedWorldId, Is.EqualTo("nature"));
        Assert.That(m_Controller.IsVisible, Is.False);
    }

    [Test]
    public void Adjust_PreservesPositionAndReturnsToDragging()
    {
        m_Controller.BeginPreCheck(new MoodCheckAvailability(true, true), true);
        m_Controller.QuickSelect(MoodKind.Low);
        var before = m_Controller.Plate.NormalizedPosition;
        m_Controller.Adjust();
        Assert.That(m_Controller.State, Is.EqualTo(MoodCheckState.Dragging));
        Assert.That(m_Controller.Plate.NormalizedPosition, Is.EqualTo(before));
    }

    [Test]
    public void MovingPlate_UpdatesMoodNameAndDescriptionImmediately()
    {
        m_Controller.BeginPreCheck(new MoodCheckAvailability(true, true), false);
        m_Controller.Plate.SetNormalizedPosition(new Vector2(-.6f, .6f), true);

        Assert.That(m_Controller.PanelTransform.Find("Readout/MoodName").GetComponent<TMPro.TMP_Text>().text,
            Is.EqualTo("烦躁"));
        Assert.That(m_Controller.PanelTransform.Find("Readout/MoodDescription").GetComponent<TMPro.TMP_Text>().text,
            Is.EqualTo("紧张、有压力"));
    }

    [Test]
    public void SkipAndImmediateDismiss_AreIdempotentAndDoNotComplete()
    {
        var skipped = 0;
        var completed = 0;
        m_Controller.Skipped += () => skipped++;
        m_Controller.Completed += _ => completed++;
        m_Controller.BeginPreCheck(new MoodCheckAvailability(true, true), false);
        m_Controller.Skip();
        m_Controller.Skip();
        Assert.That(skipped, Is.EqualTo(1));
        Assert.That(completed, Is.Zero);

        m_Controller.BeginPreCheck(new MoodCheckAvailability(true, true), false);
        m_Controller.DismissImmediate();
        Assert.That(skipped, Is.EqualTo(1));
        Assert.That(completed, Is.Zero);
        Assert.That(m_Controller.State, Is.EqualTo(MoodCheckState.Hidden));
    }

    [Test]
    public void PostCheck_StartsAtPrePositionAndCompletesAfterComparisonContinue()
    {
        var pre = new MoodCheckSample { valence = -.6f, arousal = -.6f, mood = MoodKind.Low };
        var completions = 0;
        m_Controller.Completed += _ => completions++;
        m_Controller.BeginPostCheck(pre, new MoodVisitMetrics(75f, 4), false);
        Assert.That(m_Controller.Plate.NormalizedPosition, Is.EqualTo(new Vector2(-.6f, -.6f)));
        m_Controller.QuickSelect(MoodKind.Calm);
        m_Controller.Confirm();
        Assert.That(m_Controller.State, Is.EqualTo(MoodCheckState.Comparing));
        Assert.That(completions, Is.Zero);
        Assert.That(m_Controller.PanelTransform.Find("ComparisonCard/Result").GetComponent<TMPro.TMP_Text>().text,
            Is.Not.Empty);
        m_Controller.ContinueComparison();
        Assert.That(completions, Is.EqualTo(1));
        Assert.That(m_Controller.PendingSample.durationInWorldSeconds, Is.EqualTo(75f));
        Assert.That(m_Controller.PendingSample.pinchCount, Is.EqualTo(4));
    }

    [Test]
    public void Panel_IsPlacedOnceAheadAndBelowCameraUsingYawOnly()
    {
        m_CameraObject.transform.rotation = Quaternion.Euler(25f, 90f, 15f);
        m_Controller.BeginPreCheck(new MoodCheckAvailability(true, true), false);
        var expected = new Vector3(.75f, 1.48f, 0f);
        Assert.That(Vector3.Distance(m_Controller.PanelTransform.position, expected), Is.LessThan(.001f));
        Assert.That(m_Controller.PanelTransform.eulerAngles.x, Is.EqualTo(0f).Within(.001f));
        var fixedPosition = m_Controller.PanelTransform.position;
        m_CameraObject.transform.position += Vector3.one;
        Assert.That(m_Controller.PanelTransform.position, Is.EqualTo(fixedPosition));
    }

    [Test]
    public void NoAvailableWorld_KeepsSampleValidWithoutRecommendation()
    {
        m_Controller.BeginPreCheck(new MoodCheckAvailability(false, false), false);
        m_Controller.QuickSelect(MoodKind.Happy);
        m_Controller.Confirm();
        Assert.That(m_Controller.PendingSample, Is.Not.Null);
        Assert.That(m_Controller.PendingSample.recommendedWorldId, Is.Null.Or.Empty);
    }

    [Test]
    public void PanelHasANativeSortingGroupWithTextAboveGlass()
    {
        // Without a VisionOSSortingGroup, visionOS sorts each layer by distance and text next to the opening
        // video or a world model shows dark blocks behind the letters.
        m_Controller.BeginPreCheck(new MoodCheckAvailability(true, true), false);
        var panel = m_Controller.PanelTransform;
        var group = panel.GetComponent<VisionOSSortingGroup>();
        Assert.That(group, Is.Not.Null);
        Assert.That(group.DepthPass, Is.EqualTo(VisionOSSortingGroup.DepthPassType.PostPass));
        int OrderOf(string path)
        {
            var go = panel.Find(path).gameObject;
            var index = group.Renderers.ToList().FindIndex(r => r.Renderer == go);
            Assert.That(index, Is.GreaterThanOrEqualTo(0), path + " is not in the sorting group");
            return group.Renderers[index].Order;
        }
        Assert.That(OrderOf("Header/Title"), Is.GreaterThan(OrderOf("PanelGlass/Glass Surface")));
        Assert.That(OrderOf("Header/Title"), Is.GreaterThan(OrderOf("Plate/PlateWell")));
        foreach (var label in panel.GetComponentsInChildren<TMP_Text>(true))
        {
            if (!label.GetComponent<Renderer>().enabled) continue;
            Assert.That(group.Renderers.Any(r => r.Renderer == label.gameObject), Is.True, label.name);
        }
        foreach (var sub in panel.GetComponentsInChildren<TMP_SubMesh>(true))
            Assert.That(MoodCheckController.NativeSortOrder(sub.GetComponent<Renderer>()),
                Is.EqualTo(sub.textComponent.GetComponent<Renderer>().sortingOrder), "fallback glyphs draw with their label");
    }
}
