using Moodium.Gestures;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Hands.Gestures;
using UnityEngine.XR.Management;

namespace Moodium.Interaction
{
    [DisallowMultipleComponent]
    public sealed class BimanualThumbBendGestureController : MonoBehaviour
    {
        [Header("Thumb bend detection")]
        [SerializeField, Range(0f, 1f)] float m_StartThreshold = 0.55f;
        [SerializeField, Range(0f, 1f)] float m_ReleaseThreshold = 0.35f;
        [SerializeField, Min(0f)] float m_StartDebounceSeconds = 0.05f;
        [SerializeField, Min(0f)] float m_TrackingLossGraceSeconds = 0.12f;
        [SerializeField, Min(0.001f)] float m_SmoothingSeconds = 0.08f;

        [Header("Runtime diagnostics")]
        [SerializeField] bool m_GestureEnabled;
        [SerializeField, Range(0f, 1f)] float m_LeftThumbCurl;
        [SerializeField, Range(0f, 1f)] float m_RightThumbCurl;
        [SerializeField] bool m_LeftThumbTracked;
        [SerializeField] bool m_RightThumbTracked;
        [SerializeField] bool m_IsSqueezing;

        XRHandSubsystem m_HandSubsystem;
        BimanualThumbBendStateMachine m_StateMachine;
        ChocolateCapsuleInteraction m_Target;
        bool m_LeftSmoothingInitialized;
        bool m_RightSmoothingInitialized;
        bool m_LoggedSubsystemReady;
        bool m_LoggedSubsystemMissing;

        public float LeftThumbCurl => m_LeftThumbCurl;
        public float RightThumbCurl => m_RightThumbCurl;
        public bool IsSqueezing => m_IsSqueezing;

        public void SetTarget(ChocolateCapsuleInteraction target)
        {
            if (m_Target == target)
                return;

            EndCurrentGesture();
            m_Target = target;
        }

        public void SetGestureEnabled(bool enabled)
        {
            if (m_GestureEnabled == enabled)
                return;

            m_GestureEnabled = enabled;
            if (!enabled)
                EndCurrentGesture();
        }

        void Awake()
        {
            RebuildStateMachine();
        }

        void OnEnable()
        {
            TrySubscribeToHandSubsystem();
        }

        void Update()
        {
            if (m_HandSubsystem == null)
                TrySubscribeToHandSubsystem();
        }

        void OnDisable()
        {
            if (m_HandSubsystem != null)
                m_HandSubsystem.updatedHands -= OnHandsUpdated;
            m_HandSubsystem = null;
            EndCurrentGesture();
        }

        void OnValidate()
        {
            if (m_ReleaseThreshold >= m_StartThreshold)
                m_ReleaseThreshold = Mathf.Max(0f, m_StartThreshold - 0.05f);

            if (!Application.isPlaying)
                return;

            EndCurrentGesture();
            RebuildStateMachine();
        }

        void RebuildStateMachine()
        {
            m_StateMachine = new BimanualThumbBendStateMachine(
                m_StartThreshold,
                m_ReleaseThreshold,
                m_StartDebounceSeconds,
                m_TrackingLossGraceSeconds);
        }

        void TrySubscribeToHandSubsystem()
        {
            var subsystem = XRGeneralSettings.Instance?.Manager?.activeLoader
                ?.GetLoadedSubsystem<XRHandSubsystem>();
            if (subsystem == null)
            {
                if (!m_LoggedSubsystemMissing)
                {
                    m_LoggedSubsystemMissing = true;
                    Debug.LogWarning("[Moodium Thumb Bend] XRHandSubsystem is not available yet.");
                }
                return;
            }

            m_HandSubsystem = subsystem;
            m_HandSubsystem.updatedHands -= OnHandsUpdated;
            m_HandSubsystem.updatedHands += OnHandsUpdated;
            m_LoggedSubsystemMissing = false;
            if (!m_LoggedSubsystemReady)
            {
                m_LoggedSubsystemReady = true;
                Debug.Log("[Moodium Thumb Bend] XRHandSubsystem connected.");
            }
        }

        void OnHandsUpdated(
            XRHandSubsystem subsystem,
            XRHandSubsystem.UpdateSuccessFlags updateSuccessFlags,
            XRHandSubsystem.UpdateType updateType)
        {
            if (updateType != XRHandSubsystem.UpdateType.Dynamic || m_StateMachine == null)
                return;

            if (!m_GestureEnabled || m_Target == null || !m_Target.isActiveAndEnabled)
            {
                EndCurrentGesture();
                return;
            }

            m_LeftThumbTracked = TryGetThumbCurl(subsystem.leftHand, out var rawLeftCurl);
            m_RightThumbTracked = TryGetThumbCurl(subsystem.rightHand, out var rawRightCurl);
            var deltaTime = Mathf.Max(0f, Time.unscaledDeltaTime);
            if (m_LeftThumbTracked)
                m_LeftThumbCurl = SmoothCurl(rawLeftCurl, ref m_LeftSmoothingInitialized, m_LeftThumbCurl, deltaTime);
            if (m_RightThumbTracked)
                m_RightThumbCurl = SmoothCurl(rawRightCurl, ref m_RightSmoothingInitialized, m_RightThumbCurl, deltaTime);

            var result = m_StateMachine.Update(
                m_LeftThumbCurl,
                m_RightThumbCurl,
                m_LeftThumbTracked,
                m_RightThumbTracked,
                deltaTime);
            m_IsSqueezing = result.IsSqueezing;

            if (result.Started)
                m_Target.GestureStarted(result.Strength);
            else if (result.IsSqueezing)
                m_Target.GestureUpdated(result.Strength);

            if (result.Ended)
                m_Target.GestureEnded();
        }

        float SmoothCurl(float rawCurl, ref bool initialized, float currentValue, float deltaTime)
        {
            rawCurl = Mathf.Clamp01(rawCurl);
            if (!initialized)
            {
                initialized = true;
                return rawCurl;
            }

            var blend = 1f - Mathf.Exp(-deltaTime / m_SmoothingSeconds);
            return Mathf.Lerp(currentValue, rawCurl, blend);
        }

        static bool TryGetThumbCurl(XRHand hand, out float curl)
        {
            curl = 0f;
            if (!hand.isTracked)
                return false;

            var shape = hand.CalculateFingerShape(
                XRHandFingerID.Thumb,
                XRFingerShapeTypes.FullCurl);
            return shape.TryGetFullCurl(out curl);
        }

        void EndCurrentGesture()
        {
            if (m_StateMachine == null)
                return;

            var result = m_StateMachine.Reset();
            if (result.Ended && m_Target != null)
                m_Target.GestureEnded();

            m_IsSqueezing = false;
            m_LeftThumbTracked = false;
            m_RightThumbTracked = false;
            m_LeftSmoothingInitialized = false;
            m_RightSmoothingInitialized = false;
        }
    }
}
