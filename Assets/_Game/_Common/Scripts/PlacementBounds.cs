using UnityEngine;

namespace VFXViewer
{
    [DisallowMultipleComponent]
    [AddComponentMenu("VFXViewer/Placement/Placement Bounds")]
    public sealed class PlacementBounds : MonoBehaviour
    {
        [SerializeField] private bool m_UseAttachedBoxCollider = true;
        [SerializeField] private Vector3 m_Center = Vector3.zero;
        [SerializeField] private Vector3 m_Size = new Vector3(0.3f, 0.3f, 0.3f);

        private void OnEnable()
        {
            if (!Application.isPlaying)
                return;

            ExhibitPlacementManager placementManager = FindFirstObjectByType<ExhibitPlacementManager>(FindObjectsInactive.Include);
            if (placementManager == null)
            {
                // Scenes without a placement manager should never expose placement-only colliders at runtime.
                SetRuntimeCollidersEnabled(false);
                return;
            }

            SetRuntimeCollidersEnabled(placementManager.ShouldEnablePlacementCollidersForTarget(transform));
        }

        public bool TryGetLocalBox(out Vector3 center, out Vector3 size)
        {
            if (m_UseAttachedBoxCollider && TryGetComponent(out BoxCollider boxCollider))
            {
                center = boxCollider.center;
                size = boxCollider.size;
                return size.x > 0f && size.y > 0f && size.z > 0f;
            }

            center = m_Center;
            size = m_Size;
            return size.x > 0f && size.y > 0f && size.z > 0f;
        }

        public bool TryGetAttachedBoxCollider(out BoxCollider boxCollider)
        {
            if (m_UseAttachedBoxCollider && TryGetComponent(out boxCollider))
                return true;

            boxCollider = null;
            return false;
        }

        public void SetRuntimeCollidersEnabled(bool enabled)
        {
            var colliders = GetComponents<Collider>();
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider != null)
                    collider.enabled = enabled;
            }
        }
    }
}
