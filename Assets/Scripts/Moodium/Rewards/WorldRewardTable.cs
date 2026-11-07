using System;
using System.Collections.Generic;
using UnityEngine;

namespace Moodium.Rewards
{
    public enum RewardSizeClass
    {
        Small,
        Medium,
        Large
    }

    [Serializable]
    public struct RewardEntry
    {
        public int stage;
        public GameObject[] prefabs;
        public string displayName;
        public RewardSizeClass sizeClass;
        [Min(.01f)] public float targetHeight;
        [Tooltip("Negative values use automatic miniature fitting.")]
        public float miniatureScaleOverride;

        public bool IsValid => WorldRewardTable.IsSupportedStage(stage) &&
                               prefabs != null && Array.Exists(prefabs, prefab => prefab != null) &&
                               !string.IsNullOrWhiteSpace(displayName) &&
                               float.IsFinite(targetHeight) && targetHeight > 0f;
    }

    [CreateAssetMenu(menuName = "Moodium/Rewards/World Reward Table", fileName = "WorldRewardTable")]
    public sealed class WorldRewardTable : ScriptableObject
    {
        [SerializeField] List<RewardEntry> m_Entries = new();

        public IReadOnlyList<RewardEntry> Entries => m_Entries;

        public static bool IsSupportedStage(int stage) => stage is 20 or 50 or 80 or 100;

        public bool TryGetEntry(int stage, out RewardEntry entry)
        {
            if (m_Entries != null)
                foreach (var candidate in m_Entries)
                    if (candidate.stage == stage && candidate.IsValid)
                    {
                        entry = candidate;
                        return true;
                    }

            entry = default;
            return false;
        }
    }
}
