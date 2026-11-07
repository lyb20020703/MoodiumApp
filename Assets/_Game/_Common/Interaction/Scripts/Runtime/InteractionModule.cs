using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Interaction
{
    /// <summary> 交互阶段：初始阶段（模块激活后自动执行）→ 开始 → 出现中（播放开始效果）→ 进行阶段（可触发结束）→ 结束。 </summary>
    public enum InteractionPhase { Initial, Start, Appearing, InProgress, End }

    /// <summary> 触发器类型。开始触发器驱动「出现」，结束触发器驱动「消失」。 </summary>
    public enum TriggerType
    {
        Time = 1,
        Enter = 2,
        Leave = 3,
        Touch = 4,
        LongPress = 5,
        TouchGuide = 6
    }

    /// <summary> 开始效果类型：对所有内容类型通用的显示方式。 </summary>
    public enum AppearEffectType
    {
        Show,
        Transparency,
        Shader,
        Transform,
        KeyframeAnimation,
        Hide
    }

    /// <summary> 结束效果类型：对所有内容类型通用的收尾方式（可隐藏/显示）。 </summary>
    public enum DisappearEffectType
    {
        Hide,
        Transparency,
        Shader,
        Transform,
        KeyframeAnimation,
        Show
    }

    public enum EffectTargetType
    {
        Content = 0,
        Guide = 1,
        StageModule = 2,
        System = 3
    }

    public enum StageActionType
    {
        Show = 0,
        Hide = 1,
        PlaySdfEffect = 2
    }

    public enum GuideListActionType
    {
        Show = 0,
        Hide = 1,
        MoveToWaypoint = 2,
        ShowAndMoveToWaypoint = 3,
        PlayVoice = 4,
        PlayAnimation = 5
    }

    public enum GuideMoveStyle
    {
        Straight = 0,
        Arc = 1,
        Waypoints = 2
    }

    public enum GuidePathInterpolation
    {
        Linear = 0,
        Smooth = 1
    }

    public enum DragReleaseType { None, ReturnHome, Snap, PhysicsCollision }

    [Serializable]
    public class AppearEffectEntry
    {
        public EffectTargetType targetType = EffectTargetType.Content;
        public GameObject contentRoot;
        public string stageModuleId;
        public StageActionType stageActionType = StageActionType.Show;
        public float delay;
        public bool initialHidden;
        public float duration;
        public Transform stageSdfTransformSource;
        public bool stageSdfFollowTransformSourceDuringPlayback;
        public Texture3D stageSdfTexture;
        public bool stageSdfOverrideLocalPosition;
        public Vector3 stageSdfLocalPosition = Vector3.zero;
        public bool stageSdfOverrideLocalRotation;
        public Vector3 stageSdfLocalEulerAngles = Vector3.zero;
        public bool stageSdfOverrideLocalScale;
        public Vector3 stageSdfLocalScale = Vector3.zero;
        public SystemActionType systemActionType = SystemActionType.PlayBgm;
        public AudioClip systemAudioClip;
        [Range(0f, 1f)] public float systemAudioVolume = 1f;
        [Min(0f)] public float systemFadeSeconds = 0f;
        public AppearEffectType effectType = AppearEffectType.Transparency;
        [Range(0f, 1f)] public float appearInitialTransparency = 0f;
        [Range(0f, 1f)] public float appearTransparencyTarget = 1f;
        public string shaderAppearPropertyName = "_Reveal";
        public float shaderAppearFrom = 1f;
        public float shaderAppearTo = 0f;
        public bool transformAppearAnimatePosition;
        public bool transformAppearAnimateRotation;
        public bool transformAppearAnimateScale = true;
        public Vector3 transformAppearPositionTo = Vector3.zero;
        public Vector3 transformAppearRotationTo = Vector3.zero;
        public Vector3 transformAppearScaleTo = Vector3.one;
        public int transformAppearEase = (int)Ease.OutQuad;
        public string keyframeAppearStateOrTrigger = "Appear";

        public GuideListActionType guideAction = GuideListActionType.ShowAndMoveToWaypoint;
        public bool guideUseAnimationEffect;
        public string guideAnimationStateOrTrigger = string.Empty;
        public bool guideAnimationWaitForCompletion;
        public Transform guideWaypoint;
        public GuideMoveStyle guideMoveStyle = GuideMoveStyle.Straight;
        public Transform guidePathRoot;
        public GuidePathInterpolation guidePathInterpolation = GuidePathInterpolation.Linear;
        public bool guideMoveImmediate;
        public bool guideKeepHeight = true;
        public bool guideRotateTowardsTarget = true;
        public float guideMoveDuration = 1f;
        public float guideArriveDistance = 0.03f;
        public float guideRotateSpeedDeg = 360f;
        public AudioClip guideVoiceClip;
        [Range(0f, 1f)] public float guideVoiceVolume = 1f;
        public bool guideVoiceOverrideAnimationSettings;
        public bool guideVoicePlayAnimation = true;
        public string guideVoiceAnimationStateOrTrigger = "Ani_Water_Talk";
        public string guideVoiceIdleAnimationStateOrTrigger = "Ani_WaterBall_Idle";
    }

    [Serializable]
    public class DisappearEffectEntry
    {
        public EffectTargetType targetType = EffectTargetType.Content;
        public GameObject contentRoot;
        public string stageModuleId;
        public StageActionType stageActionType = StageActionType.Hide;
        public float delay;
        public float duration;
        public Transform stageSdfTransformSource;
        public bool stageSdfFollowTransformSourceDuringPlayback;
        public Texture3D stageSdfTexture;
        public bool stageSdfOverrideLocalPosition;
        public Vector3 stageSdfLocalPosition = Vector3.zero;
        public bool stageSdfOverrideLocalRotation;
        public Vector3 stageSdfLocalEulerAngles = Vector3.zero;
        public bool stageSdfOverrideLocalScale;
        public Vector3 stageSdfLocalScale = Vector3.zero;
        public SystemActionType systemActionType = SystemActionType.StopBgm;
        public AudioClip systemAudioClip;
        [Range(0f, 1f)] public float systemAudioVolume = 1f;
        [Min(0f)] public float systemFadeSeconds = 0f;
        public DisappearEffectType effectType = DisappearEffectType.Transparency;
        [Range(0f, 1f)] public float endTransparencyTarget = 0f;
        public string shaderDisappearPropertyName = "_Reveal";
        public float shaderDisappearFrom = 0f;
        public float shaderDisappearTo = 1f;
        public bool transformDisappearAnimatePosition;
        public bool transformDisappearAnimateRotation;
        public bool transformDisappearAnimateScale = true;
        public Vector3 transformDisappearPositionTo = Vector3.zero;
        public Vector3 transformDisappearRotationTo = Vector3.zero;
        public Vector3 transformDisappearScaleTo = Vector3.zero;
        public int transformDisappearEase = (int)Ease.OutQuad;
        public string keyframeDisappearStateOrTrigger = "Disappear";

        public GuideListActionType guideAction = GuideListActionType.Hide;
        public bool guideUseAnimationEffect;
        public string guideAnimationStateOrTrigger = string.Empty;
        public bool guideAnimationWaitForCompletion;
        public Transform guideWaypoint;
        public GuideMoveStyle guideMoveStyle = GuideMoveStyle.Straight;
        public Transform guidePathRoot;
        public GuidePathInterpolation guidePathInterpolation = GuidePathInterpolation.Linear;
        public bool guideMoveImmediate;
        public bool guideKeepHeight = true;
        public bool guideRotateTowardsTarget = true;
        public float guideMoveDuration = 1f;
        public float guideArriveDistance = 0.03f;
        public float guideRotateSpeedDeg = 360f;
        public AudioClip guideVoiceClip;
        [Range(0f, 1f)] public float guideVoiceVolume = 1f;
        public bool guideVoiceOverrideAnimationSettings;
        public bool guideVoicePlayAnimation = true;
        public string guideVoiceAnimationStateOrTrigger = "Ani_Water_Talk";
        public string guideVoiceIdleAnimationStateOrTrigger = "Ani_WaterBall_Idle";
    }

    /// <summary>
    /// 一个 InteractionModule = 一个交互模块，负责一块内容的出现/消失。场景里可放多个，各自独立。
    /// 开始/结束效果为列表，每条可配置内容根、延时、类型与参数；触发时并行执行，每条在自身完成后按自己的时间隐藏。
    /// </summary>
    public partial class InteractionModule : InteractionBase
    {
        [Header("调试")]
        [SerializeField] private bool enableVerboseLogging = false;
        private static readonly List<InteractionModule> s_instances = new List<InteractionModule>();

        /// <summary> 当前场景中所有 InteractionModule 实例（只读）。 </summary>
        public static IReadOnlyList<InteractionModule> AllInstances => s_instances;

        [Header("触发器与列表效果")]
        [Tooltip("开始触发器：触发时执行「出现」。")]
        [SerializeField] private TriggerType startTriggerType = TriggerType.Time;
        [Tooltip("结束触发器：触发时执行「结束效果」。")]
        [SerializeField] private TriggerType endTriggerType = TriggerType.Time;
        [Tooltip("初始效果列表：模块激活后自动执行一次。可用于延时显示交互体、播放入场动画等。")]
        [SerializeField] private List<AppearEffectEntry> initialEffectEntries = new List<AppearEffectEntry>();
        [Tooltip("开始效果列表：每条可配置内容根、延时、类型与参数；触发时并行执行。")]
        [SerializeField] private List<AppearEffectEntry> appearEffectEntries = new List<AppearEffectEntry>();
        [Tooltip("结束效果列表：每条可配置内容根、延时、类型与参数；每条在自身完成后即隐藏该条内容根。")]
        [SerializeField] private List<DisappearEffectEntry> disappearEffectEntries = new List<DisappearEffectEntry>();
        [Tooltip("拖拽释放行为：选择后会在本物体上自动添加对应组件（可选「无」）。")]
        [SerializeField] private DragReleaseType dragReleaseType = DragReleaseType.None;
        [Tooltip("对所有触发器类型均适用。勾选：结束之后可再次触发开始；不勾选：仅执行一次 开始→结束。")]
        [SerializeField] private bool allowRestartAfterEnd = true;

        [Header("阶段超时兜底")]
        [Tooltip("启用后，模块在任一阶段超时卡住时会自动推进到下一个阶段。")]
        [SerializeField] private bool enablePhaseTimeoutAutoAdvance = true;
        [Min(0f)]
        [SerializeField] private float initialPhaseTimeoutSeconds = 60f;
        [Min(0f)]
        [SerializeField] private float startPhaseTimeoutSeconds = 60f;
        [Min(0f)]
        [SerializeField] private float appearingPhaseTimeoutSeconds = 60f;
        [Min(0f)]
        [SerializeField] private float inProgressPhaseTimeoutSeconds = 60f;
        [Min(0f)]
        [SerializeField] private float endPhaseTimeoutSeconds = 60f;

        private const float DefaultDurationWhenZero = 0.5f;
        private const float PhaseTimeoutSafetyBufferSeconds = 0.5f;

        public TriggerType StartTriggerType => startTriggerType;
        public TriggerType EndTriggerType => endTriggerType;
        public bool AllowRestartAfterEnd => allowRestartAfterEnd;
        public int InitialEntryCount => initialEffectEntries?.Count ?? 0;
        public int AppearEntryCount => appearEffectEntries?.Count ?? 0;
        public int DisappearEntryCount => disappearEffectEntries?.Count ?? 0;
        /// <summary> 模块一次完整「开始->结束」流程完成时触发。allowRestartAfterEnd=true 时每轮都会触发。 </summary>
        public event Action<InteractionModule> Completed;
        /// <summary> 模块阶段变化时触发：previous -> current。 </summary>
        public event Action<InteractionModule, InteractionPhase, InteractionPhase> PhaseChanged;

        // ---------- 开始触发器参数 ----------
        [Header("开始触发器参数")]
        [SerializeField] private float startTimeDelay = 1f;
        [SerializeField] private Collider enterRegionCollider;
        [SerializeField] private XRBaseInteractable touchInteractable;
        [SerializeField] private XRBaseInteractable startLongPressInteractable;
        [SerializeField] private float startLongPressHoldSec = 1f;
        // ---------- 结束触发器参数 ----------
        [Header("结束触发器参数")]
        [SerializeField] private float endTimeDelay = 1f;
        [SerializeField] private Collider leaveRegionCollider;
        [SerializeField] private XRBaseInteractable endLongPressInteractable;
        [SerializeField] private float endLongPressHoldSec = 1f;
        [SerializeField] private bool guideTouchPlayAnimationOnTouch = true;
        [SerializeField] private string guideTouchAnimationStateOrTrigger = "Ani_Water_Touch";

        // ---------- 拖拽释放参数（根据 dragReleaseType 生效）----------
        [Header("拖拽释放参数")]
        [SerializeField] private bool dragReturnHomeRecordOnRelease;
        [SerializeField] private bool dragSnapToGrid;
        [SerializeField] private float dragSnapGridSize = 0.5f;
        [SerializeField] private bool dragSnapZeroVelocity = true;
        [SerializeField] private bool dragPhysicsAddRigidbodyIfMissing = true;
        [SerializeField] private bool dragPhysicsUseGravity = true;

        private IContentHandle _contentHandle;
        private IAppearEffect _appearEffect;
        private IDisappearEffect _disappearEffect;
        private readonly List<ITrigger> _triggers = new List<ITrigger>();
        private readonly List<TouchTrigger> _touchTriggers = new List<TouchTrigger>();
        private readonly List<LongPressTrigger> _longPressTriggers = new List<LongPressTrigger>();
        private readonly List<TimeTrigger> _timeTriggers = new List<TimeTrigger>();
        private readonly HashSet<IContentHandle> _appearedContents = new HashSet<IContentHandle>();
        private readonly List<IContentHandle> _currentAppearHandles = new List<IContentHandle>();
        private readonly Dictionary<int, IDragReleaseBehavior> _dragReleaseBehaviors = new Dictionary<int, IDragReleaseBehavior>();
        private readonly Dictionary<int, IContentHandle> _contentHandleCache = new Dictionary<int, IContentHandle>();
        private readonly List<Tween> _delayedCalls = new List<Tween>();
        private bool _startedOnce;

        /// <summary> 当前交互阶段：初始阶段 / 开始阶段 / 出现中 / 进行阶段 / 结束阶段。 </summary>
        public InteractionPhase CurrentPhase => _currentPhase;
        public int TriggerCount => _triggers.Count;
        public int AppearedContentCount => _appearedContents.Count;
        public int DelayedCallCount => _delayedCalls.Count;
        private InteractionPhase _currentPhase = InteractionPhase.Start;
        /// <summary> 当外部在「初始阶段」请求开始时挂起，待初始阶段完成并进入开始阶段后执行。 </summary>
        private bool _pendingStartWhileInitial;
        /// <summary> 当结束触发器在「出现中」阶段提前触发时，记录一次挂起的结束请求，待开始效果全部完成后立即执行结束效果列表。 </summary>
        private bool _pendingEndWhileAppearing;

        public override bool TryResolveModuleId(out string moduleId)
        {
            if (TryResolveModuleIdFromExhibitInfo(out moduleId))
                return true;

            return TryResolveModuleIdFromGameObjectName(out moduleId);
        }

        private void NotifyCompleted()
        {
            FinalizePhaseTimingCycleIfActive("module_completed");
            LogModuleState($"completed allowRestartAfterEnd={allowRestartAfterEnd}");
            LogInteractionFile("InteractionPhase", $"completed allowRestartAfterEnd={allowRestartAfterEnd}");
            Completed?.Invoke(this);
            NotifyCompletedForExtensions();
        }

        private void SetPhase(InteractionPhase nextPhase)
        {
            if (!_phaseTimingCycleActive)
                BeginPhaseTimingCycle(nextPhase, "set_phase");
            else if (_currentPhase != nextPhase)
                TrackPhaseTimingTransition(nextPhase, "phase_changed");

            if (_currentPhase == nextPhase)
            {
                SchedulePhaseTimeoutWatchdogForCurrentPhase();
                return;
            }

            InteractionPhase previous = _currentPhase;
            _currentPhase = nextPhase;
            if (nextPhase == InteractionPhase.End)
            {
                _activeEndPhaseVersion++;
                _endPhaseCompletionPending = true;
            }
            else if (previous == InteractionPhase.End)
            {
                _endPhaseCompletionPending = false;
            }
            LogModuleState($"phaseChanged {previous} -> {nextPhase}");
            LogInteractionFile("InteractionPhase", $"phaseChanged previous={previous} current={nextPhase}");
            PhaseChanged?.Invoke(this, previous, nextPhase);
            NotifyPhaseChangedForExtensions(previous, nextPhase);
            SchedulePhaseTimeoutWatchdogForCurrentPhase();
        }

        private void OnEnable()
        {
            if (!s_instances.Contains(this))
                s_instances.Add(this);
            LogModuleState($"OnEnable startedOnce={_startedOnce}");
            LogInteractionFile("InteractionLifecycle", $"onEnable startedOnce={_startedOnce}");
            NotifyEnabledForExtensions();

            // 运行中被再次激活时，Start 不会再次调用，需要主动重建触发器与配置。
            if (Application.isPlaying && _startedOnce)
                StartCoroutine(ApplyConfigNextFrame());
        }

        private void Start()
        {
            _startedOnce = true;
            StartCoroutine(ApplyConfigNextFrame());
        }

        private System.Collections.IEnumerator ApplyConfigNextFrame()
        {
            yield return null;
            if (_isPlacementPreviewActive)
            {
                ApplyPlacementPreviewVisibility();
                yield break;
            }
            ApplyConfig();
        }

        /// <summary>
        /// 根据开始/结束效果列表自动添加对应组件并注册，配置即生效。
        /// </summary>
        private void ApplyConfig()
        {
            CapturePlacementPreviewRuntimeStateIfNeeded();
            LogModuleState(
                $"ApplyConfig startTrigger={startTriggerType} endTrigger={endTriggerType} " +
                $"initialEntries={InitialEntryCount} appearEntries={AppearEntryCount} " +
                $"disappearEntries={DisappearEntryCount} allowRestartAfterEnd={allowRestartAfterEnd}");
            LogInteractionFile("InteractionConfig", $"applyConfig {DescribeTriggerSummary()}");
            ApplyConfigInternal();
        }

        /// <summary> 为触发器注入参数。isStartSlot 表示该触发器用于开始，isEndSlot 表示用于结束（同一组件可同时服务开始和结束）。 </summary>

        private void ApplyDragReleaseConfig(IDragReleaseBehavior behavior)
        {
            switch (dragReleaseType)
            {
                case DragReleaseType.ReturnHome:
                    if (behavior is DragReturnHome drh) drh.SetConfig(dragReturnHomeRecordOnRelease);
                    break;
                case DragReleaseType.Snap:
                    if (behavior is DragSnap ds) ds.SetConfig(dragSnapToGrid, dragSnapGridSize, dragSnapZeroVelocity);
                    break;
                case DragReleaseType.PhysicsCollision:
                    if (behavior is DragPhysicsCollision dpc) dpc.SetConfig(dragPhysicsAddRigidbodyIfMissing, dragPhysicsUseGravity);
                    break;
            }
        }



        private IDragReleaseBehavior GetOrAddDragReleaseBehavior(DragReleaseType type)
        {
            switch (type)
            {
                case DragReleaseType.ReturnHome: return GetOrAddComponent<DragReturnHome>();
                case DragReleaseType.Snap: return GetOrAddComponent<DragSnap>();
                case DragReleaseType.PhysicsCollision: return GetOrAddComponent<DragPhysicsCollision>();
                default: return null;
            }
        }

        private T GetOrAddComponent<T>() where T : Component
        {
            var c = GetComponent<T>();
            if (c == null) c = gameObject.AddComponent<T>();
            return c;
        }

        private void OnDisable()
        {
            LogModuleState("OnDisable cleanupRuntimeState");
            LogInteractionFile("InteractionLifecycle", "onDisable cleanupRuntimeState");
            NotifyDisabledForExtensions();
            CleanupRuntimeState();
        }

        private void ResetToStartPhase()
        {
            _pendingStartWhileInitial = false;
            _pendingEndWhileAppearing = false;
            FinalizePhaseTimingCycleIfActive("reset_to_start_phase");
            LogModuleState("ResetToStartPhase");
            LogInteractionFile("InteractionPhase", "reset_to_start_phase");
            SetPhase(InteractionPhase.Start);
            NotifyBackToStart();
        }

        partial void NotifyEnabledForExtensions();
        partial void NotifyDisabledForExtensions();
        partial void NotifyPhaseChangedForExtensions(InteractionPhase previous, InteractionPhase current);
        partial void NotifyCompletedForExtensions();

    }
}
