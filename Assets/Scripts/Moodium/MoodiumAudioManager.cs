using System.Collections;
using UnityEngine;

namespace Moodium.Audio
{
    public enum MoodiumAudioCue
    {
        LogoAppear,
        OpeningCandyTouch,
        OpeningCandyCrack,
        SpriteReveal,
        RealitySqueeze,
        ObjectTouch,
        CandyEnergyComplete
    }

    [DisallowMultipleComponent]
    public sealed class MoodiumAudioManager : MonoBehaviour
    {
        public static MoodiumAudioManager Instance { get; private set; }

        [Header("Opening")]
        [SerializeField] AudioClip m_LogoAppear;
        [SerializeField] AudioClip m_OpeningCandyTouch;
        [SerializeField] AudioClip m_OpeningCandyCrack;
        [SerializeField] AudioClip m_SpriteReveal;

        [Header("Experiences")]
        [SerializeField] AudioClip m_RealitySqueeze;
        [Tooltip("Reserved for Creative Space particle + audio feedback. Leave empty to keep it silent.")]
        [SerializeField] AudioClip m_ObjectTouch;
        [SerializeField] AudioClip m_CandyEnergyComplete;

        [Header("Playback")]
        [SerializeField, Range(0f, 1f)] float m_MasterVolume = 0.75f;
        [SerializeField, Range(0f, 1f)] float m_BackgroundMusicVolume = 0.35f;
        [SerializeField, Min(0f)] float m_BackgroundMusicCrossfadeDuration = 0.8f;
        [SerializeField, Range(0f, 1f)] float m_SpatialBlend = 0.8f;

        AudioSource m_BackgroundMusicSource;
        AudioSource m_BackgroundMusicCrossfadeSource;
        AudioSource m_InterfaceSource;
        AudioSource m_SpatialSource;
        Coroutine m_BackgroundMusicCrossfade;
        AudioClip m_CurrentBackgroundMusic;
        public static double BackgroundMusicDuration=>Instance!=null&&Instance.m_CurrentBackgroundMusic!=null?Instance.m_CurrentBackgroundMusic.length:0;
        /// <summary>Reads the requested BGM, not the outgoing crossfade source.</summary>
        public static bool TryGetBackgroundMusicTime(out double seconds)
        {
            seconds=0;
            var instance=Instance;if(instance==null||instance.m_CurrentBackgroundMusic==null)return false;
            var a=instance.m_BackgroundMusicSource;var b=instance.m_BackgroundMusicCrossfadeSource;
            // A paused source still owns the music clock; don't switch to wall time.
            var source=a!=null&&a.clip==instance.m_CurrentBackgroundMusic?a:
                b!=null&&b.clip==instance.m_CurrentBackgroundMusic?b:null;
            if(source==null)return false;
            seconds=source.timeSamples/(double)Mathf.Max(1,source.clip.frequency);return true;
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
            m_BackgroundMusicSource = CreateSource("Moodium Background Music", 0f);
            m_BackgroundMusicSource.loop = true;
            m_BackgroundMusicSource.volume = m_BackgroundMusicVolume;
            m_BackgroundMusicCrossfadeSource = CreateSource("Moodium Background Music Crossfade", 0f);
            m_BackgroundMusicCrossfadeSource.loop = true;
            m_BackgroundMusicCrossfadeSource.volume = 0f;
            m_InterfaceSource = CreateSource("Moodium Interface Audio", 0f);
            m_SpatialSource = CreateSource("Moodium Spatial Audio", m_SpatialBlend);
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public static void Play(MoodiumAudioCue cue)
        {
            Instance?.PlayCue(cue, null);
        }

        public static void Play(MoodiumAudioCue cue, Vector3 worldPosition)
        {
            Instance?.PlayCue(cue, worldPosition);
        }

        public static void PlayBackgroundMusic(AudioClip clip)
        {
            Instance?.PlayBackgroundMusicClip(clip);
        }

        public static void StopBackgroundMusic()
        {
            Instance?.StopBackgroundMusicClip();
        }

        public void PlayCue(MoodiumAudioCue cue, Vector3? worldPosition)
        {
            var clip = GetClip(cue);
            if (clip == null)
                return;
            var source = worldPosition.HasValue ? m_SpatialSource : m_InterfaceSource;
            if (worldPosition.HasValue)
                source.transform.position = worldPosition.Value;
            source.PlayOneShot(clip, m_MasterVolume);
            Debug.Log($"[Moodium Audio] Play {cue}: {clip.name}");
        }

        void PlayBackgroundMusicClip(AudioClip clip)
        {
            if (clip == null)
            {
                StopBackgroundMusicClip();
                return;
            }

            if (m_CurrentBackgroundMusic == clip &&
                ((m_BackgroundMusicSource.clip == clip && m_BackgroundMusicSource.isPlaying) ||
                 (m_BackgroundMusicCrossfadeSource.clip == clip && m_BackgroundMusicCrossfadeSource.isPlaying)))
                return;

            if (m_BackgroundMusicCrossfade != null)
            {
                StopCoroutine(m_BackgroundMusicCrossfade);
                m_BackgroundMusicCrossfade = null;
            }

            var targetSource = GetSourceForClip(clip);
            var otherSource = targetSource == m_BackgroundMusicSource
                ? m_BackgroundMusicCrossfadeSource
                : m_BackgroundMusicSource;

            if (targetSource.clip != clip)
            {
                targetSource.Stop();
                targetSource.clip = clip;
                targetSource.volume = 0f;
            }
            if (!targetSource.isPlaying)
                targetSource.Play();

            m_CurrentBackgroundMusic = clip;
            if (!otherSource.isPlaying || m_BackgroundMusicCrossfadeDuration <= 0f)
            {
                otherSource.Stop();
                otherSource.clip = null;
                otherSource.volume = 0f;
                targetSource.volume = m_BackgroundMusicVolume;
            }
            else
            {
                m_BackgroundMusicCrossfade = StartCoroutine(CrossfadeBackgroundMusic(
                    otherSource,
                    targetSource,
                    m_BackgroundMusicCrossfadeDuration));
            }
            Debug.Log($"[Moodium Audio] Play world BGM: {clip.name}");
        }

        void StopBackgroundMusicClip()
        {
            if (m_BackgroundMusicSource == null)
                return;

            if (m_BackgroundMusicCrossfade != null)
            {
                StopCoroutine(m_BackgroundMusicCrossfade);
                m_BackgroundMusicCrossfade = null;
            }

            StopAndClear(m_BackgroundMusicSource);
            StopAndClear(m_BackgroundMusicCrossfadeSource);
            m_CurrentBackgroundMusic = null;
        }

        AudioSource GetSourceForClip(AudioClip clip)
        {
            if (m_BackgroundMusicSource.clip == clip)
                return m_BackgroundMusicSource;
            if (m_BackgroundMusicCrossfadeSource.clip == clip)
                return m_BackgroundMusicCrossfadeSource;

            if (!m_BackgroundMusicSource.isPlaying)
                return m_BackgroundMusicSource;
            if (!m_BackgroundMusicCrossfadeSource.isPlaying)
                return m_BackgroundMusicCrossfadeSource;
            return m_BackgroundMusicSource.volume <= m_BackgroundMusicCrossfadeSource.volume
                ? m_BackgroundMusicSource
                : m_BackgroundMusicCrossfadeSource;
        }

        IEnumerator CrossfadeBackgroundMusic(AudioSource from, AudioSource to, float duration)
        {
            var elapsed = 0f;
            var fromStartVolume = from.volume;
            var toStartVolume = to.volume;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                var progress = Mathf.Clamp01(elapsed / duration);
                from.volume = Mathf.Lerp(fromStartVolume, 0f, progress);
                to.volume = Mathf.Lerp(toStartVolume, m_BackgroundMusicVolume, progress);
                yield return null;
            }

            StopAndClear(from);
            to.volume = m_BackgroundMusicVolume;
            m_BackgroundMusicCrossfade = null;
        }

        static void StopAndClear(AudioSource source)
        {
            if (source == null)
                return;
            source.Stop();
            source.clip = null;
            source.volume = 0f;
        }

        AudioSource CreateSource(string sourceName, float spatialBlend)
        {
            var child = new GameObject(sourceName);
            child.transform.SetParent(transform, false);
            var source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = spatialBlend;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 0.15f;
            source.maxDistance = 5f;
            return source;
        }

        AudioClip GetClip(MoodiumAudioCue cue) => cue switch
        {
            MoodiumAudioCue.LogoAppear => m_LogoAppear,
            MoodiumAudioCue.OpeningCandyTouch => m_OpeningCandyTouch,
            MoodiumAudioCue.OpeningCandyCrack => m_OpeningCandyCrack,
            MoodiumAudioCue.SpriteReveal => m_SpriteReveal,
            MoodiumAudioCue.RealitySqueeze => m_RealitySqueeze,
            MoodiumAudioCue.ObjectTouch => m_ObjectTouch,
            MoodiumAudioCue.CandyEnergyComplete => m_CandyEnergyComplete,
            _ => null
        };
    }
}
