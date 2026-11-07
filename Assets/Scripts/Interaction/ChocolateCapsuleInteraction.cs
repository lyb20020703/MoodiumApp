using System;
using System.Collections;
using UnityEngine;
using Moodium.Audio;

namespace Moodium.Interaction
{
    /// <summary>Whole-capsule pinch interaction for the tracked tissue experience.</summary>
    public sealed class ChocolateCapsuleInteraction : MonoBehaviour
    {
        public static event Action<ChocolateCapsuleInteraction, Vector3> AnyCapsulePinched;
        public static event Action<ChocolateCapsuleInteraction, Vector3> AnyCapsuleHeld;
        public static event Action<ChocolateCapsuleInteraction> AnyCapsuleReleased;
        public static event Action<ChocolateCapsuleInteraction> AnyCapsuleTrackingLost;
        public void NotifyTrackingLost() => AnyCapsuleTrackingLost?.Invoke(this);

        [SerializeField] Animator m_Animator;
        [SerializeField] GameObject m_NormalShell;
        [SerializeField] GameObject m_CrackPiecesRoot;
        [SerializeField] string m_CrackStateName = "ChocolateCrack";
        [SerializeField, Range(0f, 0.9f)] float m_CrackStartNormalizedTime = 0.5f;
        [SerializeField, Range(0.03f, 0.2f)] float m_SquashDuration = 0.08f;
        [SerializeField, Range(0.7f, 0.98f)] float m_SquashY = 0.9f;
        [SerializeField, Range(1f, 1.2f)] float m_SquashXZ = 1.06f;
        [SerializeField, Range(1f, 1.15f)] float m_ReleaseOvershoot = 1.05f;
        [SerializeField] bool m_InteractionEnabled;
        [SerializeField] bool m_AllowSpatialPointer = true;

        TouchParticleFeedback m_ParticleFeedback;
        MoodiumTouchAudio m_TouchAudio;
        SoftTouchDeformationController m_SoftDeformation;

        Transform[] m_Pieces;
        Vector3[] m_InitialPositions;
        Quaternion[] m_InitialRotations;
        Vector3[] m_InitialScales;
        int m_CrackStateHash;
        int m_ActivePointerId = -1;
        bool m_IsPinched;
        Vector3 m_BurstCenterLocal;
        Vector3 m_InteractionBaseScale;
        Coroutine m_ScaleAnimation;

        public bool InteractionEnabled => m_InteractionEnabled;
        public bool SpatialPointerEnabled => m_AllowSpatialPointer;
        public bool IsPinched => m_IsPinched;
        public int PieceCount => m_Pieces == null ? 0 : m_Pieces.Length;
        public Vector3 BurstCenter => transform.TransformPoint(m_BurstCenterLocal);

        public void Configure(Animator animator, GameObject normalShell, GameObject crackPiecesRoot)
        {
            m_Animator = animator;
            m_NormalShell = normalShell;
            m_CrackPiecesRoot = crackPiecesRoot;
        }

        public void SetInteractionEnabled(bool enabled)
        {
            m_InteractionEnabled = enabled;
            if (!enabled && m_IsPinched)
                ResetCapsule(true);
        }

        public void SetSpatialPointerEnabled(bool enabled)
        {
            m_AllowSpatialPointer = enabled;
            if (!enabled && m_ActivePointerId >= 0)
                ResetCapsule(true);
        }

        void Awake()
        {
            ResolveReferences();
            m_ParticleFeedback = GetComponent<TouchParticleFeedback>();
            m_TouchAudio = GetComponent<MoodiumTouchAudio>();
            m_SoftDeformation = GetComponent<SoftTouchDeformationController>();
            CapturePiecePose();
            CaptureBurstCenter();
            m_InteractionBaseScale = transform.localScale;
            m_CrackStateHash = Animator.StringToHash(m_CrackStateName);
            ResetCapsule(false);

            var hasState = m_Animator != null && m_Animator.runtimeAnimatorController != null &&
                           m_Animator.HasState(0, m_CrackStateHash);
            Debug.Log(
                $"[Moodium Chocolate] Ready. animator={m_Animator != null}, " +
                $"controller={m_Animator?.runtimeAnimatorController?.name ?? "missing"}, " +
                $"state={m_CrackStateName}, hasState={hasState}, pieces={PieceCount}");
        }

        public void PointerStarted(int pointerId, Vector3 interactionPosition)
        {
            if (!m_AllowSpatialPointer || !BeginSqueeze())
                return;

            m_ActivePointerId = pointerId;
        }

        public void GestureStarted(float strength)
        {
            if (BeginSqueeze())
                GestureUpdated(strength);
        }

        public void GestureUpdated(float strength)
        {
            // Reserved for continuous, pressure-like visual deformation. The first version
            // intentionally keeps the existing authored crack animation as a one-shot response.
        }

        public void GestureEnded()
        {
            if (m_IsPinched && m_ActivePointerId < 0)
                ResetCapsule(true);
        }

        public void PalmContactStarted()
        {
            BeginSqueeze(BurstCenter);
        }

        public void HandContactStarted(Vector3 contactPosition)
        {
            BeginSqueeze(contactPosition);
        }

        /// <summary>
        /// A new squeeze while the hand stays on the bag (pressing down or closing the fingers again, see
        /// TrackedCapsuleHandCollision). Every AnyCapsulePinched listener treats it like a fresh press, so the hand no
        /// longer has to lift off between squeezes. Inside a gaze-pinch squeeze it does nothing.
        /// </summary>
        public bool HandSqueezeInPlace(Vector3 contactPosition)
        {
            if (!m_InteractionEnabled)
                return false;
            // The contact began during the cooldown, so it never counted: this squeeze is its first.
            if (!m_IsPinched)
                return BeginSqueeze(contactPosition);
            if (m_ActivePointerId >= 0)
                return false;

            if (m_SoftDeformation != null)
                m_SoftDeformation.TouchBegin(contactPosition);
            else
                PlayPressSquash();
            m_ParticleFeedback?.Play();
            if (m_TouchAudio != null)
                m_TouchAudio.Play();
            else
                MoodiumAudioManager.Play(MoodiumAudioCue.RealitySqueeze, BurstCenter);
            AnyCapsulePinched?.Invoke(this, BurstCenter);
            return true;
        }

        public void HandContactHeld(Vector3 contactPosition)
        {
            if (m_IsPinched)
            {
                m_SoftDeformation?.TouchHold(contactPosition);
            }
            if(m_InteractionEnabled)AnyCapsuleHeld?.Invoke(this,contactPosition);
        }

        /// <summary>
        /// Plays the authored ShellCrack sequence for a falling energy reward. This is intentionally
        /// separate from the tracked capsule's squeeze flow: the reward is activated only by real
        /// hand contact and is removed by its caller after the animation reaches its end pose.
        /// </summary>
        public bool PlayShellCrackForReward(Action onComplete)
        {
            if (m_Animator == null || m_Animator.runtimeAnimatorController == null ||
                !m_Animator.HasState(0, m_CrackStateHash))
                return false;

            m_NormalShell?.SetActive(false);
            m_CrackPiecesRoot?.SetActive(true);
            RestorePiecePose();
            m_Animator.enabled = true;
            m_Animator.speed = 1f;
            m_Animator.Play(m_CrackStateHash, 0, 0f);
            m_Animator.Update(0f);
            StartCoroutine(CompleteRewardShellCrack(GetCrackDuration(), onComplete));
            Debug.Log("[Moodium Energy Release] Chocolate reward touched; playing ShellCrack.");
            return true;
        }

        public void PalmContactEnded()
        {
            if (m_IsPinched && m_ActivePointerId < 0)
                ResetCapsule(true);
            else if(m_ActivePointerId<0)AnyCapsuleReleased?.Invoke(this);
        }

        void OnDisable()
        {
            if(m_IsPinched)ResetCapsule(false);
            else AnyCapsuleReleased?.Invoke(this);
        }

        bool BeginSqueeze()
        {
            return BeginSqueeze(BurstCenter);
        }

        bool BeginSqueeze(Vector3 contactPosition)
        {
            if (!m_InteractionEnabled || m_IsPinched)
                return false;

            m_IsPinched = true;
            m_InteractionBaseScale = transform.localScale;
            if (m_SoftDeformation != null)
                m_SoftDeformation.TouchBegin(contactPosition);
            else
                PlayPressSquash();
            Debug.Log("Chocolate Capsule Pinched");
            m_ParticleFeedback?.Play();
            if (m_TouchAudio != null)
                m_TouchAudio.Play();
            else
                MoodiumAudioManager.Play(MoodiumAudioCue.RealitySqueeze, BurstCenter);
            AnyCapsulePinched?.Invoke(this, BurstCenter);

            // Legacy Chocolate prefabs retain their authored crack animation. CandySoft
            // deliberately uses the Creative Space soft-deformation path instead.
            if (m_SoftDeformation == null && m_CrackPiecesRoot != null)
                m_CrackPiecesRoot.SetActive(true);

            if (m_SoftDeformation == null)
                RestorePiecePose();
            if (m_SoftDeformation == null && m_Animator != null && m_Animator.runtimeAnimatorController != null)
            {
                m_Animator.enabled = true;
                m_Animator.speed = 1f;
                m_Animator.Play(m_CrackStateHash, 0, m_CrackStartNormalizedTime);
                m_Animator.Update(0f);
            }
            if (m_SoftDeformation == null && m_NormalShell != null)
                m_NormalShell.SetActive(false);

            Debug.Log("Chocolate Crack Animation Start");
            return true;
        }

        public void PointerEnded(int pointerId)
        {
            if (!m_IsPinched || pointerId != m_ActivePointerId)
                return;
            ResetCapsule(true);
        }

        void ResetCapsule(bool logReset)
        {
            var wasPinched = m_IsPinched;
            m_IsPinched = false;
            if (wasPinched) AnyCapsuleReleased?.Invoke(this);
            m_ActivePointerId = -1;
            m_SoftDeformation?.TouchEnd();
            if (wasPinched && m_SoftDeformation == null)
                PlayReleaseBounce();

            if (m_CrackPiecesRoot != null)
                m_CrackPiecesRoot.SetActive(true);

            if (m_Animator != null && m_Animator.runtimeAnimatorController != null)
            {
                m_Animator.speed = 0f;
                m_Animator.Play(m_CrackStateHash, 0, 0f);
                m_Animator.Update(0f);
            }

            RestorePiecePose();
            if (m_NormalShell != null)
                m_NormalShell.SetActive(true);
            if (m_CrackPiecesRoot != null)
                m_CrackPiecesRoot.SetActive(false);

            if (logReset)
                Debug.Log("Chocolate Capsule Reset");
        }

        float GetCrackDuration()
        {
            foreach (var clip in m_Animator.runtimeAnimatorController.animationClips)
                if (clip != null && clip.name == "ChocolateCrack_Combined")
                    return Mathf.Max(.01f, clip.length);
            return Mathf.Max(.01f, m_Animator.GetCurrentAnimatorStateInfo(0).length);
        }

        IEnumerator CompleteRewardShellCrack(float duration, Action onComplete)
        {
            yield return new WaitForSeconds(duration);
            onComplete?.Invoke();
        }

        void CaptureBurstCenter()
        {
            var root=m_NormalShell!=null?m_NormalShell.transform:transform;
            m_BurstCenterLocal=Moodium.Flow.SoftModelBounds.TryGetLocalBounds(root,out var bounds)
                ?transform.InverseTransformPoint(root.TransformPoint(bounds.center)):Vector3.zero;
        }

        void PlayPressSquash()
        {
            if (m_ScaleAnimation != null)
                StopCoroutine(m_ScaleAnimation);
            transform.localScale = m_InteractionBaseScale;
            var target = Vector3.Scale(m_InteractionBaseScale, new Vector3(m_SquashXZ, m_SquashY, m_SquashXZ));
            m_ScaleAnimation = StartCoroutine(AnimateScale(target, m_SquashDuration, false));
        }

        void PlayReleaseBounce()
        {
            if (m_ScaleAnimation != null)
                StopCoroutine(m_ScaleAnimation);
            m_ScaleAnimation = StartCoroutine(ReleaseBounce());
        }

        IEnumerator ReleaseBounce()
        {
            yield return AnimateScale(m_InteractionBaseScale * m_ReleaseOvershoot, 0.08f, true);
            yield return AnimateScale(m_InteractionBaseScale, 0.1f, true);
            transform.localScale = m_InteractionBaseScale;
            m_ScaleAnimation = null;
        }

        IEnumerator AnimateScale(Vector3 target, float duration, bool keepCoroutineReference)
        {
            var start = transform.localScale;
            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / duration);
                var eased = t * t * (3f - 2f * t);
                transform.localScale = Vector3.LerpUnclamped(start, target, eased);
                yield return null;
            }
            transform.localScale = target;
            if (!keepCoroutineReference)
                m_ScaleAnimation = null;
        }

        void ResolveReferences()
        {
            if (m_Animator == null)
                m_Animator = GetComponentInChildren<Animator>(true);

            var transforms = GetComponentsInChildren<Transform>(true);
            foreach (var candidate in transforms)
            {
                if (m_NormalShell == null && candidate.name == "Shell_Normal")
                    m_NormalShell = candidate.gameObject;
                else if (m_CrackPiecesRoot == null && candidate.name == "Shell_Crack_Pieces_New")
                    m_CrackPiecesRoot = candidate.gameObject;
            }
        }

        void CapturePiecePose()
        {
            if (m_CrackPiecesRoot == null)
            {
                m_Pieces = System.Array.Empty<Transform>();
                return;
            }

            var allTransforms = m_CrackPiecesRoot.GetComponentsInChildren<Transform>(true);
            var pieces = new System.Collections.Generic.List<Transform>(20);
            foreach (var item in allTransforms)
            {
                if (item.name.StartsWith("CrackPiece_", System.StringComparison.Ordinal))
                    pieces.Add(item);
            }

            pieces.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            m_Pieces = pieces.ToArray();
            m_InitialPositions = new Vector3[m_Pieces.Length];
            m_InitialRotations = new Quaternion[m_Pieces.Length];
            m_InitialScales = new Vector3[m_Pieces.Length];
            for (var i = 0; i < m_Pieces.Length; i++)
            {
                m_InitialPositions[i] = m_Pieces[i].localPosition;
                m_InitialRotations[i] = m_Pieces[i].localRotation;
                m_InitialScales[i] = m_Pieces[i].localScale;
            }
        }

        void RestorePiecePose()
        {
            if (m_Pieces == null || m_InitialPositions == null)
                return;

            for (var i = 0; i < m_Pieces.Length; i++)
            {
                if (m_Pieces[i] == null)
                    continue;
                m_Pieces[i].SetLocalPositionAndRotation(m_InitialPositions[i], m_InitialRotations[i]);
                m_Pieces[i].localScale = m_InitialScales[i];
            }
        }
    }
}
