namespace Moodium.Squeeze
{
    public enum SqueezeMode { Free, Rhythm, Breath }
    public readonly struct SqueezeSample
    {
        public readonly double Time, HoldSeconds, GapSeconds, BeatTime;
        public readonly float? Closure;
        public SqueezeSample(double time,double holdSeconds=0,double gapSeconds=double.PositiveInfinity,
            float? closure=null,double beatTime=double.NaN)
        { Time=time; HoldSeconds=holdSeconds; GapSeconds=gapSeconds; Closure=closure; BeatTime=beatTime; }
    }
    public interface ISqueezeScorer
    {
        double OnStarted(SqueezeSample sample);
        double OnHeld(SqueezeSample sample,double deltaSeconds);
        double OnReleased(SqueezeSample sample);
    }
}
