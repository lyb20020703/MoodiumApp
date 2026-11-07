using System;
using UnityEngine;
using UnityEngine.VFX;
using UnityEngine.Video;
using RenderHeads.Media.AVProVideo;

namespace Interaction
{
    /// <summary>
    /// 内容句柄工厂：根据内容根节点上的组件自动推断 <see cref="ContentType"/>，
    /// 并返回对应的 <see cref="IContentHandle"/> 实现。
    /// 对外只接受 GameObject，不暴露 ContentType 参数。
    /// </summary>
    public static class ContentHandleFactory
    {
        /// <summary>
        /// 仅根据内容根节点推断内容类型。
        /// </summary>
        public static ContentType DetectType(GameObject root)
        {
            if (root == null) return ContentType.Model;

            // VFX：根据 VisualEffect 判断（IVFXController 不是 Component，不能直接用于泛型约束）
            if (HasComponentInChildren<VisualEffect>(root))
            {
                if (VFXRuntimeGuard.DisableUnsupportedVFX(root))
                    return ContentType.Model;
                return ContentType.VFX;
            }

            if (HasComponentInChildren<ParticleSystem>(root))
                return ContentType.Particle;

            // 内置 VideoPlayer、AVPro Video 的 VideoPlayer/MediaPlayer 都视为 Video 内容
            if (HasComponentInChildren<VideoPlayer>(root)
                || HasComponentInChildren<VideoPlayer_AVPro>(root)
                || HasComponentInChildren<MediaPlayer>(root))
                return ContentType.Video;

            return ContentType.Model;
        }

        /// <summary>
        /// 根据内容根节点创建合适的 IContentHandle。
        /// </summary>
        /// <param name="root">内容根节点，不能为空。</param>
        /// <exception cref="ArgumentNullException">root 为空时抛出。</exception>
        public static IContentHandle Create(GameObject root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));

            switch (DetectType(root))
            {
                case ContentType.VFX:
                    return new VFXContentHandle(root);
                case ContentType.Particle:
                    return new ParticleContentHandle(root);
                case ContentType.Video:
                    return new VideoContentHandle(root);
                case ContentType.Model:
                default:
                    return new ModelContentHandle(root);
            }
        }

        private static bool HasComponentInChildren<T>(GameObject root) where T : Component
        {
            return root != null && root.GetComponentInChildren<T>(true) != null;
        }
    }
}
