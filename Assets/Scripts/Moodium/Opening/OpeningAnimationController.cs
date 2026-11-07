using System.Collections;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Moodium.Opening
{
    public sealed class OpeningAnimationController : MonoBehaviour
    {
        [SerializeField] Animator m_CandyAnimator;
        [SerializeField] GameObject m_CandyRoot;
        [SerializeField] GameObject m_SpriteRoot;
        [SerializeField] GameObject m_LegacyLogoRoot;
        [SerializeField] OpeningLogoTextView m_LogoTextView;
        [SerializeField] AnimationClip m_CandyTransformClip;
        [SerializeField, Min(0.01f)] float m_CandyTransformDuration = 2.4f;

        Vector3 m_IdlePosition;
        Quaternion m_IdleRotation;
        PlayableGraph m_CandyGraph;
        bool m_CandyIdle;

        public float CandyTransformDuration => m_CandyTransformDuration;

        void Awake()
        {
            m_CandyAnimator = ResolveAnimator(m_CandyAnimator, m_CandyRoot);
            m_CandyTransformClip ??= Resources.Load<AnimationClip>("MoodiumOpening/Candy_Transform");
            if (m_CandyRoot != null)
            {
                m_IdlePosition = m_CandyRoot.transform.localPosition;
                m_IdleRotation = m_CandyRoot.transform.localRotation;
            }
            ResetOpening();
        }

        public void ResetOpening()
        {
            SetActive(m_CandyRoot, true);
            // Retain these imported assets for future expansion, but keep them out
            // of the current Candy -> text-logo experience.
            SetActive(m_SpriteRoot, false);
            SetActive(m_LegacyLogoRoot, false);
            m_LogoTextView?.SetVisibleImmediate(false);
            StopGraph(ref m_CandyGraph);
            m_CandyIdle = true;
        }

        /// <summary>Hides every opening visual while the pre-roll video owns the view.</summary>
        public void HideOpeningVisuals()
        {
            SetActive(m_CandyRoot, false);
            SetActive(m_SpriteRoot, false);
            SetActive(m_LegacyLogoRoot, false);
            m_LogoTextView?.SetVisibleImmediate(false);
            StopGraph(ref m_CandyGraph);
            m_CandyIdle = false;
        }

        public void PlayCandyTransform()
        {
            m_CandyIdle = false;
            m_CandyTransformClip = ResolveClip(m_CandyTransformClip, m_CandyAnimator, "Candy_Transform");
            PlayClip(ref m_CandyGraph, m_CandyAnimator, m_CandyTransformClip, "Candy_Transform");
        }

        public IEnumerator FadeInLogoText(float duration)
        {
            if (m_LogoTextView == null)
            {
                Debug.LogError("[Moodium Opening] MoodiumLogoText view is missing.");
                yield break;
            }
            yield return m_LogoTextView.FadeIn(duration);
        }

        public IEnumerator HideAll(float delay)
        {
            if (delay > 0f)
                yield return new WaitForSeconds(delay);
            SetActive(m_CandyRoot, false);
            SetActive(m_SpriteRoot, false);
            SetActive(m_LegacyLogoRoot, false);
            m_LogoTextView?.SetVisibleImmediate(false);
        }

        void LateUpdate()
        {
            if (m_CandyRoot == null || !m_CandyRoot.activeSelf || !m_CandyIdle)
                return;
            var t = Time.unscaledTime;
            m_CandyRoot.transform.localPosition = m_IdlePosition + Vector3.up * (Mathf.Sin(t * 1.25f) * 0.018f);
            m_CandyRoot.transform.localRotation = m_IdleRotation * Quaternion.Euler(0f, Mathf.Sin(t * 0.42f) * 7f, 0f);
        }

        void OnDestroy() => StopGraph(ref m_CandyGraph);

        static void PlayClip(ref PlayableGraph graph, Animator animator, AnimationClip clip, string label)
        {
            StopGraph(ref graph);
            if (animator == null || clip == null)
            {
                Debug.LogError($"[Moodium Opening] Cannot play {label}: Animator={(animator == null ? "MISSING" : animator.name)}, Clip={(clip == null ? "MISSING" : clip.name)}.");
                return;
            }
            animator.enabled = true;
            graph = PlayableGraph.Create($"Moodium_{label}");
            graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            var playable = AnimationClipPlayable.Create(graph, clip);
            playable.SetApplyFootIK(false);
            var output = AnimationPlayableOutput.Create(graph, label, animator);
            output.SetSourcePlayable(playable);
            graph.Play();
            Debug.Log($"[Moodium Opening] Playing clip {clip.name} ({clip.length:0.###}s) on {animator.name} via Playables.");
        }

        static void StopGraph(ref PlayableGraph graph)
        {
            if (graph.IsValid()) graph.Destroy();
        }

        static Animator ResolveAnimator(Animator current, GameObject root)
        {
            if (current != null || root == null) return current;
            var found = root.GetComponentInChildren<Animator>(true);
            if (found != null) return found;
            var target = root.transform.childCount > 0 ? root.transform.GetChild(0).gameObject : root;
            return target.AddComponent<Animator>();
        }

        static AnimationClip ResolveClip(AnimationClip current, Animator animator, string clipName)
        {
            if (current != null) return current;
            var loaded = Resources.Load<AnimationClip>($"MoodiumOpening/{clipName}");
            if (loaded != null) return loaded;
            var controller = animator != null ? animator.runtimeAnimatorController : null;
            if (controller != null)
                foreach (var candidate in controller.animationClips)
                    if (candidate != null && candidate.name == clipName) return candidate;
            Debug.LogError($"[Moodium Opening] AnimationClip '{clipName}' could not be resolved.");
            return null;
        }

        static void SetActive(GameObject target, bool active)
        {
            if (target != null) target.SetActive(active);
        }
    }
}
