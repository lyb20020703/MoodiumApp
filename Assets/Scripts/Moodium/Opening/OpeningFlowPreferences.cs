using Moodium.Flow;
using UnityEngine;

namespace Moodium.Opening
{
    /// <summary>
    /// Which questions the opening asks a returning user, set in Settings → Intro. Both default to "ask".
    ///
    /// When a question is off, the opening skips it and uses the answer already saved (the Language and
    /// Sensory bag rows of the same settings panel). The very first run always asks both: until the
    /// opening has been completed once there is no answer to fall back on.
    /// </summary>
    public static class OpeningFlowPreferences
    {
        public const string AskLanguageKey = "Moodium.Opening.AskLanguage";
        public const string AskBagKey = "Moodium.Opening.AskBag";
        /// <summary>Set by OpeningManager when the whole opening has been seen once.</summary>
        public const string CompletedKey = "Moodium.Opening.Completed";
        public const string LanguageKey = "Moodium.Language";

        public static bool AskLanguage
        {
            get => PlayerPrefs.GetInt(AskLanguageKey, 1) == 1;
            set => Save(AskLanguageKey, value);
        }

        public static bool AskBag
        {
            get => PlayerPrefs.GetInt(AskBagKey, 1) == 1;
            set => Save(AskBagKey, value);
        }

        public static bool CompletedOnce => PlayerPrefs.GetInt(CompletedKey, 0) == 1;

        public static bool SavedEnglish =>
            string.Equals(PlayerPrefs.GetString(LanguageKey, "Chinese"), "English", System.StringComparison.OrdinalIgnoreCase);

        /// <summary>Ask unless the user turned the question off, has finished the opening before, and an answer is saved.</summary>
        public static bool ShouldAsk(bool preference, bool completedOnce, bool hasSavedAnswer) =>
            preference || !completedOnce || !hasSavedAnswer;

        public static bool ShouldAskLanguage() => ShouldAsk(AskLanguage, CompletedOnce, PlayerPrefs.HasKey(LanguageKey));

        public static bool ShouldAskBag() => ShouldAsk(AskBag, CompletedOnce, PlayerPrefs.HasKey(SensoryBagOwnership.Key));

        static void Save(string key, bool ask)
        {
            PlayerPrefs.SetInt(key, ask ? 1 : 0);
            PlayerPrefs.Save();
            Debug.Log($"[Moodium Opening] {key} = {(ask ? "ask" : "skip")}");
        }
    }
}
