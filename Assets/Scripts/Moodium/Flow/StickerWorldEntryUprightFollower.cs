using UnityEngine;

namespace Moodium.Flow
{
    /// <summary>
    /// Keeps a sticker-driven world entry at the image anchor's position while
    /// deliberately decoupling its visual up direction from the sticker rotation.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StickerWorldEntryUprightFollower : MonoBehaviour
    {
        Transform m_Anchor;
        Vector3 m_LocalPosition;
        Vector3 m_LocalScale = Vector3.one;

        public void Configure(Transform anchor, Vector3 localPosition, Vector3 localScale)
        {
            m_Anchor = anchor;
            m_LocalPosition = localPosition;
            m_LocalScale = localScale;
            FollowNow();
        }

        void LateUpdate() => FollowNow();

        void FollowNow()
        {
            if (m_Anchor == null)
                return;

            transform.position = m_Anchor.TransformPoint(m_LocalPosition);
            var camera = Camera.main;
            var toViewer = camera != null
                ? camera.transform.position - transform.position
                : Vector3.forward;
            transform.rotation = CalculateUprightRotation(transform.position, transform.position + toViewer);
            transform.localScale = m_LocalScale;
        }

        public static Quaternion CalculateUprightRotation(Vector3 position, Vector3 viewerPosition)
        {
            var horizontal = Vector3.ProjectOnPlane(viewerPosition - position, Vector3.up);
            if (horizontal.sqrMagnitude < 0.0001f)
                horizontal = Vector3.forward;
            // These entrance prefabs were authored with their display front on local
            // -Z. Point that face at the viewer instead of exposing the back side.
            return Quaternion.LookRotation(-horizontal.normalized, Vector3.up);
        }
    }
}
