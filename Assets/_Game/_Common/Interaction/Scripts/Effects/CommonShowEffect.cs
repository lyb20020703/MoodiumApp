using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.VFX;
using UnityEngine.Video;
using RenderHeads.Media.AVProVideo;
using VFXViewer;

namespace Interaction
{
    /// <summary>
    /// 通用「显示」效果：根据内容类型执行合适的显现逻辑（Model/Video/Particle/VFX）。
    /// </summary>
    public class CommonShowEffect : MonoBehaviour, IAppearEffect
    {
        [Tooltip("出现效果作用的内容根。为空则由 InteractionModule 等根据 IContentHandle.Root 决定。")]
        [SerializeField] private GameObject contentRoot;

        public GameObject ContentRoot => contentRoot;

        public void Appear(IContentHandle target, float duration, Action onComplete)
        {
            if (target?.Root == null)
            {
                onComplete?.Invoke();
                return;
            }

            void DoShow()
            {
                var root = target.Root;
                if (!root.activeSelf)
                    root.SetActive(true);

                switch (target.Type)
                {
                    case ContentType.Video:
                        PlayVideo(target);
                        break;
                    case ContentType.Particle:
                        HandleParticleShow(target);
                        break;
                    case ContentType.VFX:
                        HandleVFXShow(target, 0f, onComplete);
                        return; // 由 VFX 控制回调
                    case ContentType.Model:
                    default:
                        break;
                }

                onComplete?.Invoke();
            }

            // 开始过程时长：触发开始后等待 duration 秒，再执行显示逻辑
            if (duration > 0f)
                DOVirtual.DelayedCall(duration, DoShow).SetTarget(target.Root);
            else
                DoShow();
        }

        /// <summary> 播放视频（支持 Unity VideoPlayer 与 AVPro MediaPlayer）。供其它效果/模块复用。 </summary>
        public static void PlayVideo(IContentHandle target)
        {
            // Unity 内置 VideoPlayer
            var vp = target.GetComponentForEffect<VideoPlayer>();
            if (vp != null && !vp.isPlaying)
                vp.Play();

            // AVPro VideoPlayer adapter: use the same guarded MediaPlayer path.
            var avp = target.GetComponentForEffect<VideoPlayer_AVPro>();
            if (avp != null)
            {
                AVProSafePlayback.Play(
                    avp,
                    target.Root,
                    target.Root != null ? target.Root.name : "<missing-root>",
                    "CommonShowEffect.PlayVideo.VideoPlayer_AVPro");
                return;
            }

            // AVPro MediaPlayer: open only after metadata/texture state is valid.
            var mp = target.GetComponentForEffect<MediaPlayer>();
            if (mp != null)
            {
                AVProSafePlayback.Play(
                    mp,
                    target.Root,
                    target.Root != null ? target.Root.name : "<missing-root>",
                    "CommonShowEffect.PlayVideo");
            }
        }

        private static void HandleParticleShow(IContentHandle target)
        {
            var systems = target.Root.GetComponentsInChildren<ParticleSystem>(true);
            if (systems == null || systems.Length == 0) return;

            foreach (var ps in systems)
            {
                if (ps == null) continue;
                ps.Clear(true);
                ps.Simulate(0f, true, true);
                ps.Play(true);
            }
        }

        private static void HandleVFXShow(IContentHandle target, float duration, Action onComplete)
        {
            if (VFXRuntimeGuard.DisableUnsupportedVFX(target.Root))
            {
                onComplete?.Invoke();
                return;
            }

            var controller = target.Root.GetComponentInChildren<IVFXController>(true);
            if (controller != null)
            {
                if (controller is Component controllerComponent)
                {
                    ActivateControllerHierarchy(target.Root.transform, controllerComponent.transform);
                    if (!controllerComponent.gameObject.activeInHierarchy)
                    {
                        Debug.LogWarning(
                            $"[{nameof(CommonShowEffect)}] VFX 控制器仍处于未激活层级，已跳过播放：{controllerComponent.name}",
                            controllerComponent);
                        onComplete?.Invoke();
                        return;
                    }
                }

                controller.Appear(duration, onComplete);
                return;
            }

            var vfx = target.Root.GetComponentInChildren<VisualEffect>(true);
            if (vfx != null)
                vfx.Play();

            onComplete?.Invoke();
        }

        private static void ActivateControllerHierarchy(Transform root, Transform controllerTransform)
        {
            for (var current = controllerTransform; current != null; current = current.parent)
            {
                if (!current.gameObject.activeSelf)
                    current.gameObject.SetActive(true);

                if (current == root)
                    break;
            }
        }
    }
}
