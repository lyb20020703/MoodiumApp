using System;
using UnityEngine;

namespace Moodium.Flow
{
    [DisallowMultipleComponent]
    public sealed class MoodiumSliderHandle : MonoBehaviour
    {
        [SerializeField, Range(.1f, 1f)] float m_CompleteThreshold = .9f;
        Transform m_Track;
        Transform m_Handle;
        Action m_Completed;
        Vector3 m_Start;
        Vector3 m_End;
        bool m_Dragging;
        int m_PointerId;
        public float Progress { get; private set; }

        public void Configure(Transform track, Transform handle, Action completed)
        {
            m_Track = track;
            m_Handle = handle;
            m_Completed = completed;
            m_Start = new Vector3(-.078f, 0f, -.012f);
            m_End = new Vector3(.078f, 0f, -.012f);
            ResetSlider();
        }
        public void PointerStarted(int pointerId, Vector3 worldPosition)
        {
            m_Dragging = true;
            m_PointerId = pointerId;
            PointerMoved(pointerId, worldPosition);
        }
        public void PointerMoved(int pointerId, Vector3 worldPosition)
        {
            if (!m_Dragging || pointerId != m_PointerId || m_Track == null) return;
            var local = m_Track.InverseTransformPoint(worldPosition);
            Progress = Mathf.Clamp01(Mathf.InverseLerp(m_Start.x, m_End.x, local.x));
            if (m_Handle != null) m_Handle.localPosition = Vector3.LerpUnclamped(m_Start, m_End, Progress);
        }
        public void PointerEnded(int pointerId, bool cancelled = false)
        {
            if (!m_Dragging || pointerId != m_PointerId) return;
            m_Dragging = false;
            if (!cancelled && Progress >= m_CompleteThreshold)
            {
                Progress = 1f;
                if (m_Handle != null) m_Handle.localPosition = m_End;
                m_Completed?.Invoke();
            }
            else ResetSlider();
        }
        void ResetSlider() { Progress = 0f; if (m_Handle != null) m_Handle.localPosition = m_Start; }
    }
}
