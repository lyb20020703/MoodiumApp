using System;
using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// 消失效果接口。对目标内容执行「消失」动画。
    /// </summary>
    public interface IDisappearEffect
    {
        /// <summary> 内容（结束效果作用的目标）。在效果组件上配置。 </summary>
        GameObject ContentRoot { get; }

        /// <summary>
        /// 执行消失效果。
        /// </summary>
        /// <param name="target">目标内容句柄</param>
        /// <param name="duration">动画时长（秒）</param>
        /// <param name="onComplete">完成回调（可为 null）</param>
        void Disappear(IContentHandle target, float duration, Action onComplete);
    }
}
