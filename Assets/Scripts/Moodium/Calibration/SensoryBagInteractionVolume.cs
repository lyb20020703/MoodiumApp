using UnityEngine;

namespace Moodium.Calibration
{
    [DisallowMultipleComponent]
    public sealed class SensoryBagInteractionVolume : MonoBehaviour
    {
        BoxCollider m_Collider;
        public BoxCollider Collider => m_Collider;
        public float Width => m_Collider != null ? m_Collider.size.x : 0f;
        public float Height => m_Collider != null ? m_Collider.size.y : 0f;
        public float Depth => m_Collider != null ? m_Collider.size.z : 0f;

        void Awake() => EnsureCollider();

        public void Configure(float width, float height, float depth)
        {
            EnsureCollider();
            m_Collider.isTrigger = true;
            m_Collider.size = new Vector3(Mathf.Max(.0001f, width), Mathf.Max(.0001f, height), Mathf.Max(.0001f, depth));
        }

        void EnsureCollider()
        {
            // Do not retain a Unity "fake null" component reference across an
            // editor scene reload. Resolve from this exact InteractionRoot each
            // time so the calibration preview and the runtime take the same path.
            var collider = gameObject.GetComponent<BoxCollider>();
            if (collider == null)
                collider = gameObject.AddComponent<BoxCollider>();
            m_Collider = collider;
        }
    }
}
