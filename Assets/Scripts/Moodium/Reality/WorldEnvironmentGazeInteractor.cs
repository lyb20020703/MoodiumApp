using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace Moodium.Reality
{
    /// <summary>
    /// A deliberately forgiving Vision Pro path for the late-world environmental
    /// interactions: look at scanned room geometry and pinch once. It observes the
    /// existing AR mesh colliders and does not create another tracking system.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorldEnvironmentGazeInteractor : MonoBehaviour
    {
        const float MaxRayDistance = 7.5f;
        const float RefreshInterval = 1f;

        readonly HashSet<Collider> m_SpatialColliders = new();
        Camera m_Camera;
        ARMeshManager m_MeshManager;
        Func<RaycastHit, bool> m_Activate;
        bool m_Enabled;
        float m_LastRefresh;
        float m_LastActivation = -100f;
        float m_LastDiagnostic;
        RaycastHit m_CurrentHit;
        bool m_HasCurrentHit;

        public void Configure(Camera camera, ARMeshManager meshManager, Func<RaycastHit, bool> activate)
        {
            m_Camera = camera;
            m_MeshManager = meshManager;
            m_Activate = activate;
            RefreshSpatialColliders();
            SetInteractionEnabled(false);
        }

        public void SetInteractionEnabled(bool enabled)
        {
            m_Enabled = enabled;
            if (!enabled)
                m_HasCurrentHit = false;
        }

        public bool TryActivateFromSpatialPointer(Collider targetCollider, Vector3 interactionPosition)
        {
            if (!m_Enabled || !WorldEnvironmentGazePolicy.CanActivate(1f, Time.unscaledTime, m_LastActivation))
                return false;
            if (Time.unscaledTime - m_LastRefresh > RefreshInterval)
                RefreshSpatialColliders();

            if (!TryResolvePointerHit(targetCollider, interactionPosition, out var hit) &&
                !TryGetCurrentHit(out hit))
                return false;
            var activated = m_Activate != null && m_Activate(hit);
            if (activated)
                m_LastActivation = Time.unscaledTime;
            return activated;
        }

        void Update()
        {
            if (!m_Enabled)
                return;
            if (Time.unscaledTime - m_LastRefresh > RefreshInterval)
                RefreshSpatialColliders();
            m_HasCurrentHit = TryGetCurrentHit(out m_CurrentHit);
        }

        bool TryGetCurrentHit(out RaycastHit hit)
        {
            hit = default;
            if (m_Camera == null)
                m_Camera = Camera.main;
            if (m_Camera == null)
                return false;
            var ray = new Ray(m_Camera.transform.position, m_Camera.transform.forward);
            var hits = Physics.RaycastAll(ray, MaxRayDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
            foreach (var candidate in hits)
            {
                if (candidate.collider != null && IsSpatialCollider(candidate.collider))
                {
                    hit = candidate;
                    return true;
                }
            }
            return false;
        }

        bool TryResolvePointerHit(Collider targetCollider, Vector3 interactionPosition, out RaycastHit hit)
        {
            hit = default;
            if (targetCollider == null || !IsSpatialCollider(targetCollider))
                return false;
            if (m_Camera == null)
                m_Camera = Camera.main;
            if (m_Camera == null)
                return false;
            var direction = interactionPosition - m_Camera.transform.position;
            if (direction.sqrMagnitude < .0001f)
                return false;
            var ray = new Ray(m_Camera.transform.position, direction.normalized);
            return targetCollider.Raycast(ray, out hit, Mathf.Min(direction.magnitude + .2f, MaxRayDistance));
        }

        void RefreshSpatialColliders()
        {
            m_LastRefresh = Time.unscaledTime;
            m_SpatialColliders.Clear();
            if (m_MeshManager != null)
                foreach (var collider in m_MeshManager.GetComponentsInChildren<Collider>(true))
                    m_SpatialColliders.Add(collider);

            // On visionOS the AR mesh children can be created after the manager's
            // first frame. Include live MeshColliders as a fallback so the gaze ray
            // does not permanently retain an empty/stale cache.
            if (m_SpatialColliders.Count == 0)
                foreach (var collider in FindObjectsByType<MeshCollider>(
                             FindObjectsInactive.Include, FindObjectsSortMode.None))
                    m_SpatialColliders.Add(collider);

            if (m_Enabled && m_SpatialColliders.Count == 0 &&
                Time.unscaledTime - m_LastDiagnostic > 2f)
            {
                m_LastDiagnostic = Time.unscaledTime;
                Debug.LogWarning("[Environment Gaze] No spatial mesh colliders are available yet; retrying.");
            }
        }

        bool IsSpatialCollider(Collider collider)
        {
            if (collider == null)
                return false;
            if (m_SpatialColliders.Contains(collider))
                return true;
            return m_MeshManager != null &&
                   collider.GetComponentInParent<ARMeshManager>() == m_MeshManager;
        }

    }

    public static class WorldEnvironmentGazePolicy
    {
        public static bool ShouldEnable(float normalizedProgress) => normalizedProgress >= 1f;

        public static bool CanActivate(float normalizedProgress, float now, float lastActivation) =>
            ShouldEnable(normalizedProgress) && now - lastActivation >= .3f;
    }
}
