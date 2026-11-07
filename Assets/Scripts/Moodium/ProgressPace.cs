using System;
using UnityEngine;

namespace Moodium.CandyWorld
{
    /// <summary>
    /// How fast the energy bar fills: Settings ▸ 进度快慢 / Pace. Three options, kept in PlayerPrefs:
    ///  0 标准 / Normal ×1 (the tuning everyone gets by default),
    ///  1 轻快 / Brisk  ×2,
    ///  2 速览 / Quick  ×4 (short demos: every squeeze mode reaches 100 % in well under a minute).
    /// It scales every energy gain in <see cref="CandyEnergyController.AddEnergy"/>, so all three squeeze modes keep
    /// their own feel (what earns the most) and only the time to 100 % changes. Stage rewards (20 / 50 / 80) and the
    /// 100 % sequence are untouched.
    /// </summary>
    public static class ProgressPace
    {
        public const int Normal = 0, Brisk = 1, Quick = 2, Count = 3, DefaultLevel = Normal;
        public const string PrefsKey = "Moodium.ProgressPace";

        static readonly float[] Multipliers = { 1f, 2f, 4f };
        static readonly string[] Names = { "Normal", "Brisk", "Quick" };

        static int s_Level = -1;   // -1 = not read from PlayerPrefs yet

        public static event Action<int> Changed;

        public static int Clamp(int level) => Mathf.Clamp(level, 0, Count - 1);

        public static float MultiplierFor(int level) => Multipliers[Clamp(level)];

        public static string NameOf(int level) => Names[Clamp(level)];

        public static int Level
        {
            get
            {
                if (s_Level < 0) s_Level = Clamp(PlayerPrefs.GetInt(PrefsKey, DefaultLevel));
                return s_Level;
            }
        }

        public static float Multiplier => MultiplierFor(Level);

        /// <summary>Applies to the next energy gain; <paramref name="save"/> keeps it for the next launch.</summary>
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
            Debug.Log($"[Progress Pace] {NameOf(level)} (energy ×{MultiplierFor(level):0}).");
            Changed?.Invoke(level);
        }
    }
}
