using System;
using UnityEngine;

namespace Moodium.MoodCheck
{
    [DisallowMultipleComponent]
    public sealed class MoodCheckPlateHandle : MonoBehaviour
    {
        [SerializeField] MoodCheckPlate m_Plate;
        int m_ActivePointerId = -1;

        public event Action<Vector2> Released;
        public int ActivePointerId => m_ActivePointerId;

        public void Configure(MoodCheckPlate plate) => m_Plate = plate;

        public void PointerStarted(int pointerId, Vector3 worldPosition)
        {
            if (m_Plate == null || m_ActivePointerId >= 0)
                return;
            m_ActivePointerId = pointerId;
            m_Plate.BeginDrag();
            m_Plate.SetFromWorldPosition(worldPosition);
        }

        public void PointerMoved(int pointerId, Vector3 worldPosition)
        {
            if (pointerId == m_ActivePointerId)
                m_Plate?.SetFromWorldPosition(worldPosition);
        }

        public void PointerEnded(int pointerId, Vector3 worldPosition)
        {
            if (pointerId != m_ActivePointerId)
                return;
            m_Plate?.SetFromWorldPosition(worldPosition);
            m_Plate?.EndDrag();
            m_ActivePointerId = -1;
            if (m_Plate != null)
                Released?.Invoke(m_Plate.NormalizedPosition);
        }

        public void CancelPointer(int pointerId)
        {
            if (pointerId != m_ActivePointerId)
                return;
            m_Plate?.EndDrag();
            m_ActivePointerId = -1;
        }
    }
}
