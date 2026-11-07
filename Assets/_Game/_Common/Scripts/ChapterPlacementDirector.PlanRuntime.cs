using System;
using System.Collections.Generic;
using Interaction;
using UnityEngine;

namespace VFXViewer
{
    public partial class ChapterPlacementDirector
    {
        private void RebuildStepCache()
        {
            m_FlatSteps.Clear();
            m_FlatIndexByModuleId.Clear();
            m_RuntimeGroups.Clear();
            m_GroupIndexByFlatIndex.Clear();
            m_CurrentFlatIndex = 0;
            m_CurrentGroupIndex = 0;
            m_IsRootNavigationActive = false;
            int rootStepCount = 0;
            int firstRootFlatIndex = -1;

            if (m_Plan == null || m_Plan.chapters == null) return;

            if (ShouldInjectImplicitRootStep() &&
                TryGetImplicitRootHostChapterIndex(out int implicitRootChapterIndex))
            {
                m_FlatSteps.Add(new StepPointer
                {
                    chapterIndex = implicitRootChapterIndex,
                    stepIndex = -1,
                    isImplicitRoot = true,
                });

                string implicitRootModuleId = NormalizeModuleId(m_ImplicitRootStep.moduleId);
                if (!string.IsNullOrEmpty(implicitRootModuleId))
                    m_FlatIndexByModuleId[implicitRootModuleId] = 0;

                rootStepCount = 1;
                firstRootFlatIndex = 0;
            }

            for (int c = 0; c < m_Plan.chapters.Count; c++)
            {
                var chapter = m_Plan.chapters[c];
                if (chapter == null || chapter.steps == null || chapter.steps.Count == 0) continue;
                for (int s = 0; s < chapter.steps.Count; s++)
                {
                    int flatIndex = m_FlatSteps.Count;
                    m_FlatSteps.Add(new StepPointer
                    {
                        chapterIndex = c,
                        stepIndex = s,
                        isImplicitRoot = false,
                    });

                    var step = chapter.steps[s];
                    if (step == null) continue;
                    if (IsRootStep(step))
                    {
                        rootStepCount++;
                        if (firstRootFlatIndex < 0)
                            firstRootFlatIndex = flatIndex;
                    }

                    string moduleId = NormalizeModuleId(step.moduleId);
                    if (string.IsNullOrEmpty(moduleId)) continue;
                    if (!m_FlatIndexByModuleId.ContainsKey(moduleId))
                        m_FlatIndexByModuleId[moduleId] = flatIndex;
                }
            }

            if (m_Plan.stageModules != null)
            {
                for (int i = 0; i < m_Plan.stageModules.Count; i++)
                {
                    var stageModule = m_Plan.stageModules[i];
                    if (stageModule == null)
                        continue;

                    string moduleId = NormalizeModuleId(stageModule.moduleId);
                    if (string.IsNullOrEmpty(moduleId))
                        continue;

                    if (m_FlatIndexByModuleId.ContainsKey(moduleId))
                    {
                        Debug.LogWarning(
                            $"[ChapterPlacementDirector] 展台 moduleId 不应出现在步骤链中，已检测到重复配置：{moduleId}",
                            this);
                    }
                }
            }

            if (rootStepCount > 1)
            {
                Debug.LogWarning(
                    $"[ChapterPlacementDirector] 检测到 {rootStepCount} 个 Root 步骤。当前仅支持一个 Root 步骤，并且它必须位于总步骤链第 1 步。",
                    this);
            }

            if (firstRootFlatIndex > 0)
            {
                Debug.LogWarning(
                    $"[ChapterPlacementDirector] Root 步骤必须位于总步骤链第 1 步。当前 Root flatIndex={firstRootFlatIndex}。",
                    this);
            }

            RebuildRuntimeGroups();
        }

        private bool IsStageModuleId(string moduleId)
        {
            moduleId = NormalizeModuleId(moduleId);
            if (string.IsNullOrEmpty(moduleId) || m_Plan == null || m_Plan.stageModules == null)
                return false;

            for (int i = 0; i < m_Plan.stageModules.Count; i++)
            {
                var stageModule = m_Plan.stageModules[i];
                if (stageModule == null)
                    continue;

                if (string.Equals(NormalizeModuleId(stageModule.moduleId), moduleId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private void CollectStageModuleIds(HashSet<string> moduleIds)
        {
            if (moduleIds == null || m_Plan == null || m_Plan.stageModules == null)
                return;

            for (int i = 0; i < m_Plan.stageModules.Count; i++)
            {
                var stageModule = m_Plan.stageModules[i];
                if (stageModule == null)
                    continue;

                string moduleId = NormalizeModuleId(stageModule.moduleId);
                if (!string.IsNullOrEmpty(moduleId))
                    moduleIds.Add(moduleId);
            }
        }

        private void RebuildRuntimeGroups()
        {
            m_RuntimeGroups.Clear();
            m_GroupIndexByFlatIndex.Clear();

            if (m_FlatSteps.Count == 0) return;

            if (!IsGroupingEnabledInModuleMode())
            {
                for (int i = 0; i < m_FlatSteps.Count; i++)
                    m_RuntimeGroups.Add(new List<int> { i });
            }
            else
            {
                for (int i = 0; i < m_FlatSteps.Count; i++)
                {
                    if (!TryGetStepByFlatIndex(i, out var step))
                    {
                        m_RuntimeGroups.Add(new List<int> { i });
                        continue;
                    }

                    string groupKey = NormalizeGroupKey(step.groupKey);
                    if (string.IsNullOrEmpty(groupKey))
                    {
                        m_RuntimeGroups.Add(new List<int> { i });
                        continue;
                    }

                    var group = new List<int> { i };
                    int j = i + 1;
                    while (j < m_FlatSteps.Count)
                    {
                        if (!TryGetStepByFlatIndex(j, out var nextStep)) break;
                        string nextKey = NormalizeGroupKey(nextStep.groupKey);
                        if (!string.Equals(groupKey, nextKey, StringComparison.OrdinalIgnoreCase)) break;
                        group.Add(j);
                        j++;
                    }

                    m_RuntimeGroups.Add(group);
                    i = j - 1;
                }
            }

            for (int g = 0; g < m_RuntimeGroups.Count; g++)
            {
                var group = m_RuntimeGroups[g];
                for (int i = 0; i < group.Count; i++)
                    m_GroupIndexByFlatIndex[group[i]] = g;
            }
        }

        private bool TryGetCurrentStep(out ChapterPlacementChapter chapter, out ChapterPlacementStep step, out int flatIndex)
        {
            chapter = null;
            step = null;
            flatIndex = -1;

            if (!IsGuidedModeReady || m_FlatSteps.Count == 0)
            {
                PublishStatus("引导不可用：请检查 ChapterPlacementPlan 配置。");
                return false;
            }

            if (m_CurrentFlatIndex < 0) m_CurrentFlatIndex = 0;
            if (m_CurrentFlatIndex >= m_FlatSteps.Count) m_CurrentFlatIndex = m_FlatSteps.Count - 1;

            flatIndex = m_CurrentFlatIndex;
            if (!TryResolveFlatStep(flatIndex, out chapter, out step))
            {
                PublishStatus("引导清单损坏：步骤为空。");
                return false;
            }

            return true;
        }

        private bool TryGetStepByFlatIndex(int flatIndex, out ChapterPlacementStep step)
        {
            step = null;
            return TryResolveFlatStep(flatIndex, out _, out step);
        }

        private bool TryResolveFlatStep(int flatIndex, out ChapterPlacementChapter chapter, out ChapterPlacementStep step)
        {
            chapter = null;
            step = null;
            if (flatIndex < 0 || flatIndex >= m_FlatSteps.Count)
                return false;

            var pointer = m_FlatSteps[flatIndex];
            if (m_Plan == null || m_Plan.chapters == null)
                return false;

            if (pointer.chapterIndex < 0 || pointer.chapterIndex >= m_Plan.chapters.Count)
                return false;

            chapter = m_Plan.chapters[pointer.chapterIndex];
            if (chapter == null)
                return false;

            if (pointer.isImplicitRoot)
            {
                step = m_ImplicitRootStep;
                return true;
            }

            if (chapter.steps == null || pointer.stepIndex < 0 || pointer.stepIndex >= chapter.steps.Count)
                return false;

            step = chapter.steps[pointer.stepIndex];
            return step != null;
        }

        private bool MoveStep(int delta)
        {
            if (m_FlatSteps.Count == 0) return false;
            int next = m_CurrentFlatIndex + delta;
            if (next < 0 || next >= m_FlatSteps.Count) return false;
            m_IsRootNavigationActive = false;
            m_CurrentFlatIndex = next;
            return true;
        }

        private int GetPlacementNavigationUpperBoundInclusive()
        {
            if (m_FlatSteps.Count == 0)
                return -1;

            int highestPlaced = -1;
            foreach (int flatIndex in m_PlacedSteps)
            {
                if (flatIndex > highestPlaced)
                    highestPlaced = flatIndex;
            }

            int firstUnplaced = -1;
            for (int i = 0; i < m_FlatSteps.Count; i++)
            {
                if (m_PlacedSteps.Contains(i))
                    continue;

                firstUnplaced = i;
                break;
            }

            int upperBound = highestPlaced;
            if (firstUnplaced > upperBound)
                upperBound = firstUnplaced;

            if (upperBound < 0)
                upperBound = 0;

            if (upperBound >= m_FlatSteps.Count)
                upperBound = m_FlatSteps.Count - 1;

            return upperBound;
        }

        private bool MoveToNextGroup()
        {
            int next = m_CurrentGroupIndex + 1;
            if (next < 0 || next >= m_RuntimeGroups.Count) return false;
            m_CurrentGroupIndex = next;
            var group = m_RuntimeGroups[m_CurrentGroupIndex];
            if (group.Count == 0) return false;
            m_CurrentFlatIndex = group[0];
            return true;
        }

        private void SyncCurrentStepToSpawner()
        {
            if (m_Spawner == null) return;
            bool selectRoot = IsLegacyRootNavigationActive;
            if (!selectRoot &&
                IsPlacementMode &&
                TryGetCurrentStep(out _, out var currentStep, out _) &&
                IsRootStep(currentStep))
            {
                selectRoot = true;
            }

            m_Spawner.SetRootManipulationEnabled(selectRoot);

            if (selectRoot)
            {
                m_Spawner.SelectRootMarker();
                return;
            }

            if (!TryGetCurrentStep(out _, out var step, out _)) return;
            m_Spawner.SelectByModuleId(step.moduleId);
        }

        private void ResetPlaybackProgressToFirstGroup()
        {
            m_PlaybackCompletedSteps.Clear();
            m_CurrentGroupIndex = 0;
            m_CurrentFlatIndex = 0;
            m_IsRootNavigationActive = false;
            GlobalSystemRuntime.StopAllAndResetAll();

            if (m_RuntimeGroups.Count > 0 && m_RuntimeGroups[0].Count > 0)
                m_CurrentFlatIndex = m_RuntimeGroups[0][0];
        }

        private void SyncCurrentGroupToCurrentStep()
        {
            int groupIndex = GetGroupIndexByFlatIndex(m_CurrentFlatIndex);
            if (groupIndex >= 0)
            {
                m_CurrentGroupIndex = groupIndex;
                return;
            }

            m_CurrentGroupIndex = 0;
            if (m_RuntimeGroups.Count > 0 && m_RuntimeGroups[0].Count > 0)
                m_CurrentFlatIndex = m_RuntimeGroups[0][0];
        }

        private List<int> GetGateTargetGroup()
        {
            if (m_RuntimeGroups.Count == 0)
                return new List<int>();

            if (!m_IsRestoredPlaybackMode && m_ResetToFirstStepWhenSessionRestored)
                return m_RuntimeGroups[0];

            if (m_CurrentGroupIndex >= 0 && m_CurrentGroupIndex < m_RuntimeGroups.Count)
                return m_RuntimeGroups[m_CurrentGroupIndex];

            return m_RuntimeGroups[0];
        }

        private bool TryPrepareCurrentChapterPlacements(out List<string> pendingModuleNames, out string chapterDisplayName)
        {
            pendingModuleNames = new List<string>();
            chapterDisplayName = string.Empty;

            if (!TryGetCurrentStep(out _, out _, out int flatIndex))
                return false;

            if (!TryGetChapterIndexByFlatIndex(flatIndex, out int chapterIndex))
                return false;

            return TryPrepareChapterPlacements(
                chapterIndex,
                includeStageModules: true,
                out pendingModuleNames,
                out chapterDisplayName);
        }

        private void PreloadNextChapterPlacementsIfNeeded()
        {
            if (m_Spawner == null ||
                m_CurrentGroupIndex < 0 ||
                m_CurrentGroupIndex >= m_RuntimeGroups.Count - 1)
            {
                return;
            }

            var currentGroup = m_RuntimeGroups[m_CurrentGroupIndex];
            if (currentGroup == null || currentGroup.Count == 0)
                return;

            var nextGroup = m_RuntimeGroups[m_CurrentGroupIndex + 1];
            if (nextGroup == null || nextGroup.Count == 0)
                return;

            int currentFlatIndex = currentGroup[currentGroup.Count - 1];
            int nextFlatIndex = nextGroup[0];
            if (!TryGetChapterIndexByFlatIndex(currentFlatIndex, out int currentChapterIndex) ||
                !TryGetChapterIndexByFlatIndex(nextFlatIndex, out int nextChapterIndex) ||
                nextChapterIndex <= currentChapterIndex)
            {
                return;
            }

            TryPrepareChapterPlacements(
                nextChapterIndex,
                includeStageModules: false,
                out _,
                out _);
        }

        private bool TryPrepareChapterPlacements(
            int chapterIndex,
            bool includeStageModules,
            out List<string> pendingModuleNames,
            out string chapterDisplayName)
        {
            pendingModuleNames = new List<string>();
            chapterDisplayName = string.Empty;

            if (m_Spawner == null)
                return true;

            if (!TryGetChapterByIndex(chapterIndex, out var chapter))
                return false;

            chapterDisplayName = chapter != null ? chapter.DisplayName : string.Empty;
            var preparedModuleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (chapter != null && chapter.steps != null)
            {
                for (int i = 0; i < chapter.steps.Count; i++)
                {
                    var step = chapter.steps[i];
                    if (IsRootStep(step))
                        continue;

                    string moduleId = NormalizeModuleId(step != null ? step.moduleId : string.Empty);
                    if (string.IsNullOrEmpty(moduleId) || !preparedModuleIds.Add(moduleId))
                        continue;

                    if (!m_Spawner.TryEnsureExperiencePlacementAvailableByModuleId(moduleId, allowLayoutFallback: true))
                    {
                        string displayName = step != null ? step.DisplayName : moduleId;
                        pendingModuleNames.Add(string.IsNullOrWhiteSpace(displayName) ? moduleId : displayName);
                    }
                }
            }

            if (includeStageModules)
            {
                var stageModuleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                CollectStageModuleIds(stageModuleIds);
                foreach (var stageModuleId in stageModuleIds)
                {
                    if (!preparedModuleIds.Add(stageModuleId))
                        continue;

                    if (!m_Spawner.TryEnsureExperiencePlacementAvailableByModuleId(stageModuleId, allowLayoutFallback: true))
                        pendingModuleNames.Add(stageModuleId);
                }
            }

            return pendingModuleNames.Count == 0;
        }

        private bool TryGetChapterIndexByFlatIndex(int flatIndex, out int chapterIndex)
        {
            chapterIndex = -1;
            if (flatIndex < 0 || flatIndex >= m_FlatSteps.Count)
                return false;

            chapterIndex = m_FlatSteps[flatIndex].chapterIndex;
            return m_Plan != null &&
                   m_Plan.chapters != null &&
                   chapterIndex >= 0 &&
                   chapterIndex < m_Plan.chapters.Count;
        }

        private bool TryGetChapterByIndex(int chapterIndex, out ChapterPlacementChapter chapter)
        {
            chapter = null;
            if (m_Plan == null || m_Plan.chapters == null)
                return false;

            if (chapterIndex < 0 || chapterIndex >= m_Plan.chapters.Count)
                return false;

            chapter = m_Plan.chapters[chapterIndex];
            return chapter != null;
        }

        private bool IsGroupingEnabledInModuleMode()
        {
            return IsExperienceMode;
        }

        private int GetGroupIndexByFlatIndex(int flatIndex)
        {
            if (m_GroupIndexByFlatIndex.TryGetValue(flatIndex, out int groupIndex))
                return groupIndex;
            return -1;
        }

        private bool IsCurrentGroupCompleted()
        {
            if (m_CurrentGroupIndex < 0 || m_CurrentGroupIndex >= m_RuntimeGroups.Count) return false;
            var group = m_RuntimeGroups[m_CurrentGroupIndex];
            for (int i = 0; i < group.Count; i++)
            {
                if (!m_PlaybackCompletedSteps.Contains(group[i])) return false;
            }
            return true;
        }

        public bool TryGetGuidePreviewFlatIndex(string moduleId, out int flatIndex)
        {
            moduleId = NormalizeModuleId(moduleId);
            if (string.IsNullOrEmpty(moduleId))
            {
                flatIndex = -1;
                return false;
            }

            return m_FlatIndexByModuleId.TryGetValue(moduleId, out flatIndex);
        }

        private static string NormalizeModuleId(string moduleId)
        {
            return string.IsNullOrWhiteSpace(moduleId) ? string.Empty : moduleId.Trim();
        }

        private static string NormalizeGroupKey(string groupKey)
        {
            return string.IsNullOrWhiteSpace(groupKey) ? string.Empty : groupKey.Trim();
        }

        private void PublishGuide()
        {
            if (!TryGetCurrentStep(out var chapter, out var step, out int flatIndex))
                return;

            if (IsPlacementMode)
            {
                if (IsLegacyRootNavigationActive)
                {
                    PublishStatus("[布展模式] 当前为定位点 | 可直接抓取调整，或点击“下个展品”返回第一个展品。");
                    return;
                }

                if (IsRootStep(step))
                {
                    string rootStatus = m_Spawner == null || !m_Spawner.HasRootMarker
                        ? " (待放置)"
                        : !m_Spawner.HasSavedRootAnchor
                            ? " (待保存)"
                            : string.Empty;
                    PublishStatus(
                        $"[布展模式] 当前定位点 | {step.DisplayName}{rootStatus}");
                    return;
                }

                if (m_Spawner != null && !m_Spawner.HasRootMarker)
                {
                    PublishStatus("[布展模式] 定位点未创建 | 请先完成第 1 步“定位点”。");
                    return;
                }

                string stepStatus = string.Empty;
                if (m_PlacedSteps.Contains(flatIndex) && !m_SavedSteps.Contains(flatIndex))
                    stepStatus = " (待保存)";
                else if (!m_PlacedSteps.Contains(flatIndex))
                    stepStatus = " (待放置)";

                PublishStatus(
                    $"[布展模式] 当前展品 {GetPlacementExhibitProgressLabel(flatIndex)} | " +
                    $"{chapter.DisplayName} / {step.DisplayName}{stepStatus}");
                return;
            }

            string playbackStatus = m_PlaybackCompletedSteps.Contains(flatIndex) ? "已完成" : "待体验";
            PublishStatus(
                $"[体验模式] 当前 {m_CurrentFlatIndex + 1}/{m_FlatSteps.Count} | 已完成 {m_PlaybackCompletedSteps.Count}/{m_FlatSteps.Count} | " +
                $"{chapter.DisplayName} / {step.DisplayName} ({playbackStatus})");
        }

        private void PublishStatus(string message)
        {
            if (m_Spawner != null && m_Spawner.onStatusMessage != null)
                m_Spawner.onStatusMessage.Invoke(message);
            else
                Debug.Log(message, this);
        }

        private string GetPlacementExhibitProgressLabel(int flatIndex)
        {
            if (TryGetExhibitOrdinalForFlatIndex(flatIndex, out int exhibitNumber, out int totalExhibits) &&
                exhibitNumber > 0 &&
                totalExhibits > 0)
            {
                return $"{exhibitNumber}/{totalExhibits}";
            }

            return $"{m_CurrentFlatIndex + 1}/{m_FlatSteps.Count}";
        }
    }
}
