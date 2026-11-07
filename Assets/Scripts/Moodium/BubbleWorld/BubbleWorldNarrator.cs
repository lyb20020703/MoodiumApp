using System.Collections;
using System.Collections.Generic;
using Moodium.Characters;
using UnityEngine;

namespace Moodium.NatureWorld
{
    /// <summary>
    /// Moodi's spoken welcome when a bubble world layer opens around the viewer (one short, calm intro per layer).
    /// Moodi is hidden inside the bubble, so this plays as a non-spatial voice close to the listener instead of
    /// through MoodiVoice's 3D emitter. Each layer is introduced once per world session.
    /// Clips: Resources/MoodiumVoice/{zh|en}/moodi_vo_{W1c|W2c|W1n|W2n|W3n}_{lang}.wav
    /// (2026-11-03) An intro counts as said only once it has played (at least HeardShare of it). Stepping back out during
    /// the 1 s delay or early in the clip used to lose that world's welcome for the whole session.
    /// </summary>
    public sealed class BubbleWorldNarrator : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f)] float m_Volume = .9f;
        [SerializeField] float m_Delay = 1f;
        const float FadeSeconds = .5f;
        /// <summary>An intro stopped after this share of it has played counts as heard.</summary>
        public const float HeardShare = .8f;

        readonly Queue<AudioClip> m_Lines=new();
        AudioSource m_Source;
        Coroutine m_Routine;
        readonly HashSet<string> m_Said = new HashSet<string>();
        string m_Pending;
        AudioClip m_PendingClip;

        /// <summary>The intro being delayed or played right now (not yet counted as said), or null.</summary>
        public string Pending => m_Pending;
        public bool HasSaid(string id) => m_Said.Contains(id);
        public static bool CountsAsHeard(float played, float length) => length > 0f && played / length + 1e-6f >= HeardShare;

        /// <summary>Intro line for a bubble world folder: W1c candy marshmallow, W2c candy mint chocolate,
        /// W1n green forest, W2n snowy forest, W3n maple forest (枫林). Null for an unknown folder.</summary>
        public static string IntroId(string folder) => folder switch
        {
            BubbleWorldData.CandyFolder => "W1c",
            BubbleWorldData.NestedCandyFolder => "W2c",
            BubbleWorldData.NatureFolder => "W1n",
            BubbleWorldData.NestedNatureFolder => "W2n",
            BubbleWorldData.MapleNatureFolder => "W3n",
            _ => null,
        };

        public static readonly string[] AllIds = { "W1c", "W2c", "W1n", "W2n", "W3n" };

        /// <summary>True from the moment an intro is scheduled until it has finished (or was stopped).</summary>
        public bool IsSpeaking => m_Routine != null;

        /// <summary>Forget which layers were introduced (call when a new world session starts).</summary>
        public void ResetSession() { Stop(); m_Said.Clear(); }

        /// <summary>Introduce the layer that just opened. Stops any intro still playing from the outer layer.</summary>
        public bool PlayIntro(string folder)
        {
            var id = IntroId(folder);
            if (folder == BubbleWorldData.NatureFolder || id == null || m_Said.Contains(id) || id == m_Pending) return false;
            var clip = Resources.Load<AudioClip>(MoodiVoice.ClipResourcePath(id, MoodiVoice.LanguageCode));
            if (clip == null) { Debug.LogWarning($"[Bubble World] Missing intro voice {id} ({MoodiVoice.LanguageCode})."); return false; }
            SettlePending();
            m_Pending = id; m_PendingClip = clip; m_Lines.Clear();
            StopRoutine();
            m_Routine = StartCoroutine(Speak(clip));
            return true;
        }

        /// <summary>Fade the intro out (the user did something Moodi needs to answer, or left the bubble).</summary>
        public void Stop()
        {
            SettlePending();
            m_Lines.Clear();
            if (m_Routine == null) return;
            StopRoutine();
            if (m_Source != null && m_Source.isPlaying && isActiveAndEnabled) m_Routine = StartCoroutine(FadeOut());
            else if (m_Source != null) m_Source.Stop();
        }

        public void SayLine(string id)
        {
            if(!isActiveAndEnabled||!Application.isPlaying)return;
            var clip=Resources.Load<AudioClip>(MoodiVoice.ClipResourcePath(id,MoodiVoice.LanguageCode));
            if(clip==null){Debug.LogWarning("[Bubble Play] Missing voice "+id);return;}
            if(m_Lines.Count<8)m_Lines.Enqueue(clip);
            if(m_Routine==null)m_Routine=StartCoroutine(DrainLines());
        }
        IEnumerator DrainLines()
        {
            yield return null;
            while(m_Lines.Count>0){var source=EnsureSource();source.volume=m_Volume;source.clip=m_Lines.Dequeue();source.Play();while(source.isPlaying)yield return null;}
            m_Routine=null;
        }
        IEnumerator Speak(AudioClip clip)
        {
            var source = EnsureSource();
            if (source.isPlaying)
            {
                var start = source.volume;
                for (var t = 0f; t < FadeSeconds && source.isPlaying; t += Time.deltaTime)
                {
                    source.volume = Mathf.Lerp(start, 0f, t / FadeSeconds);
                    yield return null;
                }
                source.Stop();
            }
            yield return new WaitForSeconds(m_Delay);
            source.Stop();
            source.clip = clip;
            source.volume = m_Volume;
            source.Play();
            while (source.isPlaying) yield return null;
            if (m_PendingClip == clip) { m_Said.Add(m_Pending); m_Pending = null; m_PendingClip = null; }
            if(m_Lines.Count>0)yield return DrainLines();
            m_Routine = null;
        }

        IEnumerator FadeOut()
        {
            var start = m_Source.volume;
            for (var t = 0f; t < FadeSeconds && m_Source.isPlaying; t += Time.deltaTime)
            {
                m_Source.volume = Mathf.Lerp(start, 0f, t / FadeSeconds);
                yield return null;
            }
            m_Source.Stop();
            if(m_Lines.Count>0)yield return DrainLines();
            m_Routine = null;
        }

        void StopRoutine()
        {
            if (m_Routine != null) StopCoroutine(m_Routine);
            m_Routine = null;
        }

        AudioSource EnsureSource()
        {
            if (m_Source != null) return m_Source;
            m_Source = gameObject.AddComponent<AudioSource>();
            m_Source.playOnAwake = false;
            m_Source.loop = false;
            m_Source.spatialBlend = 0f;
            m_Source.priority = 0;
            return m_Source;
        }

        /// <summary>
        /// The pending intro is being cut off: it counts as said only if most of it was heard; otherwise the world will
        /// welcome the user again next time its layer opens.
        /// </summary>
        void SettlePending()
        {
            if (m_Pending == null) return;
            var heard = m_Source != null && m_Source.clip == m_PendingClip && m_PendingClip != null &&
                        CountsAsHeard(m_Source.time, m_PendingClip.length);
            if (heard) m_Said.Add(m_Pending);
            Debug.Log($"[Bubble World] Intro {m_Pending} cut off" + (heard ? " after most of it: counts as said" : ": will play again next time"));
            m_Pending = null; m_PendingClip = null;
        }

        void OnDisable()
        {
            SettlePending();
            m_Lines.Clear();
            StopRoutine();
            if (m_Source != null) m_Source.Stop();
        }
    }
}
