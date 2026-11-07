using Moodium.Reality;
using NUnit.Framework;
using UnityEngine;

/// <summary>Squeeze rescue: jelly dome spring, voice muffling and encouragement rules.</summary>
public sealed class MoodiRescueTests
{
    static float Run(float value, ref float velocity, float target, float stiffness, float damping, float seconds,
        out float min, out float max)
    {
        min = max = value;
        for (var t = 0f; t < seconds; t += 1f / 90f)
        {
            value = SqueezeProgressVideoController.StepSpring(value, ref velocity, target, stiffness, damping, 1f / 90f);
            min = Mathf.Min(min, value);
            max = Mathf.Max(max, value);
        }
        return value;
    }

    [Test]
    public void PressingSquashesQuicklyWithoutWobble()
    {
        var velocity = 0f;
        var s = Run(0f, ref velocity, 1f, 2500f, .85f, .1f, out _, out var max);
        Assert.That(s, Is.GreaterThan(.95f), "fully pressed within 0.1 s");
        Assert.That(max, Is.LessThan(1.05f), "no visible overshoot while pressed");
    }

    [Test]
    public void ReleasingSpringsBackTallerThenSettles()
    {
        var velocity = 0f;
        var s = Run(1f, ref velocity, 0f, 220f, .32f, 1.6f, out var min, out _);
        Assert.That(min, Is.LessThan(-.25f), "overshoots past rest, so the dome briefly stands taller");
        Assert.That(min, Is.GreaterThan(-.45f), "but not wildly");
        Assert.That(Mathf.Abs(s), Is.LessThan(.03f), "and has settled after 1.6 s");
    }

    [Test]
    public void DomeIsShorterAndWiderWhenPressedAndTallerWhenSpringingBack()
    {
        var pressed = SqueezeProgressVideoController.DomeScale(1f, .30f, .14f);
        Assert.That(pressed.y, Is.EqualTo(.70f).Within(.001f));
        Assert.That(pressed.x, Is.EqualTo(1.14f).Within(.001f));
        Assert.That(pressed.z, Is.EqualTo(pressed.x));
        var rebound = SqueezeProgressVideoController.DomeScale(-.34f, .30f, .14f);
        Assert.That(rebound.y, Is.GreaterThan(1.09f));
        Assert.That(rebound.x, Is.LessThan(1f));
        Assert.That(SqueezeProgressVideoController.DomeScale(10f, .30f, .14f).y, Is.GreaterThanOrEqualTo(.2f), "never collapses");
    }

    [Test]
    public void VoiceIsMuffledInsideAndClearNearTheEnd()
    {
        Assert.That(MoodiRescueDirector.MuffleForProgress(0f), Is.EqualTo(1f));
        Assert.That(MoodiRescueDirector.MuffleForProgress(.85f), Is.EqualTo(0f).Within(.0001f));
        Assert.That(MoodiRescueDirector.MuffleForProgress(1f), Is.EqualTo(0f));
        Assert.That(MoodiRescueDirector.MuffleForProgress(.5f), Is.LessThan(MoodiRescueDirector.MuffleForProgress(.2f)));
    }

    [Test]
    public void StagesMapToTheirLines()
    {
        Assert.That(MoodiRescueDirector.StageLine(20), Is.EqualTo("K5"));
        Assert.That(MoodiRescueDirector.StageLine(50), Is.EqualTo("K6"));
        Assert.That(MoodiRescueDirector.StageLine(80), Is.EqualTo("K7"));
        Assert.That(MoodiRescueDirector.StageLine(100), Is.Null, "100% is the jump-out, not a stage line");
    }

    [Test]
    public void CheersNeedSteadyPressingAndAGap()
    {
        Assert.That(MoodiRescueDirector.ShouldCheer(5, 5, 12f, 10f, .4f), Is.True);
        Assert.That(MoodiRescueDirector.ShouldCheer(4, 5, 12f, 10f, .4f), Is.False, "not enough presses");
        Assert.That(MoodiRescueDirector.ShouldCheer(6, 5, 4f, 10f, .4f), Is.False, "too soon after the last line");
        Assert.That(MoodiRescueDirector.ShouldCheer(6, 5, 12f, 10f, .85f), Is.False, "the 80% line takes over");
    }

    [Test]
    public void NudgeOnlyWhenTheUserStopsMidway()
    {
        Assert.That(MoodiRescueDirector.ShouldNudge(true, .4f, 13f, 12f, 40f, 30f), Is.True);
        Assert.That(MoodiRescueDirector.ShouldNudge(false, .4f, 13f, 12f, 40f, 30f), Is.False, "intro still running");
        Assert.That(MoodiRescueDirector.ShouldNudge(true, 0f, 13f, 12f, 40f, 30f), Is.False, "not started yet");
        Assert.That(MoodiRescueDirector.ShouldNudge(true, 1f, 13f, 12f, 40f, 30f), Is.False, "already out");
        Assert.That(MoodiRescueDirector.ShouldNudge(true, .4f, 5f, 12f, 40f, 30f), Is.False, "pressed recently");
        Assert.That(MoodiRescueDirector.ShouldNudge(true, .4f, 13f, 12f, 10f, 30f), Is.False, "nudged recently");
    }

    [Test]
    public void JumpArcStartsAndEndsOnItsPoints()
    {
        var from = new Vector3(0f, 1f, 0f);
        var to = new Vector3(0f, 1.22f, -.1f);
        Assert.That(Vector3.Distance(MoodiRescueDirector.ArcPoint(from, to, Vector3.up, 0f, .08f), from), Is.LessThan(1e-5f));
        Assert.That(Vector3.Distance(MoodiRescueDirector.ArcPoint(from, to, Vector3.up, 1f, .08f), to), Is.LessThan(1e-5f));
        Assert.That(MoodiRescueDirector.ArcPoint(from, to, Vector3.up, .5f, .08f).y, Is.GreaterThan((from.y + to.y) * .5f));
    }
}
