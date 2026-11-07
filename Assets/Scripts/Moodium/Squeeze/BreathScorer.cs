using System;
namespace Moodium.Squeeze
{
    public sealed class BreathScorer : ISqueezeScorer
    {
        readonly SqueezeParameters p;
        double released=double.NaN;
        public double LastQuality {get;private set;}
        public double LastExhaleQuality {get;private set;}
        public int BreathCount {get;private set;}
        public BreathScorer(SqueezeParameters parameters){p=parameters;}
        public static double Quality(double t,double lo=1.2,double okLo=3,double okHi=5.5,double hi=8)=>
            t<lo||t>hi?0:t<okLo?(t-lo)/(okLo-lo):t<=okHi?1:(hi-t)/(hi-okHi);
        public static double ExhaleQuality(double gap,double target)=>Math.Clamp((gap-2)/Math.Max(.001,target-2),0,1);
        public static double HoldIntegral(double hold,double rate=.5,double inhale=4,double end=6)
        {
            var first=Math.Clamp(hold,0,inhale);
            var tail=Math.Clamp(hold-inhale,0,Math.Max(.001,end-inhale));
            return rate*(first+tail-tail*tail/(2*Math.Max(.001,end-inhale)));
        }
        public double OnStarted(SqueezeSample sample)
        {
            LastExhaleQuality=double.IsNaN(released)?0:ExhaleQuality(Math.Max(0,sample.Time-released),p.ExhaleOk);
            return p.ExhaleBonus*LastExhaleQuality+p.Floor*(sample.GapSeconds>=p.FloorMinGap?1:p.FloorSpamMult);
        }
        public double OnHeld(SqueezeSample sample,double deltaSeconds)=>
            HoldIntegral(sample.HoldSeconds,p.Rate,p.Inhale,p.FadeEnd)-HoldIntegral(Math.Max(0,sample.HoldSeconds-Math.Max(0,deltaSeconds)),p.Rate,p.Inhale,p.FadeEnd);
        public double OnReleased(SqueezeSample sample)
        {
            released=sample.Time;
            LastQuality=Quality(sample.HoldSeconds,p.QLo,p.QOkLo,p.QOkHi,p.QHi);
            if(LastQuality>0)BreathCount++;
            return p.BreathBonus*LastQuality;
        }
    }
}
