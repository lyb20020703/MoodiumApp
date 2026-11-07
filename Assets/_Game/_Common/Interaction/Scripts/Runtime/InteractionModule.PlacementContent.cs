using System.Collections.Generic;
using UnityEngine;
using VFXViewer;

namespace Interaction
{
    public partial class InteractionModule
    {
        public void CollectPlacementContentRoots(List<GameObject> roots, HashSet<int> seen)
        {
            if (roots == null || seen == null)
                return;

            CollectPlacementContentRoots(initialEffectEntries, roots, seen);
            CollectPlacementContentRoots(appearEffectEntries, roots, seen);
            CollectPlacementContentRoots(disappearEffectEntries, roots, seen);
        }

        public bool IsPlacementContentRootProtected(GameObject root)
        {
            if (root == null)
                return false;

            Transform rootTransform = root.transform;
            if (rootTransform == null)
                return false;

            if (IsPlacementContentRootProtectedBy(rootTransform, ResolveLocalTouchInteractable(touchInteractable)))
                return true;
            if (IsPlacementContentRootProtectedBy(rootTransform, startLongPressInteractable))
                return true;
            if (IsPlacementContentRootProtectedBy(rootTransform, endLongPressInteractable))
                return true;
            if (IsPlacementContentRootProtectedBy(rootTransform, enterRegionCollider))
                return true;
            if (IsPlacementContentRootProtectedBy(rootTransform, leaveRegionCollider))
                return true;

            return false;
        }

        public bool HasPlacementHeavyPreviewContent()
        {
            var roots = new List<GameObject>();
            var seen = new HashSet<int>();
            CollectPlacementContentRoots(roots, seen);

            for (int i = 0; i < roots.Count; i++)
            {
                if (IsPlacementHeavyPreviewRoot(roots[i]))
                    return true;
            }

            return false;
        }

        public bool HasPlacementVideoPreviewContent()
        {
            var roots = new List<GameObject>();
            var seen = new HashSet<int>();
            CollectPlacementContentRoots(roots, seen);

            for (int i = 0; i < roots.Count; i++)
            {
                if (IsPlacementVideoRoot(roots[i]))
                    return true;
            }

            return false;
        }

        public bool HasPlacementPointCloudPreviewContent()
        {
            var roots = new List<GameObject>();
            var seen = new HashSet<int>();
            CollectPlacementContentRoots(roots, seen);

            for (int i = 0; i < roots.Count; i++)
            {
                if (IsPlacementPointCloudRoot(roots[i]))
                    return true;
            }

            return false;
        }

        internal void RefreshPlacementPreviewIfActive()
        {
            if (_isPlacementPreviewActive)
                ApplyPlacementPreviewVisibility();
        }

        private static void CollectPlacementContentRoots(
            List<AppearEffectEntry> entries,
            List<GameObject> roots,
            HashSet<int> seen)
        {
            if (entries == null)
                return;

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry == null || entry.targetType == EffectTargetType.Guide || entry.targetType == EffectTargetType.System || entry.contentRoot == null)
                    continue;

                int id = entry.contentRoot.GetInstanceID();
                if (seen.Add(id))
                    roots.Add(entry.contentRoot);
            }
        }

        private static void CollectPlacementContentRoots(
            List<DisappearEffectEntry> entries,
            List<GameObject> roots,
            HashSet<int> seen)
        {
            if (entries == null)
                return;

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry == null || entry.targetType == EffectTargetType.Guide || entry.targetType == EffectTargetType.System || entry.contentRoot == null)
                    continue;

                int id = entry.contentRoot.GetInstanceID();
                if (seen.Add(id))
                    roots.Add(entry.contentRoot);
            }
        }

        private static bool IsPlacementContentRootProtectedBy(Transform rootTransform, Component component)
        {
            if (rootTransform == null || component == null || component.transform == null)
                return false;

            Transform targetTransform = component.transform;
            return targetTransform == rootTransform || targetTransform.IsChildOf(rootTransform);
        }

        private static bool IsPlacementVideoRoot(GameObject root)
        {
            return root != null && ContentHandleFactory.DetectType(root) == ContentType.Video;
        }

        private static bool IsPlacementPointCloudRoot(GameObject root)
        {
            return root != null && root.GetComponentInChildren<PointCloudController>(true) != null;
        }

        private static bool IsPlacementHeavyPreviewRoot(GameObject root)
        {
            return IsPlacementVideoRoot(root) || IsPlacementPointCloudRoot(root);
        }

        private ExhibitInfo GetPlacementPreviewExhibitInfo()
        {
            return GetComponentInParent<ExhibitInfo>(true);
        }

        private bool ShouldHidePlacementContentForCurrentExhibit()
        {
            var placementManager = ExhibitPlacementManager.Instance;
            var exhibitInfo = GetPlacementPreviewExhibitInfo();
            return placementManager != null && placementManager.IsPlacementContentHidden(exhibitInfo);
        }

        private bool ShouldAllowPlacementVideoPreviewForCurrentExhibit()
        {
            var placementManager = ExhibitPlacementManager.Instance;
            var exhibitInfo = GetPlacementPreviewExhibitInfo();
            return placementManager != null && placementManager.ShouldAllowPlacementVideoPreview(exhibitInfo);
        }

        private bool ShouldAllowPlacementPointCloudPreviewForCurrentExhibit()
        {
            var placementManager = ExhibitPlacementManager.Instance;
            var exhibitInfo = GetPlacementPreviewExhibitInfo();
            return placementManager != null && placementManager.ShouldAllowPlacementPointCloudPreview(exhibitInfo);
        }

        private bool ShouldIncludePlacementPreviewContentRoot(GameObject root)
        {
            if (root == null)
                return false;

            var placementManager = ExhibitPlacementManager.Instance;
            bool placementModeActive = placementManager != null &&
                                       placementManager.CurrentAppMode == ExhibitAppMode.Placement;
            bool isProtected = IsPlacementContentRootProtected(root);
            if (ShouldHidePlacementContentForCurrentExhibit() && !isProtected)
                return false;

            if (placementModeActive && !isProtected)
            {
                bool hasVideo = IsPlacementVideoRoot(root);
                bool hasPointCloud = IsPlacementPointCloudRoot(root);
                bool allowVideo = hasVideo && ShouldAllowPlacementVideoPreviewForCurrentExhibit();
                bool allowPointCloud = hasPointCloud && ShouldAllowPlacementPointCloudPreviewForCurrentExhibit();

                if ((hasVideo || hasPointCloud) && !(allowVideo || allowPointCloud))
                    return false;
            }

            return true;
        }

        private bool ShouldActivatePlacementHeavyPreviewContent(GameObject root)
        {
            if (!IsPlacementHeavyPreviewRoot(root))
                return true;

            var placementManager = ExhibitPlacementManager.Instance;
            if (placementManager == null || placementManager.CurrentAppMode != ExhibitAppMode.Placement)
                return true;

            bool allowVideo = !IsPlacementVideoRoot(root) || ShouldAllowPlacementVideoPreviewForCurrentExhibit();
            bool allowPointCloud = !IsPlacementPointCloudRoot(root) || ShouldAllowPlacementPointCloudPreviewForCurrentExhibit();
            return allowVideo || allowPointCloud;
        }

        private bool ShouldActivatePlacementVideoPreviewContent(GameObject root)
        {
            if (!IsPlacementVideoRoot(root))
                return true;

            var placementManager = ExhibitPlacementManager.Instance;
            if (placementManager == null || placementManager.CurrentAppMode != ExhibitAppMode.Placement)
                return true;

            return ShouldAllowPlacementVideoPreviewForCurrentExhibit();
        }

        private bool ShouldActivatePlacementPointCloudPreviewContent(GameObject root)
        {
            if (!IsPlacementPointCloudRoot(root))
                return true;

            var placementManager = ExhibitPlacementManager.Instance;
            if (placementManager == null || placementManager.CurrentAppMode != ExhibitAppMode.Placement)
                return true;

            return ShouldAllowPlacementPointCloudPreviewForCurrentExhibit();
        }
    }
}
