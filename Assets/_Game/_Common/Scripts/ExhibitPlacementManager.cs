using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System;
using System.Text;
using System.Threading.Tasks;
using Autohand;
using Interaction;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace VFXViewer
{
    [System.Serializable]
    public class SessionData
    {
        public string trackableId;
        public string originalTrackableId;
        public string fallbackTrackableId;
        public string instanceID;
        public string moduleId;
        public string displayName;
        public bool placementContentHidden;
    }

    [System.Serializable]
    public class SessionDataList
    {
        public List<SessionData> items = new List<SessionData>();
        public RootSessionData root;
    }

    [System.Serializable]
    public class LayoutData
    {
        public string instanceID;
        public string moduleId;
        public string displayName;
        public Vector3 relativePosition;
        public Quaternion relativeRotation;
        public Vector3 scale;
        public bool placementContentHidden;
    }

    [System.Serializable]
    public class LayoutDataList
    {
        public bool hasSeparateRoot = true;
        public List<LayoutData> items = new List<LayoutData>();
    }

    [RequireComponent(typeof(ARAnchorManager))]
    public partial class ExhibitPlacementManager : MonoBehaviour
    {
        private const string PlacementGrabLayerName = "Placement Grab";
        private const string ExperienceGrabLayerName = "Grabbable";
        private const string PlacementGrabColliderName = "__PlacementGrabCollider";
        private const string PlacementDiagnosticsModuleId = "P00_04_introduction2";
        private const int InitialAnchorRestoreBatchSize = 4;
        private const float PlacementSpawnYawOffsetDegrees = 180f;
        private const float AnchorPoseConflictPositionToleranceMeters = 0.01f;
        private const float AnchorPoseConflictRotationToleranceDegrees = 1f;
        private const float ModuleRestoreVerifiedPositionToleranceMeters = 0.05f;
        private const float ModuleRestoreVerifiedRotationToleranceDegrees = 5f;
        private const float ModuleRestoreWeakPositionToleranceMeters = 0.20f;
        private const float ModuleRestoreWeakRotationToleranceDegrees = 20f;
        private const float RootRestoreVerifiedPositionToleranceMeters = 0.05f;
        private const float RootRestoreVerifiedRotationToleranceDegrees = 5f;
        private const float RootRestoreWeakPositionToleranceMeters = 0.20f;
        private const float RootRestoreWeakRotationToleranceDegrees = 18f;

        [Header("核心配置")]
        [SerializeField] private string m_SessionFileName = "museum_session.json";
        [SerializeField] private string m_LayoutFileName = "museum_layout.json";

        [Header("恢复播放")]
        [SerializeField] private bool m_HideRestoredExhibitsUntilPlayback = true;

        [Header("布展预览")]
        [SerializeField] private bool m_EnablePlacementPreviewInPlacementMode = true;

        [Header("调试")]
        [SerializeField] private bool m_EnableAnchorLifecycleLogs = true;

        [Header("运行时容器")]
        [SerializeField] private Transform m_ModuleContainerRoot;
        [SerializeField] private string m_DefaultModuleContainerName = "ModuleManager";

        [Header("UI 事件")]
        public UnityEvent<string> onPrefabChanged;
        public UnityEvent<string> onStatusMessage;
        public UnityEvent<string> onSelectionChanged;
        public UnityEvent<string> onExhibitRestored = new UnityEvent<string>();
        public event Action<bool> AnchorPoseConflictChanged;
        public event Action RestoreAvailabilityChanged;

        private ARAnchorManager m_AnchorManager;
        private Camera m_MainCamera;
        private ChapterPlacementDirector m_PlanDirector;
        private int m_CurrentSelectedIndex;

        private struct PlacementModuleDefinition
        {
            public string moduleId;
            public string displayName;
            public GameObject prefab;
            public float spawnDistance;
            public bool isStageModule;
        }

        private readonly List<ChapterPlacementStep> m_AvailableSteps = new List<ChapterPlacementStep>();
        private readonly Dictionary<string, ChapterPlacementStep> m_StepByModuleId = new Dictionary<string, ChapterPlacementStep>();
        private readonly Dictionary<string, int> m_IndexByModuleId = new Dictionary<string, int>();
        private readonly Dictionary<string, PlacementModuleDefinition> m_DeployableModuleById =
            new Dictionary<string, PlacementModuleDefinition>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ChapterPlacementStageModule> m_StageModuleByModuleId =
            new Dictionary<string, ChapterPlacementStageModule>(StringComparer.OrdinalIgnoreCase);

        private List<LayoutData> m_CachedLayout = new List<LayoutData>();
        private readonly Dictionary<string, SessionData> m_SessionSnapshotByInstanceId =
            new Dictionary<string, SessionData>(StringComparer.Ordinal);
        private readonly Dictionary<string, LayoutData> m_LayoutSnapshotByInstanceId =
            new Dictionary<string, LayoutData>(StringComparer.Ordinal);
        private readonly HashSet<string> m_DirtySessionInstanceIds =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> m_DirtyLayoutInstanceIds =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> m_RemovedSessionInstanceIds =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> m_RemovedLayoutInstanceIds =
            new HashSet<string>(StringComparer.Ordinal);
        private bool m_HasLayoutLoaded;
        private Transform m_RuntimeRootTrans;
        private int m_NextID = 1;
        private readonly Dictionary<TrackableId, SessionData> m_SessionLookup = new Dictionary<TrackableId, SessionData>();
        private readonly HashSet<int> m_QueuedAnchorNodeInstanceIds = new HashSet<int>();

        private Transform m_ManualSelectedTarget;
        private bool m_IsSaving;
        private bool m_PendingSave;
        private bool m_PendingSaveLayout = true;
        private bool m_HasRestoredSessionExhibits;
        private bool m_HasRestoredRootExhibit;
        private bool m_HasAnchorPoseConflict;
        private string m_AnchorPoseConflictSummary = string.Empty;
        private readonly HashSet<string> m_RestoredModuleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<int> m_PlacementPreviewPrewarmPending = new HashSet<int>();
        private Coroutine m_SoftRestoreRetryCoroutine;
        private Coroutine m_StartupSnapshotLoadCoroutine;
        private Coroutine m_InitialAnchorRestoreCoroutine;
        private readonly List<ARAnchor> m_InitialAnchorRestoreQueue = new List<ARAnchor>();
        private bool m_InitialAnchorRestoreDidRestoreAny;
        private readonly Dictionary<UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor, LayerMask> m_OriginalRayInteractorMasks = new Dictionary<UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor, LayerMask>();
        private readonly Dictionary<UnityEngine.XR.Interaction.Toolkit.Interactors.XRPokeInteractor, LayerMask> m_OriginalPokeInteractorMasks = new Dictionary<UnityEngine.XR.Interaction.Toolkit.Interactors.XRPokeInteractor, LayerMask>();
        private readonly Dictionary<UnityEngine.XR.Interaction.Toolkit.Interactors.NearFarInteractor, bool> m_OriginalNearFarInteractorFarCastingStates = new Dictionary<UnityEngine.XR.Interaction.Toolkit.Interactors.NearFarInteractor, bool>();
        private ExhibitAppMode m_CurrentAppMode = ExhibitAppMode.Placement;
        private ARSessionState m_LastObservedArSessionState = ARSessionState.None;
        private NotTrackingReason m_LastObservedNotTrackingReason = NotTrackingReason.None;
        private bool m_HasLoggedArSessionStatus;
        private bool m_RootManipulationEnabled;
        private bool m_FocusPlacementVisibilityToCurrentTarget;
        private bool m_PendingInitialFocusedContentHide;
        private Coroutine m_LayoutRedeployCoroutine;
        private Action<bool> m_LayoutRedeployCompletedCallback;
        private InteractionModuleManager m_RuntimeModuleManager;
        private string m_PlacementVideoPreviewOwnerInstanceId = string.Empty;
        private string m_PlacementPointCloudPreviewOwnerInstanceId = string.Empty;

        [Header("一键布展")]
        [SerializeField] private float m_LayoutRedeploySpawnIntervalSeconds = 0.2f;

        [Header("体验模式")]
        [SerializeField] private float m_TransientExperienceSpawnIntervalSeconds = 0f;
        [SerializeField] private float m_TransientExperienceAnchorPlaybackGraceSeconds = 1.5f;

        public bool HasRestoredSessionExhibits => m_HasRestoredSessionExhibits;
        public bool HasRestoredRootExhibit => m_HasRestoredRootExhibit;
        public bool HasAnchorPoseConflict => m_HasAnchorPoseConflict;
        public string AnchorPoseConflictSummary => m_AnchorPoseConflictSummary;
        public int RestoredSessionExhibitCount
        {
            get
            {
                int count = 0;
                foreach (var state in m_RestoreStateByInstanceId.Values)
                {
                    if (state != SessionRestoreState.Unresolved &&
                        state != SessionRestoreState.Invalid)
                        count++;
                }

                return count;
            }
        }
        public int ExpectedSessionExhibitCount => m_SessionSnapshotByInstanceId.Count;
        public ExhibitAppMode CurrentAppMode => m_CurrentAppMode;
        public bool IsLayoutRedeployInProgress => m_LayoutRedeployCoroutine != null;
        public static ExhibitPlacementManager Instance { get; private set; }

        public void ConfigureInitialAppMode(ExhibitAppMode mode)
        {
            m_CurrentAppMode = mode;
        }

        public InteractionModuleManager GetOrCreateRuntimeModuleManager()
        {
            if (m_RuntimeModuleManager != null)
                return m_RuntimeModuleManager;

            if (m_ModuleContainerRoot != null)
            {
                m_RuntimeModuleManager = m_ModuleContainerRoot.GetComponent<InteractionModuleManager>();
                if (m_RuntimeModuleManager == null)
                    m_RuntimeModuleManager = m_ModuleContainerRoot.gameObject.AddComponent<InteractionModuleManager>();
                return m_RuntimeModuleManager;
            }

            m_RuntimeModuleManager = FindFirstObjectByType<InteractionModuleManager>(FindObjectsInactive.Include);
            if (m_RuntimeModuleManager != null)
            {
                m_ModuleContainerRoot = m_RuntimeModuleManager.transform;
                return m_RuntimeModuleManager;
            }

            var containerRoot = ResolveModuleContainerRoot(createIfMissing: true);
            if (containerRoot == null)
                return null;

            m_RuntimeModuleManager = containerRoot.GetComponent<InteractionModuleManager>();
            if (m_RuntimeModuleManager == null)
                m_RuntimeModuleManager = containerRoot.gameObject.AddComponent<InteractionModuleManager>();
            return m_RuntimeModuleManager;
        }

        private void Awake()
        {
            Instance = this;
            m_AnchorManager = GetComponent<ARAnchorManager>();
            m_MainCamera = Camera.main;
            PlacementDebugFileLogger.EnsureCreated();
        }

        private void OnEnable()
        {
            SubscribeExhibitRegistry();
            ARSession.stateChanged += OnArSessionStateChanged;
            if (m_AnchorManager != null) m_AnchorManager.anchorsChanged += OnAnchorsChanged;
            LogArSessionStatus("placement_manager_enabled");
        }

        private void OnDisable()
        {
            LogArSessionStatus("placement_manager_disabled");
            CancelLayoutRedeploy("placement_manager_disabled");
            CancelStartupSnapshotLoad();
            CancelInitialAnchorRestore();
            ARSession.stateChanged -= OnArSessionStateChanged;
            if (m_AnchorManager != null) m_AnchorManager.anchorsChanged -= OnAnchorsChanged;
            CancelQueuedSave();
            UnsubscribeExhibitRegistry();
            if (Instance == this)
                Instance = null;
        }

        private void Start()
        {
            if (!BuildStepLookupFromPlan())
            {
                Debug.LogError("请在 ChapterPlacementDirector 上配置 ChapterPlacementPlan，并确保步骤/展台的 moduleId 唯一且 prefab 已配置。");
                return;
            }

            BeginStartupSnapshotLoadAndRestore();
            NotifyUIUpdate();
        }

        private void Update()
        {
            PumpPendingAnchorAttachments();
            PollArSessionTrackingStatusChanges();
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            LogArSessionStatus($"application_pause paused={pauseStatus}");
            if (pauseStatus &&
                m_CurrentAppMode == ExhibitAppMode.Experience &&
                m_HasTransientExperienceBootstrapRootPose)
            {
                LogAnchorLifecycle(
                    $"runtime_anchor_pause_preserved: {BuildTransientExperienceAnchorSummary()}");
            }

            if (!pauseStatus &&
                m_CurrentAppMode == ExhibitAppMode.Experience &&
                m_HasTransientExperienceBootstrapRootPose)
            {
                LogAnchorLifecycle(
                    $"runtime_anchor_resume_begin: {BuildTransientExperienceAnchorSummary()}");
                RefreshTransientExperienceRuntimeAnchorsAfterResume();
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            LogArSessionStatus($"application_focus hasFocus={hasFocus}");
        }

        private void OnApplicationQuit()
        {
            LogArSessionStatus("application_quit");
            if (m_CurrentAppMode == ExhibitAppMode.Experience)
            {
                LogAnchorLifecycle(
                    $"runtime_anchor_quit_clear: {BuildTransientExperienceAnchorSummary()}");
                ClearTransientExperienceRuntime("application_quit");
            }
        }

        private void OnArSessionStateChanged(ARSessionStateChangedEventArgs args)
        {
            LogArSessionStatus($"ar_session_state_changed state={args.state}");
        }

        private void PollArSessionTrackingStatusChanges()
        {
            ARSessionState currentState = ARSession.state;
            NotTrackingReason currentNotTrackingReason = ARSession.notTrackingReason;

            if (!m_HasLoggedArSessionStatus)
            {
                LogArSessionStatus("ar_session_status_initial");
                return;
            }

            if (currentState == m_LastObservedArSessionState &&
                currentNotTrackingReason == m_LastObservedNotTrackingReason)
                return;

            string reason;
            if (currentState != m_LastObservedArSessionState &&
                currentNotTrackingReason != m_LastObservedNotTrackingReason)
                reason = "ar_session_status_changed state+reason";
            else if (currentState != m_LastObservedArSessionState)
                reason = "ar_session_status_changed state";
            else
                reason = "ar_session_status_changed notTrackingReason";

            LogArSessionStatus(reason);
        }

        private void LogArSessionStatus(string eventName)
        {
            m_LastObservedArSessionState = ARSession.state;
            m_LastObservedNotTrackingReason = ARSession.notTrackingReason;
            m_HasLoggedArSessionStatus = true;
            LogAnchorLifecycle($"{eventName}: {DescribeArSessionRestoreContext()}");
        }

        private string DescribeArSessionRestoreContext()
        {
            string rootFailureReason = string.IsNullOrWhiteSpace(m_RootRestoreFailureReason)
                ? "<none>"
                : m_RootRestoreFailureReason;
            string conflictSummary = string.IsNullOrWhiteSpace(m_AnchorPoseConflictSummary)
                ? "<none>"
                : m_AnchorPoseConflictSummary;

            return
                $"frame={Time.frameCount}, realtime={Time.realtimeSinceStartup:F2}, " +
                $"arSessionState={ARSession.state}, notTrackingReason={ARSession.notTrackingReason}, " +
                $"networkReachability={Application.internetReachability}, appMode={m_CurrentAppMode}, " +
                $"trackables={GetAnchorTrackableCount()}, restoredExhibits={RestoredSessionExhibitCount}/{ExpectedSessionExhibitCount}, " +
                $"restoredAnchors={RestoredSessionAnchorCount}/{ExpectedSessionAnchorCount}, restoredRoot={m_HasRestoredRootExhibit}, " +
                $"resolvedRoot={m_HasResolvedRootForPlayback}, rootReady={IsRootReadyForPlayback}, " +
                $"rootState={FormatRootRestoreState(m_RootRestoreState)}, rootConfidence={FormatRestoreConfidence(m_RootRestoreConfidence)}, " +
                $"rootFailure={rootFailureReason}, anchorPoseConflict={m_HasAnchorPoseConflict}, conflictSummary={conflictSummary}";
        }

        public void SetManualSelection(Transform target)
        {
            if (m_ManualSelectedTarget == target)
                return;

            m_ManualSelectedTarget = target;
            if (Application.isPlaying)
                RefreshCurrentExhibitManipulationState();
            UpdateSelectionUI(target, true);
            RefreshPlacementFocusedExhibitVisibility("manual_selection_changed");
        }

        public void ClearManualSelectionOverride(bool notifyUI = true)
        {
            ClearManualSelection(refreshUi: notifyUI);
        }

        private void UpdateSelectionUI(Transform target, bool isManual)
        {
            if (target == null)
            {
                onSelectionChanged?.Invoke("当前选中: 无");
                RefreshPlacementHeavyPreviewOwner(forceApply: true);
                return;
            }

            Transform rootTransform = GetRootTransform();
            if (rootTransform != null && target == rootTransform)
            {
                onSelectionChanged?.Invoke("当前选中: 定位点");
                RefreshPlacementHeavyPreviewOwner(forceApply: true);
                return;
            }

            if (TryParseExhibitInfo(target, out var moduleId, out var name, out var id))
            {
                onSelectionChanged?.Invoke($"当前选中: {name} (ID:{id}, Module:{moduleId})");
                RefreshPlacementHeavyPreviewOwner();
            }
            else
            {
                onSelectionChanged?.Invoke("当前选中: (无法解析名称)");
                RefreshPlacementHeavyPreviewOwner(forceApply: true);
            }
        }

        private void ClearManualSelection(bool refreshUi = true)
        {
            m_ManualSelectedTarget = null;
            if (Application.isPlaying)
                RefreshCurrentExhibitManipulationState();
            if (refreshUi)
                UpdateSelectionUI(null, false);
            RefreshPlacementFocusedExhibitVisibility("manual_selection_cleared");
        }

        private static string NormalizeModuleId(string moduleId)
        {
            return string.IsNullOrWhiteSpace(moduleId) ? string.Empty : moduleId.Trim();
        }

        private ChapterPlacementDirector ResolvePlanDirector()
        {
            if (m_PlanDirector != null)
                return m_PlanDirector;

            var directors = FindObjectsByType<ChapterPlacementDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            ChapterPlacementDirector fallback = null;
            for (int i = 0; i < directors.Length; i++)
            {
                var director = directors[i];
                if (director == null)
                    continue;

                fallback ??= director;
                if (director.UsesSpawner(this))
                {
                    m_PlanDirector = director;
                    return m_PlanDirector;
                }
            }

            if (fallback != null && directors.Length == 1)
            {
                m_PlanDirector = fallback;
                return m_PlanDirector;
            }

            return null;
        }

        private ChapterPlacementPlan ResolvePlacementPlan()
        {
            var director = ResolvePlanDirector();
            return director != null ? director.Plan : null;
        }

        private bool BuildStepLookupFromPlan()
        {
            m_AvailableSteps.Clear();
            m_StepByModuleId.Clear();
            m_IndexByModuleId.Clear();
            m_DeployableModuleById.Clear();
            m_StageModuleByModuleId.Clear();
            m_CurrentSelectedIndex = 0;
            bool hasRootStep = false;

            ChapterPlacementPlan plan = ResolvePlacementPlan();
            if (plan == null)
                return false;

            if (plan.chapters != null)
            {
                for (int c = 0; c < plan.chapters.Count; c++)
                {
                    var chapter = plan.chapters[c];
                    if (chapter == null || chapter.steps == null) continue;

                    for (int s = 0; s < chapter.steps.Count; s++)
                    {
                        var step = chapter.steps[s];
                        if (step == null) continue;

                        string moduleId = NormalizeModuleId(step.moduleId);
                        if (string.IsNullOrEmpty(moduleId))
                        {
                            Debug.LogWarning($"[Spawner] 跳过空 moduleId 步骤：chapter={chapter.DisplayName}, stepIndex={s}", this);
                            continue;
                        }

                        if (step.stepKind == ChapterPlacementStepKind.Root)
                        {
                            hasRootStep = true;
                            if (m_StepByModuleId.ContainsKey(moduleId))
                            {
                                Debug.LogWarning($"[Spawner] Root 步骤 moduleId 重复，后续步骤被忽略：{moduleId}", this);
                                continue;
                            }

                            m_StepByModuleId[moduleId] = step;
                            continue;
                        }

                        if (step.prefab == null)
                        {
                            Debug.LogWarning($"[Spawner] 跳过未配置 prefab 的步骤：moduleId={moduleId}", this);
                            continue;
                        }

                        if (m_StepByModuleId.ContainsKey(moduleId))
                        {
                            Debug.LogWarning($"[Spawner] moduleId 重复，后续步骤被忽略：{moduleId}", this);
                            continue;
                        }

                        int index = m_AvailableSteps.Count;
                        m_AvailableSteps.Add(step);
                        m_StepByModuleId[moduleId] = step;
                        m_IndexByModuleId[moduleId] = index;
                        RegisterDeployableModule(
                            moduleId,
                            step.DisplayName,
                            step.prefab,
                            step.spawnDistance,
                            isStageModule: false);
                    }
                }
            }

            if (plan.stageModules != null)
            {
                for (int i = 0; i < plan.stageModules.Count; i++)
                {
                    var stageModule = plan.stageModules[i];
                    if (stageModule == null)
                        continue;

                    string moduleId = NormalizeModuleId(stageModule.moduleId);
                    if (string.IsNullOrEmpty(moduleId))
                    {
                        Debug.LogWarning($"[Spawner] 跳过空 moduleId 展台：index={i}", this);
                        continue;
                    }

                    if (stageModule.prefab == null)
                    {
                        Debug.LogWarning($"[Spawner] 跳过未配置 prefab 的展台：moduleId={moduleId}", this);
                        continue;
                    }

                    if (m_StageModuleByModuleId.ContainsKey(moduleId))
                    {
                        Debug.LogWarning($"[Spawner] 展台 moduleId 重复，后续配置被忽略：{moduleId}", this);
                        continue;
                    }

                    if (m_StepByModuleId.ContainsKey(moduleId))
                    {
                        Debug.LogWarning($"[Spawner] 展台 moduleId 与步骤冲突，已忽略：{moduleId}", this);
                        continue;
                    }

                    m_StageModuleByModuleId[moduleId] = stageModule;
                    RegisterDeployableModule(
                        moduleId,
                        stageModule.DisplayName,
                        stageModule.prefab,
                        stageModule.spawnDistance,
                        isStageModule: true);
                }
            }

            return hasRootStep || m_AvailableSteps.Count > 0 || m_DeployableModuleById.Count > 0;
        }

        private void RegisterDeployableModule(
            string moduleId,
            string displayName,
            GameObject prefab,
            float spawnDistance,
            bool isStageModule)
        {
            moduleId = NormalizeModuleId(moduleId);
            if (string.IsNullOrEmpty(moduleId) || prefab == null)
                return;

            if (m_DeployableModuleById.ContainsKey(moduleId))
            {
                Debug.LogWarning($"[Spawner] 可部署 moduleId 重复，后续配置被忽略：{moduleId}", this);
                return;
            }

            m_DeployableModuleById[moduleId] = new PlacementModuleDefinition
            {
                moduleId = moduleId,
                displayName = string.IsNullOrWhiteSpace(displayName) ? moduleId : displayName.Trim(),
                prefab = prefab,
                spawnDistance = spawnDistance,
                isStageModule = isStageModule
            };
        }

        private bool TryGetStepByModuleId(string moduleId, out ChapterPlacementStep step)
        {
            return m_StepByModuleId.TryGetValue(NormalizeModuleId(moduleId), out step);
        }

        private bool TryGetDeployableModuleDefinition(string moduleId, out PlacementModuleDefinition definition)
        {
            return m_DeployableModuleById.TryGetValue(NormalizeModuleId(moduleId), out definition);
        }

        public bool IsStageModuleId(string moduleId)
        {
            return m_StageModuleByModuleId.ContainsKey(NormalizeModuleId(moduleId));
        }

        private bool TryGetCurrentStep(out ChapterPlacementStep step)
        {
            step = null;
            if (m_AvailableSteps.Count == 0) return false;
            if (m_CurrentSelectedIndex < 0) m_CurrentSelectedIndex = 0;
            if (m_CurrentSelectedIndex >= m_AvailableSteps.Count) m_CurrentSelectedIndex = m_AvailableSteps.Count - 1;
            step = m_AvailableSteps[m_CurrentSelectedIndex];
            return step != null;
        }

        // =========================================================
        //  自动恢复
        // =========================================================
        private void OnAnchorsChanged(ARAnchorsChangedEventArgs args)
        {
            int addedCount = args.added != null ? args.added.Count : 0;

            if (m_EnableAnchorLifecycleLogs)
            {
                int updatedCount = args.updated != null ? args.updated.Count : 0;
                int removedCount = args.removed != null ? args.removed.Count : 0;
                if (addedCount > 0 || updatedCount > 0 || removedCount > 0)
                    LogAnchorLifecycle($"anchorsChanged added={addedCount} updated={updatedCount} removed={removedCount}");
            }

            RefreshRelevantAnchorPoseHealth("anchorsChanged/pre");
            NoteAddedAnchorsProcessed(addedCount);

            if (args.added != null)
            {
                foreach (var anchor in args.added)
                {
                    if (anchor != null)
                        LogAnchorLifecycle($"anchor_added: {DescribeAnchorState(anchor)}");
                }

                if (m_CurrentAppMode == ExhibitAppMode.Experience)
                    PruneStaleAnchorsForExperience(args.added, "anchorsChanged.added");

                TryCompletePendingAnchorAttachments(args.added);
            }

            if (args.updated != null)
            {
                foreach (var anchor in args.updated)
                {
                    if (anchor != null)
                        LogAnchorLifecycle($"anchor_updated: {DescribeAnchorState(anchor)}");
                }

                TryCompletePendingAnchorAttachments(args.updated);
            }

            if (args.removed != null)
            {
                foreach (var anchor in args.removed)
                {
                    if (anchor == null)
                        continue;

                    LogAnchorLifecycle($"anchor_removed: {DescribeAnchorState(anchor)}");
                    RemovePendingAnchorAttachment(anchor.trackableId, removeAnchorTrackable: false);
                }
            }

            if (ShouldRestoreAnchorsForCurrentMode() && args.added != null)
            {
                foreach (var anchor in args.added)
                    TryRestoreExhibitOnAnchor(anchor);
            }

            if (ShouldRestoreAnchorsForCurrentMode() && args.updated != null)
            {
                foreach (var anchor in args.updated)
                    TryRestoreExhibitOnAnchor(anchor);
            }

            if (ShouldRestoreAnchorsForCurrentMode())
            {
                TryResolveMissingSessionObjectsFromLayout();
                RefreshExperienceRestoreHealthAndRecovery("anchorsChanged");
            }

            RefreshRelevantAnchorPoseHealth("anchorsChanged/post");
        }

        private void CheckExistingAnchors()
        {
            CancelInitialAnchorRestore();
            PumpPendingAnchorAttachments();

            if (!ShouldRestoreAnchorsForCurrentMode())
                return;

            RefreshRelevantAnchorPoseHealth("checkExistingAnchors");
            foreach (var anchor in m_AnchorManager.trackables)
                TryRestoreExhibitOnAnchor(anchor);

            TryResolveMissingSessionObjectsFromLayout();
            RefreshExperienceRestoreHealthAndRecovery("checkExistingAnchors");
        }

        private void BeginInitialAnchorRestore()
        {
            CancelInitialAnchorRestore();
            PumpPendingAnchorAttachments();

            if (!ShouldRestoreAnchorsForCurrentMode() || m_AnchorManager == null)
            {
                CompleteInitialAnchorRestore();
                return;
            }

            RefreshRelevantAnchorPoseHealth("beginInitialAnchorRestore");
            m_InitialAnchorRestoreDidRestoreAny = false;
            m_InitialAnchorRestoreQueue.Clear();

            foreach (var anchor in m_AnchorManager.trackables)
            {
                if (anchor != null)
                    m_InitialAnchorRestoreQueue.Add(anchor);
            }

            if (m_InitialAnchorRestoreQueue.Count == 0)
            {
                CompleteInitialAnchorRestore();
                return;
            }

            TryRestoreRootAnchorImmediatelyFromInitialQueue();
            if (m_InitialAnchorRestoreQueue.Count == 0)
            {
                CompleteInitialAnchorRestore();
                return;
            }

            m_InitialAnchorRestoreCoroutine = StartCoroutine(RestoreInitialAnchorsInBatches());
        }

        private void CancelInitialAnchorRestore()
        {
            if (m_InitialAnchorRestoreCoroutine != null)
            {
                StopCoroutine(m_InitialAnchorRestoreCoroutine);
                m_InitialAnchorRestoreCoroutine = null;
            }

            m_InitialAnchorRestoreQueue.Clear();
            m_InitialAnchorRestoreDidRestoreAny = false;
        }

        private void TryRestoreRootAnchorImmediatelyFromInitialQueue()
        {
            if ((!HasSavedRootSessionTrackableId() && m_RootSessionTrackableId == TrackableId.invalidId) ||
                m_InitialAnchorRestoreQueue.Count == 0)
            {
                return;
            }

            for (int i = 0; i < m_InitialAnchorRestoreQueue.Count; i++)
            {
                var anchor = m_InitialAnchorRestoreQueue[i];
                if (anchor == null ||
                    (!RootSessionContainsTrackableId(anchor.trackableId) &&
                     anchor.trackableId != m_RootSessionTrackableId))
                {
                    continue;
                }

                if (TryRestoreExhibitOnAnchor(anchor, deferPostRestoreRefresh: true))
                    m_InitialAnchorRestoreDidRestoreAny = true;

                m_InitialAnchorRestoreQueue.RemoveAt(i);
                return;
            }
        }

        private IEnumerator RestoreInitialAnchorsInBatches()
        {
            while (m_InitialAnchorRestoreQueue.Count > 0)
            {
                int restoredThisBatch = 0;
                int processedCount = Mathf.Min(InitialAnchorRestoreBatchSize, m_InitialAnchorRestoreQueue.Count);
                for (int i = 0; i < processedCount; i++)
                {
                    int lastIndex = m_InitialAnchorRestoreQueue.Count - 1;
                    var anchor = m_InitialAnchorRestoreQueue[lastIndex];
                    m_InitialAnchorRestoreQueue.RemoveAt(lastIndex);
                    if (anchor == null)
                        continue;

                    if (TryRestoreExhibitOnAnchor(anchor, deferPostRestoreRefresh: true))
                    {
                        restoredThisBatch++;
                        m_InitialAnchorRestoreDidRestoreAny = true;
                    }
                }

                if (restoredThisBatch > 0 && m_InitialAnchorRestoreQueue.Count > 0)
                    RefreshAfterDeferredAnchorRestoreBatch();

                if (m_InitialAnchorRestoreQueue.Count > 0)
                    yield return null;
            }

            m_InitialAnchorRestoreCoroutine = null;
            CompleteInitialAnchorRestore();
        }

        private void CompleteInitialAnchorRestore()
        {
            if (m_InitialAnchorRestoreDidRestoreAny)
                RefreshAfterDeferredAnchorRestoreBatch();

            CleanupOrphanRootAnchors();
            TryResolveMissingSessionObjectsFromLayout();
            RefreshExperienceRestoreHealthAndRecovery("initialAnchorRestoreComplete");
            NotifyUIUpdate();
            m_InitialAnchorRestoreQueue.Clear();
            m_InitialAnchorRestoreDidRestoreAny = false;
        }

        private void BeginStartupSnapshotLoadAndRestore()
        {
            CancelStartupSnapshotLoad();
            m_StartupSnapshotLoadCoroutine = StartCoroutine(LoadStartupSnapshotsAndRestore());
        }

        private void CancelStartupSnapshotLoad()
        {
            if (m_StartupSnapshotLoadCoroutine == null)
                return;

            StopCoroutine(m_StartupSnapshotLoadCoroutine);
            m_StartupSnapshotLoadCoroutine = null;
        }

        private IEnumerator LoadStartupSnapshotsAndRestore()
        {
            string sessionPath = GetSessionFilePath();
            string layoutPath = GetLayoutFilePath();

            Task<string> layoutReadTask = ReadPersistentJsonFileAsync(layoutPath);
            Task<string> sessionReadTask = ReadPersistentJsonFileAsync(sessionPath);
            while (!layoutReadTask.IsCompleted || !sessionReadTask.IsCompleted)
                yield return null;

            m_StartupSnapshotLoadCoroutine = null;

            ApplyLoadedLayoutJson(layoutReadTask.Result);
            if (ShouldLoadSessionSnapshotForCurrentMode())
            {
                ApplyLoadedSessionJson(sessionReadTask.Result, notifyStatusMessage: true);
                BeginInitialAnchorRestore();
            }
            else
            {
                ResetSessionSnapshotState();
                LogAnchorLifecycle(
                    $"startup session restore skipped: mode={m_CurrentAppMode}, source=RootCard_bootstrap_only");
            }
        }

        private static Task<string> ReadPersistentJsonFileAsync(string path)
        {
            return Task.Run(() =>
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    return string.Empty;

                try
                {
                    return File.ReadAllText(path);
                }
                catch
                {
                    return string.Empty;
                }
            });
        }

        private void RefreshAfterDeferredAnchorRestoreBatch()
        {
            if (m_CurrentAppMode == ExhibitAppMode.Placement && m_EnablePlacementPreviewInPlacementMode)
            {
                RefreshPlacementHeavyPreviewOwner(forceApply: true);
                InteractionModule.ForceRefreshPlacementGuidePathPreviews();
            }

            RefreshCurrentExhibitManipulationState();
        }

        private void PumpPendingAnchorAttachments()
        {
            if (m_AnchorManager == null || m_PendingAnchorAttachments.Count == 0)
                return;

            foreach (var anchor in m_AnchorManager.trackables)
                TryCompletePendingAnchorAttachment(anchor);
        }

        private bool ShouldRestoreAnchorsForCurrentMode()
        {
            return m_CurrentAppMode == ExhibitAppMode.Placement;
        }

        private bool TryRestoreExhibitOnAnchor(ARAnchor anchor, bool deferPostRestoreRefresh = false)
        {
            if (TryRestoreRootMarkerOnAnchor(anchor))
                return true;

            if (anchor == null)
                return false;

            if (!m_SessionLookup.TryGetValue(anchor.trackableId, out var data))
            {
                LogAnchorLifecycle($"restore skipped no session match: {DescribeAnchorState(anchor)}");
                return false;
            }

            TryEvaluateModulePoseConfidence(
                data,
                anchor.transform,
                out RestoreConfidence candidateConfidence,
                out float candidatePositionErrorMeters,
                out float candidateRotationErrorDegrees,
                out string candidateFailureReason);
            LogAnchorLifecycle(
                $"restore_attempt: moduleId={NormalizeModuleId(data.moduleId)}, instanceId={data.instanceID}, displayName={data.displayName}, " +
                $"{DescribeRestoreSource(data, anchor.trackableId.ToString())}, candidateConfidence={FormatRestoreConfidence(candidateConfidence)}, " +
                $"positionError={candidatePositionErrorMeters:F4}, rotationError={candidateRotationErrorDegrees:F2}, {DescribeAnchorState(anchor)}");

            if (candidateConfidence == RestoreConfidence.Unsafe)
            {
                LogAnchorLifecycle(
                    $"restore_rejected_inconsistent: moduleId={NormalizeModuleId(data.moduleId)}, instanceId={data.instanceID}, " +
                    $"trackableId={anchor.trackableId}, reason={candidateFailureReason}, " +
                    $"positionError={candidatePositionErrorMeters:F4}, rotationError={candidateRotationErrorDegrees:F2}");
                return false;
            }

            if (TryAdoptExistingSessionExhibitForAnchor(anchor, data, deferPostRestoreRefresh, out var adoptedTransform, out bool adoptedThisPass))
            {
                ARAnchor adoptedAnchor = adoptedTransform != null ? adoptedTransform.GetComponentInParent<ARAnchor>() : null;
                if (adoptedThisPass && adoptedAnchor == anchor)
                {
                    MarkRestoredExhibitAvailable(anchor, data, adoptedTransform, "restore_bound_existing");
                    TryPromoteRestoredSessionCandidate(data, anchor.trackableId);
                    onStatusMessage?.Invoke($"自动识别: {data.displayName}");
                }

                return true;
            }

            if (anchor.transform.childCount > 0)
            {
                if (TryReviveExistingAnchoredExhibit(anchor, data, deferPostRestoreRefresh, out var revivedTransform))
                {
                    MarkRestoredExhibitAvailable(anchor, data, revivedTransform, "restore_bound_revived");
                    TryPromoteRestoredSessionCandidate(data, anchor.trackableId);
                    return true;
                }

                LogAnchorLifecycle($"restore skipped occupied anchor: trackableId={anchor.trackableId}, childCount={anchor.transform.childCount}, name={anchor.name}");
                return false;
            }

            LogAnchorLifecycle(
                $"restore session match: moduleId={data.moduleId}, instanceId={data.instanceID}, displayName={data.displayName}, " +
                $"{DescribeRestoreSource(data, anchor.trackableId.ToString())}, {DescribeAnchorState(anchor)}");
            RestoreExhibitOnAnchor(anchor, data, deferPostRestoreRefresh);
            bool restored = anchor.transform.childCount > 0;
            if (restored)
            {
                string restoreEvent = string.Equals(
                    ClassifyRestoreTrackableSource(
                        GetOriginalTrackableId(data),
                        GetFallbackTrackableId(data),
                        anchor.trackableId.ToString()),
                    "fallback",
                    StringComparison.Ordinal)
                    ? "restore_bound_fallback"
                    : "restore_bound_original";
                MarkRestoredExhibitAvailable(anchor, data, anchor.transform.GetChild(0), restoreEvent);
                TryPromoteRestoredSessionCandidate(data, anchor.trackableId);
            }
            else
            {
                LogAnchorLifecycle($"restore failed to instantiate: moduleId={data.moduleId}, trackableId={anchor.trackableId}, displayName={data.displayName}");
            }
            onStatusMessage?.Invoke($"自动识别: {data.displayName}");
            return restored;
        }

        private bool TryReviveExistingAnchoredExhibit(
            ARAnchor anchor,
            SessionData data,
            bool deferPostRestoreRefresh,
            out Transform revivedTransform)
        {
            revivedTransform = null;
            if (anchor == null || data == null)
                return false;

            if (!TryFindExistingAnchoredExhibitInfo(anchor, data, out var info) || info == null || info.transform == null)
                return false;

            Transform exhibitTransform = info.transform;
            GameObject exhibitObject = exhibitTransform.gameObject;
            bool wasActiveSelf = exhibitObject.activeSelf;
            bool wasRegistered = TryGetRegisteredExhibitInfo(exhibitTransform, out _);
            bool infoMismatch =
                !string.Equals(NormalizeModuleId(info.moduleId), NormalizeModuleId(data.moduleId), StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(info.displayName ?? string.Empty, data.displayName ?? string.Empty, StringComparison.Ordinal) ||
                !string.Equals(info.instanceID ?? string.Empty, data.instanceID ?? string.Empty, StringComparison.Ordinal) ||
                info.placementContentHidden != data.placementContentHidden;

            if (wasActiveSelf && wasRegistered && !infoMismatch)
            {
                revivedTransform = exhibitTransform;
                return false;
            }

            AttachExhibitInfo(
                exhibitObject,
                data.moduleId,
                data.displayName,
                data.instanceID,
                data.placementContentHidden,
                markPersistenceDirty: false);

            exhibitTransform.SetParent(anchor.transform, false);
            exhibitTransform.localPosition = Vector3.zero;
            exhibitTransform.localRotation = Quaternion.identity;
            if (TryGetLayoutSnapshotScale(data.instanceID, out var savedScale))
                exhibitTransform.localScale = savedScale;

            if (!exhibitObject.activeSelf)
                exhibitObject.SetActive(true);
            else
                RegisterOrUpdateExhibitInfo(info);

            bool shouldPreview = m_CurrentAppMode == ExhibitAppMode.Placement && m_EnablePlacementPreviewInPlacementMode;
            if (shouldPreview)
                EnablePlacementPreviewForExhibitInPlacementMode(exhibitTransform);

            if (!deferPostRestoreRefresh)
                RefreshCurrentExhibitManipulationState();
            if (shouldPreview && !deferPostRestoreRefresh)
                InteractionModule.ForceRefreshPlacementGuidePathPreviews();

            revivedTransform = exhibitTransform;
            LogAnchorLifecycle(
                $"restore reused occupied anchor: moduleId={data.moduleId}, instanceId={data.instanceID}, " +
                $"wasActiveSelf={wasActiveSelf}, wasRegistered={wasRegistered}, {DescribeAnchorState(anchor)}, {DescribeTransformPose(exhibitTransform)}");
            return !wasActiveSelf || !wasRegistered;
        }

        private bool TryFindExistingAnchoredExhibitInfo(ARAnchor anchor, SessionData data, out ExhibitInfo bestInfo)
        {
            bestInfo = null;
            if (anchor == null || data == null)
                return false;

            string moduleId = NormalizeModuleId(data.moduleId);
            string instanceId = string.IsNullOrWhiteSpace(data.instanceID) ? string.Empty : data.instanceID.Trim();
            int bestScore = int.MinValue;

            var infos = anchor.GetComponentsInChildren<ExhibitInfo>(true);
            for (int i = 0; i < infos.Length; i++)
            {
                ExhibitInfo candidate = infos[i];
                if (candidate == null || candidate.transform == null)
                    continue;

                int score = 0;
                if (!string.IsNullOrEmpty(instanceId) &&
                    string.Equals(candidate.instanceID, instanceId, StringComparison.Ordinal))
                {
                    score += 100;
                }

                if (!string.IsNullOrEmpty(moduleId) &&
                    string.Equals(NormalizeModuleId(candidate.moduleId), moduleId, StringComparison.OrdinalIgnoreCase))
                {
                    score += 10;
                }

                if (candidate.transform.parent == anchor.transform)
                    score += 1;

                if (score <= 0 || score <= bestScore)
                    continue;

                bestScore = score;
                bestInfo = candidate;
            }

            return bestInfo != null;
        }

        private void MarkRestoredExhibitAvailable(ARAnchor anchor, SessionData data, Transform exhibitTransform, string logPrefix)
        {
            if (data == null || exhibitTransform == null)
                return;

            SessionRestoreState restoreState = GetAnchoredSessionRestoreState(data, anchor != null ? anchor.trackableId : TrackableId.invalidId);
            TryEvaluateModulePoseConfidence(
                data,
                exhibitTransform,
                out RestoreConfidence confidence,
                out _,
                out _,
                out string failureReason);
            MarkSessionExhibitAvailable(
                data,
                exhibitTransform,
                restoreState,
                confidence,
                logPrefix,
                anchor,
                failureReason);
        }

        public bool IsRestoredModuleAvailable(string moduleId)
        {
            moduleId = NormalizeModuleId(moduleId);
            if (string.IsNullOrEmpty(moduleId)) return false;
            return m_RestoredModuleIds.Contains(moduleId);
        }

        public void SetAppMode(
            ExhibitAppMode mode,
            bool suppressImmediateAnchorRestore = false,
            bool deferPlacementPreviewSync = false)
        {
            ExhibitAppMode previousMode = m_CurrentAppMode;
            if (mode != ExhibitAppMode.Placement)
                CancelLayoutRedeploy($"set_app_mode:{mode}");

            string selectedModuleId = GetCurrentSelectedModuleId();
            Transform selectedTargetBeforeSwitch = string.IsNullOrEmpty(selectedModuleId)
                ? null
                : ResolveDeleteTargetForModule(selectedModuleId);
            LogPlacementStateSnapshot(
                $"SetAppMode begin targetMode={mode}",
                selectedModuleId,
                selectedTargetBeforeSwitch,
                $"previousMode={m_CurrentAppMode}, enablePlacementPreview={m_EnablePlacementPreviewInPlacementMode}");

            m_CurrentAppMode = mode;
            if (m_CurrentAppMode == ExhibitAppMode.Experience)
            {
                PrepareTransientExperienceModeEntry();
                ResetSessionSnapshotState();
                LogAnchorLifecycle("experience session restore disabled: transient experience uses first RootCard bootstrap only");
            }
            else if (previousMode == ExhibitAppMode.Experience)
            {
                ClearTransientExperienceRuntime($"set_app_mode:{mode}");
                EnsureSessionSnapshotLoadedForCurrentMode(notifyStatusMessage: false);
            }
            ApplyPlacementInteractorLayerFiltering();
            RefreshPlacementOnlyCollidersInScene();
            LogPlacementDiagnostics($"after SetAppMode({mode}) collider refresh");

            if (m_CurrentAppMode == ExhibitAppMode.Experience)
                ResetLayoutFallbackUntilFirstAddedGate();

            EnsureRootStateForCurrentMode();

            if (ShouldRestoreAnchorsForCurrentMode() && !suppressImmediateAnchorRestore)
                CheckExistingAnchors();
            else if (ShouldRestoreAnchorsForCurrentMode() && suppressImmediateAnchorRestore)
                LogAnchorLifecycle($"SetAppMode deferred immediate anchor restore: mode={mode}");

            if (m_CurrentAppMode == ExhibitAppMode.Placement && m_EnablePlacementPreviewInPlacementMode)
            {
                if (!deferPlacementPreviewSync)
                {
                    RefreshPlacementHeavyPreviewOwner(forceApply: false);
                    ApplyPlacementPreviewToAllSceneExhibits();
                    RefreshCurrentExhibitManipulationState();
                    RefreshPlacementFocusedExhibitVisibility($"set_app_mode:{mode}");
                    InteractionModule.ForceRefreshPlacementGuidePathPreviews();
                    LogPlacementDiagnostics($"after SetAppMode({mode}) final refresh");
                }
                else
                {
                    LogPlacementDiagnostics($"SetAppMode deferred placement preview sync: mode={mode}");
                }

                Transform selectedTargetAfterPlacementSwitch = string.IsNullOrEmpty(selectedModuleId)
                    ? null
                    : ResolveDeleteTargetForModule(selectedModuleId);
                LogPlacementStateSnapshot(
                    $"SetAppMode end mode={mode}",
                    selectedModuleId,
                    selectedTargetAfterPlacementSwitch,
                    $"hasRestoredSessionExhibits={m_HasRestoredSessionExhibits}, hasRestoredRoot={m_HasRestoredRootExhibit}");
                return;
            }

            if (!deferPlacementPreviewSync)
            {
                RefreshPlacementHeavyPreviewOwner(forceApply: true);
                ClearPlacementPreviewFromAllSceneExhibits();
                RefreshCurrentExhibitManipulationState();
                LogPlacementDiagnostics($"after SetAppMode({mode}) final refresh");
            }
            else
            {
                LogPlacementDiagnostics($"SetAppMode deferred placement preview sync: mode={mode}");
            }

            Transform selectedTargetAfterSwitch = string.IsNullOrEmpty(selectedModuleId)
                ? null
                : ResolveDeleteTargetForModule(selectedModuleId);
            LogPlacementStateSnapshot(
                $"SetAppMode end mode={mode}",
                selectedModuleId,
                selectedTargetAfterSwitch,
                $"hasRestoredSessionExhibits={m_HasRestoredSessionExhibits}, hasRestoredRoot={m_HasRestoredRootExhibit}");
        }

        public void RefreshPlacementPreviewForCurrentMode()
        {
            ApplyPlacementInteractorLayerFiltering();
            RefreshPlacementOnlyCollidersInScene();

            if (m_CurrentAppMode == ExhibitAppMode.Placement && m_EnablePlacementPreviewInPlacementMode)
            {
                RefreshPlacementHeavyPreviewOwner(forceApply: false);
                ApplyPlacementPreviewToAllSceneExhibits();
                RefreshCurrentExhibitManipulationState();
                RefreshPlacementFocusedExhibitVisibility("refresh_preview_for_current_mode");
                InteractionModule.ForceRefreshPlacementGuidePathPreviews();
                LogPlacementDiagnostics("after RefreshPlacementPreviewForCurrentMode");
                return;
            }

            RefreshPlacementHeavyPreviewOwner(forceApply: true);
            ClearPlacementPreviewFromAllSceneExhibits();
            RefreshCurrentExhibitManipulationState();
            LogPlacementDiagnostics("after RefreshPlacementPreviewForCurrentMode");
        }

        public bool TrySoftRestartRestoreSessionExhibits()
        {
            if (m_CurrentAppMode != ExhibitAppMode.Experience)
                return false;

            if (GetRootAnchor() == null)
            {
                LogAnchorLifecycle("soft restore aborted: root anchor missing");
                return false;
            }

            SaveAllToFiles(saveLayout: false, suppressStatusMessage: true);
            LogAnchorLifecycle($"soft restore saved session: expectedExhibits={ExpectedSessionExhibitCount}, expectedAnchors={ExpectedSessionAnchorCount}");
            if (ExpectedSessionExhibitCount <= 0)
            {
                LogAnchorLifecycle("soft restore aborted: no saved exhibit anchors in session");
                return false;
            }

            int detachedCount = DestroyAnchoredExhibitInstancesPreserveAnchors();
            ResetRuntimeRestoreTrackingState();
            CancelSoftRestoreRetry();
            CheckExistingAnchors();
            LogAnchorLifecycle($"soft restore first pass: detached={detachedCount}, restored={RestoredSessionExhibitCount}/{ExpectedSessionExhibitCount}, restoredRoot={m_HasRestoredRootExhibit}");

            if (detachedCount > 0 && !m_HasRestoredSessionExhibits)
            {
                LogAnchorLifecycle("soft restore scheduling retry on next frame");
                m_SoftRestoreRetryCoroutine = StartCoroutine(RetrySoftRestoreNextFrame());
            }

            RefreshCurrentExhibitManipulationState();
            return detachedCount > 0 && m_HasRestoredSessionExhibits;
        }

        private IEnumerator RetrySoftRestoreNextFrame()
        {
            yield return null;

            m_SoftRestoreRetryCoroutine = null;
            if (m_CurrentAppMode != ExhibitAppMode.Experience)
            {
                LogAnchorLifecycle("soft restore retry cancelled: app mode is no longer Experience");
                yield break;
            }

            LoadSessionToMemory();
            LogAnchorLifecycle($"soft restore retry pass: expectedExhibits={ExpectedSessionExhibitCount}, trackables={GetAnchorTrackableCount()}");
            CheckExistingAnchors();
            LogAnchorLifecycle($"soft restore retry result: restored={RestoredSessionExhibitCount}/{ExpectedSessionExhibitCount}, restoredRoot={m_HasRestoredRootExhibit}");

            if (!m_HasRestoredSessionExhibits && ExpectedSessionExhibitCount > 0)
            {
                onStatusMessage?.Invoke($"体验模式恢复未命中：0/{ExpectedSessionExhibitCount} 个展品。请查看 [AnchorLifecycle] 日志。");
            }
        }

        private void CancelSoftRestoreRetry()
        {
            if (m_SoftRestoreRetryCoroutine == null)
                return;

            StopCoroutine(m_SoftRestoreRetryCoroutine);
            m_SoftRestoreRetryCoroutine = null;
        }

        private int GetAnchorTrackableCount()
        {
            if (m_AnchorManager == null)
                return 0;

            int count = 0;
            foreach (var _ in m_AnchorManager.trackables)
                count++;

            return count;
        }

        private Transform ResolveModuleContainerRoot(bool createIfMissing = false)
        {
            if (m_ModuleContainerRoot != null)
                return m_ModuleContainerRoot;

            if (m_RuntimeModuleManager != null)
            {
                m_ModuleContainerRoot = m_RuntimeModuleManager.transform;
                return m_ModuleContainerRoot;
            }

            var existingManager = FindFirstObjectByType<InteractionModuleManager>(FindObjectsInactive.Include);
            if (existingManager != null)
            {
                m_RuntimeModuleManager = existingManager;
                m_ModuleContainerRoot = existingManager.transform;
                return m_ModuleContainerRoot;
            }

            if (!string.IsNullOrWhiteSpace(m_DefaultModuleContainerName))
            {
                var allTransforms = GameObject.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                for (int i = 0; i < allTransforms.Length; i++)
                {
                    var candidate = allTransforms[i];
                    if (candidate == null) continue;
                    if (!string.Equals(candidate.name, m_DefaultModuleContainerName, StringComparison.Ordinal)) continue;
                    m_ModuleContainerRoot = candidate;
                    return m_ModuleContainerRoot;
                }
            }

            if (!createIfMissing)
                return null;

            string containerName = string.IsNullOrWhiteSpace(m_DefaultModuleContainerName)
                ? "ModuleManager"
                : m_DefaultModuleContainerName;
            var host = new GameObject(containerName);
            m_ModuleContainerRoot = host.transform;
            return m_ModuleContainerRoot;
        }

        private void ParentLooseExhibitToModuleContainer(Transform exhibitTransform)
        {
            if (exhibitTransform == null) return;
            if (exhibitTransform.parent != null) return;

            var containerRoot = ResolveModuleContainerRoot();
            if (containerRoot == null) return;

            exhibitTransform.SetParent(containerRoot, true);
        }

        private void TryParentAnchorToModuleContainer(Transform anchorTransform)
        {
            if (anchorTransform == null) return;

            var containerRoot = ResolveModuleContainerRoot();
            if (containerRoot == null || anchorTransform.parent == containerRoot)
                return;

            if (!IsSafeAnchorContainerRoot(containerRoot))
                return;

            anchorTransform.SetParent(containerRoot, true);
        }

        private static bool IsSafeAnchorContainerRoot(Transform containerRoot)
        {
            if (containerRoot == null) return false;
            if (containerRoot.parent != null) return false;
            if (containerRoot.localPosition.sqrMagnitude > 0.000001f) return false;
            if (Quaternion.Angle(containerRoot.localRotation, Quaternion.identity) > 0.01f) return false;
            if ((containerRoot.localScale - Vector3.one).sqrMagnitude > 0.000001f) return false;
            return true;
        }

        // =========================================================
        //  UI 按钮入口
        // =========================================================
        public bool SelectByModuleId(string moduleId, bool notifyUI = true)
        {
            moduleId = NormalizeModuleId(moduleId);
            if (string.IsNullOrEmpty(moduleId)) return false;
            if (!m_IndexByModuleId.TryGetValue(moduleId, out int index)) return false;
            m_CurrentSelectedIndex = index;
            RefreshPlacementHeavyPreviewOwner();
            RefreshCurrentExhibitManipulationState();
            RefreshPlacementFocusedExhibitVisibility($"select_module:{moduleId}");
            if (notifyUI) NotifyUIUpdate();
            return true;
        }

        public bool SelectRootMarker(bool notifyUI = true)
        {
            Transform rootTransform = GetRootTransform();
            if (rootTransform == null)
                return false;

            SetManualSelection(rootTransform);
            if (notifyUI)
                NotifyUIUpdate();
            return true;
        }

        public void SetRootManipulationEnabled(bool enabled)
        {
            if (m_RootManipulationEnabled == enabled)
                return;

            m_RootManipulationEnabled = enabled;
            RefreshCurrentExhibitManipulationState();
            RefreshPlacementFocusedExhibitVisibility($"root_manipulation:{enabled}");
        }

        public Transform SpawnByModuleId(string moduleId, string overrideInstanceId = null, bool showToast = true)
        {
            moduleId = NormalizeModuleId(moduleId);
            if (IsStageModuleId(moduleId))
                return PlaceStageModule(moduleId, overrideInstanceId, showToast);

            if (!SelectByModuleId(moduleId))
            {
                onStatusMessage?.Invoke($"模块未在可用清单中: {moduleId}");
                return null;
            }
            return SpawnCurrentSelectedAnchorInternal(overrideInstanceId, showToast);
        }

        public Transform PlaceStageModule(string moduleId, string overrideInstanceId = null, bool showToast = true)
        {
            moduleId = NormalizeModuleId(moduleId);
            if (string.IsNullOrEmpty(moduleId))
            {
                onStatusMessage?.Invoke("放置展台失败：moduleId 为空。");
                return null;
            }

            if (!TryGetDeployableModuleDefinition(moduleId, out var definition) || !definition.isStageModule)
            {
                onStatusMessage?.Invoke($"放置展台失败：未找到 moduleId={moduleId} 的展台配置。");
                return null;
            }

            Transform existing = ResolveDeleteTargetForModule(moduleId);
            if (existing != null)
            {
                SetManualSelection(existing);
                if (showToast)
                    onStatusMessage?.Invoke($"已选中展台：{definition.displayName}");
                return existing;
            }

            if (GetRootAnchor() == null)
            {
                onStatusMessage?.Invoke("定位点空间锚点尚未确认，请稍候再放置展台。");
                return null;
            }

            var exhibit = SpawnSingleItem(definition, overrideInstanceId, showToast: false);
            if (exhibit != null)
            {
                SetManualSelection(exhibit);
                if (!TryAutoSaveExhibit(exhibit))
                {
                    if (showToast)
                        onStatusMessage?.Invoke($"已放置：{definition.displayName}，但保存未能启动。");
                }
                else if (showToast)
                {
                    onStatusMessage?.Invoke($"已放置：{definition.displayName}，正在创建空间锚点。");
                }
            }

            return exhibit;
        }

        public bool DeleteStageModule(string moduleId)
        {
            moduleId = NormalizeModuleId(moduleId);
            if (!IsStageModuleId(moduleId))
            {
                onStatusMessage?.Invoke($"删除展台失败：未找到 moduleId={moduleId} 的展台配置。");
                return false;
            }

            return TryDeleteClosestAnchorByModuleId(moduleId);
        }

        public bool TryGetStageModulePlaced(string moduleId, out Transform target)
        {
            moduleId = NormalizeModuleId(moduleId);
            if (!IsStageModuleId(moduleId))
            {
                target = null;
                return false;
            }

            target = ResolveDeleteTargetForModule(moduleId);
            return target != null;
        }

        private Transform SpawnCurrentSelectedAnchorInternal(string overrideInstanceId, bool showToast)
        {
            if (!TryGetCurrentStep(out var step))
            {
                onStatusMessage?.Invoke("无可放置步骤，请检查 ChapterPlacementPlan。");
                return null;
            }

            if (!TryGetDeployableModuleDefinition(step.moduleId, out var definition))
            {
                onStatusMessage?.Invoke($"当前步骤 moduleId 无法解析：{step.moduleId}");
                return null;
            }

            var exhibit = SpawnSingleItem(definition, overrideInstanceId, showToast);
            if (exhibit == null) return null;

            SetManualSelection(exhibit);
            return exhibit;
        }

        public bool RebuildWholeLayout(Action<bool> onCompleted = null)
        {
            if (!m_HasLayoutLoaded || m_CachedLayout.Count == 0)
            {
                onStatusMessage?.Invoke("一键布展失败：无图纸。请先策展并保存。");
                return false;
            }

            Transform existingRoot = GetRootTransform();
            if (existingRoot == null)
            {
                onStatusMessage?.Invoke("一键布展失败：缺定位点。请先调整定位点。");
                return false;
            }

            if (GetRootAnchor() == null)
            {
                onStatusMessage?.Invoke("一键布展失败：Root 尚未固定。请先调整 Root 并完成锚定。");
                return false;
            }

            if (m_LayoutRedeployCoroutine != null)
            {
                onStatusMessage?.Invoke("一键布展进行中，请稍候。");
                return false;
            }

            int clearedCount = ClearPlacedExhibitsForLayoutRedeploy();
            m_LayoutRedeployCompletedCallback = onCompleted;
            m_LayoutRedeployCoroutine = StartCoroutine(RebuildWholeLayoutRoutine(existingRoot, clearedCount));
            return true;
        }

        public bool HasUsableLayoutSnapshot()
        {
            return m_HasLayoutLoaded && m_CachedLayout != null && m_CachedLayout.Count > 0;
        }

        public void TryRestoreSession()
        {
            if (m_CurrentAppMode == ExhibitAppMode.Experience)
            {
                ResetSessionSnapshotState();
                onStatusMessage?.Invoke("体验模式已禁用旧锚点恢复，请先识别 RootCard。");
                return;
            }

            LoadSessionToMemory();
            if (ShouldRestoreAnchorsForCurrentMode())
            {
                CheckExistingAnchors();
                onStatusMessage?.Invoke("已刷新锚点状态");
                return;
            }

            onStatusMessage?.Invoke("已刷新会话记录；进入体验模式后会自动恢复锚点。");
        }

        public bool TrySaveClosestAnchor()
        {
            if (m_ManualSelectedTarget == null)
            {
                onStatusMessage?.Invoke("未选中展品");
                return false;
            }

            if (!TryParseExhibitInfo(m_ManualSelectedTarget, out var moduleId, out _, out _))
            {
                onStatusMessage?.Invoke("保存失败：当前选中物体不是有效展品。");
                return false;
            }

            return TrySaveAnchorByModuleId(moduleId);
        }

        public bool TrySaveAnchorByModuleId(string moduleId)
        {
            moduleId = NormalizeModuleId(moduleId);
            if (string.IsNullOrEmpty(moduleId))
            {
                onStatusMessage?.Invoke("保存失败：moduleId 为空。");
                return false;
            }

            if (GetRootAnchor() == null)
            {
                onStatusMessage?.Invoke("保存失败：请先调整并固定定位点。");
                return false;
            }

            Transform target = ResolveDeleteTargetForModule(moduleId);
            if (target == null)
            {
                onStatusMessage?.Invoke($"保存失败：未找到 moduleId={moduleId} 的已放置展品。");
                return false;
            }

            if (!TryParseExhibitInfo(target, out var targetModuleId, out _, out _) || targetModuleId != moduleId)
            {
                onStatusMessage?.Invoke($"保存失败：选中展品与当前步骤 moduleId 不匹配（期望 {moduleId}）。");
                return false;
            }

            return TrySaveExhibitPlacement(target, saveLayout: true, suppressStatusMessage: false);
        }

        public void SaveClosestAnchor()
        {
            TrySaveClosestAnchor();
        }

        public bool TryAutoSaveExhibit(Transform exhibitTransform)
        {
            if (exhibitTransform == null)
                return false;

            return TrySaveExhibitPlacement(exhibitTransform, saveLayout: true, suppressStatusMessage: false);
        }

        public bool TryDetachExhibitFromCurrentAnchor(Transform exhibitTransform)
        {
            if (exhibitTransform == null)
                return false;

            if (!DetachExhibitFromCurrentAnchorInternal(exhibitTransform, logReason: "manual move"))
                return false;

            if (TryGetRegisteredExhibitInfo(exhibitTransform, out var info))
                MarkExhibitPersistenceDirty(info, markSession: true, markLayout: false);
            QueueSaveToFiles(saveLayout: false, suppressStatusMessage: true);
            RefreshPlacementProgressInDirector();
            return true;
        }

        public bool TryDeleteClosestAnchor()
        {
            onStatusMessage?.Invoke("请使用当前步骤删除（按 moduleId）入口。");
            return false;
        }

        public bool TryDeleteClosestAnchorByModuleId(string moduleId)
        {
            moduleId = NormalizeModuleId(moduleId);
            if (string.IsNullOrEmpty(moduleId))
            {
                onStatusMessage?.Invoke("删除失败：moduleId 为空。");
                return false;
            }

            Transform target = ResolveDeleteTargetForModule(moduleId);
            if (target == null)
            {
                onStatusMessage?.Invoke($"删除失败：未找到 moduleId={moduleId} 的已放置展品。");
                return false;
            }

            if (!TryParseExhibitInfo(target, out var targetModuleId, out var displayName, out _) || targetModuleId != moduleId)
            {
                onStatusMessage?.Invoke($"删除失败：选中展品与当前步骤 moduleId 不匹配（期望 {moduleId}）。");
                return false;
            }

            var removedInfos = target.GetComponentsInChildren<ExhibitInfo>(true);
            for (int i = 0; i < removedInfos.Length; i++)
            {
                var removedInfo = removedInfos[i];
                if (removedInfo == null)
                    continue;

                MarkRemovedExhibitPersistenceState(removedInfo.instanceID, removeSession: true, removeLayout: true);
            }

            if (m_RuntimeRootTrans == target) m_RuntimeRootTrans = null;

            CancelPendingAnchorAttachmentForTarget(target);
            if (target.parent && target.parent.GetComponent<ARAnchor>())
                QueueAnchorNodeForDestruction(target.parent.gameObject);
            else
                Destroy(target.gameObject);

            ClearManualSelection(refreshUi: true);

            RemoveRegisteredExhibitHierarchy(target);
            RefreshPlacementHeavyPreviewOwner(forceApply: true);
            RefreshCurrentExhibitManipulationState();
            if (m_CurrentAppMode == ExhibitAppMode.Placement && m_EnablePlacementPreviewInPlacementMode)
                InteractionModule.ForceRefreshPlacementGuidePathPreviews();
            QueueSaveToFiles(saveLayout: true, suppressStatusMessage: false);
            onStatusMessage?.Invoke($"已删除: {displayName}");
            return true;
        }

        public bool HasPlacedAnchorByModuleId(string moduleId)
        {
            moduleId = NormalizeModuleId(moduleId);
            if (string.IsNullOrEmpty(moduleId)) return false;
            return ResolveDeleteTargetForModule(moduleId) != null;
        }

        public bool HasSavedPlacementByModuleId(string moduleId)
        {
            moduleId = NormalizeModuleId(moduleId);
            if (string.IsNullOrEmpty(moduleId)) return false;

            Transform target = ResolveDeleteTargetForModule(moduleId);
            return target != null && target.GetComponentInParent<ARAnchor>() != null;
        }

        public bool HasSavedSessionPlacementByModuleId(string moduleId)
        {
            moduleId = NormalizeModuleId(moduleId);
            if (string.IsNullOrEmpty(moduleId))
                return false;

            foreach (var entry in m_SessionSnapshotByInstanceId.Values)
            {
                if (entry == null)
                    continue;

                if (NormalizeModuleId(entry.moduleId) == moduleId)
                    return true;
            }

            return false;
        }

        internal bool TryEnsureSessionPlacementRestoredByModuleId(string moduleId)
        {
            moduleId = NormalizeModuleId(moduleId);
            if (string.IsNullOrEmpty(moduleId))
                return false;

            LogPlacementStateSnapshot(
                "ensureRestore begin",
                moduleId,
                null,
                $"hasAnchorManager={(m_AnchorManager != null)}, sessionLookupCount={m_SessionLookup.Count}");

            if (ResolveDeleteTargetForModule(moduleId) != null && IsExperiencePlacementUsableByModuleId(moduleId))
            {
                LogDetailedPlacementResolution($"ensureRestore skipped existing target: moduleId={moduleId}");
                return true;
            }

            if (m_AnchorManager == null || m_SessionLookup.Count == 0)
            {
                LogDetailedPlacementResolution($"ensureRestore unavailable: moduleId={moduleId}, hasAnchorManager={(m_AnchorManager != null)}, sessionLookupCount={m_SessionLookup.Count}");
                return false;
            }

            foreach (var entry in m_SessionSnapshotByInstanceId)
            {
                SessionData data = entry.Value;
                if (data == null || NormalizeModuleId(data.moduleId) != moduleId)
                    continue;

                var candidateTrackableIds = GetCandidateTrackableIds(data);
                for (int i = 0; i < candidateTrackableIds.Count; i++)
                {
                    TrackableId candidateTrackableId = ParseTrackableId(candidateTrackableIds[i]);
                    if (candidateTrackableId == TrackableId.invalidId)
                        continue;

                    foreach (var anchor in m_AnchorManager.trackables)
                    {
                        if (anchor == null || anchor.trackableId != candidateTrackableId)
                            continue;

                        LogDetailedPlacementResolution(
                            $"ensureRestore attempting: moduleId={moduleId}, trackableId={candidateTrackableId}, anchorName={anchor.name}, childCount={anchor.transform.childCount}");
                        TryRestoreExhibitOnAnchor(anchor);
                        Transform restoredTarget = ResolveDeleteTargetForModule(moduleId);
                        bool restored = restoredTarget != null;
                        LogDetailedPlacementResolution(
                            $"ensureRestore result: moduleId={moduleId}, restored={restored}, trackableId={candidateTrackableId}");
                        LogPlacementStateSnapshot(
                            "ensureRestore result",
                            moduleId,
                            restoredTarget,
                            $"restored={restored}, trackableId={candidateTrackableId}");
                        return restored;
                    }

                }
            }

            LogDetailedPlacementResolution($"ensureRestore no matching anchor trackable: moduleId={moduleId}");
            LogPlacementStateSnapshot("ensureRestore missingAnchor", moduleId, null, "result=false");
            return false;
        }

        private bool IsExperiencePlacementUsableByModuleId(string moduleId)
        {
            moduleId = NormalizeModuleId(moduleId);
            if (string.IsNullOrEmpty(moduleId))
                return false;

            if (!IsRootReadyForPlayback)
                return false;

            if (m_CurrentAppMode == ExhibitAppMode.Experience && m_HasTransientExperienceBootstrapRootPose)
            {
                if (!m_TransientExperienceInstanceIdByModuleId.TryGetValue(moduleId, out string transientInstanceId) ||
                    !m_TransientExperienceStatesByInstanceId.TryGetValue(transientInstanceId, out var transientState) ||
                    transientState == null)
                {
                    return false;
                }

                return transientState.target != null;
            }

            foreach (var entry in m_SessionSnapshotByInstanceId)
            {
                SessionData data = entry.Value;
                if (data == null || NormalizeModuleId(data.moduleId) != moduleId)
                    continue;

                string instanceId = NormalizeInstanceId(data.instanceID);
                if (string.IsNullOrEmpty(instanceId) || !IsSessionInstanceUsable(instanceId))
                    continue;

                if (TryGetSceneExhibitByInstanceId(instanceId, out var info) &&
                    info != null &&
                    info.transform != null)
                {
                    return true;
                }
            }

            return false;
        }

        internal bool TryEnsureExperiencePlacementAvailableByModuleId(string moduleId, bool allowLayoutFallback = true)
        {
            moduleId = NormalizeModuleId(moduleId);
            if (string.IsNullOrEmpty(moduleId))
                return false;

            if (m_CurrentAppMode == ExhibitAppMode.Experience)
            {
                if (!m_HasTransientExperienceBootstrapRootPose)
                {
                    LogDetailedPlacementResolution(
                        $"ensureAvailable blocked: moduleId={moduleId}, reason=waiting_for_first_rootcard_bootstrap");
                    return false;
                }

                return TryEnsureTransientExperiencePlacementAvailableByModuleId(moduleId);
            }

            if (m_CurrentAppMode == ExhibitAppMode.Experience && m_HasAnchorPoseConflict)
            {
                LogDetailedPlacementResolution(
                    $"ensureAvailable blocked by anchor pose conflict: moduleId={moduleId}, summary={m_AnchorPoseConflictSummary}");
                return false;
            }

            Transform existingTarget = ResolveDeleteTargetForModule(moduleId);
            if (existingTarget != null && IsExperiencePlacementUsableByModuleId(moduleId))
                return true;

            if (TryEnsureSessionPlacementRestoredByModuleId(moduleId))
            {
                RefreshExperienceRestoreHealthAndRecovery($"ensureAvailable:{moduleId}");
                if (IsExperiencePlacementUsableByModuleId(moduleId))
                    return true;
            }

            if (!allowLayoutFallback)
                return false;

            Transform rootTransform = GetRootTransform();
            if (rootTransform == null)
            {
                LogDetailedPlacementResolution(
                    $"ensureAvailable fallback skipped missing root: moduleId={moduleId}");
                return false;
            }

            bool createdFallback = false;
            foreach (var entry in m_SessionSnapshotByInstanceId)
            {
                SessionData data = entry.Value;
                if (data == null || NormalizeModuleId(data.moduleId) != moduleId)
                    continue;

                string instanceId = NormalizeInstanceId(data.instanceID);
                if (string.IsNullOrEmpty(instanceId) || IsSessionInstanceAvailable(instanceId))
                    continue;

                if (!TryGetLayoutSnapshotItem(instanceId, out var layoutData) || layoutData == null)
                    continue;

                createdFallback |= TryCreateLayoutFallbackExhibit(rootTransform, data, layoutData);
            }

            if (createdFallback)
            {
                LogDetailedPlacementResolution(
                    $"ensureAvailable fallback created: moduleId={moduleId}");
            }
            else
            {
                LogDetailedPlacementResolution(
                    $"ensureAvailable fallback unavailable: moduleId={moduleId}");
            }

            RefreshExperienceRestoreHealthAndRecovery($"ensureAvailableFallback:{moduleId}");
            return IsExperiencePlacementUsableByModuleId(moduleId);
        }

        public void DeleteClosestAnchor()
        {
            TryDeleteClosestAnchor();
        }

        // =========================================================
        //  基于现有 Root 展开剩余物体
        // =========================================================
        private IEnumerator RebuildWholeLayoutRoutine(Transform rootTrans, int clearedCount)
        {
            int deployedCount = 0;
            float spawnIntervalSeconds = Mathf.Max(0f, m_LayoutRedeploySpawnIntervalSeconds);

            onStatusMessage?.Invoke(
                spawnIntervalSeconds > 0f
                    ? $"一键布展已开始：将按 {spawnIntervalSeconds:0.##} 秒间隔依次生成展品。"
                    : "一键布展已开始：正在连续生成展品。");

            foreach (var data in m_CachedLayout)
            {
                if (rootTrans == null)
                {
                    onStatusMessage?.Invoke("一键布展失败：定位点在布展过程中丢失。");
                    FinishLayoutRedeploy(false);
                    yield break;
                }

                if (CheckIfInstanceExists(data.instanceID)) continue;

                if (!TryGetDeployableModuleDefinition(data.moduleId, out var definition) || definition.prefab == null)
                {
                    onStatusMessage?.Invoke($"跳过未知 moduleId: {data.moduleId}");
                    continue;
                }

                Vector3 worldPos = rootTrans.TransformPoint(data.relativePosition);
                Quaternion worldRot = rootTrans.rotation * data.relativeRotation;

                GameObject childObj = Instantiate(definition.prefab);
                childObj.name = $"Exhibit_{data.moduleId}_{data.displayName}_{data.instanceID}";
                AttachExhibitInfo(childObj, data.moduleId, data.displayName, data.instanceID, data.placementContentHidden, markPersistenceDirty: false);
                if (TryGetRegisteredExhibitInfo(childObj.transform, out var restoredLayoutInfo))
                    MarkExhibitPersistenceDirty(restoredLayoutInfo, markSession: true, markLayout: false);
                childObj.transform.SetPositionAndRotation(worldPos, worldRot);
                ParentLooseExhibitToModuleContainer(childObj.transform);
                childObj.transform.localScale = data.scale;
                if (m_CurrentAppMode == ExhibitAppMode.Placement && m_EnablePlacementPreviewInPlacementMode)
                    EnablePlacementPreviewForExhibitInPlacementMode(childObj.transform);

                if (!TrySaveExhibitPlacement(
                        childObj.transform,
                        saveLayout: false,
                        suppressStatusMessage: true,
                        updateManualSelection: false))
                {
                    Destroy(childObj);
                    onStatusMessage?.Invoke($"一键布展失败：无法为 {data.displayName} 创建锚点。");
                    FinishLayoutRedeploy(false);
                    yield break;
                }

                deployedCount++;
                LogAnchorLifecycle(
                    $"layout redeploy spawned exhibit: moduleId={data.moduleId}, instanceId={data.instanceID}, " +
                    $"displayName={data.displayName}, deployedCount={deployedCount}, intervalSeconds={spawnIntervalSeconds:0.##}, " +
                    $"{DescribeTransformPose(childObj.transform)}");

                if (spawnIntervalSeconds > 0f)
                    yield return new WaitForSecondsRealtime(spawnIntervalSeconds);
            }

            RefreshCurrentExhibitManipulationState();
            if (m_CurrentAppMode == ExhibitAppMode.Placement && m_EnablePlacementPreviewInPlacementMode)
                InteractionModule.ForceRefreshPlacementGuidePathPreviews();

            m_FocusPlacementVisibilityToCurrentTarget = true;
            m_PendingInitialFocusedContentHide = true;
            RefreshPlacementFocusedExhibitVisibility("layout_redeploy_completed");
            LogAnchorLifecycle(
                $"layout redeploy completed: cleared={clearedCount}, deployed={deployedCount}, layoutCount={m_CachedLayout.Count}, " +
                $"intervalSeconds={spawnIntervalSeconds:0.##}, root={DescribeTransformPose(rootTrans)}");
            onStatusMessage?.Invoke($"一键布展已完成：已为 {deployedCount} 个展品发起空间锚点创建。");
            FinishLayoutRedeploy(true);
        }

        private void CancelLayoutRedeploy(string reason)
        {
            if (m_LayoutRedeployCoroutine == null)
                return;

            m_LayoutRedeployCompletedCallback = null;
            StopCoroutine(m_LayoutRedeployCoroutine);
            m_LayoutRedeployCoroutine = null;
            LogAnchorLifecycle($"layout redeploy cancelled: reason={reason}");
        }

        private void FinishLayoutRedeploy(bool success)
        {
            Action<bool> completedCallback = m_LayoutRedeployCompletedCallback;
            m_LayoutRedeployCompletedCallback = null;
            m_LayoutRedeployCoroutine = null;
            completedCallback?.Invoke(success);
        }

        private int ClearPlacedExhibitsForLayoutRedeploy()
        {
            var exhibits = CaptureRegisteredExhibitSnapshot();
            if (exhibits.Count == 0)
                return 0;

            int removedCount = 0;
            var processedTransformIds = new HashSet<int>();
            for (int i = 0; i < exhibits.Count; i++)
            {
                ExhibitInfo info = exhibits[i];
                Transform exhibitRoot = info != null ? info.transform : null;
                if (exhibitRoot == null)
                    continue;

                int transformId = exhibitRoot.GetInstanceID();
                if (!processedTransformIds.Add(transformId))
                    continue;

                ARAnchor parentAnchor = exhibitRoot.GetComponentInParent<ARAnchor>();
                TrackableId currentTrackableId = parentAnchor != null
                    ? parentAnchor.trackableId
                    : TrackableId.invalidId;

                if (TryParseExhibitInfo(exhibitRoot, out var moduleId, out var displayName, out var instanceId))
                {
                    LogAnchorLifecycle(
                        $"layout redeploy clearing exhibit: moduleId={moduleId}, instanceId={instanceId}, displayName={displayName}, " +
                        $"anchored={(parentAnchor != null)}, pending={IsAnchorAttachmentPending(exhibitRoot)}, " +
                        $"trackableId={(currentTrackableId != TrackableId.invalidId ? currentTrackableId.ToString() : "invalid")}, " +
                        $"{DescribeTransformPose(exhibitRoot)}");

                    MarkRemovedExhibitPersistenceState(instanceId, removeSession: true, removeLayout: false);
                    RemoveSessionLookupForInstanceId(instanceId, currentTrackableId);
                    m_SessionSnapshotByInstanceId.Remove(instanceId);
                    m_RestoreStateByInstanceId.Remove(instanceId);
                    m_RestoreConfidenceByInstanceId.Remove(instanceId);
                    m_RestoreFailureReasonByInstanceId.Remove(instanceId);
                    m_RestoredModuleIds.Remove(moduleId);
                }
                else
                {
                    LogAnchorLifecycle(
                        $"layout redeploy clearing exhibit (unparsed): name={exhibitRoot.name}, " +
                        $"anchored={(parentAnchor != null)}, pending={IsAnchorAttachmentPending(exhibitRoot)}, " +
                        $"trackableId={(currentTrackableId != TrackableId.invalidId ? currentTrackableId.ToString() : "invalid")}, " +
                        $"{DescribeTransformPose(exhibitRoot)}");
                }

                CancelPendingAnchorAttachmentForTarget(exhibitRoot);

                parentAnchor = exhibitRoot.GetComponentInParent<ARAnchor>();
                if (parentAnchor != null)
                    QueueAnchorNodeForDestruction(parentAnchor.gameObject);
                else
                    Destroy(exhibitRoot.gameObject);

                RemoveRegisteredExhibitHierarchy(exhibitRoot);
                removedCount++;
            }

            if (removedCount <= 0)
                return 0;

            ClearManualSelection(refreshUi: true);
            RefreshPlacementHeavyPreviewOwner(forceApply: true);
            m_PlacementVideoPreviewOwnerInstanceId = string.Empty;
            m_PlacementPointCloudPreviewOwnerInstanceId = string.Empty;
            RefreshCurrentExhibitManipulationState();
            if (m_CurrentAppMode == ExhibitAppMode.Placement && m_EnablePlacementPreviewInPlacementMode)
                InteractionModule.ForceRefreshPlacementGuidePathPreviews();

            LogAnchorLifecycle($"layout redeploy cleared existing exhibits: count={removedCount}");
            return removedCount;
        }

        private bool CheckIfInstanceExists(string instanceID)
        {
            return TryGetRegisteredExhibitByInstanceId(instanceID, out _);
        }

        // =========================================================
        //  生成单个物体
        // =========================================================
        private Transform SpawnSingleItem(PlacementModuleDefinition definition, string overrideInstanceId, bool showToast = false)
        {
            if (definition.prefab == null)
            {
                onStatusMessage?.Invoke("当前模块 prefab 未配置");
                return null;
            }

            if (m_MainCamera == null) m_MainCamera = Camera.main;
            if (m_MainCamera == null)
            {
                onStatusMessage?.Invoke("未找到主摄像机");
                return null;
            }

            Transform cameraTrans = m_MainCamera.transform;

            string currentInstanceId;
            if (string.IsNullOrEmpty(overrideInstanceId))
            {
                currentInstanceId = m_NextID.ToString();
                m_NextID++;
            }
            else
            {
                currentInstanceId = overrideInstanceId;
                if (int.TryParse(overrideInstanceId, out int val) && val >= m_NextID) m_NextID = val + 1;
            }

            string displayName = definition.displayName;
            GameObject obj = Instantiate(definition.prefab);
            obj.name = $"Exhibit_{definition.moduleId}_{displayName}_{currentInstanceId}";
            AttachExhibitInfo(obj, definition.moduleId, displayName, currentInstanceId, placementContentHidden: false);

            float dist = definition.spawnDistance <= 0.1f ? 1.5f : definition.spawnDistance;
            Vector3 horizontalForward = Quaternion.Euler(0f, cameraTrans.eulerAngles.y, 0f) * Vector3.forward;
            Vector3 spawnPos = cameraTrans.position + (horizontalForward * dist);
            spawnPos.y = cameraTrans.position.y - 1.6f;
            Quaternion spawnRot =
                Quaternion.LookRotation(horizontalForward, Vector3.up) *
                Quaternion.Euler(0f, PlacementSpawnYawOffsetDegrees, 0f);
            obj.transform.SetPositionAndRotation(spawnPos, spawnRot);
            ParentLooseExhibitToModuleContainer(obj.transform);
            if (m_CurrentAppMode == ExhibitAppMode.Placement && m_EnablePlacementPreviewInPlacementMode)
                EnablePlacementPreviewForExhibitInPlacementMode(obj.transform);
            RefreshCurrentExhibitManipulationState();
            if (m_CurrentAppMode == ExhibitAppMode.Placement && m_EnablePlacementPreviewInPlacementMode)
                InteractionModule.ForceRefreshPlacementGuidePathPreviews();

            Debug.Log($"[放置] {currentInstanceId}");
            if (showToast) onStatusMessage?.Invoke($"已放置(未保存): {displayName}");

            return obj.transform;
        }

        // =========================================================
        //  文件 I/O 与 工具
        // =========================================================
        public void SaveAllToFiles(bool saveLayout = true, bool suppressStatusMessage = false)
        {
            ConsumeQueuedSaveRequest(ref saveLayout, ref suppressStatusMessage);

            if (m_IsSaving)
            {
                m_PendingSave = true;
                m_PendingSaveLayout |= saveLayout;
                return;
            }

            m_IsSaving = true;
            try
            {
                Transform root = GetRootTransform();
                if (root == null)
                {
                    if (!suppressStatusMessage)
                        onStatusMessage?.Invoke("没有定位点，请先调整定位点。");
                    return;
                }

                ARAnchor rootAnchor = GetRootAnchor();
                if (rootAnchor == null)
                {
                    if (!suppressStatusMessage)
                        onStatusMessage?.Invoke("定位点尚未固定，请先调整定位点。");
                    return;
                }

                SessionDataList sList = new SessionDataList();
                LayoutDataList lList = new LayoutDataList();
                lList.hasSeparateRoot = true;
                sList.root = BuildCurrentRootSessionData(rootAnchor);
                LogAnchorLifecycle($"save session root: trackableId={rootAnchor.trackableId}, {DescribeTransformPose(root)}, {DescribeAnchorState(rootAnchor)}");

                HashSet<string> processedSessionInstanceIds = CaptureDirtyPersistenceIds(m_DirtySessionInstanceIds);
                HashSet<string> processedLayoutInstanceIds = saveLayout
                    ? CaptureDirtyPersistenceIds(m_DirtyLayoutInstanceIds)
                    : null;
                ApplyDirtyPersistenceSnapshots(root, processedSessionInstanceIds, processedLayoutInstanceIds, saveLayout, suppressStatusMessage);
                sList.items.AddRange(BuildOrderedSessionSnapshotItems());
                if (saveLayout)
                    lList.items.AddRange(BuildOrderedLayoutSnapshotItems());

                if (!TryWritePersistentJsonFile(GetSessionFilePath(), sList, out string sessionSaveError))
                {
                    Debug.LogWarning($"保存会话失败: {sessionSaveError}");
                    if (!suppressStatusMessage)
                        onStatusMessage?.Invoke("保存会话失败，请重试。");
                    return;
                }

                ApplySessionSnapshotToMemory(sList, notifyStatusMessage: false);
                AcknowledgeSavedPersistenceIds(processedSessionInstanceIds, m_DirtySessionInstanceIds, m_RemovedSessionInstanceIds);
                int savedAnchorCount = sList.items.Count + (sList.root != null && GetCandidateTrackableIds(sList.root).Count > 0 ? 1 : 0);

                if (saveLayout)
                {
                    if (!TryWritePersistentJsonFile(GetLayoutFilePath(), lList, out string layoutSaveError))
                    {
                        Debug.LogWarning($"保存布局失败: {layoutSaveError}");
                        if (!suppressStatusMessage)
                            onStatusMessage?.Invoke("会话已保存，但布局保存失败，请重试。");
                        return;
                    }

                    ApplyLayoutSnapshotToMemory(lList);
                    AcknowledgeSavedPersistenceIds(processedLayoutInstanceIds, m_DirtyLayoutInstanceIds, m_RemovedLayoutInstanceIds);
                    if (!suppressStatusMessage)
                        onStatusMessage?.Invoke($"已保存布局与会话 ({savedAnchorCount})");
                }
                else
                {
                    if (!suppressStatusMessage)
                        onStatusMessage?.Invoke($"已保存会话绑定 ({savedAnchorCount})");
                }
            }
            finally
            {
                m_IsSaving = false;

                if (m_PendingSave)
                {
                    bool pendingLayout = m_PendingSaveLayout;
                    m_PendingSave = false;
                    m_PendingSaveLayout = true;
                    SaveAllToFiles(pendingLayout);
                }
            }
        }

        private IEnumerator DelaySave()
        {
            QueueSaveToFiles(saveLayout: true, suppressStatusMessage: false);
            yield break;
        }

        private IEnumerator DelaySaveSessionOnlySilently()
        {
            QueueSaveToFiles(saveLayout: false, suppressStatusMessage: true);
            yield break;
        }

        private string GetSessionFilePath()
        {
            return Path.Combine(Application.persistentDataPath, m_SessionFileName);
        }

        private string GetLayoutFilePath()
        {
            return Path.Combine(Application.persistentDataPath, m_LayoutFileName);
        }

        private static bool ShouldPrettyPrintPersistentJson()
        {
            return Application.isEditor;
        }

        private bool TryWritePersistentJsonFile<T>(string path, T value, out string errorMessage)
        {
            try
            {
                return TryWriteTextFileAtomically(path, JsonUtility.ToJson(value, ShouldPrettyPrintPersistentJson()), out errorMessage);
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        private static bool TryWriteTextFileAtomically(string path, string content, out string errorMessage)
        {
            errorMessage = string.Empty;
            string tempPath = path + ".tmp";

            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                if (File.Exists(path) && string.Equals(File.ReadAllText(path), content, StringComparison.Ordinal))
                    return true;

                File.WriteAllText(tempPath, content);

                if (File.Exists(path))
                {
                    try
                    {
                        File.Replace(tempPath, path, null);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        File.Delete(path);
                        File.Move(tempPath, path);
                    }
                    catch (IOException)
                    {
                        File.Delete(path);
                        File.Move(tempPath, path);
                    }
                }
                else
                {
                    File.Move(tempPath, path);
                }

                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    try
                    {
                        File.Delete(tempPath);
                    }
                    catch
                    {
                    }
                }
            }
        }

        public void NotifyExhibitAnchorDetached(Transform exhibitTransform)
        {
            if (exhibitTransform == null)
                return;

            if (!TryParseExhibitInfo(exhibitTransform, out _, out _, out _))
                return;

            if (TryGetRegisteredExhibitInfo(exhibitTransform, out var info))
                MarkExhibitPersistenceDirty(info, markSession: true, markLayout: false);
            SetManualSelection(exhibitTransform);
            QueueSaveToFiles(saveLayout: false, suppressStatusMessage: true);
        }

        private void QueueAnchorNodeForDestruction(GameObject anchorNode, string reason = null)
        {
            if (anchorNode == null)
                return;

            int anchorNodeInstanceId = anchorNode.GetInstanceID();
            if (!m_QueuedAnchorNodeInstanceIds.Add(anchorNodeInstanceId))
            {
                if (m_EnableAnchorLifecycleLogs)
                {
                    string duplicateReason = string.IsNullOrWhiteSpace(reason) ? "unspecified" : reason;
                    LogAnchorLifecycle(
                        $"anchor_remove_skipped_duplicate: reason={duplicateReason}, anchorNode={anchorNode.name}, instanceId={anchorNodeInstanceId}");
                }
                return;
            }

            string removeReason = string.IsNullOrWhiteSpace(reason) ? "unspecified" : reason;
            var anchor = anchorNode.GetComponent<ARAnchor>();
            if (anchor != null && m_AnchorManager != null && m_AnchorManager.enabled)
            {
                try
                {
                    bool removed = m_AnchorManager.TryRemoveAnchor(anchor);
                    LogAnchorLifecycle(
                        $"anchor_remove_requested: reason={removeReason}, removed={removed}, {DescribeAnchorState(anchor)}");
                }
                catch (Exception ex)
                {
                    LogAnchorLifecycle(
                        $"anchor_remove_exception: reason={removeReason}, anchorNode={anchorNode.name}, " +
                        $"trackableId={(anchor != null ? anchor.trackableId.ToString() : "<none>")}, error={ex.GetType().Name}:{ex.Message}");
                    Debug.LogWarning($"[Anchor] 移除旧锚点失败: {anchorNode.name}, {ex.Message}", this);
                }
            }
            else
            {
                LogAnchorLifecycle(
                    $"anchor_remove_skipped: reason={removeReason}, anchorNode={anchorNode.name}, " +
                    $"hasAnchor={(anchor != null)}, managerReady={(m_AnchorManager != null && m_AnchorManager.enabled)}");
            }

            StartCoroutine(DestroyAnchorNodeDeferred(anchorNode, anchorNodeInstanceId));
        }

        private IEnumerator DestroyAnchorNodeDeferred(GameObject anchorNode, int anchorNodeInstanceId)
        {
            try
            {
                if (anchorNode == null)
                    yield break;

                anchorNode.hideFlags = HideFlags.HideInHierarchy;
                anchorNode.SetActive(false);

                yield return null;

                if (anchorNode != null)
                    Destroy(anchorNode);

                yield return null;

                if (anchorNode != null && Application.isEditor)
                    DestroyImmediate(anchorNode);
            }
            finally
            {
                m_QueuedAnchorNodeInstanceIds.Remove(anchorNodeInstanceId);
            }
        }

        private bool EnsureAnchorForTarget(Transform target, bool saveLayout, bool suppressStatusMessage = false)
        {
            if (target == null)
            {
                if (!suppressStatusMessage)
                    onStatusMessage?.Invoke("保存失败：未选中展品。");
                return false;
            }

            if (!TryParseExhibitInfo(target, out var moduleId, out var displayName, out var instanceId))
            {
                if (!suppressStatusMessage)
                    onStatusMessage?.Invoke("保存失败：当前选中物体不是有效展品。");
                return false;
            }

            ARAnchor parentAnchor = target.GetComponentInParent<ARAnchor>();
            if (parentAnchor != null)
            {
                if (!ShouldReanchorAttachedTarget(target))
                    return true;

                LogAnchorLifecycle(
                    $"reanchor required for moved exhibit: displayName={displayName}, {DescribeAnchorState(parentAnchor)}, {DescribeTransformPose(target)}");

                if (!DetachExhibitFromCurrentAnchorInternal(target, logReason: "save reanchor"))
                {
                    if (!suppressStatusMessage)
                        onStatusMessage?.Invoke($"保存失败：无法更新 {displayName} 的空间锚点。");
                    return false;
                }
            }

            if (target.GetComponentInParent<ARAnchor>() != null)
                return true;

            if (IsAnchorAttachmentPending(target))
                return true;

            if (instanceId == "0")
                m_RuntimeRootTrans = target;

            return TryBeginAnchorAttachment(
                target,
                new Pose(target.position, target.rotation),
                $"Anchor_{displayName}",
                isRoot: false,
                saveLayout: saveLayout,
                suppressStatusMessage: suppressStatusMessage,
                displayName: displayName);
        }

        private bool TrySaveExhibitPlacement(
            Transform target,
            bool saveLayout,
            bool suppressStatusMessage,
            bool updateManualSelection = true)
        {
            if (target == null)
            {
                if (!suppressStatusMessage)
                    onStatusMessage?.Invoke("保存失败：未选中展品。");
                return false;
            }

            if (GetRootAnchor() == null)
            {
                if (!suppressStatusMessage)
                    onStatusMessage?.Invoke("保存失败：请先调整并固定定位点。");
                return false;
            }

            if (!TryParseExhibitInfo(target, out var moduleId, out var displayName, out var instanceId))
            {
                if (!suppressStatusMessage)
                    onStatusMessage?.Invoke("保存失败：当前选中物体不是有效展品。");
                return false;
            }

            if (updateManualSelection)
                SetManualSelection(target);
            if (!EnsureAnchorForTarget(target, saveLayout, suppressStatusMessage))
                return false;

            MarkExhibitPersistenceDirtyByInstanceId(instanceId, markSession: true, markLayout: saveLayout);
            LogAnchorLifecycle(
                $"save exhibit queued: moduleId={moduleId}, instanceId={instanceId}, " +
                $"displayName={displayName}, saveLayout={saveLayout}, anchored={(target.GetComponentInParent<ARAnchor>() != null)}, pending={IsAnchorAttachmentPending(target)}, {DescribeTransformPose(target)}");

            if (IsAnchorAttachmentPending(target))
                return true;

            QueueSaveToFiles(saveLayout, suppressStatusMessage);
            return true;
        }

        private Transform ResolveDeleteTargetForModule(string moduleId)
        {
            if (m_ManualSelectedTarget != null &&
                TryParseExhibitInfo(m_ManualSelectedTarget, out var selectedModuleId, out _, out _) &&
                selectedModuleId == moduleId)
            {
                LogDetailedPlacementResolution(
                    $"resolve moduleId={moduleId}, selected=manual, target={DescribePlacementResolveTarget(m_ManualSelectedTarget)}");
                return m_ManualSelectedTarget;
            }

            var candidates = new List<ExhibitInfo>();
            TryGetRegisteredExhibitsByModuleId(moduleId, candidates);
            if (candidates.Count == 0)
                AppendSceneExhibitCandidatesByModuleId(moduleId, candidates);

            if (m_MainCamera == null) m_MainCamera = Camera.main;
            Transform cam = m_MainCamera != null ? m_MainCamera.transform : null;
            Transform best = null;
            int bestPriority = int.MinValue;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < candidates.Count; i++)
            {
                ExhibitInfo info = candidates[i];
                Transform t = info != null ? info.transform : null;
                if (t == null || !TryParseExhibitInfo(t, out _, out _, out _))
                    continue;

                int priority = GetDeleteTargetPriority(info, t);
                float distance = cam != null ? Vector3.Distance(cam.position, t.position) : 0f;
                if (priority > bestPriority ||
                    (priority == bestPriority && distance < bestDistance))
                {
                    bestPriority = priority;
                    bestDistance = distance;
                    best = t;
                }
            }

            if (ShouldLogDetailedPlacementResolution(moduleId, candidates.Count, best))
            {
                var sb = new StringBuilder();
                sb.Append($"resolve moduleId={moduleId}, candidates={candidates.Count}, selected={DescribePlacementResolveTarget(best)}");
                for (int i = 0; i < candidates.Count; i++)
                {
                    ExhibitInfo info = candidates[i];
                    sb.Append(" | candidate[");
                    sb.Append(i);
                    sb.Append("]=");
                    sb.Append(DescribePlacementResolveCandidate(info));
                }

                LogDetailedPlacementResolution(sb.ToString());
            }

            return best;
        }

        private int GetDeleteTargetPriority(ExhibitInfo info, Transform target)
        {
            int priority = 0;
            if (target == null)
                return priority;

            if (target.GetComponentInParent<ARAnchor>() != null)
                priority += 1000;

            if (info != null && !string.IsNullOrWhiteSpace(info.instanceID))
                priority += 100;

            if (m_ModuleContainerRoot != null && target.IsChildOf(m_ModuleContainerRoot))
                priority += 50;

            if (target.gameObject.activeInHierarchy)
                priority += 10;

            return priority;
        }

        private bool ShouldLogDetailedPlacementResolution(string moduleId, int candidateCount, Transform selectedTarget)
        {
            string currentModuleId = GetCurrentSelectedModuleId();
            return string.Equals(currentModuleId, NormalizeModuleId(moduleId), StringComparison.OrdinalIgnoreCase) ||
                   candidateCount != 1 ||
                   selectedTarget == null;
        }

        private string DescribePlacementResolveCandidate(ExhibitInfo info)
        {
            if (info == null)
                return "null";

            Transform target = info.transform;
            return $"{DescribePlacementResolveTarget(target)}, infoModuleId={info.moduleId}, infoInstanceId={info.instanceID}";
        }

        private string DescribePlacementResolveTarget(Transform target)
        {
            if (target == null)
                return "null";

            bool hasAnchor = target.GetComponentInParent<ARAnchor>() != null;
            bool isInContainer = m_ModuleContainerRoot != null && target.IsChildOf(m_ModuleContainerRoot);
            bool parsed = TryParseExhibitInfo(target, out var moduleId, out _, out var instanceId);
            return $"name={target.name}, active={target.gameObject.activeInHierarchy}, anchored={hasAnchor}, inContainer={isInContainer}, moduleId={(parsed ? moduleId : "n/a")}, instanceId={(parsed ? instanceId : "n/a")}";
        }

        private static string FormatPlacementDebugValue(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "<none>" : value;
        }

        private void LogPlacementStateSnapshot(string stage, string moduleId, Transform resolvedTarget, string extra = null)
        {
            moduleId = NormalizeModuleId(moduleId);
            string selectedModuleId = GetCurrentSelectedModuleId();
            bool sessionSaved = !string.IsNullOrEmpty(moduleId) && HasSavedSessionPlacementByModuleId(moduleId);
            bool sessionHidden = false;
            bool hasSessionHidden = !string.IsNullOrEmpty(moduleId) &&
                                    TryGetSavedSessionPlacementContentVisibilityStateByModuleId(moduleId, out sessionHidden);

            string sceneHidden = "n/a";
            if (resolvedTarget != null && TryGetRegisteredExhibitInfoInHierarchy(resolvedTarget, out var info))
                sceneHidden = info.placementContentHidden.ToString();

            bool sceneAnchored = resolvedTarget != null && resolvedTarget.GetComponentInParent<ARAnchor>() != null;
            string suffix = string.IsNullOrWhiteSpace(extra) ? string.Empty : $", {extra}";

            PlacementDebugFileLogger.Log(
                $"[PlacementState] stage={stage}, mode={m_CurrentAppMode}, " +
                $"selectedModuleId={FormatPlacementDebugValue(selectedModuleId)}, moduleId={FormatPlacementDebugValue(moduleId)}, " +
                $"target={DescribePlacementResolveTarget(resolvedTarget)}, scenePlaced={(resolvedTarget != null)}, sceneAnchored={sceneAnchored}, " +
                $"sceneHidden={sceneHidden}, sessionSaved={sessionSaved}, sessionHidden={(hasSessionHidden ? sessionHidden.ToString() : "n/a")}, " +
                $"videoOwner={FormatPlacementDebugValue(m_PlacementVideoPreviewOwnerInstanceId)}, " +
                $"pointCloudOwner={FormatPlacementDebugValue(m_PlacementPointCloudPreviewOwnerInstanceId)}{suffix}");
        }

        private void LogDetailedPlacementResolution(string message)
        {
            PlacementDebugFileLogger.Log($"[PlacementResolve] {message}");
        }

        private void LoadSessionToMemory()
        {
            CancelStartupSnapshotLoad();
            LoadSessionToMemoryInternal(notifyStatusMessage: true);
        }

        private bool ShouldLoadSessionSnapshotForCurrentMode()
        {
            return m_CurrentAppMode == ExhibitAppMode.Placement;
        }

        private bool HasSessionSnapshotRestoreData()
        {
            return m_SessionLookup.Count > 0 ||
                   m_SessionSnapshotByInstanceId.Count > 0 ||
                   HasSavedRootSessionTrackableId();
        }

        private void EnsureSessionSnapshotLoadedForCurrentMode(bool notifyStatusMessage)
        {
            if (!ShouldLoadSessionSnapshotForCurrentMode())
                return;

            if (HasSessionSnapshotRestoreData())
                return;

            LoadSessionToMemoryInternal(notifyStatusMessage);
        }

        private void LoadSessionToMemoryInternal(bool notifyStatusMessage)
        {
            string path = GetSessionFilePath();
            if (!File.Exists(path))
            {
                ResetSessionSnapshotState();
                return;
            }

            try
            {
                ApplyLoadedSessionJson(File.ReadAllText(path), notifyStatusMessage);
            }
            catch
            {
                Debug.LogWarning("加载会话失败");
            }
        }

        private static TrackableId ParseTrackableId(string idStr)
        {
            if (string.IsNullOrEmpty(idStr)) return TrackableId.invalidId;
            string[] parts = idStr.Split('-');
            if (parts.Length != 2) return TrackableId.invalidId;
            try
            {
                return new TrackableId(
                    ulong.Parse(parts[0], System.Globalization.NumberStyles.HexNumber),
                    ulong.Parse(parts[1], System.Globalization.NumberStyles.HexNumber));
            }
            catch
            {
                return TrackableId.invalidId;
            }
        }

        private void RestoreExhibitOnAnchor(ARAnchor anchor, SessionData data, bool deferPostRestoreRefresh = false)
        {
            TryParentAnchorToModuleContainer(anchor != null ? anchor.transform : null);
            if (anchor == null || data == null)
                return;

            if (anchor.transform.childCount > 0)
            {
                LogAnchorLifecycle($"restore instantiate skipped occupied after parent: trackableId={anchor.trackableId}, childCount={anchor.transform.childCount}");
                return;
            }

            if (!TryGetDeployableModuleDefinition(data.moduleId, out var definition))
            {
                LogAnchorLifecycle($"restore instantiate skipped missing deployable module: moduleId={data.moduleId}, trackableId={anchor.trackableId}");
                return;
            }

            if (definition.prefab == null)
            {
                LogAnchorLifecycle($"restore instantiate skipped missing prefab: moduleId={data.moduleId}, trackableId={anchor.trackableId}");
                return;
            }

            GameObject obj = Instantiate(definition.prefab);
            obj.name = $"Exhibit_{data.moduleId}_{data.displayName}_{data.instanceID}";
            AttachExhibitInfo(obj, data.moduleId, data.displayName, data.instanceID, data.placementContentHidden, markPersistenceDirty: false);
            obj.transform.SetParent(anchor.transform, false);
            obj.transform.localPosition = Vector3.zero;
            obj.transform.localRotation = Quaternion.identity;
            if (TryGetLayoutSnapshotScale(data.instanceID, out var savedScale))
                obj.transform.localScale = savedScale;
            bool shouldPreview = m_CurrentAppMode == ExhibitAppMode.Placement && m_EnablePlacementPreviewInPlacementMode;
            if (m_HideRestoredExhibitsUntilPlayback && !shouldPreview)
                obj.SetActive(false);
            LogAnchorLifecycle($"restore instantiated: moduleId={data.moduleId}, instanceId={data.instanceID}, hiddenUntilPlayback={m_HideRestoredExhibitsUntilPlayback && !shouldPreview}, {DescribeAnchorState(anchor)}, {DescribeTransformPose(obj.transform)}");
            if (shouldPreview)
                EnablePlacementPreviewForExhibitInPlacementMode(obj.transform);
            if (!deferPostRestoreRefresh)
                RefreshCurrentExhibitManipulationState();
            if (shouldPreview && !deferPostRestoreRefresh)
                InteractionModule.ForceRefreshPlacementGuidePathPreviews();
        }

        private void LoadLayoutFile()
        {
            CancelStartupSnapshotLoad();
            string path = GetLayoutFilePath();
            if (!File.Exists(path))
            {
                m_CachedLayout.Clear();
                m_HasLayoutLoaded = false;
                return;
            }

            try
            {
                ApplyLoadedLayoutJson(File.ReadAllText(path));
            }
            catch
            {
                Debug.LogWarning("加载布局失败");
            }
        }

        private void ApplyLoadedSessionJson(string json, bool notifyStatusMessage)
        {
            ResetSessionSnapshotState();
            if (string.IsNullOrWhiteSpace(json))
                return;

            try
            {
                var sessionList = JsonUtility.FromJson<SessionDataList>(json);
                ApplySessionSnapshotToMemory(sessionList, notifyStatusMessage);
            }
            catch
            {
                Debug.LogWarning("加载会话失败");
            }
        }

        private void ApplyLoadedLayoutJson(string json)
        {
            m_CachedLayout.Clear();
            m_HasLayoutLoaded = false;
            if (string.IsNullOrWhiteSpace(json))
                return;

            try
            {
                var list = JsonUtility.FromJson<LayoutDataList>(json);
                ApplyLayoutSnapshotToMemory(list);
            }
            catch
            {
                Debug.LogWarning("加载布局失败");
            }
        }

        private void ResetSessionSnapshotState()
        {
            m_SessionLookup.Clear();
            ResetRuntimeRestoreTrackingState();
            m_RootSessionTrackableId = TrackableId.invalidId;
            SetRootSessionTrackableIds(string.Empty);
            ResetSessionSnapshotCache();
        }

        private void ApplySessionSnapshotToMemory(SessionDataList sessionList, bool notifyStatusMessage)
        {
            ResetSessionSnapshotState();
            if (sessionList == null)
                return;

            var items = sessionList.items ?? new List<SessionData>();
            if (sessionList.root != null)
            {
                string rootOriginalTrackableId = string.IsNullOrWhiteSpace(sessionList.root.originalTrackableId)
                    ? sessionList.root.trackableId
                    : sessionList.root.originalTrackableId;
                SetRootSessionTrackableIds(rootOriginalTrackableId, sessionList.root.fallbackTrackableId);
                m_RootSessionTrackableId = ParseTrackableId(rootOriginalTrackableId);
                LogAnchorLifecycle($"load session root: trackableId={m_RootSessionTrackableId}");
            }

            foreach (var item in items)
            {
                if (item == null)
                    continue;

                SetRestoreTrackableIds(
                    item,
                    string.IsNullOrWhiteSpace(item.originalTrackableId) ? item.trackableId : item.originalTrackableId,
                    item.fallbackTrackableId);

                var candidateTrackableIds = GetCandidateTrackableIds(item);
                for (int i = 0; i < candidateTrackableIds.Count; i++)
                {
                    TrackableId tid = ParseTrackableId(candidateTrackableIds[i]);
                    if (tid == TrackableId.invalidId || m_SessionLookup.ContainsKey(tid))
                        continue;

                    m_SessionLookup.Add(tid, item);
                    LogAnchorLifecycle(
                        $"load session exhibit: moduleId={item.moduleId}, instanceId={item.instanceID}, displayName={item.displayName}, " +
                        $"{DescribeRestoreSource(item, tid.ToString())}");
                }

                CacheSessionSnapshotItem(item);
            }

            UpdateMaxIDFromSession(items);
            int totalAnchors = items.Count + (m_RootSessionTrackableId != TrackableId.invalidId ? 1 : 0);
            if (notifyStatusMessage && totalAnchors > 0)
                onStatusMessage?.Invoke($"已加载会话，等待系统恢复 {totalAnchors} 个锚点");
        }

        private void ApplyLayoutSnapshotToMemory(LayoutDataList layoutList)
        {
            ResetLayoutSnapshotCache();
            var items = layoutList != null && layoutList.items != null ? layoutList.items : new List<LayoutData>();
            m_CachedLayout = new List<LayoutData>(items);
            for (int i = 0; i < m_CachedLayout.Count; i++)
                CacheLayoutSnapshotItem(m_CachedLayout[i]);
            m_HasLayoutLoaded = m_CachedLayout.Count > 0;
        }

        private void ResetSessionSnapshotCache()
        {
            m_SessionSnapshotByInstanceId.Clear();
            m_DirtySessionInstanceIds.Clear();
            m_RemovedSessionInstanceIds.Clear();
        }

        private void ResetLayoutSnapshotCache()
        {
            m_LayoutSnapshotByInstanceId.Clear();
            m_DirtyLayoutInstanceIds.Clear();
            m_RemovedLayoutInstanceIds.Clear();
        }

        private bool TryGetLayoutSnapshotScale(string instanceId, out Vector3 scale)
        {
            scale = Vector3.one;
            instanceId = NormalizeInstanceId(instanceId);
            if (string.IsNullOrEmpty(instanceId))
                return false;

            if (!m_LayoutSnapshotByInstanceId.TryGetValue(instanceId, out var layoutData) || layoutData == null)
                return false;

            scale = layoutData.scale;
            return true;
        }

        private static string NormalizeInstanceId(string instanceId)
        {
            return string.IsNullOrWhiteSpace(instanceId) ? string.Empty : instanceId.Trim();
        }

        private void CacheSessionSnapshotItem(SessionData item)
        {
            if (item == null)
                return;

            string instanceId = NormalizeInstanceId(item.instanceID);
            if (string.IsNullOrEmpty(instanceId))
                return;

            m_SessionSnapshotByInstanceId[instanceId] = CloneSessionData(item);
        }

        private void CacheLayoutSnapshotItem(LayoutData item)
        {
            if (item == null)
                return;

            string instanceId = NormalizeInstanceId(item.instanceID);
            if (string.IsNullOrEmpty(instanceId))
                return;

            m_LayoutSnapshotByInstanceId[instanceId] = CloneLayoutData(item);
        }

        private void MarkExhibitPersistenceDirty(ExhibitInfo info, bool markSession, bool markLayout)
        {
            if (info == null)
                return;

            MarkExhibitPersistenceDirtyByInstanceId(info.instanceID, markSession, markLayout);
        }

        private void MarkExhibitPersistenceDirtyByInstanceId(string instanceId, bool markSession, bool markLayout)
        {
            instanceId = NormalizeInstanceId(instanceId);
            if (string.IsNullOrEmpty(instanceId))
                return;

            if (markSession)
            {
                m_DirtySessionInstanceIds.Add(instanceId);
                m_RemovedSessionInstanceIds.Remove(instanceId);
            }

            if (markLayout)
            {
                m_DirtyLayoutInstanceIds.Add(instanceId);
                m_RemovedLayoutInstanceIds.Remove(instanceId);
            }
        }

        private void MarkAllRegisteredExhibitPersistenceDirty(bool markSession, bool markLayout)
        {
            if (!markSession && !markLayout)
                return;

            var exhibits = CaptureRegisteredExhibitSnapshot();
            for (int i = 0; i < exhibits.Count; i++)
                MarkExhibitPersistenceDirty(exhibits[i], markSession, markLayout);
        }

        private void MarkRemovedExhibitPersistenceState(string instanceId, bool removeSession, bool removeLayout)
        {
            instanceId = NormalizeInstanceId(instanceId);
            if (string.IsNullOrEmpty(instanceId))
                return;

            if (removeSession)
            {
                m_DirtySessionInstanceIds.Add(instanceId);
                m_RemovedSessionInstanceIds.Add(instanceId);
            }

            if (removeLayout)
            {
                m_DirtyLayoutInstanceIds.Add(instanceId);
                m_RemovedLayoutInstanceIds.Add(instanceId);
            }
        }

        private void RemoveSessionLookupForInstanceId(string instanceId, TrackableId currentTrackableId = default)
        {
            instanceId = NormalizeInstanceId(instanceId);
            if (string.IsNullOrEmpty(instanceId))
                return;

            if (m_SessionSnapshotByInstanceId.TryGetValue(instanceId, out var snapshot) && snapshot != null)
            {
                var candidateTrackableIds = GetCandidateTrackableIds(snapshot);
                for (int i = 0; i < candidateTrackableIds.Count; i++)
                {
                    TrackableId candidateTrackableId = ParseTrackableId(candidateTrackableIds[i]);
                    if (candidateTrackableId == TrackableId.invalidId)
                        continue;

                    if (!m_SessionLookup.TryGetValue(candidateTrackableId, out var mappedData) ||
                        NormalizeInstanceId(mappedData != null ? mappedData.instanceID : string.Empty) != instanceId)
                    {
                        continue;
                    }

                    m_SessionLookup.Remove(candidateTrackableId);
                }
            }

            if (currentTrackableId == TrackableId.invalidId)
                return;

            if (!m_SessionLookup.TryGetValue(currentTrackableId, out var currentMappedData) ||
                NormalizeInstanceId(currentMappedData != null ? currentMappedData.instanceID : string.Empty) != instanceId)
            {
                return;
            }

            m_SessionLookup.Remove(currentTrackableId);
        }

        private static HashSet<string> CaptureDirtyPersistenceIds(HashSet<string> source)
        {
            return source != null && source.Count > 0
                ? new HashSet<string>(source, StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);
        }

        private void AcknowledgeSavedPersistenceIds(HashSet<string> processedInstanceIds, HashSet<string> dirtySet, HashSet<string> removedSet)
        {
            if (processedInstanceIds == null || processedInstanceIds.Count == 0)
                return;

            foreach (string instanceId in processedInstanceIds)
            {
                dirtySet.Remove(instanceId);
                removedSet.Remove(instanceId);
            }
        }

        private void ApplyDirtyPersistenceSnapshots(
            Transform root,
            HashSet<string> sessionInstanceIds,
            HashSet<string> layoutInstanceIds,
            bool saveLayout,
            bool suppressStatusMessage)
        {
            var allDirtyInstanceIds = new HashSet<string>(sessionInstanceIds, StringComparer.Ordinal);
            if (saveLayout && layoutInstanceIds != null)
                allDirtyInstanceIds.UnionWith(layoutInstanceIds);

            foreach (string instanceId in allDirtyInstanceIds)
            {
                bool refreshSession = sessionInstanceIds.Contains(instanceId);
                bool refreshLayout = saveLayout && layoutInstanceIds != null && layoutInstanceIds.Contains(instanceId);

                if (refreshSession && m_RemovedSessionInstanceIds.Contains(instanceId))
                    m_SessionSnapshotByInstanceId.Remove(instanceId);

                if (refreshLayout && m_RemovedLayoutInstanceIds.Contains(instanceId))
                    m_LayoutSnapshotByInstanceId.Remove(instanceId);

                if ((!refreshSession || m_RemovedSessionInstanceIds.Contains(instanceId)) &&
                    (!refreshLayout || m_RemovedLayoutInstanceIds.Contains(instanceId)))
                {
                    continue;
                }

                SessionData sessionData = null;
                LayoutData layoutData = null;
                string displayName = null;
                bool hasRegisteredExhibit = TryGetRegisteredExhibitByInstanceId(instanceId, out var info);
                bool createdSnapshot = hasRegisteredExhibit &&
                    TryCreatePersistenceSnapshotForExhibit(info, root, refreshSession, refreshLayout, out sessionData, out layoutData, out displayName);

                if (!createdSnapshot)
                {
                    if (refreshSession)
                        m_SessionSnapshotByInstanceId.Remove(instanceId);

                    if (refreshLayout)
                        m_LayoutSnapshotByInstanceId.Remove(instanceId);

                    if (!suppressStatusMessage && !string.IsNullOrWhiteSpace(displayName))
                        onStatusMessage?.Invoke($"未创建锚点，跳过保存: {displayName}");

                    continue;
                }

                if (refreshSession && sessionData != null)
                    m_SessionSnapshotByInstanceId[instanceId] = sessionData;

                if (refreshLayout && layoutData != null)
                    m_LayoutSnapshotByInstanceId[instanceId] = layoutData;
            }
        }

        private bool TryCreatePersistenceSnapshotForExhibit(
            ExhibitInfo info,
            Transform root,
            bool includeSession,
            bool includeLayout,
            out SessionData sessionData,
            out LayoutData layoutData,
            out string displayName)
        {
            sessionData = null;
            layoutData = null;
            displayName = string.Empty;

            if (info == null || info.transform == null)
                return false;

            if (!TryParseExhibitInfo(info.transform, out var moduleId, out displayName, out var instanceId))
                return false;

            ARAnchor anchor = info.transform.GetComponentInParent<ARAnchor>();
            if (anchor == null)
            {
                LogAnchorLifecycle($"save session skipped no anchor: moduleId={moduleId}, instanceId={instanceId}, displayName={displayName}, {DescribeTransformPose(info.transform)}");
                return false;
            }

            string trackableId = anchor.trackableId.ToString();
            if (includeSession)
            {
                sessionData = new SessionData
                {
                    trackableId = trackableId,
                    instanceID = instanceId,
                    moduleId = moduleId,
                    displayName = displayName,
                    placementContentHidden = info.placementContentHidden
                };
                SetRestoreTrackableIds(sessionData, trackableId);
                LogAnchorLifecycle($"save session exhibit: moduleId={moduleId}, instanceId={instanceId}, displayName={displayName}, trackableId={trackableId}, {DescribeTransformPose(info.transform)}, {DescribeAnchorState(anchor)}");
            }

            if (includeLayout)
            {
                layoutData = new LayoutData
                {
                    instanceID = instanceId,
                    moduleId = moduleId,
                    displayName = displayName,
                    relativePosition = root.InverseTransformPoint(info.transform.position),
                    relativeRotation = Quaternion.Inverse(root.rotation) * info.transform.rotation,
                    scale = info.transform.localScale,
                    placementContentHidden = info.placementContentHidden
                };
                LogAnchorLifecycle(
                    $"save layout exhibit: moduleId={moduleId}, instanceId={instanceId}, displayName={displayName}, " +
                    $"relativePos={FormatVector3(layoutData.relativePosition)}, relativeRot={FormatRotation(layoutData.relativeRotation)}, " +
                    $"{DescribeTransformPose(info.transform)}");
            }

            return true;
        }

        private List<SessionData> BuildOrderedSessionSnapshotItems()
        {
            var items = new List<SessionData>(m_SessionSnapshotByInstanceId.Values);
            items.Sort(CompareSessionDataByInstanceId);
            return items;
        }

        private List<LayoutData> BuildOrderedLayoutSnapshotItems()
        {
            var items = new List<LayoutData>(m_LayoutSnapshotByInstanceId.Values);
            items.Sort(CompareLayoutDataByInstanceId);
            return items;
        }

        private static int CompareSessionDataByInstanceId(SessionData left, SessionData right)
        {
            return CompareInstanceIds(left != null ? left.instanceID : string.Empty, right != null ? right.instanceID : string.Empty);
        }

        private static int CompareLayoutDataByInstanceId(LayoutData left, LayoutData right)
        {
            return CompareInstanceIds(left != null ? left.instanceID : string.Empty, right != null ? right.instanceID : string.Empty);
        }

        private static int CompareInstanceIds(string left, string right)
        {
            bool leftIsNumber = int.TryParse(left, out int leftValue);
            bool rightIsNumber = int.TryParse(right, out int rightValue);

            if (leftIsNumber && rightIsNumber)
                return leftValue.CompareTo(rightValue);

            if (leftIsNumber != rightIsNumber)
                return leftIsNumber ? -1 : 1;

            return string.Compare(left, right, StringComparison.Ordinal);
        }

        private static SessionData CloneSessionData(SessionData source)
        {
            if (source == null)
                return null;

            return new SessionData
            {
                trackableId = source.trackableId,
                originalTrackableId = source.originalTrackableId,
                fallbackTrackableId = source.fallbackTrackableId,
                instanceID = source.instanceID,
                moduleId = source.moduleId,
                displayName = source.displayName,
                placementContentHidden = source.placementContentHidden
            };
        }

        private static LayoutData CloneLayoutData(LayoutData source)
        {
            if (source == null)
                return null;

            return new LayoutData
            {
                instanceID = source.instanceID,
                moduleId = source.moduleId,
                displayName = source.displayName,
                relativePosition = source.relativePosition,
                relativeRotation = source.relativeRotation,
                scale = source.scale,
                placementContentHidden = source.placementContentHidden
            };
        }

        private void UpdateMaxIDFromSession(List<SessionData> items)
        {
            int max = 0;
            foreach (var i in items)
            {
                if (int.TryParse(i.instanceID, out int v) && v > max) max = v;
            }
            if (max >= m_NextID) m_NextID = max + 1;
        }

        private bool TryParseExhibitInfo(Transform t, out string moduleId, out string displayName, out string instanceId)
        {
            moduleId = string.Empty;
            displayName = string.Empty;
            instanceId = string.Empty;

            if (!TryGetRegisteredExhibitInfo(t, out var info))
                info = t != null ? t.GetComponent<ExhibitInfo>() : null;

            if (info == null) return false;

            moduleId = NormalizeModuleId(info.moduleId);
            displayName = info.displayName;
            instanceId = info.instanceID;
            return !string.IsNullOrEmpty(moduleId);
        }

        private Transform GetRootTransform()
        {
            if (m_RuntimeRootTrans != null) return m_RuntimeRootTrans;

            var rootMarker = GetRootMarker();
            if (rootMarker != null)
            {
                m_RuntimeRootTrans = rootMarker.transform;
                return m_RuntimeRootTrans;
            }

            if (TryFindLegacyRootTransform(out Transform legacyRoot))
            {
                m_RuntimeRootTrans = legacyRoot;
                return m_RuntimeRootTrans;
            }

            return null;
        }

        private ARAnchor CreateAnchorAtPose(Pose pose, string name)
        {
            if (m_AnchorManager == null)
            {
                onStatusMessage?.Invoke("创建锚点失败：未找到 ARAnchorManager。");
                return null;
            }

            GameObject anchorNode = new GameObject(name);
            anchorNode.transform.SetPositionAndRotation(pose.position, pose.rotation);
            TryParentAnchorToModuleContainer(anchorNode.transform);

            ARAnchor anchor = anchorNode.AddComponent<ARAnchor>();
            if (anchor == null)
            {
                Destroy(anchorNode);
                onStatusMessage?.Invoke($"创建锚点失败：{name}");
                return null;
            }

            return anchor;
        }

        private void AttachExhibitInfo(GameObject obj, string moduleId, string displayName, string instanceId, bool placementContentHidden = false, bool markPersistenceDirty = true)
        {
            var info = obj.GetComponent<ExhibitInfo>();
            if (info == null) info = obj.AddComponent<ExhibitInfo>();
            info.moduleId = NormalizeModuleId(moduleId);
            info.displayName = displayName;
            info.instanceID = instanceId;
            info.placementContentHidden = placementContentHidden;
            RegisterOrUpdateExhibitInfo(info);
            if (markPersistenceDirty)
                MarkExhibitPersistenceDirty(info, markSession: true, markLayout: true);

            ApplyPlacementRuntimeOverrides(obj);
            if (m_CurrentAppMode == ExhibitAppMode.Placement)
                EnsureExhibitInteractionComponents(obj);
        }

        private void ApplyPlacementPreviewToAllSceneExhibits()
        {
            var exhibits = CaptureRegisteredExhibitSnapshot();
            for (int i = 0; i < exhibits.Count; i++)
            {
                var info = exhibits[i];
                if (info == null)
                    continue;

                EnablePlacementPreviewForExhibitInPlacementMode(info.transform);
            }
        }

        private void RefreshPlacementFocusedExhibitVisibility(string reason)
        {
            if (!Application.isPlaying || m_CurrentAppMode != ExhibitAppMode.Placement)
                return;

            bool hadPendingInitialFocusedContentHide = m_PendingInitialFocusedContentHide;
            bool showAll = !m_FocusPlacementVisibilityToCurrentTarget || m_RootManipulationEnabled;
            string focusModuleId = showAll ? string.Empty : NormalizeModuleId(GetPlacementManipulationModuleId());
            if (!showAll && string.IsNullOrEmpty(focusModuleId))
                showAll = true;
            bool applyInitialFocusedContentHide = m_PendingInitialFocusedContentHide &&
                                                  !showAll &&
                                                  !string.IsNullOrEmpty(focusModuleId);

            int activeCount = 0;
            int contentShownCount = 0;
            int contentHiddenCount = 0;
            int changedCount = 0;
            int focusHiddenAppliedCount = 0;

            var exhibits = CaptureRegisteredExhibitSnapshot();
            for (int i = 0; i < exhibits.Count; i++)
            {
                var info = exhibits[i];
                Transform exhibitRoot = info != null ? info.transform : null;
                if (exhibitRoot == null)
                    continue;

                string moduleId = NormalizeModuleId(info.moduleId);
                if (string.IsNullOrEmpty(moduleId))
                    TryResolvePlacementTargetModuleId(exhibitRoot, out moduleId);

                bool isStageModule = !string.IsNullOrEmpty(moduleId) && IsStageModuleId(moduleId);
                bool isFocusedModule = !string.IsNullOrEmpty(moduleId) &&
                                       string.Equals(moduleId, focusModuleId, StringComparison.OrdinalIgnoreCase);
                bool shouldAutoHideContent = !isStageModule && !showAll && !isFocusedModule;

                bool wasActive = exhibitRoot.gameObject.activeSelf;
                if (!wasActive)
                {
                    exhibitRoot.gameObject.SetActive(true);
                    changedCount++;
                }

                if (applyInitialFocusedContentHide &&
                    shouldAutoHideContent &&
                    !info.placementContentHidden)
                {
                    info.placementContentHidden = true;
                    changedCount++;
                    focusHiddenAppliedCount++;
                }

                if (m_EnablePlacementPreviewInPlacementMode)
                    EnablePlacementPreviewForExhibitInPlacementMode(exhibitRoot);

                activeCount++;
                if (isStageModule || !info.placementContentHidden)
                    contentShownCount++;
                else
                    contentHiddenCount++;
            }

            if (applyInitialFocusedContentHide)
                m_PendingInitialFocusedContentHide = false;

            LogAnchorLifecycle(
                $"[PlacementVisibility] reason={reason}, focusEnabled={m_FocusPlacementVisibilityToCurrentTarget}, " +
                $"rootManipulation={m_RootManipulationEnabled}, focusModuleId={(string.IsNullOrEmpty(focusModuleId) ? "<all>" : focusModuleId)}, " +
                $"pendingInitialHideBefore={hadPendingInitialFocusedContentHide}, pendingInitialHideAfter={m_PendingInitialFocusedContentHide}, " +
                $"applyInitialHide={applyInitialFocusedContentHide}, " +
                $"showAll={showAll}, active={activeCount}, contentShown={contentShownCount}, contentHidden={contentHiddenCount}, " +
                $"focusHiddenApplied={focusHiddenAppliedCount}, changed={changedCount}");
        }

        private void ClearPlacementPreviewFromAllSceneExhibits()
        {
            var exhibits = CaptureRegisteredExhibitSnapshot();
            for (int i = 0; i < exhibits.Count; i++)
            {
                var info = exhibits[i];
                if (info == null)
                    continue;

                SetPlacementPreviewEnabledForExhibit(info.transform, false);
            }
        }

        private void EnablePlacementPreviewForExhibitInPlacementMode(Transform exhibitRoot)
        {
            if (exhibitRoot == null)
                return;

            if (m_CurrentAppMode != ExhibitAppMode.Placement || !m_EnablePlacementPreviewInPlacementMode)
            {
                SetPlacementPreviewEnabledForExhibit(exhibitRoot, true);
                return;
            }

            var controllers = GetExhibitPlacementPreviewCache(exhibitRoot).pointCloudControllers;
            bool hasActivePlacementPreview = HasActivePlacementPreview(exhibitRoot);

            if (controllers.Length == 0)
            {
                if (hasActivePlacementPreview)
                    RefreshActivePlacementPreviewForExhibit(exhibitRoot);
                else
                    SetPlacementPreviewEnabledForExhibit(exhibitRoot, true);
                return;
            }

            if (!ShouldAllowPlacementPointCloudPreviewForExhibit(exhibitRoot))
            {
                SuppressPlacementPointCloudPreview(exhibitRoot);
                if (hasActivePlacementPreview)
                    RefreshActivePlacementPreviewForExhibit(exhibitRoot);
                else
                    SetPlacementPreviewEnabledForExhibit(exhibitRoot, true);
                return;
            }

            bool allPrewarmed = true;
            for (int i = 0; i < controllers.Length; i++)
            {
                var controller = controllers[i];
                if (controller == null)
                    continue;

                if (!controller.IsPlacementPreviewPrewarmed)
                {
                    allPrewarmed = false;
                    controller.PrewarmForPlacementPreview();
                }
            }

            if (allPrewarmed)
            {
                if (hasActivePlacementPreview)
                    RefreshActivePlacementPreviewForExhibit(exhibitRoot);
                else
                    SetPlacementPreviewEnabledForExhibit(exhibitRoot, true);
                return;
            }

            int exhibitId = exhibitRoot.gameObject.GetInstanceID();
            if (!m_PlacementPreviewPrewarmPending.Add(exhibitId))
                return;

            StartCoroutine(EnablePlacementPreviewAfterPrewarm(exhibitRoot, controllers, exhibitId));
        }

        private IEnumerator EnablePlacementPreviewAfterPrewarm(Transform exhibitRoot, PointCloudController[] controllers, int exhibitId)
        {
            const int maxFrames = 24;
            int waitedFrames = 0;

            while (waitedFrames < maxFrames)
            {
                if (exhibitRoot == null || m_CurrentAppMode != ExhibitAppMode.Placement || !m_EnablePlacementPreviewInPlacementMode)
                    break;

                bool allPrewarmed = true;
                for (int i = 0; i < controllers.Length; i++)
                {
                    var controller = controllers[i];
                    if (controller == null)
                        continue;

                    if (!controller.IsPlacementPreviewPrewarmed)
                    {
                        allPrewarmed = false;
                        break;
                    }
                }

                if (allPrewarmed)
                    break;

                waitedFrames++;
                yield return null;
            }

            m_PlacementPreviewPrewarmPending.Remove(exhibitId);

            if (exhibitRoot == null || m_CurrentAppMode != ExhibitAppMode.Placement || !m_EnablePlacementPreviewInPlacementMode)
                yield break;

            if (HasActivePlacementPreview(exhibitRoot))
                RefreshActivePlacementPreviewForExhibit(exhibitRoot);
            else
                SetPlacementPreviewEnabledForExhibit(exhibitRoot, true);

            InteractionModule.ForceRefreshPlacementGuidePathPreviews();
        }

        private bool HasActivePlacementPreview(Transform exhibitRoot)
        {
            if (exhibitRoot == null)
                return false;

            var modules = GetExhibitPlacementPreviewCache(exhibitRoot).modules;
            if (modules == null || modules.Length == 0)
                return false;

            for (int i = 0; i < modules.Length; i++)
            {
                var module = modules[i];
                if (module != null && module.IsPlacementPreviewActive)
                    return true;
            }

            return false;
        }

        private void RefreshActivePlacementPreviewForExhibit(Transform exhibitRoot)
        {
            if (exhibitRoot == null)
                return;

            var modules = GetExhibitPlacementPreviewCache(exhibitRoot).modules;
            if (modules == null || modules.Length == 0)
                return;

            for (int i = 0; i < modules.Length; i++)
            {
                var module = modules[i];
                if (module == null)
                    continue;

                module.RefreshPlacementPreviewIfActive();
            }
        }

        private void SetPlacementPreviewEnabledForExhibit(Transform exhibitRoot, bool enabled)
        {
            if (exhibitRoot == null)
                return;

            if (enabled && !exhibitRoot.gameObject.activeSelf)
                exhibitRoot.gameObject.SetActive(true);

            var modules = GetExhibitPlacementPreviewCache(exhibitRoot).modules;
            if (modules == null || modules.Length == 0)
            {
                if (enabled && !exhibitRoot.gameObject.activeSelf)
                    exhibitRoot.gameObject.SetActive(true);
                return;
            }

            for (int i = 0; i < modules.Length; i++)
            {
                var module = modules[i];
                if (module == null)
                    continue;

                if (enabled && !module.gameObject.activeSelf)
                    module.gameObject.SetActive(true);

                if (enabled)
                    module.EnterPlacementPreview();
                else
                    module.ExitPlacementPreview();
            }
        }

        // 【新增功能】一键清展 (仅清空场景物体和Session，保留图纸 Layout)
        public void ClearScenePreserveLayout()
        {
            CancelLayoutRedeploy("clear_scene_preserve_layout");
            CancelQueuedSave();
            ClearPendingAnchorAttachments(removeAnchorTrackables: true);
            DestroyAllSceneExhibits();

            string sessionPath = GetSessionFilePath();
            if (File.Exists(sessionPath)) File.Delete(sessionPath);

            m_RuntimeRootTrans = null;
            m_NextID = 1;
            m_SessionLookup.Clear();
            m_RootSessionTrackableId = TrackableId.invalidId;
            SetRootSessionTrackableIds(string.Empty);
            ClearManualSelection(refreshUi: true);
            m_HasRestoredSessionExhibits = false;
            m_HasRestoredRootExhibit = false;
            m_HasResolvedRootForPlayback = false;
            m_RestoredModuleIds.Clear();
            m_RestoreStateByInstanceId.Clear();
            m_RestoreConfidenceByInstanceId.Clear();
            m_RestoreFailureReasonByInstanceId.Clear();
            m_RootRestoreState = RootRestoreState.Unresolved;
            m_RootRestoreConfidence = RestoreConfidence.Unknown;
            m_RootRestoreFailureReason = string.Empty;
            ResetLayoutFallbackUntilFirstAddedGate();
            m_FocusPlacementVisibilityToCurrentTarget = false;
            m_PendingInitialFocusedContentHide = false;
            m_PlacementVideoPreviewOwnerInstanceId = string.Empty;
            m_PlacementPointCloudPreviewOwnerInstanceId = string.Empty;
            ResetSessionSnapshotCache();

            onStatusMessage?.Invoke("已撤展 (图纸已保留)");
        }

        // 【原有功能】彻底清空 (恢复出厂设置，图纸也删)
        public void ClearAllAnchors()
        {
            CancelLayoutRedeploy("clear_all_anchors");
            CancelQueuedSave();
            ClearPendingAnchorAttachments(removeAnchorTrackables: true);
            DestroyAllSceneExhibits();

            File.Delete(GetSessionFilePath());
            File.Delete(GetLayoutFilePath());

            m_CachedLayout.Clear();
            m_HasLayoutLoaded = false;
            m_RuntimeRootTrans = null;
            m_NextID = 1;
            m_SessionLookup.Clear();
            m_RootSessionTrackableId = TrackableId.invalidId;
            SetRootSessionTrackableIds(string.Empty);
            m_HasRestoredSessionExhibits = false;
            m_HasRestoredRootExhibit = false;
            m_HasResolvedRootForPlayback = false;
            m_RestoredModuleIds.Clear();
            m_RestoreStateByInstanceId.Clear();
            m_RestoreConfidenceByInstanceId.Clear();
            m_RestoreFailureReasonByInstanceId.Clear();
            m_RootRestoreState = RootRestoreState.Unresolved;
            m_RootRestoreConfidence = RestoreConfidence.Unknown;
            m_RootRestoreFailureReason = string.Empty;
            ResetLayoutFallbackUntilFirstAddedGate();
            m_FocusPlacementVisibilityToCurrentTarget = false;
            m_PendingInitialFocusedContentHide = false;
            m_PlacementVideoPreviewOwnerInstanceId = string.Empty;
            m_PlacementPointCloudPreviewOwnerInstanceId = string.Empty;
            ResetSessionSnapshotCache();
            ResetLayoutSnapshotCache();
            onStatusMessage?.Invoke("全部数据已清空 (恢复出厂)");
        }

        private void DestroyAllSceneExhibits()
        {
            ClearPendingAnchorAttachments(removeAnchorTrackables: true);

            foreach (var a in FindObjectsByType<ARAnchor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Destroy(a.gameObject);

            foreach (var rootMarker in FindObjectsByType<PlacementRootMarker>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (rootMarker == null) continue;
                if (rootMarker.GetComponentInParent<ARAnchor>() != null) continue;
                Destroy(rootMarker.gameObject);
            }

            var unanchoredExhibits = CaptureRegisteredExhibitSnapshot();
            for (int i = 0; i < unanchoredExhibits.Count; i++)
            {
                var info = unanchoredExhibits[i];
                if (info == null) continue;
                if (info.GetComponentInParent<ARAnchor>() != null) continue;
                UnregisterExhibitInfo(info);
                Destroy(info.gameObject);
            }

            ClearExhibitRegistry();
        }

        private int DestroyAnchoredExhibitInstancesPreserveAnchors()
        {
            int detachedCount = 0;
            var registeredExhibits = CaptureRegisteredExhibitSnapshot();
            for (int i = 0; i < registeredExhibits.Count; i++)
            {
                var info = registeredExhibits[i];
                if (info == null)
                    continue;

                if (info.GetComponentInParent<ARAnchor>() == null)
                    continue;

                Transform exhibitRoot = info.transform;
                exhibitRoot.SetParent(null, true);
                exhibitRoot.gameObject.SetActive(false);
                UnregisterExhibitInfo(info);
                Destroy(exhibitRoot.gameObject);
                detachedCount++;
            }

            ClearManualSelection(refreshUi: true);
            return detachedCount;
        }

        private void ResetRuntimeRestoreTrackingState()
        {
            m_HasRestoredSessionExhibits = false;
            m_HasRestoredRootExhibit = false;
            m_HasResolvedRootForPlayback = false;
            m_RestoredModuleIds.Clear();
            m_RestoreStateByInstanceId.Clear();
            m_RestoreConfidenceByInstanceId.Clear();
            m_RestoreFailureReasonByInstanceId.Clear();
            m_RootRestoreState = RootRestoreState.Unresolved;
            m_RootRestoreConfidence = RestoreConfidence.Unknown;
            m_RootRestoreFailureReason = string.Empty;
            ResetLayoutFallbackUntilFirstAddedGate();
            ResetAnchorPoseConflictState();
        }

        private void ResetLayoutFallbackUntilFirstAddedGate()
        {
            m_HasSeenFirstAddedEventForLayoutFallback = false;
            m_HasLoggedWaitingForFirstAddedEventForLayoutFallback = false;
            ResetRootLayoutFallbackDiagnostics();
        }

        private void ResetAnchorPoseConflictState()
        {
            bool changed = m_HasAnchorPoseConflict || !string.IsNullOrEmpty(m_AnchorPoseConflictSummary);
            m_HasAnchorPoseConflict = false;
            m_AnchorPoseConflictSummary = string.Empty;
            m_LastPartialPoseConflictSummary = string.Empty;
            if (changed)
                AnchorPoseConflictChanged?.Invoke(false);
        }

        private void NoteAddedAnchorsProcessed(int addedCount)
        {
            if (addedCount <= 0 || m_HasSeenFirstAddedEventForLayoutFallback)
                return;

            m_HasSeenFirstAddedEventForLayoutFallback = true;
            m_HasLoggedWaitingForFirstAddedEventForLayoutFallback = false;
            LogAnchorLifecycle($"layout fallback gate opened after first added batch: added={addedCount}");
        }

        private void RefreshRelevantAnchorPoseHealth(string reason)
        {
            bool hasConflict = TryBuildRelevantAnchorPoseConflictSummary(
                out string summary,
                out string details,
                out string partialSummary,
                out string partialDetails);
            bool changed =
                m_HasAnchorPoseConflict != hasConflict ||
                !string.Equals(m_AnchorPoseConflictSummary, hasConflict ? summary : string.Empty, StringComparison.Ordinal);

            m_HasAnchorPoseConflict = hasConflict;
            m_AnchorPoseConflictSummary = hasConflict ? summary : string.Empty;

            if (!string.IsNullOrWhiteSpace(partialSummary) &&
                !string.Equals(m_LastPartialPoseConflictSummary, partialSummary, StringComparison.Ordinal))
            {
                m_LastPartialPoseConflictSummary = partialSummary;
                LogAnchorLifecycle($"partial_pose_conflict_warning: reason={reason}, summary={partialSummary}, {partialDetails}");
            }
            else if (string.IsNullOrWhiteSpace(partialSummary))
            {
                m_LastPartialPoseConflictSummary = string.Empty;
            }

            if (!changed)
                return;

            if (hasConflict)
                LogAnchorLifecycle($"all_pose_conflict_blocked: reason={reason}, summary={summary}, {details}");
            else
                LogAnchorLifecycle($"anchor pose conflict cleared: reason={reason}");

            AnchorPoseConflictChanged?.Invoke(hasConflict);
        }

        private bool TryBuildRelevantAnchorPoseConflictSummary(
            out string summary,
            out string details,
            out string partialSummary,
            out string partialDetails)
        {
            summary = string.Empty;
            details = string.Empty;
            partialSummary = string.Empty;
            partialDetails = string.Empty;
            if (m_AnchorManager == null)
                return false;

            var relevantAnchors = new List<ARAnchor>();
            foreach (var anchor in m_AnchorManager.trackables)
            {
                if (IsRelevantAnchorForPoseHealth(anchor))
                    relevantAnchors.Add(anchor);
            }

            if (relevantAnchors.Count < 2)
                return false;

            float positionToleranceSqr =
                AnchorPoseConflictPositionToleranceMeters * AnchorPoseConflictPositionToleranceMeters;
            for (int i = 0; i < relevantAnchors.Count; i++)
            {
                var group = new List<ARAnchor> { relevantAnchors[i] };
                for (int j = i + 1; j < relevantAnchors.Count; j++)
                {
                    if (AreAnchorPosesEquivalent(
                            relevantAnchors[i],
                            relevantAnchors[j],
                            positionToleranceSqr,
                            AnchorPoseConflictRotationToleranceDegrees))
                    {
                        group.Add(relevantAnchors[j]);
                    }
                }

                if (group.Count < 2)
                    continue;

                var trackableIds = new List<string>(group.Count);
                for (int groupIndex = 0; groupIndex < group.Count; groupIndex++)
                    trackableIds.Add(group[groupIndex].trackableId.ToString());

                string groupSummary = $"检测到 {group.Count}/{relevantAnchors.Count} 个恢复锚点共享同一组位姿。";
                string groupDetails =
                    $"pose={FormatPose(new Pose(group[0].transform.position, group[0].transform.rotation))}, " +
                    $"trackableIds={string.Join("|", trackableIds)}";
                if (group.Count >= relevantAnchors.Count)
                {
                    summary = $"检测到全部 {relevantAnchors.Count} 个恢复锚点共享同一组位姿。";
                    details = groupDetails;
                    return true;
                }

                partialSummary = groupSummary;
                partialDetails = groupDetails;
            }

            return false;
        }

        private bool IsRelevantAnchorForPoseHealth(ARAnchor anchor)
        {
            if (anchor == null)
                return false;

            if (m_SessionLookup.ContainsKey(anchor.trackableId) ||
                RootSessionContainsTrackableId(anchor.trackableId) ||
                anchor.trackableId == m_RootSessionTrackableId)
            {
                return true;
            }

            if (anchor.GetComponentInChildren<PlacementRootMarker>(true) != null)
                return true;

            var exhibitInfos = anchor.GetComponentsInChildren<ExhibitInfo>(true);
            for (int i = 0; i < exhibitInfos.Length; i++)
            {
                var exhibitInfo = exhibitInfos[i];
                if (exhibitInfo == null)
                    continue;

                string instanceId = NormalizeInstanceId(exhibitInfo.instanceID);
                if (!string.IsNullOrEmpty(instanceId) && m_SessionSnapshotByInstanceId.ContainsKey(instanceId))
                    return true;
            }

            return false;
        }

        private static bool AreAnchorPosesEquivalent(
            ARAnchor first,
            ARAnchor second,
            float positionToleranceSqr,
            float rotationToleranceDegrees)
        {
            if (first == null || second == null)
                return false;

            Vector3 positionDelta = first.transform.position - second.transform.position;
            if (positionDelta.sqrMagnitude > positionToleranceSqr)
                return false;

            return Quaternion.Angle(first.transform.rotation, second.transform.rotation) <= rotationToleranceDegrees;
        }

        private void NotifyUIUpdate()
        {
            if (m_AvailableSteps.Count == 0) return;
            if (!TryGetCurrentStep(out var step)) return;
            onPrefabChanged?.Invoke(step.DisplayName);
        }

        private void RefreshCurrentExhibitManipulationState()
        {
            RefreshPlacementOnlyCollidersInScene();

            foreach (var info in EnumerateRegisteredExhibits())
            {
                if (info == null)
                    continue;

                ApplyPlacementRuntimeOverrides(info.gameObject);
                if (m_CurrentAppMode == ExhibitAppMode.Placement)
                {
                    RestoreExperienceGrabbableLayers(info.transform);
                    EnsureExhibitInteractionComponents(info.gameObject);
                }
                else
                {
                    EnsureExperienceInteractionComponents(info.gameObject);
                }
            }

            bool rootEnabled = m_CurrentAppMode == ExhibitAppMode.Placement && m_RootManipulationEnabled;
            var rootMarkers = FindObjectsByType<PlacementRootMarker>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < rootMarkers.Length; i++)
            {
                var rootMarker = rootMarkers[i];
                if (rootMarker == null)
                    continue;

                rootMarker.SetManipulationEnabled(rootEnabled);
            }

            string currentModuleId = GetPlacementManipulationModuleId();
            bool allowManipulation = m_CurrentAppMode == ExhibitAppMode.Placement &&
                                     !m_RootManipulationEnabled &&
                                     !string.IsNullOrEmpty(currentModuleId);

            foreach (var info in EnumerateRegisteredExhibits())
            {
                var movable = info != null ? info.GetComponent<MovableExhibit>() : null;
                if (movable == null)
                    continue;

                bool enabled = m_CurrentAppMode == ExhibitAppMode.Experience;
                if (!enabled &&
                    allowManipulation &&
                    TryParseExhibitInfo(movable.transform, out var moduleId, out _, out _))
                {
                    enabled = string.Equals(moduleId, currentModuleId, StringComparison.OrdinalIgnoreCase);
                }

                movable.SetManipulationEnabled(enabled);
            }
        }

        private string GetCurrentSelectedModuleId()
        {
            if (!TryGetCurrentStep(out var step) || step == null)
                return string.Empty;

            return NormalizeModuleId(step.moduleId);
        }

        private string GetPlacementManipulationModuleId()
        {
            if (m_CurrentAppMode != ExhibitAppMode.Placement || m_RootManipulationEnabled)
                return string.Empty;

            if (m_ManualSelectedTarget != null &&
                TryResolvePlacementTargetModuleId(m_ManualSelectedTarget, out string manualModuleId) &&
                IsStageModuleId(manualModuleId))
            {
                return manualModuleId;
            }

            return GetCurrentSelectedModuleId();
        }

        private void EnsureExhibitInteractionComponents(GameObject exhibit)
        {
            if (exhibit == null)
                return;

            ApplyPlacementRuntimeOverrides(exhibit);

            var placementCollider = EnsurePlacementGrabCollider(exhibit.transform);
            EnsurePlacementGrabTarget(exhibit, placementCollider.transform, usePlacementColliderOnly: true);

            if (exhibit.GetComponent<MovableExhibit>() == null)
                exhibit.AddComponent<MovableExhibit>();

            EnsureSpecialChildGrabTargets(exhibit, useAutoHand: false);
        }

        private void EnsureExperienceInteractionComponents(GameObject exhibit)
        {
            if (exhibit == null)
                return;

            ApplyExperienceGrabbableLayers(exhibit.transform);
            DisableExperienceRootAutoHandGrab(exhibit);
            EnsureSpecialChildGrabTargets(exhibit, useAutoHand: true);

            if (exhibit.GetComponent<MovableExhibit>() == null)
                exhibit.AddComponent<MovableExhibit>();
        }

        private static void DisableExperienceRootAutoHandGrab(GameObject exhibit)
        {
            if (exhibit == null)
                return;

            if (exhibit.TryGetComponent(out Grabbable grabbable))
                Destroy(grabbable);

            if (exhibit.TryGetComponent(out AutoHandGrabAdapter autoHandGrabAdapter))
                Destroy(autoHandGrabAdapter);

            if (exhibit.TryGetComponent(out Rigidbody rigidbody))
            {
                rigidbody.useGravity = false;
                rigidbody.isKinematic = true;
            }

            bool hasExplicitRootInteraction =
                exhibit.GetComponent<XRBaseInteractable>() != null ||
                exhibit.GetComponent<GrabReturnToOrigin>() != null ||
                exhibit.GetComponent<InteractionDragReleaseBridge>() != null;

            if (!hasExplicitRootInteraction)
            {
                var rootColliders = exhibit.GetComponents<Collider>();
                for (int i = 0; i < rootColliders.Length; i++)
                {
                    if (rootColliders[i] is BoxCollider boxCollider)
                        Destroy(boxCollider);
                }
            }
        }

        private bool EnsureSpecialChildGrabTargets(GameObject exhibit, bool useAutoHand)
        {
            if (exhibit == null)
                return false;

            var targets = new HashSet<GameObject>();

            var childInteractables = exhibit.GetComponentsInChildren<XRGrabInteractable>(true);
            for (int i = 0; i < childInteractables.Length; i++)
            {
                XRGrabInteractable interactable = childInteractables[i];
                if (interactable != null && interactable.gameObject != exhibit)
                    targets.Add(interactable.gameObject);
            }

            var returnToOriginBehaviours = exhibit.GetComponentsInChildren<GrabReturnToOrigin>(true);
            for (int i = 0; i < returnToOriginBehaviours.Length; i++)
            {
                GrabReturnToOrigin behaviour = returnToOriginBehaviours[i];
                if (behaviour != null && behaviour.gameObject != exhibit)
                    targets.Add(behaviour.gameObject);
            }

            var dragReleaseBridges = exhibit.GetComponentsInChildren<InteractionDragReleaseBridge>(true);
            for (int i = 0; i < dragReleaseBridges.Length; i++)
            {
                InteractionDragReleaseBridge behaviour = dragReleaseBridges[i];
                if (behaviour != null && behaviour.gameObject != exhibit)
                    targets.Add(behaviour.gameObject);
            }

            foreach (GameObject target in targets)
            {
                if (useAutoHand)
                    EnableConfiguredAutoHandGrabTarget(target);
                else
                    EnsurePlacementGrabTarget(target, attachTransform: null, usePlacementColliderOnly: false);
            }

            return targets.Count > 0;
        }

        private void EnsurePlacementGrabTarget(GameObject target, Transform attachTransform, bool usePlacementColliderOnly)
        {
            if (target == null)
                return;

            EnsureGrabLifecycleComponents(target);

            Collider collider = target.GetComponent<Collider>();
            if (collider == null)
                collider = target.AddComponent<BoxCollider>();

            Rigidbody rigidbody = target.GetComponent<Rigidbody>();
            if (rigidbody == null)
                rigidbody = target.AddComponent<Rigidbody>();

            rigidbody.useGravity = false;
            rigidbody.isKinematic = true;

            XRGrabInteractable interactable = target.GetComponent<XRGrabInteractable>();
            if (interactable == null)
                interactable = target.AddComponent<XRGrabInteractable>();

            interactable.useDynamicAttach = true;
            interactable.movementType = XRBaseInteractable.MovementType.Instantaneous;
            interactable.throwOnDetach = false;

            if (usePlacementColliderOnly && attachTransform != null)
            {
                interactable.attachTransform = attachTransform;
                interactable.colliders.Clear();
                if (attachTransform.TryGetComponent(out Collider attachCollider))
                    interactable.colliders.Add(attachCollider);
            }
            else
            {
                PopulateInteractableColliders(interactable, target);
                if (interactable.attachTransform == null)
                    interactable.attachTransform = target.transform;
            }

            if (target.GetComponent<XriGrabAdapter>() == null)
                target.AddComponent<XriGrabAdapter>();

            Grabbable grabbable = target.GetComponent<Grabbable>();
            if (grabbable != null)
                grabbable.enabled = false;

            interactable.enabled = true;
        }

        private static void EnableConfiguredAutoHandGrabTarget(GameObject target)
        {
            if (target == null)
                return;

            XRGrabInteractable interactable = target.GetComponent<XRGrabInteractable>();
            if (interactable != null)
                interactable.enabled = false;

            Collider collider = target.GetComponent<Collider>();
            Rigidbody rigidbody = target.GetComponent<Rigidbody>();
            Grabbable grabbable = target.GetComponent<Grabbable>();
            GrabLifecycleRelay relay = target.GetComponent<GrabLifecycleRelay>();
            AutoHandGrabAdapter adapter = target.GetComponent<AutoHandGrabAdapter>();

            bool hasConfiguredAutoHandGrab =
                collider != null &&
                rigidbody != null &&
                grabbable != null &&
                relay != null &&
                adapter != null;

            if (!hasConfiguredAutoHandGrab)
            {
                if (grabbable != null)
                    grabbable.enabled = false;

                if (adapter != null)
                    adapter.enabled = false;

                return;
            }

            grabbable.enabled = true;
            adapter.enabled = true;
        }

        private static void EnsureGrabLifecycleComponents(GameObject target)
        {
            if (target != null && target.GetComponent<GrabLifecycleRelay>() == null)
                target.AddComponent<GrabLifecycleRelay>();
        }

        private void ApplyExperienceGrabbableLayers(Transform exhibitRoot)
        {
            if (exhibitRoot == null || !TryResolveExperienceGrabLayer(out int grabbableLayer))
                return;

            var layerState = exhibitRoot.GetComponent<ExperienceGrabbableLayerState>();
            if (layerState == null)
                layerState = exhibitRoot.gameObject.AddComponent<ExperienceGrabbableLayerState>();

            layerState.ApplyToColliders(grabbableLayer);
        }

        private static void RestoreExperienceGrabbableLayers(Transform exhibitRoot)
        {
            if (exhibitRoot == null)
                return;

            ExperienceGrabbableLayerState layerState = exhibitRoot.GetComponent<ExperienceGrabbableLayerState>();
            if (layerState != null)
                layerState.Restore();
        }

        private static bool TryResolveExperienceGrabLayer(out int layer)
        {
            layer = LayerMask.NameToLayer(ExperienceGrabLayerName);
            return layer >= 0;
        }

        private static void PopulateInteractableColliders(XRGrabInteractable interactable, GameObject target)
        {
            if (interactable == null || target == null)
                return;

            if (interactable.colliders.Count > 0)
                return;

            var colliders = target.GetComponents<Collider>();
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider != null && !collider.isTrigger)
                    interactable.colliders.Add(collider);
            }
        }

        private void ApplyPlacementRuntimeOverrides(GameObject exhibit)
        {
            if (exhibit == null)
                return;

            bool placementMode = m_CurrentAppMode == ExhibitAppMode.Placement;
            var state = exhibit.GetComponent<PlacementModeBehaviourState>();
            if (state == null)
                state = exhibit.AddComponent<PlacementModeBehaviourState>();

            ApplyPlacementColliderMode(exhibit.transform);

            XRGrabInteractable placementInteractable = exhibit.GetComponent<XRGrabInteractable>();
            if (!placementMode && placementInteractable != null)
            {
                if (placementInteractable.enabled)
                    placementInteractable.enabled = false;
                placementInteractable.colliders.Clear();
            }

            var returnToOriginBehaviours = exhibit.GetComponentsInChildren<GrabReturnToOrigin>(true);
            for (int i = 0; i < returnToOriginBehaviours.Length; i++)
                ApplyPlacementOverride(state, returnToOriginBehaviours[i], placementMode);

            var dragReleaseBridges = exhibit.GetComponentsInChildren<InteractionDragReleaseBridge>(true);
            for (int i = 0; i < dragReleaseBridges.Length; i++)
                ApplyPlacementOverride(state, dragReleaseBridges[i], placementMode);

            var childInteractables = exhibit.GetComponentsInChildren<XRBaseInteractable>(true);
            for (int i = 0; i < childInteractables.Length; i++)
            {
                XRBaseInteractable interactable = childInteractables[i];
                if (interactable == null || interactable == placementInteractable)
                    continue;

                ApplyPlacementOverride(state, interactable, placementMode);
            }

            LogPlacementDiagnosticsForTarget(exhibit.transform, $"ApplyPlacementRuntimeOverrides placementMode={placementMode}");
        }

        private void LogPlacementDiagnostics(string reason)
        {
            bool found = false;
            foreach (var info in EnumerateRegisteredExhibits())
            {
                if (info == null)
                    continue;

                string moduleId = NormalizeModuleId(info.moduleId);
                if (!string.Equals(moduleId, PlacementDiagnosticsModuleId, StringComparison.OrdinalIgnoreCase))
                    continue;

                found = true;
                LogPlacementDiagnosticsForTarget(info.transform, reason);
            }

            if (!found)
                Debug.Log($"[PlacementDebug] reason={reason}, moduleId={PlacementDiagnosticsModuleId}, exhibitInfoMatch=not-found", this);
        }

        public void LogPlacementPreviewVisibilitySnapshot(string reason)
        {
            var exhibits = CaptureRegisteredExhibitSnapshot();
            string selectedModuleId = GetCurrentSelectedModuleId();
            PlacementDebugFileLogger.Log(
                $"[PlacementPreview] reason={reason}, appMode={m_CurrentAppMode}, registeredExhibits={exhibits.Count}, " +
                $"selectedModuleId={(string.IsNullOrEmpty(selectedModuleId) ? "<none>" : selectedModuleId)}, rootManipulation={m_RootManipulationEnabled}");

            for (int i = 0; i < exhibits.Count; i++)
            {
                var info = exhibits[i];
                if (info == null || info.transform == null)
                    continue;

                Transform root = info.transform;
                var modules = root.GetComponentsInChildren<InteractionModule>(true);
                int activeModuleCount = 0;
                int previewActiveCount = 0;
                var moduleStates = new List<string>(modules.Length);
                for (int moduleIndex = 0; moduleIndex < modules.Length; moduleIndex++)
                {
                    var module = modules[moduleIndex];
                    if (module == null)
                        continue;

                    if (module.gameObject.activeSelf)
                        activeModuleCount++;
                    if (module.IsPlacementPreviewActive)
                        previewActiveCount++;

                    moduleStates.Add(
                        $"{module.name}[activeSelf={module.gameObject.activeSelf},activeInHierarchy={module.gameObject.activeInHierarchy},preview={module.IsPlacementPreviewActive},phase={module.CurrentPhase}]");
                }

                var renderers = root.GetComponentsInChildren<Renderer>(true);
                int visibleRendererCount = 0;
                for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
                {
                    var renderer = renderers[rendererIndex];
                    if (renderer == null)
                        continue;

                    if (renderer.enabled && !renderer.forceRenderingOff && renderer.gameObject.activeInHierarchy)
                        visibleRendererCount++;
                }

                string moduleStateSummary = moduleStates.Count > 0
                    ? string.Join(" | ", moduleStates)
                    : "none";
                string moduleId = NormalizeModuleId(info.moduleId);
                PlacementDebugFileLogger.Log(
                    $"[PlacementPreview] reason={reason}, moduleId={(string.IsNullOrEmpty(moduleId) ? "<none>" : moduleId)}, " +
                    $"displayName={info.displayName}, root={root.name}, selected={string.Equals(moduleId, selectedModuleId, StringComparison.OrdinalIgnoreCase)}, " +
                    $"rootActiveSelf={root.gameObject.activeSelf}, rootActiveInHierarchy={root.gameObject.activeInHierarchy}, " +
                    $"modules={modules.Length}, activeModules={activeModuleCount}, previewActiveModules={previewActiveCount}, " +
                    $"visibleRenderers={visibleRendererCount}/{renderers.Length}, moduleStates={moduleStateSummary}");
            }
        }

        private void LogPlacementDiagnosticsForTarget(Transform exhibitRoot, string reason)
        {
            if (exhibitRoot == null ||
                !TryResolvePlacementTargetModuleId(exhibitRoot, out string moduleId) ||
                !string.Equals(moduleId, PlacementDiagnosticsModuleId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Transform placementBoundsTransform = exhibitRoot.Find("PlacementBounds");
            Transform placementGrabTransform = exhibitRoot.Find(PlacementGrabColliderName);
            Transform cubeTransform = exhibitRoot.Find("Cube");

            PlacementBounds placementBounds = placementBoundsTransform != null
                ? placementBoundsTransform.GetComponent<PlacementBounds>()
                : null;
            InteractionModule interactionModule = exhibitRoot.GetComponent<InteractionModule>();
            XRGrabInteractable rootGrabInteractable = exhibitRoot.GetComponent<XRGrabInteractable>();
            XRBaseInteractable cubeInteractable = cubeTransform != null
                ? cubeTransform.GetComponent<XRBaseInteractable>()
                : null;

            string placementBoundsState = DescribeColliderState(
                placementBoundsTransform,
                placementBoundsTransform != null ? placementBoundsTransform.GetComponents<Collider>() : null);
            string placementGrabState = DescribeColliderState(
                placementGrabTransform,
                placementGrabTransform != null ? placementGrabTransform.GetComponents<Collider>() : null);
            string cubeColliderState = DescribeColliderState(
                cubeTransform,
                cubeTransform != null ? cubeTransform.GetComponents<Collider>() : null);

            Debug.Log(
                $"[PlacementDebug] reason={reason}, mode={m_CurrentAppMode}, shouldEnablePlacement={ShouldEnablePlacementCollidersForTarget(exhibitRoot)}, " +
                $"root={exhibitRoot.name}, activeInHierarchy={exhibitRoot.gameObject.activeInHierarchy}, " +
                $"interactionModule={(interactionModule != null ? $"{interactionModule.enabled}/{interactionModule.CurrentPhase}" : "missing")}, " +
                $"rootGrab={(rootGrabInteractable != null ? $"{rootGrabInteractable.enabled}, colliders={rootGrabInteractable.colliders.Count}" : "missing")}, " +
                $"placementBoundsComp={(placementBounds != null ? placementBounds.enabled.ToString() : "missing")}, " +
                $"placementBounds={placementBoundsState}, placementGrab={placementGrabState}, " +
                $"cubeInteractable={(cubeInteractable != null ? cubeInteractable.enabled.ToString() : "missing")}, cubeColliders={cubeColliderState}",
                this);
        }

        private static string DescribeColliderState(Transform target, Collider[] colliders)
        {
            if (target == null)
                return "missing";

            if (colliders == null || colliders.Length == 0)
                return $"{target.name}:none";

            string[] parts = new string[colliders.Length];
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                parts[i] = collider == null
                    ? "null"
                    : $"{collider.GetType().Name}(enabled={collider.enabled},trigger={collider.isTrigger})";
            }

            return $"{target.name}[activeSelf={target.gameObject.activeSelf},activeInHierarchy={target.gameObject.activeInHierarchy}] {string.Join(", ", parts)}";
        }

        private bool ShouldReanchorAttachedTarget(Transform target)
        {
            if (target == null)
                return false;

            const float positionEpsilon = 0.0001f;
            const float rotationEpsilonDegrees = 0.5f;

            return target.localPosition.sqrMagnitude > positionEpsilon ||
                   Quaternion.Angle(target.localRotation, Quaternion.identity) > rotationEpsilonDegrees;
        }

        private bool DetachExhibitFromCurrentAnchorInternal(Transform exhibitTransform, string logReason)
        {
            if (exhibitTransform == null)
                return false;

            if (CancelPendingAnchorAttachmentForTarget(exhibitTransform))
            {
                LogAnchorLifecycle(
                    $"detach exhibit cancelled pending anchor: reason={logReason}, {DescribeTransformPose(exhibitTransform)}");
                ParentLooseExhibitToModuleContainer(exhibitTransform);
                SetManualSelection(exhibitTransform);
                if (TryGetRegisteredExhibitInfo(exhibitTransform, out var pendingInfo))
                    MarkExhibitPersistenceDirty(pendingInfo, markSession: true, markLayout: false);
                return true;
            }

            ARAnchor parentAnchor = exhibitTransform.GetComponentInParent<ARAnchor>();
            if (parentAnchor == null)
            {
                LogAnchorLifecycle(
                    $"detach exhibit skipped no parent anchor: reason={logReason}, {DescribeTransformPose(exhibitTransform)}");
                return false;
            }

            TrackableId detachedTrackableId = parentAnchor.trackableId;
            GameObject anchorNode = parentAnchor.gameObject;
            LogAnchorLifecycle(
                $"detach exhibit from anchor: reason={logReason}, {DescribeAnchorState(parentAnchor)}, {DescribeTransformPose(exhibitTransform)}");

            exhibitTransform.SetParent(null, true);
            ParentLooseExhibitToModuleContainer(exhibitTransform);

            if (detachedTrackableId != TrackableId.invalidId)
                m_SessionLookup.Remove(detachedTrackableId);

            SetManualSelection(exhibitTransform);
            if (TryGetRegisteredExhibitInfo(exhibitTransform, out var detachedInfo))
                MarkExhibitPersistenceDirty(detachedInfo, markSession: true, markLayout: false);
            QueueAnchorNodeForDestruction(anchorNode);
            return true;
        }

        private static void ApplyPlacementOverride(PlacementModeBehaviourState state, Behaviour behaviour, bool placementMode)
        {
            if (state == null || behaviour == null)
                return;

            state.CaptureIfNeeded(behaviour);

            bool desiredEnabled = true;
            if (placementMode)
            {
                desiredEnabled = false;
            }
            else if (!state.TryGetOriginalEnabled(behaviour, out desiredEnabled))
            {
                desiredEnabled = behaviour.enabled;
            }

            if (behaviour is GrabReturnToOrigin)
            {
                Debug.Log(
                    $"[GrabReturnToOrigin] placementOverride name={behaviour.name} frame={Time.frameCount} " +
                    $"placementMode={placementMode} currentEnabled={behaviour.enabled} desiredEnabled={desiredEnabled}",
                    behaviour);
            }

            if (behaviour.enabled != desiredEnabled)
                behaviour.enabled = desiredEnabled;
        }

        private void ApplyPlacementColliderMode(Transform exhibitRoot)
        {
            if (exhibitRoot == null)
                return;

            bool enablePlacementColliders = ShouldEnablePlacementCollidersForTarget(exhibitRoot);

            Transform placementGrabTransform = exhibitRoot.Find(PlacementGrabColliderName);
            if (placementGrabTransform != null && placementGrabTransform.TryGetComponent(out BoxCollider placementGrabCollider))
                placementGrabCollider.enabled = enablePlacementColliders;

            var placementBounds = exhibitRoot.GetComponentsInChildren<PlacementBounds>(true);
            for (int i = 0; i < placementBounds.Length; i++)
            {
                PlacementBounds candidate = placementBounds[i];
                if (candidate == null)
                    continue;

                candidate.SetRuntimeCollidersEnabled(enablePlacementColliders);
            }

            Transform namedBounds = exhibitRoot.Find("PlacementBounds");
            if (namedBounds != null &&
                namedBounds.GetComponent<PlacementBounds>() == null)
            {
                SetCollidersEnabled(namedBounds.gameObject, enablePlacementColliders);
            }
        }

        private void RefreshPlacementOnlyCollidersInScene()
        {
            var placementBounds = FindObjectsByType<PlacementBounds>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < placementBounds.Length; i++)
            {
                PlacementBounds candidate = placementBounds[i];
                if (candidate == null)
                    continue;

                candidate.SetRuntimeCollidersEnabled(ShouldEnablePlacementCollidersForTarget(candidate.transform));
            }

            var transforms = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < transforms.Length; i++)
            {
                Transform candidate = transforms[i];
                if (candidate == null)
                    continue;

                if (candidate.name == PlacementGrabColliderName &&
                    candidate.TryGetComponent(out BoxCollider placementGrabCollider))
                {
                    placementGrabCollider.enabled = ShouldEnablePlacementCollidersForTarget(candidate);
                    continue;
                }

                if (candidate.name == "PlacementBounds" &&
                    candidate.GetComponent<PlacementBounds>() == null)
                {
                    SetCollidersEnabled(candidate.gameObject, ShouldEnablePlacementCollidersForTarget(candidate));
                }
            }
        }

        public bool ShouldEnablePlacementCollidersForTarget(Transform target)
        {
            if (target == null)
                return false;

            string currentModuleId = GetPlacementManipulationModuleId();
            if (m_CurrentAppMode != ExhibitAppMode.Placement ||
                m_RootManipulationEnabled ||
                string.IsNullOrEmpty(currentModuleId))
            {
                return false;
            }

            return TryResolvePlacementTargetModuleId(target, out string moduleId) &&
                   string.Equals(moduleId, currentModuleId, StringComparison.OrdinalIgnoreCase);
        }

        private bool TryResolvePlacementTargetModuleId(Transform target, out string moduleId)
        {
            moduleId = string.Empty;
            for (Transform current = target; current != null; current = current.parent)
            {
                if (TryParseExhibitInfo(current, out moduleId, out _, out _))
                    return true;

                string candidateName = NormalizeModuleId(current.name);
                if (!string.IsNullOrEmpty(candidateName) && m_DeployableModuleById.ContainsKey(candidateName))
                {
                    moduleId = candidateName;
                    return true;
                }
            }

            return false;
        }

        private static void SetCollidersEnabled(GameObject target, bool enabled)
        {
            if (target == null)
                return;

            var colliders = target.GetComponents<Collider>();
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider != null)
                    collider.enabled = enabled;
            }
        }

        private BoxCollider EnsurePlacementGrabCollider(Transform exhibitRoot)
        {
            Transform colliderTransform = exhibitRoot.Find(PlacementGrabColliderName);
            if (colliderTransform == null)
            {
                var colliderObject = new GameObject(PlacementGrabColliderName);
                colliderTransform = colliderObject.transform;
                colliderTransform.SetParent(exhibitRoot, false);
                colliderTransform.localPosition = Vector3.zero;
                colliderTransform.localRotation = Quaternion.identity;
                colliderTransform.localScale = Vector3.one;
            }

            if (TryResolvePlacementGrabLayer(out int placementGrabLayer))
                colliderTransform.gameObject.layer = placementGrabLayer;

            var boxCollider = colliderTransform.GetComponent<BoxCollider>();
            if (boxCollider == null)
                boxCollider = colliderTransform.gameObject.AddComponent<BoxCollider>();

            if (TryResolvePlacementBounds(exhibitRoot, out Transform placementBoundsTransform, out Vector3 manualCenter, out Vector3 manualSize))
            {
                colliderTransform.localPosition = placementBoundsTransform.localPosition;
                colliderTransform.localRotation = placementBoundsTransform.localRotation;
                colliderTransform.localScale = placementBoundsTransform.localScale;
                boxCollider.center = manualCenter;
                boxCollider.size = ClampPlacementColliderSize(manualSize);
            }
            else
            {
                Bounds localBounds = CalculateLocalRendererBounds(exhibitRoot);
                colliderTransform.localRotation = Quaternion.identity;
                colliderTransform.localScale = Vector3.one;
                if (localBounds.size.sqrMagnitude <= 0.000001f)
                {
                    colliderTransform.localPosition = Vector3.zero;
                    boxCollider.center = Vector3.zero;
                    boxCollider.size = new Vector3(0.2f, 0.2f, 0.2f);
                }
                else
                {
                    colliderTransform.localPosition = localBounds.center;
                    boxCollider.center = Vector3.zero;
                    boxCollider.size = ClampPlacementColliderSize(localBounds.size);
                }
            }

            boxCollider.isTrigger = false;
            boxCollider.enabled = ShouldEnablePlacementCollidersForTarget(exhibitRoot);
            return boxCollider;
        }

        private static bool TryResolvePlacementBounds(Transform exhibitRoot, out Transform boundsTransform, out Vector3 center, out Vector3 size)
        {
            boundsTransform = null;
            center = Vector3.zero;
            size = Vector3.zero;
            if (exhibitRoot == null)
                return false;

            var placementBounds = exhibitRoot.GetComponentsInChildren<PlacementBounds>(true);
            for (int i = 0; i < placementBounds.Length; i++)
            {
                PlacementBounds candidate = placementBounds[i];
                if (candidate == null)
                    continue;

                if (!candidate.TryGetLocalBox(out center, out size))
                    continue;

                boundsTransform = candidate.transform;
                return true;
            }

            Transform namedBounds = exhibitRoot.Find("PlacementBounds");
            if (namedBounds != null)
            {
                BoxCollider namedBoxCollider = namedBounds.GetComponent<BoxCollider>();
                if (namedBoxCollider != null)
                {
                    boundsTransform = namedBounds;
                    center = namedBoxCollider.center;
                    size = namedBoxCollider.size;
                    return size.x > 0f && size.y > 0f && size.z > 0f;
                }
            }

            return false;
        }

        private static Vector3 ClampPlacementColliderSize(Vector3 size)
        {
            size.x = Mathf.Max(size.x, 0.08f);
            size.y = Mathf.Max(size.y, 0.08f);
            size.z = Mathf.Max(size.z, 0.08f);
            return size;
        }

        private void ApplyPlacementInteractorLayerFiltering()
        {
            bool placementMode = m_CurrentAppMode == ExhibitAppMode.Placement;
            LayerMask placementMask = default;
            if (placementMode)
            {
                if (!TryResolvePlacementGrabLayer(out int placementGrabLayer))
                    return;

                placementMask = BuildPlacementInteractionMask(placementGrabLayer);
            }

            var rayInteractors = FindObjectsByType<UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < rayInteractors.Length; i++)
            {
                UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor rayInteractor = rayInteractors[i];
                if (rayInteractor == null)
                    continue;

                if (!m_OriginalRayInteractorMasks.ContainsKey(rayInteractor))
                    m_OriginalRayInteractorMasks.Add(rayInteractor, rayInteractor.raycastMask);

                LayerMask originalMask = m_OriginalRayInteractorMasks[rayInteractor];
                rayInteractor.raycastMask = placementMode
                    ? (originalMask & placementMask)
                    : originalMask;
            }

            var pokeInteractors = FindObjectsByType<UnityEngine.XR.Interaction.Toolkit.Interactors.XRPokeInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < pokeInteractors.Length; i++)
            {
                UnityEngine.XR.Interaction.Toolkit.Interactors.XRPokeInteractor pokeInteractor = pokeInteractors[i];
                if (pokeInteractor == null)
                    continue;

                if (!m_OriginalPokeInteractorMasks.ContainsKey(pokeInteractor))
                    m_OriginalPokeInteractorMasks.Add(pokeInteractor, pokeInteractor.physicsLayerMask);

                LayerMask originalMask = m_OriginalPokeInteractorMasks[pokeInteractor];
                pokeInteractor.physicsLayerMask = placementMode
                    ? (originalMask & placementMask)
                    : originalMask;
            }

            var nearFarInteractors = FindObjectsByType<UnityEngine.XR.Interaction.Toolkit.Interactors.NearFarInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < nearFarInteractors.Length; i++)
            {
                UnityEngine.XR.Interaction.Toolkit.Interactors.NearFarInteractor nearFarInteractor = nearFarInteractors[i];
                if (nearFarInteractor == null)
                    continue;

                if (!m_OriginalNearFarInteractorFarCastingStates.ContainsKey(nearFarInteractor))
                    m_OriginalNearFarInteractorFarCastingStates.Add(nearFarInteractor, nearFarInteractor.enableFarCasting);

                nearFarInteractor.enableFarCasting = m_OriginalNearFarInteractorFarCastingStates[nearFarInteractor];
            }

            CleanupInteractorMaskCache();
        }

        private static LayerMask BuildUiOnlyInteractionMask()
        {
            int uiLayer = LayerMask.NameToLayer("UI");
            return uiLayer >= 0
                ? (1 << uiLayer)
                : 0;
        }

        private void CleanupInteractorMaskCache()
        {
            var missingRayInteractors = new List<UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor>();
            foreach (var entry in m_OriginalRayInteractorMasks)
            {
                if (entry.Key == null)
                    missingRayInteractors.Add(entry.Key);
            }

            for (int i = 0; i < missingRayInteractors.Count; i++)
                m_OriginalRayInteractorMasks.Remove(missingRayInteractors[i]);

            var missingPokeInteractors = new List<UnityEngine.XR.Interaction.Toolkit.Interactors.XRPokeInteractor>();
            foreach (var entry in m_OriginalPokeInteractorMasks)
            {
                if (entry.Key == null)
                    missingPokeInteractors.Add(entry.Key);
            }

            for (int i = 0; i < missingPokeInteractors.Count; i++)
                m_OriginalPokeInteractorMasks.Remove(missingPokeInteractors[i]);

            var missingNearFarInteractors = new List<UnityEngine.XR.Interaction.Toolkit.Interactors.NearFarInteractor>();
            foreach (var entry in m_OriginalNearFarInteractorFarCastingStates)
            {
                if (entry.Key == null)
                    missingNearFarInteractors.Add(entry.Key);
            }

            for (int i = 0; i < missingNearFarInteractors.Count; i++)
                m_OriginalNearFarInteractorFarCastingStates.Remove(missingNearFarInteractors[i]);
        }

        private static bool TryResolvePlacementGrabLayer(out int placementGrabLayer)
        {
            placementGrabLayer = LayerMask.NameToLayer(PlacementGrabLayerName);
            return placementGrabLayer >= 0;
        }

        private static LayerMask BuildPlacementInteractionMask(int placementGrabLayer)
        {
            int uiLayer = LayerMask.NameToLayer("UI");
            int mask = 1 << placementGrabLayer;
            if (uiLayer >= 0)
                mask |= 1 << uiLayer;
            return mask;
        }
    }
}
