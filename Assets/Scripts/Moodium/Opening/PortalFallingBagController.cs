using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Moodium.Opening
{
    [DisallowMultipleComponent]
    public sealed class PortalFallingBagController : MonoBehaviour
    {
        sealed class BagRuntime
        {
            public Transform Transform;
            public Rigidbody Rigidbody;
            public Collider Collider;
            public Vector3 BaseScale;
            public bool Recycling;
            public float RestingTime;
        }

        [Header("Bag library")]
        [SerializeField] GameObject[] m_BagPrefabs;
        [SerializeField, Range(1, 64)] int m_ConcurrentCount = 30;

        [Header("Spawn region (PortalContentRoot local space)")]
        // Match the widened portal opening while keeping a margin from the chamber walls.
        [SerializeField] Vector2 m_SpawnX = new(-0.75f, 0.75f);
        [SerializeField] Vector2 m_SpawnY = new(0.78f, 1.08f);
        [SerializeField] Vector2 m_SpawnZ = new(0.2f, 0.5f);
        [SerializeField] Vector2 m_UniformScale = new(0.15f, 0.18f);
        [SerializeField, Min(1f)] float m_LeafBagScaleMultiplier = 1.15f;
        [SerializeField, Min(0.05f)] float m_InitialSpawnInterval = 0.35f;
        [SerializeField] AudioClip m_BagCollisionClip;

        [Header("Physical motion")]
        [SerializeField, Range(0.05f, 1f)] float m_GravityMultiplier = 0.3f;
        [SerializeField] Vector2 m_InitialDownSpeed = new(0.02f, 0.12f);
        [SerializeField] Vector2 m_AngularSpeed = new(0.4f, 1.8f);
        [SerializeField, Min(0.01f)] float m_Mass = 0.12f;

        [Header("Physics optimization")]
        [SerializeField, Min(0.05f)] float m_FreezeAfterRestSeconds = 0.4f;
        [SerializeField, Min(0.001f)] float m_LinearRestSpeed = 0.045f;
        [SerializeField, Min(0.001f)] float m_AngularRestSpeed = 0.12f;

        readonly List<BagRuntime> m_Bags = new();

        public int SpawnedCount => m_Bags.Count;

        void OnEnable()
        {
            EnsurePool();
            for (var i = 0; i < m_Bags.Count; i++)
            {
                var bag = m_Bags[i];
                SetDormant(bag);
                StartCoroutine(ActivateBagAfterDelay(bag, i * m_InitialSpawnInterval));
            }
        }

        void OnDisable()
        {
            StopAllCoroutines();
            foreach (var bag in m_Bags)
            {
                if (bag?.Rigidbody == null)
                    continue;
                bag.Rigidbody.linearVelocity = Vector3.zero;
                bag.Rigidbody.angularVelocity = Vector3.zero;
                bag.Rigidbody.isKinematic = true;
                bag.Recycling = false;
            }
        }

        void FixedUpdate()
        {
            foreach (var bag in m_Bags)
            {
                if (bag.Recycling || bag.Rigidbody == null || bag.Rigidbody.isKinematic)
                    continue;
                bag.Rigidbody.AddForce(Physics.gravity * m_GravityMultiplier, ForceMode.Acceleration);

                if (bag.Rigidbody.linearVelocity.sqrMagnitude > m_LinearRestSpeed * m_LinearRestSpeed ||
                    bag.Rigidbody.angularVelocity.sqrMagnitude > m_AngularRestSpeed * m_AngularRestSpeed)
                {
                    bag.RestingTime = 0f;
                    continue;
                }

                bag.RestingTime += Time.fixedDeltaTime;
                if (bag.RestingTime >= m_FreezeAfterRestSeconds)
                    FreezeAsStaticStackMember(bag);
            }
        }

        public void ConfigureForTests(GameObject[] prefabs, int count)
        {
            m_BagPrefabs = prefabs;
            m_ConcurrentCount = count;
        }

        public void BuildPoolForTests() => EnsurePool();

        void EnsurePool()
        {
            if (m_Bags.Count > 0 || m_BagPrefabs == null || m_BagPrefabs.Length == 0)
                return;

            for (var i = 0; i < m_ConcurrentCount; i++)
            {
                var prefab = GetPrefabForIndex(i);
                if (prefab == null)
                    continue;
                var instance = Instantiate(prefab, transform, false);
                instance.name = $"{prefab.name} (Portal Falling {i + 1})";
                instance.transform.localPosition = RandomSpawnPosition();
                instance.transform.localRotation = Quaternion.identity;
                var scale = Random.Range(m_UniformScale.x, m_UniformScale.y);
                if (prefab.name.Contains("LeafBag"))
                    scale *= m_LeafBagScaleMultiplier;
                instance.transform.localScale *= scale;

                var collider = instance.GetComponent<BoxCollider>();
                if (collider == null)
                    collider = instance.AddComponent<BoxCollider>();
                FitColliderToRenderers(instance.transform, collider);

                var rigidbody = instance.GetComponent<Rigidbody>();
                if (rigidbody == null)
                    rigidbody = instance.AddComponent<Rigidbody>();
                rigidbody.mass = m_Mass;
                rigidbody.useGravity = false;
                rigidbody.linearDamping = 0.08f;
                rigidbody.angularDamping = 0.15f;
                rigidbody.interpolation = RigidbodyInterpolation.None;
                rigidbody.collisionDetectionMode = CollisionDetectionMode.Discrete;
                rigidbody.solverIterations = 4;
                rigidbody.solverVelocityIterations = 1;
                if (m_BagCollisionClip != null)
                {
                    var collisionAudio = instance.GetComponent<PortalBagCollisionAudio>();
                    if (collisionAudio == null)
                        collisionAudio = instance.AddComponent<PortalBagCollisionAudio>();
                    collisionAudio.Configure(m_BagCollisionClip);
                }
                var runtime = new BagRuntime
                {
                    Transform = instance.transform,
                    Rigidbody = rigidbody,
                    Collider = collider,
                    BaseScale = instance.transform.localScale
                };
                m_Bags.Add(runtime);
                SetDormant(runtime);
            }
        }

        IEnumerator ActivateBagAfterDelay(BagRuntime bag, float delay)
        {
            if (delay > 0f)
                yield return new WaitForSeconds(delay);
            if (isActiveAndEnabled)
                ResetBag(bag);
        }

        static void SetDormant(BagRuntime bag)
        {
            bag.Rigidbody.linearVelocity = Vector3.zero;
            bag.Rigidbody.angularVelocity = Vector3.zero;
            bag.Rigidbody.isKinematic = true;
            bag.Collider.enabled = false;
            bag.Transform.gameObject.SetActive(false);
            bag.Recycling = false;
            bag.RestingTime = 0f;
        }

        void ResetBag(BagRuntime bag)
        {
            bag.Transform.gameObject.SetActive(true);
            bag.Transform.localPosition = RandomSpawnPosition();
            bag.Transform.localRotation = Random.rotation;
            bag.Transform.localScale = bag.BaseScale;
            bag.Collider.enabled = true;
            bag.Rigidbody.isKinematic = false;
            bag.Rigidbody.linearVelocity = Vector3.down * Random.Range(m_InitialDownSpeed.x, m_InitialDownSpeed.y);
            bag.Rigidbody.angularVelocity = Random.onUnitSphere * Random.Range(m_AngularSpeed.x, m_AngularSpeed.y);
            bag.Recycling = false;
            bag.RestingTime = 0f;
        }

        static void FreezeAsStaticStackMember(BagRuntime bag)
        {
            bag.Rigidbody.linearVelocity = Vector3.zero;
            bag.Rigidbody.angularVelocity = Vector3.zero;
            bag.Rigidbody.isKinematic = true;
            bag.RestingTime = 0f;
        }

        GameObject GetPrefabForIndex(int index)
        {
            for (var attempt = 0; attempt < m_BagPrefabs.Length; attempt++)
            {
                var candidate = m_BagPrefabs[(index + attempt) % m_BagPrefabs.Length];
                if (candidate != null)
                    return candidate;
            }
            return null;
        }

        Vector3 RandomSpawnPosition()
        {
            return new Vector3(
                Random.Range(m_SpawnX.x, m_SpawnX.y),
                Random.Range(m_SpawnY.x, m_SpawnY.y),
                Random.Range(m_SpawnZ.x, m_SpawnZ.y));
        }

        static void FitColliderToRenderers(Transform root, BoxCollider collider)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                collider.center = Vector3.zero;
                collider.size = Vector3.one;
                return;
            }

            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            collider.center = root.InverseTransformPoint(bounds.center);
            var scale = root.lossyScale;
            collider.size = new Vector3(
                bounds.size.x / Mathf.Max(Mathf.Abs(scale.x), 0.0001f),
                bounds.size.y / Mathf.Max(Mathf.Abs(scale.y), 0.0001f),
                bounds.size.z / Mathf.Max(Mathf.Abs(scale.z), 0.0001f));
        }
    }
}
