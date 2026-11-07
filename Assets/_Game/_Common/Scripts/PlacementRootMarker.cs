using System.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace VFXViewer
{
    [RequireComponent(typeof(XRGrabInteractable))]
    [RequireComponent(typeof(Rigidbody))]
    public class PlacementRootMarker : MonoBehaviour
    {
        private static int s_ActiveGrabCount;

        [SerializeField] private RotationMode m_RotationMode = RotationMode.KeepUpright;

        private XRGrabInteractable m_Interactable;
        private Rigidbody m_Rigidbody;
        private ExhibitPlacementManager m_Spawner;
        private bool m_ManipulationEnabled = true;
        private bool m_PresentationVisible = true;
        private Coroutine m_PendingSaveCoroutine;
        private bool m_IsGrabbed;
        private Renderer[] m_PresentationRenderers;
        private Collider[] m_PresentationColliders;

        public static bool IsAnyGrabInProgress => s_ActiveGrabCount > 0;
        public bool IsPresentationVisible => m_PresentationVisible;

        private void Awake()
        {
            m_Interactable = GetComponent<XRGrabInteractable>();
            m_Rigidbody = GetComponent<Rigidbody>();
            m_Spawner = FindAnyObjectByType<ExhibitPlacementManager>();
            m_PresentationRenderers = GetComponentsInChildren<Renderer>(true);
            m_PresentationColliders = GetComponentsInChildren<Collider>(true);

            if (m_Interactable != null)
            {
                m_Interactable.useDynamicAttach = true;
                m_Interactable.movementType = XRBaseInteractable.MovementType.Instantaneous;
                m_Interactable.throwOnDetach = false;
                m_Interactable.trackRotation = m_RotationMode != RotationMode.Fixed;
            }

            if (m_Rigidbody != null)
            {
                m_Rigidbody.useGravity = false;
                m_Rigidbody.isKinematic = true;
            }

            ApplyPresentationState();
        }

        private void OnEnable()
        {
            if (m_Interactable == null)
                return;

            m_Interactable.selectEntered.AddListener(OnGrabStart);
            m_Interactable.selectExited.AddListener(OnGrabEnd);
            Application.onBeforeRender += HandleBeforeRender;
        }

        private void OnDisable()
        {
            if (m_Interactable == null)
                return;

            m_Interactable.selectEntered.RemoveListener(OnGrabStart);
            m_Interactable.selectExited.RemoveListener(OnGrabEnd);
            Application.onBeforeRender -= HandleBeforeRender;
            SetGrabbedState(false);
        }

        private void LateUpdate()
        {
            ApplyKeepUprightRotationIfNeeded();
        }

        private void HandleBeforeRender()
        {
            ApplyKeepUprightRotationIfNeeded();
        }

        private void ApplyKeepUprightRotationIfNeeded()
        {
            if (m_Interactable == null || !m_ManipulationEnabled)
                return;

            if (!m_Interactable.isSelected || m_RotationMode != RotationMode.KeepUpright)
                return;

            Vector3 currentEuler = transform.eulerAngles;
            transform.rotation = Quaternion.Euler(0f, currentEuler.y, 0f);
        }

        public void SetManipulationEnabled(bool enabled)
        {
            m_ManipulationEnabled = enabled;

            if (m_Interactable == null)
                m_Interactable = GetComponent<XRGrabInteractable>();
            if (m_Rigidbody == null)
                m_Rigidbody = GetComponent<Rigidbody>();

            ApplyPresentationState();
        }

        public void SetPresentationVisible(bool visible)
        {
            m_PresentationVisible = visible;

            if (m_Interactable == null)
                m_Interactable = GetComponent<XRGrabInteractable>();
            if (m_Rigidbody == null)
                m_Rigidbody = GetComponent<Rigidbody>();
            if (m_PresentationRenderers == null || m_PresentationRenderers.Length == 0)
                m_PresentationRenderers = GetComponentsInChildren<Renderer>(true);
            if (m_PresentationColliders == null || m_PresentationColliders.Length == 0)
                m_PresentationColliders = GetComponentsInChildren<Collider>(true);

            ApplyPresentationState();
        }

        private void ApplyPresentationState()
        {
            bool interactionEnabled = m_ManipulationEnabled && m_PresentationVisible;

            if (m_Interactable != null)
                m_Interactable.enabled = interactionEnabled;

            if (m_Rigidbody != null && !interactionEnabled)
                m_Rigidbody.isKinematic = true;

            if (m_PresentationRenderers != null)
            {
                for (int i = 0; i < m_PresentationRenderers.Length; i++)
                {
                    Renderer renderer = m_PresentationRenderers[i];
                    if (renderer != null)
                        renderer.enabled = m_PresentationVisible;
                }
            }

            if (m_PresentationColliders != null)
            {
                for (int i = 0; i < m_PresentationColliders.Length; i++)
                {
                    Collider collider = m_PresentationColliders[i];
                    if (collider != null)
                        collider.enabled = m_PresentationVisible;
                }
            }
        }

        private void OnGrabStart(SelectEnterEventArgs args)
        {
            if (!m_ManipulationEnabled)
                return;

            SetGrabbedState(true);
            m_Spawner?.TryDetachRootFromCurrentAnchor(transform);

            if (m_Rigidbody != null)
                m_Rigidbody.isKinematic = true;

            m_Spawner?.NotifyRootMarkerGrabStarted(transform);
        }

        private void OnGrabEnd(SelectExitEventArgs args)
        {
            if (!m_ManipulationEnabled)
                return;

            SetGrabbedState(false);

            if (m_PendingSaveCoroutine != null)
                StopCoroutine(m_PendingSaveCoroutine);

            m_PendingSaveCoroutine = StartCoroutine(SaveRootAfterReleaseSettles());
        }

        private IEnumerator SaveRootAfterReleaseSettles()
        {
            yield return null;
            yield return new WaitForEndOfFrame();

            if (m_RotationMode == RotationMode.KeepUpright)
            {
                Vector3 currentEuler = transform.eulerAngles;
                transform.rotation = Quaternion.Euler(0f, currentEuler.y, 0f);
            }

            if (m_Rigidbody != null)
                m_Rigidbody.isKinematic = true;

            m_Spawner?.NotifyRootMarkerReleased(transform);
            m_PendingSaveCoroutine = null;
        }

        private void OnDestroy()
        {
            SetGrabbedState(false);
        }

        private void SetGrabbedState(bool grabbed)
        {
            if (m_IsGrabbed == grabbed)
                return;

            m_IsGrabbed = grabbed;
            if (grabbed)
                s_ActiveGrabCount++;
            else if (s_ActiveGrabCount > 0)
                s_ActiveGrabCount--;
        }
    }

}
