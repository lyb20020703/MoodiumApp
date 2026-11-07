using System.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace VFXViewer
{
    /// <summary>
    /// Test 1 scene only: on Vision Pro, align the runtime headset view to the
    /// scene-authored XR Origin view so module-only playback can run without Root placement.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Test1VisionProViewAligner : MonoBehaviour
    {
        private const string TargetSceneName = "Test 1";
        private const int ReapplyFrameCount = 2;
        private const float TrackingWaitTimeoutSeconds = 5f;

        private XROrigin m_XROrigin;
        private Vector3 m_AuthoredCameraPosition;
        private Quaternion m_AuthoredCameraRotation;
        private bool m_HasAuthoredViewPose;
        private Coroutine m_AlignCoroutine;

        private void Awake()
        {
            m_XROrigin = GetComponent<XROrigin>();
            CacheAuthoredViewPose();
        }

        private void OnEnable()
        {
#if UNITY_EDITOR || !UNITY_VISIONOS
            enabled = false;
            return;
#else
            if (!IsTargetScene())
            {
                enabled = false;
                return;
            }

            if (m_AlignCoroutine == null)
                m_AlignCoroutine = StartCoroutine(AlignWhenTrackingReady());
#endif
        }

        private void OnDisable()
        {
            if (m_AlignCoroutine == null)
                return;

            StopCoroutine(m_AlignCoroutine);
            m_AlignCoroutine = null;
        }

        private bool IsTargetScene()
        {
            return string.Equals(gameObject.scene.name, TargetSceneName, System.StringComparison.Ordinal);
        }

        private void CacheAuthoredViewPose()
        {
            if (m_XROrigin == null)
                return;

            Transform cameraTransform = m_XROrigin.Camera != null ? m_XROrigin.Camera.transform : null;
            if (cameraTransform == null)
                return;

            m_AuthoredCameraPosition = cameraTransform.position;
            m_AuthoredCameraRotation = cameraTransform.rotation;
            m_HasAuthoredViewPose = true;
        }

        private IEnumerator AlignWhenTrackingReady()
        {
            float startTime = Time.realtimeSinceStartup;
            while (!CanAlignNow())
            {
                if (Time.realtimeSinceStartup - startTime >= TrackingWaitTimeoutSeconds)
                    break;

                yield return null;
            }

            yield return null;
            yield return new WaitForEndOfFrame();

            for (int i = 0; i < ReapplyFrameCount; i++)
            {
                ApplyAlignment();
                yield return null;
            }

            m_AlignCoroutine = null;
            enabled = false;
        }

        private bool CanAlignNow()
        {
            if (m_XROrigin == null || m_XROrigin.Camera == null || !m_HasAuthoredViewPose)
                return false;

            var session = FindFirstObjectByType<ARSession>(FindObjectsInactive.Exclude);
            if (session == null)
                return true;

            return ARSession.state >= ARSessionState.SessionTracking;
        }

        private void ApplyAlignment()
        {
            if (m_XROrigin == null || !m_HasAuthoredViewPose)
                return;

            m_XROrigin.MoveCameraToWorldLocation(m_AuthoredCameraPosition);

            Vector3 desiredForward = Vector3.ProjectOnPlane(
                m_AuthoredCameraRotation * Vector3.forward,
                Vector3.up);

            if (desiredForward.sqrMagnitude >= 0.0001f)
                m_XROrigin.MatchOriginUpCameraForward(Vector3.up, desiredForward.normalized);
        }
    }
}
