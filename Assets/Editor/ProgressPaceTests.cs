using System.IO;
using System.Linq;
using Moodium.CandyWorld;
using Moodium.UI;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Settings ▸ 进度快慢 / Pace (2026-11-04): 标准 ×1, 轻快 ×2, 速览 ×4 on every energy gain, so a demo reaches 100 %
/// quickly in all three squeeze modes. Nothing is saved to PlayerPrefs here (save: false) and the level is restored.
/// </summary>
public sealed class ProgressPaceTests
{
    int m_Before;
    GameObject m_Root;

    [SetUp]
    public void SetUp()
    {
        m_Before = ProgressPace.Level;
        ProgressPace.Set(ProgressPace.Normal, save: false);
    }

    [TearDown]
    public void TearDown()
    {
        if (m_Root != null) Object.DestroyImmediate(m_Root);
        ProgressPace.Set(m_Before, save: false);
    }

    [Test]
    public void ThreePacesDefaultToTheCurrentTuning()
    {
        Assert.That(ProgressPace.DefaultLevel, Is.EqualTo(ProgressPace.Normal));
        Assert.That(ProgressPace.Count, Is.EqualTo(3));
        Assert.That(ProgressPace.MultiplierFor(ProgressPace.Normal), Is.EqualTo(1f));
        Assert.That(ProgressPace.MultiplierFor(ProgressPace.Brisk), Is.EqualTo(2f));
        Assert.That(ProgressPace.MultiplierFor(ProgressPace.Quick), Is.EqualTo(4f));
        Assert.That(ProgressPace.MultiplierFor(-3), Is.EqualTo(1f), "clamped");
        Assert.That(ProgressPace.MultiplierFor(9), Is.EqualTo(4f), "clamped");
    }

    [Test]
    public void SetChangesTheLevelAndTellsListeners()
    {
        var heard = -1;
        void Heard(int level) => heard = level;
        ProgressPace.Changed += Heard;
        try
        {
            ProgressPace.Set(ProgressPace.Quick, save: false);
            Assert.That((ProgressPace.Level, heard, ProgressPace.Multiplier), Is.EqualTo((2, 2, 4f)));
            heard = -1;
            ProgressPace.Set(ProgressPace.Quick, save: false);
            Assert.That(heard, Is.EqualTo(-1), "no event when nothing changes");
        }
        finally { ProgressPace.Changed -= Heard; }
    }

    static int PinchesToFull()
    {
        var root = new GameObject("Energy");
        try
        {
            var energy = root.AddComponent<CandyEnergyController>();
            var pinches = 0;
            while (energy.NormalizedEnergy < 1f && pinches < 100) { energy.RegisterPinch(); pinches++; }
            return pinches;
        }
        finally { Object.DestroyImmediate(root); }
    }

    [Test]
    public void FreeModeNeedsTwentyTenOrFiveSqueezes()
    {
        Assert.That(PinchesToFull(), Is.EqualTo(20));
        ProgressPace.Set(ProgressPace.Brisk, save: false);
        Assert.That(PinchesToFull(), Is.EqualTo(10));
        ProgressPace.Set(ProgressPace.Quick, save: false);
        Assert.That(PinchesToFull(), Is.EqualTo(5));
    }

    [Test]
    public void EveryEnergyGainIsScaledAndStagesStillFire()
    {
        ProgressPace.Set(ProgressPace.Quick, save: false);
        m_Root = new GameObject("Energy");
        var energy = m_Root.AddComponent<CandyEnergyController>();
        var stages = new System.Collections.Generic.List<int>();
        energy.StageReached += stages.Add;
        energy.AddEnergy(2.5f, "squeeze-hold");   // breath / rhythm gains go through the same call
        Assert.That(energy.Energy, Is.EqualTo(10f).Within(1e-4));
        energy.AddEnergy(25f, "squeeze-release");
        Assert.That(energy.Energy, Is.EqualTo(100f), "capped at the maximum");
        Assert.That(stages, Is.EqualTo(new[] { 20, 50, 80, 100 }), "a big step still reaches every stage in order");
    }

    static SpriteRenderer Named(GameObject root, string name) =>
        root.GetComponentsInChildren<SpriteRenderer>(true).Single(r => r.name == name);

    [Test]
    public void SettingsPaceRowChoosesAndShowsTheOption()
    {
        m_Root = new GameObject("Settings");
        var applied = -1;
        var panel = m_Root.AddComponent<SelectionSettingsVisual>();
        panel.Initialize(false, true, true, true, (_, __) => { }, (_, __) => { }, () => { }, true, 3, _ => { },
            pace: 0, applyPace: p => applied = p);
        Assert.That(panel.Pace, Is.EqualTo(0));
        Assert.That((Vector2)Named(m_Root, "Settings Pace Thumb").transform.localPosition,
            Is.EqualTo(MoodiumUIRefreshLayout.SettingsPaceOption(0)));
        Assert.That(Named(m_Root, "Settings Pace Option 0").color, Is.EqualTo(MoodiumUIRefreshLayout.Ink));
        Assert.That(Named(m_Root, "Settings Pace Option 2").sprite.name, Is.EqualTo("settings_label_pace_2_zh"));

        panel.ChoosePace(2);
        Assert.That((panel.Pace, applied), Is.EqualTo((2, 2)));
        Assert.That((Vector2)Named(m_Root, "Settings Pace Thumb").transform.localPosition,
            Is.EqualTo(MoodiumUIRefreshLayout.SettingsPaceOption(2)));
        Assert.That(Named(m_Root, "Settings Pace Option 2").color, Is.EqualTo(MoodiumUIRefreshLayout.Ink));
        Assert.That(Named(m_Root, "Settings Pace Option 0").color.a,
            Is.EqualTo(MoodiumUIRefreshLayout.SettingsLabelColor(false).a).Within(.001));
        applied = -1;
        panel.ChoosePace(2);
        Assert.That(applied, Is.EqualTo(-1), "choosing the shown option does nothing");

        panel.Choose(SelectionSettingsVisual.LanguageRow, 1);
        Assert.That(Named(m_Root, "Settings Pace Option 1").sprite.name, Is.EqualTo("settings_label_pace_1_en"));

        Assert.That(Named(m_Root, "Settings Pace Option 1").GetComponent<Collider>().enabled, Is.True);
        panel.ShowIntroPage(true);
        Assert.That(Named(m_Root, "Settings Pace Option 1").GetComponent<Collider>().enabled, Is.False, "main page only");
        Assert.That(Named(m_Root, "Settings Pace Thumb").enabled, Is.False);
    }

    [Test]
    public void PaceRowIsTheFifthRowAndLinesUpWithTheOthers()
    {
        var px = 1f / MoodiumUIRefreshLayout.DesignPxToMeters;
        // Two-option tracks are 200 px wide; the pace track is three 78 px segments and ends where they end.
        var rightEdge = (MoodiumUIRefreshLayout.SettingsBagTrack.x * px) + 100f;
        Assert.That(MoodiumUIRefreshLayout.SettingsPaceOption(2).x * px + 39f, Is.EqualTo(rightEdge).Within(.01));
        Assert.That(MoodiumUIRefreshLayout.SettingsPaceTrack.y * px, Is.EqualTo(-164f).Within(.01));
        foreach (var id in new[] { "settings_seg3_track", "settings_seg3_thumb" }
                     .Concat(Enumerable.Range(0, 3).SelectMany(i => new[] { $"settings_label_pace_{i}_zh", $"settings_label_pace_{i}_en" })))
            Assert.That(Resources.Load<Sprite>("MoodiumUIRefresh/" + id), Is.Not.Null, id);
        var thumb = Resources.Load<Sprite>("MoodiumUIRefresh/settings_seg3_thumb");
        // Visible thumb 78 px wide (one segment) + the same 12 px shadow margin as the two-option thumb.
        Assert.That(thumb.bounds.size.x * px, Is.EqualTo(78f + 24f).Within(.5));
    }

    [Test]
    public void SettingsOpenerPassesAndSavesThePace()
    {
        var flow = File.ReadAllText("Assets/Scripts/Moodium/MoodiumAppFlowController.cs");
        Assert.That(flow, Does.Contain("pace: Moodium.CandyWorld.ProgressPace.Level"));
        Assert.That(flow, Does.Contain("applyPace: pace => Moodium.CandyWorld.ProgressPace.Set(pace)"));
        var energy = File.ReadAllText("Assets/Scripts/Moodium/CandyEnergyController.cs");
        Assert.That(energy, Does.Contain("amount *= ProgressPace.Multiplier;"));
    }
}
