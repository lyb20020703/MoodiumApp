using Moodium.Squeeze;
using NUnit.Framework;

public sealed class SqueezeScoringTests
{
    [Test] public void RhythmUsesStartToStartIntervalNotTimeSpentExhaling()
    {
        var p=new SqueezeParameters{InputLatency=0};var r=new RhythmScorer(p);
        r.OnStarted(new SqueezeSample(p.RhythmPeriod,0,.1,null,p.RhythmPeriod));
        r.OnStarted(new SqueezeSample(p.RhythmPeriod*2,0,.1,null,p.RhythmPeriod*2));
        Assert.That(r.LastAccuracy,Is.EqualTo(1));
        Assert.That(r.OnStarted(new SqueezeSample(p.RhythmPeriod*3,0,.1,null,p.RhythmPeriod*3)),Is.GreaterThan(1.8));
    }
    [Test] public void FreeAndAssistPreserveLegacyAndBoundedBoost()
    {
        Assert.That(new FreeScorer().OnStarted(new SqueezeSample(0)), Is.EqualTo(5));
        Assert.That(EnergyAssist.Multiplier(75, SqueezeMode.Rhythm), Is.EqualTo(1));
        Assert.That(EnergyAssist.Multiplier(150, SqueezeMode.Breath), Is.EqualTo(2.5));
        Assert.That(EnergyAssist.Multiplier(150, SqueezeMode.Free), Is.EqualTo(1));
    }
    [Test] public void BreathIntegralIsFrameRateIndependentAndStopsAfterSixSeconds()
    {
        Assert.That(BreathScorer.HoldIntegral(4), Is.EqualTo(2).Within(.00001));
        Assert.That(BreathScorer.HoldIntegral(6), Is.EqualTo(2.5).Within(.00001));
        Assert.That(BreathScorer.HoldIntegral(20), Is.EqualTo(2.5).Within(.00001));
        var breath = new BreathScorer(new SqueezeParameters());
        double total = 0;
        for (var i = 1; i <= 400; i++) total += breath.OnHeld(new SqueezeSample(i * .01, i * .01), .01);
        Assert.That(total, Is.EqualTo(2).Within(.00001));
    }
    [TestCase(1.2,0)] [TestCase(3,1)] [TestCase(5.5,1)] [TestCase(8,0)]
    public void BreathQualityUsesTrapezoid(double hold,double expected) =>
        Assert.That(BreathScorer.Quality(hold), Is.EqualTo(expected).Within(.00001));
    [Test] public void RhythmHitsBoundaryComboAndSpamAreDeterministic()
    {
        var p = new SqueezeParameters { InputLatency = 0 };
        var rhythm = new RhythmScorer(p);
        var period = p.RhythmPeriod;
        for (var i=1;i<=8;i++) rhythm.OnStarted(new SqueezeSample(i*period,0,period,null,i*period));
        Assert.That(rhythm.Combo, Is.EqualTo(8));
        Assert.That(rhythm.LastAccuracy, Is.EqualTo(1).Within(.00001));
        Assert.That(rhythm.LastRush, Is.True);
        Assert.That(rhythm.LastMultiplier, Is.EqualTo(1.48).Within(.00001));
        Assert.That(RhythmScorer.Accuracy(p.Window,p.Window), Is.Zero);
        Assert.That(RhythmScorer.SpamMultiplier(.4,p), Is.EqualTo(.25));
        Assert.That(RhythmScorer.SpamMultiplier(.7,p), Is.EqualTo(.6));
    }
    [Test] public void FourMissesAdaptToStableUserInterval()
    {
        var p = new SqueezeParameters { InputLatency = 0 };
        var rhythm = new RhythmScorer(p);
        for(var i=1;i<=4;i++) rhythm.OnStarted(new SqueezeSample(i*1.1,0,1.1,null,p.RhythmPeriod*.5));
        Assert.That(rhythm.Adaptive, Is.True);
        rhythm.OnStarted(new SqueezeSample(5.5,0,1.1,null,99));
        Assert.That(rhythm.LastAccuracy, Is.EqualTo(1).Within(.00001));
    }
    [Test] public void ExhaleRewardsRespectTargetAndSpamFloor()
    {
        Assert.That(BreathScorer.ExhaleQuality(2,5), Is.Zero);
        Assert.That(BreathScorer.ExhaleQuality(5,5), Is.EqualTo(1));
        var b = new BreathScorer(new SqueezeParameters());
        b.OnReleased(new SqueezeSample(4,4));
        Assert.That(b.OnStarted(new SqueezeSample(4.4,0,.4)), Is.EqualTo(.16).Within(.00001));
    }
}
