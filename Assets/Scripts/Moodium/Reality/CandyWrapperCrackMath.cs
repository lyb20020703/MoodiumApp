using UnityEngine;

namespace Moodium.Reality
{
    /// <summary>Pure, allocation-free crack classification helpers kept separate from XR callbacks for testability.</summary>
    public static class CandyWrapperCrackMath
    {
        public readonly struct SurfaceLocalPose
        {
            public readonly Vector3 LocalPosition;
            public readonly Quaternion LocalRotation;

            public SurfaceLocalPose(Vector3 localPosition, Quaternion localRotation)
            {
                LocalPosition = localPosition;
                LocalRotation = localRotation;
            }
        }

        public static bool IsLargeCluster(int contactCount, bool hasPalm, int minimumJointCountForLarge)
            => hasPalm || contactCount >= Mathf.Max(1, minimumJointCountForLarge);

        public static bool IsDuplicateSpawn(
            Vector3 point, Vector3 lastPoint, float now, float lastSpawnTime,
            float minimumDistance, float minimumTime)
        {
            return now - lastSpawnTime < minimumTime &&
                   Vector3.Distance(point, lastPoint) < Mathf.Max(0f, minimumDistance);
        }

        public static float TowardSurfaceVelocity(Vector3 velocity, Vector3 surfaceNormal)
        {
            if (surfaceNormal.sqrMagnitude < 0.000001f)
                return 0f;
            return Mathf.Max(0f, Vector3.Dot(velocity, -surfaceNormal.normalized));
        }

        public static float Impact01(float towardSurfaceVelocity, float lowVelocity, float highVelocity)
        {
            return Mathf.InverseLerp(Mathf.Max(0f, lowVelocity), Mathf.Max(lowVelocity + .0001f, highVelocity),
                Mathf.Max(0f, towardSurfaceVelocity));
        }

        /// <summary>
        /// Starts just beyond the hand-side of a closest surface point and travels
        /// back through that point. Collider.Raycast then supplies the triangle's
        /// authoritative normal instead of treating the hand-to-surface vector as one.
        /// </summary>
        public static Ray BuildSurfaceNormalProbe(
            Vector3 surfacePoint, Vector3 pointOutsideSurface, float minimumOffset)
        {
            var outward = pointOutsideSurface - surfacePoint;
            if (outward.sqrMagnitude < .000001f)
                outward = Vector3.forward;
            outward.Normalize();
            var offset = Mathf.Max(Mathf.Max(0f, minimumOffset),
                Vector3.Distance(surfacePoint, pointOutsideSurface) + Mathf.Max(0f, minimumOffset));
            return new Ray(surfacePoint + outward * offset, -outward);
        }

        /// <summary>Converts an authoritative mesh hit into a pose that follows its mesh transform.</summary>
        public static SurfaceLocalPose ToSurfaceLocalPose(
            Transform surface, Vector3 point, Vector3 normal, float surfaceOffset, float roll)
        {
            if (surface == null)
                return new SurfaceLocalPose(
                    point + normal.normalized * surfaceOffset,
                    Quaternion.LookRotation(normal.normalized) * Quaternion.AngleAxis(roll, Vector3.forward));

            var localNormal = surface.InverseTransformDirection(normal).normalized;
            if (localNormal.sqrMagnitude < .000001f)
                localNormal = Vector3.forward;
            return new SurfaceLocalPose(
                surface.InverseTransformPoint(point + normal.normalized * surfaceOffset),
                Quaternion.LookRotation(localNormal) * Quaternion.AngleAxis(roll, Vector3.forward));
        }
    }
}
