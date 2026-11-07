using UnityEngine;

namespace Moodium.Opening
{
    /// <summary>Applies the shared Medium Opening Video placement to runtime video surfaces.</summary>
    public static class OpeningVideoDisplayPlacement
    {
        public static void Apply(
            Transform display,
            Camera camera,
            OpeningVideoSequenceConfig sequence,
            float fallbackDistance = 1.35f,
            float fallbackHeightOffset = -0.05f)
        {
            if (display == null || camera == null)
                return;

            var viewForward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized;
            if (viewForward.sqrMagnitude < 0.01f)
                viewForward = camera.transform.forward.normalized;
            var videoDistance = sequence != null ? sequence.DistanceFromUser : fallbackDistance;
            var videoHeight = sequence != null ? sequence.HeightOffset : fallbackHeightOffset;
            var videoPosition = camera.transform.position + viewForward * videoDistance + Vector3.up * videoHeight;
            if (sequence != null)
                videoPosition.y = sequence.WorldPositionY;
            display.position = videoPosition;
            // The surface must stay parallel to a wall even if the user looks up or
            // down. Only use the camera's horizontal heading, not its pitch.
            display.rotation = BuildUprightFacingRotation(camera.transform.forward);
            display.localScale = new Vector3(1.28f, 0.72f, 1f);
        }

        /// <summary>Builds a world-upright video rotation from a viewer's heading.</summary>
        public static Quaternion BuildUprightFacingRotation(Vector3 viewerForward)
        {
            var horizontalForward = Vector3.ProjectOnPlane(viewerForward, Vector3.up);
            if (horizontalForward.sqrMagnitude < 0.01f)
                horizontalForward = Vector3.forward;
            return Quaternion.LookRotation(horizontalForward.normalized, Vector3.up);
        }
    }
}
