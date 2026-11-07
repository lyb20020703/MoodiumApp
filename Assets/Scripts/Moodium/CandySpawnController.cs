using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Moodium.CandyWorld
{
    public sealed class CandySpawnController : MonoBehaviour
    {
        [SerializeField] float m_HeightAboveCapsule = 0.16f;
        [SerializeField] Vector2 m_UpwardSpeed = new(1.15f, 1.8f);
        [SerializeField] float m_SidewaysSpeed = 0.65f;
        [SerializeField] float m_SpawnInterval = 0.075f;

        readonly Dictionary<string, GameObject> m_Prefabs = new(System.StringComparer.OrdinalIgnoreCase);
        Transform m_Source;

        public void Configure(GameObject[] prefabs)
        {
            m_Prefabs.Clear();
            if (prefabs == null)
                return;

            foreach (var prefab in prefabs)
            {
                if (prefab == null)
                    continue;
                m_Prefabs[Normalize(prefab.name)] = prefab;
            }
        }

        public void SetSource(Transform source)
        {
            m_Source = source;
        }

        public void ReleaseStage(int stage)
        {
            if (m_Source == null)
            {
                Debug.LogWarning("[Candy Spawn] No tracked Chocolate Capsule is available.");
                return;
            }

            var names = stage switch
            {
                20 => new[] { "Heart", "Star" },
                50 => new[] { "Cookie", "Macaron", "Heart" },
                80 => new[] { "Chocolate", "Cookie", "Star", "Chocolate", "Star" },
                100 => new[] { "Chocolate", "Cookie", "Heart", "Macaron", "Star", "Chocolate", "Cookie", "Heart", "Star" },
                _ => System.Array.Empty<string>()
            };
            StartCoroutine(Release(names, stage));
        }

        IEnumerator Release(string[] names, int stage)
        {
            Debug.Log($"[Candy Spawn] Energy stage {stage}% release started. count={names.Length}");
            foreach (var prefabName in names)
            {
                if (TryGetPrefab(prefabName, out var prefab))
                    Spawn(prefab);
                else
                    Debug.LogWarning($"[Candy Spawn] Prefab not found in selected world: {prefabName}");
                yield return new WaitForSeconds(m_SpawnInterval);
            }
        }

        void Spawn(GameObject prefab)
        {
            if (m_Source == null)
                return;

            var origin = m_Source.position + Vector3.up * m_HeightAboveCapsule;
            var horizontal = Random.insideUnitCircle * 0.055f;
            origin += new Vector3(horizontal.x, 0f, horizontal.y);
            var rotation = Random.rotation;
            var instance = Instantiate(prefab, origin, rotation);
            instance.name = $"{prefab.name} (Candy Energy)";

            EnsureCollider(instance);
            var body = instance.GetComponent<Rigidbody>();
            if (body == null)
                body = instance.AddComponent<Rigidbody>();
            body.useGravity = true;
            body.isKinematic = false;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.linearVelocity = Vector3.up * Random.Range(m_UpwardSpeed.x, m_UpwardSpeed.y) +
                                  new Vector3(
                                      Random.Range(-m_SidewaysSpeed, m_SidewaysSpeed),
                                      0f,
                                      Random.Range(-m_SidewaysSpeed, m_SidewaysSpeed));
            body.angularVelocity = Random.insideUnitSphere * 4f;
        }

        bool TryGetPrefab(string displayName, out GameObject prefab)
        {
            return m_Prefabs.TryGetValue(Normalize(displayName), out prefab);
        }

        static string Normalize(string value)
        {
            return value.Replace("Prefab", string.Empty).Replace("_", string.Empty).Trim();
        }

        static void EnsureCollider(GameObject instance)
        {
            var colliders = instance.GetComponentsInChildren<Collider>(true);
            if (colliders.Length > 0)
            {
                foreach (var collider in colliders)
                {
                    collider.enabled = true;
                    if (collider is MeshCollider meshCollider)
                        meshCollider.convex = true;
                }
                return;
            }

            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return;
            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            var colliderToAdd = instance.AddComponent<BoxCollider>();
            colliderToAdd.center = instance.transform.InverseTransformPoint(bounds.center);
            var scale = instance.transform.lossyScale;
            colliderToAdd.size = new Vector3(
                Divide(bounds.size.x, scale.x),
                Divide(bounds.size.y, scale.y),
                Divide(bounds.size.z, scale.z));
        }

        static float Divide(float value, float divisor) =>
            Mathf.Abs(divisor) > 0.0001f ? value / Mathf.Abs(divisor) : value;
    }
}
