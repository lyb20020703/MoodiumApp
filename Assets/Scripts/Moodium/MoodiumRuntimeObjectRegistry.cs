using System.Collections.Generic;
using UnityEngine;

namespace Moodium.Flow
{
    public static class MoodiumRuntimeObjectRegistry
    {
        static readonly HashSet<GameObject> CreativeObjects = new();

        public static void RegisterCreative(GameObject instance)
        {
            if (instance != null)
                CreativeObjects.Add(instance);
        }

        public static bool IsCreativeObject(GameObject instance)
        {
            RemoveDestroyedEntries();
            return instance != null && CreativeObjects.Contains(instance);
        }

        public static int ClearCreativeObjects()
        {
            RemoveDestroyedEntries();
            var removed = 0;
            foreach (var instance in CreativeObjects)
            {
                if (instance == null)
                    continue;
                Object.Destroy(instance);
                removed++;
            }
            CreativeObjects.Clear();
            Debug.Log($"[Moodium Runtime Registry] Cleared {removed} Creative Space runtime objects/effects.");
            return removed;
        }

        static void RemoveDestroyedEntries() => CreativeObjects.RemoveWhere(instance => instance == null);
    }
}
