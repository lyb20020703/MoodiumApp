using UnityEngine;

namespace Moodium.Guide
{
    /// <summary>
    /// Invisible collider behind the tutorial card face. MoodiumSpatialInteractionController forwards a
    /// pinch that starts on it (see the README patch), and a horizontal drag turns the page.
    /// Same pointer contract as SensoryBagCalibrationHandle: PointerStarted / PointerMoved / PointerEnded.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorldGuideSwipeHandle : MonoBehaviour
    {
        WorldGuideCards m_Owner;
        int m_ActivePointerId = -1;
        public bool CanInteract => isActiveAndEnabled && m_Owner!=null && m_Owner.CanInteract;

        public void Configure(WorldGuideCards owner) => m_Owner = owner;

        public void PointerStarted(int pointerId, Vector3 worldPosition)
        {
            if (!CanInteract || m_ActivePointerId != -1) return;
            m_ActivePointerId = pointerId;
            m_Owner.BeginSwipe(worldPosition);
        }

        public void PointerMoved(int pointerId, Vector3 worldPosition)
        {
            if (pointerId == m_ActivePointerId && m_Owner != null)
                m_Owner.MoveSwipe(worldPosition);
        }

        public void PointerEnded(int pointerId)
        {
            if (pointerId != m_ActivePointerId) return;
            m_ActivePointerId = -1;
            if (m_Owner != null) m_Owner.EndSwipe();
        }

        public void CancelPointer(int pointerId)
        {
            if (m_ActivePointerId != pointerId) return;
            m_ActivePointerId = -1;
            if (m_Owner != null) m_Owner.CancelSwipe();
        }
        void OnDisable(){if(m_ActivePointerId!=-1)CancelPointer(m_ActivePointerId);}
    }
}
