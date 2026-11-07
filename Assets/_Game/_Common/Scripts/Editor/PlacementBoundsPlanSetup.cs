using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace VFXViewer.Editor
{
    public static class PlacementBoundsPlanSetup
    {
        private const string PlanAssetPath = "Assets/_Game/_Common/Config/ChapterPlacementPlan.asset";
        private const string PlacementBoundsName = "PlacementBounds";

        [MenuItem("工具/布展/为 ChapterPlan 生成 PlacementBounds", priority = 220)]
        public static void GenerateForChapterPlan()
        {
            Execute();
        }

        public static void GenerateForChapterPlanBatch()
        {
            Execute();
        }

        private static void Execute()
        {
            var plan = AssetDatabase.LoadAssetAtPath<ChapterPlacementPlan>(PlanAssetPath);
            if (plan == null)
                throw new System.InvalidOperationException($"ChapterPlacementPlan not found at {PlanAssetPath}.");

            var prefabPaths = CollectPrefabPaths(plan);
            if (prefabPaths.Count == 0)
            {
                Debug.LogWarning("[PlacementBoundsPlanSetup] No prefabs found in ChapterPlacementPlan.");
                return;
            }

            int changedCount = 0;
            foreach (string prefabPath in prefabPaths)
            {
                var root = PrefabUtility.LoadPrefabContents(prefabPath);
                bool changed = false;

                try
                {
                    changed = EnsurePlacementBounds(root);
                    if (changed)
                    {
                        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                        changedCount++;
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[PlacementBoundsPlanSetup] Updated {changedCount}/{prefabPaths.Count} prefabs from {PlanAssetPath}.");
        }

        private static List<string> CollectPrefabPaths(ChapterPlacementPlan plan)
        {
            var results = new List<string>();
            var visited = new HashSet<string>();
            if (plan == null || plan.chapters == null)
                return results;

            for (int c = 0; c < plan.chapters.Count; c++)
            {
                var chapter = plan.chapters[c];
                if (chapter == null || chapter.steps == null)
                    continue;

                for (int s = 0; s < chapter.steps.Count; s++)
                {
                    var step = chapter.steps[s];
                    if (step == null || step.prefab == null)
                        continue;

                    string prefabPath = AssetDatabase.GetAssetPath(step.prefab);
                    if (string.IsNullOrWhiteSpace(prefabPath) || !visited.Add(prefabPath))
                        continue;

                    results.Add(prefabPath);
                }
            }

            return results;
        }

        private static bool EnsurePlacementBounds(GameObject root)
        {
            if (root == null)
                return false;

            Transform rootTransform = root.transform;
            Transform boundsTransform = rootTransform.Find(PlacementBoundsName);
            bool changed = false;
            bool createdBoundsObject = false;

            if (boundsTransform == null)
            {
                var boundsObject = new GameObject(PlacementBoundsName);
                boundsTransform = boundsObject.transform;
                boundsTransform.SetParent(rootTransform, false);
                changed = true;
                createdBoundsObject = true;
            }

            if (createdBoundsObject && boundsTransform.localRotation != Quaternion.identity)
            {
                boundsTransform.localRotation = Quaternion.identity;
                changed = true;
            }

            if (createdBoundsObject && boundsTransform.localScale != Vector3.one)
            {
                boundsTransform.localScale = Vector3.one;
                changed = true;
            }

            var boxCollider = boundsTransform.GetComponent<BoxCollider>();
            bool createdBoxCollider = false;
            if (boxCollider == null)
            {
                boxCollider = boundsTransform.gameObject.AddComponent<BoxCollider>();
                changed = true;
                createdBoxCollider = true;
            }

            var placementBounds = boundsTransform.GetComponent<PlacementBounds>();
            bool createdPlacementBounds = false;
            if (placementBounds == null)
            {
                placementBounds = boundsTransform.gameObject.AddComponent<PlacementBounds>();
                changed = true;
                createdPlacementBounds = true;
            }

            bool needsDefaultShape = createdBoundsObject ||
                                     createdBoxCollider ||
                                     ColliderHasNoValidShape(boxCollider);
            if (needsDefaultShape)
            {
                // Prefer authored collider shapes if the module already has them; otherwise fall back to renderers.
                Bounds localBounds;
                if (!TryCalculateBoundsFromColliders(rootTransform, boundsTransform, out localBounds) &&
                    !TryCalculateBoundsFromRenderers(rootTransform, boundsTransform, out localBounds))
                {
                    localBounds = new Bounds(Vector3.zero, new Vector3(0.25f, 0.25f, 0.25f));
                }

                Vector3 clampedSize = ClampSize(localBounds.size);
                if (boundsTransform.localPosition != localBounds.center)
                {
                    boundsTransform.localPosition = localBounds.center;
                    changed = true;
                }

                if (boundsTransform.localRotation != Quaternion.identity)
                {
                    boundsTransform.localRotation = Quaternion.identity;
                    changed = true;
                }

                if (boundsTransform.localScale != Vector3.one)
                {
                    boundsTransform.localScale = Vector3.one;
                    changed = true;
                }

                if (boxCollider.center != Vector3.zero)
                {
                    boxCollider.center = Vector3.zero;
                    changed = true;
                }

                if (boxCollider.size != clampedSize)
                {
                    boxCollider.size = clampedSize;
                    changed = true;
                }

                if (boxCollider.isTrigger)
                {
                    boxCollider.isTrigger = false;
                    changed = true;
                }
            }

            if (createdPlacementBounds)
            {
                var serializedBounds = new SerializedObject(placementBounds);
                var useAttachedBox = serializedBounds.FindProperty("m_UseAttachedBoxCollider");
                if (useAttachedBox != null && !useAttachedBox.boolValue)
                {
                    useAttachedBox.boolValue = true;
                    serializedBounds.ApplyModifiedPropertiesWithoutUndo();
                    changed = true;
                }
            }

            return changed;
        }

        private static bool ColliderHasNoValidShape(BoxCollider boxCollider)
        {
            if (boxCollider == null)
                return true;

            Vector3 size = boxCollider.size;
            return size.x <= 0f || size.y <= 0f || size.z <= 0f;
        }

        private static bool TryCalculateBoundsFromColliders(Transform root, Transform excludeRoot, out Bounds localBounds)
        {
            var colliders = root.GetComponentsInChildren<Collider>(true);
            return TryCalculateLocalBounds(
                root,
                colliders,
                excludeRoot,
                collider => collider != null && collider.enabled && !collider.isTrigger,
                collider => collider.bounds,
                out localBounds);
        }

        private static bool TryCalculateBoundsFromRenderers(Transform root, Transform excludeRoot, out Bounds localBounds)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            return TryCalculateLocalBounds(
                root,
                renderers,
                excludeRoot,
                renderer =>
                    renderer != null &&
                    renderer.enabled &&
                    renderer is not ParticleSystemRenderer &&
                    renderer is not TrailRenderer &&
                    renderer is not LineRenderer,
                renderer => renderer.bounds,
                out localBounds);
        }

        private static bool TryCalculateLocalBounds<T>(
            Transform root,
            T[] components,
            Transform excludeRoot,
            System.Func<T, bool> predicate,
            System.Func<T, Bounds> readBounds,
            out Bounds localBounds)
            where T : Component
        {
            localBounds = default;
            if (root == null || components == null || components.Length == 0)
                return false;

            bool hasBounds = false;
            Matrix4x4 worldToLocal = root.worldToLocalMatrix;

            for (int i = 0; i < components.Length; i++)
            {
                T component = components[i];
                if (!predicate(component))
                    continue;

                if (excludeRoot != null && component.transform.IsChildOf(excludeRoot))
                    continue;

                Bounds componentBounds = readBounds(component);
                Vector3 min = componentBounds.min;
                Vector3 max = componentBounds.max;
                Vector3[] corners =
                {
                    new Vector3(min.x, min.y, min.z),
                    new Vector3(min.x, min.y, max.z),
                    new Vector3(min.x, max.y, min.z),
                    new Vector3(min.x, max.y, max.z),
                    new Vector3(max.x, min.y, min.z),
                    new Vector3(max.x, min.y, max.z),
                    new Vector3(max.x, max.y, min.z),
                    new Vector3(max.x, max.y, max.z),
                };

                for (int j = 0; j < corners.Length; j++)
                {
                    Vector3 localPoint = worldToLocal.MultiplyPoint3x4(corners[j]);
                    if (!hasBounds)
                    {
                        localBounds = new Bounds(localPoint, Vector3.zero);
                        hasBounds = true;
                    }
                    else
                    {
                        localBounds.Encapsulate(localPoint);
                    }
                }
            }

            return hasBounds;
        }

        private static Vector3 ClampSize(Vector3 size)
        {
            return new Vector3(
                Mathf.Max(size.x, 0.08f),
                Mathf.Max(size.y, 0.08f),
                Mathf.Max(size.z, 0.08f));
        }
    }
}
