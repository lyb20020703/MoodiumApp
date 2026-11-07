namespace Interaction
{
    /// <summary>
    /// 拖拽释放时的策略接口（Phase 3 实现）。
    /// 释放后归位 / 释放固定 / 释放物理碰撞 等具体实现实现此接口。
    /// </summary>
    public interface IDragReleaseBehavior
    {
        /// <summary>
        /// 在拖拽释放时由 InteractionModule 或运行时行为调用，执行对应策略。
        /// </summary>
        /// <param name="content">当前被拖拽的内容句柄</param>
        void OnRelease(IContentHandle content);
    }
}
