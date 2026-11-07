using System;
using UnityEngine;

namespace Moodium.Interaction
{
    /// <summary>
    /// How sensitive squeezing the sensory bag is, from 1 (needs a firm press) to 5 (the lightest press counts). The
    /// player sets it in Settings ▸ Sensitivity; it is kept in PlayerPrefs. Level 3 is the tuning that shipped on
    /// 2026-11-02 (press 6 mm, close 8 mm, lift 2 cm). Higher levels count smaller squeezes in place and need a smaller
    /// lift between squeezes, at the cost of more false squeezes from hand-tracking jitter.
    /// </summary>
    public static class SqueezeSensitivity
    {
        public const int MinLevel = 1, MaxLevel = 5, DefaultLevel = 3;
        public const string PrefsKey = "Moodium.SqueezeSensitivity";

        /// <summary>Thresholds for one level, in metres.</summary>
        public readonly struct Profile
        {
            public readonly int Level;
            /// <summary>Pressing down this far counts as a squeeze …</summary>
            public readonly float PressDepth;
            /// <summary>… and the hand has to come back up this far before the next one.</summary>
            public readonly float PressRebound;
            /// <summary>Fingertips closing this much closer to the palm counts as a squeeze …</summary>
            public readonly float CloseAmount;
            /// <summary>… and they have to open this far again before the next one.</summary>
            public readonly float CloseRebound;
            /// <summary>A contact ends once every contact joint is further than this from the bag.</summary>
            public readonly float ReleaseRadius;

            public Profile(int level, float pressDepth, float pressRebound, float closeAmount, float closeRebound,
                float releaseRadius)
            {
                Level = level;
                PressDepth = pressDepth;
                PressRebound = pressRebound;
                CloseAmount = closeAmount;
                CloseRebound = closeRebound;
                ReleaseRadius = releaseRadius;
            }
        }

        static readonly Profile[] Profiles =
        {
            new(1, .010f, .006f, .013f, .008f, .025f),
            new(2, .008f, .005f, .010f, .006f, .022f),
            new(3, HandSqueezePulse.PressDepth, HandSqueezePulse.PressRebound,
                HandSqueezePulse.CloseAmount, HandSqueezePulse.CloseRebound,
                TrackedCapsuleHandCollision.DefaultReleaseRadius),
            new(4, .0045f, .003f, .006f, .004f, .017f),
            new(5, .0035f, .0025f, .005f, .0035f, .015f),
        };

        static int s_Level;   // 0 = not read from PlayerPrefs yet

        public static event Action<int> Changed;

        public static int Clamp(int level) => Mathf.Clamp(level, MinLevel, MaxLevel);

        public static Profile For(int level) => Profiles[Clamp(level) - MinLevel];

        public static int Level
        {
            get
            {
                if (s_Level == 0) s_Level = Clamp(PlayerPrefs.GetInt(PrefsKey, DefaultLevel));
                return s_Level;
            }
        }

        public static Profile Current => For(Level);

        /// <summary>Applies at once to every sensory bag; <paramref name="save"/> keeps it for the next launch.</summary>
        public static void Set(int level, bool save = true)
        {
            level = Clamp(level);
            if (level == Level) return;
            s_Level = level;
            if (save)
            {
                PlayerPrefs.SetInt(PrefsKey, level);
                PlayerPrefs.Save();
            }
            Debug.Log($"[Squeeze Sensitivity] Level {level}.");
            Changed?.Invoke(level);
        }
    }
}
