using UnityEngine;

namespace Moodium.MoodCheck
{
    public static class MoodClassifier
    {
        public const float NeutralRadius = 0.22f;

        public static MoodKind Classify(float valence, float arousal)
        {
            var v = Mathf.Clamp(valence, -1f, 1f);
            var a = Mathf.Clamp(arousal, -1f, 1f);
            if (Mathf.Sqrt(v * v + a * a) < NeutralRadius)
                return MoodKind.Neutral;
            if (v >= 0f)
                return a >= 0f ? MoodKind.Happy : MoodKind.Calm;
            return a >= 0f ? MoodKind.Tense : MoodKind.Low;
        }

        public static float Intensity(float valence, float arousal)
        {
            var v = Mathf.Clamp(valence, -1f, 1f);
            var a = Mathf.Clamp(arousal, -1f, 1f);
            return Mathf.Clamp01(Mathf.Sqrt(v * v + a * a));
        }

        public static Vector2 Preset(MoodKind mood) => mood switch
        {
            MoodKind.Happy => new Vector2(.6f, .6f),
            MoodKind.Calm => new Vector2(.6f, -.6f),
            MoodKind.Low => new Vector2(-.6f, -.6f),
            MoodKind.Tense => new Vector2(-.6f, .6f),
            _ => Vector2.zero
        };

        public static string Recommend(MoodKind mood, bool candyAvailable, bool natureAvailable)
        {
            var prefersCandy = mood is MoodKind.Happy or MoodKind.Low;
            if (prefersCandy && candyAvailable)
                return "candy";
            if (!prefersCandy && natureAvailable)
                return "nature";
            if (candyAvailable)
                return "candy";
            if (natureAvailable)
                return "nature";
            return null;
        }

        public static MoodComparison Compare(
            Vector2 before,
            Vector2 after,
            Vector2 target,
            float threshold = .15f)
        {
            var improvement = Vector2.Distance(before, target) - Vector2.Distance(after, target);
            if (improvement + Mathf.Epsilon >= threshold)
                return MoodComparison.Better;
            if (improvement - Mathf.Epsilon <= -threshold)
                return MoodComparison.Worse;
            return MoodComparison.Same;
        }
    }
}
