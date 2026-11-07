using UnityEngine;

namespace Moodium.UI
{
    /// <summary>Places only the Meshing 1 glass-panel preview in front of the viewer.</summary>
    public sealed class MoodiumGlassPanelTestPresenter : MonoBehaviour
    {
        [SerializeField, Min(0.5f)] float m_Distance = 1.1f;
        [SerializeField] float m_VerticalOffset = -0.02f;

        void Start()
        {
            var viewer = Camera.main;
            if (viewer == null)
            {
                Debug.LogWarning("[Moodium Glass UI] Main Camera was not found; keeping the scene preview position.");
                return;
            }

            var cameraTransform = viewer.transform;
            var forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.01f)
                forward = cameraTransform.forward.normalized;

            transform.SetPositionAndRotation(
                cameraTransform.position + forward * m_Distance + Vector3.up * m_VerticalOffset,
                Quaternion.LookRotation(forward, Vector3.up));
        }
    }
}
