using System;
using UnityEngine;
using UnityEngine.VFX;
using VFXViewer;
using Object = UnityEngine.Object;

namespace Interaction
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Interaction/InteractionStage")]
    public sealed class InteractionStage : InteractionBase
    {
        private const string StageSdfPropertyName = "SDF";
        private const string StageSdfSpawnRatePropertyName = "Spawn Rate";
        private const string StageSdfLineLifetimePropertyName = "Line Lifetime";
        private const float MinimumSdfGracefulStopDelaySeconds = 0.05f;
        private const float StageSdfSwitchPositionThreshold = 0.001f;
        private const float StageSdfSwitchRotationThresholdDegrees = 0.1f;
        private const float StageSdfSwitchScaleThreshold = 0.001f;
        private static readonly string[] StageSdfFieldCenterPropertyCandidates = { "FieldTransform_center", "FieldTransform.center", "FieldTransform/center" };
        private static readonly string[] StageSdfFieldAnglesPropertyCandidates = { "FieldTransform_angles", "FieldTransform.angles", "FieldTransform/angles" };
        private static readonly string[] StageSdfFieldSizePropertyCandidates = { "FieldTransform_size", "FieldTransform.size", "FieldTransform/size" };
        private const float StageSdfSwitchTurbulenceParallelFactor = 0.25f;

        [Tooltip("可选。为空时优先使用父级 ExhibitInfo 的 moduleId。")]
        [SerializeField] private string stageModuleId = "";
        [Tooltip("其他模块驱动“展台”目标时，实际作用到的内容根。")]
        [SerializeField] private GameObject stageEffectRoot;
        [Tooltip("播放“VFX Graph_SDF Effect”时，优先从这个节点及其子节点查找现成的 SDF_LineModel。为空时优先使用“展台内容”。")]
        [SerializeField] private Transform stageSdfAnchor;
        [Tooltip("SDF 从一个目标平滑移动到另一个目标时的过渡时长。")]
        [SerializeField, Min(0f)] private float stageSdfSwitchTransitionDurationSeconds = 0.5f;
        [Tooltip("启用后，SDF 切换目标时会在移动路径上叠加湍流扰动，让切换更自然。")]
        [SerializeField] private bool stageSdfSwitchTurbulenceEnabled = true;
        [Tooltip("湍流横向摆动相对于移动距离的比例。")]
        [SerializeField, Range(0f, 0.5f)] private float stageSdfSwitchTurbulenceDistanceFactor = 0.12f;
        [Tooltip("湍流横向摆动的最大世界坐标偏移。")]
        [SerializeField, Min(0f)] private float stageSdfSwitchTurbulenceMaxOffset = 0.12f;
        [Tooltip("湍流扰动随时间变化的速度。")]
        [SerializeField, Min(0f)] private float stageSdfSwitchTurbulenceFrequency = 4.5f;
        [Tooltip("湍流带来的额外旋转晃动角度。")]
        [SerializeField, Range(0f, 45f)] private float stageSdfSwitchTurbulenceRotationDegrees = 8f;

        [NonSerialized] private VisualEffect _runtimeStageSdfEffect;
        [NonSerialized] private bool _runtimeInitialVisibilityApplied;
        [NonSerialized] private Transform _runtimeStageSdfTransform;
        [NonSerialized] private bool _runtimeStageSdfDefaultTransformCached;
        [NonSerialized] private bool _runtimeStageSdfDefaultTextureCached;
        [NonSerialized] private Vector3 _runtimeStageSdfDefaultLocalPosition;
        [NonSerialized] private Quaternion _runtimeStageSdfDefaultLocalRotation;
        [NonSerialized] private Vector3 _runtimeStageSdfDefaultLocalScale;
        [NonSerialized] private Texture _runtimeStageSdfDefaultTexture;
        [NonSerialized] private bool _runtimeStageSdfDefaultSpawnRateCached;
        [NonSerialized] private float _runtimeStageSdfDefaultSpawnRate;
        [NonSerialized] private bool _runtimeStageSdfDefaultLineLifetimeCached;
        [NonSerialized] private float _runtimeStageSdfDefaultLineLifetime;
        [NonSerialized] private bool _runtimeStageSdfFieldTransformLookupResolved;
        [NonSerialized] private bool _runtimeStageSdfFieldTransformSupported;
        [NonSerialized] private string _runtimeStageSdfFieldCenterPropertyName;
        [NonSerialized] private string _runtimeStageSdfFieldAnglesPropertyName;
        [NonSerialized] private string _runtimeStageSdfFieldSizePropertyName;
        [NonSerialized] private bool _runtimeStageSdfDefaultFieldTransformCached;
        [NonSerialized] private Vector3 _runtimeStageSdfDefaultFieldCenter;
        [NonSerialized] private Vector3 _runtimeStageSdfDefaultFieldAngles;
        [NonSerialized] private Vector3 _runtimeStageSdfDefaultFieldSize;
        [NonSerialized] private bool _runtimeStageSdfFollowTransformActive;
        [NonSerialized] private Transform _runtimeStageSdfFollowSource;
        [NonSerialized] private bool _runtimeStageSdfFollowOverrideLocalPosition;
        [NonSerialized] private Vector3 _runtimeStageSdfFollowLocalPosition;
        [NonSerialized] private bool _runtimeStageSdfFollowOverrideLocalRotation;
        [NonSerialized] private Vector3 _runtimeStageSdfFollowLocalEulerAngles;
        [NonSerialized] private bool _runtimeStageSdfFollowOverrideLocalScale;
        [NonSerialized] private Vector3 _runtimeStageSdfFollowLocalScale;
        [NonSerialized] private bool _runtimeStageSdfTransitionActive;
        [NonSerialized] private float _runtimeStageSdfTransitionElapsed;
        [NonSerialized] private Vector3 _runtimeStageSdfTransitionStartPosition;
        [NonSerialized] private Quaternion _runtimeStageSdfTransitionStartRotation;
        [NonSerialized] private Vector3 _runtimeStageSdfTransitionStartScale;
        [NonSerialized] private float _runtimeStageSdfTransitionNoiseSeed;
        [NonSerialized] private bool _runtimeStageSdfCurrentWorldTransformValid;
        [NonSerialized] private Vector3 _runtimeStageSdfCurrentWorldPosition;
        [NonSerialized] private Quaternion _runtimeStageSdfCurrentWorldRotation;
        [NonSerialized] private Vector3 _runtimeStageSdfCurrentWorldScale;
        [NonSerialized] private bool _runtimeAppModeVisibilityInitialized;
        [NonSerialized] private ExhibitAppMode _runtimeLastKnownAppMode;

        public string StageModuleId => ModuleId;

        public GameObject StageEffectRoot => stageEffectRoot;
        public Transform StageSdfAnchor => stageSdfAnchor;
        public bool HasHideableStageContent => stageEffectRoot != null;
        public bool IsStageContentHidden => stageEffectRoot == null || !stageEffectRoot.activeSelf;

        private void Start()
        {
            if (!Application.isPlaying || _runtimeInitialVisibilityApplied)
                return;

            _runtimeInitialVisibilityApplied = true;
            ApplyStageVisibilityForCurrentAppMode(force: true);
        }

        private void OnEnable()
        {
            if (!Application.isPlaying)
                return;

            ApplyStageVisibilityForCurrentAppMode(force: true);
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying)
                return;

            ApplyStageVisibilityForCurrentAppMode(force: false);

            if (!_runtimeStageSdfTransitionActive && !_runtimeStageSdfFollowTransformActive)
                return;

            if (!TryGetStageSdfEffect(out var visualEffect) || visualEffect == null)
            {
                ClearStageSdfTransitionState();
                ClearStageSdfFollowState();
                _runtimeStageSdfCurrentWorldTransformValid = false;
                return;
            }

            if (_runtimeStageSdfTransitionActive)
            {
                UpdateStageSdfTransition(
                    visualEffect,
                    visualEffect.transform,
                    _runtimeStageSdfFollowSource,
                    _runtimeStageSdfFollowOverrideLocalPosition,
                    _runtimeStageSdfFollowLocalPosition,
                    _runtimeStageSdfFollowOverrideLocalRotation,
                    _runtimeStageSdfFollowLocalEulerAngles,
                    _runtimeStageSdfFollowOverrideLocalScale,
                    _runtimeStageSdfFollowLocalScale);
                return;
            }

            if (!_runtimeStageSdfFollowTransformActive)
                return;

            ApplyStageSdfTransformOverrides(
                visualEffect,
                visualEffect.transform,
                _runtimeStageSdfFollowSource,
                _runtimeStageSdfFollowOverrideLocalPosition,
                _runtimeStageSdfFollowLocalPosition,
                _runtimeStageSdfFollowOverrideLocalRotation,
                _runtimeStageSdfFollowLocalEulerAngles,
                _runtimeStageSdfFollowOverrideLocalScale,
                _runtimeStageSdfFollowLocalScale);
        }

        private void ApplyStageVisibilityForCurrentAppMode(bool force)
        {
            if (!TryGetCurrentAppMode(out var appMode))
                return;

            if (!force &&
                _runtimeAppModeVisibilityInitialized &&
                _runtimeLastKnownAppMode == appMode)
            {
                return;
            }

            _runtimeAppModeVisibilityInitialized = true;
            _runtimeLastKnownAppMode = appMode;

            bool shouldShowStageContent = appMode == ExhibitAppMode.Placement;
            if (!shouldShowStageContent)
            {
                StopSdfEffect();

                if (stageEffectRoot != null && stageEffectRoot.activeSelf)
                    stageEffectRoot.SetActive(false);
                return;
            }

            if (stageEffectRoot != null && !stageEffectRoot.activeSelf)
                stageEffectRoot.SetActive(true);

            PlayDefaultSdfEffectIfConfigured();
        }

        private static bool TryGetCurrentAppMode(out ExhibitAppMode appMode)
        {
            var placementManager = ExhibitPlacementManager.Instance;
            if (placementManager == null)
                placementManager = Object.FindFirstObjectByType<ExhibitPlacementManager>(FindObjectsInactive.Include);

            if (placementManager != null)
            {
                appMode = placementManager.CurrentAppMode;
                return true;
            }

            var director = Object.FindFirstObjectByType<ChapterPlacementDirector>(FindObjectsInactive.Include);
            if (director != null)
            {
                appMode = director.CurrentAppMode;
                return true;
            }

            appMode = ExhibitAppMode.Placement;
            return false;
        }

        public override bool TryResolveModuleId(out string moduleId)
        {
            moduleId = NormalizeModuleIdValue(stageModuleId);
            if (!string.IsNullOrEmpty(moduleId))
                return true;

            if (TryResolveModuleIdFromExhibitInfo(out moduleId))
                return true;

            return TryResolveModuleIdFromGameObjectName(out moduleId);
        }

        public static bool TryFindByModuleId(string stageModuleId, out InteractionStage stage)
        {
            stage = null;
            stageModuleId = NormalizeModuleIdValue(stageModuleId);
            if (string.IsNullOrEmpty(stageModuleId))
                return false;

            var manager = Object.FindFirstObjectByType<InteractionModuleManager>(FindObjectsInactive.Include);
            if (manager != null)
            {
                if (manager.TryGetStageById(stageModuleId, out stage) && stage != null)
                    return true;

                manager.RefreshRegistry();
                if (manager.TryGetStageById(stageModuleId, out stage) && stage != null)
                    return true;
            }

            var stages = FindObjectsByType<InteractionStage>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            int bestScore = int.MinValue;
            for (int i = 0; i < stages.Length; i++)
            {
                var candidate = stages[i];
                if (candidate == null || !candidate.TryResolveModuleId(out var candidateId))
                    continue;

                if (!string.Equals(candidateId, stageModuleId, System.StringComparison.OrdinalIgnoreCase))
                    continue;

                int score = GetDefaultCandidateScore(candidate);
                if (score <= bestScore)
                    continue;

                bestScore = score;
                stage = candidate;
            }

            return stage != null;
        }

        public static bool TryFindEffectRootByModuleId(string stageModuleId, out GameObject root)
        {
            root = null;
            if (!TryFindByModuleId(stageModuleId, out var stage) || stage == null)
                return false;

            root = stage.StageEffectRoot;
            return root != null;
        }

        internal bool HasValidSdfEffectConfig(out string issue)
        {
            if (!TryGetStageSdfEffect(out var visualEffect) || visualEffect == null)
            {
                issue = "未找到可用的 SDF_LineModel / VisualEffect";
                return false;
            }

            if (visualEffect.visualEffectAsset == null)
            {
                issue = "已找到 SDF 节点，但未配置 SDF 特效图";
                return false;
            }

            issue = string.Empty;
            return true;
        }

        internal bool TryPlaySdfEffect(
            float duration,
            Transform transformSource,
            bool followTransformSourceDuringPlayback,
            Texture sdfTexture,
            bool overrideLocalPosition,
            Vector3 localPosition,
            bool overrideLocalRotation,
            Vector3 localEulerAngles,
            bool overrideLocalScale,
            Vector3 localScale,
            Action onComplete)
        {
            return TryPlaySdfEffectInternal(
                duration,
                transformSource,
                followTransformSourceDuringPlayback,
                sdfTexture,
                overrideLocalPosition,
                localPosition,
                overrideLocalRotation,
                localEulerAngles,
                overrideLocalScale,
                localScale,
                autoStop: true,
                allowTransitionWhenActive: true,
                onComplete);
        }

        private bool TryPlaySdfEffectInternal(
            float duration,
            Transform transformSource,
            bool followTransformSourceDuringPlayback,
            Texture sdfTexture,
            bool overrideLocalPosition,
            Vector3 localPosition,
            bool overrideLocalRotation,
            Vector3 localEulerAngles,
            bool overrideLocalScale,
            Vector3 localScale,
            bool autoStop,
            bool allowTransitionWhenActive,
            Action onComplete)
        {
            if (!HasValidSdfEffectConfig(out var issue) || !TryGetStageSdfEffect(out var visualEffect) || visualEffect == null)
            {
                Debug.LogWarning($"展台 {ModuleId} 无法播放 SDF 特效：{issue}");
                onComplete?.Invoke();
                return false;
            }

            var host = visualEffect.gameObject;
            bool wasActive = host.activeSelf;
            bool shouldBlendTransformSwitch = wasActive &&
                                             allowTransitionWhenActive &&
                                             IsStageSdfTransformSwitchRequired(
                                                 host.transform,
                                                 transformSource,
                                                 overrideLocalPosition,
                                                 localPosition,
                                                 overrideLocalRotation,
                                                 localEulerAngles,
                                                 overrideLocalScale,
                                                 localScale);

            var runner = host.GetComponent<VFXEffectRunner>();
            if (runner == null)
                runner = host.AddComponent<VFXEffectRunner>();
            runner.Cancel();

            ApplyStageSdfTextureOverride(visualEffect, sdfTexture);
            SetStageSdfFollowState(
                followTransformSourceDuringPlayback && transformSource != null,
                transformSource,
                overrideLocalPosition,
                localPosition,
                overrideLocalRotation,
                localEulerAngles,
                overrideLocalScale,
                localScale);

            if (shouldBlendTransformSwitch)
            {
                BeginStageSdfTransition(
                    visualEffect,
                    host.transform,
                    transformSource,
                    overrideLocalPosition,
                    localPosition,
                    overrideLocalRotation,
                    localEulerAngles,
                    overrideLocalScale,
                    localScale);
            }
            else
            {
                ClearStageSdfTransitionState();
                ApplyStageSdfTransformOverrides(
                    visualEffect,
                    host.transform,
                    transformSource,
                    overrideLocalPosition,
                    localPosition,
                    overrideLocalRotation,
                    localEulerAngles,
                    overrideLocalScale,
                    localScale);
            }

            if (!host.activeSelf)
                host.SetActive(true);

            RestoreStageSdfDefaultFloatOverrides(visualEffect);
            if (!wasActive || !allowTransitionWhenActive)
                visualEffect.Reinit();
            visualEffect.Play();

            float waitDuration = duration > 0f ? duration : 0.5f;

            if (!autoStop)
            {
                onComplete?.Invoke();
                return true;
            }

            runner.InvokeAfter(waitDuration, () =>
            {
                BeginGracefulStopSdfEffect(onComplete);
            });
            return true;
        }

        private void BeginGracefulStopSdfEffect(Action onComplete)
        {
            if (!TryGetStageSdfEffect(out var visualEffect) || visualEffect == null)
            {
                ClearStageSdfTransitionState();
                StopSdfEffect();
                onComplete?.Invoke();
                return;
            }

            var host = visualEffect.gameObject;
            var runner = host.GetComponent<VFXEffectRunner>();
            runner?.Cancel();

            CacheStageSdfDefaultFloatOverrides(visualEffect);
            ApplyStageSdfGracefulStopOverrides(visualEffect);

            float waitDuration = GetStageSdfGracefulStopDelaySeconds();
            if (waitDuration <= 0f)
            {
                StopSdfEffect();
                onComplete?.Invoke();
                return;
            }

            if (runner == null)
                runner = host.AddComponent<VFXEffectRunner>();
            runner.InvokeAfter(waitDuration, () =>
            {
                StopSdfEffect();
                onComplete?.Invoke();
            });
        }

        internal void PlayDefaultSdfEffectIfConfigured()
        {
            if (!HasValidSdfEffectConfig(out _))
                return;

            TryPlaySdfEffectInternal(
                0f,
                null,
                false,
                null,
                false,
                Vector3.zero,
                false,
                Vector3.zero,
                false,
                Vector3.zero,
                false,
                true,
                null);
        }

        internal void StopSdfEffect()
        {
            if (!TryGetStageSdfEffect(out var visualEffect) || visualEffect == null)
            {
                ClearStageSdfTransitionState();
                ClearStageSdfFollowState();
                return;
            }

            var host = visualEffect.gameObject;
            var runner = host.GetComponent<VFXEffectRunner>();
            runner?.Cancel();

            ClearStageSdfTransitionState();
            ClearStageSdfFollowState();
            visualEffect.Stop();
            RestoreStageSdfDefaultTexture(visualEffect);
            RestoreStageSdfDefaultFloatOverrides(visualEffect);
            RestoreStageSdfDefaultFieldTransform(visualEffect);
            RestoreStageSdfDefaultTransform(host.transform);
            _runtimeStageSdfCurrentWorldTransformValid = false;
            if (host.activeSelf)
                host.SetActive(false);
        }

        internal bool TrySetStageContentVisible(bool visible)
        {
            if (stageEffectRoot == null)
                return false;

            if (!visible)
                StopSdfEffect();

            if (stageEffectRoot.activeSelf != visible)
                stageEffectRoot.SetActive(visible);

            if (visible)
                PlayDefaultSdfEffectIfConfigured();

            return true;
        }

        private bool TryGetStageSdfEffect(out VisualEffect visualEffect)
        {
            if (_runtimeStageSdfEffect != null)
            {
                visualEffect = _runtimeStageSdfEffect;
                return true;
            }

            var parent = ResolveStageSdfParent();
            if (parent != null)
            {
                if (parent.TryGetComponent<VisualEffect>(out var selfEffect) && selfEffect != null)
                {
                    _runtimeStageSdfEffect = selfEffect;
                    visualEffect = selfEffect;
                    return true;
                }

                var childEffect = parent.GetComponentInChildren<VisualEffect>(true);
                if (childEffect != null)
                {
                    _runtimeStageSdfEffect = childEffect;
                    visualEffect = childEffect;
                    return true;
                }
            }

            visualEffect = null;
            return false;
        }

        private void ApplyStageSdfTransformOverrides(
            VisualEffect visualEffect,
            Transform target,
            Transform transformSource,
            bool overrideLocalPosition,
            Vector3 localPosition,
            bool overrideLocalRotation,
            Vector3 localEulerAngles,
            bool overrideLocalScale,
            Vector3 localScale)
        {
            if (target == null)
                return;

            GetDesiredStageSdfWorldTransform(
                target,
                transformSource,
                overrideLocalPosition,
                localPosition,
                overrideLocalRotation,
                localEulerAngles,
                overrideLocalScale,
                localScale,
                out var desiredPosition,
                out var desiredRotation,
                out var desiredScale);

            ApplyStageSdfResolvedWorldTransform(visualEffect, target, desiredPosition, desiredRotation, desiredScale);
        }

        private void SetStageSdfFollowState(
            bool enabled,
            Transform transformSource,
            bool overrideLocalPosition,
            Vector3 localPosition,
            bool overrideLocalRotation,
            Vector3 localEulerAngles,
            bool overrideLocalScale,
            Vector3 localScale)
        {
            _runtimeStageSdfFollowTransformActive = enabled;
            _runtimeStageSdfFollowSource = transformSource;
            _runtimeStageSdfFollowOverrideLocalPosition = overrideLocalPosition;
            _runtimeStageSdfFollowLocalPosition = localPosition;
            _runtimeStageSdfFollowOverrideLocalRotation = overrideLocalRotation;
            _runtimeStageSdfFollowLocalEulerAngles = localEulerAngles;
            _runtimeStageSdfFollowOverrideLocalScale = overrideLocalScale;
            _runtimeStageSdfFollowLocalScale = localScale;
        }

        private void ClearStageSdfFollowState()
        {
            _runtimeStageSdfFollowTransformActive = false;
            _runtimeStageSdfFollowSource = null;
        }

        private void BeginStageSdfTransition(
            VisualEffect visualEffect,
            Transform target,
            Transform transformSource,
            bool overrideLocalPosition,
            Vector3 localPosition,
            bool overrideLocalRotation,
            Vector3 localEulerAngles,
            bool overrideLocalScale,
            Vector3 localScale)
        {
            if (target == null)
            {
                ClearStageSdfTransitionState();
                return;
            }

            _runtimeStageSdfTransitionActive = true;
            _runtimeStageSdfTransitionElapsed = 0f;
            _runtimeStageSdfTransitionStartPosition = _runtimeStageSdfCurrentWorldTransformValid ? _runtimeStageSdfCurrentWorldPosition : target.position;
            _runtimeStageSdfTransitionStartRotation = _runtimeStageSdfCurrentWorldTransformValid ? _runtimeStageSdfCurrentWorldRotation : target.rotation;
            _runtimeStageSdfTransitionStartScale = _runtimeStageSdfCurrentWorldTransformValid ? _runtimeStageSdfCurrentWorldScale : target.lossyScale;
            _runtimeStageSdfTransitionNoiseSeed = CalculateStageSdfTransitionNoiseSeed(target, transformSource);

            UpdateStageSdfTransition(
                visualEffect,
                target,
                transformSource,
                overrideLocalPosition,
                localPosition,
                overrideLocalRotation,
                localEulerAngles,
                overrideLocalScale,
                localScale);
        }

        private void UpdateStageSdfTransition(
            VisualEffect visualEffect,
            Transform target,
            Transform transformSource,
            bool overrideLocalPosition,
            Vector3 localPosition,
            bool overrideLocalRotation,
            Vector3 localEulerAngles,
            bool overrideLocalScale,
            Vector3 localScale)
        {
            if (target == null)
            {
                ClearStageSdfTransitionState();
                return;
            }

            GetDesiredStageSdfWorldTransform(
                target,
                transformSource,
                overrideLocalPosition,
                localPosition,
                overrideLocalRotation,
                localEulerAngles,
                overrideLocalScale,
                localScale,
                out var desiredPosition,
                out var desiredRotation,
                out var desiredScale);

            if (!_runtimeStageSdfTransitionActive)
            {
                ApplyStageSdfResolvedWorldTransform(visualEffect, target, desiredPosition, desiredRotation, desiredScale);
                return;
            }

            _runtimeStageSdfTransitionElapsed += Time.deltaTime;
            float t = stageSdfSwitchTransitionDurationSeconds <= 0f
                ? 1f
                : Mathf.Clamp01(_runtimeStageSdfTransitionElapsed / stageSdfSwitchTransitionDurationSeconds);
            float easedT = Mathf.SmoothStep(0f, 1f, t);

            Vector3 blendedPosition = Vector3.Lerp(_runtimeStageSdfTransitionStartPosition, desiredPosition, easedT);
            Quaternion blendedRotation = Quaternion.Slerp(_runtimeStageSdfTransitionStartRotation, desiredRotation, easedT);
            Vector3 blendedScale = Vector3.Lerp(_runtimeStageSdfTransitionStartScale, desiredScale, easedT);

            ApplyStageSdfTransitionTurbulence(
                t,
                desiredPosition,
                ref blendedPosition,
                ref blendedRotation);

            ApplyStageSdfResolvedWorldTransform(
                visualEffect,
                target,
                blendedPosition,
                blendedRotation,
                blendedScale);

            if (t >= 1f)
                ClearStageSdfTransitionState();
        }

        private void ApplyStageSdfTransitionTurbulence(
            float normalizedTransition,
            Vector3 desiredPosition,
            ref Vector3 worldPosition,
            ref Quaternion worldRotation)
        {
            if (!stageSdfSwitchTurbulenceEnabled)
                return;

            float envelope = Mathf.Sin(Mathf.Clamp01(normalizedTransition) * Mathf.PI);
            if (envelope <= Mathf.Epsilon)
                return;

            Vector3 travelVector = desiredPosition - _runtimeStageSdfTransitionStartPosition;
            float travelDistance = travelVector.magnitude;
            float baseOffset = Mathf.Min(stageSdfSwitchTurbulenceMaxOffset, travelDistance * stageSdfSwitchTurbulenceDistanceFactor);
            float positionAmplitude = baseOffset * envelope;
            float rotationAmplitude = stageSdfSwitchTurbulenceRotationDegrees * envelope;
            if (positionAmplitude <= Mathf.Epsilon && rotationAmplitude <= Mathf.Epsilon)
                return;

            GetStageSdfTurbulenceBasis(travelVector, worldRotation, out var basisA, out var basisB, out var travelDirection);

            float noiseTime = _runtimeStageSdfTransitionElapsed * Mathf.Max(stageSdfSwitchTurbulenceFrequency, 0f);
            float noiseSeed = _runtimeStageSdfTransitionNoiseSeed;

            float offsetNoiseA = SampleCenteredPerlinNoise(noiseTime + 0.17f, noiseSeed + 0.71f);
            float offsetNoiseB = SampleCenteredPerlinNoise(noiseTime + 1.63f, noiseSeed + 1.91f);
            float offsetNoiseForward = SampleCenteredPerlinNoise(noiseTime + 2.41f, noiseSeed + 3.27f);
            worldPosition +=
                basisA * (offsetNoiseA * positionAmplitude) +
                basisB * (offsetNoiseB * positionAmplitude) +
                travelDirection * (offsetNoiseForward * positionAmplitude * StageSdfSwitchTurbulenceParallelFactor);

            if (rotationAmplitude <= Mathf.Epsilon)
                return;

            Vector3 rotationNoise = new Vector3(
                SampleCenteredPerlinNoise(noiseTime + 3.11f, noiseSeed + 4.33f),
                SampleCenteredPerlinNoise(noiseTime + 4.57f, noiseSeed + 5.47f),
                SampleCenteredPerlinNoise(noiseTime + 5.89f, noiseSeed + 6.79f));
            worldRotation = Quaternion.Euler(rotationNoise * rotationAmplitude) * worldRotation;
        }

        private void ClearStageSdfTransitionState()
        {
            _runtimeStageSdfTransitionActive = false;
            _runtimeStageSdfTransitionElapsed = 0f;
        }

        private static void GetStageSdfTurbulenceBasis(
            Vector3 travelVector,
            Quaternion referenceRotation,
            out Vector3 basisA,
            out Vector3 basisB,
            out Vector3 travelDirection)
        {
            if (travelVector.sqrMagnitude > 0.000001f)
            {
                travelDirection = travelVector.normalized;
            }
            else
            {
                travelDirection = referenceRotation * Vector3.forward;
                if (travelDirection.sqrMagnitude <= 0.000001f)
                    travelDirection = Vector3.forward;
            }

            basisA = Vector3.Cross(travelDirection, Vector3.up);
            if (basisA.sqrMagnitude <= 0.000001f)
                basisA = Vector3.Cross(travelDirection, referenceRotation * Vector3.right);
            if (basisA.sqrMagnitude <= 0.000001f)
                basisA = Vector3.Cross(travelDirection, Vector3.right);

            basisA.Normalize();
            basisB = Vector3.Cross(travelDirection, basisA).normalized;
        }

        private static float CalculateStageSdfTransitionNoiseSeed(Transform target, Transform transformSource)
        {
            int seed = 17;
            if (target != null)
                seed = (seed * 31) ^ target.GetInstanceID();
            if (transformSource != null)
                seed = (seed * 31) ^ transformSource.GetInstanceID();

            return Mathf.Abs(seed * 0.017f);
        }

        private static float SampleCenteredPerlinNoise(float x, float y)
        {
            return Mathf.PerlinNoise(x, y) * 2f - 1f;
        }

        private static void MatchWorldScale(Transform target, Vector3 desiredWorldScale)
        {
            if (target == null)
                return;

            var parent = target.parent;
            if (parent == null)
            {
                target.localScale = desiredWorldScale;
                return;
            }

            var parentScale = parent.lossyScale;
            target.localScale = new Vector3(
                SafeDivide(desiredWorldScale.x, parentScale.x),
                SafeDivide(desiredWorldScale.y, parentScale.y),
                SafeDivide(desiredWorldScale.z, parentScale.z));
        }

        private static float SafeDivide(float numerator, float denominator)
        {
            if (Mathf.Approximately(denominator, 0f))
                return numerator;

            return numerator / denominator;
        }

        private void ApplyStageSdfResolvedWorldTransform(
            VisualEffect visualEffect,
            Transform target,
            Vector3 worldPosition,
            Quaternion worldRotation,
            Vector3 worldScale)
        {
            bool appliedToFieldTransform = TryApplyStageSdfFieldTransform(visualEffect, target, worldPosition, worldRotation, worldScale);
            if (!appliedToFieldTransform)
                ApplyStageSdfWorldTransform(target, worldPosition, worldRotation, worldScale);

            _runtimeStageSdfCurrentWorldTransformValid = true;
            _runtimeStageSdfCurrentWorldPosition = worldPosition;
            _runtimeStageSdfCurrentWorldRotation = worldRotation;
            _runtimeStageSdfCurrentWorldScale = worldScale;
        }

        private void ApplyStageSdfWorldTransform(Transform target, Vector3 worldPosition, Quaternion worldRotation, Vector3 worldScale)
        {
            if (target == null)
                return;

            target.SetPositionAndRotation(worldPosition, worldRotation);
            MatchWorldScale(target, worldScale);
        }

        private bool TryApplyStageSdfFieldTransform(
            VisualEffect visualEffect,
            Transform target,
            Vector3 worldPosition,
            Quaternion worldRotation,
            Vector3 worldScale)
        {
            if (visualEffect == null ||
                target == null ||
                !TryResolveStageSdfFieldTransformPropertyNames(
                    visualEffect,
                    out var centerPropertyName,
                    out var anglesPropertyName,
                    out var sizePropertyName))
            {
                return false;
            }

            CacheStageSdfDefaultTransform(target);
            CacheStageSdfDefaultFieldTransform(visualEffect);
            RestoreStageSdfDefaultTransform(target);

            Vector3 localCenter = target.InverseTransformPoint(worldPosition);
            Quaternion localRotation = Quaternion.Inverse(target.rotation) * worldRotation;
            Vector3 targetScale = target.lossyScale;
            Vector3 localSize = new Vector3(
                SafeDivide(worldScale.x, targetScale.x),
                SafeDivide(worldScale.y, targetScale.y),
                SafeDivide(worldScale.z, targetScale.z));

            visualEffect.SetVector3(centerPropertyName, localCenter);
            visualEffect.SetVector3(anglesPropertyName, localRotation.eulerAngles);
            visualEffect.SetVector3(sizePropertyName, localSize);
            return true;
        }

        private void ApplyStageSdfTextureOverride(VisualEffect visualEffect, Texture sdfTexture)
        {
            if (visualEffect == null)
                return;

            CacheStageSdfDefaultTexture(visualEffect);
            if (sdfTexture != null)
            {
                visualEffect.SetTexture(StageSdfPropertyName, sdfTexture);
                return;
            }

            RestoreStageSdfDefaultTexture(visualEffect);
        }

        private void CacheStageSdfDefaultFloatOverrides(VisualEffect visualEffect)
        {
            if (visualEffect == null)
                return;

            if (!_runtimeStageSdfDefaultSpawnRateCached && visualEffect.HasFloat(StageSdfSpawnRatePropertyName))
            {
                _runtimeStageSdfDefaultSpawnRate = visualEffect.GetFloat(StageSdfSpawnRatePropertyName);
                _runtimeStageSdfDefaultSpawnRateCached = true;
            }

            if (!_runtimeStageSdfDefaultLineLifetimeCached && visualEffect.HasFloat(StageSdfLineLifetimePropertyName))
            {
                _runtimeStageSdfDefaultLineLifetime = visualEffect.GetFloat(StageSdfLineLifetimePropertyName);
                _runtimeStageSdfDefaultLineLifetimeCached = true;
            }
        }

        private void RestoreStageSdfDefaultFloatOverrides(VisualEffect visualEffect)
        {
            if (visualEffect == null)
                return;

            CacheStageSdfDefaultFloatOverrides(visualEffect);

            if (_runtimeStageSdfDefaultSpawnRateCached)
                visualEffect.SetFloat(StageSdfSpawnRatePropertyName, _runtimeStageSdfDefaultSpawnRate);
            else if (visualEffect.HasFloat(StageSdfSpawnRatePropertyName))
                visualEffect.ResetOverride(StageSdfSpawnRatePropertyName);

            if (_runtimeStageSdfDefaultLineLifetimeCached)
                visualEffect.SetFloat(StageSdfLineLifetimePropertyName, _runtimeStageSdfDefaultLineLifetime);
            else if (visualEffect.HasFloat(StageSdfLineLifetimePropertyName))
                visualEffect.ResetOverride(StageSdfLineLifetimePropertyName);
        }

        private bool TryResolveStageSdfFieldTransformPropertyNames(
            VisualEffect visualEffect,
            out string centerPropertyName,
            out string anglesPropertyName,
            out string sizePropertyName)
        {
            centerPropertyName = null;
            anglesPropertyName = null;
            sizePropertyName = null;

            if (visualEffect == null)
                return false;

            if (!_runtimeStageSdfFieldTransformLookupResolved)
            {
                _runtimeStageSdfFieldCenterPropertyName = ResolveFirstStageSdfVector3PropertyName(visualEffect, StageSdfFieldCenterPropertyCandidates);
                _runtimeStageSdfFieldAnglesPropertyName = ResolveFirstStageSdfVector3PropertyName(visualEffect, StageSdfFieldAnglesPropertyCandidates);
                _runtimeStageSdfFieldSizePropertyName = ResolveFirstStageSdfVector3PropertyName(visualEffect, StageSdfFieldSizePropertyCandidates);
                _runtimeStageSdfFieldTransformSupported =
                    !string.IsNullOrEmpty(_runtimeStageSdfFieldCenterPropertyName) &&
                    !string.IsNullOrEmpty(_runtimeStageSdfFieldAnglesPropertyName) &&
                    !string.IsNullOrEmpty(_runtimeStageSdfFieldSizePropertyName);
                _runtimeStageSdfFieldTransformLookupResolved = true;
            }

            if (!_runtimeStageSdfFieldTransformSupported)
                return false;

            centerPropertyName = _runtimeStageSdfFieldCenterPropertyName;
            anglesPropertyName = _runtimeStageSdfFieldAnglesPropertyName;
            sizePropertyName = _runtimeStageSdfFieldSizePropertyName;
            return true;
        }

        private static string ResolveFirstStageSdfVector3PropertyName(VisualEffect visualEffect, string[] candidates)
        {
            if (visualEffect == null || candidates == null)
                return null;

            for (int i = 0; i < candidates.Length; i++)
            {
                var candidate = candidates[i];
                if (!string.IsNullOrEmpty(candidate) && visualEffect.HasVector3(candidate))
                    return candidate;
            }

            return null;
        }

        private void CacheStageSdfDefaultFieldTransform(VisualEffect visualEffect)
        {
            if (visualEffect == null ||
                _runtimeStageSdfDefaultFieldTransformCached ||
                !TryResolveStageSdfFieldTransformPropertyNames(
                    visualEffect,
                    out var centerPropertyName,
                    out var anglesPropertyName,
                    out var sizePropertyName))
            {
                return;
            }

            _runtimeStageSdfDefaultFieldCenter = visualEffect.GetVector3(centerPropertyName);
            _runtimeStageSdfDefaultFieldAngles = visualEffect.GetVector3(anglesPropertyName);
            _runtimeStageSdfDefaultFieldSize = visualEffect.GetVector3(sizePropertyName);
            _runtimeStageSdfDefaultFieldTransformCached = true;
        }

        private void RestoreStageSdfDefaultFieldTransform(VisualEffect visualEffect)
        {
            if (visualEffect == null)
                return;

            CacheStageSdfDefaultFieldTransform(visualEffect);
            if (!_runtimeStageSdfDefaultFieldTransformCached ||
                !TryResolveStageSdfFieldTransformPropertyNames(
                    visualEffect,
                    out var centerPropertyName,
                    out var anglesPropertyName,
                    out var sizePropertyName))
            {
                return;
            }

            visualEffect.SetVector3(centerPropertyName, _runtimeStageSdfDefaultFieldCenter);
            visualEffect.SetVector3(anglesPropertyName, _runtimeStageSdfDefaultFieldAngles);
            visualEffect.SetVector3(sizePropertyName, _runtimeStageSdfDefaultFieldSize);
        }

        private void ApplyStageSdfGracefulStopOverrides(VisualEffect visualEffect)
        {
            if (visualEffect == null)
                return;

            if (visualEffect.HasFloat(StageSdfSpawnRatePropertyName))
                visualEffect.SetFloat(StageSdfSpawnRatePropertyName, 0f);

            if (visualEffect.HasFloat(StageSdfLineLifetimePropertyName))
                visualEffect.SetFloat(StageSdfLineLifetimePropertyName, 0f);
        }

        private float GetStageSdfGracefulStopDelaySeconds()
        {
            if (!_runtimeStageSdfDefaultLineLifetimeCached)
                return MinimumSdfGracefulStopDelaySeconds;

            return Mathf.Max(_runtimeStageSdfDefaultLineLifetime, MinimumSdfGracefulStopDelaySeconds);
        }

        private void CacheStageSdfDefaultTransform(Transform target)
        {
            if (target == null)
                return;

            if (_runtimeStageSdfTransform == target && _runtimeStageSdfDefaultTransformCached)
                return;

            _runtimeStageSdfTransform = target;
            _runtimeStageSdfDefaultLocalPosition = target.localPosition;
            _runtimeStageSdfDefaultLocalRotation = target.localRotation;
            _runtimeStageSdfDefaultLocalScale = target.localScale;
            _runtimeStageSdfDefaultTransformCached = true;
        }

        private void RestoreStageSdfDefaultTransform(Transform target)
        {
            if (target == null)
                return;

            CacheStageSdfDefaultTransform(target);

            if (!_runtimeStageSdfDefaultTransformCached)
                return;

            target.localPosition = _runtimeStageSdfDefaultLocalPosition;
            target.localRotation = _runtimeStageSdfDefaultLocalRotation;
            target.localScale = _runtimeStageSdfDefaultLocalScale;
        }

        private void CacheStageSdfDefaultTexture(VisualEffect visualEffect)
        {
            if (visualEffect == null || _runtimeStageSdfDefaultTextureCached)
                return;

            _runtimeStageSdfDefaultTexture = visualEffect.GetTexture(StageSdfPropertyName);
            _runtimeStageSdfDefaultTextureCached = true;
        }

        private void RestoreStageSdfDefaultTexture(VisualEffect visualEffect)
        {
            if (visualEffect == null)
                return;

            CacheStageSdfDefaultTexture(visualEffect);
            if (!_runtimeStageSdfDefaultTextureCached)
                return;

            if (_runtimeStageSdfDefaultTexture != null)
            {
                visualEffect.SetTexture(StageSdfPropertyName, _runtimeStageSdfDefaultTexture);
                return;
            }

            visualEffect.ResetOverride(StageSdfPropertyName);
        }

        private bool IsStageSdfTransformSwitchRequired(
            Transform target,
            Transform transformSource,
            bool overrideLocalPosition,
            Vector3 localPosition,
            bool overrideLocalRotation,
            Vector3 localEulerAngles,
            bool overrideLocalScale,
            Vector3 localScale)
        {
            if (target == null)
                return false;

            GetDesiredStageSdfWorldTransform(
                target,
                transformSource,
                overrideLocalPosition,
                localPosition,
                overrideLocalRotation,
                localEulerAngles,
                overrideLocalScale,
                localScale,
                out var desiredPosition,
                out var desiredRotation,
                out var desiredScale);

            Vector3 currentPosition = _runtimeStageSdfCurrentWorldTransformValid ? _runtimeStageSdfCurrentWorldPosition : target.position;
            Quaternion currentRotation = _runtimeStageSdfCurrentWorldTransformValid ? _runtimeStageSdfCurrentWorldRotation : target.rotation;
            Vector3 currentScale = _runtimeStageSdfCurrentWorldTransformValid ? _runtimeStageSdfCurrentWorldScale : target.lossyScale;

            return Vector3.Distance(currentPosition, desiredPosition) > StageSdfSwitchPositionThreshold ||
                   Quaternion.Angle(currentRotation, desiredRotation) > StageSdfSwitchRotationThresholdDegrees ||
                   Vector3.Distance(currentScale, desiredScale) > StageSdfSwitchScaleThreshold;
        }

        private void GetDesiredStageSdfWorldTransform(
            Transform target,
            Transform transformSource,
            bool overrideLocalPosition,
            Vector3 localPosition,
            bool overrideLocalRotation,
            Vector3 localEulerAngles,
            bool overrideLocalScale,
            Vector3 localScale,
            out Vector3 worldPosition,
            out Quaternion worldRotation,
            out Vector3 worldScale)
        {
            CacheStageSdfDefaultTransform(target);

            if (transformSource != null)
            {
                GetStageSdfSourceWorldTransform(transformSource, out worldPosition, out worldRotation, out worldScale);

                if (overrideLocalPosition)
                    worldPosition += worldRotation * localPosition;

                if (overrideLocalRotation)
                    worldRotation *= Quaternion.Euler(localEulerAngles);

                if (overrideLocalScale)
                    worldScale += localScale;

                return;
            }

            Vector3 desiredLocalPosition = overrideLocalPosition
                ? _runtimeStageSdfDefaultLocalPosition + localPosition
                : _runtimeStageSdfDefaultLocalPosition;
            Quaternion desiredLocalRotation = overrideLocalRotation
                ? _runtimeStageSdfDefaultLocalRotation * Quaternion.Euler(localEulerAngles)
                : _runtimeStageSdfDefaultLocalRotation;
            Vector3 desiredLocalScale = overrideLocalScale
                ? _runtimeStageSdfDefaultLocalScale + localScale
                : _runtimeStageSdfDefaultLocalScale;

            var parent = target.parent;
            if (parent == null)
            {
                worldPosition = desiredLocalPosition;
                worldRotation = desiredLocalRotation;
                worldScale = desiredLocalScale;
                return;
            }

            worldPosition = parent.TransformPoint(desiredLocalPosition);
            worldRotation = parent.rotation * desiredLocalRotation;
            worldScale = Vector3.Scale(parent.lossyScale, desiredLocalScale);
        }

        private static void GetStageSdfSourceWorldTransform(
            Transform transformSource,
            out Vector3 worldPosition,
            out Quaternion worldRotation,
            out Vector3 worldScale)
        {
            worldPosition = transformSource.position;
            worldRotation = transformSource.rotation;
            worldScale = transformSource.lossyScale;

            if (transformSource.TryGetComponent<FaceMainCamera>(out var faceMainCamera) &&
                faceMainCamera != null &&
                faceMainCamera.TryGetFacingRotation(out var facingRotation))
            {
                worldRotation = facingRotation;
            }
        }

        private Transform ResolveStageSdfParent()
        {
            if (stageSdfAnchor != null)
                return stageSdfAnchor;

            if (stageEffectRoot != null)
                return stageEffectRoot.transform;

            return transform;
        }
    }
}
