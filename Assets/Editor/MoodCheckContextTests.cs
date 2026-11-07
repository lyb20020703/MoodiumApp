using Moodium.MoodCheck;
using NUnit.Framework;

public sealed class MoodCheckContextTests
{
    [Test]
    public void Visits_KeepSessionIdButReceiveUniqueVisitIds()
    {
        var context = new MoodCheckContext();
        var session = context.SessionId;

        var first = context.BeginVisit("candy", 10f);
        context.CompleteVisit();
        var second = context.BeginVisit("nature", 20f);

        Assert.That(session, Is.Not.Empty);
        Assert.That(context.SessionId, Is.EqualTo(session));
        Assert.That(first, Is.Not.EqualTo(second));
    }

    [Test]
    public void VisitMetrics_CountOnlyActiveVisitAndClampNegativeDuration()
    {
        var context = new MoodCheckContext();
        context.RegisterPinch();
        context.BeginVisit("candy", 10f);
        context.RegisterPinch();
        context.RegisterPinch();

        var metrics = context.SnapshotMetrics(5f);

        Assert.That(metrics.DurationInWorldSeconds, Is.Zero);
        Assert.That(metrics.PinchCount, Is.EqualTo(2));
        context.CompleteVisit();
        context.RegisterPinch();
        Assert.That(context.ActiveVisitId, Is.Null);
        Assert.That(context.PinchCount, Is.Zero);
    }

    [Test]
    public void PreSampleAndAutoPromptStateSurviveVisitCompletion()
    {
        var context = new MoodCheckContext();
        var sample = new MoodCheckSample { mood = MoodKind.Calm };
        context.SetPreSample(sample);
        context.BeginVisit("nature", 1f);
        context.CompleteVisit();

        Assert.That(context.PreSample, Is.SameAs(sample));
        Assert.That(context.TryMarkAutoPromptShown(), Is.True);
        Assert.That(context.TryMarkAutoPromptShown(), Is.False);
    }
}
