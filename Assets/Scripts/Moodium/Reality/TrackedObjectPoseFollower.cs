using UnityEngine;

namespace Moodium.Reality
{
    /// <summary>
    /// Keeps a visual enhancement rigidly bound to an AR tracked-object anchor.
    /// The explicit LateUpdate sync protects the binding from runtime interaction
    /// components that may otherwise rewrite the spawned object's transform.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TrackedObjectPoseFollower : MonoBehaviour
    {
        Transform m_Anchor;
        Vector3 m_LocalPosition;
        Quaternion m_LocalRotation = Quaternion.identity;

        public Transform Anchor => m_Anchor;

        public void Configure(Transform anchor, Vector3 localPosition, Quaternion localRotation)
        {
            m_Anchor = anchor;
            m_LocalPosition = localPosition;
            m_LocalRotation = localRotation;
            if (transform.parent != null)
                transform.SetParent(null, true);
            SyncNow();
        }

        public void SyncNow()
        {
            if (m_Anchor == null)
                return;

            transform.SetPositionAndRotation(
                m_Anchor.TransformPoint(m_LocalPosition),
                m_Anchor.rotation * m_LocalRotation);
        }

        void LateUpdate()
        {
            SyncNow();
        }
    }
}
