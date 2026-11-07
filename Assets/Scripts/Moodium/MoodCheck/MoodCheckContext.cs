using System;

namespace Moodium.MoodCheck
{
    public sealed class MoodCheckContext
    {
        float m_VisitStartRealtime;
        bool m_AutoPromptShown;
        public readonly System.Collections.Generic.HashSet<string> SqueezeModesUsed=new();
        public int BreathCount {get;private set;}
        public float BeatAccuracySum {get;private set;}
        public int BeatAccuracyCount {get;private set;}
        public void RecordSqueezeMode(string mode){if(HasActiveVisit)SqueezeModesUsed.Add(mode);}
        public void RecordBreath(){if(HasActiveVisit)BreathCount++;}
        public void RecordRhythmAccuracy(float accuracy){if(HasActiveVisit){BeatAccuracySum+=accuracy;BeatAccuracyCount++;}}

        public MoodCheckContext()
        {
            SessionId = Guid.NewGuid().ToString("N");
        }

        public string SessionId { get; }
        public MoodCheckSample PreSample { get; private set; }
        public string ActiveVisitId { get; private set; }
        public string EnteredWorldId { get; private set; }
        public int PinchCount { get; private set; }
        public bool HasActiveVisit => !string.IsNullOrEmpty(ActiveVisitId);

        public void SetPreSample(MoodCheckSample sample) => PreSample = sample;

        public string BeginVisit(string enteredWorldId, float realtime)
        {
            ActiveVisitId = Guid.NewGuid().ToString("N");
            EnteredWorldId = enteredWorldId ?? string.Empty;
            m_VisitStartRealtime = realtime;
            PinchCount = 0;
            SqueezeModesUsed.Clear();BreathCount=BeatAccuracyCount=0;BeatAccuracySum=0;
            return ActiveVisitId;
        }

        public void RegisterPinch()
        {
            if (HasActiveVisit)
                PinchCount++;
        }

        public MoodVisitMetrics SnapshotMetrics(float realtime) =>
            HasActiveVisit
                ? new MoodVisitMetrics(realtime - m_VisitStartRealtime, PinchCount)
                : new MoodVisitMetrics(0f, 0);

        public void CompleteVisit()
        {
            ActiveVisitId = null;
            EnteredWorldId = null;
            PinchCount = 0;
            m_VisitStartRealtime = 0f;
        }

        public bool TryMarkAutoPromptShown()
        {
            if (m_AutoPromptShown)
                return false;
            m_AutoPromptShown = true;
            return true;
        }
    }
}
