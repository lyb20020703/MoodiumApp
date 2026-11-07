using UnityEngine;

namespace Moodium.Flow
{
    public static class WorldModelPrefabResolver
    {
        public static GameObject Resolve(
            string worldId,
            GameObject candyPrefab,
            GameObject naturePrefab,
            GameObject fallbackPrefab)
        {
            if (string.Equals(worldId, "candy", System.StringComparison.OrdinalIgnoreCase))
                return candyPrefab;
            if (string.Equals(worldId, "nature", System.StringComparison.OrdinalIgnoreCase))
                return naturePrefab;
            return fallbackPrefab;
        }
    }
}
