using UnityEngine;
using UnityEngine.XR.ARFoundation;
using Moodium.Interaction;

namespace Moodium.CandyWorld
{
    /// <summary>Places Candy World's staged completion houses on the real floor.</summary>
    [DisallowMultipleComponent]
    public sealed class CandyCompletionHouseController : MonoBehaviour
    {
        public void RegisterRoomVisibility(Moodium.Flow.RoomContentVisibility visibility)
        {
            visibility.RegisterImmediateHide(m_HouseInstance);
            visibility.RegisterImmediateHide(m_LittleHouseInstance);
            visibility.RegisterImmediateHide(m_HoneyJarInstance);
        }

        const float MainHouseProgress = .55f;
        const float HoneyJarProgress = .75f;
        const float LittleHouseProgress = .9f;
        const float HouseScaleMultiplier = 3f;
        // Place both completion houses in the user's forward view first. The two
        // offsets form the first arc around the viewer instead of hiding behind them.
        // The houses are scaled three times at runtime. Keep a wide forward arc so
        // their renderer bounds do not overlap even when both completion stages are visible.
        static readonly Vector2 ViewerRelativeSpawnOffset = new(2.8f, 2.9f);
        static readonly Vector2 LittleHouseViewerRelativeSpawnOffset = new(-2.8f, 2.9f);

        [SerializeField, Range(0f, 1f)] float m_MinGroundNormalY = .55f;
        [SerializeField, Min(0f)] float m_SurfaceOffset = .01f;
        [SerializeField] float m_FallbackGroundY = 0f;

        Transform m_Viewer;
        ARMeshManager m_SpatialMeshManager;
        GameObject m_HousePrefab;
        GameObject m_LittleHousePrefab;
        GameObject m_HoneyJarPrefab;
        GameObject m_HoneyJarInstance;
        AudioClip m_PrefabSpawnSound;
        AudioClip m_DeformationSound;
        GameObject m_HouseInstance;
        GameObject m_LittleHouseInstance;
        bool m_Started;
        CandyBurstController m_ContactBursts;

        public void ConfigureContactBursts(CandyBurstController bursts) => m_ContactBursts = bursts;

        public static Vector2 GetViewerRelativeSpawnOffset() => ViewerRelativeSpawnOffset;
        public static float GetHouseScaleMultiplier() => HouseScaleMultiplier;
        public static bool ShouldSpawnCandyHouse(float normalizedProgress, bool hasSpawned) =>
            normalizedProgress >= MainHouseProgress && !hasSpawned;
        public static bool ShouldSpawnCandyLittleHouse(float normalizedProgress, bool hasSpawned) =>
            normalizedProgress >= LittleHouseProgress && !hasSpawned;

        public void Configure(Transform viewer, ARMeshManager spatialMeshManager, GameObject housePrefab,
            GameObject littleHousePrefab, AudioClip prefabSpawnSound = null)
        {
            m_Viewer = viewer;
            m_SpatialMeshManager = spatialMeshManager;
            m_HousePrefab = housePrefab;
            m_LittleHousePrefab = littleHousePrefab;
            m_PrefabSpawnSound = prefabSpawnSound;
        }

        public void ConfigureDeformationSound(AudioClip sound) => m_DeformationSound = sound;

        public void ConfigureHoneyJar(GameObject prefab) => m_HoneyJarPrefab = prefab;

        public void StartDisplay()
        {
            StopAndClear();
            m_Started = true;
        }

        public void SetProgress(float normalizedProgress)
        {
            if (!m_Started || m_Viewer == null)
                return;

            if (m_HousePrefab != null && ShouldSpawnCandyHouse(normalizedProgress, m_HouseInstance != null))
                m_HouseInstance = SpawnHouse(m_HousePrefab, "Candy Completion House", ViewerRelativeSpawnOffset);
            if (m_HoneyJarPrefab != null && normalizedProgress >= HoneyJarProgress && m_HoneyJarInstance == null)
                m_HoneyJarInstance = SpawnHouse(m_HoneyJarPrefab, "Candy Progress Honey Jar", new Vector2(0f, 2.2f), 1f);
            if (m_LittleHousePrefab != null &&
                ShouldSpawnCandyLittleHouse(normalizedProgress, m_LittleHouseInstance != null))
                m_LittleHouseInstance = SpawnHouse(m_LittleHousePrefab, "Candy Little Completion House",
                    LittleHouseViewerRelativeSpawnOffset);
        }

        public void StopAndClear()
        {
            m_Started = false;
            if (m_HoneyJarInstance != null)
                Destroy(m_HoneyJarInstance);
            m_HoneyJarInstance = null;
            if (m_HouseInstance != null)
                Destroy(m_HouseInstance);
            m_HouseInstance = null;
            if (m_LittleHouseInstance != null)
                Destroy(m_LittleHouseInstance);
            m_LittleHouseInstance = null;
        }

        GameObject SpawnHouse(GameObject prefab, string instanceName, Vector2 spawnOffset,
            float scaleMultiplier = HouseScaleMultiplier)
        {
            var forward = Vector3.ProjectOnPlane(m_Viewer.forward, Vector3.up);
            if (forward.sqrMagnitude < .01f)
                forward = Vector3.forward;
            forward.Normalize();
            var right = Vector3.Cross(Vector3.up, forward).normalized;
            var desiredPosition = m_Viewer.position + right * spawnOffset.x + forward * spawnOffset.y;
            var ground = FindGroundPosition(desiredPosition);
            var yaw = Mathf.Atan2(-forward.x, -forward.z) * Mathf.Rad2Deg + 180f;
            var instance = Instantiate(prefab, ground.Position, Quaternion.Euler(0f, yaw, 0f));
            instance.name = $"{prefab.name} ({instanceName})";
            if (m_PrefabSpawnSound != null)
                AudioSource.PlayClipAtPoint(m_PrefabSpawnSound, ground.Position, 0.7f);
            instance.transform.localScale *= scaleMultiplier;
            AlignVisualBaseToSurface(instance, ground.Position.y);
            // Progress rewards retain their authored meshes and local soft-touch deformation.
            var softInteraction = instance.GetComponent<CompletionHouseSoftInteraction>();
            if (softInteraction == null)
                softInteraction = instance.AddComponent<CompletionHouseSoftInteraction>();
            softInteraction.Configure();
            softInteraction.ConfigureHoldSound(m_DeformationSound);
            softInteraction.ContactBurstRequested += point =>
            {
                if (m_Started && m_ContactBursts != null && instance != null)
                    m_ContactBursts.BurstAtContact(point, instance.transform);
            };
            return instance;
        }

        GroundPlacement FindGroundPosition(Vector3 desiredPosition)
        {
            if (m_SpatialMeshManager != null)
            {
                var hits = Physics.RaycastAll(desiredPosition + Vector3.up * 4f, Vector3.down, 8f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                System.Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
                foreach (var hit in hits)
                    if (hit.collider is MeshCollider && hit.normal.y >= m_MinGroundNormalY &&
                        hit.collider.GetComponentInParent<ARMeshManager>() == m_SpatialMeshManager)
                        return new GroundPlacement(hit.point + hit.normal * m_SurfaceOffset);
            }
            return new GroundPlacement(new Vector3(desiredPosition.x, m_FallbackGroundY, desiredPosition.z));
        }

        static void AlignVisualBaseToSurface(GameObject house, float surfaceY)
        {
            var lowestY = float.PositiveInfinity;
            foreach (var renderer in house.GetComponentsInChildren<Renderer>(true))
                if (renderer.enabled)
                    lowestY = Mathf.Min(lowestY, renderer.bounds.min.y);
            if (!float.IsPositiveInfinity(lowestY))
                house.transform.position += Vector3.up * (surfaceY - lowestY);
        }

        readonly struct GroundPlacement
        {
            public GroundPlacement(Vector3 position) => Position = position;
            public Vector3 Position { get; }
        }

        void OnDisable() => StopAndClear();
    }
}
