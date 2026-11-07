using UnityEngine;

namespace Interaction
{
    public partial class InteractionModule
    {
        // ---------- 拖拽释放行为（Phase 3）----------

        /// <summary>
        /// 为指定内容注册拖拽释放时的策略（归位/固定/物理碰撞等）。
        /// 释放时由 XRI 桥接或外部调用 <see cref="NotifyDragReleased"/>，模块会执行对应策略。
        /// </summary>
        public void RegisterDragReleaseBehavior(IContentHandle content, IDragReleaseBehavior behavior)
        {
            if (content?.Root == null || behavior == null) return;
            _dragReleaseBehaviors[content.Root.GetInstanceID()] = behavior;
        }

        /// <summary>
        /// 取消指定内容的拖拽释放行为。
        /// </summary>
        public void UnregisterDragReleaseBehavior(IContentHandle content)
        {
            if (content?.Root == null) return;
            _dragReleaseBehaviors.Remove(content.Root.GetInstanceID());
        }

        /// <summary>
        /// 通知本模块：指定内容刚被拖拽释放。会查找本模块内已注册的 <see cref="IDragReleaseBehavior"/> 并执行。
        /// </summary>
        public void NotifyDragReleased(IContentHandle content)
        {
            if (content?.Root == null) return;
            if (_dragReleaseBehaviors.TryGetValue(content.Root.GetInstanceID(), out var behavior))
                behavior.OnRelease(content);
        }

        /// <summary>
        /// 静态方法：通知「拥有该内容」的交互模块执行拖拽释放行为。
        /// 由 XRI 桥接（InteractionDragReleaseBridge）或叙事层在 Grab/Select 释放时调用；会自动找到注册了该内容的模块。
        /// </summary>
        public static void NotifyDragReleasedStatic(IContentHandle content)
        {
            if (content?.Root == null) return;
            int id = content.Root.GetInstanceID();
            for (int i = 0; i < s_instances.Count; i++)
            {
                if (s_instances[i] != null && s_instances[i].HasDragReleaseFor(id))
                {
                    s_instances[i].NotifyDragReleased(content);
                    return;
                }
            }
        }

        private bool HasDragReleaseFor(int contentRootId)
        {
            return _dragReleaseBehaviors.ContainsKey(contentRootId);
        }
    }
}
