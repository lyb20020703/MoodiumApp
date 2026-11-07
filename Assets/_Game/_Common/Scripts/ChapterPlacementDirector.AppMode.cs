using System;
using System.Collections;
using UnityEngine;

namespace VFXViewer
{
    public partial class ChapterPlacementDirector
    {
        private Coroutine m_ExperienceBootstrapReadinessCoroutine;

        private void Awake()
        {
            EnsureFlows();
            if (m_Spawner != null)
                m_Spawner.ConfigureInitialAppMode(ResolveStartupAppMode());
        }

        public void EnterPlacementMode()
        {
            EnsureFlows();
            SetAppMode(ExhibitAppMode.Placement, publishStatus: true);
        }

        public void EnterExperienceMode()
        {
            EnsureFlows();
            SetAppMode(ExhibitAppMode.Experience, publishStatus: true);
        }

        private void SetAppMode(ExhibitAppMode mode, bool publishStatus)
        {
            ExhibitAppMode previousMode = m_CurrentAppMode;
            bool hadInitializedAppMode = m_HasInitializedAppMode;
            PlacementDebugFileLogger.Log(
                $"[PlacementMode] request targetMode={mode}, currentMode={m_CurrentAppMode}, " +
                $"initialized={m_HasInitializedAppMode}, rootNavigation={m_IsRootNavigationActive}");
            CancelDeferredPlacementPreviewRefresh();

            if (m_HasInitializedAppMode && m_CurrentAppMode == mode)
            {
                if (publishStatus)
                {
                    PublishStatus(mode == ExhibitAppMode.Placement
                        ? "当前已经处于布展模式。"
                        : "当前已经处于体验模式。");
                }

                return;
            }

            if (mode == ExhibitAppMode.Experience)
            {
                if (!IsGuidedModeReady)
                {
                    PublishStatus("体验模式不可用：请检查 ChapterPlacementPlan 配置。");
                    return;
                }

                if (m_Spawner != null && m_Spawner.HasPendingAnchorAttachments)
                {
                    PublishStatus("仍有空间锚点创建中的展品或 Root，请稍候再进入体验模式。");
                    return;
                }

                if (HasUnsavedPlacementStep())
                {
                    PublishStatus("仍有未保存的展品，请先检查定位点或删除后再进入体验模式。");
                    return;
                }
            }

            m_CurrentAppMode = mode;
            m_IsRootNavigationActive = false;
            m_LastRestoreGateMessage = string.Empty;
            m_IsRestoredPlaybackMode = false;
            m_IsExperiencePlaybackActive = false;
            m_PlaybackCompletedSteps.Clear();
            RebuildRuntimeGroups();

            if (m_CurrentAppMode == ExhibitAppMode.Placement)
            {
                EnterPlacementModeInternal(publishStatus);
                m_HasInitializedAppMode = true;
                PublishGuide();
                PersistCurrentAppMode();
                AppModeChanged?.Invoke(m_CurrentAppMode);
                return;
            }

            bool enteredFromPlacement = hadInitializedAppMode && previousMode == ExhibitAppMode.Placement;
            EnterExperienceModeInternal(publishStatus, enteredFromPlacement);
            m_HasInitializedAppMode = true;
            PublishGuide();
            PersistCurrentAppMode();
            AppModeChanged?.Invoke(m_CurrentAppMode);
        }

        private void EnterPlacementModeInternal(bool publishStatus)
        {
            CancelExperienceBootstrapReadinessWatch();
            ReleaseGlobalGuideCommands("enter_placement_mode");
            m_ModulePlaybackFlow.DetachCallbacks();
            PrepareGuideRuntimesForModeTransition(visible: false);
            PlacementDebugFileLogger.Log("[PlacementMode] enter placement begin");
            m_HasAcceptedExperienceRootCardThisSession = false;
            m_HasStartedRootStepPlaybackThisSession = false;

            if (m_Spawner != null)
            {
                m_DeferPlacementProgressRefreshUntilFinalPass = true;
                m_Spawner.SetAppMode(ExhibitAppMode.Placement);
                SyncCurrentStepToSpawner();
                m_Spawner.RefreshPlacementPreviewForCurrentMode();
                m_Spawner.LogPlacementPreviewVisibilitySnapshot("enter placement immediate");
                ScheduleDeferredPlacementPreviewRefresh();
            }

            PlacementDebugFileLogger.Log("[PlacementMode] enter placement end");

            if (publishStatus)
                PublishStatus("已切换到布展模式。当前场景展品会以预览态显示，便于摆放和调整。");
        }

        private void EnterExperienceModeInternal(bool publishStatus, bool enteredFromPlacement)
        {
            CancelExperienceBootstrapReadinessWatch();
            BlockGlobalGuideCommands("enter_experience_mode");
            PrepareGuideRuntimesForModeTransition(visible: false);
            PlacementDebugFileLogger.Log("[PlacementMode] enter experience begin");
            m_HasCompletedRootStepPlaybackThisSession = false;
            m_HasAcceptedExperienceRootCardThisSession = false;
            m_HasStartedRootStepPlaybackThisSession = false;

            if (m_Spawner != null)
                m_Spawner.SetAppMode(
                    ExhibitAppMode.Experience,
                    suppressImmediateAnchorRestore: true,
                    deferPlacementPreviewSync: false);

            if (m_ResetToFirstStepWhenExperienceEntered)
                ResetPlaybackProgressToFirstGroup();
            else
                SyncCurrentGroupToCurrentStep();

            if (IsModuleOnlyTestModeActive)
            {
                ReleaseGlobalGuideCommands("enter_module_only_test_mode");
                m_ModulePlaybackFlow.EnterModuleOnlyTestMode();
                if (publishStatus)
                    PublishStatus("已切换到体验模式（模块测试）。");
                return;
            }
            m_ModulePlaybackFlow.DetachCallbacks();
            m_IsExperiencePlaybackActive = false;
            m_IsRestoredPlaybackMode = false;
            m_LastRestoreGateMessage = string.Empty;
            if (publishStatus)
                PublishStatus("已切换到体验模式，请先走进识别光圈；进入后再将 RootCard 保持在 0.5m~2.0m 范围内稳定识别约 1 秒。");
        }

        private void StartExperiencePlaybackFromScene()
        {
            if (m_Spawner == null)
                return;

            m_IsRestoredPlaybackMode = false;
            m_IsExperiencePlaybackActive = true;
            if (!m_ModulePlaybackFlow.EnsureModuleManagerForCurrentMode())
            {
                m_IsExperiencePlaybackActive = false;
                PublishStatus("体验模式启动失败：未找到 InteractionModuleManager。");
                return;
            }

            m_ModulePlaybackFlow.RefreshRegistry();
            PlacementDebugFileLogger.Log(
                "[PlaybackFlow] reset_active_modules_before_current_group source=scene_playback");
            m_ModulePlaybackFlow.HideAllModules(includeStageModules: true);
            m_ModulePlaybackFlow.SkipCompletedOrUnavailableRootStepAtPlaybackStart();
            SyncCurrentStepToSpawner();
            ReleaseGlobalGuideCommands("scene_playback_ready");
            m_ModulePlaybackFlow.AttachCallbacks();
            m_ModulePlaybackFlow.TryShowCurrentModule();
            m_ModulePlaybackFlow.StartDeferredCurrentRootStepModuleAfterPlaybackReady();
        }

        internal bool TryHandleExperienceRootCardPose(Pose rootPose, string matchedImageName, string poseSummary)
        {
            if (!IsExperienceMode || m_Spawner == null)
                return false;

            if (m_HasAcceptedExperienceRootCardThisSession)
            {
                PlacementDebugFileLogger.Log(
                    $"[PlacementMode] experience bootstrap ignored matchedImage={matchedImageName}, " +
                    $"reason=session_locked playbackActive={m_IsExperiencePlaybackActive}, " +
                    $"waitingReadiness={(m_ExperienceBootstrapReadinessCoroutine != null)}, " +
                    $"poseSummary={poseSummary}");
                return true;
            }

            if (!m_Spawner.TryBeginTransientExperienceBootstrap(rootPose, out bool hasLayout, out string failureReason))
            {
                if (!string.IsNullOrWhiteSpace(failureReason))
                    PublishStatus(failureReason);
                return false;
            }

            PlacementDebugFileLogger.Log(
                $"[PlacementMode] experience bootstrap accepted matchedImage={matchedImageName}, " +
                $"poseSummary={poseSummary}, hasLayout={hasLayout}");
            m_HasAcceptedExperienceRootCardThisSession = true;
            m_ModulePlaybackFlow.DeferRootStepModuleStartUntilScenePlaybackReady();

            if (!hasLayout)
            {
                m_LastRestoreGateMessage = string.Empty;
                PublishStatus("RootCard 已识别并建立 Root，但当前没有 layout 数据。请先同步 layout。");
                ReleaseGlobalGuideCommands("experience_root_only_ready");
                return true;
            }

            StartExperienceBootstrapReadinessWatch();
            return true;
        }

        private void StartExperienceBootstrapReadinessWatch()
        {
            CancelExperienceBootstrapReadinessWatch();
            if (!isActiveAndEnabled)
                return;

            m_ExperienceBootstrapReadinessCoroutine = StartCoroutine(WaitForExperienceBootstrapReadiness());
        }

        private void CancelExperienceBootstrapReadinessWatch()
        {
            if (m_ExperienceBootstrapReadinessCoroutine == null)
                return;

            StopCoroutine(m_ExperienceBootstrapReadinessCoroutine);
            m_ExperienceBootstrapReadinessCoroutine = null;
        }

        private IEnumerator WaitForExperienceBootstrapReadiness()
        {
            while (isActiveAndEnabled && IsExperienceMode && m_Spawner != null)
            {
                if (!m_Spawner.HasTransientExperienceLayoutAtBootstrap)
                {
                    PublishStatus("RootCard 已识别并建立 Root，但当前没有 layout 数据。请先同步 layout。");
                    m_ExperienceBootstrapReadinessCoroutine = null;
                    yield break;
                }

                if (TryPrepareCurrentChapterPlacements(out var pendingModuleNames, out string chapterDisplayName))
                {
                    m_LastRestoreGateMessage = string.Empty;
                    StartExperiencePlaybackFromScene();
                    PublishStatus("已切换到体验模式。");
                    m_ExperienceBootstrapReadinessCoroutine = null;
                    yield break;
                }

                string chapterLabel = string.IsNullOrWhiteSpace(chapterDisplayName) ? "当前章节" : chapterDisplayName;
                string gateMessage = pendingModuleNames.Count == 0
                    ? $"体验模式等待章节展品生成：{chapterLabel}。"
                    : $"体验模式等待章节展品生成：{chapterLabel}（{string.Join("、", pendingModuleNames)}）。";
                if (!string.Equals(m_LastRestoreGateMessage, gateMessage, StringComparison.Ordinal))
                {
                    m_LastRestoreGateMessage = gateMessage;
                    PublishStatus(gateMessage);
                }

                yield return null;
            }

            m_ExperienceBootstrapReadinessCoroutine = null;
        }

        private bool HasUnsavedPlacementStep()
        {
            foreach (int flatIndex in m_PlacedSteps)
            {
                if (!m_SavedSteps.Contains(flatIndex))
                    return true;
            }
            return false;
        }
    }
}
