using System;
using System.Collections.Generic;
namespace Moodium.Squeeze
{
    /// <summary>One logical squeeze across overlapping tracked sources. All times are monotonic seconds.</summary>
    public sealed class SqueezeTracker
    {
        readonly HashSet<int> sources=new();
        double start,lastPaid,lastRelease=double.NaN,pending=double.NaN,grace=.15;
        public bool IsHeld {get;private set;}
        public double StartedAt=>start;
        public event Action<SqueezeSample> Started,Released;
        public event Action<SqueezeSample,double> Held;
        public void Contact(int source,double now)
        {
            if(!double.IsFinite(now))return;
            Tick(now);
            sources.Add(source);
            pending=double.NaN;grace=.15;
            if(IsHeld)return;
            IsHeld=true;start=lastPaid=now;
            Started?.Invoke(new SqueezeSample(now,0,double.IsNaN(lastRelease)?double.PositiveInfinity:Math.Max(0,now-lastRelease)));
        }
        /// <summary>
        /// A new squeeze from <paramref name="source"/>: while the hand is still on the bag (or came back within the
        /// release grace) it closes the running squeeze and starts the next one. Another source joining a running
        /// squeeze is still the same squeeze (one hand over two overlapping tracked bags).
        /// </summary>
        public void Pulse(int source,double now)
        {
            if(!double.IsFinite(now))return;
            Tick(now);
            if(!IsHeld||(double.IsNaN(pending)&&!sources.Contains(source))){Contact(source,now);return;}
            var end=double.IsNaN(pending)?now:pending;
            sources.Clear();pending=double.NaN;grace=.15;lastRelease=end;
            Released?.Invoke(new SqueezeSample(end,Math.Max(0,end-start)));
            sources.Add(source);start=lastPaid=now;
            Started?.Invoke(new SqueezeSample(now,0,Math.Max(0,now-end)));
        }
        public void Release(int source,double now,bool trackingLost=false)
        {
            sources.Remove(source);
            if(!IsHeld||sources.Count>0)return;
            if(double.IsNaN(pending)){pending=now;grace=trackingLost?.3:.15;}
            else if(trackingLost)grace=Math.Max(grace,.3);
        }
        public void Tick(double now)
        {
            if(!IsHeld||!double.IsFinite(now))return;
            var through=double.IsNaN(pending)?now:Math.Min(now,pending);
            if(through>lastPaid)
            {Held?.Invoke(new SqueezeSample(through,through-start),through-lastPaid);lastPaid=through;}
            if(!double.IsNaN(pending)&&now-pending>=grace)
            {
                var end=pending;
                IsHeld=false;sources.Clear();pending=double.NaN;lastRelease=end;
                Released?.Invoke(new SqueezeSample(end,Math.Max(0,end-start)));
            }
        }
        public void Reset(){sources.Clear();IsHeld=false;pending=lastRelease=double.NaN;}
        public void ReleaseAllForExit(double now)
        {sources.Clear();if(IsHeld&&double.IsNaN(pending))pending=now;}
    }
}
