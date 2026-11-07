using Dynamite3D.RealIvy;
using UnityEngine;

namespace VFXViewer
{
    [CreateAssetMenu(fileName = "EnvironmentIvyGrowIvyRuntimeConfig", menuName = "VFXViewer/Environment Ivy Grow/Ivy Runtime Config")]
    public sealed class EnvironmentIvyGrowIvyRuntimeConfig : ScriptableObject
    {
        public IvyController ivyPrefab;
        public Material worldOcclusionMaterial;
        public IvyPreset ivyPreset;
        public IvyPreset[] ivyPresets;
        public bool randomizePresets = true;

        [Min(1)]
        public int maxConcurrentIvies = 24;

        [Range(0.25f, 1f)]
        public float growthDurationScale = 0.72f;

        [Min(0f)]
        public float spawnOffset = 0f;

        [Min(0f)]
        public float spawnCooldownSeconds = 0.12f;

        [Min(0f)]
        public float minSpawnDistance = 0.05f;

        public bool triggerOnHover;
        public bool triggerOnSelect = true;
    }
}
