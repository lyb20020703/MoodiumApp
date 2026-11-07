using System;
namespace Moodium.Squeeze
{
    public sealed class RhythmScorer : ISqueezeScorer
    {
        readonly SqueezeParameters p;
        int misses,hits;
        double last=double.NaN;
        public int Combo {get;private set;}
        public bool Adaptive {get;private set;}
        public bool LastRush {get;private set;}
        public double LastAccuracy {get;private set;}
        public double LastError {get;private set;}
        public double LastMultiplier {get;private set;}
        public double AdaptiveInterval {get;private set;}
        public double LastStart =>last;
        public RhythmScorer(SqueezeParameters parameters){p=parameters;AdaptiveInterval=p.RhythmPeriod;}
        public static double Accuracy(double error,double window)=>Math.Clamp(1-Math.Abs(error)/Math.Max(.001,window),0,1);
        public static double SpamMultiplier(double gap,SqueezeParameters p)=>gap<p.SpamHard?p.SpamHardMult:gap<p.SpamSoft?p.SpamSoftMult:1;
        public double OnStarted(SqueezeSample sample)
        {
            if(double.IsNaN(sample.Time)||double.IsInfinity(sample.Time)||(!double.IsNaN(last)&&sample.Time<last))return 0;
            // Rhythm cadence is start-to-start; breath's release-to-start gap is different.
            var gap=double.IsNaN(last)?double.PositiveInfinity:sample.Time-last;
            var beat=double.IsNaN(sample.BeatTime)?sample.Time:sample.BeatTime;
            var phase=(beat-p.BeatOffset-p.InputLatency)/p.RhythmPeriod;
            LastError=Adaptive&&!double.IsNaN(last)?Math.Abs(sample.Time-last-AdaptiveInterval):Math.Abs(phase-Math.Round(phase))*p.RhythmPeriod;
            LastAccuracy=Accuracy(LastError,Adaptive?p.AssistWindow:p.Window);
            if(LastAccuracy>0){Combo++;misses=0;hits++;}else{Combo=0;misses++;}
            LastMultiplier=1+p.ComboStep*(p.EarlyComboBoost&&hits<=8?2:1)*Math.Min(Combo,p.ComboCap);
            LastRush=Combo>0&&Combo%Math.Max(1,p.RushEvery)==0;
            var energy=(p.Base+p.Rhythm*LastAccuracy+(sample.Closure>=.6f?p.Strength:0))*LastMultiplier*SpamMultiplier(gap,p)+(LastRush?p.RushBonus:0);
            if(!Adaptive&&misses>=p.AdaptMisses)
            {Adaptive=true;AdaptiveInterval=Math.Clamp(double.IsFinite(gap)?gap:p.RhythmPeriod,p.AdaptMin,p.AdaptMax);}
            else if(Adaptive&&double.IsFinite(gap))
                AdaptiveInterval=Math.Clamp(AdaptiveInterval*.7+gap*.3,p.AdaptMin,p.AdaptMax);
            last=sample.Time;
            return Math.Max(0,energy);
        }
        public double OnHeld(SqueezeSample sample,double deltaSeconds)=>0;
        public double OnReleased(SqueezeSample sample)=>0;
    }
}
