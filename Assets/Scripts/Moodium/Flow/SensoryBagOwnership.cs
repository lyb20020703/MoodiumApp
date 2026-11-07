using UnityEngine;

namespace Moodium.Flow
{
    public static class SensoryBagOwnership
    {
        public const string Key = "Moodium.HasSensoryBag";
        public static bool HasBag => PlayerPrefs.GetInt(Key, 1) == 1;
        public static void SetHasBag(bool hasBag)
        {
            PlayerPrefs.SetInt(Key, hasBag ? 1 : 0);
            PlayerPrefs.Save();
            Debug.Log($"[Sensory Bag Branch] mode={(hasBag ? "has" : "none")} step=ownership-saved");
        }
    }
}
