using Moodium.MoodCheck;
namespace Moodium.Squeeze
{
    public sealed class SqueezeModeState
    {
        public SqueezeMode Current {get;private set;}
        public SqueezeMode Recommended {get;}
        public SqueezeMode? Pending {get;private set;}
        public bool ManualOverride {get;private set;}
        public SqueezeModeState(MoodKind? mood){Current=Recommended=SqueezeTuning.Recommend(mood);}
        public bool Request(SqueezeMode mode,bool held)
        {
            if(mode==Current){Pending=null;return false;}
            // Mode buttons are explicit selections: commit immediately even when
            // the sensory model is currently held. The old deferred behaviour made
            // the button look selected only after the next squeeze was released.
            Current=mode;Pending=null;ManualOverride=true;return true;
        }
        public bool Released(){if(!Pending.HasValue)return false;return Request(Pending.Value,false);}
    }
}
