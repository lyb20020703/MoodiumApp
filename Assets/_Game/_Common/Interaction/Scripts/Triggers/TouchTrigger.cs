using System;
using System.Collections.Generic;
using Autohand;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using VFXViewer;

namespace Interaction
{
    /// <summary>
    /// 触摸触发器：布局模式绑定 XRBaseInteractable，体验模式绑定 AutoHand 触摸 relay。
    /// 当开始和结束都是触摸时：第一次触摸 = 出现，第二次触摸 = 消失（切换模式）。
    /// 当仅开始或仅结束为触摸时：Enter 触发开始，Exit 触发结束。
    /// </summary>
    public class TouchTrigger : MonoBehaviour, ITrigger
    {
        public event Action<ITrigger, bool> Triggered;

        /// <summary> 由 InteractionModule 传入触摸交互体；布局模式走 XRI，体验模式自动切到 AutoHand。 </summary>
        public void SetConfig(XRBaseInteractable interactable, bool fireStart, bool fireEnd, bool useHover = false, Action touchEnterCallback = null)
        {
            _xrInteractable = interactable;
            _fireStart = fireStart;
            _fireEnd = fireEnd;
            _useHover = useHover;
            _touchEnterCallback = touchEnterCallback;
            _configured = true;
            EnsurePlacementDirectorSubscription();
            RefreshBindings(resetTouchState: true);
            LogFile(
                "set_config",
                $"fireStart={fireStart}, fireEnd={fireEnd}, useHover={useHover}, touchEnterCallback={(touchEnterCallback != null)}");
        }

        private void RefreshBindings(bool resetTouchState)
        {
            UnsubscribeAll();

            if (resetTouchState)
            {
                _startEmittedWaitingForEnd = false;
                _autoHandTouchCounts.Clear();
            }

            if (!_configured)
                return;

            LogFile(
                "refresh_bindings",
                $"resetTouchState={resetTouchState}, useAutoHand={ShouldUseAutoHandTouch()}");

            if (ShouldUseAutoHandTouch())
            {
                BindAutoHandTouch();
                return;
            }

            BindXrTouch();
        }

        private void BindXrTouch()
        {
            if (_xrInteractable != null)
            {
                if (_useHover)
                {
                    VisionProHoverHelper.EnsurePokeFilterForVisionProHover(_xrInteractable);
                    _xrInteractable.hoverEntered.AddListener(OnXRHoverEntered);
                    _xrInteractable.hoverExited.AddListener(OnXRHoverExited);
                }
                else
                {
                    _xrInteractable.selectEntered.AddListener(OnXRSelectEntered);
                    _xrInteractable.selectExited.AddListener(OnXRSelectExited);
                }

                LogFile("bind_xr_touch", $"listenerType={(_useHover ? "Hover" : "Select")}");
            }
            else if (_fireStart || _fireEnd)
            {
                LogFile("bind_xr_touch_missing_interactable");
                Debug.LogWarning($"{GetDebugPrefix()} 触摸交互体未赋值，Select/Hover 不会触发。请检查 InteractionModule 的「触摸交互体」或「触摸导游」配置。", this);
            }
        }

        private void BindAutoHandTouch()
        {
            if (_xrInteractable == null)
            {
                if (_fireStart || _fireEnd)
                {
                    LogFile("bind_autohand_missing_interactable");
                    Debug.LogWarning($"{GetDebugPrefix()} 触摸交互体未赋值，AutoHand 触摸不会触发。", this);
                }
                return;
            }

            CollectAutoHandTouchRelays(_xrInteractable, _autoHandTouchRelays);
            for (int i = 0; i < _autoHandTouchRelays.Count; i++)
            {
                AutoHandTouchRelay relay = _autoHandTouchRelays[i];
                relay.AcquireExperienceTouchMode();
                relay.TouchStarted += OnAutoHandTouchStarted;
                relay.TouchEnded += OnAutoHandTouchEnded;
            }

            LogFile("bind_autohand_touch", $"relayCount={_autoHandTouchRelays.Count}");

            if (_autoHandTouchRelays.Count == 0)
            {
                LogFile("bind_autohand_no_relays");
                Debug.LogWarning($"{GetDebugPrefix()} 未找到可用于 AutoHand 触摸的 Collider，体验模式触摸不会触发。", this);
                return;
            }
        }

        private void UnsubscribeAll()
        {
            if (_xrInteractable != null)
            {
                _xrInteractable.selectEntered.RemoveListener(OnXRSelectEntered);
                _xrInteractable.selectExited.RemoveListener(OnXRSelectExited);
                _xrInteractable.hoverEntered.RemoveListener(OnXRHoverEntered);
                _xrInteractable.hoverExited.RemoveListener(OnXRHoverExited);
            }

            for (int i = 0; i < _autoHandTouchRelays.Count; i++)
            {
                AutoHandTouchRelay relay = _autoHandTouchRelays[i];
                if (relay == null)
                    continue;

                relay.TouchStarted -= OnAutoHandTouchStarted;
                relay.TouchEnded -= OnAutoHandTouchEnded;
                relay.ReleaseExperienceTouchMode();
            }

            _autoHandTouchRelays.Clear();

        }

        private XRBaseInteractable _xrInteractable;
        private ChapterPlacementDirector _placementDirector;
        private readonly List<AutoHandTouchRelay> _autoHandTouchRelays = new List<AutoHandTouchRelay>();
        private readonly Dictionary<int, int> _autoHandTouchCounts = new Dictionary<int, int>();
        private bool _enabled;
        private bool _fireStart;
        private bool _fireEnd;
        private bool _useHover;
        private bool _configured;
        private bool _appModeSubscribed;
        private Action _touchEnterCallback;
        /// <summary> 已发出「开始」且尚未收到「结束」时不再重复发「开始」（非切换模式用）。 </summary>
        private bool _startEmittedWaitingForEnd;
        /// <summary> 模块已进入进行阶段（用于切换模式：第二次触摸发结束）。 </summary>
        private bool _moduleInProgress;
        /// <summary> 开始和结束都是触摸时为 true，第一次触摸=出现、第二次触摸=消失。 </summary>
        private bool _toggleMode => _fireStart && _fireEnd;

        private void OnDestroy()
        {
            UnsubscribeAll();
            UnsubscribePlacementDirector();
        }

        // 轮询兜底逻辑已移除：仅依赖 XR 的 Enter/Exit 事件发「开始/结束」
        private void Update() { }

        private void OnXRSelectEntered(SelectEnterEventArgs args)
        {
            HandleTouchEnter("SelectEntered");
        }

        private void OnXRSelectExited(SelectExitEventArgs args)
        {
            HandleTouchExit("SelectExited");
        }

        private void OnXRHoverEntered(UnityEngine.XR.Interaction.Toolkit.HoverEnterEventArgs args)
        {
            HandleTouchEnter("HoverEntered");
        }

        private void OnXRHoverExited(UnityEngine.XR.Interaction.Toolkit.HoverExitEventArgs args)
        {
            HandleTouchExit("HoverExited");
        }

        private void OnAutoHandTouchStarted(AutoHandTouchRelay relay, Hand hand)
        {
            if (hand == null)
                return;

            int handId = hand.GetInstanceID();
            _autoHandTouchCounts.TryGetValue(handId, out int currentCount);
            _autoHandTouchCounts[handId] = currentCount + 1;
            if (currentCount > 0)
            {
                LogFile(
                    "autohand_touch_started_ignored",
                    $"reason=duplicate_hand_contact, hand={DescribeHand(hand)}, relay={(relay != null ? relay.name : "null")}");
                return;
            }

            LogFile(
                "autohand_touch_started",
                $"hand={DescribeHand(hand)}, relay={(relay != null ? relay.name : "null")}");
            HandleTouchEnter("AutoHandTouchStarted");
        }

        private void OnAutoHandTouchEnded(AutoHandTouchRelay relay, Hand hand)
        {
            if (hand == null)
                return;

            int handId = hand.GetInstanceID();
            if (!_autoHandTouchCounts.TryGetValue(handId, out int currentCount))
            {
                LogFile(
                    "autohand_touch_ended_ignored",
                    $"reason=unknown_hand, hand={DescribeHand(hand)}, relay={(relay != null ? relay.name : "null")}");
                return;
            }

            if (currentCount > 1)
            {
                _autoHandTouchCounts[handId] = currentCount - 1;
                LogFile(
                    "autohand_touch_ended_ignored",
                    $"reason=remaining_contacts, hand={DescribeHand(hand)}, relay={(relay != null ? relay.name : "null")}, remaining={currentCount - 1}");
                return;
            }

            _autoHandTouchCounts.Remove(handId);
            LogFile(
                "autohand_touch_ended",
                $"hand={DescribeHand(hand)}, relay={(relay != null ? relay.name : "null")}");
            HandleTouchExit("AutoHandTouchEnded");
        }

        private void HandleTouchEnter(string source)
        {
            if (!_enabled)
            {
                LogFile("handle_touch_enter_ignored", $"reason=trigger_disabled, source={source}");
                return;
            }

            _touchEnterCallback?.Invoke();

            if (_toggleMode)
            {
                if (!_moduleInProgress)
                {
                    if (!_fireStart)
                    {
                        LogFile("handle_touch_enter_ignored", $"reason=toggle_no_start, source={source}");
                        return;
                    }

                    LogFile("emit_trigger", $"source={source}, emitStart=true, mode=toggle");
                    Triggered?.Invoke(this, true);
                }
                else
                {
                    if (!_fireEnd)
                    {
                        LogFile("handle_touch_enter_ignored", $"reason=toggle_no_end, source={source}");
                        return;
                    }

                    LogFile("emit_trigger", $"source={source}, emitStart=false, mode=toggle");
                    Triggered?.Invoke(this, false);
                }

                return;
            }

            if (!_fireStart)
            {
                LogFile("handle_touch_enter_ignored", $"reason=start_not_enabled, source={source}");
                return;
            }

            if (_startEmittedWaitingForEnd)
            {
                LogFile("handle_touch_enter_ignored", $"reason=waiting_for_end, source={source}");
                return;
            }

            _startEmittedWaitingForEnd = true;
            LogFile("emit_trigger", $"source={source}, emitStart=true, mode=single");
            Triggered?.Invoke(this, true);
        }

        private void HandleTouchExit(string source)
        {
            if (_toggleMode)
            {
                LogFile("handle_touch_exit_ignored", $"reason=toggle_mode, source={source}");
                return;
            }

            if (!_enabled || !_fireEnd)
            {
                LogFile(
                    "handle_touch_exit_ignored",
                    $"reason={(!_enabled ? "trigger_disabled" : "end_not_enabled")}, source={source}");
                return;
            }

            if (!_startEmittedWaitingForEnd)
            {
                LogFile("handle_touch_exit_ignored", $"reason=start_not_emitted, source={source}");
                return;
            }

            _startEmittedWaitingForEnd = false;
            LogFile("emit_trigger", $"source={source}, emitStart=false, mode=single");
            Triggered?.Invoke(this, false);
        }

        private bool ShouldUseAutoHandTouch()
        {
            if (Application.isEditor)
                return false;

            EnsurePlacementDirectorSubscription();
            return _placementDirector != null && _placementDirector.CurrentAppMode == ExhibitAppMode.Experience;
        }

        private void EnsurePlacementDirectorSubscription()
        {
            if (_placementDirector == null)
                _placementDirector = FindFirstObjectByType<ChapterPlacementDirector>(FindObjectsInactive.Include);

            if (_placementDirector == null || _appModeSubscribed)
                return;

            _placementDirector.AppModeChanged += OnAppModeChanged;
            _appModeSubscribed = true;
        }

        private void UnsubscribePlacementDirector()
        {
            if (!_appModeSubscribed)
                return;

            if (_placementDirector != null)
                _placementDirector.AppModeChanged -= OnAppModeChanged;

            _appModeSubscribed = false;
        }

        private void OnAppModeChanged(ExhibitAppMode mode)
        {
            LogFile("app_mode_changed", $"mode={mode}");
            RefreshBindings(resetTouchState: true);
        }

        private static void CollectAutoHandTouchRelays(XRBaseInteractable interactable, List<AutoHandTouchRelay> results)
        {
            results.Clear();
            if (interactable == null)
                return;

            var processedObjects = new HashSet<int>();
            if (interactable.colliders != null && interactable.colliders.Count > 0)
            {
                for (int i = 0; i < interactable.colliders.Count; i++)
                    AddAutoHandTouchRelay(interactable.colliders[i], processedObjects, results);

                return;
            }

            var colliders = interactable.transform.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
                AddAutoHandTouchRelay(colliders[i], processedObjects, results);
        }

        private static void AddAutoHandTouchRelay(
            Collider collider,
            HashSet<int> processedObjects,
            List<AutoHandTouchRelay> results)
        {
            if (collider == null)
                return;

            int instanceId = collider.gameObject.GetInstanceID();
            if (!processedObjects.Add(instanceId))
                return;

            AutoHandTouchRelay relay = AutoHandTouchRelay.Ensure(collider.gameObject);
            if (relay != null)
                results.Add(relay);
        }

        /// <summary> 模块回到开始阶段时调用，重置状态以便下次触摸可再次触发开始（切换模式会清 _moduleInProgress）。 </summary>
        public void NotifyModuleBackToStartPhase()
        {
            _startEmittedWaitingForEnd = false;
            _moduleInProgress = false;
            LogFile("module_back_to_start");
        }

        /// <summary> 模块进入进行阶段时调用，切换模式下第二次触摸将发「结束」。 </summary>
        public void NotifyModuleEnteredInProgress()
        {
            _moduleInProgress = true;
            // 情况：开始=时间，结束=触摸
            // 此时本触发器只负责结束（_fireEnd=true, _fireStart=false），没有机会自己发「开始」；
            // 进入进行阶段时视为本周期已经有过「开始」，后续一次 SelectExited/HoverExited 即可发「结束」。
            if (_fireEnd && !_fireStart && !_toggleMode)
                _startEmittedWaitingForEnd = true;
            LogFile("module_entered_in_progress");
        }

        public void Enable()
        {
            _enabled = true;
            LogFile("enable");
        }

        public void Disable()
        {
            _enabled = false;
            LogFile("disable");
        }

        private void OnEnable()
        {
            _enabled = true;
            EnsurePlacementDirectorSubscription();
            RefreshBindings(resetTouchState: false);
            LogFile("on_enable");
        }

        private void OnDisable()
        {
            _enabled = false;
            UnsubscribeAll();
            _autoHandTouchCounts.Clear();
            UnsubscribePlacementDirector();
            LogFile("on_disable");
        }

        private void LogFile(string stage, string extra = null)
        {
            if (!PlacementDebugFileLogger.IsLoggingEnabled)
                return;

            string appMode = _placementDirector != null ? _placementDirector.CurrentAppMode.ToString() : "<unknown>";
            string suffix = string.IsNullOrWhiteSpace(extra) ? string.Empty : $", {extra}";
            PlacementDebugFileLogger.Log(
                $"[TouchTrigger] owner={GetDebugOwnerName()}, stage={stage}, enabled={_enabled}, configured={_configured}, " +
                $"fireStart={_fireStart}, fireEnd={_fireEnd}, useHover={_useHover}, toggleMode={_toggleMode}, " +
                $"moduleInProgress={_moduleInProgress}, appMode={appMode}, relayCount={_autoHandTouchRelays.Count}, " +
                $"activeTouchHands={_autoHandTouchCounts.Count}, {DescribeInteractableState()}{suffix}");
        }

        private string DescribeInteractableState()
        {
            if (_xrInteractable == null)
                return "interactable=<none>";

            Transform transform = _xrInteractable.transform;
            int explicitColliderCount = _xrInteractable.colliders != null ? _xrInteractable.colliders.Count : 0;
            int resolvedColliderCount = 0;
            var colliders = _xrInteractable.GetComponentsInChildren<Collider>(true);
            if (colliders != null)
                resolvedColliderCount = colliders.Length;

            return
                $"interactable={_xrInteractable.gameObject.name}" +
                $"[activeSelf={_xrInteractable.gameObject.activeSelf}," +
                $"activeInHierarchy={_xrInteractable.gameObject.activeInHierarchy}," +
                $"explicitColliders={explicitColliderCount},resolvedColliders={resolvedColliderCount}," +
                $"worldPos=({transform.position.x:F3},{transform.position.y:F3},{transform.position.z:F3})," +
                $"worldRot=({transform.eulerAngles.x:F3},{transform.eulerAngles.y:F3},{transform.eulerAngles.z:F3})]";
        }

        private static string DescribeHand(Hand hand)
        {
            if (hand == null)
                return "<null>";

            return $"{hand.name}#{hand.GetInstanceID()}";
        }

        private string GetDebugPrefix()
        {
            string owner = GetDebugOwnerName();
            string interactableName = _xrInteractable != null ? _xrInteractable.gameObject.name : "null";
            return $"[TouchTrigger] owner={owner} interactable={interactableName}";
        }

        private string GetDebugOwnerName()
        {
            var exhibitInfo = GetComponentInParent<ExhibitInfo>(true);
            if (exhibitInfo != null && !string.IsNullOrWhiteSpace(exhibitInfo.moduleId))
                return exhibitInfo.moduleId.Trim();

            var module = GetComponentInParent<InteractionModule>(true);
            if (module != null)
                return module.gameObject.name;

            return transform.root != null ? transform.root.name : gameObject.name;
        }
    }
}
