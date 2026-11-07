using DG.Tweening;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Interaction
{
    public enum SystemActionType
    {
        PlayBgm = 0,
        StopBgm = 1,
        PlaySfx = 2,
    }

    public struct SystemActionRequest
    {
        public SystemActionType actionType;
        public AudioClip audioClip;
        public float audioVolume;
        public float fadeSeconds;
        public Object context;
    }

    /// <summary>
    /// 全局系统执行器：当前负责系统级音频动作，后续可继续扩展更多全局能力。
    /// </summary>
    public class GlobalSystemRuntime : MonoBehaviour
    {
        private const string RuntimeObjectName = "InteractionSystemRuntime";

        private static GlobalSystemRuntime s_CachedInstance;

        [SerializeField] private AudioSource m_BgmAudioSource;
        [SerializeField] private AudioSource m_SfxAudioSource;

        private Tween m_BgmFadeTween;

        public static GlobalSystemRuntime GetOrCreate()
        {
            if (TryGetExisting(out var runtime))
                return runtime;

            var host = new GameObject(RuntimeObjectName);
            MoveToRuntimeScene(host);
            s_CachedInstance = host.AddComponent<GlobalSystemRuntime>();
            return s_CachedInstance;
        }

        public static GlobalSystemRuntime GetExistingOrNull()
        {
            return TryGetExisting(out var runtime) ? runtime : null;
        }

        public static void StopAllAndResetAll()
        {
            var runtimes = FindObjectsByType<GlobalSystemRuntime>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (int i = 0; i < runtimes.Length; i++)
            {
                if (runtimes[i] == null)
                    continue;

                runtimes[i].StopAllAndReset();
            }
        }

        public void Execute(SystemActionRequest request)
        {
            switch (request.actionType)
            {
                case SystemActionType.PlayBgm:
                    if (request.audioClip == null)
                    {
                        Debug.LogWarning("[全局系统运行时] 播放背景音乐动作缺少音频片段，已跳过。", request.context);
                        return;
                    }

                    PlayBgm(request.audioClip, request.audioVolume, request.fadeSeconds);
                    return;

                case SystemActionType.StopBgm:
                    StopBgm(request.fadeSeconds);
                    return;

                case SystemActionType.PlaySfx:
                    if (request.audioClip == null)
                    {
                        Debug.LogWarning("[全局系统运行时] 播放音效动作缺少音频片段，已跳过。", request.context);
                        return;
                    }

                    PlaySfx(request.audioClip, request.audioVolume);
                    return;

                default:
                    Debug.LogWarning($"[全局系统运行时] 未知系统动作：{DescribeAction(request.actionType)}。", request.context);
                    return;
            }
        }

        public void StopAllAndReset()
        {
            KillBgmFadeTween();

            if (m_BgmAudioSource != null)
            {
                m_BgmAudioSource.Stop();
                m_BgmAudioSource.clip = null;
                m_BgmAudioSource.volume = 1f;
            }

            if (m_SfxAudioSource != null)
            {
                m_SfxAudioSource.Stop();
                m_SfxAudioSource.clip = null;
                m_SfxAudioSource.volume = 1f;
            }
        }

        private void OnDestroy()
        {
            KillBgmFadeTween();
            if (s_CachedInstance == this)
                s_CachedInstance = null;
        }

        private static bool TryGetExisting(out GlobalSystemRuntime runtime)
        {
            runtime = s_CachedInstance;
            if (runtime != null)
                return true;

            var runtimes = FindObjectsByType<GlobalSystemRuntime>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            if (runtimes == null || runtimes.Length == 0)
                return false;

            runtime = runtimes[0];
            s_CachedInstance = runtime;
            return runtime != null;
        }

        private static void MoveToRuntimeScene(GameObject gameObject)
        {
            if (gameObject == null)
                return;

            var scene = SceneManager.GetActiveScene();
            if (scene.IsValid() && scene.isLoaded)
            {
                SceneManager.MoveGameObjectToScene(gameObject, scene);
                return;
            }

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded)
                    continue;

                SceneManager.MoveGameObjectToScene(gameObject, scene);
                return;
            }
        }

        private void PlayBgm(AudioClip clip, float volume, float fadeSeconds)
        {
            var source = EnsureBgmAudioSource();
            if (source == null)
                return;

            float targetVolume = Mathf.Clamp01(volume);
            float fade = Mathf.Max(0f, fadeSeconds);

            KillBgmFadeTween();

            if (!source.isPlaying || source.clip == null)
            {
                source.clip = clip;
                source.loop = true;
                source.volume = fade > 0f ? 0f : targetVolume;
                source.Play();
                if (fade > 0f)
                    StartBgmFade(source, targetVolume, fade, null);
                return;
            }

            if (fade <= 0f)
            {
                source.Stop();
                source.clip = clip;
                source.loop = true;
                source.volume = targetVolume;
                source.Play();
                return;
            }

            StartBgmFade(
                source,
                0f,
                fade,
                () =>
                {
                    if (source == null)
                        return;

                    source.Stop();
                    source.clip = clip;
                    source.loop = true;
                    source.volume = 0f;
                    source.Play();
                    StartBgmFade(source, targetVolume, fade, null);
                });
        }

        private void StopBgm(float fadeSeconds)
        {
            var source = m_BgmAudioSource;
            if (source == null)
                return;

            Configure2DAudioSource(source, loop: true);
            float fade = Mathf.Max(0f, fadeSeconds);
            KillBgmFadeTween();

            if (!source.isPlaying || fade <= 0f)
            {
                source.Stop();
                source.clip = null;
                source.volume = 1f;
                return;
            }

            StartBgmFade(
                source,
                0f,
                fade,
                () =>
                {
                    if (source == null)
                        return;

                    source.Stop();
                    source.clip = null;
                    source.volume = 1f;
                });
        }

        private void PlaySfx(AudioClip clip, float volume)
        {
            var source = EnsureSfxAudioSource();
            if (source == null)
                return;

            source.volume = 1f;
            source.PlayOneShot(clip, Mathf.Clamp01(volume));
        }

        private void StartBgmFade(AudioSource source, float targetVolume, float duration, TweenCallback onComplete)
        {
            if (source == null)
            {
                onComplete?.Invoke();
                return;
            }

            if (duration <= 0f)
            {
                source.volume = targetVolume;
                onComplete?.Invoke();
                return;
            }

            m_BgmFadeTween = DOTween
                .To(
                    () => source.volume,
                    value => source.volume = value,
                    targetVolume,
                    duration)
                .SetTarget(this)
                .OnComplete(() =>
                {
                    m_BgmFadeTween = null;
                    onComplete?.Invoke();
                });
        }

        private void KillBgmFadeTween()
        {
            if (m_BgmFadeTween != null && m_BgmFadeTween.IsActive())
                m_BgmFadeTween.Kill();

            m_BgmFadeTween = null;
        }

        private AudioSource EnsureBgmAudioSource()
        {
            if (m_BgmAudioSource == null)
                m_BgmAudioSource = CreateAudioSource("BgmAudioSource");

            Configure2DAudioSource(m_BgmAudioSource, loop: true);
            return m_BgmAudioSource;
        }

        private AudioSource EnsureSfxAudioSource()
        {
            if (m_SfxAudioSource == null)
                m_SfxAudioSource = CreateAudioSource("SfxAudioSource");

            Configure2DAudioSource(m_SfxAudioSource, loop: false);
            return m_SfxAudioSource;
        }

        private AudioSource CreateAudioSource(string sourceName)
        {
            var sourceObject = new GameObject(sourceName);
            sourceObject.transform.SetParent(transform, false);
            return sourceObject.AddComponent<AudioSource>();
        }

        private static void Configure2DAudioSource(AudioSource source, bool loop)
        {
            if (source == null)
                return;

            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = 0f;
            source.panStereo = 0f;
        }

        private static string DescribeAction(SystemActionType actionType)
        {
            return actionType switch
            {
                SystemActionType.PlayBgm => "播放背景音乐",
                SystemActionType.StopBgm => "停止背景音乐",
                SystemActionType.PlaySfx => "播放音效",
                _ => actionType.ToString()
            };
        }
    }
}
