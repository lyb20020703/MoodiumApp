using System;
using UnityEngine;

namespace VFXViewer
{
    [DisallowMultipleComponent]
    public sealed class GrabLifecycleRelay : MonoBehaviour, IGrabLifecycleSource
    {
        private int m_GrabCount;

        public bool IsGrabbed => m_GrabCount > 0;

        public event Action GrabStarted;
        public event Action GrabEnded;

        public void NotifyGrabStarted()
        {
            m_GrabCount++;
            if (m_GrabCount == 1)
                GrabStarted?.Invoke();
        }

        public void NotifyGrabEnded()
        {
            if (m_GrabCount <= 0)
                return;

            m_GrabCount--;
            if (m_GrabCount == 0)
                GrabEnded?.Invoke();
        }

        public void ForceClear()
        {
            if (m_GrabCount <= 0)
                return;

            m_GrabCount = 0;
            GrabEnded?.Invoke();
        }
    }
}
