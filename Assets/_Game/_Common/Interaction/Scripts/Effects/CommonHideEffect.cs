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
    /// 通用「隐藏」效果：根据内容类型执行合适的收尾/隐藏逻辑（Model/Video/Particle/VFX）。
    /// </summary>
    public class CommonHideEffect : MonoBehaviour, IDisappearEffect
    {
        [Tooltip("结束效果作用的内容根。为空则由 InteractionModule 等根据 IContentHandle.Root 决定。")]
        [SerializeField] private GameObject contentRoot;

        public GameObject ContentRoot => contentRoot;

        public void Disappear(IContentHandle target, float duration, Action onComplete)
        {
            if (target?.Root == null)
            {
                onComplete?.Invoke();
                return;
            }

            // 1. 触发结束瞬间：先一次性执行各内容类型的收尾逻辑（释放视频、停止粒子/VFX 等）
            switch (target.Type)
            {
                case ContentType.Video:
                    SuspendVideo(target);
                    break;
                case ContentType.Particle:
                    HandleParticleHide(target);
                    break;
                case ContentType.VFX:
                    HandleVFXHide(target, duration);
                    break;
                case ContentType.Model:
                default:
                    break;
            }

            // 2. VFX 且 duration > 0 时：等待此时长再回调，以便先执行收尾再隐藏；其他类型立即回调
            if (target.Type == ContentType.VFX && duration > 0f)
                DOVirtual.DelayedCall(duration, () => onComplete?.Invoke()).SetTarget(target.Root);
            else
                onComplete?.Invoke();
        }

        /// <summary> 暂停视频（支持 Unity VideoPlayer 与 AVPro MediaPlayer）。供其它效果/模块复用。 </summary>
        public static void PauseVideo(IContentHandle target)
        {
            // Unity 内置 VideoPlayer
            var vp = target.GetComponentForEffect<VideoPlayer>();
            if (vp != null && vp.isPlaying)
                vp.Pause();

            // AVPro MediaPlayer：直接调用组件级 Pause
            var mp = target.GetComponentForEffect<MediaPlayer>();
            if (mp != null)
            {
                AVProMediaDiagnostics.LogPlayRequest(
                    mp,
                    target.Root != null ? target.Root.name : "<missing-root>",
                    "CommonHideEffect.PauseVideo");
                mp.Pause();
                AVProMediaDiagnostics.LogPlayResult(
                    mp,
                    target.Root != null ? target.Root.name : "<missing-root>",
                    "CommonHideEffect.PauseVideo");
            }
        }

        /// <summary>
        /// 挂起视频预览，尽量释放解码/播放压力。
        /// 用于布展模式非 owner 的视频内容，比 Pause 更激进。
        /// </summary>
        public static void SuspendVideo(IContentHandle target)
        {
            if (target == null)
                return;

            var vp = target.GetComponentForEffect<VideoPlayer>();
            if (vp != null)
            {
                if (vp.isPlaying || vp.isPrepared)
                    vp.Stop();
            }

            var avp = target.GetComponentForEffect<VideoPlayer_AVPro>();
            if (avp != null)
            {
                avp.CloseMedia();
                return;
            }

            var mp = target.GetComponentForEffect<MediaPlayer>();
            if (mp != null)
            {
                AVProMediaDiagnostics.LogCloseRequest(
                    mp,
                    target.Root != null ? target.Root.name : "<missing-root>",
                    "CommonHideEffect.SuspendVideo");
                mp.CloseMedia();
                AVProMediaDiagnostics.LogCloseResult(
                    mp,
                    target.Root != null ? target.Root.name : "<missing-root>",
                    "CommonHideEffect.SuspendVideo");
            }
        }

        /// <summary>
        /// 布展预览里临时停用视频，但不彻底关闭媒体，避免频繁显示/隐藏时反复 reopen 导致失效。
        /// 体验模式正式结束仍然走 SuspendVideo() 做真正释放。
        /// </summary>
        public static void SuspendPlacementPreviewVideo(IContentHandle target)
        {
            if (target == null)
                return;

            var vp = target.GetComponentForEffect<VideoPlayer>();
            if (vp != null)
            {
                if (vp.isPlaying || vp.isPrepared)
                    vp.Stop();
            }

            var avp = target.GetComponentForEffect<VideoPlayer_AVPro>();
            if (avp != null)
            {
                TryInvokeNoArg(avp, "Stop");
                TryInvokeNoArg(avp, "Pause");
                return;
            }

            var mp = target.GetComponentForEffect<MediaPlayer>();
            if (mp != null)
            {
                AVProMediaDiagnostics.EnsureForMediaPlayer(
                    mp,
                    target.Root != null ? target.Root.name : "<missing-root>",
                    "CommonHideEffect.SuspendPlacementPreviewVideo.stop_before");
                mp.Stop();
                AVProMediaDiagnostics.EnsureForMediaPlayer(
                    mp,
                    target.Root != null ? target.Root.name : "<missing-root>",
                    "CommonHideEffect.SuspendPlacementPreviewVideo.stop_after");
            }
        }

        private static void TryInvokeNoArg(object instance, string methodName)
        {
            if (instance == null || string.IsNullOrWhiteSpace(methodName))
                return;

            try
            {
                var method = instance.GetType().GetMethod(methodName, Type.EmptyTypes);
                method?.Invoke(instance, null);
            }
            catch
            {
            }
        }

        private static void HandleParticleHide(IContentHandle target)
        {
            var systems = target.Root.GetComponentsInChildren<ParticleSystem>(true);
            if (systems == null || systems.Length == 0) return;

            foreach (var ps in systems)
            {
                if (ps == null) continue;
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private static void HandleVFXHide(IContentHandle target, float duration)
        {
            if (VFXRuntimeGuard.DisableUnsupportedVFX(target.Root))
                return;

            var controller = target.Root.GetComponentInChildren<IVFXController>(true);
            if (controller != null)
            {
                controller.Disappear(duration, null);
                return;
            }

            var vfx = target.Root.GetComponentInChildren<VisualEffect>(true);
            if (vfx != null)
                vfx.Stop();
        }

        private static void SafeSetInactive(GameObject go)
        {
            if (go != null && go.activeSelf)
                go.SetActive(false);
        }
    }
}
