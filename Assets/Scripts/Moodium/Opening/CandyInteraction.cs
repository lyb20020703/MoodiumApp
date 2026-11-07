using System;
using Moodium.Interaction;
using Unity.PolySpatial.InputDevices;
using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.InputSystem.LowLevel;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace Moodium.Opening
{
    public sealed class CandyInteraction : MonoBehaviour
    {
        public event Action Touched;
        bool m_Consumed;

        void OnEnable() => EnhancedTouchSupport.Enable();

        void Update()
        {
            if (m_Consumed)
                return;

            foreach (var touch in Touch.activeTouches)
            {
                var pointer = EnhancedSpatialPointerSupport.GetPointerState(touch);
                if (pointer.phase != SpatialPointerPhase.Began || pointer.targetObject == null)
                    continue;
                if (pointer.targetObject == gameObject || pointer.targetObject.transform.IsChildOf(transform))
                {
                    Consume();
                    return;
                }
            }
        }

        public void ResetInteraction() => m_Consumed = false;

        void Consume()
        {
            if (m_Consumed)
                return;
            m_Consumed = true;
            var burstPosition = GetVisualCenter();
            MoodiumInteractionVFXManager.PlayInteractionEffect(
                burstPosition,
                Quaternion.identity,
                gameObject);
            Debug.Log($"[Moodium Opening] Candy touch burst at {burstPosition}.");
            Touched?.Invoke();
        }

        Vector3 GetVisualCenter()
        {
            var renderers = GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return transform.position;
            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            return bounds.center;
        }
    }
}
