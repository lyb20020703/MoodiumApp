using System;

namespace Interaction
{
    /// <summary>
    /// VFX 出现/消失驱动接口。需要向 VFX 传参或特殊行为（如聚合/消散）时，在内容根或其子物体上挂实现此接口的 Controller，
    /// VFXAppear/VFXDisappear 会优先调用接口方法；未实现时回退为 VisualEffect.Play/Stop。
    /// </summary>
    public interface IVFXController
    {
        /// <summary> 执行出现效果（如聚合），在完成或超时后调用 onComplete。 </summary>
        void Appear(float duration, Action onComplete);

        /// <summary> 执行消失效果（如消散），在完成或超时后调用 onComplete。 </summary>
        void Disappear(float duration, Action onComplete);
    }

    /// <summary>
    /// VFX 在布展预览态下的可选扩展接口。
    /// 用于把运行时特效直接推到稳定可见状态，而不是依赖正式模块播放流程。
    /// </summary>
    public interface IPlacementPreviewVFXController
    {
        void PrewarmForPlacementPreview();

        bool IsPlacementPreviewPrewarmed { get; }

        void ShowForPlacementPreview();

        void HideForPlacementPreview();

        void RestoreAfterPlacementPreview();
    }
}
