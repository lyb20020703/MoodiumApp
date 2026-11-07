using System.Collections.Generic;
using UnityEngine;

namespace Moodium.NatureWorld
{
    /// <summary>Finds a tabletop-like hit among colliders that belong to the real spatial mesh.</summary>
    public sealed class NatureIvySurfaceResolver
    {
        readonly ICollection<Collider> m_SpatialColliders;
        readonly float m_MinUpNormalDot;
        readonly float m_MaxDistance;
        readonly float m_SampleRadius;
        readonly int m_SampleCount;

        public NatureIvySurfaceResolver(ICollection<Collider> spatialColliders,
            float minUpNormalDot, float maxDistance, float sampleRadius, int sampleCount)
        {
            m_SpatialColliders = spatialColliders;
            m_MinUpNormalDot = Mathf.Clamp01(minUpNormalDot);
            m_MaxDistance = Mathf.Max(.01f, maxDistance);
            m_SampleRadius = Mathf.Max(0f, sampleRadius);
            m_SampleCount = Mathf.Max(0, sampleCount);
        }

        public bool TryFindSurfaceBelow(Vector3 origin, out RaycastHit bestHit)
        {
            bestHit = default;
            var found = TryFindAt(origin, out bestHit);
            var bestDistance = found ? bestHit.distance : float.PositiveInfinity;
            for (var i = 0; i < m_SampleCount; i++)
            {
                var angle = Mathf.PI * 2f * i / m_SampleCount;
                var offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * m_SampleRadius;
                if (!TryFindAt(origin + offset, out var candidate) || candidate.distance >= bestDistance)
                    continue;
                bestHit = candidate;
                bestDistance = candidate.distance;
                found = true;
            }
            return found;
        }

        // Query only owned room colliders, so virtual objects cannot occlude the probe.
        public static bool TryRaycast(IEnumerable<Collider> colliders, Ray ray, float distance, out RaycastHit bestHit)
        {
            bestHit = default;
            if (colliders == null || ray.direction.sqrMagnitude < .000001f) return false;
            var found = false;
            foreach (var collider in colliders)
            {
                if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy || collider.isTrigger)
                    continue;
                if (!collider.Raycast(ray, out var hit, distance)) continue;
                distance = hit.distance;
                bestHit = hit;
                found = true;
            }
            return found;
        }

        bool TryFindAt(Vector3 origin, out RaycastHit bestHit)
        {
            bestHit = default;
            var found = false;
            var bestDistance = float.PositiveInfinity;
            var hits = Physics.RaycastAll(origin, Vector3.down, m_MaxDistance,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            foreach (var hit in hits)
            {
                if (hit.collider == null || m_SpatialColliders == null || !m_SpatialColliders.Contains(hit.collider) ||
                    Vector3.Dot(hit.normal, Vector3.up) < m_MinUpNormalDot || hit.distance >= bestDistance)
                    continue;
                bestHit = hit;
                bestDistance = hit.distance;
                found = true;
            }
            return found;
        }
    }
}
