using Moodium.Flow;
using Moodium.Interaction;
using UnityEngine;

namespace Moodium.Reality
{
    public static class TrackedCapsuleRuntimeFactory
    {
        public static GameObject Create(
            GameObject prefab,
            Transform anchor,
            Vector3 localPosition,
            Quaternion localRotation,
            string instanceName)
        {
            if (prefab == null || anchor == null)
                return null;

            var instance = Object.Instantiate(prefab);
            instance.name = instanceName;
            // Soft tracked-world props use the same runtime deformation as Creative Space.
            if (prefab.name.EndsWith("Soft", System.StringComparison.OrdinalIgnoreCase) &&
                instance.GetComponent<SoftTouchDeformationController>() == null)
                instance.AddComponent<SoftTouchDeformationController>();
            var manipulable = MoodiumRuntimeObjectSetup.Configure(
                instance,
                allowMove: false,
                allowScale: false,
                disablePhysics: true);
            manipulable?.SetInteractionState(MoodiumInteractionState.InteractionMode);

            var follower = instance.GetComponent<TrackedObjectPoseFollower>();
            if (follower == null)
                follower = instance.AddComponent<TrackedObjectPoseFollower>();
            follower.Configure(anchor, localPosition, localRotation);

            var interaction = instance.GetComponent<ChocolateCapsuleInteraction>();
            if (interaction == null)
                interaction = instance.AddComponent<ChocolateCapsuleInteraction>();
            interaction.SetInteractionEnabled(true);
            interaction.SetSpatialPointerEnabled(false);
            if (instance.GetComponent<TrackedCapsuleHandCollision>() == null)
                instance.AddComponent<TrackedCapsuleHandCollision>();
            return instance;
        }
    }
}
