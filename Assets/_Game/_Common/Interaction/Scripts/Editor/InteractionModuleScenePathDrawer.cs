using System.Collections.Generic;
using Interaction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Interaction.Editor
{
    [InitializeOnLoad]
    internal static class InteractionModuleScenePathDrawer
    {
        private static readonly List<InteractionModule> s_SortedModulesCache = new List<InteractionModule>();
        private static bool s_SortedModulesCacheDirty = true;

        static InteractionModuleScenePathDrawer()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            SceneView.duringSceneGui += OnSceneGUI;
            EditorApplication.hierarchyChanged += MarkCacheDirty;
            EditorApplication.projectChanged += MarkCacheDirty;
            PrefabStage.prefabStageOpened += OnPrefabStageChanged;
            PrefabStage.prefabStageClosing += OnPrefabStageChanged;
        }

        private static void OnSceneGUI(SceneView sceneView)
        {
            if (sceneView == null)
                return;
            if (Event.current == null || Event.current.type != EventType.Repaint)
                return;

            var sorted = GetSortedModules();
            if (sorted.Count == 0)
                return;

            bool hasChainStart = false;
            Vector3 chainStart = Vector3.zero;
            for (int i = 0; i < sorted.Count; i++)
            {
                var module = sorted[i];
                if (!IsDrawableModule(module))
                {
                    MarkCacheDirty();
                    continue;
                }

                bool hasOwnStart;
                Vector3 ownStart;
                try
                {
                    hasOwnStart = module.TryGetGuidePreviewStartForEditor(out ownStart);
                }
                catch (MissingReferenceException)
                {
                    MarkCacheDirty();
                    continue;
                }

                if (!hasOwnStart)
                {
                    DrawModule(module);
                    continue;
                }

                if (!hasChainStart)
                {
                    chainStart = ownStart;
                    hasChainStart = true;
                }

                if (!module.DrawGuidePathGizmosForEditor(chainStart, out var moduleEnd))
                    continue;

                chainStart = moduleEnd;
            }
        }

        private static void MarkCacheDirty()
        {
            s_SortedModulesCacheDirty = true;
        }

        private static void OnPrefabStageChanged(PrefabStage _)
        {
            MarkCacheDirty();
        }

        private static List<InteractionModule> GetSortedModules()
        {
            if (!s_SortedModulesCacheDirty)
            {
                PruneInvalidCachedModules();
                return s_SortedModulesCache;
            }

            s_SortedModulesCacheDirty = false;
            s_SortedModulesCache.Clear();

            var modules = Resources.FindObjectsOfTypeAll<InteractionModule>();
            if (modules == null || modules.Length == 0)
                return s_SortedModulesCache;

            for (int i = 0; i < modules.Length; i++)
            {
                var module = modules[i];
                if (IsDrawableModule(module))
                    s_SortedModulesCache.Add(module);
            }

            s_SortedModulesCache.Sort(CompareModuleHierarchyOrder);
            return s_SortedModulesCache;
        }

        private static void PruneInvalidCachedModules()
        {
            for (int i = s_SortedModulesCache.Count - 1; i >= 0; i--)
            {
                if (!IsDrawableModule(s_SortedModulesCache[i]))
                    s_SortedModulesCache.RemoveAt(i);
            }
        }

        private static bool IsDrawableModule(InteractionModule module)
        {
            if (module == null)
                return false;

            try
            {
                if (EditorUtility.IsPersistent(module))
                    return false;

                var go = module.gameObject;
                if (go == null)
                    return false;

                var scene = go.scene;
                return scene.IsValid() && scene.isLoaded;
            }
            catch (MissingReferenceException)
            {
                return false;
            }
        }

        private static int CompareModuleHierarchyOrder(InteractionModule a, InteractionModule b)
        {
            if (ReferenceEquals(a, b))
                return 0;
            if (a == null)
                return -1;
            if (b == null)
                return 1;

            var aScene = a.gameObject.scene;
            var bScene = b.gameObject.scene;
            int sceneCompare = aScene.handle.CompareTo(bScene.handle);
            if (sceneCompare != 0)
                return sceneCompare;

            return CompareTransformOrder(a.transform, b.transform);
        }

        private static int CompareTransformOrder(Transform a, Transform b)
        {
            if (a == b)
                return 0;

            int aDepth = GetDepth(a);
            int bDepth = GetDepth(b);
            int maxDepth = Mathf.Max(aDepth, bDepth);

            for (int level = 0; level < maxDepth; level++)
            {
                int aIndex = GetSiblingIndexAtLevel(a, aDepth, level);
                int bIndex = GetSiblingIndexAtLevel(b, bDepth, level);
                int cmp = aIndex.CompareTo(bIndex);
                if (cmp != 0)
                    return cmp;
            }

            return aDepth.CompareTo(bDepth);
        }

        private static int GetDepth(Transform t)
        {
            int depth = 0;
            while (t != null)
            {
                depth++;
                t = t.parent;
            }
            return depth;
        }

        private static int GetSiblingIndexAtLevel(Transform t, int depth, int level)
        {
            int fromLeaf = depth - level - 1;
            while (fromLeaf-- > 0 && t != null)
                t = t.parent;
            return t == null ? -1 : t.GetSiblingIndex();
        }

        private static void DrawModule(InteractionModule module)
        {
            if (!IsDrawableModule(module))
                return;

            module.DrawGuidePathGizmosForEditor();
        }
    }
}
