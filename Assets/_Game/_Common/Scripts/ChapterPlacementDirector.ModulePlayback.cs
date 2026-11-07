using System;
using System.Collections.Generic;
using Interaction;
using UnityEngine;

namespace VFXViewer
{
    public partial class ChapterPlacementDirector
    {
        private sealed class ModulePlaybackFlow
        {
            private static readonly bool EnableRootPlaybackTransitionDiagnostics = false;
            private readonly ChapterPlacementDirector m_Owner;
            private bool m_HasDeferredRootStepModuleStart;
            private string m_DeferredRootStepModuleId = string.Empty;

            public ModulePlaybackFlow(ChapterPlacementDirector owner)
            {
                m_Owner = owner;
            }

            public void OnEnable()
            {
                AttachCallbacks();
            }

            public void OnDisable()
            {
                DetachCallbacks();
            }

            public void EnterModuleOnlyTestMode()
            {
                AttachCallbacks();
                SkipAlreadyCompletedPrefixByModules();
                m_Owner.PublishStatus("当前为模块测试模式：未绑定 Spawner，将按 InteractionModule 完成事件自动推进步骤。");
                TryShowCurrentModule();
            }

            public void AttachCallbacks()
            {
                if (!m_Owner.IsModuleDrivenVisibilityActive) return;
                if (!EnsureModuleManagerForCurrentMode()) return;
                if (m_Owner.m_ModuleManager == null) return;

                m_Owner.m_ModuleManager.ModuleCompleted -= HandleModuleCompleted;
                m_Owner.m_ModuleManager.ModuleCompleted += HandleModuleCompleted;
                m_Owner.m_ModuleManager.RegistryChanged -= HandleModuleRegistryChanged;
                m_Owner.m_ModuleManager.RegistryChanged += HandleModuleRegistryChanged;
            }

            public void DetachCallbacks()
            {
                if (m_Owner.m_ModuleManager == null) return;
                m_Owner.m_ModuleManager.ModuleCompleted -= HandleModuleCompleted;
                m_Owner.m_ModuleManager.RegistryChanged -= HandleModuleRegistryChanged;
            }

            public bool EnsureModuleManagerForCurrentMode()
            {
                if (m_Owner.m_ModuleManager == null && m_Owner.m_Spawner != null)
                    m_Owner.m_ModuleManager = m_Owner.m_Spawner.GetOrCreateRuntimeModuleManager();

                if (m_Owner.m_ModuleManager == null)
                    m_Owner.m_ModuleManager = FindFirstObjectByType<InteractionModuleManager>(FindObjectsInactive.Include);

                if (m_Owner.m_ModuleManager == null &&
                    m_Owner.IsExperienceMode &&
                    m_Owner.m_Spawner != null &&
                    m_Owner.m_AutoCreateModuleManagerForRestoredPlayback)
                {
                    m_Owner.m_ModuleManager = m_Owner.m_Spawner.GetOrCreateRuntimeModuleManager();
                }

                if (m_Owner.m_ModuleManager == null &&
                    m_Owner.IsExperienceMode &&
                    m_Owner.m_AutoCreateModuleManagerForRestoredPlayback)
                {
                    var host = new GameObject("InteractionModuleManager");
                    m_Owner.m_ModuleManager = host.AddComponent<InteractionModuleManager>();
                }

                return m_Owner.m_ModuleManager != null;
            }

            public void RefreshRegistry()
            {
                if (m_Owner.m_ModuleManager == null) return;
                m_Owner.m_ModuleManager.RefreshRegistry();
            }

            public void HideAllModules(bool includeStageModules = false)
            {
                if (!EnsureModuleManagerForCurrentMode()) return;
                if (m_Owner.m_ModuleManager == null) return;
                RefreshRegistry();

                var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var hiddenTargets = new HashSet<int>();
                for (int flat = 0; flat < m_Owner.m_FlatSteps.Count; flat++)
                {
                    if (!m_Owner.TryGetStepByFlatIndex(flat, out var step)) continue;
                    string moduleId = NormalizeModuleId(step.moduleId);
                    if (string.IsNullOrEmpty(moduleId)) continue;
                    if (!includeStageModules && m_Owner.IsStageModuleId(moduleId)) continue;
                    if (!visited.Add(moduleId)) continue;
                    if (!m_Owner.m_ModuleManager.TryGetModuleById(moduleId, out var module) || module == null) continue;

                    var target = ResolvePlaybackTarget(module);
                    if (target != null && IsPlaybackTargetVisible(moduleId, target))
                    {
                        if (!hiddenTargets.Add(target.GetInstanceID()))
                            continue;

                        TrySetPlaybackTargetVisible(moduleId, target, false, "hide_all_modules");
                    }
                }

                if (!includeStageModules)
                    return;

                var stageModuleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                m_Owner.CollectStageModuleIds(stageModuleIds);
                foreach (var stageModuleId in stageModuleIds)
                {
                    if (!TryResolvePlaybackTargetByModuleId(stageModuleId, out var target))
                        continue;

                    if (target != null && target.activeSelf && hiddenTargets.Add(target.GetInstanceID()))
                        TrySetPlaybackTargetVisible(stageModuleId, target, false, "hide_all_modules_stage");
                }
            }

            public void SkipCompletedOrUnavailableRootStepAtPlaybackStart()
            {
                if (!TryGetCurrentRootStepForPlayback(out int rootFlatIndex, out var rootStep))
                    return;

                string rootModuleId = NormalizeModuleId(rootStep != null ? rootStep.moduleId : string.Empty);
                if (string.IsNullOrEmpty(rootModuleId))
                {
                    SkipCurrentRootStep("定位点步骤未配置有效的 moduleId，已跳过 Root 开场。", rootFlatIndex);
                    return;
                }

                if (m_Owner.m_HasCompletedRootStepPlaybackThisSession)
                {
                    SkipCurrentRootStep("本次会话已播放过定位点开场，直接进入第一个展品。", rootFlatIndex);
                    return;
                }

                if (m_Owner.m_ModuleManager == null ||
                    !m_Owner.m_ModuleManager.TryGetModuleById(rootModuleId, out var module) ||
                    module == null)
                {
                    SkipCurrentRootStep(
                        $"未找到 Root 开场模块（moduleId={rootModuleId}），已跳过定位点开场并继续后续展品。",
                        rootFlatIndex);
                }
            }

            public void TryShowCurrentModule()
            {
                if (!m_Owner.IsModuleDrivenVisibilityActive) return;
                if (!m_Owner.m_AutoShowCurrentModuleOnStepEntered) return;
                if (m_Owner.m_ModuleManager == null) return;
                if (m_Owner.m_CurrentGroupIndex < 0 || m_Owner.m_CurrentGroupIndex >= m_Owner.m_RuntimeGroups.Count) return;
                if (m_Owner.IsRestoredModulePlaybackActive &&
                    m_Owner.m_Spawner != null &&
                    m_Owner.m_Spawner.HasAnchorPoseConflict)
                {
                    HideAllModules(includeStageModules: true);
                    return;
                }

                var group = m_Owner.m_RuntimeGroups[m_Owner.m_CurrentGroupIndex];
                var currentGroupModuleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < group.Count; i++)
                {
                    if (!m_Owner.TryGetStepByFlatIndex(group[i], out var step)) continue;
                    string moduleId = NormalizeModuleId(step.moduleId);
                    if (string.IsNullOrEmpty(moduleId)) continue;
                    currentGroupModuleIds.Add(moduleId);
                }

                var stageModuleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                m_Owner.CollectStageModuleIds(stageModuleIds);

                PlacementDebugFileLogger.Log(
                    $"[PlaybackFlow] show_current_group groupIndex={m_Owner.m_CurrentGroupIndex}, " +
                    $"flatIndex={m_Owner.m_CurrentFlatIndex}, modules={string.Join("|", currentGroupModuleIds)}, " +
                    $"stageModules={string.Join("|", stageModuleIds)}, autoHideNonCurrent={m_Owner.m_AutoHideNonCurrentModulesInModuleMode}, " +
                    $"restoredPlayback={m_Owner.IsRestoredModulePlaybackActive}, " +
                    $"hasAnchorPoseConflict={(m_Owner.m_Spawner != null && m_Owner.m_Spawner.HasAnchorPoseConflict)}");

                if (m_Owner.m_AutoHideNonCurrentModulesInModuleMode)
                {
                    var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var targetActiveStates = new Dictionary<int, (GameObject target, bool shouldBeActive)>();
                    for (int flat = 0; flat < m_Owner.m_FlatSteps.Count; flat++)
                    {
                        if (!m_Owner.TryGetStepByFlatIndex(flat, out var step)) continue;
                        string moduleId = NormalizeModuleId(step.moduleId);
                        if (string.IsNullOrEmpty(moduleId)) continue;
                        if (!visited.Add(moduleId)) continue;
                        if (!m_Owner.m_ModuleManager.TryGetModuleById(moduleId, out var module) || module == null) continue;

                        var target = ResolvePlaybackTarget(module);
                        if (target == null) continue;

                        bool shouldBeActive = currentGroupModuleIds.Contains(moduleId);
                        int targetId = target.GetInstanceID();
                        if (targetActiveStates.TryGetValue(targetId, out var existing))
                        {
                            targetActiveStates[targetId] = (existing.target, existing.shouldBeActive || shouldBeActive);
                            continue;
                        }

                        targetActiveStates[targetId] = (target, shouldBeActive);
                    }

                    foreach (var stageModuleId in stageModuleIds)
                    {
                        if (!visited.Add(stageModuleId))
                            continue;

                        if (!TryResolvePlaybackTargetByModuleId(stageModuleId, out var target))
                            continue;

                        int targetId = target.GetInstanceID();
                        if (targetActiveStates.TryGetValue(targetId, out var existing))
                        {
                            targetActiveStates[targetId] = (existing.target, true);
                            continue;
                        }

                        targetActiveStates[targetId] = (target, true);
                    }

                    foreach (var kv in targetActiveStates)
                    {
                        var (target, shouldBeActive) = kv.Value;
                        if (target != null &&
                            IsPlaybackTargetVisible(ResolvePlaybackTargetModuleId(target), target) != shouldBeActive)
                        {
                            TrySetPlaybackTargetVisible(
                                ResolvePlaybackTargetModuleId(target),
                                target,
                                shouldBeActive,
                                "show_current_group");
                        }
                    }
                    return;
                }

                var shownTargets = new HashSet<int>();
                foreach (var moduleId in currentGroupModuleIds)
                {
                    if (!m_Owner.m_ModuleManager.TryGetModuleById(moduleId, out var module) || module == null) continue;
                    var target = ResolvePlaybackTarget(module);
                    if (target != null &&
                        shownTargets.Add(target.GetInstanceID()) &&
                        !IsPlaybackTargetVisible(moduleId, target))
                    {
                        TrySetPlaybackTargetVisible(moduleId, target, true, "show_current_module");
                    }
                }

                foreach (var stageModuleId in stageModuleIds)
                {
                    if (!TryResolvePlaybackTargetByModuleId(stageModuleId, out var target))
                        continue;

                    if (target != null &&
                        shownTargets.Add(target.GetInstanceID()) &&
                        !target.activeSelf)
                    {
                        TrySetPlaybackTargetVisible(stageModuleId, target, true, "show_stage_module");
                    }
                }
            }

            public void DeferRootStepModuleStartUntilScenePlaybackReady()
            {
                m_HasDeferredRootStepModuleStart = false;
                m_DeferredRootStepModuleId = string.Empty;

                if (!m_Owner.TryGetRootStepFlatIndex(out int rootFlatIndex) ||
                    !m_Owner.TryGetStepByFlatIndex(rootFlatIndex, out var rootStep) ||
                    rootStep == null)
                    return;

                string rootModuleId = NormalizeModuleId(rootStep.moduleId);
                if (string.IsNullOrEmpty(rootModuleId))
                    return;

                if (!TryResolveModuleById(rootModuleId, out var module) || module == null)
                {
                    if (EnableRootPlaybackTransitionDiagnostics)
                    {
                        PlacementDebugFileLogger.Log(
                            $"[PlaybackFlow] defer_root_step_start_skipped moduleId={rootModuleId}, reason=module_not_found");
                    }
                    return;
                }

                m_HasDeferredRootStepModuleStart = true;
                m_DeferredRootStepModuleId = rootModuleId;

                if (EnableRootPlaybackTransitionDiagnostics)
                {
                    PlacementDebugFileLogger.Log(
                        $"[PlaybackFlow] defer_root_step_start moduleId={rootModuleId}, " +
                        $"phase={module.CurrentPhase}, moduleEnabled={module.enabled}, " +
                        $"targetActiveSelf={module.gameObject.activeSelf}, targetActiveInHierarchy={module.gameObject.activeInHierarchy}");
                }

                if (module.enabled)
                    module.enabled = false;
            }

            public void StartDeferredCurrentRootStepModuleAfterPlaybackReady()
            {
                if (!m_HasDeferredRootStepModuleStart ||
                    string.IsNullOrWhiteSpace(m_DeferredRootStepModuleId))
                {
                    return;
                }

                if (!TryGetCurrentRootStepForPlayback(out _, out var rootStep) ||
                    rootStep == null ||
                    !string.Equals(
                        NormalizeModuleId(rootStep.moduleId),
                        m_DeferredRootStepModuleId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    m_HasDeferredRootStepModuleStart = false;
                    m_DeferredRootStepModuleId = string.Empty;
                    return;
                }

                if (!TryResolveModuleById(m_DeferredRootStepModuleId, out var module) ||
                    module == null ||
                    !module.gameObject.activeInHierarchy)
                {
                    if (EnableRootPlaybackTransitionDiagnostics)
                    {
                        PlacementDebugFileLogger.Log(
                            $"[PlaybackFlow] start_deferred_root_step_after_ready_skipped moduleId={m_DeferredRootStepModuleId}, reason=module_not_ready");
                    }
                    return;
                }

                if (EnableRootPlaybackTransitionDiagnostics)
                {
                    PlacementDebugFileLogger.Log(
                        $"[PlaybackFlow] start_deferred_root_step_after_ready moduleId={m_DeferredRootStepModuleId}, " +
                        $"phase={module.CurrentPhase}, moduleEnabled={module.enabled}, " +
                        $"targetActiveSelf={module.gameObject.activeSelf}, targetActiveInHierarchy={module.gameObject.activeInHierarchy}");
                }

                if (!module.enabled)
                    module.enabled = true;

                m_Owner.m_HasStartedRootStepPlaybackThisSession = true;

                m_HasDeferredRootStepModuleStart = false;
                m_DeferredRootStepModuleId = string.Empty;
            }

            private void SkipAlreadyCompletedPrefixByModules()
            {
                if (m_Owner.m_ModuleManager == null) return;
                if (m_Owner.m_RuntimeGroups.Count == 0) return;

                int firstUnfinishedGroup = -1;
                for (int g = 0; g < m_Owner.m_RuntimeGroups.Count; g++)
                {
                    var group = m_Owner.m_RuntimeGroups[g];
                    bool allCompleted = true;
                    for (int i = 0; i < group.Count; i++)
                    {
                        int flatIndex = group[i];
                        bool completed = IsStepCompletedByModuleState(flatIndex);
                        if (completed)
                        {
                            m_Owner.m_PlaybackCompletedSteps.Add(flatIndex);
                        }
                        else
                        {
                            allCompleted = false;
                        }
                    }

                    if (!allCompleted)
                    {
                        firstUnfinishedGroup = g;
                        break;
                    }
                }

                if (firstUnfinishedGroup < 0)
                {
                    m_Owner.m_CurrentGroupIndex = m_Owner.m_RuntimeGroups.Count - 1;
                    var lastGroup = m_Owner.m_RuntimeGroups[m_Owner.m_CurrentGroupIndex];
                    m_Owner.m_CurrentFlatIndex = lastGroup[lastGroup.Count - 1];
                    return;
                }

                m_Owner.m_CurrentGroupIndex = firstUnfinishedGroup;
                var currentGroup = m_Owner.m_RuntimeGroups[m_Owner.m_CurrentGroupIndex];
                m_Owner.m_CurrentFlatIndex = currentGroup[0];
            }

            private void HandleModuleRegistryChanged()
            {
                if (!m_Owner.IsModuleDrivenVisibilityActive) return;
                if (m_Owner.IsModuleOnlyTestModeActive)
                    SkipAlreadyCompletedPrefixByModules();
                TryShowCurrentModule();
                m_Owner.PublishGuide();
            }

            private void HandleModuleCompleted(InteractionModule module, string completedModuleId)
            {
                if (module == null || !m_Owner.IsModuleDrivenVisibilityActive) return;
                completedModuleId = NormalizeModuleId(completedModuleId);
                if (string.IsNullOrEmpty(completedModuleId)) return;
                if (!m_Owner.m_FlatIndexByModuleId.TryGetValue(completedModuleId, out int flatIndex)) return;
                if (!m_Owner.TryResolveFlatStep(flatIndex, out var chapter, out var step)) return;

                int groupIndex = m_Owner.GetGroupIndexByFlatIndex(flatIndex);
                if (groupIndex != m_Owner.m_CurrentGroupIndex)
                    return;

                m_Owner.m_PlaybackCompletedSteps.Add(flatIndex);
                if (m_Owner.IsRootStep(step))
                {
                    m_Owner.m_HasCompletedRootStepPlaybackThisSession = true;
                    m_Owner.m_Spawner?.TrySetRootPresentationVisibleForPlayback(false);
                }
                m_Owner.PublishStatus($"模块完成：{chapter.DisplayName} / {step.DisplayName}");

                if (m_Owner.IsCurrentGroupCompleted())
                {
                    if (m_Owner.IsRestoredModulePlaybackActive)
                    {
                        m_Owner.PreloadNextChapterPlacementsIfNeeded();
                        RefreshRegistry();
                        TryShowCurrentModule();
                    }

                    if (!m_Owner.MoveToNextGroup())
                    {
                        m_Owner.PublishStatus($"全部步骤完成：{m_Owner.m_PlaybackCompletedSteps.Count}/{m_Owner.m_FlatSteps.Count}");
                        return;
                    }

                    if (m_Owner.IsRestoredModulePlaybackActive)
                    {
                        m_Owner.TryPrepareCurrentChapterPlacements(out _, out _);
                        RefreshRegistry();
                    }

                    m_Owner.SyncCurrentStepToSpawner();
                    TryShowCurrentModule();
                    m_Owner.PublishGuide();
                    return;
                }

                m_Owner.PublishGuide();
            }

            private bool IsStepCompletedByModuleState(int flatIndex)
            {
                if (!m_Owner.TryGetStepByFlatIndex(flatIndex, out var step)) return false;
                string moduleId = NormalizeModuleId(step.moduleId);
                if (string.IsNullOrEmpty(moduleId)) return false;
                if (!m_Owner.m_ModuleManager.TryGetModuleById(moduleId, out var module) || module == null) return false;
                return module.CurrentPhase == InteractionPhase.End && !module.AllowRestartAfterEnd;
            }

            private bool TryGetCurrentRootStepForPlayback(out int flatIndex, out ChapterPlacementStep rootStep)
            {
                flatIndex = -1;
                rootStep = null;

                if (m_Owner.m_CurrentGroupIndex < 0 || m_Owner.m_CurrentGroupIndex >= m_Owner.m_RuntimeGroups.Count)
                    return false;

                if (m_Owner.m_CurrentFlatIndex < 0 || m_Owner.m_CurrentFlatIndex >= m_Owner.m_FlatSteps.Count)
                    return false;

                if (!m_Owner.IsRootStepFlatIndex(m_Owner.m_CurrentFlatIndex))
                    return false;

                flatIndex = m_Owner.m_CurrentFlatIndex;
                return m_Owner.TryGetStepByFlatIndex(flatIndex, out rootStep) && rootStep != null;
            }

            private void SkipCurrentRootStep(string message, int rootFlatIndex)
            {
                m_HasDeferredRootStepModuleStart = false;
                m_DeferredRootStepModuleId = string.Empty;
                m_Owner.m_HasStartedRootStepPlaybackThisSession = true;
                m_Owner.m_PlaybackCompletedSteps.Add(rootFlatIndex);
                m_Owner.m_Spawner?.TrySetRootPresentationVisibleForPlayback(false);
                PlacementDebugFileLogger.Log($"[PlaybackFlow] skip_root_step flatIndex={rootFlatIndex}, reason={message}");
                m_Owner.PublishStatus(message);

                if (m_Owner.MoveToNextGroup())
                    return;

                m_Owner.PublishStatus($"全部步骤完成：{m_Owner.m_PlaybackCompletedSteps.Count}/{m_Owner.m_FlatSteps.Count}");
            }

            private static GameObject ResolvePlaybackTarget(InteractionModule module)
            {
                return module != null ? module.PlaybackTarget : null;
            }

            private static string ResolvePlaybackTargetModuleId(GameObject target)
            {
                if (target == null)
                    return string.Empty;

                var module = target.GetComponentInChildren<InteractionModule>(true);
                if (module != null && !string.IsNullOrWhiteSpace(module.ModuleId))
                    return module.ModuleId;

                var stage = target.GetComponentInChildren<InteractionStage>(true);
                if (stage != null && !string.IsNullOrWhiteSpace(stage.ModuleId))
                    return stage.ModuleId;

                return target.name;
            }

            private static void LogPlaybackTargetVisibilityChange(
                string moduleId,
                GameObject target,
                bool nextActive,
                string reason)
            {
                if (target == null)
                    return;

                Transform transform = target.transform;
                PlacementDebugFileLogger.Log(
                    $"[PlaybackTarget] reason={reason}, moduleId={moduleId}, nextActive={nextActive}, " +
                    $"target={target.name}, activeSelf={target.activeSelf}, activeInHierarchy={target.activeInHierarchy}, " +
                    $"worldPos=({transform.position.x:F3}, {transform.position.y:F3}, {transform.position.z:F3}), " +
                    $"worldRot=({transform.eulerAngles.x:F3}, {transform.eulerAngles.y:F3}, {transform.eulerAngles.z:F3})");
            }

            private bool IsPlaybackTargetVisible(string moduleId, GameObject target)
            {
                if (m_Owner.IsRootStepModuleId(moduleId))
                    return m_Owner.m_Spawner != null && m_Owner.m_Spawner.IsRootPresentationVisibleForPlayback;

                return target != null && target.activeSelf;
            }

            private bool TrySetPlaybackTargetVisible(
                string moduleId,
                GameObject target,
                bool visible,
                string reason)
            {
                if (target == null)
                    return false;

                if (m_Owner.IsRootStepModuleId(moduleId))
                {
                    LogPlaybackTargetVisibilityChange(moduleId, target, visible, reason);
                    if (m_Owner.m_Spawner == null)
                        return false;

                    return m_Owner.m_Spawner.TrySetRootPresentationVisibleForPlayback(visible);
                }

                if (target.activeSelf == visible)
                    return false;

                LogPlaybackTargetVisibilityChange(moduleId, target, visible, reason);
                target.SetActive(visible);
                return true;
            }

            private bool TryResolvePlaybackTargetByModuleId(string moduleId, out GameObject target)
            {
                target = null;
                moduleId = NormalizeModuleId(moduleId);
                if (string.IsNullOrEmpty(moduleId))
                    return false;

                if (m_Owner.m_ModuleManager != null &&
                    m_Owner.m_ModuleManager.TryGetModuleById(moduleId, out var module) &&
                    module != null)
                {
                    target = ResolvePlaybackTarget(module);
                    if (target != null)
                        return true;
                }

                if (m_Owner.m_ModuleManager != null &&
                    m_Owner.m_ModuleManager.TryGetStageById(moduleId, out var stageModule) &&
                    stageModule != null)
                {
                    target = stageModule.PlaybackTarget;
                    if (target != null)
                        return true;
                }

                if (m_Owner.m_Spawner != null &&
                    m_Owner.m_Spawner.TryGetStageModulePlaced(moduleId, out var stageTarget) &&
                    stageTarget != null)
                {
                    target = stageTarget.gameObject;
                    return true;
                }

                if (InteractionStage.TryFindByModuleId(moduleId, out var stage) && stage != null)
                {
                    target = stage.PlaybackTarget;
                    return target != null;
                }

                return false;
            }

            private bool TryResolveModuleById(string moduleId, out InteractionModule module)
            {
                module = null;
                moduleId = NormalizeModuleId(moduleId);
                if (string.IsNullOrEmpty(moduleId))
                    return false;

                if (EnsureModuleManagerForCurrentMode() &&
                    m_Owner.m_ModuleManager != null)
                {
                    RefreshRegistry();
                    if (m_Owner.m_ModuleManager.TryGetModuleById(moduleId, out module) &&
                        module != null)
                    {
                        return true;
                    }
                }

                var modules = FindObjectsByType<InteractionModule>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                for (int i = 0; i < modules.Length; i++)
                {
                    var candidate = modules[i];
                    if (candidate == null)
                        continue;

                    if (!string.Equals(NormalizeModuleId(candidate.ModuleId), moduleId, StringComparison.OrdinalIgnoreCase))
                        continue;

                    module = candidate;
                    return true;
                }

                return false;
            }
        }
    }
}
