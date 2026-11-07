using Moodium.Squeeze;
using NUnit.Framework;
public sealed class SqueezeTrackerTests
{
    [Test] public void ShortSeparationMergesButLongSeparationReleasesAtActualEnd()
    {
        var t=new SqueezeTracker();int starts=0,ends=0;double end=0;
        t.Started+=_=>starts++;t.Released+=s=>{ends++;end=s.Time;};
        t.Contact(1,0);t.Release(1,1);t.Contact(1,1.1);t.Release(1,4);t.Tick(4.16);
        Assert.That(starts,Is.EqualTo(1));Assert.That(ends,Is.EqualTo(1));Assert.That(end,Is.EqualTo(4));
    }
    [Test] public void LostTrackingUsesGraceAndSettlesAtLossTime()
    {
        var t=new SqueezeTracker();double end=-1;
        t.Released+=s=>end=s.Time;
        t.Contact(1,0);t.Release(1,2,true);t.Tick(2.2);Assert.That(t.IsHeld,Is.True);
        t.Tick(2.31);Assert.That(end,Is.EqualTo(2));
    }
    [Test] public void LastOverlappingSourceEndsSingleLogicalHold()
    {
        var t=new SqueezeTracker();int starts=0,ends=0;
        t.Started+=_=>starts++;t.Released+=_=>ends++;
        t.Contact(1,0);t.Contact(2,.1);t.Release(1,1);t.Tick(2);Assert.That(t.IsHeld,Is.True);
        t.Release(2,3);t.Tick(3.2);Assert.That(starts,Is.EqualTo(1));Assert.That(ends,Is.EqualTo(1));
    }
}
