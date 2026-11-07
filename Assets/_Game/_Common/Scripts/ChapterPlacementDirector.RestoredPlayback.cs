using System;
using System.Collections.Generic;
using System.Collections;
using UnityEngine;

namespace VFXViewer
{
    public partial class ChapterPlacementDirector
    {
        private sealed class RestoredPlaybackFlow
        {
            private readonly ChapterPlacementDirector m_Owner;
            private Coroutine m_DeferredVisibilityRefreshCoroutine;
            private Coroutine m_DeferredPlaybackGateRefreshCoroutine;
            private bool m_IsEvaluatingPlaybackGate;

            public RestoredPlaybackFlow(ChapterPlacementDirector owner)
            {
                m_Owner = owner;
            }

            public void OnEnable()
            {
                if (m_Owner.m_Spawner == null)
                    return;

                if (m_Owner.m_Spawner.onExhibitRestored != null)
                {
                    m_Owner.m_Spawner.onExhibitRestored.RemoveListener(HandleSessionExhibitRestored);
                    m_Owner.m_Spawner.onExhibitRestored.AddListener(HandleSessionExhibitRestored);
                }

                m_Owner.m_Spawner.AnchorPoseConflictChanged -= HandleAnchorPoseConflictChanged;
                m_Owner.m_Spawner.AnchorPoseConflictChanged += HandleAnchorPoseConflictChanged;
                m_Owner.m_Spawner.RestoreAvailabilityChanged -= HandleRestoreAvailabilityChanged;
                m_Owner.m_Spawner.RestoreAvailabilityChanged += HandleRestoreAvailabilityChanged;
            }

            public void OnDisable()
            {
                if (m_Owner.m_Spawner != null)
                {
                    if (m_Owner.m_Spawner.onExhibitRestored != null)
                        m_Owner.m_Spawner.onExhibitRestored.RemoveListener(HandleSessionExhibitRestored);

                    m_Owner.m_Spawner.AnchorPoseConflictChanged -= HandleAnchorPoseConflictChanged;
                    m_Owner.m_Spawner.RestoreAvailabilityChanged -= HandleRestoreAvailabilityChanged;
                }

                CancelDeferredVisibilityRefresh();
                CancelDeferredPlaybackGateRefresh();
            }

            public void TryEnterPlaybackIfReady()
            {
                if (m_IsEvaluatingPlaybackGate)
                {
                    SchedulePlaybackGateRefresh();
                    return;
                }

                m_IsEvaluatingPlaybackGate = true;
                try
                {
                    if (!m_Owner.IsExperienceMode) return;
                    if (!m_Owner.m_EnableRestoredModulePlayback || m_Owner.m_Spawner == null) return;
                    if (m_Owner.m_Spawner.HasTransientExperienceBootstrapRootPose) return;
                    if (m_Owner.m_Spawner.ExpectedSessionExhibitCount <= 0) return;

                    if (m_Owner.m_IsExperiencePlaybackActive && !m_Owner.m_IsRestoredPlaybackMode)
                    {
                        m_Owner.m_ModulePlaybackFlow.RefreshRegistry();
                        PlacementDebugFileLogger.Log(
                            "[PlaybackFlow] skip_restored_playback_takeover source=restored_playback existingMode=scene_playback");
                        return;
                    }

                    if (!ArePlaybackPrerequisitesSatisfied(out string pendingReason))
                    {
                        if (m_Owner.m_IsExperiencePlaybackActive && m_Owner.m_IsRestoredPlaybackMode)
                        {
                            m_Owner.m_ModulePlaybackFlow.HideAllModules(includeStageModules: true);
                            m_Owner.m_IsExperiencePlaybackActive = false;
                            m_Owner.m_IsRestoredPlaybackMode = true;
                            m_Owner.m_ModulePlaybackFlow.DetachCallbacks();
                        }

                        ReportGateMessage(pendingReason);
                        return;
                    }

                    if (m_Owner.m_IsExperiencePlaybackActive && m_Owner.m_IsRestoredPlaybackMode)
                    {
                        m_Owner.m_ModulePlaybackFlow.RefreshRegistry();
                        m_Owner.m_ModulePlaybackFlow.TryShowCurrentModule();
                        return;
                    }

                    EnterPlaybackMode();
                }
                finally
                {
                    m_IsEvaluatingPlaybackGate = false;
                }
            }

            private void HandleSessionExhibitRestored(string moduleId)
            {
                if (m_Owner.IsPlacementMode)
                {
                    if (m_Owner.m_DeferPlacementProgressRefreshUntilFinalPass)
                    {
                        PlacementDebugFileLogger.Log(
                            $"[PlacementProgress] defer restored callback while entering placement: moduleId={moduleId}");
                        return;
                    }

                    m_Owner.m_GuidedPlacementFlow.RefreshProgressFromScene(preserveCurrentSelection: true);
                    m_Owner.PublishGuide();
                    return;
                }

                if (m_Owner.m_Spawner != null && m_Owner.m_Spawner.HasTransientExperienceBootstrapRootPose)
                    return;

                SchedulePlaybackGateRefresh();
            }

            private void HandleAnchorPoseConflictChanged(bool hasConflict)
            {
                if (!m_Owner.IsExperienceMode || !m_Owner.m_EnableRestoredModulePlayback || m_Owner.m_Spawner == null)
                    return;
                if (m_Owner.m_Spawner.HasTransientExperienceBootstrapRootPose)
                    return;

                if (hasConflict)
                {
                    m_Owner.BlockGlobalGuideCommands("anchor_pose_conflict");
                    m_Owner.m_ModulePlaybackFlow.HideAllModules(includeStageModules: true);
                    if (m_Owner.m_IsExperiencePlaybackActive)
                    {
                        m_Owner.m_IsExperiencePlaybackActive = false;
                        m_Owner.m_IsRestoredPlaybackMode = true;
                        m_Owner.m_ModulePlaybackFlow.DetachCallbacks();
                    }

                    ReportGateMessage(BuildAnchorPoseConflictGateMessage());
                    return;
                }

                SchedulePlaybackGateRefresh();
            }

            private void HandleRestoreAvailabilityChanged()
            {
                if (!m_Owner.IsExperienceMode || !m_Owner.m_EnableRestoredModulePlayback || m_Owner.m_Spawner == null)
                    return;
                if (m_Owner.m_Spawner.HasTransientExperienceBootstrapRootPose)
                    return;

                SchedulePlaybackGateRefresh();
            }

            private void EnterPlaybackMode()
            {
                if (!m_Owner.m_EnableRestoredModulePlayback || m_Owner.m_Spawner == null) return;

                bool firstEnter = !m_Owner.m_IsRestoredPlaybackMode;
                m_Owner.m_IsExperiencePlaybackActive = true;
                m_Owner.m_IsRestoredPlaybackMode = true;
                if (!m_Owner.m_ModulePlaybackFlow.EnsureModuleManagerForCurrentMode())
                {
                    m_Owner.m_IsExperiencePlaybackActive = false;
                    m_Owner.m_IsRestoredPlaybackMode = false;
                    return;
                }

                if (firstEnter && m_Owner.m_ResetToFirstStepWhenSessionRestored)
                    m_Owner.ResetPlaybackProgressToFirstGroup();

                m_Owner.m_ModulePlaybackFlow.AttachCallbacks();
                m_Owner.TryPrepareCurrentChapterPlacements(out _, out _);
                m_Owner.m_ModulePlaybackFlow.RefreshRegistry();
                PlacementDebugFileLogger.Log(
                    "[PlaybackFlow] reset_active_modules_before_current_group source=restored_playback");
                m_Owner.m_ModulePlaybackFlow.HideAllModules(includeStageModules: true);
                m_Owner.SyncCurrentStepToSpawner();
                m_Owner.m_LastRestoreGateMessage = string.Empty;
                m_Owner.m_ModulePlaybackFlow.TryShowCurrentModule();
                m_Owner.ReleaseGlobalGuideCommands("restored_playback_ready");
                ScheduleDeferredVisibilityRefresh();
                m_Owner.PublishGuide();
            }

            private bool ArePlaybackPrerequisitesSatisfied(out string pendingReason)
            {
                pendingReason = string.Empty;

                if (m_Owner.m_FlatSteps.Count == 0 || m_Owner.m_RuntimeGroups.Count == 0)
                {
                    pendingReason = "恢复播放等待引导清单初始化。";
                    return false;
                }

                if (m_Owner.m_Spawner.HasAnchorPoseConflict)
                {
                    pendingReason = BuildAnchorPoseConflictGateMessage();
                    return false;
                }

                // Keep the serialized field name for compatibility, but the runtime gate is now
                // "Root is usable for playback", not "every saved anchor has already restored".
                if (m_Owner.m_WaitForRootRestoreBeforePlayback &&
                    !m_Owner.m_Spawner.IsRootReadyForPlayback)
                {
                    string pendingAnchorMessage = m_Owner.m_Spawner.HasPendingAnchorAttachments
                        ? " 当前仍有候补锚点创建中。"
                        : string.Empty;
                    pendingReason = $"恢复播放等待定位点恢复或推断完成。{pendingAnchorMessage}";
                    return false;
                }

                if (!m_Owner.m_WaitForCurrentGroupModulesBeforePlayback)
                    return true;

                if (m_Owner.TryPrepareCurrentChapterPlacements(out var pendingModuleNames, out string chapterDisplayName))
                    return true;

                string chapterLabel = string.IsNullOrWhiteSpace(chapterDisplayName) ? "当前章节" : chapterDisplayName;
                if (pendingModuleNames.Count == 0)
                {
                    pendingReason = $"恢复播放等待章节展品生成：{chapterLabel}。";
                    return false;
                }

                pendingReason = $"恢复播放等待章节展品生成：{chapterLabel}（{string.Join("、", pendingModuleNames)}）。";
                return false;
            }

            private string BuildAnchorPoseConflictGateMessage()
            {
                string summary = m_Owner.m_Spawner != null ? m_Owner.m_Spawner.AnchorPoseConflictSummary : string.Empty;
                return string.IsNullOrWhiteSpace(summary)
                    ? "恢复播放等待空间锚点位姿稳定。"
                    : $"恢复播放等待空间锚点位姿稳定。{summary}";
            }

            private void ReportGateMessage(string message)
            {
                if (!m_Owner.m_ReportRestoreGateProgress) return;
                if (string.IsNullOrWhiteSpace(message)) return;
                if (string.Equals(m_Owner.m_LastRestoreGateMessage, message, StringComparison.Ordinal))
                    return;

                m_Owner.m_LastRestoreGateMessage = message;
                m_Owner.PublishStatus(message);
                UnityEngine.Debug.Log($"[RestoredPlayback] gate: {message}", m_Owner);
            }

            private void ScheduleDeferredVisibilityRefresh()
            {
                CancelDeferredVisibilityRefresh();
                m_DeferredVisibilityRefreshCoroutine = m_Owner.StartCoroutine(RefreshVisibilityNextFrame());
            }

            private void CancelDeferredVisibilityRefresh()
            {
                if (m_DeferredVisibilityRefreshCoroutine == null)
                    return;

                m_Owner.StopCoroutine(m_DeferredVisibilityRefreshCoroutine);
                m_DeferredVisibilityRefreshCoroutine = null;
            }

            private void SchedulePlaybackGateRefresh()
            {
                if (m_DeferredPlaybackGateRefreshCoroutine != null || !m_Owner.isActiveAndEnabled)
                    return;

                m_DeferredPlaybackGateRefreshCoroutine = m_Owner.StartCoroutine(EvaluatePlaybackGateNextFrame());
            }

            private void CancelDeferredPlaybackGateRefresh()
            {
                if (m_DeferredPlaybackGateRefreshCoroutine == null)
                    return;

                m_Owner.StopCoroutine(m_DeferredPlaybackGateRefreshCoroutine);
                m_DeferredPlaybackGateRefreshCoroutine = null;
            }

            private IEnumerator EvaluatePlaybackGateNextFrame()
            {
                yield return null;
                m_DeferredPlaybackGateRefreshCoroutine = null;

                TryEnterPlaybackIfReady();
            }

            private IEnumerator RefreshVisibilityNextFrame()
            {
                yield return null;
                m_DeferredVisibilityRefreshCoroutine = null;

                if (!m_Owner.IsExperienceMode || !m_Owner.m_IsExperiencePlaybackActive)
                    yield break;

                m_Owner.m_ModulePlaybackFlow.RefreshRegistry();
                m_Owner.m_ModulePlaybackFlow.TryShowCurrentModule();
            }
        }
    }
}
