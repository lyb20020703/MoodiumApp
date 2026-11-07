using UnityEngine;

namespace VFXViewer
{
    public sealed class SpectatorRootManipulator : MonoBehaviour
    {
        private const float DefaultHeightOffset = -0.25f;

        [SerializeField] private float defaultDistance = 1.2f;
        [SerializeField] private bool startLocked;

        private Camera m_TargetCamera;

        public bool IsLocked { get; private set; }

        private void Awake()
        {
            m_TargetCamera = Camera.main;
            ResetPose();
            IsLocked = startLocked;
        }

        private void Update()
        {
            if (m_TargetCamera == null)
                m_TargetCamera = Camera.main;

            if (IsLocked)
                return;

            UpdateInput();
        }

        public void SetLocked(bool locked)
        {
            IsLocked = locked;
        }

        public void ResetPose()
        {
            if (!TryGetCameraBasis(out Vector3 cameraForward, out _))
                return;

            Vector3 position = m_TargetCamera.transform.position + cameraForward * defaultDistance;
            position.y += DefaultHeightOffset;

            Quaternion facing = Quaternion.LookRotation(-cameraForward, Vector3.up);
            transform.SetPositionAndRotation(position, facing);
        }

        public void NudgeForward(float deltaMeters)
        {
            if (TryGetRootPlanarBasis(out Vector3 rootForward, out _))
                transform.position += rootForward * deltaMeters;
        }

        public void NudgeRight(float deltaMeters)
        {
            if (TryGetRootPlanarBasis(out _, out Vector3 rootRight))
                transform.position += rootRight * deltaMeters;
        }

        public void NudgeUp(float deltaMeters)
        {
            transform.position += Vector3.up * deltaMeters;
        }

        public void NudgeYaw(float deltaDegrees)
        {
            transform.rotation = Quaternion.AngleAxis(deltaDegrees, Vector3.up) * transform.rotation;
        }

        private void UpdateInput()
        {
            if (Application.isEditor)
                UpdateEditorInput();
        }

        private void UpdateEditorInput()
        {
            if (Input.GetKey(KeyCode.A))
                NudgeYaw(-60f * Time.unscaledDeltaTime);
            if (Input.GetKey(KeyCode.D))
                NudgeYaw(60f * Time.unscaledDeltaTime);
            if (Input.GetKey(KeyCode.W))
                NudgeForward(Time.unscaledDeltaTime);
            if (Input.GetKey(KeyCode.S))
                NudgeForward(-Time.unscaledDeltaTime);
            if (Input.GetKey(KeyCode.Q))
                NudgeUp(Time.unscaledDeltaTime * 0.5f);
            if (Input.GetKey(KeyCode.E))
                NudgeUp(-Time.unscaledDeltaTime * 0.5f);

            if (Input.GetKeyDown(KeyCode.Return))
                SetLocked(!IsLocked);
        }

        private bool TryGetRootPlanarBasis(out Vector3 rootForward, out Vector3 rootRight)
        {
            rootForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (rootForward.sqrMagnitude <= 0.0001f)
                rootForward = Vector3.forward;
            rootForward.Normalize();

            rootRight = Vector3.ProjectOnPlane(transform.right, Vector3.up);
            if (rootRight.sqrMagnitude <= 0.0001f)
                rootRight = Vector3.Cross(Vector3.up, rootForward).normalized;
            else
                rootRight.Normalize();

            return true;
        }

        private bool TryGetCameraBasis(out Vector3 cameraForward, out Vector3 cameraRight)
        {
            if (m_TargetCamera == null)
                m_TargetCamera = Camera.main;

            if (m_TargetCamera == null)
            {
                cameraForward = Vector3.forward;
                cameraRight = Vector3.right;
                return false;
            }

            cameraForward = Vector3.ProjectOnPlane(m_TargetCamera.transform.forward, Vector3.up);
            if (cameraForward.sqrMagnitude <= 0.0001f)
                cameraForward = m_TargetCamera.transform.forward;
            cameraForward.Normalize();

            cameraRight = Vector3.ProjectOnPlane(m_TargetCamera.transform.right, Vector3.up);
            if (cameraRight.sqrMagnitude <= 0.0001f)
                cameraRight = Vector3.Cross(Vector3.up, cameraForward).normalized;
            else
                cameraRight.Normalize();

            return true;
        }
    }
}
