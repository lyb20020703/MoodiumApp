using Moodium.Flow;
using Moodium.MoodCheck;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class MoodCheckFlowIntegrationTests
{
    GameObject m_Root;
    GameObject m_CameraObject;
    MoodiumAppFlowController m_Flow;
    MoodCheckController m_Controller;
    MoodCheckContext m_Context;

    [SetUp]
    public void SetUp()
    {
        MoodCheckUISetup.BuildOrUpdate();
        m_Root = new GameObject("Flow");
        m_Flow = m_Root.AddComponent<MoodiumAppFlowController>();
        m_Controller = m_Root.AddComponent<MoodCheckController>();
        m_Context = new MoodCheckContext();
        m_CameraObject = new GameObject("Camera");
        var camera = m_CameraObject.AddComponent<Camera>();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/UI/Moodium/MoodCheck/Generated/MoodCheckPanel.prefab");
        m_Controller.Configure(prefab, camera);
        m_Flow.ConfigureMoodCheckForTests(m_Controller, m_Context);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(m_Root);
        Object.DestroyImmediate(m_CameraObject);
    }

    [Test]
    public void StickerRequest_StoresConfirmedPreSampleAndRecommendation()
    {
        m_Flow.RequestStickerMoodCheck();
        m_Controller.QuickSelect(MoodKind.Tense);
        m_Controller.Confirm();
        m_Controller.AcceptRecommendation();

        Assert.That(m_Context.PreSample, Is.Not.Null);
        Assert.That(m_Context.PreSample.mood, Is.EqualTo(MoodKind.Tense));
        Assert.That(m_Context.PreSample.recommendedWorldId, Is.EqualTo("nature"));
    }

    [Test]
    public void SelectedWorldStartsVisitRegardlessOfRecommendation()
    {
        m_Flow.RequestStickerMoodCheck();
        m_Controller.QuickSelect(MoodKind.Tense);
        m_Controller.Confirm();
        m_Controller.AcceptRecommendation();

        m_Flow.BeginMoodVisit("candy", 10f);

        Assert.That(m_Context.EnteredWorldId, Is.EqualTo("candy"));
        Assert.That(m_Context.ActiveVisitId, Is.Not.Empty);
    }

    [Test]
    public void ExitWithoutPreSampleDoesNotOpenPostCheck()
    {
        m_Flow.BeginMoodVisit("nature", 0f);
        m_Flow.RequestMoodCheckOnWorldExit(20f);

        Assert.That(m_Controller.State, Is.EqualTo(MoodCheckState.Hidden));
        Assert.That(m_Context.ActiveVisitId, Is.Null);
    }

    [Test]
    public void ExitWithPreSampleOpensPostAndSkipClosesVisit()
    {
        m_Flow.RequestStickerMoodCheck();
        m_Controller.QuickSelect(MoodKind.Low);
        m_Controller.Confirm();
        m_Controller.AcceptRecommendation();
        m_Flow.BeginMoodVisit("candy", 10f);

        m_Flow.RequestMoodCheckOnWorldExit(70f);
        Assert.That(m_Controller.State, Is.EqualTo(MoodCheckState.Dragging));
        Assert.That(m_Controller.Plate.NormalizedPosition, Is.EqualTo(new Vector2(-.6f, -.6f)));
        m_Controller.Skip();

        Assert.That(m_Context.ActiveVisitId, Is.Null);
    }
}
