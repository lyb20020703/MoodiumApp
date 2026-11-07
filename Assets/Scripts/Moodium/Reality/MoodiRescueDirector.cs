using System;
using System.Collections;
using System.Collections.Generic;
using Moodium.CandyWorld;
using Moodium.Characters;
using UnityEngine;

namespace Moodium.Reality
{
    /// <summary>
    /// The squeeze "rescue" story around the transparent progress video.
    ///
    /// 1. When the world starts and the soft model appears, Moodi glides in above the jelly dome,
    ///    slips (K1) and falls in: the dome splashes flat and springs back, Moodi sinks out of sight.
    /// 2. From inside it calls for help, muffled (K2), and asks to be squeezed out (K3, teaching).
    ///    IntroFinished is raised after that, so the world tutorial card can follow.
    /// 3. While the user squeezes: first press K4, energy stages 20/50/80 → K5/K6/K7, a random cheer
    ///    (K8) during steady pressing, and a nudge (K9) after 12 s without presses. The voice gets
    ///    clearer as progress rises (MoodiVoice.Muffle).
    /// 4. When the finished video pops, Moodi jumps out, spins (Happy), stops in front of the user and
    ///    thanks them (K10). Rescued(position) is raised so the walk guide can take over from there.
    ///
    /// The Moodi used here is the same prefab as the walk guide ("Moodi meshy"), at the same 4 cm size.
    /// The director never changes energy, progress or the video timeline.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MoodiRescueDirector : MonoBehaviour
    {
        [Header("Moodi")]
        [SerializeField, Range(.02f, .1f)] float m_MoodiSize = .04f;
        [Tooltip("Where Moodi first appears, above the dome (metres).")]
        [SerializeField, Range(.1f, .6f)] float m_DropHeight = .30f;
        [SerializeField, Range(1f, 15f)] float m_WaitForDomeSeconds = 8f;

        [Header("Encouragement")]
        [SerializeField, Range(3f, 30f)] float m_CheerGap = 10f;
        [SerializeField, Range(2, 12)] int m_CheerPresses = 5;
        [SerializeField, Range(2f, 15f)] float m_CheerWindow = 6f;
        [SerializeField, Range(5f, 60f)] float m_IdleNudgeAfter = 12f;
        [SerializeField, Range(10f, 120f)] float m_IdleNudgeGap = 30f;

        [Header("Jump out")]
        [SerializeField, Range(.05f, .5f)] float m_HoverHeight = .22f;
        [SerializeField, Range(0f, .3f)] float m_HoverTowardViewer = .10f;
        [Tooltip("Seconds between arriving in front of the user and handing Moodi to the walk guide.")]
        [SerializeField, Range(0f, 3f)] float m_HandOverDelay = .9f;

        /// <summary>Moodi has finished asking to be squeezed out (or the intro could not run).</summary>
        public event Action IntroFinished;
        /// <summary>Moodi jumped out and stopped at this position; the walk guide should take over there.</summary>
        public event Action<Vector3> Rescued;

        public bool IntroDone { get; private set; }
        public bool IsRescued { get; private set; }

        ISqueezeProgressDisplay m_Video;
        Transform m_Viewer;
        GameObject m_Prefab;
        Transform m_Root;
        GameObject m_Visual;
        MoodiCompanionAnimator m_Animator;
        MoodiVoice m_Voice;
        Vector3 m_VisualScale = Vector3.one;
        bool m_Inside;
        float m_Progress;
        bool m_FirstPressSaid;
        float m_LastPinch = -100f;
        float m_LastCheer = -100f;
        float m_LastNudge = -100f;
        readonly Queue<float> m_RecentPinches = new();
        Coroutine m_Routine;
        MoodiRiseDisplay MeshDisplay => (m_Video as SqueezeProgressDisplayRouter)?.Mesh;

        // ---------- pure rules (tested) ----------

        /// <summary>1 = fully muffled (deep inside), 0 = clear. Clear from 85% progress.</summary>
        public static float MuffleForProgress(float progress)
        {
            var x = Mathf.Clamp01(progress / .85f);
            x = x * x * (3f - 2f * x);
            return 1f - x;
        }

        public static bool ShouldCheer(int pressesInWindow, int pressesNeeded, float secondsSinceLastCheer, float gap, float progress) =>
            pressesInWindow >= pressesNeeded && secondsSinceLastCheer >= gap && progress > 0f && progress < .8f;

        public static bool ShouldNudge(bool introDone, float progress, float secondsSincePinch, float after,
            float secondsSinceNudge, float gap) =>
            introDone && progress > 0f && progress < 1f && secondsSincePinch >= after && secondsSinceNudge >= gap;

        /// <summary>Stage line for the CandyEnergyController stages (20/50/80); null for others.</summary>
        public static string StageLine(int stage) => stage switch { 20 => "K5", 50 => "K6", 80 => "K7", _ => null };

        public static Vector3 ArcPoint(Vector3 from, Vector3 to, Vector3 up, float t, float height)
        {
            t = Mathf.Clamp01(t);
            return Vector3.Lerp(from, to, t) + up * (Mathf.Sin(t * Mathf.PI) * height);
        }

        // ---------- API ----------

        public void Begin(GameObject moodiPrefab, ISqueezeProgressDisplay video, Transform viewer)
        {
            StopAndClear();
            m_Prefab = moodiPrefab;
            m_Video = video;
            m_Viewer = viewer;
            IntroDone = IsRescued = false;
            m_Progress = 0f;
            m_FirstPressSaid = false;
            m_LastPinch = m_LastCheer = m_LastNudge = -100f;
            m_RecentPinches.Clear();
            if (m_Video != null) m_Video.Popped += OnPopped;
            if (m_Prefab == null || m_Video == null)
            {
                FinishIntro();
                return;
            }
            m_Routine = StartCoroutine(Intro());
        }

        public void NotifyPinch()
        {
            if (IsRescued) return;
            m_LastPinch = Time.time;
            m_RecentPinches.Enqueue(Time.time);
            while (m_RecentPinches.Count > 0 && Time.time - m_RecentPinches.Peek() > m_CheerWindow)
                m_RecentPinches.Dequeue();
            if (m_Voice == null || !IntroDone) return;
            if (!m_FirstPressSaid)
            {
                m_FirstPressSaid = true;
                m_Voice.Say("K4");
                m_LastCheer = Time.time;
                return;
            }
            if (ShouldCheer(m_RecentPinches.Count, m_CheerPresses, Time.time - m_LastCheer, m_CheerGap, m_Progress) &&
                m_Voice.SayOneOf("K8"))
            {
                m_LastCheer = Time.time;
                m_RecentPinches.Clear();
            }
        }

        public void NotifyStage(int stage)
        {
            MeshDisplay?.NotifyStage(stage);
            if (IsRescued) return;
            var line = StageLine(stage);
            if (line != null && m_Voice != null)
            {
                // Stage lines matter more than a running cheer: say them after the current line.
                if (m_Voice.IsSpeaking) m_Voice.SayAfter(line, .4f); else m_Voice.Say(line);
                m_LastCheer = Time.time;
            }
            if (stage >= 100) StartCoroutine(FallbackRescue());
        }

        public void NotifyProgress(float normalizedProgress)
        {
            m_Progress = Mathf.Clamp01(normalizedProgress);
            if (!IsRescued && m_Inside && m_Voice != null) m_Voice.Muffle(MuffleForProgress(m_Progress));
        }

        public void StopAndClear()
        {
            MeshDisplay?.DetachMoodi();
            if (m_Video != null) m_Video.Popped -= OnPopped;
            StopAllCoroutines();
            m_Routine = null;
            if (m_Root != null) Destroy(m_Root.gameObject);
            m_Root = null;
            m_Visual = null;
            m_Animator = null;
            m_Voice = null;
            m_Inside = false;
            // Leaving before the rescue (Exit at < 100 %) used to keep the shared voice emitter muffled for every later line.
            MoodiVoice.ClearMuffle();
        }

        void OnDisable() => StopAndClear();

        // ---------- frame ----------

        void Update()
        {
            if (m_Root == null) return;
            // Inside the bag the (hidden) Moodi rides the tracked dome, so its voice comes from there.
            if (m_Inside && m_Video != null && m_Video.TryGetDomeCenter(out var center, out _))
                m_Root.position = center;
            if (m_Voice != null && ShouldNudge(IntroDone && !IsRescued, m_Progress, Time.time - m_LastPinch, m_IdleNudgeAfter,
                    Time.time - m_LastNudge, m_IdleNudgeGap) && !m_Voice.IsSpeaking && m_Voice.Say("K9"))
                m_LastNudge = Time.time;
        }

        // ---------- story ----------

        IEnumerator Intro()
        {
            var center = Vector3.zero;
            var up = Vector3.up;
            var deadline = Time.time + m_WaitForDomeSeconds;
            while (!(m_Video != null && m_Video.TryGetDomeCenter(out center, out up)))
            {
                if (Time.time > deadline)
                {
                    Debug.LogWarning("[Moodi Rescue] The squeeze video never bound to the soft model; skipping the intro.");
                    FinishIntro();
                    yield break;
                }
                yield return null;
            }

            CreateMoodi(center);
            var toViewer = FlatTowardViewer(center, up);
            var side = Vector3.Cross(up, toViewer).normalized;
            var above = center + up * m_DropHeight;

            // Glide in from the side: the horizontal move makes MoodiCompanionAnimator turn to the viewer
            // once it stops, and the wave plays on first appearance.
            var from = above + side * .12f;
            for (var t = 0f; t < .6f; t += Time.deltaTime)
            {
                var k = Mathf.Clamp01(t / .6f);
                m_Root.position = Vector3.Lerp(from, above, 1f - (1f - k) * (1f - k));
                SetVisualScale(Mathf.Clamp01(t / .25f));
                yield return null;
            }
            m_Root.position = above;
            SetVisualScale(1f);
            yield return new WaitForSeconds(.9f);

            // Slip and fall in.
            m_Voice.Say("K1");
            if (m_Animator != null) m_Animator.PlayTouched();
            for (var t = 0f; t < .4f; t += Time.deltaTime)
            {
                var k = Mathf.Clamp01(t / .4f);
                if (m_Video.TryGetDomeCenter(out var c, out var u)) { center = c; up = u; }
                m_Root.position = Vector3.LerpUnclamped(center + up * m_DropHeight, center, k * k);
                yield return null;
            }
            m_Video.Splash(1.6f);
            CandyWorldManager.SpawnSoftSqueezeFeedback(center);
            if(MeshDisplay!=null && MeshDisplay.Ready)
            {
                SetVisualScale(1);SetVisualVisible(true);
                MeshDisplay.AttachMoodi(m_Visual.GetComponentInChildren<Animator>(true));
                yield return new WaitForSeconds(.5f);
                MeshDisplay.SetPeeking(true);
            }
            else
            {
            for (var t = 0f; t < .25f; t += Time.deltaTime)
            {
                var k = Mathf.Clamp01(t / .25f);
                m_Root.position = center - up * (.01f * k);
                SetVisualScale(1f - k);
                yield return null;
            }
            SetVisualVisible(false);
            }
            m_Inside = true;
            m_Voice.Muffle(MuffleForProgress(m_Progress));

            // Muffled call for help, then the request (teaching, once per session).
            yield return new WaitForSeconds(.7f);
            m_Voice.Say("K2");
            yield return WaitUntilQuiet(.3f);
            m_Voice.Say("K3");
            yield return WaitUntilQuiet(.6f);
            FinishIntro();
            if(MeshDisplay!=null && MeshDisplay.Attached)MeshDisplay.SetPeeking(false);
            m_Routine = null;
        }

        void OnPopped(Vector3 center)
        {
            if (IsRescued || m_Prefab == null) return;
            if (m_Routine != null) StopCoroutine(m_Routine);
            m_Routine = StartCoroutine(JumpOut(center));
        }

        IEnumerator FallbackRescue()
        {
            // If the video is missing or never popped, still let Moodi out shortly after 100%.
            yield return new WaitForSeconds(1.2f);
            // The mesh display must finish its paced emergence before the fallback can interrupt it.
            var deadline=Time.time+3f;
            while(MeshDisplay!=null && MeshDisplay.Ready && !IsRescued && m_Progress>=1f && Time.time<deadline)
                yield return null;
            if (IsRescued) yield break;
            if (m_Video != null && m_Video.TryGetDomeCenter(out var center, out _))
                OnPopped(center);
            else if (m_Root != null)
                OnPopped(m_Root.position);
        }

        IEnumerator JumpOut(Vector3 center)
        {
            var fromMesh=MeshDisplay!=null && MeshDisplay.Attached;
            if(fromMesh)MeshDisplay.DetachMoodi();
            IsRescued = true;
            if (!IntroDone) FinishIntro();
            if (m_Root == null) CreateMoodi(center);
            m_Inside = false;
            m_Voice.Stop();
            m_Voice.Muffle(0f);
            var up = Vector3.up;
            if (m_Video != null) m_Video.TryGetDomeCenter(out _, out up);
            var hover = center + up * m_HoverHeight + FlatTowardViewer(center, up) * m_HoverTowardViewer;

            m_Root.position = center;
            SetVisualVisible(true);
            if (m_Animator != null) m_Animator.PlayHappy();
            for (var t = 0f; t < .6f; t += Time.deltaTime)
            {
                var k = Mathf.Clamp01(t / .6f);
                m_Root.position = ArcPoint(center, hover, up, 1f - (1f - k) * (1f - k), .08f);
                if(!fromMesh)SetVisualScale(Mathf.Clamp01(t / .15f));
                yield return null;
            }
            m_Root.position = hover;
            SetVisualScale(1f);
            if(fromMesh && m_Visual!=null)MoodiLandingBlink.Play(m_Visual);
            m_Voice.Say("K10");
            yield return new WaitForSeconds(m_HandOverDelay);

            // The walk guide shows its own Moodi here; ours disappears but keeps talking until K10 ends.
            Rescued?.Invoke(hover);
            SetVisualVisible(false);
            while (m_Voice != null && (m_Voice.IsSpeaking || m_Voice.HasPendingSpeech)) yield return null;
            if (m_Root != null) Destroy(m_Root.gameObject);
            m_Root = null;
            m_Visual = null;
            m_Animator = null;
            m_Voice = null;
            m_Routine = null;
        }

        // ---------- helpers ----------

        void FinishIntro()
        {
            if (IntroDone) return;
            IntroDone = true;
            IntroFinished?.Invoke();
        }

        IEnumerator WaitUntilQuiet(float extraSeconds)
        {
            yield return null;
            while (m_Voice != null && (m_Voice.IsSpeaking || m_Voice.HasPendingSpeech)) yield return null;
            if (extraSeconds > 0f) yield return new WaitForSeconds(extraSeconds);
        }

        Vector3 FlatTowardViewer(Vector3 from, Vector3 up)
        {
            var viewer = m_Viewer != null ? m_Viewer : Camera.main != null ? Camera.main.transform : null;
            var toViewer = viewer != null ? Vector3.ProjectOnPlane(viewer.position - from, up) : Vector3.zero;
            return toViewer.sqrMagnitude > 1e-4f ? toViewer.normalized : Vector3.ProjectOnPlane(Vector3.back, up).normalized;
        }

        void CreateMoodi(Vector3 position)
        {
            if (m_Root != null) Destroy(m_Root.gameObject);
            m_Root = new GameObject("Moodi Rescue").transform;
            m_Root.position = position;
            m_Voice = m_Root.gameObject.AddComponent<MoodiVoice>();
            if (m_Prefab == null) return;
            m_Visual = Instantiate(m_Prefab, m_Root);
            m_Visual.name = "Moodi Rescue Visual";
            foreach (var collider in m_Visual.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (var body in m_Visual.GetComponentsInChildren<Rigidbody>(true)) body.isKinematic = true;
            // Same sizing as the walk guide (WalkTrailGuideController.PrepareGuide): largest side = m_MoodiSize.
            var renderers = m_Visual.GetComponentsInChildren<Renderer>();
            var bounds = new Bounds(m_Visual.transform.position, Vector3.zero);
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            var size = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            // The v2 rig's skinned-mesh bounds are not reliable before the first skinning pass; when the eye
            // bones exist, measure Moodi from them instead (rest pose: eye span 0.678, largest side 1.904).
            Transform eyeL = null, eyeR = null;
            foreach (var t in m_Visual.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Eye_L") eyeL = t;
                else if (t.name == "Eye_R") eyeR = t;
            }
            if (eyeL != null && eyeR != null)
            {
                var span = Vector3.Distance(eyeL.position, eyeR.position);
                if (span > 1e-6f)
                {
                    Debug.Log($"[Moodi Rescue] size from bounds {size * 100f:F1} cm, from eyes {span / .678f * 1.904f * 100f:F1} cm");
                    size = span / .678f * 1.904f;
                }
            }
            if (size > .0001f) m_Visual.transform.localScale *= m_MoodiSize / size;
            m_VisualScale = m_Visual.transform.localScale;
            m_Animator = m_Visual.GetComponent<MoodiCompanionAnimator>();
            SetVisualScale(0f);
        }

        void SetVisualScale(float k)
        {
            if (m_Visual != null) m_Visual.transform.localScale = m_VisualScale * Mathf.Max(.0001f, k);
        }

        void SetVisualVisible(bool visible)
        {
            if (m_Visual == null) return;
            foreach (var renderer in m_Visual.GetComponentsInChildren<Renderer>(true)) renderer.enabled = visible;
        }
    }
}
