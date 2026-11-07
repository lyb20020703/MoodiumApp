using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// 内容句柄：抽象当前被交互的一「块」内容，与具体 GameObject/组件解耦。
    /// 支持图片、视频、模型三种类型，供触发器、效果系统、运行时行为使用。
    /// </summary>
    public interface IContentHandle
    {
        /// <summary> 根 GameObject。 </summary>
        GameObject Root { get; }

        /// <summary> 根 Transform，便于效果系统做位移/旋转/缩放。 </summary>
        Transform RootTransform { get; }

        /// <summary> 内容类型。 </summary>
        ContentType Type { get; }

        /// <summary>
        /// 获取用于效果的组件（如 Renderer、CanvasGroup、ParticleSystem、VFX 等）。
        /// 效果系统按需调用，无需与具体内容实现强耦合。
        /// </summary>
        T GetComponentForEffect<T>() where T : Component;
    }
}
