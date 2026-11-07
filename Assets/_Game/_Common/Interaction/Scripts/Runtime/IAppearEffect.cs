using System;
using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// 出现效果接口。对目标内容执行「出现」动画（如透明度、Shader、粒子、VFX、程序动画、关键帧动画）。
    /// </summary>
    public interface IAppearEffect
    {
        /// <summary> 内容（出现效果作用的目标）。在效果组件上配置。 </summary>
        GameObject ContentRoot { get; }

        /// <summary>
        /// 执行出现效果。
        /// </summary>
        /// <param name="target">目标内容句柄</param>
        /// <param name="duration">动画时长（秒）</param>
        /// <param name="onComplete">完成回调（可为 null）</param>
        void Appear(IContentHandle target, float duration, Action onComplete);
    }
}
