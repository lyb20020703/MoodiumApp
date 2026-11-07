using System;
using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// 时间触发器：开始延时后触发「开始」，结束延时后触发「结束」。
    /// 规则：开始是时间触发器时，从交互模块出现（Start）时起算开始延时；
    /// 结束是时间触发器时，一律从「开始触发器触发之后」起算结束延时。
    /// </summary>
    public class TimeTrigger : MonoBehaviour, ITrigger
    {
        [Tooltip("开始触发前的延时（秒），0 表示立刻触发开始")]
        [SerializeField] private float startDelay = 1f;
        [Tooltip("结束触发前的延时（秒，从开始触发后算），0 表示不触发结束")]
        [SerializeField] private float endDelay;
        [Tooltip("是否循环")]
        [SerializeField] private bool repeat;

        public event Action<ITrigger, bool> Triggered;

        /// <summary> 由 InteractionModule 等从配置注入参数。fireStart/fireEnd 表示是否触发开始/结束。 </summary>
        public void SetConfig(float startDelaySec, float endDelaySec, bool repeatTrigger, bool fireStart, bool fireEnd)
        {
            _hasStartTrigger = fireStart;
            startDelay = Mathf.Max(0f, startDelaySec);
            endDelay = fireEnd ? endDelaySec : 0f;
            repeat = repeatTrigger;
        }

        private float _timer;
        private float _phaseTimer; // 开始触发后的计时，用于 endDelay
        private bool _enabled;
        private bool _firedStart;
        private bool _firedEnd;
        private bool _hasStartTrigger;
        /// <summary> 仅当「结束」由模块在开始触发后手动启动计时时使用。 </summary>
        private bool _endDelayCounting;
        private float _endDelayStartTime;
        /// <summary> 是否允许使用「自带开始→结束计时」路径。列表模式 + 开始/结束都为时间时会关闭，自行由模块驱动结束计时。 </summary>
        private bool _useSelfTimedEnd = true;

        /// <summary>
        /// 由 InteractionModule 在「开始」触发后调用，使结束延时从此刻起算（用于开始≠时间、结束=时间的组合）。
        /// </summary>
        public void BeginEndDelayCountdown()
        {
            _endDelayCounting = true;
            _endDelayStartTime = Time.time;
            // 确保计时器处于可用状态：重新允许 Update 驱动结束计时，并重置已结束标记
            _enabled = true;
            _firedEnd = false;
        }

        /// <summary> 禁用内部自带的「从 startDelay/endDelay 推算结束」逻辑，仅使用 BeginEndDelayCountdown 路径。 </summary>
        public void DisableSelfTimedEnd()
        {
            _useSelfTimedEnd = false;
        }

        /// <summary>
        /// 由 InteractionModule 在「结束阶段→回到开始阶段」时调用，重置本轮计时状态，便于下一轮循环交互继续使用时间触发。
        /// </summary>
        public void ResetForNextCycle()
        {
            _timer = 0f;
            _phaseTimer = 0f;
            _firedStart = false;
            _firedEnd = false;
            _endDelayCounting = false;
        }

        private void Update()
        {
            if (!_enabled) return;

            _timer += Time.deltaTime;

            if (_hasStartTrigger && !_firedStart && _timer >= startDelay)
            {
                _firedStart = true;
                _phaseTimer = 0f;
                Triggered?.Invoke(this, true);
            }

            // 结束：1) 由模块在开始后启动的独立结束计时  2) 本触发器自己「开始→结束」的 phaseTimer  3) 仅结束时用 _timer
            if (!_firedEnd)
            {
                if (_endDelayCounting)
                {
                    if (endDelay <= 0f || Time.time - _endDelayStartTime >= endDelay)
                    {
                        _endDelayCounting = false;
                        _firedEnd = true;
                        Triggered?.Invoke(this, false);
                    }
                }
                else if (_useSelfTimedEnd && endDelay >= 0f)
                {
                    if (_firedStart)
                        _phaseTimer += Time.deltaTime;
                    float elapsed = _firedStart ? _phaseTimer : _timer;
                    if (elapsed >= endDelay)
                    {
                        _firedEnd = true;
                        Triggered?.Invoke(this, false);
                    }
                }
            }

            if (repeat && (_firedStart || !_hasStartTrigger) && (_firedEnd || endDelay <= 0f))
            {
                _timer = 0f;
                _phaseTimer = 0f;
                _firedStart = false;
                _firedEnd = false;
                _endDelayCounting = false;
            }
        }

        public void Enable()
        {
            _enabled = true;
            _timer = 0f;
            _phaseTimer = 0f;
            _firedStart = false;
            _firedEnd = false;
            _endDelayCounting = false;
        }

        public void Disable()
        {
            _enabled = false;
        }

        private void OnEnable()
        {
            _enabled = true;
            _timer = 0f;
            _phaseTimer = 0f;
            _firedStart = false;
            _firedEnd = false;
            _endDelayCounting = false;
        }

        private void OnDisable()
        {
            _enabled = false;
        }
    }
}
