using System;
namespace Moodium.Squeeze
{
    public static class EnergyAssist
    {
        public static double Multiplier(double elapsed,SqueezeMode mode) =>
            mode==SqueezeMode.Free?1:1+1.5*Math.Clamp((elapsed-75)/75,0,1);
    }
    public sealed class FreeScorer : ISqueezeScorer
    {
        public double OnStarted(SqueezeSample sample)=>5;
        public double OnHeld(SqueezeSample sample,double deltaSeconds)=>0;
        public double OnReleased(SqueezeSample sample)=>0;
    }
}
