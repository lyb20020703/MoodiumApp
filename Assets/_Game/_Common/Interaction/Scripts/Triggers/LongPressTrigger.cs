using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Interaction
{
    /// <summary>
    /// 长按触发器：开始阶段按住「开始交互体」满时长触发开始；结束阶段在开始触发后，按住「结束交互体」满时长触发结束。
    /// </summary>
    public class LongPressTrigger : MonoBehaviour, ITrigger
    {
        public event Action<ITrigger, bool> Triggered;

        /// <summary> 由 InteractionModule 传入：开始交互体 + 结束交互体（可不同）+ 各自按住时长 + 槽位。useHover=true 时监听 Hover（手部接触，Vision Pro 适用），否则监听 Select（捏合）。 </summary>
        public void SetConfig(XRBaseInteractable interactableStart, XRBaseInteractable interactableEnd, float holdSecStart, float holdSecEnd, bool isStartSlot, bool isEndSlot, bool useHover = false)
        {
            UnsubscribeAll();
            _interactableStart = interactableStart;
            _interactableEnd = interactableEnd;
            _holdDurationStart = Mathf.Max(holdSecStart, 0.01f);
            _holdDurationEnd = Mathf.Max(holdSecEnd, 0.01f);
            _isStartSlot = isStartSlot;
            _isEndSlot = isEndSlot;
            _useHover = useHover;
            SubscribeAll();
        }

        private XRBaseInteractable _interactableStart;
        private XRBaseInteractable _interactableEnd;
        private float _holdDurationStart;
        private float _holdDurationEnd;
        private bool _isStartSlot;
        private bool _isEndSlot;
        private bool _useHover;
        private bool _enabled;
        private float _selectEnterTime = -1f;
        private bool _firedStartThisHold;
        private float _endSelectEnterTime = -1f;
        private bool _firedEndThisHold;
        /// <summary> 模块已进入进行阶段（开始由本触发器或时间/触摸等其它触发器触发），此时允许启动结束长按计时。 </summary>
        private bool _moduleInProgress;
        /// <summary> 模块进入进行阶段的时间，用于忽略进入前已存在的 Hover（避免自动触发结束）。 </summary>
        private float _moduleEnteredInProgressTime = -1f;
        /// <summary> 进入进行阶段后至少经过此时长（秒）才接受结束交互体的 Hover/Select，避免同一帧或残留 Hover 导致自动触发结束。 </summary>
        private const float EndHoverMinDelayAfterInProgress = 0.15f;

        /// <summary> 模块进入进行阶段时由 InteractionModule 调用，使「结束」长按可在时间/触摸触发开始后正常启动计时。 </summary>
        public void NotifyModuleEnteredInProgress()
        {
            _moduleInProgress = true;
            _moduleEnteredInProgressTime = Time.time;
        }

        /// <summary> 模块回到开始阶段时由 InteractionModule 调用，重置结束计时相关状态。 </summary>
        public void NotifyModuleBackToStartPhase()
        {
            _moduleInProgress = false;
            _moduleEnteredInProgressTime = -1f;
            _endSelectEnterTime = -1f;
            _firedEndThisHold = false;
        }

        private void UnsubscribeAll()
        {
            if (_interactableStart != null)
            {
                _interactableStart.selectEntered.RemoveListener(OnStartSelectEntered);
                _interactableStart.selectExited.RemoveListener(OnStartSelectExited);
                _interactableStart.hoverEntered.RemoveListener(OnStartHoverEntered);
                _interactableStart.hoverExited.RemoveListener(OnStartHoverExited);
            }
            if (_interactableEnd != null)
            {
                _interactableEnd.selectEntered.RemoveListener(OnEndSelectEntered);
                _interactableEnd.selectExited.RemoveListener(OnEndSelectExited);
                _interactableEnd.hoverEntered.RemoveListener(OnEndHoverEntered);
                _interactableEnd.hoverExited.RemoveListener(OnEndHoverExited);
            }
        }

        private void SubscribeAll()
        {
            if (_isStartSlot && _interactableStart != null)
            {
                if (_useHover)
                {
                    VisionProHoverHelper.EnsurePokeFilterForVisionProHover(_interactableStart);
                    _interactableStart.hoverEntered.AddListener(OnStartHoverEntered);
                    _interactableStart.hoverExited.AddListener(OnStartHoverExited);
                    Debug.Log($"[LongPressTrigger] 已监听「开始」交互体 Hover: {_interactableStart.gameObject.name}，手部接触满 {_holdDurationStart}s 触发开始", this);
                }
                else
                {
                    _interactableStart.selectEntered.AddListener(OnStartSelectEntered);
                    _interactableStart.selectExited.AddListener(OnStartSelectExited);
                    Debug.Log($"[LongPressTrigger] 已监听「开始」交互体 Select: {_interactableStart.gameObject.name}，按住 {_holdDurationStart}s 触发开始", this);
                }
            }
            else if (_isStartSlot)
                Debug.LogWarning("[LongPressTrigger] 开始槽位已选但「开始长按交互体」未赋值，长按无法触发开始。", this);
            if (_isEndSlot && _interactableEnd != null)
            {
                if (_useHover)
                {
                    VisionProHoverHelper.EnsurePokeFilterForVisionProHover(_interactableEnd);
                    _interactableEnd.hoverEntered.AddListener(OnEndHoverEntered);
                    _interactableEnd.hoverExited.AddListener(OnEndHoverExited);
                    Debug.Log($"[LongPressTrigger] 已监听「结束」交互体 Hover: {_interactableEnd.gameObject.name}，手部接触满 {_holdDurationEnd}s 触发结束", this);
                }
                else
                {
                    _interactableEnd.selectEntered.AddListener(OnEndSelectEntered);
                    _interactableEnd.selectExited.AddListener(OnEndSelectExited);
                    Debug.Log($"[LongPressTrigger] 已监听「结束」交互体 Select: {_interactableEnd.gameObject.name}，按住 {_holdDurationEnd}s 触发结束", this);
                }
            }
            else if (_isEndSlot)
                Debug.LogWarning("[LongPressTrigger] 结束槽位已选但「结束长按交互体」未赋值，长按无法触发结束。", this);
        }

        private void OnDestroy()
        {
            UnsubscribeAll();
        }

        private void OnStartSelectEntered(SelectEnterEventArgs args)
        {
            Debug.Log("[LongPressTrigger] 「开始」交互体 SelectEntered 收到", this);
            if (!_enabled) return;
            _selectEnterTime = Time.time;
            _firedStartThisHold = false;
        }

        private void OnStartSelectExited(SelectExitEventArgs args)
        {
            Debug.Log("[LongPressTrigger] 「开始」交互体 SelectExited 收到", this);
            if (!_enabled) return;
            _selectEnterTime = -1f;
            _firedStartThisHold = false;
            if (_isEndSlot && _interactableEnd == _interactableStart)
            {
                _endSelectEnterTime = -1f;
                _firedEndThisHold = false;
            }
        }

        private void OnStartHoverEntered(UnityEngine.XR.Interaction.Toolkit.HoverEnterEventArgs args)
        {
            Debug.Log("[LongPressTrigger] 「开始」交互体 HoverEntered 收到", this);
            if (!_enabled) return;
            _selectEnterTime = Time.time;
            _firedStartThisHold = false;
        }

        private void OnStartHoverExited(UnityEngine.XR.Interaction.Toolkit.HoverExitEventArgs args)
        {
            Debug.Log("[LongPressTrigger] 「开始」交互体 HoverExited 收到", this);
            if (!_enabled) return;
            _selectEnterTime = -1f;
            _firedStartThisHold = false;
            if (_isEndSlot && _interactableEnd == _interactableStart)
            {
                _endSelectEnterTime = -1f;
                _firedEndThisHold = false;
            }
        }

        private void OnEndHoverEntered(UnityEngine.XR.Interaction.Toolkit.HoverEnterEventArgs args)
        {
            Debug.Log("[LongPressTrigger] 「结束」交互体 HoverEntered 收到", this);
            if (!_enabled) return;
            // 不同交互体：在模块已进入进行阶段且经过最小延时后才启动，避免残留 Hover 误触发。同一交互体：仅当本触发器已触发「开始」或（模块已进入进行阶段且经过最小延时）后启动。
            bool allowByDifferent = _interactableEnd != _interactableStart &&
                _moduleInProgress && _moduleEnteredInProgressTime >= 0f &&
                (Time.time - _moduleEnteredInProgressTime) >= EndHoverMinDelayAfterInProgress;
            bool allowBySame = _interactableEnd == _interactableStart &&
                (_firedStartThisHold || (_moduleInProgress && _moduleEnteredInProgressTime >= 0f && (Time.time - _moduleEnteredInProgressTime) >= EndHoverMinDelayAfterInProgress));
            if (allowByDifferent || allowBySame)
            {
                _endSelectEnterTime = Time.time;
                _firedEndThisHold = false;
            }
        }

        private void OnEndHoverExited(UnityEngine.XR.Interaction.Toolkit.HoverExitEventArgs args)
        {
            Debug.Log("[LongPressTrigger] 「结束」交互体 HoverExited 收到", this);
            if (!_enabled) return;
            _endSelectEnterTime = -1f;
            _firedEndThisHold = false;
        }

        private void OnEndSelectEntered(SelectEnterEventArgs args)
        {
            Debug.Log("[LongPressTrigger] 「结束」交互体 SelectEntered 收到", this);
            if (!_enabled) return;
            bool allowByDifferent = _interactableEnd != _interactableStart &&
                _moduleInProgress && _moduleEnteredInProgressTime >= 0f &&
                (Time.time - _moduleEnteredInProgressTime) >= EndHoverMinDelayAfterInProgress;
            bool allowBySame = _interactableEnd == _interactableStart &&
                (_firedStartThisHold || (_moduleInProgress && _moduleEnteredInProgressTime >= 0f && (Time.time - _moduleEnteredInProgressTime) >= EndHoverMinDelayAfterInProgress));
            if (allowByDifferent || allowBySame)
            {
                _endSelectEnterTime = Time.time;
                _firedEndThisHold = false;
            }
        }

        private void OnEndSelectExited(SelectExitEventArgs args)
        {
            Debug.Log("[LongPressTrigger] 「结束」交互体 SelectExited 收到", this);
            if (!_enabled) return;
            _endSelectEnterTime = -1f;
            _firedEndThisHold = false;
        }

        private void Update()
        {
            if (!_enabled) return;

            if (_isStartSlot && _interactableStart != null && _selectEnterTime >= 0f)
            {
                float elapsed = Time.time - _selectEnterTime;
                if (!_firedStartThisHold && elapsed >= _holdDurationStart)
                {
                    _firedStartThisHold = true;
                    Debug.Log("[LongPressTrigger] 按住满时长 → 发出「开始」", this);
                    Triggered?.Invoke(this, true);
                    if (_isEndSlot && _interactableEnd == _interactableStart)
                    {
                        _endSelectEnterTime = Time.time;
                        _firedEndThisHold = false;
                    }
                }
            }

            if (_isEndSlot && _interactableEnd != null && _endSelectEnterTime >= 0f)
            {
                // Editor 下 HoverExited 可能不触发，用 isHovered 判断：若已不再 Hover 则清除结束计时，等同收到 HoverExited
                if (!_firedEndThisHold && !_interactableEnd.isHovered)
                {
                    _endSelectEnterTime = -1f;
                    _firedEndThisHold = false;
                }
                else
                {
                    float elapsedEnd = Time.time - _endSelectEnterTime;
                    if (!_firedEndThisHold && elapsedEnd >= _holdDurationEnd)
                    {
                        _firedEndThisHold = true;
                        Debug.Log("[LongPressTrigger] 按住满时长 → 发出「结束」", this);
                        Triggered?.Invoke(this, false);
                    }
                }
            }
        }

        public void Enable()
        {
            _enabled = true;
        }

        public void Disable()
        {
            _enabled = false;
            _selectEnterTime = -1f;
            _firedStartThisHold = false;
            _endSelectEnterTime = -1f;
            _firedEndThisHold = false;
            _moduleInProgress = false;
            _moduleEnteredInProgressTime = -1f;
        }

        private void OnEnable()
        {
            _enabled = true;
        }

        private void OnDisable()
        {
            _enabled = false;
        }
    }
}
