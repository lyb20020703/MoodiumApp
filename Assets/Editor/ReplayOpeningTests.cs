using System.IO;
using System.Text.RegularExpressions;
using Moodium.Flow;
using Moodium.UI;
using Moodium.WishJournal;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// World selection ▸ ↺ replays the opening (2026-11-04): between visitors the next person sees the opening again
/// without the app being force-quit. Under the journal on the video (sticker pick), right of the gear (manual pick).
/// </summary>
public sealed class ReplayOpeningTests
{
    static readonly Vector3 VideoScale = new(1.28f, .72f, 1f);   // OpeningVideoDisplayPlacement
    const float Press = .06f;                                     // press square of every corner button
    const string FlowPath = "Assets/Scripts/Moodium/MoodiumAppFlowController.cs";
    const string OpeningPath = "Assets/Scripts/Moodium/Opening/OpeningManager.cs";

    [Test]
    public void ReplayButtonIsAGlassSpriteButtonThatCallsBack()
    {
        var parent = new GameObject("Parent");
        try
        {
            var pressed = 0;
            var root = UIRefreshVisuals.ReplayButton(parent.transform, new Vector3(.1f, .2f, -.04f), () => pressed++);
            Assert.That(root.name, Is.EqualTo("Replay Opening"));
            Assert.That(root.transform.localPosition, Is.EqualTo(new Vector3(.1f, .2f, -.04f)));
            var glass = root.transform.Find("Replay Glass");
            Assert.That(glass, Is.Not.Null);
            Assert.That(glass.GetComponent<SpriteRenderer>().sprite.name, Is.EqualTo("replay_btn_idle"));
            var size = glass.GetComponent<BoxCollider>().size;
            Assert.That((size.x, size.y), Is.EqualTo((Press, Press)), "same press square as the gear and the journal");
            glass.GetComponent<JournalSpriteButton>().Press();
            Assert.That(pressed, Is.EqualTo(1));
        }
        finally { Object.DestroyImmediate(parent); }
    }

    [Test]
    public void ReplayArtMatchesTheGearButton()
    {
        var gear = Resources.Load<Sprite>("MoodiumUIRefresh/settings_btn_idle");
        foreach (var id in new[] { "replay_btn_idle", "replay_btn_pressed" })
        {
            var sprite = Resources.Load<Sprite>("MoodiumUIRefresh/" + id);
            Assert.That(sprite, Is.Not.Null, id);
            Assert.That(sprite.bounds.size.x, Is.EqualTo(gear.bounds.size.x).Within(1e-5f), id + ": same size as the gear");
            Assert.That(sprite.pixelsPerUnit, Is.EqualTo(gear.pixelsPerUnit).Within(.01f), id);
        }
    }

    [Test]
    public void OnTheVideoReplaySitsUnderTheJournalInsideTheCorner()
    {
        var display = new GameObject("Video");
        try
        {
            display.transform.localScale = VideoScale;
            var journal = display.transform.TransformPoint(MoodiumAppFlowController.SelectionColumnLocal(VideoScale, 1));
            var replay = display.transform.TransformPoint(
                MoodiumAppFlowController.SelectionColumnLocal(VideoScale, MoodiumAppFlowController.ReplayColumnSlot));
            Assert.That(MoodiumAppFlowController.ReplayColumnSlot, Is.EqualTo(2), "gear, journal, then replay");
            Assert.That(replay.x, Is.EqualTo(journal.x).Within(1e-5f), "same column");
            Assert.That(journal.y - replay.y, Is.EqualTo(MoodiumAppFlowController.SelectionColumnPitch).Within(1e-5f));
            Assert.That(replay.x, Is.LessThanOrEqualTo(.64f - Press / 2f), "inside the right edge of the 1.28 m video");
            Assert.That(replay.y - Press / 2f, Is.GreaterThan(-.35f * VideoScale.y + .2f), "well above the instruction and mood chip");
            Assert.That(replay.z, Is.EqualTo(-.04f).Within(1e-5f));
        }
        finally { Object.DestroyImmediate(display); }
    }

    [Test]
    public void OnTheCarouselReplaySitsRightOfTheGear()
    {
        var gear = new Vector3(.215f, -.27f, -.08f);   // WorldCarouselSelectionController: right of the mood chip
        var replay = MoodiumAppFlowController.ReplayBesideGear(gear);
        Assert.That((replay.y, replay.z), Is.EqualTo((gear.y, gear.z)), "same row, same depth");
        Assert.That(replay.x - gear.x, Is.GreaterThan(Press), "press squares do not touch");
        Assert.That(replay.x + Press / 2f, Is.LessThan(.70f - .07f / 2f), "clear of the › arrow at x 0.70");
        var source = File.ReadAllText("Assets/Scripts/Moodium/Flow/WorldCarouselSelectionController.cs");
        Assert.That(source, Does.Contain("Gear(root, new Vector3(.215f, -.27f, -.08f)"), "the gear this test assumes");
    }

    [Test]
    public void FlowBuildsTheButtonOnBothSelectionsAndDropsItWithTheGear()
    {
        var flow = File.ReadAllText(FlowPath);
        Assert.That(flow, Does.Contain("ReplayButton(display, SelectionColumnLocal(display.localScale, ReplayColumnSlot), ReplayOpening)"));
        Assert.That(flow, Does.Contain("ReplayBesideGear(anchor.localPosition)"));
        Assert.That(flow, Does.Contain("m_BranchCarousel.CanInteract) ReplayOpening();"), "manual pick: only while the carousel takes input");
        // Rebuilding the selection, entering a world and replaying all drop the old button.
        Assert.That(Regex.Matches(flow, @"DisposeReplayButton\(\);").Count, Is.GreaterThanOrEqualTo(3));
        Assert.That(flow, Does.Contain("if (m_ReplayButton != null && m_SelectionHasBag) m_ReplayButton.SetActive(visible);"),
            "hidden with the corner column during a mood check");
    }

    [Test]
    public void ReplayStartsAFreshVisitAndHandsBackToTheOpening()
    {
        var flow = File.ReadAllText(FlowPath);
        var start = flow.IndexOf("public void ReplayOpening()", System.StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThan(0));
        var body = flow.Substring(start, flow.IndexOf("// ---------- Wish Journal", start, System.StringComparison.Ordinal) - start);
        Assert.That(body, Does.Contain("if (!CanReplayOpening) return;"));
        Assert.That(body, Does.Contain("m_MoodCheckContext = new MoodCheckContext();"), "the next person starts a new mood session");
        Assert.That(body, Does.Contain("session?.DisposeOnce();"), "the old video window goes");
        Assert.That(body.IndexOf("PrepareForOpening();", System.StringComparison.Ordinal),
            Is.LessThan(body.IndexOf("m_OpeningManager.ReplayOpening();", System.StringComparison.Ordinal)));
        var guard = flow.Substring(flow.IndexOf("public bool CanReplayOpening", System.StringComparison.Ordinal), 500);
        foreach (var blocked in new[] { "m_SelectedStickerEntry != null", "MoodCheckInProgress", "m_SelectionSettingsPanel != null", "m_JournalPanel != null", "!opening.IsReplaying" })
            Assert.That(guard, Does.Contain(blocked));
    }

    [Test]
    public void OpeningReplaysThroughTheSameVideoPath()
    {
        var opening = File.ReadAllText(OpeningPath);
        Assert.That(Regex.Matches(opening, @"yield return PlayPreOpeningVideo\(\);").Count, Is.EqualTo(2), "launch and replay");
        var start = opening.IndexOf("public bool ReplayOpening()", System.StringComparison.Ordinal);
        var body = opening.Substring(start, opening.IndexOf("GameObject CreateSkipButton", start, System.StringComparison.Ordinal) - start);
        Assert.That(body, Does.Contain("m_OpeningVideoHandedToStickerSelection = false;"));
        Assert.That(body, Does.Contain("m_TutorialFlowGate = new OpeningTutorialFlowGate();"), "fail-closed again for the new run");
        Assert.That(body, Does.Contain("m_Replaying = true;\n            StartCoroutine(ReplaySequence());"),
            "flag set before the coroutine, which can finish synchronously");
        Assert.That(body, Does.Contain("m_AppFlow?.BeginStickerWorldSelection(null);"), "no video → back to the selection");
    }
}
