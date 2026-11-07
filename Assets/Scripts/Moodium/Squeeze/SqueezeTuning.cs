using System;
using Moodium.MoodCheck;
using UnityEngine;

namespace Moodium.Squeeze
{
    [Serializable]
    public sealed class SqueezeParameters
    {
        public double Bpm=89, BeatsPerSqueeze=2, BeatOffset=0, InputLatency=.05;
        public double Window=.22, AssistWindow=.34, Base=.6, Rhythm=1.25, Strength=.3;
        public double ComboStep=.06, RushBonus=2, SpamHard=.55, SpamHardMult=.25, SpamSoft=.85, SpamSoftMult=.6;
        public int ComboCap=8, RushEvery=8, AdaptMisses=4;
        public double AdaptMin=.9, AdaptMax=2.2, Rate=.5, Inhale=4, FadeEnd=6;
        public double BreathBonus=5.5, QLo=1.2, QOkLo=3, QOkHi=5.5, QHi=8;
        public double ExhaleBonus=3.5, ExhaleLo=2, ExhaleOk=5, Floor=.8, FloorMinGap=1.2, FloorSpamMult=.2;
        public bool EarlyComboBoost;
        public double RhythmPeriod => 60 / Math.Max(1,Bpm) * Math.Max(1,BeatsPerSqueeze);
        public SqueezeParameters Copy() => (SqueezeParameters)MemberwiseClone();
    }
    [CreateAssetMenu(menuName="Moodium/Squeeze Tuning")]
    public sealed class SqueezeTuning : ScriptableObject
    {
        [Header("Rhythm hit feedback")]
        public AudioClip RhythmHitClip;
        [Range(0f,1f)] public float RhythmHitVolume=.22f;
        public SqueezeParameters parameters = new SqueezeParameters();
        public static SqueezeMode Recommend(MoodKind? mood) => mood switch
        { MoodKind.Happy or MoodKind.Low => SqueezeMode.Rhythm, MoodKind.Calm or MoodKind.Tense => SqueezeMode.Breath, _=>SqueezeMode.Free };
        public static SqueezeParameters ForMood(SqueezeParameters source,MoodKind? mood)
        {
            var p=source.Copy();
            if(mood==MoodKind.Low){p.Window=.28;p.EarlyComboBoost=true;}
            if(mood==MoodKind.Tense)p.ExhaleOk=6;
            return p;
        }
    }
}
