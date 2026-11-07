using System.IO;
using Moodium.CandyWorld;
using Moodium.Characters;
using Moodium.Flow;
using Moodium.NatureWorld;
using Moodium.Opening;
using NUnit.Framework;
using UnityEngine;

/// <summary>2026-10-30 device fixes: wrist band in bubbles, bubble timing, video updater, hand wake fallbacks.</summary>
public sealed class RealDevice1030bTests
{
    static string Source(string path) => File.ReadAllText(Path.Combine(Application.dataPath, path));

    [Test]
    public void WristBandIsHiddenOnlyInsideBubbleWorlds()
    {
        Assert.That(CandyWorldManager.WristHiddenFor(true, WorldJourneyStage.Bubble), Is.True);
        Assert.That(CandyWorldManager.WristHiddenFor(true, WorldJourneyStage.Explore), Is.False);
        Assert.That(CandyWorldManager.WristHiddenFor(true, WorldJourneyStage.Rescue), Is.False);
        Assert.That(CandyWorldManager.WristHiddenFor(false, WorldJourneyStage.Bubble), Is.False);
        Assert.That(CandyWorldManager.WristHiddenFor(true, null), Is.False);
    }

    [Test]
    public void SuppressedWristBandStopsInput()
    {
        var source = Source("Scripts/Moodium/CandyEnergyWristUI.cs");
        StringAssert.Contains("tracked && !Suppressed ? 1f : 0f", source);
        StringAssert.Contains("if (HandTracked && !Suppressed && m_Camera != null)", source);
        StringAssert.Contains("ShowVisuals(!(Suppressed && m_VisualAlpha <= 0f))", source);
    }

    [Test]
    public void NextBubbleAndExitDoNotWaitTenSecondsButNeedTheFingerToLeaveFirst()
    {
        var source = Source("Scripts/Moodium/BubbleWorld/BubbleWorldController.cs");
        StringAssert.DoesNotContain("Time.time - m_InsideAt < 10f", source);
        StringAssert.Contains("float m_NextBubbleDelaySeconds = 0f", source);
        // The shared gate now also handles the chime-focus unlock grace period.
        bool armed = false;
        Assert.That(Moodium.NatureWorld.BubbleWorldController.TouchAfterGrace(true, 100f, -10f, ref armed), Is.False);
        Assert.That(Moodium.NatureWorld.BubbleWorldController.TouchAfterGrace(false, 100f, -10f, ref armed), Is.False);
        Assert.That(Moodium.NatureWorld.BubbleWorldController.TouchAfterGrace(true, 100f, -10f, ref armed), Is.True);
        StringAssert.Contains("TouchAfterGrace(touchingPortal,Time.time,m_PortalUnlockAt,ref m_PortalArmed)", source);
        StringAssert.DoesNotContain("m_Home.name = \"Home Bubble\"", source); // Home bubble was removed by user request.
        StringAssert.Contains("if (m_PrepareNestedEarly) PrepareNested();", source);
    }

    [Test]
    public void VideoUpdaterKeepsItsTextureWhenHiddenAndShownAgain()
    {
        var go = new GameObject("Video");
        var texture = new RenderTexture(16, 16, 0);
        try
        {
            var updater = go.AddComponent<PolySpatialVideoRenderTextureUpdater>();
            updater.Configure(texture);
            go.SetActive(false);
            go.SetActive(true);
            Assert.That(updater.TargetTexture, Is.SameAs(texture));
        }
        finally
        {
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(texture);
        }
    }

    [Test]
    public void StickerSelectionNeverPausesBeforeItsLoopSeek()
    {
        var source = Source("Scripts/Moodium/Flow/StickerWorldSelectionController.cs");
        StringAssert.Contains("VideoSegmentLoopDriver", source);
        StringAssert.DoesNotContain("player.Pause();", source);
    }

    [Test]
    public void PickAnyFindsAPointWhenTheDirectionIsEmpty()
    {
        var lookup = new int[BubbleHandWakeMath.LonBins * BubbleHandWakeMath.LatBins * BubbleHandWakeMath.PerBin];
        for (var i = 0; i < lookup.Length; i++) lookup[i] = -1;
        Assert.That(BubbleHandWakeMath.PickAny(lookup, .5f), Is.EqualTo(-1));
        lookup[7 * BubbleHandWakeMath.PerBin] = 42;
        Assert.That(BubbleHandWakeMath.PickAny(lookup, .9f), Is.EqualTo(42));
        Assert.That(BubbleHandWakeMath.PickAny(null, .5f), Is.EqualTo(-1));
    }

    [Test]
    public void TheHandHoldingMoodiNeverBlocksOrFakesATouch()
    {
        // Right hand holds Moodi (support 1) and rests inside the touch sphere: no touch, and the left still pats.
        Assert.That(MoodiCompanionAnimator.TouchStarts(false, true, false, false, 1), Is.False);
        Assert.That(MoodiCompanionAnimator.TouchStarts(true, true, false, true, 1), Is.True);
        // Each hand has its own edge: a hand already inside does not re-trigger, a new one does.
        Assert.That(MoodiCompanionAnimator.TouchStarts(true, true, true, false, -1), Is.True);
        Assert.That(MoodiCompanionAnimator.TouchStarts(true, false, true, false, -1), Is.False);
    }

    [Test]
    public void SelectionMoodiAnswersTouchesAndStaysIdle()
    {
        var go = new GameObject("Moodi");
        try
        {
            var moodi = go.AddComponent<MoodiCompanionAnimator>();
            Assert.That(moodi.TouchFlourish, Is.False);
            moodi.ConfigureSelectionTouch();
            Assert.That(moodi.TouchFlourish, Is.True);
            moodi.SetSupportHand(0);
            Assert.That(moodi.SupportHand, Is.Zero);
        }
        finally { Object.DestroyImmediate(go); }
    }

    [Test]
    public void PalmMoodiKeepsIdleAndStaysThroughAPat()
    {
        var companion = Source("Scripts/Moodium/Characters/MoodiHandCompanion.cs");
        StringAssert.Contains("m_Animator.SetWalking(false);", companion);
        StringAssert.Contains("m_Animator.SetSupportHand(m_SummonRight ? 1 : 0);", companion);
        StringAssert.Contains("patting ? m_LostHoldWhilePatting : m_LostHold", companion);
        StringAssert.Contains("IsFlipped(hand.Facing, m_FlipDot) && !patting", companion);
        var guide = Source("Scripts/Moodium/WalkTrailGuideController.cs");
        Assert.That(guide.IndexOf("breathing.enabled = false", System.StringComparison.Ordinal),
            Is.LessThan(guide.IndexOf("PrepareGuide(visual);", System.StringComparison.Ordinal)),
            "breathing float off before the guide is scaled");
    }
}
