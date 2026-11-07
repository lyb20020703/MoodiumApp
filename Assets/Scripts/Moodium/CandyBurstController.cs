using System.Collections;
using System.Collections.Generic;
using Moodium.Interaction;
using UnityEngine;

namespace Moodium.CandyWorld
{
    public enum NatureBurstDebugPrefabSelection
    {
        Random,
        Clover,
        Flower,
        Mushroom
    }

    public sealed class CandyBurstController : MonoBehaviour
    {
        [SerializeField, Range(1, 8)] int m_MinCandyPerBurst = 2;
        [SerializeField, Range(1, 10)] int m_MaxCandyPerBurst = 4;
        [SerializeField, Range(0.01f, 0.5f)] float m_RealityScaleMultiplier = 0.05f;
        [SerializeField] Vector2 m_UpwardSpeed = new(0.55f, 0.85f);
        [SerializeField] float m_OutwardSpeed = 0.24f;
        [SerializeField] float m_SpawnInterval = 0.045f;
        [Header("Nature burst compatibility")]
        [Tooltip("Largest rendered dimension, in metres, for an emitted Nature model.")]
        [SerializeField] Vector2 m_NatureBurstSize = new(0.045f, 0.075f);
        [Tooltip("Legacy diagnostic only. Burst wrappers use their authored materials and Candy's fixed scale instead.")]
        [SerializeField] bool m_EnableLegacyNatureBurstCompatibility;

        readonly List<GameObject> m_CandyPrefabs = new();
        readonly List<GameObject> m_NaturePrefabs = new();
        readonly List<CandyProjectile> m_ActiveProjectiles = new();
        bool m_UseNaturePrefabs;
        int m_ReservedContactCandies;

        static int ContactBurstCapacity(int live, int reserved, int requested) =>
            Mathf.Clamp(requested, 0, Mathf.Max(0, 32 - live - reserved));

        /// <summary>Bounded Candy-only prop feedback; keeps the existing CandySoft burst path unchanged.</summary>
        public bool BurstAtContact(Vector3 center, Transform source)
        {
            if (!isActiveAndEnabled || source == null || m_CandyPrefabs.Count == 0) return false;
            m_ActiveProjectiles.RemoveAll(item => item == null);
            var count = ContactBurstCapacity(m_ActiveProjectiles.Count, m_ReservedContactCandies,
                Random.Range(m_MinCandyPerBurst, m_MaxCandyPerBurst + 1));
            if (count == 0) return false;
            m_ReservedContactCandies += count;
            StartCoroutine(SpawnBurst(center, source, source.GetComponentsInChildren<Collider>(true),
                m_CandyPrefabs, count, contactBurst: true));
            return true;
        }

        public void Configure(GameObject[] prefabs)
        {
            m_CandyPrefabs.Clear();
            if (prefabs == null)
                return;
            foreach (var prefab in prefabs)
            {
                if (prefab == null)
                    continue;
                var normalized = prefab.name.Replace("Prefab", string.Empty).Replace("_", string.Empty);
                if (normalized.Equals("Chocolate", System.StringComparison.OrdinalIgnoreCase) ||
                    normalized.Equals("Cookie", System.StringComparison.OrdinalIgnoreCase) ||
                    normalized.Equals("Heart", System.StringComparison.OrdinalIgnoreCase) ||
                    normalized.Equals("Macaron", System.StringComparison.OrdinalIgnoreCase) ||
                    normalized.Equals("Star", System.StringComparison.OrdinalIgnoreCase))
                    m_CandyPrefabs.Add(prefab);
            }
        }

        public void ConfigureNaturePrefabs(GameObject[] prefabs)
        {
            m_NaturePrefabs.Clear();
            if (prefabs == null)
                return;
            foreach (var prefab in prefabs)
                if (prefab != null)
                    m_NaturePrefabs.Add(prefab);
        }

        public void SetNatureWorld(bool natureWorld) => m_UseNaturePrefabs = natureWorld;
        /// <summary>Used by the reversible Nature diagnostic to select the proven Candy pool.</summary>
        public static bool ShouldUseNaturePrefabPool(bool natureWorldActive, bool useCandyPoolInNatureForDebug) =>
            natureWorldActive && !useCandyPoolInNatureForDebug;

        public static bool ShouldApplyNatureBurstCompatibility(bool usingNaturePrefabPool,
            bool legacyCompatibilityEnabled, bool pureNatureDebug) =>
            usingNaturePrefabPool && legacyCompatibilityEnabled && !pureNatureDebug;

        /// <summary>
        /// Nature FBX assets use imported texture/material bindings that do not
        /// render reliably through PolySpatial on device. These resources are
        /// explicit URP Lit material assets included in the visionOS build.
        /// </summary>
        public static string GetNatureBurstMaterialResourcePath(string prefabName)
        {
            if (string.IsNullOrWhiteSpace(prefabName))
                return string.Empty;
            var normalized = prefabName.Replace(" ", string.Empty).Replace("-", string.Empty)
                .ToLowerInvariant();
            if (normalized.Contains("flower"))
                return "Moodium/NatureBurst/Flower";
            if (normalized.Contains("clover"))
                return "Moodium/NatureBurst/Clover";
            if (normalized.Contains("mushroom"))
                return "Moodium/NatureBurst/Mushroom";
            return string.Empty;
        }

        public void Burst(Vector3 center, Transform source, float sizeMultiplier = 1f)
        {
            var prefabs = m_UseNaturePrefabs ? m_NaturePrefabs : m_CandyPrefabs;
            if (source == null || prefabs.Count == 0)
            {
                Debug.LogWarning("[Candy Burst] Chocolate Capsule source or prefab list is not ready.");
                return;
            }
            // ChocolateCapsuleInteraction already plays the unified Moodium touch VFX.
            // Do not add a second legacy/debug particle layer here.
            var sourceColliders = source.GetComponentsInChildren<Collider>(true);
            StartCoroutine(SpawnBurst(center, source, sourceColliders, prefabs,
                Random.Range(m_MinCandyPerBurst, m_MaxCandyPerBurst + 1), sizeMultiplier: sizeMultiplier));
        }

        /// <summary>
        /// Diagnostic-only Nature burst. This deliberately uses the same world-space
        /// spawn and CandyProjectile launch path as Candy, while preserving the source
        /// Nature prefab's material and using Candy's fixed 0.05 scale multiplier.
        /// </summary>
        public void ForceNatureBurstPure(Vector3 center, Transform source,
            NatureBurstDebugPrefabSelection selection)
        {
            if (source == null || m_NaturePrefabs.Count == 0)
            {
                Debug.LogWarning("[Nature Burst Debug] Force request ignored: NatureSoft source or prefab list is not ready.");
                return;
            }

            var prefabs = new List<GameObject>();
            foreach (var prefab in m_NaturePrefabs)
                if (prefab != null && MatchesNatureDebugPrefab(prefab.name, selection))
                    prefabs.Add(prefab);
            if (prefabs.Count == 0)
            {
                Debug.LogWarning($"[Nature Burst Debug] Force request '{selection}' did not match a configured Nature prefab.");
                return;
            }

            var sourceColliders = source.GetComponentsInChildren<Collider>(true);
            Debug.Log($"[Nature Burst Debug] Pure Candy pipeline request: {selection}; center={center}; count={m_MinCandyPerBurst}-{m_MaxCandyPerBurst}; scale={m_RealityScaleMultiplier:F3}.");
            StartCoroutine(SpawnBurst(center, source, sourceColliders, prefabs,
                Random.Range(m_MinCandyPerBurst, m_MaxCandyPerBurst + 1), pureNatureDebug: true));
        }

        public static bool MatchesNatureDebugPrefab(string prefabName, NatureBurstDebugPrefabSelection selection)
        {
            if (selection == NatureBurstDebugPrefabSelection.Random)
                return true;
            var normalized = string.IsNullOrWhiteSpace(prefabName)
                ? string.Empty
                : prefabName.Replace(" ", string.Empty).Replace("-", string.Empty).ToLowerInvariant();
            return selection switch
            {
                NatureBurstDebugPrefabSelection.Clover => normalized.Contains("clover"),
                NatureBurstDebugPrefabSelection.Flower => normalized.Contains("flower"),
                NatureBurstDebugPrefabSelection.Mushroom => normalized.Contains("mushroom"),
                _ => false
            };
        }

        public void StopAndClear()
        {
            StopAllCoroutines();
            m_ReservedContactCandies = 0;
            var removed = 0;
            foreach (var projectile in m_ActiveProjectiles)
            {
                if (projectile == null)
                    continue;
                Destroy(projectile.gameObject);
                removed++;
            }
            m_ActiveProjectiles.Clear();
            Debug.Log($"[Candy Burst] Cleared {removed} Reality Enhancement candy instances.");
        }

        IEnumerator SpawnBurst(Vector3 center, Transform source, Collider[] sourceColliders,
            List<GameObject> prefabs, int count, bool pureNatureDebug = false, bool contactBurst = false,
            float sizeMultiplier = 1f)
        {
            Debug.Log($"[Candy Burst] Spawning {count} miniature candies from Chocolate Capsule center at {center}.");
            var protectedVideo = FindFirstObjectByType<Moodium.Reality.SqueezeProgressDisplayRouter>();
            var remaining = contactBurst ? count : 0;
            try
            {
            for (var i = 0; i < count; i++)
            {
                var prefab = prefabs[Random.Range(0, prefabs.Count)];
                if (source == null)
                    yield break;
                var radial = Random.onUnitSphere;
                radial.y = Mathf.Abs(radial.y) * 0.55f + 0.25f;
                radial.Normalize();
                var origin = center + radial * Random.Range(0.006f, 0.015f);
                if (pureNatureDebug)
                    Debug.Log($"[Nature Burst Debug][Before Instantiate] Prefab={prefab.name}; RootScale={prefab.transform.localScale}; Origin={origin}.");
                var instance = Instantiate(prefab, origin, Random.rotation);
                instance.name = $"{prefab.name} (Reality Capsule Burst Mini)";
                var useNatureCompatibility = !contactBurst && ShouldApplyNatureBurstCompatibility(
                    m_UseNaturePrefabs, m_EnableLegacyNatureBurstCompatibility, pureNatureDebug);
                if (useNatureCompatibility)
                    ApplyNatureBurstMaterial(instance, prefab.name);
                var outward = radial * Random.Range(m_OutwardSpeed * 0.55f, m_OutwardSpeed);
                var velocity = Vector3.up * Random.Range(m_UpwardSpeed.x, m_UpwardSpeed.y) + outward;
                if (!contactBurst && !pureNatureDebug)
                {
                    // Clear the bag before gravity brings the larger props back down.
                    var horizontal = new Vector3(radial.x, 0f, radial.z).normalized;
                    velocity = Vector3.up * (velocity.y + 0.65f)
                        + horizontal * Random.Range(0.85f, 1.2f);
                }
                var projectile = instance.GetComponent<CandyProjectile>();
                if (projectile == null)
                    projectile = instance.AddComponent<CandyProjectile>();
                var baseScale = useNatureCompatibility
                    ? ResolveNatureScaleMultiplier(instance)
                    : m_RealityScaleMultiplier;
                projectile.Launch(velocity, baseScale * sizeMultiplier);
                projectile.ProtectVideo(protectedVideo);
                if (pureNatureDebug)
                    LogPureNatureBurstInstance(instance, projectile, velocity);
                projectile.IgnoreCollisionsWith(sourceColliders);
                m_ActiveProjectiles.RemoveAll(item => item == null);
                foreach (var previous in m_ActiveProjectiles)
                {
                    projectile.IgnoreCollisionsWith(previous.Colliders);
                    previous.IgnoreCollisionsWith(projectile.Colliders);
                }
                m_ActiveProjectiles.Add(projectile);
                if (contactBurst) { remaining--; m_ReservedContactCandies--; }
                yield return new WaitForSeconds(m_SpawnInterval);
            }
            }
            finally
            {
                m_ReservedContactCandies = Mathf.Max(0, m_ReservedContactCandies - remaining);
            }
        }

        static void LogPureNatureBurstInstance(GameObject instance, CandyProjectile projectile, Vector3 initialVelocity)
        {
            var parentName = instance.transform.parent != null ? instance.transform.parent.name : "<World Space>";
            Debug.Log($"[Nature Burst Debug][After Instantiate] Instance={instance.name}; WorldPosition={instance.transform.position}; " +
                      $"LocalScale={instance.transform.localScale}; LossyScale={instance.transform.lossyScale}; Parent={parentName}.");

            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            Debug.Log($"[Nature Burst Debug][Renderer] Count={renderers.Length}.");
            foreach (var renderer in renderers)
            {
                var meshName = renderer switch
                {
                    SkinnedMeshRenderer skinned => skinned.sharedMesh != null ? skinned.sharedMesh.name : "<none>",
                    _ => renderer.GetComponent<MeshFilter>()?.sharedMesh != null
                        ? renderer.GetComponent<MeshFilter>().sharedMesh.name : "<none>"
                };
                var material = renderer.sharedMaterial;
                var texture = material != null && material.HasProperty("_BaseMap")
                    ? material.GetTexture("_BaseMap")
                    : material != null && material.HasProperty("_MainTex") ? material.mainTexture : null;
                var bounds = renderer.bounds;
                Debug.Log($"[Nature Burst Debug][Renderer] Name={renderer.name}; Mesh={meshName}; Material={material?.name ?? "<none>"}; " +
                          $"Shader={material?.shader?.name ?? "<none>"}; BoundsCenter={bounds.center}; BoundsSize={bounds.size}; " +
                          $"Enabled={renderer.enabled}; Texture={texture?.name ?? "<none>"}.");
                if (bounds.size.x > .5f || bounds.size.y > .5f || bounds.size.z > .5f)
                    Debug.LogWarning($"[Nature Burst Debug] NATURE BURST ABNORMAL BOUNDS: {renderer.name}; size={bounds.size}.");
            }

            var scale = instance.transform.lossyScale;
            if (Mathf.Abs(scale.x) < .005f || Mathf.Abs(scale.y) < .005f || Mathf.Abs(scale.z) < .005f)
                Debug.LogWarning($"[Nature Burst Debug] NATURE BURST TOO SMALL: {instance.name}; lossyScale={scale}.");
            var body = instance.GetComponent<Rigidbody>();
            Debug.Log($"[Nature Burst Debug][Rigidbody] InitialVelocity={body?.linearVelocity ?? initialVelocity}; " +
                      $"AngularVelocity={body?.angularVelocity ?? Vector3.zero}; ColliderCount={projectile.Colliders.Length}.");
        }

        float ResolveNatureScaleMultiplier(GameObject instance)
        {
            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return m_RealityScaleMultiplier;
            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            var largestDimension = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            if (largestDimension <= .0001f)
                return m_RealityScaleMultiplier;
            return Random.Range(m_NatureBurstSize.x, m_NatureBurstSize.y) / largestDimension;
        }

        static void ApplyNatureBurstMaterial(GameObject instance, string prefabName)
        {
            var resourcePath = GetNatureBurstMaterialResourcePath(prefabName);
            var material = string.IsNullOrEmpty(resourcePath) ? null : Resources.Load<Material>(resourcePath);
            if (material == null)
            {
                Debug.LogWarning($"[Nature Burst] No visionOS-safe material was found for '{prefabName}'.");
                return;
            }
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                renderer.sharedMaterial = material;
        }

        void OnDisable() => StopAndClear();
    }
}
