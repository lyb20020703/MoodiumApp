using System.IO;
using System.Linq;
using Moodium.CandyWorld;
using Moodium.Interaction;
using Moodium.UI;
using NUnit.Framework;
using UnityEngine;

/// <summary>Settings ▸ squeeze sensitivity, and the reward rain on the table beside the bag (2026-11-03).</summary>
public sealed class SqueezeSensitivityAndTableRainTests
{
    const float Frame = 1f / 90f;

    [Test]
    public void LevelThreeIsTheShippedTuningAndLevelsGetMoreSensitive()
    {
        var three = SqueezeSensitivity.For(SqueezeSensitivity.DefaultLevel);
        Assert.That(SqueezeSensitivity.DefaultLevel, Is.EqualTo(3));
        Assert.That(three.PressDepth, Is.EqualTo(HandSqueezePulse.PressDepth));
        Assert.That(three.CloseAmount, Is.EqualTo(HandSqueezePulse.CloseAmount));
        Assert.That(three.ReleaseRadius, Is.EqualTo(TrackedCapsuleHandCollision.DefaultReleaseRadius));
        for (var level = SqueezeSensitivity.MinLevel; level < SqueezeSensitivity.MaxLevel; level++)
        {
            SqueezeSensitivity.Profile a = SqueezeSensitivity.For(level), b = SqueezeSensitivity.For(level + 1);
            Assert.That(b.PressDepth, Is.LessThan(a.PressDepth), $"level {level + 1} press");
            Assert.That(b.CloseAmount, Is.LessThan(a.CloseAmount), $"level {level + 1} close");
            Assert.That(b.ReleaseRadius, Is.LessThan(a.ReleaseRadius), $"level {level + 1} lift");
            Assert.That(b.PressRebound, Is.LessThan(b.PressDepth));
            Assert.That(b.CloseRebound, Is.LessThan(b.CloseAmount));
        }
        Assert.That(SqueezeSensitivity.For(0).Level, Is.EqualTo(1));
        Assert.That(SqueezeSensitivity.For(9).Level, Is.EqualTo(5));
    }

    [Test]
    public void ReleaseAlwaysNeedsMoreDistanceThanTouching()
    {
        foreach (var level in Enumerable.Range(1, 5))
            Assert.That(TrackedCapsuleHandCollision.ReleaseRadiusFor(SqueezeSensitivity.For(level),
                TrackedCapsuleHandCollision.DefaultContactRadius),
                Is.GreaterThanOrEqualTo(TrackedCapsuleHandCollision.DefaultContactRadius + TrackedCapsuleHandCollision.MinReleaseMargin));
        Assert.That(TrackedCapsuleHandCollision.ReleaseRadiusFor(SqueezeSensitivity.For(5), .02f), Is.EqualTo(.023f).Within(1e-6));
    }

    [Test]
    public void SettingTheLevelAppliesAtOnceAndNotifies()
    {
        var before = SqueezeSensitivity.Level;
        var heard = 0;
        void Heard(int level) => heard = level;
        SqueezeSensitivity.Changed += Heard;
        try
        {
            var target = before == 5 ? 4 : 5;
            SqueezeSensitivity.Set(target, save: false);
            Assert.That(SqueezeSensitivity.Level, Is.EqualTo(target));
            Assert.That(SqueezeSensitivity.Current.Level, Is.EqualTo(target));
            Assert.That(heard, Is.EqualTo(target));
            SqueezeSensitivity.Set(12, save: false);
            Assert.That(SqueezeSensitivity.Level, Is.EqualTo(5), "clamped");
        }
        finally
        {
            SqueezeSensitivity.Changed -= Heard;
            SqueezeSensitivity.Set(before, save: false);
        }
    }

    static int PressOf(HandSqueezePulse pulse, float depth)
    {
        float h = .10f, s = .09f;
        var fired = 0;
        void Ramp(float to, float seconds)
        {
            var frames = Mathf.RoundToInt(seconds / Frame);
            var from = h;
            for (var i = 1; i <= frames; i++)
            {
                h = Mathf.Lerp(from, to, i / (float)frames);
                if (pulse.Update(new HandSqueezeSample(h, s), Frame, out _, out _)) fired++;
            }
        }
        pulse.Update(new HandSqueezeSample(h, s), Frame, out _, out _);
        Ramp(.112f, .25f);
        Ramp(.112f - depth, .15f);
        Ramp(.112f - depth, .3f);
        return fired;
    }

    [Test]
    public void AHigherLevelCountsALighterPress()
    {
        Assert.That(PressOf(new HandSqueezePulse(), .006f), Is.EqualTo(0), "6 mm is too light for level 3");
        var sensitive = new HandSqueezePulse();
        sensitive.Configure(SqueezeSensitivity.For(5));
        Assert.That(sensitive.Level, Is.EqualTo(5));
        Assert.That(PressOf(sensitive, .006f), Is.EqualTo(1), "… but counts at level 5");
        var firm = new HandSqueezePulse();
        firm.Configure(SqueezeSensitivity.For(1));
        Assert.That(PressOf(firm, .010f), Is.EqualTo(0), "level 1 needs a firmer press");
        Assert.That(PressOf(new HandSqueezePulse(), .010f), Is.EqualTo(1));
    }

    static SpriteRenderer Named(GameObject root, string name) =>
        root.GetComponentsInChildren<SpriteRenderer>(true).Single(r => r.name == name);

    [Test]
    public void SettingsStepperShowsTheLevelAndStopsAtTheEnds()
    {
        var root = new GameObject("Settings");
        try
        {
            var applied = 0;
            var panel = root.AddComponent<SelectionSettingsVisual>();
            panel.Initialize(false, true, true, true, (_, __) => { }, (_, __) => { }, () => { }, true, 3, l => applied = l);
            Assert.That(Named(root, "Settings Sensitivity Dot 3").color.a, Is.EqualTo(MoodiumUIRefreshLayout.SettingsDotOn.a).Within(.001));
            Assert.That(Named(root, "Settings Sensitivity Dot 4").color.a, Is.EqualTo(MoodiumUIRefreshLayout.SettingsDotOff.a).Within(.001));
            Assert.That((Vector2)Named(root, "Settings Sensitivity Track").transform.localPosition,
                Is.EqualTo(MoodiumUIRefreshLayout.SettingsSensitivityTrack));

            panel.StepSensitivity(1);
            Assert.That((panel.Sensitivity, applied), Is.EqualTo((4, 4)));
            Assert.That(Named(root, "Settings Sensitivity Dot 4").color.a, Is.EqualTo(MoodiumUIRefreshLayout.SettingsDotOn.a).Within(.001));
            panel.StepSensitivity(1);
            panel.StepSensitivity(1);
            Assert.That(panel.Sensitivity, Is.EqualTo(5));
            Assert.That(Named(root, "Settings Sensitivity Plus").color.a,
                Is.EqualTo(MoodiumUIRefreshLayout.SettingsStepperDisabledAlpha).Within(.001), "+ dims at 5");
            Assert.That(Named(root, "Settings Sensitivity Minus").color.a, Is.EqualTo(1f).Within(.001));
            for (var i = 0; i < 6; i++) panel.StepSensitivity(-1);
            Assert.That((panel.Sensitivity, applied), Is.EqualTo((1, 1)));
            Assert.That(Named(root, "Settings Sensitivity Minus").color.a,
                Is.EqualTo(MoodiumUIRefreshLayout.SettingsStepperDisabledAlpha).Within(.001), "− dims at 1");

            Assert.That(Named(root, "Settings Sensitivity Minus").GetComponent<Collider>().enabled, Is.True);
            panel.ShowIntroPage(true);
            Assert.That(Named(root, "Settings Sensitivity Minus").GetComponent<Collider>().enabled, Is.False, "main page only");
            Assert.That(Named(root, "Settings Sensitivity Minus").enabled, Is.False);
        }
        finally { Object.DestroyImmediate(root); }
    }

    [Test]
    public void SettingsOpenerPassesAndSavesTheLevel()
    {
        var flow = File.ReadAllText("Assets/Scripts/Moodium/MoodiumAppFlowController.cs");
        Assert.That(flow, Does.Contain("sensitivity: Moodium.Interaction.SqueezeSensitivity.Level"));
        Assert.That(flow, Does.Contain("applySensitivity: level => Moodium.Interaction.SqueezeSensitivity.Set(level)"));
        var collision = File.ReadAllText("Assets/Scripts/Interaction/TrackedCapsuleHandCollision.cs");
        Assert.That(collision, Does.Contain("ApplySensitivity(SqueezeSensitivity.Current)"));
    }

    [Test]
    public void RewardsRestOnAnyStaticSurfaceButNotOnEachOther()
    {
        var table = new GameObject("Scanned table");
        var reward = new GameObject("Falling reward");
        try
        {
            var tableCollider = table.AddComponent<BoxCollider>();
            var rewardCollider = reward.AddComponent<SphereCollider>();
            reward.AddComponent<Rigidbody>();
            Assert.That(ReleaseRewardLifecycle.IsRestingSurface(tableCollider), Is.True);
            Assert.That(ReleaseRewardLifecycle.IsRestingSurface(rewardCollider), Is.False);
            Assert.That(ReleaseRewardLifecycle.IsRestingSurface(null), Is.False);
        }
        finally
        {
            Object.DestroyImmediate(table);
            Object.DestroyImmediate(reward);
        }
    }
}
