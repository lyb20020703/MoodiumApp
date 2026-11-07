using Autohand;
using UnityEngine;

namespace VFXViewer
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(GrabLifecycleRelay))]
    public sealed class AutoHandGrabAdapter : MonoBehaviour
    {
        private GrabLifecycleRelay m_Relay;
        private Grabbable m_Grabbable;
        private int m_LocalGrabCount;

        private void Awake()
        {
            m_Relay = GetComponent<GrabLifecycleRelay>();
            m_Grabbable = GetComponent<Grabbable>();
        }

        private void OnEnable()
        {
            if (m_Grabbable == null)
                m_Grabbable = GetComponent<Grabbable>();

            if (m_Grabbable == null)
                return;

            m_Grabbable.OnGrabEvent += OnGrabbed;
            m_Grabbable.OnReleaseEvent += OnReleased;
        }

        private void Update()
        {
            if (m_LocalGrabCount <= 0 || m_Grabbable == null)
                return;

            if (IsActuallyGrabbed())
                return;

            m_LocalGrabCount = 0;
            m_Relay?.ForceClear();
        }

        private void OnDisable()
        {
            if (m_Grabbable != null)
            {
                m_Grabbable.OnGrabEvent -= OnGrabbed;
                m_Grabbable.OnReleaseEvent -= OnReleased;
            }

            while (m_LocalGrabCount > 0)
            {
                m_Relay?.NotifyGrabEnded();
                m_LocalGrabCount--;
            }
        }

        private void OnGrabbed(Hand hand, Grabbable grab)
        {
            m_LocalGrabCount++;
            m_Relay?.NotifyGrabStarted();
        }

        private void OnReleased(Hand hand, Grabbable grab)
        {
            if (m_LocalGrabCount <= 0)
                return;

            m_LocalGrabCount--;
            m_Relay?.NotifyGrabEnded();
        }

        private bool IsActuallyGrabbed()
        {
            return m_Grabbable != null &&
                   (m_Grabbable.beingGrabbed || m_Grabbable.HeldCount(false, false, false) > 0);
        }
    }
}
