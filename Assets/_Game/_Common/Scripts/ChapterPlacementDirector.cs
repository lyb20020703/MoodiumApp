using System;
using System.Collections.Generic;
using Interaction;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace VFXViewer
{
    /// <summary>
    /// 预配置清单驱动的布展引导器：
    /// 现场只需按顺序「放置 -> 保存」，无需手动选择展品。
    /// </summary>
    public partial class ChapterPlacementDirector : MonoBehaviour
    {
        [Header("核心引用")]
        [SerializeField] private ExhibitPlacementManager m_Spawner;
        [SerializeField] private ChapterPlacementPlan m_Plan;
        [SerializeField] private InteractionModuleManager m_ModuleManager;

        [Header("应用模式")]
        [SerializeField] private ExhibitAppMode m_StartAppMode = ExhibitAppMode.Placement;
        [SerializeField] private bool m_ResetToFirstStepWhenExperienceEntered = true;

        [Header("流程设置")]
        [SerializeField] private bool m_AutoAdvanceAfterSave = true;
        [SerializeField] private bool m_SelectCurrentStepOnStart = true;

        [Header("模块测试模式")]
        [SerializeField] private bool m_EnableModuleAutoAdvanceWithoutSpawner = true;
        [SerializeField] private bool m_AutoShowCurrentModuleOnStepEntered = true;
        [SerializeField] private bool m_AutoHideNonCurrentModulesInModuleMode = true;

        [Header("恢复播放模式")]
        [SerializeField] private bool m_EnableRestoredModulePlayback = true;
        [SerializeField] private bool m_SoftRestartRestoreWhenExperienceEntered = true;
        [SerializeField] private bool m_AutoCreateModuleManagerForRestoredPlayback = true;
        [SerializeField] private bool m_ResetToFirstStepWhenSessionRestored = true;

        [Header("恢复就绪门槛")]
        [SerializeField] private bool m_WaitForRootRestoreBeforePlayback = true;
        [SerializeField] private bool m_WaitForCurrentGroupModulesBeforePlayback = true;
        [SerializeField] private bool m_ReportRestoreGateProgress = true;

        private readonly List<StepPointer> m_FlatSteps = new List<StepPointer>();
        private readonly HashSet<int> m_PlacedSteps = new HashSet<int>();
        private readonly HashSet<int> m_SavedSteps = new HashSet<int>();
        private readonly HashSet<int> m_PlaybackCompletedSteps = new HashSet<int>();
        private readonly Dictionary<string, int> m_FlatIndexByModuleId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly List<List<int>> m_RuntimeGroups = new List<List<int>>();
        private readonly Dictionary<int, int> m_GroupIndexByFlatIndex = new Dictionary<int, int>();
        private readonly Dictionary<int, bool> m_GuideInteractableInitialStates = new Dictionary<int, bool>();
        private readonly Dictionary<int, bool> m_GuideColliderInitialStates = new Dictionary<int, bool>();
        private readonly ChapterPlacementStep m_ImplicitRootStep = new ChapterPlacementStep
        {
            stepKind = ChapterPlacementStepKind.Root,
            moduleId = "RootStep",
            moduleName = "定位点",
            groupKey = string.Empty,
            prefab = null,
            spawnDistance = 1f,
            fixedInstanceId = string.Empty,
        };
        private int m_CurrentFlatIndex;
        private int m_CurrentGroupIndex;
        private ExhibitAppMode m_CurrentAppMode = ExhibitAppMode.Placement;
        private bool m_IsRootNavigationActive;
        private bool m_IsExperiencePlaybackActive;
        private bool m_IsRestoredPlaybackMode;
        private bool m_HasInitializedAppMode;
        private bool m_HasCompletedRootStepPlaybackThisSession;
        private bool m_HasStartedRootStepPlaybackThisSession;
        private bool m_HasAcceptedExperienceRootCardThisSession;
        private string m_LastRestoreGateMessage = string.Empty;
        private bool m_BlockGlobalGuideCommandsDuringTransition;
        private string m_GlobalGuideCommandBlockReason = string.Empty;
        private Coroutine m_DeferredPlacementPreviewRefreshCoroutine;
        private bool m_DeferPlacementProgressRefreshUntilFinalPass;
        private GuidedPlacementFlow m_GuidedPlacementFlow;
        private ModulePlaybackFlow m_ModulePlaybackFlow;
        private RestoredPlaybackFlow m_RestoredPlaybackFlow;

        public event Action<ExhibitAppMode> AppModeChanged;

        private struct StepPointer
        {
            public int chapterIndex;
            public int stepIndex;
            public bool isImplicitRoot;
        }

        public bool IsGuidedModeReady => m_Plan != null && m_Plan.HasAnyStep;
        public bool IsPlacementActionsReady => IsGuidedModeReady && m_Spawner != null;
        public int TotalSteps => m_FlatSteps.Count;
        public int SavedStepsCount => m_SavedSteps.Count;
        public ExhibitAppMode CurrentAppMode => m_CurrentAppMode;
        public ChapterPlacementPlan Plan => m_Plan;
        public bool IsPlacementMode => m_CurrentAppMode == ExhibitAppMode.Placement;
        public bool IsExperienceMode => m_CurrentAppMode == ExhibitAppMode.Experience;
        public bool IsRootNavigationActive => IsLegacyRootNavigationActive;
        internal bool ShouldIgnoreExperienceRootCardDetections => IsExperienceMode && m_HasAcceptedExperienceRootCardThisSession;
        internal bool ShouldKeepRecognitionZoneVisibleAfterExperienceAcceptance =>
            IsExperienceMode && m_HasAcceptedExperienceRootCardThisSession && !m_HasStartedRootStepPlaybackThisSession;
        private bool IsModuleOnlyTestModeActive => IsExperienceMode && m_Spawner == null && m_EnableModuleAutoAdvanceWithoutSpawner;
        private bool IsExperienceScenePlaybackActive => IsExperienceMode && m_Spawner != null && m_IsExperiencePlaybackActive;
        private bool IsRestoredModulePlaybackActive => IsExperienceScenePlaybackActive && m_IsRestoredPlaybackMode;
        private bool IsModuleDrivenVisibilityActive => IsModuleOnlyTestModeActive || IsExperienceScenePlaybackActive;
        private bool IsLegacyRootNavigationActive => IsPlacementMode && m_IsRootNavigationActive && !HasConfiguredRootStep();

        internal bool UsesSpawner(ExhibitPlacementManager spawner)
        {
            return m_Spawner == spawner;
        }

        private void EnsureFlows()
        {
            if (m_GuidedPlacementFlow == null)
                m_GuidedPlacementFlow = new GuidedPlacementFlow(this);
            if (m_ModulePlaybackFlow == null)
                m_ModulePlaybackFlow = new ModulePlaybackFlow(this);
            if (m_RestoredPlaybackFlow == null)
                m_RestoredPlaybackFlow = new RestoredPlaybackFlow(this);
        }

        private bool IsRootStep(ChapterPlacementStep step)
        {
            return step != null && step.stepKind == ChapterPlacementStepKind.Root;
        }

        private bool ShouldInjectImplicitRootStep()
        {
            if (m_Plan == null || m_Plan.rootSettings == null || m_Plan.rootSettings.markerPrefab == null)
                return false;

            return !PlanContainsExplicitRootStep();
        }

        private bool PlanContainsExplicitRootStep()
        {
            if (m_Plan == null || m_Plan.chapters == null)
                return false;

            for (int chapterIndex = 0; chapterIndex < m_Plan.chapters.Count; chapterIndex++)
            {
                var chapter = m_Plan.chapters[chapterIndex];
                if (chapter == null || chapter.steps == null)
                    continue;

                for (int stepIndex = 0; stepIndex < chapter.steps.Count; stepIndex++)
                {
                    if (IsRootStep(chapter.steps[stepIndex]))
                        return true;
                }
            }

            return false;
        }

        private bool TryGetImplicitRootHostChapterIndex(out int chapterIndex)
        {
            chapterIndex = -1;
            if (m_Plan == null || m_Plan.chapters == null)
                return false;

            for (int i = 0; i < m_Plan.chapters.Count; i++)
            {
                var chapter = m_Plan.chapters[i];
                if (chapter != null && chapter.steps != null && chapter.steps.Count > 0)
                {
                    chapterIndex = i;
                    return true;
                }
            }

            for (int i = 0; i < m_Plan.chapters.Count; i++)
            {
                if (m_Plan.chapters[i] == null)
                    continue;

                chapterIndex = i;
                return true;
            }

            return false;
        }

        private bool IsRootStepFlatIndex(int flatIndex)
        {
            return TryGetStepByFlatIndex(flatIndex, out var step) && IsRootStep(step);
        }

        private bool TryGetRootStepFlatIndex(out int flatIndex)
        {
            flatIndex = -1;
            for (int i = 0; i < m_FlatSteps.Count; i++)
            {
                if (!IsRootStepFlatIndex(i))
                    continue;

                flatIndex = i;
                return true;
            }

            return false;
        }

        private bool HasConfiguredRootStep()
        {
            return TryGetRootStepFlatIndex(out _);
        }

        private bool IsRootStepModuleId(string moduleId)
        {
            moduleId = NormalizeModuleId(moduleId);
            if (string.IsNullOrEmpty(moduleId))
                return false;

            if (!TryGetRootStepFlatIndex(out int rootFlatIndex) ||
                !TryGetStepByFlatIndex(rootFlatIndex, out var rootStep) ||
                rootStep == null)
            {
                return false;
            }

            return string.Equals(
                NormalizeModuleId(rootStep.moduleId),
                moduleId,
                StringComparison.OrdinalIgnoreCase);
        }

        private bool TrySelectConfiguredRootStep()
        {
            if (!TryGetRootStepFlatIndex(out int rootFlatIndex))
                return false;

            m_IsRootNavigationActive = false;
            m_CurrentFlatIndex = rootFlatIndex;
            SyncCurrentStepToSpawner();
            return true;
        }

        private bool TryGetFirstExhibitFlatIndex(out int flatIndex)
        {
            flatIndex = -1;
            for (int i = 0; i < m_FlatSteps.Count; i++)
            {
                if (IsRootStepFlatIndex(i))
                    continue;

                flatIndex = i;
                return true;
            }

            return false;
        }

        private int GetExhibitStepCount()
        {
            int count = 0;
            for (int i = 0; i < m_FlatSteps.Count; i++)
            {
                if (!IsRootStepFlatIndex(i))
                    count++;
            }

            return count;
        }

        private bool TryGetExhibitOrdinalForFlatIndex(int flatIndex, out int stepNumber, out int totalSteps)
        {
            stepNumber = 0;
            totalSteps = GetExhibitStepCount();
            if (flatIndex < 0 || totalSteps <= 0)
                return false;

            int exhibitIndex = 0;
            for (int i = 0; i < m_FlatSteps.Count; i++)
            {
                if (IsRootStepFlatIndex(i))
                    continue;

                exhibitIndex++;
                if (i != flatIndex)
                    continue;

                stepNumber = exhibitIndex;
                return true;
            }

            return false;
        }

        public bool TrySelectFirstExhibitStep()
        {
            if (!TryGetFirstExhibitFlatIndex(out int exhibitFlatIndex))
                return false;

            m_IsRootNavigationActive = false;
            m_CurrentFlatIndex = exhibitFlatIndex;
            SyncCurrentStepToSpawner();
            return true;
        }

        public bool EnsureExhibitStepSelectedForPlacementUi()
        {
            if (!IsPlacementMode || !HasConfiguredRootStep())
                return m_FlatSteps.Count > 0;

            if (!TryGetCurrentStep(out _, out var currentStep, out _))
                return TrySelectFirstExhibitStep();

            if (!IsRootStep(currentStep))
                return true;

            return TrySelectFirstExhibitStep();
        }

        public bool CanAcceptRootCardPlacementUpdate()
        {
            if (!IsPlacementMode)
                return false;

            if (IsLegacyRootNavigationActive)
                return true;

            if (!TryGetCurrentStep(out _, out var currentStep, out _))
                return false;

            return IsRootStep(currentStep);
        }

        private void SyncRootStepProgress(int flatIndex)
        {
            if (flatIndex < 0)
                return;

            if (m_Spawner != null && m_Spawner.HasRootMarker)
                m_PlacedSteps.Add(flatIndex);
            else
                m_PlacedSteps.Remove(flatIndex);

            if (m_Spawner != null && m_Spawner.HasSavedRootAnchor)
                m_SavedSteps.Add(flatIndex);
            else
                m_SavedSteps.Remove(flatIndex);
        }

        private bool TryGetRootStepStatus(out ChapterPlacementStepStatus status)
        {
            status = ChapterPlacementStepStatus.Unknown;
            if (!IsPlacementMode || m_Spawner == null)
                return false;

            if (!m_Spawner.HasRootMarker)
                status = ChapterPlacementStepStatus.RootMissing;
            else if (!m_Spawner.HasSavedRootAnchor)
                status = ChapterPlacementStepStatus.RootUnsaved;
            else
                status = ChapterPlacementStepStatus.RootSaved;

            return true;
        }

        private void OnEnable()
        {
            EnsureFlows();
            m_RestoredPlaybackFlow.OnEnable();
            m_ModulePlaybackFlow.OnEnable();
        }

        private void OnDisable()
        {
            CancelDeferredPlacementPreviewRefresh();
            CancelExperienceBootstrapReadinessWatch();
            m_RestoredPlaybackFlow?.OnDisable();
            m_ModulePlaybackFlow?.OnDisable();
        }

        private void Start()
        {
            EnsureFlows();
            RebuildStepCache();
            if (!IsGuidedModeReady)
            {
                PublishStatus("引导未启用：请绑定 ChapterPlacementPlan。");
                return;
            }

            if (m_SelectCurrentStepOnStart && m_Spawner != null)
                SyncCurrentStepToSpawner();

            SetAppMode(ResolveStartupAppMode(), publishStatus: false);
        }

        internal void BlockGlobalGuideCommands(string reason)
        {
            reason = string.IsNullOrWhiteSpace(reason) ? "unspecified" : reason.Trim();
            if (m_BlockGlobalGuideCommandsDuringTransition &&
                string.Equals(m_GlobalGuideCommandBlockReason, reason, StringComparison.Ordinal))
                return;

            m_BlockGlobalGuideCommandsDuringTransition = true;
            m_GlobalGuideCommandBlockReason = reason;
            PlacementDebugFileLogger.Log(
                $"[GuideActionGate] transition_block begin reason={reason}, " +
                $"mode={m_CurrentAppMode}, currentGroup={m_CurrentGroupIndex}, currentFlat={m_CurrentFlatIndex}, " +
                $"currentGroupModules={BuildCurrentGroupModuleSummary()}");
        }

        internal void ReleaseGlobalGuideCommands(string reason)
        {
            string normalizedReason = string.IsNullOrWhiteSpace(reason) ? "unspecified" : reason.Trim();
            if (!m_BlockGlobalGuideCommandsDuringTransition &&
                string.IsNullOrEmpty(m_GlobalGuideCommandBlockReason))
                return;

            PlacementDebugFileLogger.Log(
                $"[GuideActionGate] transition_block end reason={normalizedReason}, " +
                $"previousReason={m_GlobalGuideCommandBlockReason}, mode={m_CurrentAppMode}, " +
                $"currentGroup={m_CurrentGroupIndex}, currentFlat={m_CurrentFlatIndex}, " +
                $"currentGroupModules={BuildCurrentGroupModuleSummary()}");

            m_BlockGlobalGuideCommandsDuringTransition = false;
            m_GlobalGuideCommandBlockReason = string.Empty;
        }

        internal static bool TryAuthorizeGlobalGuideCommand(
            InteractionModule module,
            out string reason)
        {
            reason = string.Empty;
            if (!Application.isPlaying || module == null)
                return true;

            var director = FindFirstObjectByType<ChapterPlacementDirector>(FindObjectsInactive.Include);
            if (director == null)
                return true;

            return director.CanModuleDriveGlobalGuide(module, out reason);
        }

        private bool CanModuleDriveGlobalGuide(InteractionModule module, out string reason)
        {
            reason = string.Empty;
            if (module == null)
                return true;

            if (IsPlacementMode)
                return true;

            if (m_BlockGlobalGuideCommandsDuringTransition)
            {
                reason = $"transition_blocked:{m_GlobalGuideCommandBlockReason}";
                return false;
            }

            if (IsModuleOnlyTestModeActive)
                return true;

            if (!IsExperienceMode)
                return true;

            if (!m_IsExperiencePlaybackActive || !IsModuleDrivenVisibilityActive)
            {
                reason = $"experience_playback_inactive mode={m_CurrentAppMode} " +
                    $"active={m_IsExperiencePlaybackActive} restored={m_IsRestoredPlaybackMode}";
                return false;
            }

            string moduleId = NormalizeModuleId(module.ModuleId);
            if (string.IsNullOrEmpty(moduleId))
            {
                reason = "module_id_empty";
                return false;
            }

            GameObject playbackTarget = module.PlaybackTarget;
            if (playbackTarget != null && !playbackTarget.activeInHierarchy)
            {
                reason = $"playback_target_hidden target={playbackTarget.name}";
                return false;
            }

            if (!IsModuleInCurrentPlaybackGroup(moduleId))
            {
                reason = $"not_current_group currentGroupModules={BuildCurrentGroupModuleSummary()}";
                return false;
            }

            return true;
        }

        private bool IsModuleInCurrentPlaybackGroup(string moduleId)
        {
            moduleId = NormalizeModuleId(moduleId);
            if (string.IsNullOrEmpty(moduleId))
                return false;

            if (m_CurrentGroupIndex >= 0 && m_CurrentGroupIndex < m_RuntimeGroups.Count)
            {
                var group = m_RuntimeGroups[m_CurrentGroupIndex];
                for (int i = 0; i < group.Count; i++)
                {
                    if (!TryGetStepByFlatIndex(group[i], out var step) || step == null)
                        continue;

                    if (string.Equals(NormalizeModuleId(step.moduleId), moduleId, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            return TryGetCurrentStep(out _, out var currentStep, out _) &&
                   currentStep != null &&
                   string.Equals(NormalizeModuleId(currentStep.moduleId), moduleId, StringComparison.OrdinalIgnoreCase);
        }

        private string BuildCurrentGroupModuleSummary()
        {
            if (m_CurrentGroupIndex < 0 || m_CurrentGroupIndex >= m_RuntimeGroups.Count)
            {
                if (TryGetCurrentStep(out _, out var currentStep, out _) && currentStep != null)
                {
                    string currentModuleId = NormalizeModuleId(currentStep.moduleId);
                    return string.IsNullOrEmpty(currentModuleId) ? "none" : currentModuleId;
                }

                return "none";
            }

            var moduleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var group = m_RuntimeGroups[m_CurrentGroupIndex];
            for (int i = 0; i < group.Count; i++)
            {
                if (!TryGetStepByFlatIndex(group[i], out var step) || step == null)
                    continue;

                string moduleId = NormalizeModuleId(step.moduleId);
                if (!string.IsNullOrEmpty(moduleId))
                    moduleIds.Add(moduleId);
            }

            return moduleIds.Count > 0 ? string.Join("|", moduleIds) : "none";
        }

        private void RefreshGuideTouchInteractivity()
        {
            var guideTouchModules = CollectGuideTouchModuleUsagesForCurrentGroup();
            bool shouldEnable = IsExperienceMode && guideTouchModules.Count > 0;
            var processedRoots = new HashSet<int>();
            var guideStates = new List<string>();

            var guideRuntimes = FindObjectsByType<GlobalGuideRuntime>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (int i = 0; i < guideRuntimes.Length; i++)
            {
                var runtime = guideRuntimes[i];
                if (runtime == null)
                    continue;

                ApplyGuideTouchInteractivityToRoot(runtime.transform, shouldEnable, processedRoots, guideStates);
            }

            var guideNames = CollectGuideObjectNamesFromPlan();
            for (int i = 0; i < guideNames.Count; i++)
            {
                string guideName = guideNames[i];
                if (string.IsNullOrWhiteSpace(guideName))
                    continue;

                for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
                {
                    var scene = SceneManager.GetSceneAt(sceneIndex);
                    if (!scene.IsValid() || !scene.isLoaded)
                        continue;

                    var roots = scene.GetRootGameObjects();
                    for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
                    {
                        var root = roots[rootIndex];
                        if (root == null)
                            continue;

                        var guideRoot = FindChildByNameRecursive(root.transform, guideName);
                        if (guideRoot != null)
                            ApplyGuideTouchInteractivityToRoot(guideRoot, shouldEnable, processedRoots, guideStates);
                    }
                }
            }

            LogGuideTouchDiagnostics("RefreshGuideTouchInteractivity", shouldEnable, guideTouchModules, guideStates);
        }

        private void PrepareGuideRuntimesForModeTransition(bool visible)
        {
            var guideRuntimes = FindObjectsByType<GlobalGuideRuntime>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (int i = 0; i < guideRuntimes.Length; i++)
            {
                var runtime = guideRuntimes[i];
                if (runtime == null)
                    continue;

                runtime.PrepareForAppModeTransition(visible);
            }

            GlobalSystemRuntime.StopAllAndResetAll();
        }

        private void ScheduleDeferredPlacementPreviewRefresh()
        {
            CancelDeferredPlacementPreviewRefresh();

            if (!isActiveAndEnabled || m_Spawner == null)
                return;

            m_DeferredPlacementPreviewRefreshCoroutine = StartCoroutine(DeferredPlacementPreviewRefreshRoutine());
        }

        private void CancelDeferredPlacementPreviewRefresh()
        {
            if (m_DeferredPlacementPreviewRefreshCoroutine == null)
            {
                m_DeferPlacementProgressRefreshUntilFinalPass = false;
                return;
            }

            StopCoroutine(m_DeferredPlacementPreviewRefreshCoroutine);
            m_DeferredPlacementPreviewRefreshCoroutine = null;
            m_DeferPlacementProgressRefreshUntilFinalPass = false;
        }

        private System.Collections.IEnumerator DeferredPlacementPreviewRefreshRoutine()
        {
            yield return null;
            if (!RunDeferredPlacementPreviewRefreshPass(1))
                yield break;

            yield return new WaitForEndOfFrame();
            if (!RunDeferredPlacementPreviewRefreshPass(2))
                yield break;

            yield return null;
            RunDeferredPlacementPreviewRefreshPass(3);
        }

        private bool RunDeferredPlacementPreviewRefreshPass(int passIndex)
        {
            if (!isActiveAndEnabled || !IsPlacementMode || m_Spawner == null)
            {
                m_DeferredPlacementPreviewRefreshCoroutine = null;
                m_DeferPlacementProgressRefreshUntilFinalPass = false;
                return false;
            }

            bool preserveCurrentSelection = true;
            if (m_DeferPlacementProgressRefreshUntilFinalPass && passIndex >= 3)
            {
                preserveCurrentSelection = false;
                m_DeferPlacementProgressRefreshUntilFinalPass = false;
            }

            m_GuidedPlacementFlow?.RefreshProgressFromScene(preserveCurrentSelection);
            SyncCurrentStepToSpawner();
            m_Spawner.RefreshPlacementPreviewForCurrentMode();
            m_Spawner.LogPlacementPreviewVisibilitySnapshot($"deferred pass {passIndex}");
            Debug.Log(
                $"[PlacementMode] deferred placement preview refresh pass={passIndex}, preserveCurrentSelection={preserveCurrentSelection}",
                this);

            if (passIndex >= 3)
                m_DeferredPlacementPreviewRefreshCoroutine = null;

            return true;
        }

        private bool ShouldEnableGuideTouchInteractivityForCurrentGroup()
        {
            if (!IsExperienceMode || !IsGuidedModeReady)
                return false;

            if (m_CurrentGroupIndex >= 0 && m_CurrentGroupIndex < m_RuntimeGroups.Count)
            {
                var group = m_RuntimeGroups[m_CurrentGroupIndex];
                for (int i = 0; i < group.Count; i++)
                {
                    if (TryGetStepByFlatIndex(group[i], out var groupStep) && StepUsesGuideTouch(groupStep))
                        return true;
                }
            }

            return TryGetCurrentStep(out _, out var currentStep, out _) && StepUsesGuideTouch(currentStep);
        }

        private bool StepUsesGuideTouch(ChapterPlacementStep step)
        {
            if (!TryResolveInteractionModuleForStep(step, out var module) || module == null)
                return false;

            return module.StartTriggerType == TriggerType.TouchGuide ||
                   module.EndTriggerType == TriggerType.TouchGuide;
        }

        private bool TryResolveInteractionModuleForStep(ChapterPlacementStep step, out InteractionModule module)
        {
            module = null;
            if (step == null)
                return false;

            string moduleId = NormalizeModuleId(step.moduleId);
            if (m_ModuleManager != null &&
                !string.IsNullOrEmpty(moduleId) &&
                m_ModuleManager.TryGetModuleById(moduleId, out var runtimeModule) &&
                runtimeModule != null)
            {
                module = runtimeModule;
                return true;
            }

            if (step.prefab == null)
                return false;

            module = step.prefab.GetComponentInChildren<InteractionModule>(true);
            return module != null;
        }

        private List<string> CollectGuideObjectNamesFromPlan()
        {
            var guideNames = new HashSet<string>(StringComparer.Ordinal);
            guideNames.Add("Guider");

            for (int flatIndex = 0; flatIndex < m_FlatSteps.Count; flatIndex++)
            {
                if (!TryGetStepByFlatIndex(flatIndex, out var step))
                    continue;

                if (!TryResolveInteractionModuleForStep(step, out var module) || module == null)
                    continue;

                guideNames.Add(module.GlobalGuideObjectName);
            }

            return new List<string>(guideNames);
        }

        private List<string> CollectGuideTouchModuleUsagesForCurrentGroup()
        {
            var moduleUsages = new List<string>();

            if (!IsExperienceMode || !IsGuidedModeReady)
                return moduleUsages;

            if (m_CurrentGroupIndex >= 0 && m_CurrentGroupIndex < m_RuntimeGroups.Count)
            {
                var group = m_RuntimeGroups[m_CurrentGroupIndex];
                for (int i = 0; i < group.Count; i++)
                {
                    if (!TryGetStepByFlatIndex(group[i], out var step))
                        continue;

                    if (!TryResolveInteractionModuleForStep(step, out var module) || module == null)
                        continue;

                    string usage = DescribeGuideTouchUsage(step, module);
                    if (!string.IsNullOrEmpty(usage))
                        moduleUsages.Add(usage);
                }
            }

            if (moduleUsages.Count == 0 &&
                TryGetCurrentStep(out _, out var currentStep, out _) &&
                TryResolveInteractionModuleForStep(currentStep, out var currentModule) &&
                currentModule != null)
            {
                string usage = DescribeGuideTouchUsage(currentStep, currentModule);
                if (!string.IsNullOrEmpty(usage))
                    moduleUsages.Add(usage);
            }

            return moduleUsages;
        }

        private static string DescribeGuideTouchUsage(ChapterPlacementStep step, InteractionModule module)
        {
            if (step == null || module == null)
                return string.Empty;

            bool usesStart = module.StartTriggerType == TriggerType.TouchGuide;
            bool usesEnd = module.EndTriggerType == TriggerType.TouchGuide;
            if (!usesStart && !usesEnd)
                return string.Empty;

            string moduleId = string.IsNullOrWhiteSpace(step.moduleId) ? module.gameObject.name : step.moduleId.Trim();
            if (usesStart && usesEnd)
                return $"{moduleId}(start,end)";
            if (usesStart)
                return $"{moduleId}(start)";
            return $"{moduleId}(end)";
        }

        private void ApplyGuideTouchInteractivityToRoot(
            Transform guideRoot,
            bool shouldEnable,
            HashSet<int> processedRoots,
            List<string> guideStates)
        {
            if (guideRoot == null)
                return;

            if (!processedRoots.Add(guideRoot.GetInstanceID()))
                return;

            var interactables = guideRoot.GetComponentsInChildren<XRBaseInteractable>(true);
            var interactableStates = new List<string>();
            for (int i = 0; i < interactables.Length; i++)
            {
                var interactable = interactables[i];
                if (interactable == null)
                    continue;

                interactable.enabled = shouldEnable && GetInitialGuideInteractableState(interactable);

                var colliders = interactable.GetComponents<Collider>();
                var colliderStates = new List<string>();
                for (int colliderIndex = 0; colliderIndex < colliders.Length; colliderIndex++)
                {
                    var collider = colliders[colliderIndex];
                    if (collider == null)
                        continue;

                    collider.enabled = shouldEnable && GetInitialGuideColliderState(collider);
                    colliderStates.Add($"{collider.GetType().Name}(enabled={collider.enabled})");
                }

                string colliderSummary = colliderStates.Count > 0 ? string.Join(",", colliderStates) : "none";
                interactableStates.Add(
                    $"{interactable.gameObject.name}(enabled={interactable.enabled},colliders={colliderSummary})");
            }

            if (guideStates != null)
            {
                string interactableSummary = interactableStates.Count > 0 ? string.Join(";", interactableStates) : "none";
                guideStates.Add(
                    $"{guideRoot.name}[activeSelf={guideRoot.gameObject.activeSelf},activeInHierarchy={guideRoot.gameObject.activeInHierarchy}]={interactableSummary}");
            }
        }

        private bool GetInitialGuideInteractableState(XRBaseInteractable interactable)
        {
            int id = interactable.GetInstanceID();
            if (!m_GuideInteractableInitialStates.TryGetValue(id, out bool initialState))
            {
                initialState = interactable.enabled;
                m_GuideInteractableInitialStates[id] = initialState;
            }

            return initialState;
        }

        private bool GetInitialGuideColliderState(Collider collider)
        {
            int id = collider.GetInstanceID();
            if (!m_GuideColliderInitialStates.TryGetValue(id, out bool initialState))
            {
                initialState = collider.enabled;
                m_GuideColliderInitialStates[id] = initialState;
            }

            return initialState;
        }

        private static Transform FindChildByNameRecursive(Transform root, string targetName)
        {
            if (root == null || string.IsNullOrWhiteSpace(targetName))
                return null;

            if (string.Equals(root.name, targetName, StringComparison.Ordinal))
                return root;

            for (int i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                var found = FindChildByNameRecursive(child, targetName);
                if (found != null)
                    return found;
            }

            return null;
        }

        private void LogGuideTouchDiagnostics(
            string reason,
            bool shouldEnable,
            List<string> guideTouchModules,
            List<string> guideStates)
        {
            if (!Application.isPlaying)
                return;

            string currentStepId = "none";
            if (TryGetCurrentStep(out _, out var step, out _))
                currentStepId = string.IsNullOrWhiteSpace(step?.moduleId) ? "unnamed" : step.moduleId.Trim();

            string moduleSummary = guideTouchModules != null && guideTouchModules.Count > 0
                ? string.Join("|", guideTouchModules)
                : "none";
            string guideSummary = guideStates != null && guideStates.Count > 0
                ? string.Join(" | ", guideStates)
                : "none-found";

            Debug.Log(
                $"[GuideTouchDebug] reason={reason}, mode={m_CurrentAppMode}, currentStep={currentStepId}, currentGroup={m_CurrentGroupIndex}, currentFlat={m_CurrentFlatIndex}, shouldEnable={shouldEnable}, touchGuideModules={moduleSummary}, guides={guideSummary}",
                this);
        }
    }
}
