using Moodium.MoodCheck;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class WorldMoodOverlayControllerTests
{
    GameObject m_Root;
    GameObject m_CameraRoot;
    WorldMoodOverlayController m_Overlay;

    [SetUp]
    public void SetUp()
    {
        m_Root = new GameObject("Mood Overlay");
        m_CameraRoot = new GameObject("Camera Root");
        m_Overlay = m_Root.AddComponent<WorldMoodOverlayController>();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(m_Root);
        Object.DestroyImmediate(m_CameraRoot);
    }

    [TestCase(MoodKind.Happy)]
    [TestCase(MoodKind.Calm)]
    [TestCase(MoodKind.Neutral)]
    public void NonNegativeMoods_CreateNoOverlay(MoodKind mood)
    {
        m_Overlay.Configure(true);
        m_Overlay.Begin(Sample(mood, .8f), m_CameraRoot.transform);
        Assert.That(m_Overlay.IsActive, Is.False);
    }

    [Test]
    public void DisabledOrNullSample_CreateNoOverlay()
    {
        m_Overlay.Configure(false);
        m_Overlay.Begin(Sample(MoodKind.Low, .8f), m_CameraRoot.transform);
        Assert.That(m_Overlay.IsActive, Is.False);
        m_Overlay.Configure(true);
        m_Overlay.Begin(null, m_CameraRoot.transform);
        Assert.That(m_Overlay.IsActive, Is.False);
    }

    [TestCase(MoodKind.Low)]
    [TestCase(MoodKind.Tense)]
    public void NegativeHighOrLowArousal_StartAtSampleIntensity(MoodKind mood)
    {
        m_Overlay.Configure(true);
        m_Overlay.Begin(Sample(mood, .8f), m_CameraRoot.transform);
        Assert.That(m_Overlay.IsActive, Is.True);
        Assert.That(m_Overlay.CurrentIntensity, Is.EqualTo(.8f).Within(.001f));
        Assert.That(m_Overlay.ParticleSystem, Is.Not.Null);
    }

    [Test]
    public void ProgressFadesOverlayAndCompletionClearsOnce()
    {
        m_Overlay.Configure(true);
        m_Overlay.Begin(Sample(MoodKind.Tense, .8f), m_CameraRoot.transform);
        m_Overlay.SetNormalizedProgress(.4f);
        Assert.That(m_Overlay.CurrentIntensity, Is.EqualTo(.48f).Within(.001f));
        m_Overlay.SetNormalizedProgress(1f);
        m_Overlay.SetNormalizedProgress(1f);
        Assert.That(m_Overlay.IsActive, Is.False);
        Assert.That(m_Overlay.ClearingPlayCount, Is.EqualTo(1));
    }

    [Test]
    public void MissingCameraRoot_WarnsAndDoesNotBlock()
    {
        m_Overlay.Configure(true);
        LogAssert.Expect(LogType.Warning, "[Mood Check] Mood overlay skipped because the camera root is missing.");
        m_Overlay.Begin(Sample(MoodKind.Low, .7f), null);
        Assert.That(m_Overlay.IsActive, Is.False);
    }

    static MoodCheckSample Sample(MoodKind mood, float intensity) => new()
    {
        mood = mood,
        intensity = intensity,
        phase = MoodCheckPhase.Pre
    };
}
