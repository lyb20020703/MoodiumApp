using System.Collections.Generic;
using UnityEngine;

namespace Moodium.Characters
{
    /// <summary>
    /// Moodi's voice (MiniMax speech-2.8-hd, voice "Cute Spirit", Chinese and English).
    /// Clips: Resources/MoodiumVoice/{zh|en}/moodi_vo_{id}_{zh|en}.
    ///
    /// Rules:
    /// - The language follows PlayerPrefs "Moodium.Language" ("English" → en, anything else → zh).
    /// - Teaching lines (A2, A3, C1, C6, D3, E2, …) play at most once per session, and never after
    ///   MarkLearned(id). If Moodi is talking, a teaching line waits and plays 1 s after it ends.
    ///   (2026-11-03) A teaching line counts as taught only once it actually starts; a waiting one gives up after
    ///   ReservedTimeout s, and MarkLearned also cancels a waiting one, so a tip never plays after the user already
    ///   did it (e.g. C6 "turn your hand over" after Moodi has flown off) and a dropped tip is not lost for the session.
    /// - Reaction lines never interrupt: if Moodi is talking they are skipped.
    /// - Some lines have their own cooldown (C5 20 s, D5 15 s, E1 60 s).
    /// - Variant groups (B1, C2, D4) pick a random line and never repeat the previous pick.
    /// The voice comes from Moodi's own position (3D AudioSource).
    /// S1c / S1n / S2 are said by the Moodi on a sticker's world entry (CandyWorldUI / NatureWorldUI).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MoodiVoice : MonoBehaviour
    {
        public const string ResourceFolder = "MoodiumVoice";

        /// <summary>Every line id, in script order. Each has a zh and an en clip.</summary>
        public static readonly string[] AllIds =
        {
            "A1", "A2", "A3",
            "B1a", "B1b", "B2",
            "C1", "C2a", "C2b", "C2c", "C3", "C4", "C5", "C6",
            "D1", "D2", "D3", "D4a", "D4b", "D5", "D6n", "D6c", "D7",
            "E1", "E2",
            "F2", "F3", "F4", "F5", "F6", "F7",
            "S1c", "S1n", "S2",
            "K1", "K2", "K3", "K4", "K5", "K6", "K7", "K8a", "K8b", "K8c", "K9", "K10",
            "V1", "V2", "V3"
        };

        static readonly HashSet<string> TeachingIds = new() { "A2", "A3", "C1", "C6", "D3", "E2", "K3", "F2", "F4", "V1", "V2" };
        static readonly HashSet<string> OptionalIds = new() { "K4", "K5", "K6", "K7", "K8a", "K8b", "K8c", "K9", "B1a", "B1b", "C2a", "C2b", "C2c", "C3", "C4", "C5", "D1", "D2", "D4a", "D4b", "D5", "D6n", "D6c", "E1", "V3" };
        static readonly Queue<QueuedLine> s_Queue = new();
        static MoodiVoice s_Current;
        static AudioSource s_Emitter;
        static float s_BusyUntil = -10f;
        const float GlobalGap = .35f;
        const float QueueTimeout = 10f;
        /// <summary>A waiting teaching line is dropped after this long (it was never dropped before).</summary>
        public const float ReservedTimeout = 20f;
        const int QueueLimit = 3;
        struct QueuedLine { public MoodiVoice Voice; public string Id; public float Expires; public bool Reserved; }

        static readonly Dictionary<string, float> Cooldowns = new() { { "C5", 20f }, { "D5", 15f }, { "E1", 60f }, { "K9", 30f }, { "F5", 20f } };

        static readonly Dictionary<string, string[]> Variants = new()
        {
            { "B1", new[] { "B1a", "B1b" } },
            { "C2", new[] { "C2a", "C2b", "C2c" } },
            { "D4", new[] { "D4a", "D4b" } },
            { "K8", new[] { "K8a", "K8b", "K8c" } }
        };

        static readonly HashSet<string> s_Taught = new();

        [SerializeField] AudioSource m_Source;
        [SerializeField, Range(0f, 1f)] float m_Volume = .9f;
        [Tooltip("Seconds to wait after Moodi stops talking before a queued teaching line starts.")]
        [SerializeField, Range(0f, 3f)] float m_TeachingGap = 1f;

        readonly Dictionary<string, AudioClip> m_Clips = new();
        readonly Dictionary<string, float> m_NextAllowed = new();
        readonly Dictionary<string, string> m_LastVariant = new();
        string m_Current;
        AudioLowPassFilter m_LowPass;

        /// <summary>Low-pass cutoff for a muffle amount: 0 = clear (22 kHz), 1 = deep inside the jelly (1.4 kHz).</summary>
        public static float MuffleCutoff(float amount) => 22000f * Mathf.Pow(1400f / 22000f, Mathf.Clamp01(amount));

        /// <summary>Make Moodi sound like it is inside the squeeze bag (K lines). 0 turns the filter off.</summary>
        public void Muffle(float amount)
        {
            EnsureEmitter();
            if (m_LowPass == null)
            {
                m_LowPass=s_Emitter.GetComponent<AudioLowPassFilter>();
                if(m_LowPass==null)m_LowPass=s_Emitter.gameObject.AddComponent<AudioLowPassFilter>();
            }
            m_LowPass.enabled = amount > .001f;
            m_LowPass.cutoffFrequency = MuffleCutoff(amount);
            m_LowPass.lowpassResonanceQ = 1f;
        }

        public static string LanguageCode =>
            PlayerPrefs.GetString("Moodium.Language", "Chinese") == "English" ? "en" : "zh";

        /// <summary>Set to force a language for this Moodi (e.g. the opening session's choice); null follows PlayerPrefs.</summary>
        public bool? EnglishOverride { get; set; }

        string Language => EnglishOverride.HasValue ? (EnglishOverride.Value ? "en" : "zh") : LanguageCode;

        public static bool IsTeaching(string id) => TeachingIds.Contains(id);

        public static string ClipResourcePath(string id, string language) =>
            $"{ResourceFolder}/{language}/moodi_vo_{id}_{language}";

        /// <summary>Forget which teaching lines were already said (call when a new world session starts).</summary>
        public static void ResetSession() => s_Taught.Clear();

        /// <summary>The user already did what this teaching line explains, so never say it this session (a waiting one is cancelled).</summary>
        public static void MarkLearned(string id)
        {
            s_Taught.Add(id);
            RemoveQueuedId(id);
        }

        /// <summary>True while a line with this id waits in the shared queue.</summary>
        public static bool IsQueued(string id) { foreach (var q in s_Queue) if (q.Id == id) return true; return false; }

        /// <summary>
        /// Turn the "inside the bag" muffle off on the shared emitter. Leaving a world before the rescue used to keep every
        /// later line (the sticker Moodi's too) sounding as if through a wall.
        /// </summary>
        public static void ClearMuffle()
        {
            if (s_Emitter == null) return;
            var lowPass = s_Emitter.GetComponent<AudioLowPassFilter>();
            if (lowPass != null) lowPass.enabled = false;
        }

        /// <summary>Whether the shared emitter is muffled right now (tests and logs).</summary>
        public static bool IsMuffled
        {
            get
            {
                if (s_Emitter == null) return false;
                var lowPass = s_Emitter.GetComponent<AudioLowPassFilter>();
                return lowPass != null && lowPass.enabled;
            }
        }

        public bool IsSpeaking => s_Current == this && s_Emitter != null && s_Emitter.isPlaying;
        public static bool IsAnyoneSpeaking => s_Emitter != null && s_Emitter.isPlaying;
        public bool HasPendingSpeech => HasQueued(this);
        public string CurrentLine => IsSpeaking ? m_Current : null;

        public bool TryGetOutputLevel(float[] samples, out float level)
        {
            level=0f;
            if(!IsSpeaking || samples==null || samples.Length==0)return false;
            s_Emitter.GetOutputData(samples,0);
            foreach(var sample in samples)level+=sample*sample;
            level=Mathf.Sqrt(level/samples.Length);
            return level>0.00001f;
        }

        void Awake() => EnsureEmitter();

        // A hidden Moodi (sticker lost, mood check open) must not speak a stale line when it comes back.
        void OnDisable()
        {
            if (s_Current == this) Stop();
            RemoveQueued(this);
        }

        void Update()
        {
            if (s_Current == this && s_Emitter != null && !s_Emitter.isPlaying) { s_Current = null; m_Current = null; }
            if (IsAnyoneSpeaking || Time.unscaledTime < s_BusyUntil) return;
            while (s_Queue.Count > 0)
            {
                var q = s_Queue.Dequeue();
                if (q.Voice == null || !q.Voice.isActiveAndEnabled || Time.unscaledTime > q.Expires) continue;
                if (q.Voice.Play(q.Id)) break;
            }
        }

        /// <summary>Say one line by id. Returns true if it started (or was queued, for a teaching line).</summary>
        public bool Say(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            if (Variants.ContainsKey(id)) return SayOneOf(id);
            if (TeachingIds.Contains(id))
            {
                // Taught once it really starts (Play): a full queue or a hidden Moodi no longer loses the tip for the session.
                if (s_Taught.Contains(id) || IsQueued(id)) return false;
                return Request(id, true);
            }
            if (OptionalIds.Contains(id) && IsAnyoneSpeaking) return false;
            if (m_NextAllowed.TryGetValue(id, out var next) && Time.time < next) return false;
            return Request(id, false);
        }

        /// <summary>Say a random line from a variant group ("B1", "C2" or "D4"), never the same one twice in a row.</summary>
        public bool SayOneOf(string group)
        {
            if (!Variants.TryGetValue(group, out var ids)) return Say(group);
            if (IsAnyoneSpeaking) return false;
            m_LastVariant.TryGetValue(group, out var last);
            var pick = ids[Random.Range(0, ids.Length)];
            if (ids.Length > 1 && pick == last) pick = ids[(System.Array.IndexOf(ids, pick) + 1) % ids.Length];
            m_LastVariant[group] = pick;
            return Request(pick, false);
        }

        /// <summary>Say a line after a short delay (e.g. once a model has appeared). A newer request replaces an older one.</summary>
        public void SayAfter(string id, float delay)
        {
            StartCoroutine(Delayed(id, delay));
        }

        /// <summary>D6 for the world you are in.</summary>
        public bool SayGardenFound(bool candy) => Say(candy ? "D6c" : "D6n");

        public void Stop()
        {
            RemoveQueued(this);
            if (s_Current == this && s_Emitter != null) { s_Emitter.Stop(); s_Current = null; s_BusyUntil = Time.unscaledTime + GlobalGap; m_Current = null; }
        }

        System.Collections.IEnumerator Delayed(string id, float delay) { yield return new WaitForSecondsRealtime(Mathf.Max(0f, delay)); Say(id); }
        static bool HasQueued(MoodiVoice v) { foreach (var q in s_Queue) if (q.Voice == v) return true; return false; }
        static void RemoveQueuedId(string id) { var keep = new Queue<QueuedLine>(); while (s_Queue.Count > 0) { var q=s_Queue.Dequeue(); if(q.Id!=id) keep.Enqueue(q); } while(keep.Count>0)s_Queue.Enqueue(keep.Dequeue()); }
        static void RemoveQueued(MoodiVoice v) { var keep = new Queue<QueuedLine>(); while (s_Queue.Count > 0) { var q=s_Queue.Dequeue(); if(q.Voice!=v) keep.Enqueue(q); } while(keep.Count>0)s_Queue.Enqueue(keep.Dequeue()); }
        bool Request(string id, bool reserved)
        {
            if (!reserved && IsAnyoneSpeaking) return false;
            if (!reserved && s_Queue.Count >= QueueLimit) return false;
            if (IsAnyoneSpeaking || Time.unscaledTime < s_BusyUntil) { if (s_Queue.Count >= QueueLimit) return false; s_Queue.Enqueue(new QueuedLine { Voice=this, Id=id, Expires=Time.unscaledTime+(reserved ? ReservedTimeout : QueueTimeout), Reserved=reserved }); return true; }
            return Play(id);
        }

        bool Play(string id)
        {
            var clip = Load(id);
            if (clip == null) return false;
            EnsureSource();
            EnsureEmitter();
            if (IsAnyoneSpeaking || Time.unscaledTime < s_BusyUntil) return Request(id, TeachingIds.Contains(id));
            s_Emitter.clip = clip; s_Emitter.volume = m_Volume; s_Emitter.Play(); s_Current = this;
            m_Current = id;
            if (TeachingIds.Contains(id)) s_Taught.Add(id);
            if (Cooldowns.TryGetValue(id, out var cooldown)) m_NextAllowed[id] = Time.time + cooldown;
            s_BusyUntil = Time.unscaledTime + clip.length + GlobalGap;
            return true;
        }

        AudioClip Load(string id)
        {
            var path = ClipResourcePath(id, Language);
            if (m_Clips.TryGetValue(path, out var cached)) return cached;
            var clip = Resources.Load<AudioClip>(path);
            if (clip == null) Debug.LogWarning($"[Moodi Voice] Missing clip {path}");
            m_Clips[path] = clip;
            return clip;
        }

        void EnsureSource()
        {
            EnsureEmitter();
        }

        void EnsureEmitter()
        {
            if (s_Emitter != null) return;
            var go = GameObject.Find("Moodi Voice Emitter") ?? new GameObject("Moodi Voice Emitter");
            s_Emitter = go.GetComponent<AudioSource>();
            // Unity's missing native components can be non-null CLR wrappers.
            if(s_Emitter==null)s_Emitter=go.AddComponent<AudioSource>();
            s_Emitter.playOnAwake = false; s_Emitter.loop = false; s_Emitter.spatialBlend = 1f; s_Emitter.rolloffMode = AudioRolloffMode.Logarithmic; s_Emitter.minDistance=.4f; s_Emitter.maxDistance=6f; s_Emitter.dopplerLevel=0f;
        }
        void LateUpdate() { if (s_Current == this && s_Emitter != null) s_Emitter.transform.position = transform.position; }
    }
}
