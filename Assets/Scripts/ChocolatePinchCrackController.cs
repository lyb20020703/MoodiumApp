using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;

namespace Moodium
{
    public sealed class ChocolatePinchCrackController : MonoBehaviour
    {
        [SerializeField] Animator m_Animator;
        [SerializeField] string m_CrackStateName = "ChocolateCrack";
        [SerializeField, Min(0.005f)] float m_PinchThreshold = 0.025f;
        [SerializeField, Min(0.005f)] float m_ReleaseThreshold = 0.035f;

        int m_CrackStateHash;
        bool m_IsPinching;
        bool m_LoggedHandSubsystemReady;
        bool m_LoggedMissingHandSubsystem;
        bool m_WasTrackingHands;

        XRHandSubsystem m_HandSubsystem;

        public void Configure(Animator animator)
        {
            m_Animator = animator;
        }

        void Awake()
        {
            if (m_Animator == null)
                m_Animator = GetComponentInChildren<Animator>(true);

            m_CrackStateHash = Animator.StringToHash(m_CrackStateName);

            if (m_Animator == null)
            {
                Debug.LogError("[Moodium Animation] Chocolate Animator is missing.");
            }
            else
            {
                var controller = m_Animator.runtimeAnimatorController;
                var clipNames = controller == null
                    ? "none"
                    : string.Join(", ", System.Array.ConvertAll(
                        controller.animationClips,
                        clip => clip != null ? clip.name : "null"));
                Debug.Log(
                    $"[Moodium Animation] Animator ready. " +
                    $"controller={(controller != null ? controller.name : "missing")}, " +
                    $"state={m_CrackStateName}, hasState={m_Animator.HasState(0, m_CrackStateHash)}, " +
                    $"clips=[{clipNames}]");
            }

            ResetChocolate();
        }

        void Update()
        {
            if (!TryGetHandSubsystem())
                return;

            var flags = m_HandSubsystem.TryUpdateHands(XRHandSubsystem.UpdateType.Dynamic);
            var rightTracked = (flags & XRHandSubsystem.UpdateSuccessFlags.RightHandJoints) != 0;
            var leftTracked = (flags & XRHandSubsystem.UpdateSuccessFlags.LeftHandJoints) != 0;
            var trackingHands = rightTracked || leftTracked;

            if (trackingHands != m_WasTrackingHands)
            {
                m_WasTrackingHands = trackingHands;
                Debug.Log(
                    trackingHands
                        ? $"[Moodium Hand] Hand joints tracked. left={leftTracked}, right={rightTracked}"
                        : "[Moodium Hand] Hand joint tracking lost.");
            }

            var rightPinching = rightTracked && IsHandPinching(m_HandSubsystem.rightHand);
            var leftPinching = leftTracked && IsHandPinching(m_HandSubsystem.leftHand);
            var eitherHandPinching = rightPinching || leftPinching;

            if (!m_IsPinching && eitherHandPinching)
                BeginCrack();
            else if (m_IsPinching && !eitherHandPinching)
                ResetChocolate();
        }

        bool TryGetHandSubsystem()
        {
            if (m_HandSubsystem != null)
                return true;

            m_HandSubsystem = XRGeneralSettings.Instance?.Manager?.activeLoader
                ?.GetLoadedSubsystem<XRHandSubsystem>();

            if (m_HandSubsystem == null)
            {
                if (!m_LoggedMissingHandSubsystem)
                {
                    m_LoggedMissingHandSubsystem = true;
                    Debug.LogError("[Moodium Hand] XRHandSubsystem is unavailable.");
                }

                return false;
            }

            if (!m_HandSubsystem.running)
                m_HandSubsystem.Start();

            if (!m_LoggedHandSubsystemReady)
            {
                m_LoggedHandSubsystemReady = true;
                Debug.Log("[Moodium Hand] XRHandSubsystem ready.");
            }

            return true;
        }

        bool IsHandPinching(XRHand hand)
        {
            var index = hand.GetJoint(XRHandJointID.IndexTip);
            var thumb = hand.GetJoint(XRHandJointID.ThumbTip);

            if (index.trackingState == XRHandJointTrackingState.None ||
                thumb.trackingState == XRHandJointTrackingState.None ||
                !index.TryGetPose(out var indexPose) ||
                !thumb.TryGetPose(out var thumbPose))
                return false;

            var threshold = m_IsPinching ? m_ReleaseThreshold : m_PinchThreshold;
            return Vector3.Distance(indexPose.position, thumbPose.position) <= threshold;
        }

        void BeginCrack()
        {
            if (m_Animator == null)
                return;

            m_IsPinching = true;
            m_Animator.speed = 1f;
            m_Animator.Play(m_CrackStateHash, 0, 0f);
            m_Animator.Update(0f);
            Debug.Log("[Moodium Hand] Pinch started. Playing ChocolateCrack.");
        }

        void ResetChocolate()
        {
            var wasPinching = m_IsPinching;
            m_IsPinching = false;
            if (m_Animator == null)
                return;

            m_Animator.Play(m_CrackStateHash, 0, 0f);
            m_Animator.Update(0f);
            m_Animator.speed = 0f;

            if (Application.isPlaying && wasPinching)
                Debug.Log("[Moodium Hand] Pinch ended. Chocolate reset.");
        }
    }
}
