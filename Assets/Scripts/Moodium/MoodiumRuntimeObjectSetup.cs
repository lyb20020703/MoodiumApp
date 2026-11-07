using UnityEngine;
using Moodium.Interaction;

namespace Moodium.Flow
{
    public static class MoodiumRuntimeObjectSetup
    {
        public static MoodiumManipulable Configure(
            GameObject instance,
            bool allowMove,
            bool allowScale,
            bool disablePhysics)
        {
            if (instance == null)
                return null;

            if (disablePhysics)
            {
                foreach (var body in instance.GetComponentsInChildren<Rigidbody>(true))
                {
                    body.useGravity = false;
                    body.isKinematic = true;
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
            }

            EnsureCollider(instance);

            var manipulable = instance.GetComponent<MoodiumManipulable>();
            if (manipulable == null)
                manipulable = instance.AddComponent<MoodiumManipulable>();
            manipulable.Configure(allowMove, allowScale);

            if (disablePhysics && instance.GetComponent<MoodiumObjectInteraction>() == null)
                instance.AddComponent<MoodiumObjectInteraction>();
            if (disablePhysics && instance.GetComponent<SoftTouchFeedback>() == null)
                instance.AddComponent<SoftTouchFeedback>();
            return manipulable;
        }

        static void EnsureCollider(GameObject instance)
        {
            if (instance.GetComponentInChildren<Collider>(true) != null)
                return;

            if(!SoftModelBounds.TryGetLocalBounds(instance.transform,out var bounds))return;
            var collider = instance.AddComponent<BoxCollider>();
            collider.center = bounds.center;
            collider.size = bounds.size;
        }

        static float SafeDivide(float value, float divisor)
        {
            return Mathf.Abs(divisor) > 0.0001f ? value / Mathf.Abs(divisor) : value;
        }
    }
}
