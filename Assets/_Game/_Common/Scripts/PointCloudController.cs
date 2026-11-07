using System;
using System.Collections;
using UnityEngine;
using UnityEngine.VFX;
using Interaction; // 你的自定义接口命名空间
using VFXViewer;

[RequireComponent(typeof(VisualEffect))] 
public class PointCloudController : MonoBehaviour, IVFXController, IPlacementPreviewVFXController
{
    private const float RuntimeStateEpsilon = 0.0001f;

    [Header("核心组件")]
    public VisualEffect vfx;

    [Header("1. 出现控制 (Appear)")]
    [Tooltip("控制粒子从虚空中显现并聚合成模型")]
    public bool isAssembled = false; 
    [Tooltip("若勾选，出现时粒子会从散开状态聚合到模型；否则粒子从一开始就在原位，仅透明度渐显。")]
    [SerializeField] private bool animateAssembleOnAppear = false;
    [Range(0.1f, 5f)]
    public float assembleSpeed = 0.8f; // 聚合与显现的速度

    [Header("2. 消散控制 (Disappear)")]
    [Tooltip("控制模型瞬间炸开并慢慢消失")]
    public bool isDispersing = false; 
    public float scatterIntensity = 5.0f; // 风力/扰动强度
    [Range(0.1f, 5f)]
    public float fadeOutSpeed = 0.5f; // 消失时的淡出速度

    [Header("3. 交互控制 (Interaction)")]
    [Tooltip("将 Vision Pro 的右手食指尖骨骼 (R_IndexTip) 拖到这里")]
    public Transform rightHandIndexTip; 
    [Tooltip("手部触摸能推开粒子的范围半径")]
    public float touchRadius = 0.1f;    

    [Header("4. 可选原点聚合")]
    [Tooltip("若挂了 PointCloudAssembleFromOrigin，会在出现时把 VFX 粒子出生点改到指定原点。")]
    [SerializeField] private PointCloudAssembleFromOrigin assembleFromOrigin;

    // --- 内部变量 (记录当前动画进度) ---
    private float currentWeight = 0.0f; // 当前聚合程度 (0=散, 1=聚)
    private float currentAlpha = 0.0f;  // 当前透明度 (0=透, 1=显)
    private Coroutine _completionCoroutine;
    private Coroutine _placementPreviewBootstrapCoroutine;
    private Coroutine _placementPreviewPrewarmCoroutine;
    private bool _forcePlacementPreviewVisible;
    private bool _placementPreviewPriming;
    private bool _placementPreviewPrewarmed;
    private bool _placementPreviewNeedsPlaybackRestart;
    private int _placementPreviewDebugFramesRemaining;
    private float _cachedAssembleWeight = float.NaN;
    private float _cachedScatterForce = float.NaN;
    private float _cachedFadeAlpha = float.NaN;
    private float _cachedHandRadius = float.NaN;
    private Vector3 _cachedHandLocalPosition;
    private bool _hasCachedHandLocalPosition;
    private bool _cachedEnableRising;
    private bool _hasCachedEnableRising;
    private float _cachedOriginProgress = float.NaN;
    private bool _cachedOriginDispersing;
    private bool _hasCachedOriginProgress;

    public bool IsPlacementPreviewPrewarmed => _placementPreviewPrewarmed;

    private void Awake()
    {
        LogPointCloudState("Awake:enter");

        if (assembleFromOrigin == null)
            assembleFromOrigin = GetComponent<PointCloudAssembleFromOrigin>();

        assembleFromOrigin?.CaptureHomePoseIfNeeded();

        if (VFXRuntimeGuard.DisableUnsupportedVFX(gameObject, this))
        {
            LogPointCloudState("Awake:disableUnsupportedVFX");
            enabled = false;
            return;
        }

        LogPointCloudState("Awake:exit");
    }

    // ========================================================================
    // 接口实现：触发出现
    // ========================================================================
    public void Appear(float duration, Action onComplete)
    {
        LogPointCloudState($"Appear:enter duration={duration}");

        if (vfx == null) vfx = GetComponent<VisualEffect>();
        if (vfx == null)
        {
            PlacementDebugFileLogger.Log($"[PlacementVFX] pointCloud={name} Appear:missingVFX");
            onComplete?.Invoke();
            return;
        }
        if (_completionCoroutine != null) StopCoroutine(_completionCoroutine);

        currentWeight = GetHiddenAssembleWeight();
        currentAlpha = 0f;
        SetAssembleWeight(currentWeight);
        SetScatterForce(0f);
        SetFadeAlpha(0f);
        SetEnableRising(false);
        LogPointCloudState("Appear:afterReset");
        if (animateAssembleOnAppear)
            assembleFromOrigin?.BeginAppear();
        LogPointCloudState("Appear:afterBeginAppear");
        vfx.Play();
        LogPointCloudState("Appear:afterPlay");
        isDispersing = false;
        isAssembled = true;
        LogPointCloudState("Appear:armed");
        _completionCoroutine = StartCoroutine(WaitForAppearComplete(duration, onComplete));
    }

    public void ShowForPlacementPreview()
    {
        LogPointCloudState("ShowForPlacementPreview:enter");

        if (vfx == null) vfx = GetComponent<VisualEffect>();
        if (vfx == null)
        {
            PlacementDebugFileLogger.Log($"[PlacementVFX] pointCloud={name} ShowForPlacementPreview:missingVFX");
            return;
        }

        if (_forcePlacementPreviewVisible || _placementPreviewPriming)
        {
            vfx.enabled = true;
            LogPointCloudState("ShowForPlacementPreview:skipAlreadyActive");
            return;
        }

        if (_placementPreviewPrewarmCoroutine != null)
        {
            if (IsPlacementPreviewWarmReady())
                _placementPreviewPrewarmed = true;

            StopCoroutine(_placementPreviewPrewarmCoroutine);
            _placementPreviewPrewarmCoroutine = null;
        }

        if (_placementPreviewPrewarmed)
        {
            _placementPreviewPriming = false;
            _forcePlacementPreviewVisible = true;
            _placementPreviewDebugFramesRemaining = 12;
            bool restartPlayback = _placementPreviewNeedsPlaybackRestart || vfx.aliveParticleCount <= 0;
            ApplyPlacementPreviewVisibleState(restartPlayback);
            _placementPreviewNeedsPlaybackRestart = false;
            LogPointCloudState("ShowForPlacementPreview:usePrewarmedState");
            return;
        }

        if (_completionCoroutine != null)
        {
            StopCoroutine(_completionCoroutine);
            _completionCoroutine = null;
        }

        if (_placementPreviewBootstrapCoroutine != null)
        {
            StopCoroutine(_placementPreviewBootstrapCoroutine);
            _placementPreviewBootstrapCoroutine = null;
        }

        _forcePlacementPreviewVisible = false;
        _placementPreviewPriming = true;
        _placementPreviewDebugFramesRemaining = 12;
        LogPointCloudState("ShowForPlacementPreview:primeBeforeAppear");
        Appear(0f, null);
        _placementPreviewBootstrapCoroutine = StartCoroutine(BootstrapPlacementPreviewVisibility());
        LogPointCloudState("ShowForPlacementPreview:bootstrapStarted");
    }

    public void PrewarmForPlacementPreview()
    {
        LogPointCloudState("PrewarmForPlacementPreview:enter");

        if (!enabled)
        {
            PlacementDebugFileLogger.Log($"[PlacementVFX] pointCloud={name} PrewarmForPlacementPreview:disabled");
            return;
        }

        if (vfx == null) vfx = GetComponent<VisualEffect>();
        if (vfx == null)
        {
            PlacementDebugFileLogger.Log($"[PlacementVFX] pointCloud={name} PrewarmForPlacementPreview:missingVFX");
            return;
        }

        if (_forcePlacementPreviewVisible || _placementPreviewPriming || _placementPreviewPrewarmed || _placementPreviewPrewarmCoroutine != null)
        {
            LogPointCloudState("PrewarmForPlacementPreview:skipAlreadyPrepared");
            return;
        }

        _placementPreviewPrewarmCoroutine = StartCoroutine(PrewarmPlacementPreviewCoroutine());
        LogPointCloudState("PrewarmForPlacementPreview:started");
    }

    public void RestoreAfterPlacementPreview()
    {
        LogPointCloudState("RestoreAfterPlacementPreview:enter");
        _forcePlacementPreviewVisible = false;
        _placementPreviewPriming = false;
        _placementPreviewNeedsPlaybackRestart = false;
        _placementPreviewDebugFramesRemaining = 0;
        if (_placementPreviewBootstrapCoroutine != null)
        {
            StopCoroutine(_placementPreviewBootstrapCoroutine);
            _placementPreviewBootstrapCoroutine = null;
        }

        if (_placementPreviewPrewarmCoroutine != null)
        {
            StopCoroutine(_placementPreviewPrewarmCoroutine);
            _placementPreviewPrewarmCoroutine = null;
        }

        LogPointCloudState("RestoreAfterPlacementPreview:exit");
    }

    public void HideForPlacementPreview()
    {
        LogPointCloudState("HideForPlacementPreview:enter");

        _forcePlacementPreviewVisible = false;
        _placementPreviewPriming = false;
        _placementPreviewPrewarmed = false;
        _placementPreviewNeedsPlaybackRestart = true;
        _placementPreviewDebugFramesRemaining = 0;

        if (_placementPreviewBootstrapCoroutine != null)
        {
            StopCoroutine(_placementPreviewBootstrapCoroutine);
            _placementPreviewBootstrapCoroutine = null;
        }

        if (_placementPreviewPrewarmCoroutine != null)
        {
            StopCoroutine(_placementPreviewPrewarmCoroutine);
            _placementPreviewPrewarmCoroutine = null;
        }

        if (_completionCoroutine != null)
        {
            StopCoroutine(_completionCoroutine);
            _completionCoroutine = null;
        }

        if (vfx == null)
            vfx = GetComponent<VisualEffect>();

        currentWeight = GetHiddenAssembleWeight();
        currentAlpha = 0f;
        isAssembled = false;
        isDispersing = false;

        if (vfx != null)
        {
            SetAssembleWeight(currentWeight);
            SetScatterForce(0f);
            SetFadeAlpha(0f);
            SetEnableRising(false);
            vfx.Stop();
            vfx.enabled = false;

            var renderer = vfx.GetComponent<Renderer>();
            if (renderer != null)
                renderer.forceRenderingOff = true;
        }

        ApplyOriginProgressIfNeeded(0f, false);
        LogPointCloudState("HideForPlacementPreview:exit");
    }

    // ========================================================================
    // 接口实现：触发消失
    // ========================================================================
    public void Disappear(float duration, Action onComplete)
    {
        if (vfx == null) vfx = GetComponent<VisualEffect>();
        if (vfx == null) { onComplete?.Invoke(); return; }
        if (_completionCoroutine != null) StopCoroutine(_completionCoroutine);

        assembleFromOrigin?.BeginDisappear();
        isAssembled = false;
        isDispersing = true;
        _completionCoroutine = StartCoroutine(WaitForDisappearComplete(duration, onComplete));
    }

    private IEnumerator WaitForAppearComplete(float duration, Action onComplete)
    {
        PlacementDebugFileLogger.Log($"[PlacementVFX] pointCloud={name} WaitForAppearComplete:start duration={duration}");
        float elapsed = 0f;
        while (elapsed < duration && currentAlpha < 0.99f)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }
        _completionCoroutine = null;
        LogPointCloudState($"WaitForAppearComplete:done elapsed={elapsed}");
        onComplete?.Invoke();
    }

    private IEnumerator WaitForDisappearComplete(float duration, Action onComplete)
    {
        PlacementDebugFileLogger.Log($"[PlacementVFX] pointCloud={name} WaitForDisappearComplete:start duration={duration}");
        float elapsed = 0f;
        while (elapsed < duration && currentAlpha > 0.01f)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }
        _completionCoroutine = null;
        LogPointCloudState($"WaitForDisappearComplete:done elapsed={elapsed}");
        onComplete?.Invoke();
    }

    // ========================================================================
    // Unity 生命周期
    // ========================================================================
    void Start()
    {
        LogPointCloudState("Start:enter");

        if (!enabled)
        {
            PlacementDebugFileLogger.Log($"[PlacementVFX] pointCloud={name} Start:disabled");
            return;
        }

        if (vfx == null) vfx = GetComponent<VisualEffect>();

        if (_forcePlacementPreviewVisible)
        {
            bool restartPlayback = _placementPreviewNeedsPlaybackRestart || vfx.aliveParticleCount <= 0;
            ApplyPlacementPreviewVisibleState(restartPlayback);
            _placementPreviewNeedsPlaybackRestart = false;
            LogPointCloudState("Start:forcedPlacementVisible");
            return;
        }

        if (_placementPreviewPriming)
        {
            LogPointCloudState("Start:skipResetBecausePriming");
            return;
        }

        // 【关键】初始化：散开、透明、无风、无上升粒子
        currentWeight = GetHiddenAssembleWeight();
        currentAlpha = 0f;
        
        SetAssembleWeight(currentWeight);
        SetScatterForce(0f);
        SetFadeAlpha(0f);
        SetEnableRising(false);
        LogPointCloudState("Start:initializedDefaultHiddenState");
    }

    void Update()
    {
        if (!enabled)
            return;

        if (vfx == null) return;

        if (_forcePlacementPreviewVisible)
        {
            LogPlacementPreviewFrame("Update:forcedPlacementVisibleStable");
            return;
        }

        bool shouldUpdateHandInteraction = ShouldUpdateHandInteraction();
        bool hasPendingAnimatedState = HasPendingAnimatedState();
        if (!shouldUpdateHandInteraction && !hasPendingAnimatedState)
        {
            LogPlacementPreviewFrame("Update:stableSleep");
            return;
        }

        if (shouldUpdateHandInteraction)
            ApplyHandInteractionStateIfNeeded();

        // ==================================================
        // 状态 A: 正在消散 (优先处理)
        // ==================================================
        if (isDispersing)
        {
            // 1. 强制松开，开启狂风
            SetAssembleWeight(0f);
            currentWeight = 0f; 
            SetScatterForce(scatterIntensity);

            // 2. 淡出动画
            currentAlpha = Mathf.MoveTowards(currentAlpha, 0f, Time.deltaTime * fadeOutSpeed);
            SetFadeAlpha(currentAlpha);

            // 3. 立刻停止上升粒子
            SetEnableRising(false);

            if (currentAlpha <= RuntimeStateEpsilon)
            {
                currentAlpha = 0f;
                isDispersing = false;
                SetScatterForce(0f);
            }
        }
        
        // ==================================================
        // 状态 B: 正常控制 (聚合 或 待机)
        // ==================================================
        else
        {
            // 1. 关闭狂风，保持粒子稳定
            SetScatterForce(0f);

            // 2. 计算目标值，并平滑过渡权重与透明度
            float targetWeight = isAssembled ? 1.0f : GetHiddenAssembleWeight();
            float targetAlpha = isAssembled ? 1.0f : 0.0f;
            currentWeight = Mathf.MoveTowards(currentWeight, targetWeight, Time.deltaTime * assembleSpeed);
            currentAlpha = Mathf.MoveTowards(currentAlpha, targetAlpha, Time.deltaTime * assembleSpeed);

            // 3. 发送给 VFX
            SetAssembleWeight(currentWeight);
            SetFadeAlpha(currentAlpha);

            // 4. 上升粒子开关
            // 只有当出现基本完成后，才冒出上升仙气
            SetEnableRising(isAssembled && currentWeight > 0.95f && currentAlpha > 0.95f);
        }

        ApplyOriginProgressIfNeeded(currentAlpha, isDispersing);
        LogPlacementPreviewFrame(_placementPreviewPriming ? "Update:priming" : "Update:normal");
    }

    private void ApplyPlacementPreviewVisibleState(bool restartPlayback = true)
    {
        if (vfx == null)
            return;

        LogPointCloudState($"ApplyPlacementPreviewVisibleState:before restartPlayback={restartPlayback}");
        currentWeight = 1f;
        currentAlpha = 1f;
        isDispersing = false;
        isAssembled = true;

        vfx.enabled = true;
        if (restartPlayback)
        {
            InvalidateCachedVfxState();
            vfx.Reinit();
        }

        SetAssembleWeight(1f);
        SetScatterForce(0f);
        SetFadeAlpha(1f);
        SetEnableRising(true);
        ApplyOriginProgressIfNeeded(1f, false);
        if (restartPlayback)
            vfx.Play();
        LogPointCloudState($"ApplyPlacementPreviewVisibleState:after restartPlayback={restartPlayback}");
    }

    private IEnumerator PrewarmPlacementPreviewCoroutine()
    {
        var renderer = vfx != null ? vfx.GetComponent<Renderer>() : null;
        bool previousForceRenderingOff = renderer != null && renderer.forceRenderingOff;
        LogPointCloudState("PrewarmPlacementPreview:start");

        vfx.enabled = true;
        if (renderer != null)
            renderer.forceRenderingOff = true;

        currentWeight = GetHiddenAssembleWeight();
        currentAlpha = 0f;
        isDispersing = false;
        isAssembled = true;

        InvalidateCachedVfxState();
        SetAssembleWeight(currentWeight);
        SetScatterForce(0f);
        SetFadeAlpha(0f);
        SetEnableRising(false);
        vfx.Play();
        LogPointCloudState("PrewarmPlacementPreview:afterPlay");

        const int maxFrames = 24;
        int waitedFrames = 0;
        while (waitedFrames < maxFrames && !IsPlacementPreviewWarmReady())
        {
            waitedFrames++;
            yield return null;
        }

        _placementPreviewPrewarmed = IsPlacementPreviewWarmReady();
        _placementPreviewNeedsPlaybackRestart = !_placementPreviewPrewarmed;

        currentWeight = GetHiddenAssembleWeight();
        currentAlpha = 0f;
        isDispersing = false;
        isAssembled = false;

        SetAssembleWeight(currentWeight);
        SetScatterForce(0f);
        SetFadeAlpha(0f);
        SetEnableRising(false);

        if (renderer != null)
            renderer.forceRenderingOff = previousForceRenderingOff;

        _placementPreviewPrewarmCoroutine = null;
        LogPointCloudState($"PrewarmPlacementPreview:done waitedFrames={waitedFrames} prewarmed={_placementPreviewPrewarmed}");
    }

    private IEnumerator BootstrapPlacementPreviewVisibility()
    {
        LogPointCloudState("BootstrapPlacementPreviewVisibility:start");
        yield return null;
        if (!_placementPreviewPriming || vfx == null)
        {
            LogPointCloudState("BootstrapPlacementPreviewVisibility:abortBeforePass1");
            _placementPreviewBootstrapCoroutine = null;
            yield break;
        }

        LogPointCloudState("BootstrapPlacementPreviewVisibility:pass1");

        yield return new WaitForEndOfFrame();
        if (!_placementPreviewPriming || vfx == null)
        {
            LogPointCloudState("BootstrapPlacementPreviewVisibility:abortBeforePass2");
            _placementPreviewBootstrapCoroutine = null;
            yield break;
        }

        LogPointCloudState("BootstrapPlacementPreviewVisibility:pass2");

        yield return null;
        if (_placementPreviewPriming && vfx != null)
        {
            _placementPreviewPriming = false;
            _forcePlacementPreviewVisible = true;
            bool restartPlayback = _placementPreviewNeedsPlaybackRestart || vfx.aliveParticleCount <= 0;
            ApplyPlacementPreviewVisibleState(restartPlayback);
            _placementPreviewNeedsPlaybackRestart = false;
            LogPointCloudState("BootstrapPlacementPreviewVisibility:pass3LockVisible");
        }

        _placementPreviewBootstrapCoroutine = null;
        LogPointCloudState("BootstrapPlacementPreviewVisibility:done");
    }

    private bool IsPlacementPreviewWarmReady()
    {
        if (vfx == null)
            return false;

        if (vfx.aliveParticleCount < 0)
            return false;

        var renderer = vfx.GetComponent<Renderer>();
        if (renderer == null)
            return true;

        return renderer.bounds.size.sqrMagnitude > 0.0001f;
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    [System.Diagnostics.Conditional("VFXVIEWER_PLACEMENT_DEBUG_LOGS")]
    private void LogPlacementPreviewFrame(string stage)
    {
        if (_placementPreviewDebugFramesRemaining <= 0)
            return;

        _placementPreviewDebugFramesRemaining--;
        LogPointCloudState($"{stage} debugFrameRemaining={_placementPreviewDebugFramesRemaining}");
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    [System.Diagnostics.Conditional("VFXVIEWER_PLACEMENT_DEBUG_LOGS")]
    private void LogPointCloudState(string stage)
    {
        try
        {
            var renderer = vfx != null ? vfx.GetComponent<Renderer>() : null;
            string rendererState = renderer == null
                ? "renderer=null"
                : $"rendererEnabled={renderer.enabled} forceRenderingOff={renderer.forceRenderingOff} " +
                  $"isVisible={renderer.isVisible} boundsCenter={renderer.bounds.center} boundsSize={renderer.bounds.size}";

            bool? culled = TryReadVisualEffectBoolProperty("culled");
            bool? paused = TryReadVisualEffectBoolProperty("pause");

            PlacementDebugFileLogger.Log(
                $"[PlacementVFX] pointCloud={name} stage={stage} frame={Time.frameCount} " +
                $"activeSelf={gameObject.activeSelf} activeInHierarchy={gameObject.activeInHierarchy} enabled={enabled} " +
                $"vfxAssigned={vfx != null} vfxEnabled={(vfx != null ? vfx.enabled.ToString() : "n/a")} " +
                $"aliveParticles={(vfx != null ? vfx.aliveParticleCount.ToString() : "n/a")} " +
                $"currentAlpha={currentAlpha:F3} currentWeight={currentWeight:F3} " +
                $"isAssembled={isAssembled} isDispersing={isDispersing} " +
                $"previewPriming={_placementPreviewPriming} previewForced={_forcePlacementPreviewVisible} " +
                $"culled={FormatNullableBool(culled)} pause={FormatNullableBool(paused)} " +
                $"{rendererState}");
        }
        catch
        {
            PlacementDebugFileLogger.Log($"[PlacementVFX] pointCloud={name} stage={stage} logStateFailed");
        }
    }

    private bool? TryReadVisualEffectBoolProperty(string propertyName)
    {
        if (vfx == null || string.IsNullOrWhiteSpace(propertyName))
            return null;

        try
        {
            var property = typeof(VisualEffect).GetProperty(propertyName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            if (property == null || property.PropertyType != typeof(bool))
                return null;

            return (bool)property.GetValue(vfx);
        }
        catch
        {
            return null;
        }
    }

    private static string FormatNullableBool(bool? value)
    {
        return value.HasValue ? value.Value.ToString() : "n/a";
    }

    private bool HasPendingAnimatedState()
    {
        if (_placementPreviewPriming)
            return true;

        if (isDispersing)
            return currentAlpha > RuntimeStateEpsilon || _completionCoroutine != null;

        float targetWeight = isAssembled ? 1f : GetHiddenAssembleWeight();
        float targetAlpha = isAssembled ? 1f : 0f;
        return Mathf.Abs(currentWeight - targetWeight) > RuntimeStateEpsilon ||
               Mathf.Abs(currentAlpha - targetAlpha) > RuntimeStateEpsilon;
    }

    private bool ShouldUpdateHandInteraction()
    {
        return rightHandIndexTip != null &&
               currentAlpha > RuntimeStateEpsilon &&
               !_placementPreviewPriming;
    }

    private void ApplyHandInteractionStateIfNeeded()
    {
        Vector3 localHandPosition = vfx.transform.InverseTransformPoint(rightHandIndexTip.position);
        if (!_hasCachedHandLocalPosition || (localHandPosition - _cachedHandLocalPosition).sqrMagnitude > RuntimeStateEpsilon * RuntimeStateEpsilon)
        {
            vfx.SetVector3("HandPosition", localHandPosition);
            _cachedHandLocalPosition = localHandPosition;
            _hasCachedHandLocalPosition = true;
        }

        if (!Mathf.Approximately(_cachedHandRadius, touchRadius))
        {
            vfx.SetFloat("HandRadius", touchRadius);
            _cachedHandRadius = touchRadius;
        }
    }

    private void ApplyOriginProgressIfNeeded(float progress, bool dispersing)
    {
        if (assembleFromOrigin == null)
            return;

        if (!_hasCachedOriginProgress ||
            !Mathf.Approximately(_cachedOriginProgress, progress) ||
            _cachedOriginDispersing != dispersing)
        {
            assembleFromOrigin.ApplyProgress(progress, dispersing);
            _cachedOriginProgress = progress;
            _cachedOriginDispersing = dispersing;
            _hasCachedOriginProgress = true;
        }
    }

    private void SetAssembleWeight(float value)
    {
        if (Mathf.Approximately(_cachedAssembleWeight, value))
            return;

        vfx.SetFloat("AssembleWeight", value);
        _cachedAssembleWeight = value;
    }

    private void SetScatterForce(float value)
    {
        if (Mathf.Approximately(_cachedScatterForce, value))
            return;

        vfx.SetFloat("ScatterForce", value);
        _cachedScatterForce = value;
    }

    private void SetFadeAlpha(float value)
    {
        if (Mathf.Approximately(_cachedFadeAlpha, value))
            return;

        vfx.SetFloat("FadeAlpha", value);
        _cachedFadeAlpha = value;
    }

    private void SetEnableRising(bool value)
    {
        if (_hasCachedEnableRising && _cachedEnableRising == value)
            return;

        vfx.SetBool("EnableRising", value);
        _cachedEnableRising = value;
        _hasCachedEnableRising = true;
    }

    private void InvalidateCachedVfxState()
    {
        _cachedAssembleWeight = float.NaN;
        _cachedScatterForce = float.NaN;
        _cachedFadeAlpha = float.NaN;
        _cachedHandRadius = float.NaN;
        _cachedHandLocalPosition = Vector3.zero;
        _hasCachedHandLocalPosition = false;
        _hasCachedEnableRising = false;
        _cachedOriginProgress = float.NaN;
        _cachedOriginDispersing = false;
        _hasCachedOriginProgress = false;
    }

    private float GetHiddenAssembleWeight()
    {
        return animateAssembleOnAppear ? 0f : 1f;
    }
}
