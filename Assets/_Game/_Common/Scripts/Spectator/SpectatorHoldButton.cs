using UnityEngine;
using UnityEngine.EventSystems;

namespace VFXViewer
{
    public sealed class SpectatorHoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public enum HoldAction
        {
            RotateLeft,
            Forward,
            RotateRight,
            Left,
            Back,
            Right,
            Down,
            Up,
        }

        [SerializeField] private HoldAction m_Action;

        private SpectatorConnectPanel m_Panel;
        private bool m_IsPressed;

        private void Awake()
        {
            m_Panel = GetComponentInParent<SpectatorConnectPanel>(true);
        }

        public void Configure(SpectatorConnectPanel panel, HoldAction action)
        {
            m_Panel = panel;
            m_Action = action;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (m_Panel == null)
                m_Panel = GetComponentInParent<SpectatorConnectPanel>(true);

            m_IsPressed = true;
            m_Panel?.BeginHeldRootAdjustment(m_Action);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            Release();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            Release();
        }

        private void OnDisable()
        {
            Release();
        }

        private void Release()
        {
            if (!m_IsPressed)
                return;

            m_IsPressed = false;
            m_Panel?.EndHeldRootAdjustment(m_Action);
        }
    }
}
