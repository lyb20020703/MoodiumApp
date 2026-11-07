using System.IO;
using Moodium.Flow;
using Moodium.UI;
using NUnit.Framework;
using UnityEngine;

/// <summary>Settings + Wish Journal in a column at the video's top-right corner (2026-11-01).</summary>
public sealed class SelectionCornerColumnTests
{
    static readonly Vector3 VideoScale = new(1.28f, .72f, 1f);   // OpeningVideoDisplayPlacement

    [Test]
    public void ColumnSitsInsideTheTopRightCornerOfTheVideo()
    {
        var display = new GameObject("Video");
        try
        {
            display.transform.localScale = VideoScale;
            var gear = display.transform.TransformPoint(MoodiumAppFlowController.SelectionColumnLocal(VideoScale, 0));
            var journal = display.transform.TransformPoint(MoodiumAppFlowController.SelectionColumnLocal(VideoScale, 1));
            const float press = .06f;   // both buttons' press squares
            foreach (var p in new[] { gear, journal })
            {
                Assert.That(p.x, Is.GreaterThan(.4f).And.LessThanOrEqualTo(.64f - press / 2f), "right edge of the 1.28 m video");
                Assert.That(p.y, Is.GreaterThan(0f).And.LessThanOrEqualTo(.36f - press / 2f), "upper half, inside the top edge");
                Assert.That(p.z, Is.EqualTo(-.04f).Within(1e-5f), "4 cm in front of the video, as the gear was");
            }
            Assert.That(gear.x, Is.EqualTo(journal.x).Within(1e-5f), "a vertical column");
            Assert.That(gear.y - journal.y, Is.EqualTo(MoodiumAppFlowController.SelectionColumnPitch).Within(1e-5f), "settings on top, journal below");
            Assert.That(MoodiumAppFlowController.SelectionColumnPitch, Is.GreaterThan(press), "press squares do not touch");
            // The instruction sits at local y -0.35 (≈ -0.25 m) with the mood chip under it: the column is far above.
            Assert.That(journal.y - press / 2f, Is.GreaterThan(-.35f * VideoScale.y + .2f));
        }
        finally { Object.DestroyImmediate(display); }
    }

    [Test]
    public void SettingsPanelCanLeaveOutItsOwnGear()
    {
        var a = new GameObject("With gear"); var b = new GameObject("Without gear");
        try
        {
            a.AddComponent<SelectionSettingsVisual>().Initialize(false, true, true, true, (_, __) => { }, (_, __) => { }, () => { });
            b.AddComponent<SelectionSettingsVisual>().Initialize(false, true, true, true, (_, __) => { }, (_, __) => { }, () => { }, embeddedGear: false);
            Assert.That(a.transform.Find("Selection Settings"), Is.Not.Null, "carousel: open gear under the panel as before");
            Assert.That(b.transform.Find("Selection Settings"), Is.Null, "video: the corner gear stays and shows 'open' instead");
            Assert.That(b.transform.Find("Settings Panel Content/Settings Close"), Is.Not.Null, "× still closes it");
        }
        finally { Object.DestroyImmediate(a); Object.DestroyImmediate(b); }
    }

    [Test]
    public void FlowUsesTheColumnAndOpensSettingsInFrontOfTheVideo()
    {
        var flow = File.ReadAllText("Assets/Scripts/Moodium/MoodiumAppFlowController.cs");
        Assert.That(flow, Does.Contain("SelectionColumnLocal(display.localScale, 0), OpenSelectionSettings"));
        Assert.That(flow, Does.Contain("display.TransformPoint(SelectionColumnLocal(display.localScale, 1)), ToggleJournal"));
        Assert.That(flow, Does.Not.Contain("new Vector3(0, -.23f, -.04f)"), "no gear under the video any more");
        Assert.That(flow, Does.Contain("embeddedGear: !m_SelectionHasBag"));
        Assert.That(flow, Does.Contain("if (m_SelectionHasBag)\n                AttachToOpeningWindow(panel.transform, true);"));
        Assert.That(flow, Does.Contain("m_BagSwitch = null; m_SettingsGear = null;"), "a destroyed gear is never touched again");
        var opening = File.ReadAllText("Assets/Scripts/Moodium/Opening/OpeningManager.cs");
        Assert.That(opening, Does.Contain("if (skipButton != null) Destroy(skipButton);"), "no stale Skip in the corner after a full opening");
    }
}
