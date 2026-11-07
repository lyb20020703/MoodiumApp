using Unity.PolySpatial.InputDevices;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace Moodium.Opening
{
    /// <summary>
    /// Opening-video equivalent of the PolySpatial SpatialUI sample manager.
    /// Both indirect eye/pinch and direct hand touch arrive through the primary
    /// SpatialPointerDevice action and are dispatched to the target button.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OpeningVideoSpatialUIInputManager : MonoBehaviour
    {
        InputAction m_PrimaryTouch;

        void OnEnable()
        {
            EnhancedTouchSupport.Enable();
            EnsureEditorEventSystem();
            if (m_PrimaryTouch == null)
            {
                m_PrimaryTouch = new InputAction(
                    "Moodium Opening Primary Touch",
                    InputActionType.PassThrough,
                    "<SpatialPointerDevice>/spatialPointer0");
            }
            m_PrimaryTouch.Enable();
        }

        void OnDisable()
        {
            m_PrimaryTouch?.Disable();
        }

        void OnDestroy()
        {
            m_PrimaryTouch?.Dispose();
            m_PrimaryTouch = null;
        }

        void Update()
        {
            var activeTouches = Touch.activeTouches;
            if (activeTouches.Count == 0 || activeTouches[0].phase != TouchPhase.Began)
                return;

            var pointer = m_PrimaryTouch.ReadValue<SpatialPointerState>();
            var target = pointer.targetObject;
            if (target == null)
                return;

            var spatialButton = target.GetComponent<OpeningVideoSpatialButton>() ??
                                target.GetComponentInParent<OpeningVideoSpatialButton>();
            spatialButton?.PressFromSpatialPointer(activeTouches[0].touchId);
        }

        static void EnsureEditorEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null)
                return;

            var eventSystemObject = new GameObject("Moodium Opening EventSystem");
            eventSystemObject.AddComponent<EventSystem>();
            var inputModule = eventSystemObject.AddComponent<InputSystemUIInputModule>();
            inputModule.AssignDefaultActions();
        }
    }
}
