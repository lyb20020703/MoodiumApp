using System;
using UnityEngine;

namespace Moodium.MoodCheck
{
    public enum MoodKind { Happy, Calm, Neutral, Low, Tense }
    public enum MoodInputMethod { Plate, Quick }
    public enum MoodCheckPhase { Pre, Post }
    public enum MoodComparison { Better, Same, Worse }

    [Serializable]
    public sealed class MoodCheckSample
    {
        public string sessionId;
        public string visitId;
        public MoodCheckPhase phase;
        public float valence;
        public float arousal;
        public MoodKind mood;
        public float intensity;
        public MoodInputMethod inputMethod;
        public string recommendedWorldId;
        public string enteredWorldId;
        public string timestampUtc;
        public string language;
        public float durationInWorldSeconds;
        public int pinchCount;

        public Vector2 Position => new(valence, arousal);
    }

    public readonly struct MoodVisitMetrics
    {
        public MoodVisitMetrics(float durationInWorldSeconds, int pinchCount)
        {
            DurationInWorldSeconds = Mathf.Max(0f, durationInWorldSeconds);
            PinchCount = Mathf.Max(0, pinchCount);
        }

        public float DurationInWorldSeconds { get; }
        public int PinchCount { get; }
    }

    public readonly struct MoodCheckAvailability
    {
        public MoodCheckAvailability(bool candyAvailable, bool natureAvailable)
        {
            CandyAvailable = candyAvailable;
            NatureAvailable = natureAvailable;
        }

        public bool CandyAvailable { get; }
        public bool NatureAvailable { get; }
    }
}
