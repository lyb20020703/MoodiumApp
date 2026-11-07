using System.Linq;
using Moodium.CandyWorld;
using Moodium.Interaction;
using Moodium.Squeeze;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.XR.Hands;

/// <summary>Squeezing the sensory bag without lifting the hand off it (2026-11-02).</summary>
public sealed class SqueezeInPlaceTests
{
    const float Frame = 1f / 90f;

    /// <summary>Moves the signal linearly to <paramref name="to"/> over <paramref name="seconds"/>; returns how many squeezes fired.</summary>
    static int Ramp(SqueezePulseSignal signal, ref float value, float to, float seconds)
    {
        var fired = 0;
        var frames = Mathf.Max(1, Mathf.RoundToInt(seconds / Frame));
        var from = value;
        for (var i = 1; i <= frames; i++)
        {
            value = Mathf.Lerp(from, to, i / (float)frames);
            if (signal.Update(value, Frame, out _)) fired++;
        }
        return fired;
    }

    static SqueezePulseSignal PressSignal() => new(HandSqueezePulse.PressDepth, HandSqueezePulse.PressRebound,
        HandSqueezePulse.SmoothingSeconds, HandSqueezePulse.HighHoldSeconds, HandSqueezePulse.DriftPerSecond);

    [Test]
    public void HandSettlingOntoTheBagIsNotASecondSqueeze()
    {
        var signal = PressSignal();
        var h = .10f;
        signal.Update(h, Frame, out _);
        Assert.That(signal.Pressed, Is.True, "The contact that started the hold already was a squeeze.");
        Assert.That(Ramp(signal, ref h, .085f, .3f), Is.EqualTo(0));
        Assert.That(Ramp(signal, ref h, .085f, 1f), Is.EqualTo(0));
    }

    [Test]
    public void PressingDownAgainAfterEasingOffCountsOncePerPress()
    {
        var signal = PressSignal();
        var h = .10f;
        signal.Update(h, Frame, out _);
        Assert.That(Ramp(signal, ref h, .107f, .2f), Is.EqualTo(0), "Easing off only re-arms.");
        Assert.That(signal.Pressed, Is.False);
        // Smoothing lags a few milliseconds behind a fast press, so each press is followed by a short pause.
        Assert.That(Ramp(signal, ref h, .095f, .15f) + Ramp(signal, ref h, .095f, .1f), Is.EqualTo(1),
            "A 12 mm press is one squeeze.");
        Assert.That(Ramp(signal, ref h, .093f, .5f), Is.EqualTo(0), "Holding it down is still the same squeeze.");
        Assert.That(Ramp(signal, ref h, .101f, .2f), Is.EqualTo(0));
        Assert.That(Ramp(signal, ref h, .090f, .15f) + Ramp(signal, ref h, .090f, .1f), Is.EqualTo(1));
    }

    [Test]
    public void SmallPressesBelowTheThresholdDoNotCount()
    {
        var signal = PressSignal();
        var h = .10f;
        signal.Update(h, Frame, out _);
        Ramp(signal, ref h, .107f, .2f);
        Assert.That(Ramp(signal, ref h, .102f, .15f) + Ramp(signal, ref h, .102f, .5f), Is.EqualTo(0),
            "5 mm is under the 6 mm press depth.");
    }

    [Test]
    public void TrackingJitterDoesNotCount()
    {
        var signal = PressSignal();
        signal.Update(.10f, Frame, out _);
        var fired = 0;
        for (var i = 0; i < 400; i++)
            if (signal.Update(.10f + (i % 2 == 0 ? .003f : -.003f) + (i % 7) * .0005f, Frame, out _))
                fired++;
        Assert.That(fired, Is.EqualTo(0));
    }

    [Test]
    public void SlowDriftDownIsNotASqueeze()
    {
        var signal = PressSignal();
        var h = .10f;
        signal.Update(h, Frame, out _);
        Ramp(signal, ref h, .107f, .2f);
        Assert.That(Ramp(signal, ref h, .090f, 3f), Is.EqualTo(0), "Sinking 17 mm over three seconds is settling.");
    }

    [Test]
    public void PressingAndClosingTogetherCountOnce()
    {
        var pulse = new HandSqueezePulse();
        float h = .10f, s = .09f;
        var fired = 0;
        void Step(float toH, float toS, float seconds)
        {
            var frames = Mathf.RoundToInt(seconds / Frame);
            float fromH = h, fromS = s;
            for (var i = 1; i <= frames; i++)
            {
                h = Mathf.Lerp(fromH, toH, i / (float)frames);
                s = Mathf.Lerp(fromS, toS, i / (float)frames);
                if (pulse.Update(new HandSqueezeSample(h, s), Frame, out _, out _)) fired++;
            }
        }
        Step(h, s, Frame);
        Step(.107f, .098f, .2f);
        Step(.095f, .084f, .15f);
        Step(.095f, .084f, .1f);
        Assert.That(fired, Is.EqualTo(1));
        Step(.095f, .080f, .3f);
        Assert.That(fired, Is.EqualTo(1), "Closing further without opening first is the same squeeze.");
        Step(.102f, .088f, .2f);
        Step(.102f, .076f, .15f);
        Step(.102f, .076f, .1f);
        Assert.That(fired, Is.EqualTo(2), "Closing the fingers alone also counts.");
    }

    [Test]
    public void TrackerPulseWhileHeldEndsOneSqueezeAndStartsTheNext()
    {
        var t = new SqueezeTracker();
        int starts = 0, ends = 0;
        double endedAt = -1, lastGap = -1;
        t.Started += s => { starts++; lastGap = s.GapSeconds; };
        t.Released += s => { ends++; endedAt = s.Time; };
        t.Pulse(1, 0);
        Assert.That(starts, Is.EqualTo(1));
        t.Contact(1, .5);
        t.Pulse(1, 1);
        Assert.That(starts, Is.EqualTo(2));
        Assert.That(ends, Is.EqualTo(1));
        Assert.That(endedAt, Is.EqualTo(1));
        Assert.That(lastGap, Is.EqualTo(0));
        Assert.That(t.IsHeld, Is.True);
    }

    [Test]
    public void TrackerPulseDuringReleaseGraceIsANewSqueeze()
    {
        var t = new SqueezeTracker();
        int starts = 0;
        double endedAt = -1, lastGap = -1;
        t.Started += s => { starts++; lastGap = s.GapSeconds; };
        t.Released += s => endedAt = s.Time;
        t.Contact(1, 0);
        t.Release(1, 1);
        t.Pulse(1, 1.1);
        Assert.That(starts, Is.EqualTo(2));
        Assert.That(endedAt, Is.EqualTo(1));
        Assert.That(lastGap, Is.EqualTo(.1).Within(1e-9));
    }

    [Test]
    public void AnotherSourceJoiningARunningSqueezeIsTheSameSqueeze()
    {
        var t = new SqueezeTracker();
        int starts = 0;
        t.Started += _ => starts++;
        t.Contact(1, 0);
        t.Pulse(2, .2);
        Assert.That(starts, Is.EqualTo(1));
    }

    [Test]
    public void GatePulseNeedsARunningContactAndSharesTheCooldown()
    {
        var gate = new HandContactStabilityGate(releaseGraceSeconds: .18f, cooldownSeconds: .35f);
        Assert.That(gate.TryPulse(0f), Is.False, "No contact, no squeeze in place.");
        Assert.That(gate.Update(true, 0f), Is.True);
        Assert.That(gate.TryPulse(.2f), Is.False, "Too soon after the contact started.");
        Assert.That(gate.TryPulse(.4f), Is.True);
        Assert.That(gate.TryPulse(.6f), Is.False);
        gate.Update(false, .8f);
        Assert.That(gate.IsContactActive, Is.False);
        Assert.That(gate.Update(true, .85f), Is.True, "A fresh contact after the cooldown still counts.");
    }

    [Test]
    public void CapsulePulseFiresThePressEventWithoutLiftingOff()
    {
        var bag = new GameObject("Bag");
        var count = 0;
        void Count(ChocolateCapsuleInteraction _, Vector3 __) => count++;
        ChocolateCapsuleInteraction.AnyCapsulePinched += Count;
        try
        {
            var capsule = bag.AddComponent<ChocolateCapsuleInteraction>();
            Assert.That(capsule.HandSqueezeInPlace(Vector3.zero), Is.False, "Disabled bag.");
            capsule.SetInteractionEnabled(true);
            Assert.That(capsule.HandSqueezeInPlace(Vector3.zero), Is.True, "First squeeze of an uncounted contact.");
            Assert.That(capsule.IsPinched, Is.True);
            Assert.That(capsule.HandSqueezeInPlace(Vector3.zero), Is.True);
            Assert.That(capsule.IsPinched, Is.True);
            Assert.That(count, Is.EqualTo(2));
        }
        finally
        {
            ChocolateCapsuleInteraction.AnyCapsulePinched -= Count;
            Object.DestroyImmediate(bag);
        }
    }

    [Test]
    public void SqueezesInPlaceScoreInFreeAndRhythmButBreathKeepsOneHold()
    {
        var root = new GameObject("Session");
        var bag = new GameObject("Bag");
        try
        {
            var energy = root.AddComponent<CandyEnergyController>();
            var controller = root.AddComponent<SqueezeModeController>();
            var capsule = bag.AddComponent<ChocolateCapsuleInteraction>();
            capsule.SetInteractionEnabled(true);
            controller.BeginSession(energy, null, false, _ => true);
            Assert.That(controller.Mode, Is.EqualTo(SqueezeMode.Free));
            controller.AcceptPinch(capsule);
            controller.AcceptPinch(capsule);
            Assert.That(energy.Energy, Is.EqualTo(10f), "Free: 5 per squeeze.");

            controller.RequestMode(SqueezeMode.Rhythm);
            var hits = 0;
            controller.RhythmHit += () => hits++;
            controller.AcceptPinch(capsule);
            controller.AcceptPinch(capsule);
            Assert.That(hits, Is.EqualTo(2), "Rhythm: every squeeze in place is a beat.");

            controller.RequestMode(SqueezeMode.Breath);
            var count = typeof(SqueezeModeController).GetField("squeezeCount",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var before = (int)count.GetValue(controller);
            controller.AcceptPinch(capsule);
            Assert.That(controller.IsHeld, Is.True);
            controller.AcceptPinch(capsule);
            Assert.That((int)count.GetValue(controller), Is.EqualTo(before), "Breath: still the same held breath.");
        }
        finally
        {
            root.GetComponent<SqueezeModeController>()?.StopSession();
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(bag);
        }
    }

    [Test]
    public void ContactUsesFingertipsAndPalmWithASmallerReachAndHysteresis()
    {
        var joints = TrackedCapsuleHandCollision.ContactJointIds;
        Assert.That(joints, Does.Contain(XRHandJointID.Palm));
        Assert.That(joints, Does.Contain(XRHandJointID.IndexTip));
        Assert.That(joints, Does.Contain(XRHandJointID.ThumbTip));
        Assert.That(joints.Contains(XRHandJointID.Wrist), Is.False, "A wrist resting on the table must not hold the contact.");
        Assert.That(joints.Any(j => j.ToString().EndsWith("Metacarpal") || j.ToString().EndsWith("Proximal")), Is.False);
        Assert.That(TrackedCapsuleHandCollision.DefaultContactRadius, Is.LessThan(.025f));
        Assert.That(TrackedCapsuleHandCollision.DefaultReleaseRadius, Is.GreaterThan(TrackedCapsuleHandCollision.DefaultContactRadius));
        Assert.That(TrackedCapsuleHandCollision.DefaultReleaseRadius, Is.LessThanOrEqualTo(.02f));
    }
}
