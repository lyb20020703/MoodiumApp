using System;
using System.Collections.Generic;
using Interaction;
using UnityEngine;

namespace VFXViewer
{
    public partial class ExhibitPlacementManager
    {
        private sealed class ExhibitPlacementPreviewCache
        {
            public readonly Transform exhibitRoot;
            public readonly InteractionModule[] modules;
            public readonly PointCloudController[] pointCloudControllers;
            public bool hasComputedVideoPreviewContent;
            public bool hasVideoPreviewContent;
            public bool hasComputedPointCloudPreviewContent;
            public bool hasPointCloudPreviewContent;

            public ExhibitPlacementPreviewCache(Transform exhibitRoot)
            {
                this.exhibitRoot = exhibitRoot;
                modules = exhibitRoot != null
                    ? exhibitRoot.GetComponentsInChildren<InteractionModule>(true)
                    : Array.Empty<InteractionModule>();
                pointCloudControllers = exhibitRoot != null
                    ? exhibitRoot.GetComponentsInChildren<PointCloudController>(true)
                    : Array.Empty<PointCloudController>();
            }
        }

        private readonly Dictionary<int, ExhibitPlacementPreviewCache> m_ExhibitPlacementPreviewCaches =
            new Dictionary<int, ExhibitPlacementPreviewCache>();

        private readonly List<ExhibitInfo> m_PlacementHeavyPreviewAffectedExhibits = new List<ExhibitInfo>();
        private readonly HashSet<int> m_PlacementHeavyPreviewAffectedTransformIds = new HashSet<int>();

        public bool TryGetCurrentStepPlacementContentVisibilityState(
            out bool hasPlacedExhibit,
            out bool canToggle,
            out bool isHidden)
        {
            hasPlacedExhibit = false;
            canToggle = false;
            isHidden = false;

            string moduleId = GetCurrentSelectedModuleId();
            if (string.IsNullOrEmpty(moduleId))
            {
                LogDetailedPlacementResolution("visibility state skipped: current moduleId empty");
                return false;
            }

            Transform exhibitRoot = ResolveDeleteTargetForModule(moduleId);
            if (exhibitRoot == null && TryEnsureSessionPlacementRestoredByModuleId(moduleId))
                exhibitRoot = ResolveDeleteTargetForModule(moduleId);

            if (exhibitRoot == null)
            {
                if (TryGetSavedSessionPlacementContentVisibilityStateByModuleId(moduleId, out bool savedIsHidden))
                {
                    hasPlacedExhibit = true;
                    isHidden = savedIsHidden;
                }

                LogPlacementStateSnapshot(
                    "visibility state fallback",
                    moduleId,
                    null,
                    $"hasPlacedExhibit={hasPlacedExhibit}, canToggle={canToggle}, isHidden={isHidden}");
                LogDetailedPlacementResolution(
                    $"visibility state fallback: moduleId={moduleId}, hasPlaced={hasPlacedExhibit}, canToggle={canToggle}, isHidden={isHidden}");
                return true;
            }

            hasPlacedExhibit = true;
            if (!TryGetRegisteredExhibitInfoInHierarchy(exhibitRoot, out var info))
                return true;

            isHidden = info.placementContentHidden;
            canToggle = ExhibitHasHideablePlacementContent(exhibitRoot);
            LogPlacementStateSnapshot(
                "visibility state scene",
                moduleId,
                exhibitRoot,
                $"canToggle={canToggle}, isHidden={isHidden}");
            LogDetailedPlacementResolution(
                $"visibility state result: moduleId={moduleId}, target={DescribePlacementResolveTarget(exhibitRoot)}, canToggle={canToggle}, isHidden={isHidden}");
            return true;
        }

        public bool TryGetStagePlacementContentVisibilityState(
            string moduleId,
            out bool hasPlacedStage,
            out bool canToggle,
            out bool isHidden)
        {
            hasPlacedStage = false;
            canToggle = false;
            isHidden = false;

            moduleId = NormalizeModuleId(moduleId);
            if (string.IsNullOrEmpty(moduleId))
                return false;

            Transform stageRoot = ResolveDeleteTargetForModule(moduleId);
            if (stageRoot == null && TryEnsureSessionPlacementRestoredByModuleId(moduleId))
                stageRoot = ResolveDeleteTargetForModule(moduleId);

            if (stageRoot == null)
                return true;

            hasPlacedStage = true;
            if (!InteractionStage.TryFindByModuleId(moduleId, out var stage) || stage == null)
                return true;

            canToggle = stage.HasHideableStageContent;
            isHidden = canToggle && stage.IsStageContentHidden;
            return true;
        }

        private bool TryGetSavedSessionPlacementContentVisibilityStateByModuleId(string moduleId, out bool isHidden)
        {
            isHidden = false;
            moduleId = NormalizeModuleId(moduleId);
            if (string.IsNullOrEmpty(moduleId))
                return false;

            foreach (var entry in m_SessionSnapshotByInstanceId.Values)
            {
                if (entry == null)
                    continue;

                if (NormalizeModuleId(entry.moduleId) != moduleId)
                    continue;

                isHidden = entry.placementContentHidden;
                return true;
            }

            return false;
        }

        public bool TryToggleCurrentStepPlacementContentVisibility()
        {
            string moduleId = GetCurrentSelectedModuleId();
            LogPlacementStateSnapshot("visibility toggle click", moduleId, null, "action=toggleContentVisibility");
            if (string.IsNullOrEmpty(moduleId))
            {
                LogDetailedPlacementResolution("visibility toggle skipped: current moduleId empty");
                onStatusMessage?.Invoke("当前步骤无可切换内容。");
                return false;
            }

            Transform exhibitRoot = ResolveDeleteTargetForModule(moduleId);
            if (exhibitRoot == null && TryEnsureSessionPlacementRestoredByModuleId(moduleId))
                exhibitRoot = ResolveDeleteTargetForModule(moduleId);

            if (exhibitRoot == null)
            {
                LogDetailedPlacementResolution($"visibility toggle failed: moduleId={moduleId}, target=null");
                onStatusMessage?.Invoke("当前步骤尚未放置，无法切换内容显示。");
                return false;
            }

            if (!TryGetRegisteredExhibitInfoInHierarchy(exhibitRoot, out var info))
            {
                LogDetailedPlacementResolution(
                    $"visibility toggle failed missing info: moduleId={moduleId}, target={DescribePlacementResolveTarget(exhibitRoot)}");
                onStatusMessage?.Invoke("当前展品不是有效实例，无法切换内容显示。");
                return false;
            }

            if (!ExhibitHasHideablePlacementContent(exhibitRoot))
            {
                LogDetailedPlacementResolution(
                    $"visibility toggle failed no hideable content: moduleId={moduleId}, target={DescribePlacementResolveTarget(exhibitRoot)}");
                LogPlacementStateSnapshot("visibility toggle failed noHideableContent", moduleId, exhibitRoot, "result=false");
                onStatusMessage?.Invoke("当前展品没有可隐藏的显示内容。");
                return false;
            }

            bool nextHidden = !info.placementContentHidden;
            if (!info.SetPlacementContentHidden(nextHidden))
            {
                LogDetailedPlacementResolution(
                    $"visibility toggle skipped unchanged: moduleId={moduleId}, target={DescribePlacementResolveTarget(exhibitRoot)}, nextHidden={nextHidden}");
                return false;
            }

            RefreshPlacementContentVisibilityForCurrentMode();
            QueueSaveToFiles(saveLayout: true, suppressStatusMessage: true);
            LogPlacementStateSnapshot("visibility toggle success", moduleId, exhibitRoot, $"nextHidden={nextHidden}");
            LogDetailedPlacementResolution(
                $"visibility toggle success: moduleId={moduleId}, target={DescribePlacementResolveTarget(exhibitRoot)}, nextHidden={nextHidden}");
            onStatusMessage?.Invoke(nextHidden ? "已隐藏当前内容" : "已显示当前内容");
            return true;
        }

        public bool TryToggleStagePlacementContentVisibility(string moduleId)
        {
            moduleId = NormalizeModuleId(moduleId);
            if (string.IsNullOrEmpty(moduleId))
            {
                onStatusMessage?.Invoke("当前展台无可切换内容。");
                return false;
            }

            Transform stageRoot = ResolveDeleteTargetForModule(moduleId);
            if (stageRoot == null && TryEnsureSessionPlacementRestoredByModuleId(moduleId))
                stageRoot = ResolveDeleteTargetForModule(moduleId);

            if (stageRoot == null)
            {
                onStatusMessage?.Invoke("当前展台尚未放置，无法切换内容显示。");
                return false;
            }

            if (!InteractionStage.TryFindByModuleId(moduleId, out var stage) || stage == null)
            {
                onStatusMessage?.Invoke("当前展台缺少有效的 InteractionStage，无法切换内容显示。");
                return false;
            }

            if (!stage.HasHideableStageContent)
            {
                onStatusMessage?.Invoke("当前展台没有可隐藏的展台内容。");
                return false;
            }

            bool nextHidden = !stage.IsStageContentHidden;
            if (!stage.TrySetStageContentVisible(!nextHidden))
                return false;

            onStatusMessage?.Invoke(nextHidden ? "已隐藏当前展台内容" : "已显示当前展台内容");
            return true;
        }

        private void RefreshPlacementContentVisibilityForCurrentMode()
        {
            ApplyPlacementInteractorLayerFiltering();
            RefreshPlacementOnlyCollidersInScene();

            if (m_CurrentAppMode == ExhibitAppMode.Placement && m_EnablePlacementPreviewInPlacementMode)
            {
                RefreshPlacementHeavyPreviewOwner(forceApply: true);
                RefreshCurrentExhibitManipulationState();
                InteractionModule.ForceRefreshPlacementGuidePathPreviews();
                LogPlacementDiagnostics("after RefreshPlacementContentVisibilityForCurrentMode");
                return;
            }

            RefreshPlacementPreviewForCurrentMode();
        }

        internal bool IsPlacementContentHidden(ExhibitInfo info)
        {
            return m_CurrentAppMode == ExhibitAppMode.Placement &&
                   info != null &&
                   info.placementContentHidden;
        }

        internal bool ShouldAllowPlacementVideoPreview(ExhibitInfo info)
        {
            return m_CurrentAppMode == ExhibitAppMode.Placement &&
                   m_EnablePlacementPreviewInPlacementMode &&
                   info != null &&
                   !info.placementContentHidden &&
                   !string.IsNullOrWhiteSpace(m_PlacementVideoPreviewOwnerInstanceId) &&
                   string.Equals(info.instanceID, m_PlacementVideoPreviewOwnerInstanceId, StringComparison.Ordinal);
        }

        internal bool ShouldAllowPlacementPointCloudPreview(ExhibitInfo info)
        {
            return m_CurrentAppMode == ExhibitAppMode.Placement &&
                   m_EnablePlacementPreviewInPlacementMode &&
                   info != null &&
                   !info.placementContentHidden &&
                   !string.IsNullOrWhiteSpace(m_PlacementPointCloudPreviewOwnerInstanceId) &&
                   string.Equals(info.instanceID, m_PlacementPointCloudPreviewOwnerInstanceId, StringComparison.Ordinal);
        }

        private void RefreshPlacementHeavyPreviewOwner(bool forceApply = false)
        {
            string previousVideoOwnerInstanceId = m_PlacementVideoPreviewOwnerInstanceId;
            string previousPointCloudOwnerInstanceId = m_PlacementPointCloudPreviewOwnerInstanceId;

            ExhibitInfo previousVideoOwner = TryGetPlacementVideoPreviewOwner();
            ExhibitInfo previousPointCloudOwner = TryGetPlacementPointCloudPreviewOwner();
            ExhibitInfo nextVideoOwner = null;
            ExhibitInfo nextPointCloudOwner = null;
            TryGetCurrentSelectedExhibitInfo(out var selectedInfo);

            if (m_CurrentAppMode == ExhibitAppMode.Placement && m_EnablePlacementPreviewInPlacementMode)
            {
                if (IsValidPlacementVideoPreviewCandidate(previousVideoOwner))
                    nextVideoOwner = previousVideoOwner;

                if (IsValidPlacementPointCloudPreviewCandidate(previousPointCloudOwner))
                    nextPointCloudOwner = previousPointCloudOwner;

                if (IsValidPlacementVideoPreviewCandidate(selectedInfo))
                    nextVideoOwner = selectedInfo;

                if (IsValidPlacementPointCloudPreviewCandidate(selectedInfo))
                    nextPointCloudOwner = selectedInfo;
            }

            m_PlacementVideoPreviewOwnerInstanceId =
                nextVideoOwner != null && !string.IsNullOrWhiteSpace(nextVideoOwner.instanceID)
                    ? nextVideoOwner.instanceID.Trim()
                    : string.Empty;

            m_PlacementPointCloudPreviewOwnerInstanceId =
                nextPointCloudOwner != null && !string.IsNullOrWhiteSpace(nextPointCloudOwner.instanceID)
                    ? nextPointCloudOwner.instanceID.Trim()
                    : string.Empty;

            bool videoOwnerChanged = !string.Equals(
                previousVideoOwnerInstanceId,
                m_PlacementVideoPreviewOwnerInstanceId,
                StringComparison.Ordinal);

            bool pointCloudOwnerChanged = !string.Equals(
                previousPointCloudOwnerInstanceId,
                m_PlacementPointCloudPreviewOwnerInstanceId,
                StringComparison.Ordinal);

            if (!videoOwnerChanged && !pointCloudOwnerChanged && !forceApply)
                return;

            ApplyPlacementHeavyPreviewOwnerToPlacementPreviews(
                previousVideoOwner,
                nextVideoOwner,
                previousPointCloudOwner,
                nextPointCloudOwner,
                selectedInfo,
                forceApply);
        }

        private void ApplyPlacementHeavyPreviewOwnerToPlacementPreviews(
            ExhibitInfo previousVideoOwner,
            ExhibitInfo nextVideoOwner,
            ExhibitInfo previousPointCloudOwner,
            ExhibitInfo nextPointCloudOwner,
            ExhibitInfo selectedInfo,
            bool forceApply)
        {
            if (m_CurrentAppMode != ExhibitAppMode.Placement || !m_EnablePlacementPreviewInPlacementMode)
                return;

            CollectAffectedPlacementHeavyPreviewExhibits(
                previousVideoOwner,
                nextVideoOwner,
                previousPointCloudOwner,
                nextPointCloudOwner,
                selectedInfo,
                forceApply);

            for (int i = 0; i < m_PlacementHeavyPreviewAffectedExhibits.Count; i++)
            {
                var info = m_PlacementHeavyPreviewAffectedExhibits[i];
                if (info == null || info.transform == null)
                    continue;

                if (!ShouldAllowPlacementPointCloudPreview(info))
                    SuppressPlacementPointCloudPreview(info.transform);
            }

            for (int i = 0; i < m_PlacementHeavyPreviewAffectedExhibits.Count; i++)
            {
                var info = m_PlacementHeavyPreviewAffectedExhibits[i];
                if (info == null || info.transform == null)
                    continue;

                ApplyPlacementHeavyPreviewOwnerToExhibit(info.transform);
            }
        }

        private bool ShouldAllowPlacementPointCloudPreviewForExhibit(Transform exhibitRoot)
        {
            return TryGetRegisteredExhibitInfoInHierarchy(exhibitRoot, out var info) &&
                   ShouldAllowPlacementPointCloudPreview(info);
        }

        private void SuppressPlacementPointCloudPreview(Transform exhibitRoot)
        {
            if (exhibitRoot == null)
                return;

            var controllers = GetExhibitPlacementPreviewCache(exhibitRoot).pointCloudControllers;
            for (int i = 0; i < controllers.Length; i++)
            {
                var controller = controllers[i];
                if (controller == null)
                    continue;

                controller.HideForPlacementPreview();
            }
        }

        private ExhibitInfo TryGetPlacementVideoPreviewOwner()
        {
            if (string.IsNullOrWhiteSpace(m_PlacementVideoPreviewOwnerInstanceId))
                return null;

            if (TryGetRegisteredExhibitByInstanceId(m_PlacementVideoPreviewOwnerInstanceId, out var info))
                return info;

            return null;
        }

        private ExhibitInfo TryGetPlacementPointCloudPreviewOwner()
        {
            if (string.IsNullOrWhiteSpace(m_PlacementPointCloudPreviewOwnerInstanceId))
                return null;

            if (TryGetRegisteredExhibitByInstanceId(m_PlacementPointCloudPreviewOwnerInstanceId, out var info))
                return info;

            return null;
        }

        private bool TryGetCurrentSelectedExhibitInfo(out ExhibitInfo info)
        {
            info = null;

            string moduleId = GetCurrentSelectedModuleId();
            if (string.IsNullOrWhiteSpace(moduleId))
                return false;

            Transform exhibitRoot = ResolveDeleteTargetForModule(moduleId);
            if (exhibitRoot == null)
                return false;

            return TryGetRegisteredExhibitInfoInHierarchy(exhibitRoot, out info);
        }

        private bool TryGetRegisteredExhibitInfoInHierarchy(Transform target, out ExhibitInfo info)
        {
            info = null;
            for (Transform current = target; current != null; current = current.parent)
            {
                if (TryGetRegisteredExhibitInfo(current, out info))
                    return true;
            }

            return false;
        }

        private bool IsValidPlacementVideoPreviewCandidate(ExhibitInfo info)
        {
            return info != null &&
                   info.transform != null &&
                   !info.placementContentHidden &&
                   ExhibitHasPlacementVideoPreviewContent(info.transform);
        }

        private bool IsValidPlacementPointCloudPreviewCandidate(ExhibitInfo info)
        {
            return info != null &&
                   info.transform != null &&
                   !info.placementContentHidden &&
                   ExhibitHasPlacementPointCloudPreviewContent(info.transform);
        }

        private bool ExhibitHasPlacementVideoPreviewContent(Transform exhibitRoot)
        {
            if (exhibitRoot == null)
                return false;

            var cache = GetExhibitPlacementPreviewCache(exhibitRoot);
            if (cache.hasComputedVideoPreviewContent)
                return cache.hasVideoPreviewContent;

            bool hasVideoPreviewContent = false;
            for (int i = 0; i < cache.modules.Length; i++)
            {
                var module = cache.modules[i];
                if (module == null || !module.HasPlacementVideoPreviewContent())
                    continue;

                hasVideoPreviewContent = true;
                break;
            }

            cache.hasComputedVideoPreviewContent = true;
            cache.hasVideoPreviewContent = hasVideoPreviewContent;
            return hasVideoPreviewContent;
        }

        private bool ExhibitHasPlacementPointCloudPreviewContent(Transform exhibitRoot)
        {
            if (exhibitRoot == null)
                return false;

            var cache = GetExhibitPlacementPreviewCache(exhibitRoot);
            if (cache.hasComputedPointCloudPreviewContent)
                return cache.hasPointCloudPreviewContent;

            bool hasPointCloudPreviewContent = false;
            for (int i = 0; i < cache.modules.Length; i++)
            {
                var module = cache.modules[i];
                if (module == null || !module.HasPlacementPointCloudPreviewContent())
                    continue;

                hasPointCloudPreviewContent = true;
                break;
            }

            cache.hasComputedPointCloudPreviewContent = true;
            cache.hasPointCloudPreviewContent = hasPointCloudPreviewContent;
            return hasPointCloudPreviewContent;
        }

        private bool ExhibitHasHideablePlacementContent(Transform exhibitRoot)
        {
            if (exhibitRoot == null)
                return false;

            var modules = GetExhibitPlacementPreviewCache(exhibitRoot).modules;
            if (modules == null || modules.Length == 0)
                return false;

            var contentRoots = new List<GameObject>();
            var seenContentRootIds = new HashSet<int>();

            for (int i = 0; i < modules.Length; i++)
            {
                var module = modules[i];
                if (module == null)
                    continue;

                module.CollectPlacementContentRoots(contentRoots, seenContentRootIds);
            }

            for (int rootIndex = 0; rootIndex < contentRoots.Count; rootIndex++)
            {
                var root = contentRoots[rootIndex];
                if (root == null)
                    continue;

                bool isProtected = false;
                for (int moduleIndex = 0; moduleIndex < modules.Length; moduleIndex++)
                {
                    var module = modules[moduleIndex];
                    if (module == null)
                        continue;

                    if (!module.IsPlacementContentRootProtected(root))
                        continue;

                    isProtected = true;
                    break;
                }

                if (!isProtected)
                    return true;
            }

            return false;
        }

        private void ApplyPlacementHeavyPreviewOwnerToExhibit(Transform exhibitRoot)
        {
            if (exhibitRoot == null)
                return;

            if (ShouldAllowPlacementPointCloudPreviewForExhibit(exhibitRoot) &&
                ExhibitHasPlacementPointCloudPreviewContent(exhibitRoot))
            {
                EnablePlacementPreviewForExhibitInPlacementMode(exhibitRoot);
                return;
            }

            if (HasActivePlacementPreview(exhibitRoot))
                RefreshActivePlacementPreviewForExhibit(exhibitRoot);
            else
                SetPlacementPreviewEnabledForExhibit(exhibitRoot, true);
        }

        private void CollectAffectedPlacementHeavyPreviewExhibits(
            ExhibitInfo previousVideoOwner,
            ExhibitInfo nextVideoOwner,
            ExhibitInfo previousPointCloudOwner,
            ExhibitInfo nextPointCloudOwner,
            ExhibitInfo selectedInfo,
            bool forceApply)
        {
            m_PlacementHeavyPreviewAffectedExhibits.Clear();
            m_PlacementHeavyPreviewAffectedTransformIds.Clear();

            AddAffectedPlacementHeavyPreviewExhibit(previousVideoOwner);
            AddAffectedPlacementHeavyPreviewExhibit(nextVideoOwner);
            AddAffectedPlacementHeavyPreviewExhibit(previousPointCloudOwner);
            AddAffectedPlacementHeavyPreviewExhibit(nextPointCloudOwner);

            if (forceApply)
                AddAffectedPlacementHeavyPreviewExhibit(selectedInfo);
        }

        private void AddAffectedPlacementHeavyPreviewExhibit(ExhibitInfo info)
        {
            if (info == null || info.transform == null)
                return;

            int transformId = info.transform.GetInstanceID();
            if (!m_PlacementHeavyPreviewAffectedTransformIds.Add(transformId))
                return;

            m_PlacementHeavyPreviewAffectedExhibits.Add(info);
        }

        private ExhibitPlacementPreviewCache GetExhibitPlacementPreviewCache(Transform exhibitRoot)
        {
            if (exhibitRoot == null)
                return new ExhibitPlacementPreviewCache(null);

            int transformId = exhibitRoot.GetInstanceID();
            if (m_ExhibitPlacementPreviewCaches.TryGetValue(transformId, out var cache) &&
                cache.exhibitRoot == exhibitRoot)
            {
                return cache;
            }

            cache = new ExhibitPlacementPreviewCache(exhibitRoot);
            m_ExhibitPlacementPreviewCaches[transformId] = cache;
            return cache;
        }

        private void InvalidateExhibitPlacementPreviewCache(Transform exhibitRoot)
        {
            if (exhibitRoot == null)
                return;

            m_ExhibitPlacementPreviewCaches.Remove(exhibitRoot.GetInstanceID());
        }

        private void InvalidateExhibitPlacementPreviewCache(int transformId)
        {
            if (transformId == 0)
                return;

            m_ExhibitPlacementPreviewCaches.Remove(transformId);
        }

        private void ClearExhibitPlacementPreviewCaches()
        {
            m_ExhibitPlacementPreviewCaches.Clear();
            m_PlacementHeavyPreviewAffectedExhibits.Clear();
            m_PlacementHeavyPreviewAffectedTransformIds.Clear();
        }
    }
}
