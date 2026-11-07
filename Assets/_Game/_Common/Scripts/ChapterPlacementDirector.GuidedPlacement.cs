using UnityEngine;

namespace VFXViewer
{
    public partial class ChapterPlacementDirector
    {
        public void PlaceCurrentStep()
        {
            EnsureFlows();
            m_GuidedPlacementFlow.PlaceCurrentStep();
        }

        public void SaveCurrentStep()
        {
            EnsureFlows();
            m_GuidedPlacementFlow.SaveCurrentStep();
        }

        public void MoveToPreviousStep()
        {
            EnsureFlows();
            m_GuidedPlacementFlow.MoveToPreviousStep();
        }

        public void MoveToNextStep()
        {
            EnsureFlows();
            m_GuidedPlacementFlow.MoveToNextStep();
        }

        public void DeleteCurrentStepPlacement()
        {
            EnsureFlows();
            m_GuidedPlacementFlow.DeleteCurrentStepPlacement();
        }

        public void RefreshPlacementProgressFromScene()
        {
            EnsureFlows();
            m_GuidedPlacementFlow.RefreshProgressFromScene(preserveCurrentSelection: false);
            PublishGuide();
        }

        public void RefreshPlacementProgressFromScenePreserveCurrent()
        {
            EnsureFlows();
            m_GuidedPlacementFlow.RefreshProgressFromScene(preserveCurrentSelection: true);
            PublishGuide();
        }

        public void OneClickDeployFromLayout()
        {
            EnsureFlows();
            m_GuidedPlacementFlow.OneClickDeployFromLayout();
        }

        public void OneClickClearPlacement()
        {
            EnsureFlows();
            m_GuidedPlacementFlow.OneClickClearPlacement();
        }

        public void SyncLayoutFromServer()
        {
            EnsureFlows();
            m_GuidedPlacementFlow.SyncLayoutFromServer();
        }

        public void ClearSavedLayout()
        {
            EnsureFlows();
            m_GuidedPlacementFlow.ClearSavedLayout();
        }

        private sealed class GuidedPlacementFlow
        {
            private readonly ChapterPlacementDirector m_Owner;

            public GuidedPlacementFlow(ChapterPlacementDirector owner)
            {
                m_Owner = owner;
            }

            public void PlaceCurrentStep()
            {
                if (!EnsureSpawnerForAction("放置")) return;

                bool hasConfiguredRootStep = m_Owner.HasConfiguredRootStep();
                if (!hasConfiguredRootStep)
                {
                    if (!m_Owner.m_Spawner.HasRootMarker)
                    {
                        if (!m_Owner.m_Spawner.TryCreateRootMarkerAtDefaultPose())
                            return;

                        m_Owner.m_IsRootNavigationActive = true;
                        m_Owner.SyncCurrentStepToSpawner();
                        m_Owner.PublishGuide();
                        m_Owner.PublishStatus("已生成定位点，请先调整位置；确认后再点击“放置当前展品”。");
                        return;
                    }

                    if (m_Owner.IsLegacyRootNavigationActive)
                    {
                        m_Owner.PublishStatus("当前选中的是定位点，请直接抓取调整，或点击“下个展品”返回第一个展品。");
                        return;
                    }
                }

                if (!m_Owner.TryGetCurrentStep(out var chapter, out var step, out int flatIndex))
                    return;

                if (m_Owner.IsRootStep(step))
                {
                    if (!m_Owner.m_Spawner.HasRootMarker)
                    {
                        if (!m_Owner.m_Spawner.TryCreateRootMarkerAtDefaultPose())
                            return;

                        m_Owner.PublishStatus("已生成定位点，请先调整位置；待空间锚点确认后点击“保存当前项”进入第一个展品。");
                    }
                    else
                    {
                        m_Owner.m_Spawner.SelectRootMarker();
                        m_Owner.PublishStatus(
                            m_Owner.m_Spawner.HasSavedRootAnchor
                                ? "定位点已就绪。可继续调整位置，确认后点击“保存当前项”进入第一个展品。"
                                : "定位点已存在，请继续调整位置；待空间锚点确认后点击“保存当前项”。");
                    }

                    m_Owner.SyncRootStepProgress(flatIndex);
                    m_Owner.SyncCurrentStepToSpawner();
                    m_Owner.PublishGuide();
                    return;
                }

                if (!m_Owner.m_Spawner.HasRootMarker)
                {
                    if (m_Owner.TrySelectConfiguredRootStep())
                        m_Owner.PublishStatus("请先完成第 1 步“定位点”。");
                    else
                        m_Owner.PublishStatus("请先生成定位点。");
                    m_Owner.PublishGuide();
                    return;
                }

                if (!m_Owner.m_Spawner.HasSavedRootAnchor)
                {
                    m_Owner.PublishStatus("定位点空间锚点尚未确认，请稍候再放置展品。");
                    return;
                }

                if (m_Owner.m_PlacedSteps.Contains(flatIndex))
                {
                    if (m_Owner.m_SavedSteps.Contains(flatIndex))
                        m_Owner.PublishStatus($"当前展品已保存，不可重复放置：{chapter.DisplayName} / {step.DisplayName}。如需重放请先删除当前展品。");
                    else
                        m_Owner.PublishStatus($"当前展品已放置但尚未保存：{chapter.DisplayName} / {step.DisplayName}。请先检查定位点或删除当前展品。");
                    return;
                }

                if (!m_Owner.m_Spawner.SelectByModuleId(step.moduleId))
                {
                    m_Owner.PublishStatus($"展品配置无效：{chapter.DisplayName}/{step.DisplayName} 的 moduleId={step.moduleId} 未在 Spawner 可用清单中。");
                    return;
                }

                // fixedInstanceId 保留在数据结构中用于兼容，但运行时统一按流程顺序自动分配实例 ID。
                var exhibit = m_Owner.m_Spawner.SpawnByModuleId(step.moduleId, null, true);
                if (exhibit == null)
                {
                    m_Owner.PublishStatus($"放置失败：{chapter.DisplayName}/{step.DisplayName}");
                    return;
                }

                m_Owner.m_PlacedSteps.Add(flatIndex);
                if (!m_Owner.m_Spawner.TryAutoSaveExhibit(exhibit))
                {
                    m_Owner.m_SavedSteps.Remove(flatIndex);
                    m_Owner.PublishStatus($"已放置：{chapter.DisplayName} / {step.DisplayName}，但保存未能启动。");
                    m_Owner.PublishGuide();
                    return;
                }

                m_Owner.SyncCurrentStepToSpawner();
                m_Owner.PublishStatus($"已放置：{chapter.DisplayName} / {step.DisplayName}，正在创建空间锚点。可继续调整，完成后手动切换到下个展品。");
                m_Owner.PublishGuide();
            }

            public void SaveCurrentStep()
            {
                if (!EnsureSpawnerForAction("切换下个展品")) return;
                if (m_Owner.IsLegacyRootNavigationActive)
                {
                    m_Owner.PublishStatus("定位点会在移动后保存，无需确认当前展品。");
                    return;
                }

                if (!m_Owner.TryGetCurrentStep(out var chapter, out var step, out int flatIndex))
                    return;

                if (m_Owner.IsRootStep(step))
                {
                    if (!m_Owner.m_Spawner.HasRootMarker)
                    {
                        m_Owner.PublishStatus($"当前步骤尚未放置：{chapter.DisplayName} / {step.DisplayName}");
                        return;
                    }

                    if (!m_Owner.m_Spawner.HasSavedRootAnchor)
                    {
                        m_Owner.PublishStatus("定位点空间锚点尚未确认，请稍候再保存。");
                        return;
                    }

                    void CompleteRootSaveAndAdvance()
                    {
                        m_Owner.SyncRootStepProgress(flatIndex);
                        m_Owner.PublishStatus($"已确认：{chapter.DisplayName} / {step.DisplayName}");

                        if (m_Owner.m_AutoAdvanceAfterSave)
                        {
                            if (!m_Owner.MoveStep(1))
                            {
                                m_Owner.PublishStatus($"全部展品已确认：{m_Owner.m_SavedSteps.Count}/{m_Owner.m_FlatSteps.Count}");
                                return;
                            }

                            m_Owner.SyncCurrentStepToSpawner();
                        }

                        m_Owner.PublishGuide();
                    }

                    CompleteRootSaveAndAdvance();
                    return;
                }

                if (!m_Owner.m_PlacedSteps.Contains(flatIndex))
                {
                    m_Owner.PublishStatus($"当前展品尚未放置：{chapter.DisplayName} / {step.DisplayName}");
                    return;
                }

                void CompleteSaveAndAdvance()
                {
                    m_Owner.m_SavedSteps.Add(flatIndex);
                    m_Owner.PublishStatus($"已确认：{chapter.DisplayName} / {step.DisplayName}");

                    if (m_Owner.m_AutoAdvanceAfterSave)
                    {
                        if (!m_Owner.MoveStep(1))
                        {
                            m_Owner.PublishStatus($"全部展品已确认：{m_Owner.m_SavedSteps.Count}/{m_Owner.m_FlatSteps.Count}");
                            return;
                        }
                        m_Owner.SyncCurrentStepToSpawner();
                    }

                    m_Owner.PublishGuide();
                }

                if (!m_Owner.m_Spawner.HasSavedPlacementByModuleId(step.moduleId))
                {
                    if (!m_Owner.m_Spawner.TrySaveAnchorByModuleId(step.moduleId))
                        return;

                    CompleteSaveAndAdvance();
                    return;
                }

                CompleteSaveAndAdvance();
            }

            public void MoveToPreviousStep()
            {
                if (!EnsureSpawnerForAction("切换上个展品")) return;

                if (m_Owner.IsLegacyRootNavigationActive)
                {
                    m_Owner.PublishStatus("当前已是定位点。");
                    return;
                }

                int upperBound = m_Owner.GetPlacementNavigationUpperBoundInclusive();
                if (upperBound >= 0 && m_Owner.m_CurrentFlatIndex > upperBound)
                    m_Owner.m_CurrentFlatIndex = upperBound;

                if (!m_Owner.HasConfiguredRootStep() &&
                    m_Owner.m_CurrentFlatIndex == 0 &&
                    m_Owner.m_Spawner != null &&
                    m_Owner.m_Spawner.HasRootMarker)
                {
                    m_Owner.m_IsRootNavigationActive = true;
                    m_Owner.SyncCurrentStepToSpawner();
                    m_Owner.PublishGuide();
                    return;
                }

                if (!m_Owner.MoveStep(-1))
                {
                    m_Owner.PublishStatus("当前已是第一个展品。");
                    return;
                }

                m_Owner.SyncCurrentStepToSpawner();
                m_Owner.PublishGuide();
            }

            public void MoveToNextStep()
            {
                if (!EnsureSpawnerForAction("切换下个展品")) return;

                if (m_Owner.IsLegacyRootNavigationActive)
                {
                    m_Owner.m_IsRootNavigationActive = false;
                    m_Owner.SyncCurrentStepToSpawner();
                    m_Owner.PublishGuide();
                    return;
                }

                if (m_Owner.TryGetCurrentStep(out _, out var currentStep, out _) &&
                    m_Owner.IsRootStep(currentStep))
                {
                    if (!m_Owner.m_Spawner.HasRootMarker)
                    {
                        m_Owner.PublishStatus("请先放置定位点，才能进入下一个展品。");
                        return;
                    }

                    if (!m_Owner.m_Spawner.HasSavedRootAnchor)
                    {
                        m_Owner.PublishStatus("请先保存定位点，才能进入下一个展品。");
                        return;
                    }
                }

                int upperBound = m_Owner.GetPlacementNavigationUpperBoundInclusive();
                if (upperBound < 0)
                {
                    m_Owner.PublishStatus("当前没有可切换的展品。");
                    return;
                }

                if (m_Owner.m_CurrentFlatIndex >= upperBound)
                {
                    bool isLastExhibit = upperBound >= m_Owner.m_FlatSteps.Count - 1;
                    bool currentPlaced = m_Owner.m_PlacedSteps.Contains(m_Owner.m_CurrentFlatIndex);
                    if (isLastExhibit && currentPlaced)
                        m_Owner.PublishStatus("当前已是最后一个展品。");
                    else
                        m_Owner.PublishStatus("请先放置当前展品，才能切换到更后面的展品。");
                    return;
                }

                if (!m_Owner.MoveStep(1))
                {
                    m_Owner.PublishStatus("当前已是最后一个展品。");
                    return;
                }

                m_Owner.SyncCurrentStepToSpawner();
                m_Owner.PublishGuide();
            }

            public void DeleteCurrentStepPlacement()
            {
                if (!EnsureSpawnerForAction("删除")) return;

                if (!m_Owner.TryGetCurrentStep(out var chapter, out var step, out int flatIndex))
                    return;

                if (m_Owner.IsRootStep(step))
                {
                    bool deleted = m_Owner.m_Spawner.TryDeleteRootMarker();
                    m_Owner.SyncRootStepProgress(flatIndex);
                    m_Owner.SyncCurrentStepToSpawner();
                    if (deleted)
                        m_Owner.PublishStatus($"已删除当前步骤摆放：{chapter.DisplayName} / {step.DisplayName}");
                    m_Owner.PublishGuide();
                    return;
                }

                if (m_Owner.IsLegacyRootNavigationActive)
                {
                    m_Owner.PublishStatus("定位点不可通过当前展品删除入口处理。");
                    return;
                }

                bool wasSaved = m_Owner.m_SavedSteps.Contains(flatIndex);

                if (wasSaved)
                {
                    if (!m_Owner.m_Spawner.TryDeleteClosestAnchorByModuleId(step.moduleId))
                        return;

                    m_Owner.m_PlacedSteps.Remove(flatIndex);
                    m_Owner.m_SavedSteps.Remove(flatIndex);
                    m_Owner.SyncCurrentStepToSpawner();
                    m_Owner.PublishStatus($"已删除当前展品摆放：{chapter.DisplayName} / {step.DisplayName}，该展品状态已改为未保存（待重新放置）。");
                }
                else
                {
                    bool hadPlaced = m_Owner.m_Spawner.HasPlacedAnchorByModuleId(step.moduleId);
                    if (hadPlaced && !m_Owner.m_Spawner.TryDeleteClosestAnchorByModuleId(step.moduleId))
                        return;

                    m_Owner.m_PlacedSteps.Remove(flatIndex);
                    m_Owner.m_SavedSteps.Remove(flatIndex);

                    m_Owner.SyncCurrentStepToSpawner();
                    if (hadPlaced)
                        m_Owner.PublishStatus($"已删除当前展品摆放：{chapter.DisplayName} / {step.DisplayName}，该展品已恢复为未放置状态。");
                    else
                        m_Owner.PublishStatus($"当前展品未放置：{chapter.DisplayName} / {step.DisplayName}。");
                }

                m_Owner.PublishGuide();
            }

            public void OneClickDeployFromLayout()
            {
                if (!m_Owner.IsPlacementMode)
                {
                    m_Owner.PublishStatus("当前处于体验模式，无法一键布展。请先切回布展模式。");
                    return;
                }

                if (!m_Owner.IsPlacementActionsReady)
                {
                    m_Owner.PublishStatus("一键布展不可用：请绑定 Spawner 并检查 ChapterPlacementPlan 配置。");
                    return;
                }

                if (!m_Owner.m_Spawner.RebuildWholeLayout(success =>
                    {
                        RefreshProgressFromScene(preserveCurrentSelection: false);
                        m_Owner.SyncCurrentStepToSpawner();
                        m_Owner.PublishGuide();
                    }))
                    return;
            }

            public void OneClickClearPlacement()
            {
                if (!m_Owner.IsPlacementMode)
                {
                    m_Owner.PublishStatus("当前处于体验模式，无法一键清展。请先切回布展模式。");
                    return;
                }

                if (!m_Owner.IsPlacementActionsReady)
                {
                    m_Owner.PublishStatus("一键清展不可用：请绑定 Spawner 并检查 ChapterPlacementPlan 配置。");
                    return;
                }

                m_Owner.m_Spawner.ClearScenePreserveLayout();
                m_Owner.m_ModulePlaybackFlow.DetachCallbacks();
                m_Owner.m_IsRestoredPlaybackMode = false;
                m_Owner.m_LastRestoreGateMessage = string.Empty;
                m_Owner.m_PlacedSteps.Clear();
                m_Owner.m_SavedSteps.Clear();
                m_Owner.m_CurrentFlatIndex = 0;
                m_Owner.m_CurrentGroupIndex = 0;
                m_Owner.m_IsRootNavigationActive = !m_Owner.HasConfiguredRootStep();
                m_Owner.SyncCurrentStepToSpawner();
                m_Owner.PublishGuide();
            }

            public void SyncLayoutFromServer()
            {
                if (!m_Owner.IsPlacementMode)
                {
                    m_Owner.PublishStatus("当前处于体验模式，无法从服务器同步 layout。请先切回布展模式。");
                    return;
                }

                if (!m_Owner.IsPlacementActionsReady)
                {
                    m_Owner.PublishStatus("同步 layout 不可用：请绑定 Spawner 并检查 ChapterPlacementPlan 配置。");
                    return;
                }

                m_Owner.m_Spawner.TrySyncLayoutFromServer();
            }

            public void ClearSavedLayout()
            {
                if (!m_Owner.IsPlacementMode)
                {
                    m_Owner.PublishStatus("当前处于体验模式，无法清空 layout。请先切回布展模式。");
                    return;
                }

                if (!m_Owner.IsPlacementActionsReady)
                {
                    m_Owner.PublishStatus("清空 layout 不可用：请绑定 Spawner 并检查 ChapterPlacementPlan 配置。");
                    return;
                }

                m_Owner.m_Spawner.TryClearSavedLayout();
            }

            public void RefreshProgressFromScene(bool preserveCurrentSelection)
            {
                int previousFlatIndex = m_Owner.m_CurrentFlatIndex;
                bool canPreserveCurrent = preserveCurrentSelection &&
                                          !m_Owner.IsLegacyRootNavigationActive &&
                                          previousFlatIndex >= 0 &&
                                          previousFlatIndex < m_Owner.m_FlatSteps.Count;

                PlacementDebugFileLogger.Log(
                    $"[PlacementProgress] begin preserveCurrentSelection={preserveCurrentSelection}, " +
                    $"canPreserveCurrent={canPreserveCurrent}, previousFlatIndex={previousFlatIndex}, " +
                    $"rootNavigation={m_Owner.m_IsRootNavigationActive}, flatStepCount={m_Owner.m_FlatSteps.Count}");

                m_Owner.m_PlacedSteps.Clear();
                m_Owner.m_SavedSteps.Clear();

                for (int i = 0; i < m_Owner.m_FlatSteps.Count; i++)
                {
                    if (!m_Owner.TryGetStepByFlatIndex(i, out var step)) continue;
                    bool hasPlacedInScene;
                    bool hasSavedInScene;
                    bool hasSavedInSession;
                    if (m_Owner.IsRootStep(step))
                    {
                        hasPlacedInScene = m_Owner.m_Spawner.HasRootMarker;
                        hasSavedInScene = hasPlacedInScene && m_Owner.m_Spawner.HasSavedRootAnchor;
                        hasSavedInSession = false;
                    }
                    else
                    {
                        hasPlacedInScene = m_Owner.m_Spawner.HasPlacedAnchorByModuleId(step.moduleId);
                        hasSavedInScene = hasPlacedInScene && m_Owner.m_Spawner.HasSavedPlacementByModuleId(step.moduleId);
                        hasSavedInSession = !hasSavedInScene && m_Owner.m_Spawner.HasSavedSessionPlacementByModuleId(step.moduleId);
                    }

                    if (i == previousFlatIndex || hasPlacedInScene || hasSavedInScene || hasSavedInSession)
                    {
                        PlacementDebugFileLogger.Log(
                            $"[PlacementProgress] stepIndex={i}, moduleId={step.moduleId}, " +
                            $"placedInScene={hasPlacedInScene}, savedInScene={hasSavedInScene}, savedInSession={hasSavedInSession}");
                    }

                    if (!hasPlacedInScene && !hasSavedInSession)
                        continue;

                    m_Owner.m_PlacedSteps.Add(i);
                    if (hasSavedInScene || hasSavedInSession)
                        m_Owner.m_SavedSteps.Add(i);
                }

                if (canPreserveCurrent)
                {
                    m_Owner.m_CurrentFlatIndex = previousFlatIndex;
                    m_Owner.SyncCurrentStepToSpawner();
                    string preservedModuleId = m_Owner.TryGetStepByFlatIndex(m_Owner.m_CurrentFlatIndex, out var preservedStep) && preservedStep != null
                        ? preservedStep.moduleId
                        : "<none>";
                    PlacementDebugFileLogger.Log(
                        $"[PlacementProgress] preserveCurrent currentFlatIndex={m_Owner.m_CurrentFlatIndex}, moduleId={preservedModuleId}, " +
                        $"placedCount={m_Owner.m_PlacedSteps.Count}, savedCount={m_Owner.m_SavedSteps.Count}");
                    return;
                }

                int firstPending = -1;
                for (int i = 0; i < m_Owner.m_FlatSteps.Count; i++)
                {
                    if (m_Owner.m_SavedSteps.Contains(i)) continue;
                    firstPending = i;
                    break;
                }

                if (firstPending >= 0) m_Owner.m_CurrentFlatIndex = firstPending;
                else if (m_Owner.m_FlatSteps.Count > 0) m_Owner.m_CurrentFlatIndex = m_Owner.m_FlatSteps.Count - 1;
                else m_Owner.m_CurrentFlatIndex = 0;

                m_Owner.SyncCurrentStepToSpawner();
                string selectedModuleId = m_Owner.TryGetStepByFlatIndex(m_Owner.m_CurrentFlatIndex, out var selectedStep) && selectedStep != null
                    ? selectedStep.moduleId
                    : "<none>";
                PlacementDebugFileLogger.Log(
                    $"[PlacementProgress] end currentFlatIndex={m_Owner.m_CurrentFlatIndex}, moduleId={selectedModuleId}, " +
                    $"placedCount={m_Owner.m_PlacedSteps.Count}, savedCount={m_Owner.m_SavedSteps.Count}");
            }

            private bool EnsureSpawnerForAction(string actionName)
            {
                if (!m_Owner.IsPlacementMode)
                {
                    m_Owner.PublishStatus($"当前处于体验模式，无法执行{actionName}。请先切回布展模式。");
                    return false;
                }

                if (!m_Owner.IsGuidedModeReady)
                {
                    m_Owner.PublishStatus("引导不可用：请检查 ChapterPlacementPlan 配置。");
                    return false;
                }

                if (m_Owner.m_Spawner != null) return true;

                m_Owner.PublishStatus($"当前未绑定 Spawner，无法执行{actionName}。已启用模块测试模式，可通过 InteractionModule 完成事件自动推进。");
                return false;
            }
        }
    }
}
