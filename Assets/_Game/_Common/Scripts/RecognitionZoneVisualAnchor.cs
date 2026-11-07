using UnityEngine;

namespace VFXViewer
{
    [DisallowMultipleComponent]
    public sealed class RecognitionZoneVisualAnchor : MonoBehaviour
    {
        [Header("以当前物体世界位置作为光圈中心")]
        [SerializeField] private bool m_UseEllipse = false;
        [SerializeField] private float m_LocalRadiusMeters = 1f;
        [SerializeField] private Vector2 m_LocalRadiiMeters = Vector2.one;
        [Header("附加视觉缩放（可选）")]
        [SerializeField] private Transform m_LinkedXZScaleTarget;
        [SerializeField] private float m_LinkedFarDistanceXZScale = 0.03f;
        [SerializeField] private float m_LinkedNearDistanceXZScale = 0.3f;

        private Vector3 m_BaseLocalScale = Vector3.one;
        private bool m_HasCapturedBaseLocalScale;
        private Vector3 m_LinkedTargetBaseLocalScale = Vector3.one;
        private bool m_HasCapturedLinkedTargetBaseLocalScale;

        public Vector3 WorldCenter => transform.position;

        public bool TryContainsPlanarPoint(Vector3 worldPoint, out float planarDistanceMeters)
        {
            Vector2 worldRadii = GetWorldPlanarRadii();
            Vector2 planarCenter = new Vector2(transform.position.x, transform.position.z);
            Vector2 planarPoint = new Vector2(worldPoint.x, worldPoint.z);
            Vector2 delta = planarPoint - planarCenter;

            if (!m_UseEllipse)
            {
                planarDistanceMeters = delta.magnitude;
                return planarDistanceMeters <= Mathf.Max(0.05f, worldRadii.x);
            }

            float radiusX = Mathf.Max(0.05f, worldRadii.x);
            float radiusZ = Mathf.Max(0.05f, worldRadii.y);
            float normalized =
                (delta.x * delta.x) / (radiusX * radiusX) +
                (delta.y * delta.y) / (radiusZ * radiusZ);
            planarDistanceMeters = delta.magnitude;
            return normalized <= 1f;
        }

        public Vector2 GetWorldPlanarRadii()
        {
            Vector3 lossyScale = transform.lossyScale;
            if (!m_UseEllipse)
            {
                float worldRadius = Mathf.Max(
                    0.05f,
                    Mathf.Abs(m_LocalRadiusMeters) * Mathf.Max(Mathf.Abs(lossyScale.x), Mathf.Abs(lossyScale.z)));
                return new Vector2(worldRadius, worldRadius);
            }

            return new Vector2(
                Mathf.Max(0.05f, Mathf.Abs(m_LocalRadiiMeters.x) * Mathf.Abs(lossyScale.x)),
                Mathf.Max(0.05f, Mathf.Abs(m_LocalRadiiMeters.y) * Mathf.Abs(lossyScale.z)));
        }

        public void UpdateVisualScaleByPlanarDistance(
            Vector3 viewerWorldPosition,
            float nearDistanceMeters,
            float farDistanceMeters,
            float minScale,
            float maxScale)
        {
            CaptureBaseLocalScaleIfNeeded();

            float clampedNear = Mathf.Max(0f, nearDistanceMeters);
            float clampedFar = Mathf.Max(clampedNear + 0.001f, farDistanceMeters);
            float planarDistanceMeters = Vector2.Distance(
                new Vector2(transform.position.x, transform.position.z),
                new Vector2(viewerWorldPosition.x, viewerWorldPosition.z));
            float t = Mathf.InverseLerp(clampedNear, clampedFar, planarDistanceMeters);
            float targetScale = Mathf.Lerp(minScale, maxScale, t);

            ApplyVisualScale(targetScale, t);
        }

        public void ApplyTriggeredVisualScale(float triggeredVisualScale)
        {
            ApplyVisualScale(triggeredVisualScale, 0f);
        }

        private void ApplyVisualScale(float targetScale, float normalizedDistance)
        {
            float baseReferenceScale = Mathf.Max(
                0.0001f,
                Mathf.Max(Mathf.Abs(m_BaseLocalScale.x), Mathf.Abs(m_BaseLocalScale.y), Mathf.Abs(m_BaseLocalScale.z)));
            Vector3 normalizedBaseScale = m_BaseLocalScale / baseReferenceScale;
            transform.localScale = normalizedBaseScale * targetScale;
            UpdateLinkedTargetScale(normalizedDistance);
        }

        public void ResetVisualScale()
        {
            CaptureBaseLocalScaleIfNeeded();
            transform.localScale = m_BaseLocalScale;
            ResetLinkedTargetScale();
        }

        private void UpdateLinkedTargetScale(float normalizedDistance)
        {
            if (m_LinkedXZScaleTarget == null)
                return;

            CaptureLinkedTargetBaseLocalScaleIfNeeded();
            float targetXZScale = Mathf.Lerp(m_LinkedNearDistanceXZScale, m_LinkedFarDistanceXZScale, normalizedDistance);
            m_LinkedXZScaleTarget.localScale = new Vector3(
                targetXZScale,
                m_LinkedTargetBaseLocalScale.y,
                targetXZScale);
        }

        private void ResetLinkedTargetScale()
        {
            if (m_LinkedXZScaleTarget == null)
                return;

            CaptureLinkedTargetBaseLocalScaleIfNeeded();
            m_LinkedXZScaleTarget.localScale = m_LinkedTargetBaseLocalScale;
        }

        private void CaptureBaseLocalScaleIfNeeded()
        {
            if (m_HasCapturedBaseLocalScale)
                return;

            m_BaseLocalScale = transform.localScale;
            m_HasCapturedBaseLocalScale = true;
        }

        private void CaptureLinkedTargetBaseLocalScaleIfNeeded()
        {
            if (m_HasCapturedLinkedTargetBaseLocalScale || m_LinkedXZScaleTarget == null)
                return;

            m_LinkedTargetBaseLocalScale = m_LinkedXZScaleTarget.localScale;
            m_HasCapturedLinkedTargetBaseLocalScale = true;
        }
    }
}
