using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace VFXViewer
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(GrabLifecycleRelay))]
    public sealed class XriGrabAdapter : MonoBehaviour
    {
        private GrabLifecycleRelay m_Relay;
        private XRBaseInteractable m_Interactable;
        private int m_LocalGrabCount;

        private void Awake()
        {
            m_Relay = GetComponent<GrabLifecycleRelay>();
            m_Interactable = GetComponent<XRBaseInteractable>();
        }

        private void OnEnable()
        {
            if (m_Interactable == null)
                m_Interactable = GetComponent<XRBaseInteractable>();

            if (m_Interactable == null)
                return;

            m_Interactable.selectEntered.AddListener(OnSelectEntered);
            m_Interactable.selectExited.AddListener(OnSelectExited);
        }

        private void OnDisable()
        {
            if (m_Interactable != null)
            {
                m_Interactable.selectEntered.RemoveListener(OnSelectEntered);
                m_Interactable.selectExited.RemoveListener(OnSelectExited);
            }

            while (m_LocalGrabCount > 0)
            {
                m_Relay?.NotifyGrabEnded();
                m_LocalGrabCount--;
            }
        }

        private void OnSelectEntered(SelectEnterEventArgs args)
        {
            m_LocalGrabCount++;
            m_Relay?.NotifyGrabStarted();
        }

        private void OnSelectExited(SelectExitEventArgs args)
        {
            if (m_LocalGrabCount <= 0)
                return;

            m_LocalGrabCount--;
            m_Relay?.NotifyGrabEnded();
        }
    }
}
