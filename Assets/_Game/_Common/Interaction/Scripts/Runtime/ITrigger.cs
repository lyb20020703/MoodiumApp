using System;

namespace Interaction
{
    /// <summary>
    /// 触发器接口。多种输入源（时间、位置、触摸等）实现此接口，在满足条件时触发「开始」或「结束」事件。
    /// isStart=true 表示开始触发器（驱动出现），isStart=false 表示结束触发器（驱动消失）。
    /// </summary>
    public interface ITrigger
    {
        /// <summary> 满足条件时触发。isStart: true=开始(出现)，false=结束(消失)。 </summary>
        event Action<ITrigger, bool> Triggered;

        /// <summary> 启用触发器。 </summary>
        void Enable();

        /// <summary> 禁用触发器。 </summary>
        void Disable();
    }
}
