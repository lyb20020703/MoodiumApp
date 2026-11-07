using UnityEngine;

namespace Interaction
{
    public partial class InteractionModule
    {
        /// <summary>
        /// 注册触发器（API 用）：触发时根据 isStart 执行「出现」或「消失」。
        /// </summary>
        public void RegisterTrigger(
            ITrigger trigger,
            IContentHandle content,
            IAppearEffect appearEffect,
            IDisappearEffect disappearEffect)
        {
            if (trigger == null || content == null || appearEffect == null || disappearEffect == null)
            {
                Debug.LogWarning("RegisterTrigger 参数不能为空。");
                return;
            }
            _contentHandle = content;
            _appearEffect = appearEffect;
            _disappearEffect = disappearEffect;
            trigger.Triggered -= OnTriggerFired;
            trigger.Triggered += OnTriggerFired;
            if (!_triggers.Contains(trigger))
            {
                _triggers.Add(trigger);
                RegisterTriggerInstance(trigger);
            }
        }

        /// <summary>
        /// 取消注册触发器。
        /// </summary>
        public void UnregisterTrigger(ITrigger trigger)
        {
            if (trigger == null) return;
            trigger.Triggered -= OnTriggerFired;
            _triggers.Remove(trigger);
            UnregisterTriggerInstance(trigger);
        }

        /// <summary>
        /// 取消所有触发器注册。
        /// </summary>
        public void UnregisterAllTriggers()
        {
            foreach (var t in _triggers)
                t.Triggered -= OnTriggerFired;
            _triggers.Clear();
            ClearTriggerCaches();
        }
    }
}
