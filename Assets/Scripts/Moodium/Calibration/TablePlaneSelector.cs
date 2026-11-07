using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;

namespace Moodium.Calibration
{
    /// <summary>Deterministic, subsystem-independent tabletop selection in world metres.</summary>
    public static class TablePlaneSelector
    {
        public sealed class Candidate
        {
            public Pose Pose;
            public PlaneAlignment Alignment = PlaneAlignment.HorizontalUp;
            public PlaneClassifications Classifications;
            public Vector2 HalfSize = new(.5f, .5f);
            public Vector2[] Boundary;
        }

        public struct Settings
        {
            public float HalfFov, MaxDistance, MinDrop, MaxDrop, ForwardDistance, FallbackDrop, AnchorLift;
            public static Settings Default => new()
            {
                HalfFov = 40, MaxDistance = 1.5f, MinDrop = .2f, MaxDrop = 1,
                ForwardDistance = .5f, FallbackDrop = .45f, AnchorLift = .05f
            };
        }

        public static Vector3 Forward(Pose viewer)
        {
            var forward = Vector3.ProjectOnPlane(viewer.rotation * Vector3.forward, Vector3.up);
            return forward.sqrMagnitude > .00001f ? forward.normalized : Vector3.forward;
        }

        public static Pose Fallback(Pose viewer, Settings settings) => new(
            viewer.position + Forward(viewer) * settings.ForwardDistance - Vector3.up * settings.FallbackDrop,
            Quaternion.LookRotation(Forward(viewer), Vector3.up));

        public static Pose AnchorPose(Pose table, float lift) => new(table.position + Vector3.up * lift, table.rotation);

        public static bool TrySelect(IReadOnlyList<Candidate> candidates, Pose viewer, Settings settings, out Pose table)
        {
            table = Fallback(viewer, settings);
            var forward = Forward(viewer);
            var target = viewer.position + forward * settings.ForwardDistance;
            var bestTable = false;
            var bestDistance = float.PositiveInfinity;
            var found = false;
            foreach (var candidate in candidates)
            {
                if (candidate == null || candidate.Alignment != PlaneAlignment.HorizontalUp ||
                    Vector3.Dot(candidate.Pose.rotation * Vector3.up, Vector3.up) < .95f) continue;
                var classification = candidate.Classifications;
                if ((classification & PlaneClassifications.Floor) != 0) continue;
                var isTable = (classification & PlaneClassifications.Table) != 0;
                if (!isTable && classification != PlaneClassifications.None) continue;
                var local = Quaternion.Inverse(candidate.Pose.rotation) * (target - candidate.Pose.position);
                var point = ClampToBoundary(new Vector2(local.x, local.z), candidate.Boundary, candidate.HalfSize);
                var world = candidate.Pose.position + candidate.Pose.rotation * new Vector3(point.x, 0, point.y);
                var drop = viewer.position.y - world.y;
                var horizontal = Vector3.ProjectOnPlane(world - viewer.position, Vector3.up);
                if (drop < settings.MinDrop || drop > settings.MaxDrop || horizontal.magnitude > settings.MaxDistance ||
                    Vector3.Angle(forward, horizontal) > settings.HalfFov) continue;
                var distance = (world - target).sqrMagnitude;
                if (found && (bestTable && !isTable || bestTable == isTable && distance >= bestDistance)) continue;
                bestTable = isTable; bestDistance = distance; found = true;
                // The tabletop model frame follows the viewer's horizontal forward.
                // Facing UI toward the viewer here flipped the entire Soft model,
                // including its video and hand prompts, by 180 degrees.
                table = new Pose(world, Quaternion.LookRotation(forward, Vector3.up));
            }
            return found;
        }

        public static Vector2 ClampToBoundary(Vector2 point, Vector2[] boundary, Vector2 halfSize)
        {
            if (boundary == null || boundary.Length < 3)
                return new Vector2(Mathf.Clamp(point.x, -halfSize.x, halfSize.x), Mathf.Clamp(point.y, -halfSize.y, halfSize.y));
            var inside = false;
            var nearest = point;
            var distance = float.PositiveInfinity;
            for (int i = 0, j = boundary.Length - 1; i < boundary.Length; j = i++)
            {
                var a = boundary[j]; var b = boundary[i];
                if ((a.y > point.y) != (b.y > point.y) && point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
                var edge = b - a;
                var t = edge.sqrMagnitude > 1e-10f ? Mathf.Clamp01(Vector2.Dot(point - a, edge) / edge.sqrMagnitude) : 0;
                var candidate = a + edge * t;
                var d = (candidate - point).sqrMagnitude;
                if (d < distance) { distance = d; nearest = candidate; }
            }
            return inside ? point : nearest;
        }
    }
}
