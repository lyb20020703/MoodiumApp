using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace Moodium.NatureWorld
{
    /// <summary>
    /// Adds a small, LOD-backed grove around the player as Nature World energy grows.
    /// This stays deliberately separate from the candy burst and spatial-mesh effects.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NatureForestGrowthController : MonoBehaviour
    {
        public void RegisterRoomVisibility(Moodium.Flow.RoomContentVisibility visibility) { if (m_ForestRoot != null) foreach (Transform child in m_ForestRoot) visibility.Register(child.gameObject); }

        static readonly float[] SpawnThresholds = { 0.35f, 0.55f, 0.75f, 0.9f, 1f };
        // Local X is viewer-right and local Y is viewer-forward. Fill the user's
        // forward field first in a shallow arc, then widen to the sides/rear.
        static readonly Vector2[] ViewerRelativeSpawnOffsets =
        {
            new(-2.4f, 2.8f),  // forward-left
            new(2.4f, 2.8f),   // forward-right
            new(0f, 4.1f),     // forward-center, farther ring
            new(-4.1f, 0.9f),  // side-left
            new(4.1f, 0.9f)    // side-right
        };
        // Trees were already reduced to half their prefab scale. Nature World now
        // needs one third of that displayed size, i.e. one sixth of prefab scale.
        const float TreeScaleMultiplier = 1f / 6f;

        [Header("Ground placement")]
        [Tooltip("visionOS world-space floor height used only while the spatial mesh is unavailable.")]
        [SerializeField] float m_FallbackGroundY = 0f;
        [SerializeField, Min(0f)] float m_MinTreeSeparation = 3f;
        [SerializeField, Range(0f, 1f)] float m_MinGroundNormalY = .55f;
        [SerializeField, Min(0f)] float m_SurfaceOffset = .01f;

        readonly List<GameObject> m_TreePrefabs = new();
        readonly List<GameObject> m_SpawnedTrees = new();
        Transform m_Viewer;
        ARMeshManager m_SpatialMeshManager;
        Transform m_ForestRoot;
        AudioClip m_TreeSpawnSound;
        int m_NextThresholdIndex;
        bool m_Running;

        public int SpawnedTreeCount => m_SpawnedTrees.Count;
        public Transform ForestRoot => m_ForestRoot;

        public void ConfigureSpawnSound(AudioClip spawnSound) => m_TreeSpawnSound = spawnSound;

        public static Vector2 GetViewerRelativeSpawnOffset(int index) =>
            ViewerRelativeSpawnOffsets[index % ViewerRelativeSpawnOffsets.Length];

        public void Configure(Transform viewer, GameObject[] treePrefabs, ARMeshManager spatialMeshManager = null)
        {
            m_Viewer = viewer;
            m_SpatialMeshManager = spatialMeshManager;
            m_TreePrefabs.Clear();
            if (treePrefabs == null)
                return;

            foreach (var prefab in treePrefabs)
                if (prefab != null)
                    m_TreePrefabs.Add(prefab);
        }

        public void StartGrowth()
        {
            StopAndClear();
            m_Running = true;
            m_NextThresholdIndex = 0;
        }

        public void SetProgress(float normalizedProgress)
        {
            if (!m_Running)
                return;

            var progress = Mathf.Clamp01(normalizedProgress);
            while (m_NextThresholdIndex < SpawnThresholds.Length &&
                   progress >= SpawnThresholds[m_NextThresholdIndex])
            {
                SpawnTree(m_NextThresholdIndex);
                m_NextThresholdIndex++;
            }
        }

        public void StopAndClear()
        {
            m_Running = false;
            foreach (var tree in m_SpawnedTrees)
                if (tree != null)
                {
                    if (Application.isPlaying) Destroy(tree);
                    else DestroyImmediate(tree);
                }
            m_SpawnedTrees.Clear();

            if (m_ForestRoot != null)
            {
                if (Application.isPlaying) Destroy(m_ForestRoot.gameObject);
                else DestroyImmediate(m_ForestRoot.gameObject);
            }
            m_ForestRoot = null;
            m_NextThresholdIndex = 0;
        }

        void SpawnTree(int index)
        {
            if (m_Viewer == null || m_TreePrefabs.Count == 0)
                return;

            if (m_ForestRoot == null)
            {
                var root = new GameObject("Nature Forest Growth");
                // Trees belong to the real room, not to an image anchor, calibrated bag,
                // or any world-flow transform. A scene-root parent prevents a later
                // tracked-parent rotation/scale update from making the grove lean.
                m_ForestRoot = root.transform;
            }

            var forward = Vector3.ProjectOnPlane(m_Viewer.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.01f)
                forward = Vector3.forward;
            forward.Normalize();
            var right = Vector3.Cross(Vector3.up, forward).normalized;
            var desiredPosition = FindSeparatedDesiredPosition(index, right, forward);
            var ground = FindGroundPosition(desiredPosition);
            // Lock the tree's up vector to gravity. Only yaw is allowed to vary;
            // no image-anchor, camera-roll, or mesh-normal rotation may tilt a tree.
            var baseYaw = Mathf.Atan2(-forward.x, -forward.z) * Mathf.Rad2Deg;
            var rotation = Quaternion.Euler(0f, baseYaw + (index * 47f) % 360f, 0f);
            var prefab = m_TreePrefabs[index % m_TreePrefabs.Count];
            var instance = Instantiate(prefab, ground.Position, rotation, m_ForestRoot);
            instance.name = $"{prefab.name} (Nature Forest {index + 1})";
            if (m_TreeSpawnSound != null)
                AudioSource.PlayClipAtPoint(m_TreeSpawnSound, ground.Position, 0.7f);
            instance.transform.rotation = rotation;
            instance.transform.localScale *= TreeScaleMultiplier;
            if (ground.HasSpatialSurface)
                AlignVisualBaseToSurface(instance, ground.Position.y);
            m_SpawnedTrees.Add(instance);
            // Nature Forest uses the authored mesh presentation. Point-cloud assets may
            // still exist for other experiences, but must not replace these trees because
            // their foliage quality is too coarse at the room-scale viewing distance.
            instance.AddComponent<NatureTreeGrowIn>();
        }

        Vector3 FindSeparatedDesiredPosition(int index, Vector3 right, Vector3 forward)
        {
            var spawnOffset = GetViewerRelativeSpawnOffset(index);
            var desired = m_Viewer.position + right * spawnOffset.x + forward * spawnOffset.y;
            if (HasMinimumHorizontalSeparation(desired, m_SpawnedTrees, m_MinTreeSeparation))
                return desired;

            // Preserve the intended sector while moving a conflicting tree farther from
            // the viewer. This avoids a second trunk occupying the same visual space.
            var direction = new Vector3(spawnOffset.x, 0f, spawnOffset.y).normalized;
            for (var attempt = 1; attempt <= 4; attempt++)
            {
                var candidate = desired + (right * direction.x + forward * direction.z) * (attempt * m_MinTreeSeparation);
                if (HasMinimumHorizontalSeparation(candidate, m_SpawnedTrees, m_MinTreeSeparation))
                    return candidate;
            }
            return desired;
        }

        public static bool HasMinimumHorizontalSeparation(Vector3 candidate, Vector3 existing, float minimumDistance)
        {
            candidate.y = 0f;
            existing.y = 0f;
            return Vector3.Distance(candidate, existing) >= minimumDistance;
        }

        static bool HasMinimumHorizontalSeparation(Vector3 candidate, IEnumerable<GameObject> spawnedTrees, float minimumDistance)
        {
            foreach (var tree in spawnedTrees)
                if (tree != null && !HasMinimumHorizontalSeparation(candidate, tree.transform.position, minimumDistance))
                    return false;
            return true;
        }

        readonly struct GroundPlacement
        {
            public GroundPlacement(Vector3 position, bool hasSpatialSurface)
            {
                Position = position;
                HasSpatialSurface = hasSpatialSurface;
            }

            public Vector3 Position { get; }
            public bool HasSpatialSurface { get; }
        }

        GroundPlacement FindGroundPosition(Vector3 desiredPosition)
        {
            var origin = desiredPosition + Vector3.up * 4f;
            var hits = Physics.RaycastAll(origin, Vector3.down, 8f, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
            foreach (var hit in hits)
            {
                if (!IsSpatialGround(hit.collider, hit.normal))
                    continue;
                return new GroundPlacement(hit.point + hit.normal * m_SurfaceOffset, true);
            }

            // Spatial meshes can be unavailable for a moment at session start. The previous
            // eye-relative fallback left trees visibly floating for seated users. Keep their
            // roots on the world floor until a real mesh hit is available instead.
            return new GroundPlacement(new Vector3(desiredPosition.x, m_FallbackGroundY, desiredPosition.z), false);
        }

        bool IsSpatialGround(Collider collider, Vector3 normal)
        {
            return collider is MeshCollider && normal.y >= m_MinGroundNormalY &&
                   m_SpatialMeshManager != null &&
                   collider.GetComponentInParent<ARMeshManager>() == m_SpatialMeshManager;
        }

        static void AlignVisualBaseToSurface(GameObject tree, float surfaceY)
        {
            var renderers = tree.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return;
            var lowestY = float.PositiveInfinity;
            foreach (var renderer in renderers)
                if (renderer.enabled)
                    lowestY = Mathf.Min(lowestY, renderer.bounds.min.y);
            if (!float.IsPositiveInfinity(lowestY))
                tree.transform.position += Vector3.up * (surfaceY - lowestY);
        }

        void OnDisable() => StopAndClear();
    }
}
