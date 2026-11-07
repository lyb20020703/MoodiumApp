using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace Moodium.NatureWorld
{
    /// <summary>
    /// Grows a bounded trail of grass on the real floor beneath a walking Nature World player.
    /// Grass is deliberately scene-rooted so it is not affected by a tracked-image transform.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NatureFootstepGrassController : MonoBehaviour
    {
        public void RegisterRoomVisibility(Moodium.Flow.RoomContentVisibility visibility) { if (m_GrassRoot != null) foreach (Transform child in m_GrassRoot) visibility.Register(child.gameObject); }

        const float CompletedProgress = .999f;
        static readonly Vector2[] GroundProbeOffsets =
        {
            Vector2.zero,
            new(.25f, 0f), new(-.25f, 0f),
            new(0f, .25f), new(0f, -.25f)
        };

        [Header("Footstep placement")]
        [SerializeField, Min(.05f)] float m_DistanceBetweenGrass = .3f;
        [SerializeField, Min(.01f)] float m_MinimumGrassSeparation = .22f;
        [SerializeField, Min(1)] int m_MaxActiveGrass = 45;
        [SerializeField, Range(0f, 1f)] float m_MinGroundNormalY = .55f;
        [SerializeField, Min(0f)] float m_SurfaceOffset = .005f;
        [SerializeField, Min(.01f)] float m_GrassScaleMultiplier = 1.55f;

        readonly List<GameObject> m_SpawnedGrass = new();
        Transform m_Viewer;
        ARMeshManager m_SpatialMeshManager;
        GameObject m_GrassPrefab;
        GameObject m_AlternateGrassPrefab;
        Transform m_GrassRoot;
        Vector3 m_LastGrassPosition;
        float m_Progress;
        bool m_NatureWorldActive;
        bool m_CandyWorldActive;
        bool m_HasLastGrassPosition;
        bool m_HasLoggedMissingGround;
        bool m_Managed;
        bool m_WalkingEnabled;
        int m_TotalSpawned;
        Vector3 m_WalkDirection;
        Vector3 m_PreviousHead;

        public event System.Action<Vector3> StepGrown;
        public bool IsCandy => m_CandyWorldActive;
        public Transform Viewer => m_Viewer;
        public GameObject PrimaryPrefab => m_GrassPrefab;
        public GameObject FlowerPrefab => m_AlternateGrassPrefab != null ? m_AlternateGrassPrefab : m_GrassPrefab;
        public void SetGuidedMode(bool managed) { m_Managed = managed; m_WalkingEnabled = false; }
        public void SetWalkingEnabled(bool enabled)
        {
            if (m_WalkingEnabled == enabled) return;
            m_WalkingEnabled = enabled;
            m_HasLastGrassPosition = false;
        }

        public int SpawnedGrassCount => m_SpawnedGrass.Count;

        public static bool ShouldBeActive(bool natureWorldActive, float normalizedProgress) =>
            natureWorldActive && normalizedProgress >= CompletedProgress;

        public static bool ShouldBeActiveForCandy(bool candyWorldActive, float normalizedProgress) =>
            candyWorldActive && normalizedProgress >= CompletedProgress;

        public static bool HasMinimumHorizontalSeparation(Vector3 candidate, IEnumerable<Vector3> existingPositions,
            float minimumDistance)
        {
            candidate.y = 0f;
            foreach (var existing in existingPositions)
            {
                var horizontalExisting = existing;
                horizontalExisting.y = 0f;
                if (Vector3.Distance(candidate, horizontalExisting) < minimumDistance)
                    return false;
            }
            return true;
        }

        public static int GetInstancesToRecycle(int currentCount, int maximumCount) =>
            Mathf.Max(0, currentCount - Mathf.Max(1, maximumCount) + 1);

        public static Vector2[] GetGroundProbeOffsets() => GroundProbeOffsets;

        public static GameObject SelectFootstepPrefab(GameObject primary, GameObject alternate, int spawnIndex)
        {
            if (alternate == null || spawnIndex % 2 == 0)
                return primary;
            return alternate;
        }

        public static bool IsGeneratedSpatialMeshCollider(Collider collider,
            IEnumerable<MeshFilter> generatedMeshes)
        {
            if (collider is not MeshCollider || generatedMeshes == null)
                return false;

            foreach (var meshFilter in generatedMeshes)
            {
                if (meshFilter == null)
                    continue;
                if (collider.transform == meshFilter.transform ||
                    collider.transform.IsChildOf(meshFilter.transform))
                    return true;
            }
            return false;
        }

        public void Configure(Transform viewer, ARMeshManager spatialMeshManager, GameObject grassPrefab)
        {
            Configure(viewer, spatialMeshManager, grassPrefab, null);
        }

        public void Configure(Transform viewer, ARMeshManager spatialMeshManager, GameObject grassPrefab,
            GameObject alternateGrassPrefab)
        {
            m_CandyWorldActive = false;
            m_Viewer = viewer;
            m_SpatialMeshManager = spatialMeshManager;
            m_GrassPrefab = grassPrefab;
            m_AlternateGrassPrefab = alternateGrassPrefab;
        }

        public void ConfigureCandy(Transform viewer, ARMeshManager spatialMeshManager, GameObject beanPrefab)
        {
            m_CandyWorldActive = true;
            m_Viewer = viewer;
            m_SpatialMeshManager = spatialMeshManager;
            m_GrassPrefab = beanPrefab;
            m_AlternateGrassPrefab = null;
        }

        public void StartTracking()
        {
            var candyWorld = m_CandyWorldActive;
            StopAndClear();
            m_CandyWorldActive = candyWorld;
            m_NatureWorldActive = !candyWorld;
            m_Progress = 0f;
            m_TotalSpawned = 0;
            if (m_Viewer != null)
            {
                m_PreviousHead = m_Viewer.position;
                m_WalkDirection = Vector3.ProjectOnPlane(m_Viewer.forward, Vector3.up).normalized;
            }
        }

        public void SetProgress(float normalizedProgress) => m_Progress = Mathf.Clamp01(normalizedProgress);

        public void StopAndClear()
        {
            m_NatureWorldActive = false;
            m_CandyWorldActive = false;
            m_Progress = 0f;
            m_WalkingEnabled = false;
            m_HasLastGrassPosition = false;
            m_HasLoggedMissingGround = false;
            foreach (var grass in m_SpawnedGrass)
                if (grass != null)
                    Destroy(grass);
            m_SpawnedGrass.Clear();
            if (m_GrassRoot != null)
                Destroy(m_GrassRoot.gameObject);
            m_GrassRoot = null;
        }

        bool m_Suspended;
        public void SetSuspended(bool value) => m_Suspended = value;
        void Update()
        {
            if (m_Suspended) return;
            if ((!ShouldBeActive(m_NatureWorldActive, m_Progress) &&
                 !ShouldBeActiveForCandy(m_CandyWorldActive, m_Progress)) ||
                m_Viewer == null || (m_GrassPrefab == null && m_AlternateGrassPrefab == null) ||
                m_SpatialMeshManager == null)
                return;
            if (m_Managed && !m_WalkingEnabled) return;
            var motion = Vector3.ProjectOnPlane(m_Viewer.position - m_PreviousHead, Vector3.up);
            m_PreviousHead = m_Viewer.position;
            if (motion.magnitude > .001f)
                m_WalkDirection = Vector3.Slerp(m_WalkDirection, motion.normalized,
                    1f - Mathf.Exp(-Time.deltaTime / .5f)).normalized;
            if (m_HasLastGrassPosition && HorizontalDistance(m_Viewer.position, m_LastGrassPosition) < m_DistanceBetweenGrass)
                return;

            if (!TryFindGroundBelowViewer(out var groundHit))
            {
                if (!m_HasLoggedMissingGround)
                {
                    Debug.LogWarning("[Nature Footstep Grass] Progress is full, but no upward-facing AR spatial mesh was found beneath the player. Waiting for floor meshing.");
                    m_HasLoggedMissingGround = true;
                }
                return;
            }

            m_HasLoggedMissingGround = false;

            var groundPosition = groundHit.point + groundHit.normal * m_SurfaceOffset;
            if (!m_HasLastGrassPosition)
            {
                // Reaching 100% does not place a grass clump under a stationary user.
                m_LastGrassPosition = m_Viewer.position;
                m_HasLastGrassPosition = true;
                return;
            }

            if (HorizontalDistance(m_Viewer.position, m_LastGrassPosition) < m_DistanceBetweenGrass)
                return;

            m_LastGrassPosition = m_Viewer.position;
            var direction = m_WalkDirection.sqrMagnitude > .1f ? m_WalkDirection
                : Vector3.ProjectOnPlane(m_Viewer.forward, Vector3.up).normalized;
            var ahead = m_Viewer.position + direction * 1.2f +
                Vector3.Cross(Vector3.up, direction) * (m_TotalSpawned % 2 == 0 ? .12f : -.12f);
            if (!TryFindSafeGround(ahead, groundHit.point.y, out groundPosition)) return;
            if (!HasMinimumHorizontalSeparation(groundPosition, GetActivePositions(), m_MinimumGrassSeparation))
                return;

            SpawnGrass(groundPosition);
            StepGrown?.Invoke(groundPosition);
        }

        bool TryFindGroundBelowViewer(out RaycastHit groundHit)
        {
            // Use the same generous probe range as NatureForestGrowthController.
            // A direct point beneath a Vision Pro wearer can fall in a reconstruction
            // gap, so sample a small cross around the foot position before giving up.
            var viewerPosition = m_Viewer.position;
            var hasHit = false;
            var lowestHeight = float.PositiveInfinity;
            groundHit = default;
            foreach (var offset in GroundProbeOffsets)
            {
                var origin = new Vector3(viewerPosition.x + offset.x, viewerPosition.y + 4f,
                    viewerPosition.z + offset.y);
                var hits = Physics.RaycastAll(origin, Vector3.down, 8f, Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore);
                foreach (var hit in hits)
                {
                    if (!IsSpatialGround(hit.collider, hit.normal) || hit.normal.y < .9f)
                        continue;
                    // A tracking origin can be device-relative, not floor-relative.
                    // Select the lowest reconstructed horizontal surface below the
                    // wearer, not the first (possibly tabletop) ray hit or origin Y.
                    var belowHead = viewerPosition.y - hit.point.y;
                    if (belowHead < .9f || belowHead > 2.5f || hit.point.y >= lowestHeight) continue;
                    groundHit = hit;
                    lowestHeight = hit.point.y;
                    hasHit = true;
                }
            }
            return hasHit;
        }

        bool IsSpatialGround(Collider collider, Vector3 normal) =>
            normal.y >= m_MinGroundNormalY && m_SpatialMeshManager != null &&
            IsGeneratedSpatialMeshCollider(collider, m_SpatialMeshManager.meshes);

        IEnumerable<Vector3> GetActivePositions()
        {
            foreach (var grass in m_SpawnedGrass)
                if (grass != null)
                    yield return grass.transform.position;
        }

        public GameObject SpawnGrass(Vector3 position, bool garden = false, GameObject overridePrefab = null)
        {
            if (m_Suspended) return null;
            if (m_GrassRoot == null)
                m_GrassRoot = new GameObject(m_CandyWorldActive ? "Candy Footstep Beans" : "Nature Footstep Grass").transform;

            var recycleCount = garden ? 0 : GetInstancesToRecycle(m_SpawnedGrass.Count, m_MaxActiveGrass);
            for (var i = 0; i < recycleCount; i++)
            {
                var oldest = m_SpawnedGrass[0];
                m_SpawnedGrass.RemoveAt(0);
                if (oldest != null)
                {
                    var growth = oldest.GetComponent<WalkTrailGrowth>();
                    if (growth != null) growth.Retire(); else Destroy(oldest);
                }
            }

            var rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            var prefab = overridePrefab != null ? overridePrefab : SelectFootstepPrefab(m_GrassPrefab, m_AlternateGrassPrefab, m_TotalSpawned);
            if (prefab == null)
                return null;
            var grass = Instantiate(prefab, position, rotation, m_GrassRoot);
            grass.name = $"{prefab.name} (Footstep Grass {m_SpawnedGrass.Count + 1})";
            grass.transform.localScale *= m_GrassScaleMultiplier;
            AlignVisualBaseToSurface(grass, position.y);
            if (!garden) { m_SpawnedGrass.Add(grass); m_TotalSpawned++; }
            grass.AddComponent<WalkTrailGrowth>().Initialize(position);
            return grass;
        }

        public bool TryGetFloor(out Vector3 floor)
        {
            floor = default;
            if (m_Viewer == null || m_SpatialMeshManager == null || !TryFindGroundBelowViewer(out var hit)) return false;
            floor = hit.point;
            return true;
        }

        public bool TryFindSafeGround(Vector3 desired, float floorY, out Vector3 ground, bool clearance = false)
        {
            ground = default;
            if (m_SpatialMeshManager == null) return false;
            var hits = Physics.RaycastAll(new Vector3(desired.x, floorY + .3f, desired.z), Vector3.down,
                .6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            var found = false;
            foreach (var hit in hits)
                if (IsSpatialGround(hit.collider, hit.normal) && hit.normal.y >= .9f &&
                    Mathf.Abs(hit.point.y - floorY) <= .08f)
                { ground = hit.point + Vector3.up * m_SurfaceOffset; found = true; break; }
            if (!found) return false;
            if (!clearance) return true;
            // Verify a level, reconstructed floor disk, then reject nearby raised
            // surfaces. Missing scan data never creates an invented walking target.
            for (var i = 0; i < 12; i++)
            {
                var angle = i * Mathf.PI * 2f / 12f;
                var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                if (!TryFindSafeGround(ground + direction * .8f, floorY, out _, false)) return false;
                foreach (var height in new[] { .15f, .65f, 1.1f })
                {
                    var obstacles = Physics.RaycastAll(ground + Vector3.up * height, direction, .8f,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                    foreach (var hit in obstacles)
                        if (hit.collider is MeshCollider && IsGeneratedSpatialMeshCollider(hit.collider, m_SpatialMeshManager.meshes))
                            return false;
                }
            }
            return true;
        }

        public bool IsSafePath(Vector3 from, Vector3 to, float floorY)
        {
            // Confirm a body-width strip rather than just a line. Short guide
            // waypoints must not bypass obstacle checks used by garden targets.
            var direction = Vector3.ProjectOnPlane(to - from, Vector3.up).normalized;
            var side = Vector3.Cross(Vector3.up, direction) * .2f;
            var count = Mathf.Max(1, Mathf.CeilToInt(HorizontalDistance(from, to) / .2f));
            for (var i = 0; i <= count; i++)
            {
                var point = Vector3.Lerp(from, to, i / (float)count);
                if (!TryFindSafeGround(point, floorY, out _) ||
                    !TryFindSafeGround(point + side, floorY, out _) ||
                    !TryFindSafeGround(point - side, floorY, out _)) return false;
            }
            var basePoint = new Vector3(from.x, floorY, from.z);
            var obstacles = Physics.CapsuleCastAll(basePoint + Vector3.up * .4f,
                basePoint + Vector3.up * 1.35f, .25f, direction, HorizontalDistance(from, to),
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            foreach (var hit in obstacles)
                if (IsGeneratedSpatialMeshCollider(hit.collider, m_SpatialMeshManager.meshes)) return false;
            return true;
        }

        public void WaveTrail()
        {
            for (var i = 0; i < m_SpawnedGrass.Count; i++)
                if (m_SpawnedGrass[i] != null)
                    m_SpawnedGrass[i].GetComponent<WalkTrailGrowth>()?.Wave(i * .05f);
        }

        static float HorizontalDistance(Vector3 first, Vector3 second)
        {
            first.y = 0f;
            second.y = 0f;
            return Vector3.Distance(first, second);
        }

        static void AlignVisualBaseToSurface(GameObject instance, float surfaceY)
        {
            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            var lowestY = float.PositiveInfinity;
            foreach (var renderer in renderers)
                if (renderer.enabled)
                    lowestY = Mathf.Min(lowestY, renderer.bounds.min.y);
            if (!float.IsPositiveInfinity(lowestY))
                instance.transform.position += Vector3.up * (surfaceY - lowestY);
        }

        void OnDisable() => StopAndClear();
    }
}
