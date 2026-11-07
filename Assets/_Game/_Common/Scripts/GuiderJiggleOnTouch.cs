using FIMSpace.Jiggling;
using Interaction;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace VFXViewer
{
    [RequireComponent(typeof(XRSimpleInteractable))]
    [RequireComponent(typeof(Collider))]
    public class GuiderJiggleOnTouch : MonoBehaviour
    {
        [SerializeField] private FJiggling_Base jiggleTarget;
        [SerializeField] private float powerMultiplier = 1f;
        [SerializeField] private bool triggerOnHover = true;
        [SerializeField] private bool triggerOnSelect = true;
        [SerializeField] private float cooldown = 0.1f;

        private XRBaseInteractable _interactable;
        private float _nextAllowedTime;

        private void Awake()
        {
            _interactable = GetComponent<XRBaseInteractable>();
            if (jiggleTarget == null)
                jiggleTarget = GetComponentInChildren<FJiggling_Base>(true);

            // Poke filter is required for Vision Pro hover (hand poke), but it can block
            // editor mouse hover workflows, so only auto-add it on visionOS runtime.
            if (triggerOnHover && _interactable != null && Application.platform == RuntimePlatform.VisionOS)
                VisionProHoverHelper.EnsurePokeFilterForVisionProHover(_interactable);
        }

        private void OnEnable()
        {
            if (_interactable == null)
                _interactable = GetComponent<XRBaseInteractable>();

            if (_interactable == null)
                return;

            if (triggerOnHover)
                _interactable.hoverEntered.AddListener(OnHoverEntered);

            if (triggerOnSelect)
                _interactable.selectEntered.AddListener(OnSelectEntered);
        }

        private void OnDisable()
        {
            if (_interactable == null)
                return;

            _interactable.hoverEntered.RemoveListener(OnHoverEntered);
            _interactable.selectEntered.RemoveListener(OnSelectEntered);
        }

        private void OnHoverEntered(HoverEnterEventArgs args)
        {
            TriggerJiggle();
        }

        private void OnSelectEntered(SelectEnterEventArgs args)
        {
            TriggerJiggle();
        }

        private void TriggerJiggle()
        {
            if (jiggleTarget == null)
                jiggleTarget = GetComponentInChildren<FJiggling_Base>(true);

            if (jiggleTarget == null)
                return;

            if (cooldown > 0f && Time.unscaledTime < _nextAllowedTime)
                return;

            _nextAllowedTime = Time.unscaledTime + cooldown;
            jiggleTarget.StartJiggle(powerMultiplier);
        }
    }
}
