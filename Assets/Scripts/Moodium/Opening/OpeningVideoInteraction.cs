using Unity.PolySpatial.InputDevices;
using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.InputSystem.LowLevel;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace Moodium.Opening
{
    /// <summary>Consumes a direct spatial pinch/click on the interactive video surface.</summary>
    [DisallowMultipleComponent]
    public sealed class OpeningVideoInteraction : MonoBehaviour
    {
        public bool Activated { get; private set; }

        void OnEnable() => EnhancedTouchSupport.Enable();

        void Update()
        {
            if (Activated)
                return;

            foreach (var touch in Touch.activeTouches)
            {
                var pointer = EnhancedSpatialPointerSupport.GetPointerState(touch);
                if (pointer.phase != SpatialPointerPhase.Began || pointer.targetObject == null)
                    continue;
                if (pointer.targetObject == gameObject || pointer.targetObject.transform.IsChildOf(transform))
                {
                    Activated = true;
                    Debug.Log("[Moodium Opening] Intro video activated by user gesture.");
                    return;
                }
            }
        }
    }
}
