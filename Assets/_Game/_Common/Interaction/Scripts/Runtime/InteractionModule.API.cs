namespace Interaction
{
    public partial class InteractionModule
    {
        /// <summary>
        /// 外部主动触发「开始」流程（Start 阶段立即执行；Initial 阶段会挂起到初始效果结束后执行）。
        /// </summary>
        public bool TryTriggerStart()
        {
            if (!UnityEngine.Application.isPlaying) return false;
            if (_currentPhase == InteractionPhase.Initial)
            {
                _pendingStartWhileInitial = true;
                return true;
            }
            if (_currentPhase != InteractionPhase.Start) return false;
            HandleTrigger(true);
            return true;
        }

        /// <summary>
        /// 主动请求对指定内容执行「出现」效果。
        /// </summary>
        public void RequestAppear(IContentHandle content, IAppearEffect appearEffect, float? duration = null)
        {
            if (content == null || appearEffect == null) return;
            float d = duration ?? DefaultDurationWhenZero;
            appearEffect.Appear(content, d, () => _appearedContents.Add(content));
        }

        /// <summary>
        /// 主动请求对指定内容执行「消失」效果。
        /// </summary>
        public void RequestDisappear(IContentHandle content, IDisappearEffect disappearEffect, float? duration = null)
        {
            if (content == null || disappearEffect == null) return;
            float d = duration ?? DefaultDurationWhenZero;
            _appearedContents.Remove(content);
            disappearEffect.Disappear(content, d, null);
        }

        /// <summary>
        /// 查询指定内容当前是否处于「已出现」状态。
        /// </summary>
        public bool IsContentAppeared(IContentHandle content)
        {
            return content != null && _appearedContents.Contains(content);
        }

        /// <summary>
        /// 获取出现时长（第一条有效条目的 duration 或模块默认）。
        /// </summary>
        public float DefaultAppearDuration
        {
            get
            {
                return GetDefaultDuration(appearEffectEntries);
            }
        }

        /// <summary>
        /// 获取消失时长（第一条有效条目的 duration 或模块默认）。
        /// </summary>
        public float DefaultDisappearDuration
        {
            get
            {
                return GetDefaultDuration(disappearEffectEntries);
            }
        }
    }
}
