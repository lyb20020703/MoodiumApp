using UnityEngine;
using Moodium.Reality;

namespace Moodium.CandyWorld
{
    public sealed class CandyProjectile : MonoBehaviour
    {
        [SerializeField, Min(3f)] float m_MaxLifetime = 20f;
        [SerializeField, Range(0.1f, 1f)] float m_ColliderScale = 0.35f;

        Collider[] m_Colliders;
        ISqueezeProgressDisplay m_ProtectedVideo;
        Renderer[] m_Visuals;
        float m_VideoProtectionStarted;
        float m_VideoOverlapTime;

        public void ProtectVideo(ISqueezeProgressDisplay video)
        {
            m_ProtectedVideo = video;
            m_Visuals = GetComponentsInChildren<Renderer>(true);
            m_VideoProtectionStarted = Time.time;
            m_VideoOverlapTime = 0f;
        }

        void LateUpdate() => CheckVideoExclusion();

        void CheckVideoExclusion()
        {
            // The emitter lies inside the video region: let the initial burst
            // leave it before considering cleanup, then require sustained overlap.
            if (m_ProtectedVideo == null || Time.time - m_VideoProtectionStarted < .35f)
            {
                m_VideoOverlapTime = 0f;
                return;
            }
            var bounds = new Bounds(transform.position, Vector3.zero);
            var hasBounds = false;
            foreach (var visual in m_Visuals)
            {
                if (visual == null || !visual.enabled) continue;
                if (!hasBounds) { bounds = visual.bounds; hasBounds = true; }
                else bounds.Encapsulate(visual.bounds);
            }
            m_VideoOverlapTime = m_ProtectedVideo.OverlapsBurstVideo(bounds)
                ? m_VideoOverlapTime + Time.deltaTime : 0f;
            if (m_VideoOverlapTime >= .2f)
            {
                // Hide synchronously; Destroy alone leaves the renderer visible until frame end.
                gameObject.SetActive(false);
                Destroy(gameObject);
                return;
            }
        }

        public static bool CrossesProtectedSphere(Vector3 from, Vector3 to, float radius)
        {
            var delta = to - from;
            var t = delta.sqrMagnitude > 1e-10f
                ? Mathf.Clamp01(-Vector3.Dot(from, delta) / delta.sqrMagnitude) : 0f;
            return (from + delta * t).sqrMagnitude <= radius * radius;
        }

        public Collider[] Colliders => m_Colliders ?? System.Array.Empty<Collider>();

        public void Launch(Vector3 velocity, float scaleMultiplier)
        {
            transform.localScale *= scaleMultiplier;
            EnsureCollider();
            m_Colliders = GetComponentsInChildren<Collider>(true);
            TightenColliders();
            var landingFeedback = GetComponent<CandyPhysicsObject>();
            if (landingFeedback == null)
                landingFeedback = gameObject.AddComponent<CandyPhysicsObject>();
            landingFeedback.ConfigureFromPrefabFeedback();
            var body = GetComponent<Rigidbody>();
            if (body == null)
                body = gameObject.AddComponent<Rigidbody>();
            body.useGravity = true;
            body.isKinematic = false;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.mass = 0.08f;
            body.linearDamping = 0.04f;
            body.maxLinearVelocity = 3f;
            body.linearVelocity = velocity;
            body.angularVelocity = Random.insideUnitSphere * 5f;
            Destroy(gameObject, m_MaxLifetime);
        }

        public void IgnoreCollisionsWith(Collider[] others)
        {
            if (others == null)
                return;
            foreach (var own in Colliders)
            foreach (var other in others)
            {
                if (own != null && other != null && own != other)
                    Physics.IgnoreCollision(own, other, true);
            }
        }

        void TightenColliders()
        {
            foreach (var collider in Colliders)
            {
                switch (collider)
                {
                    case BoxCollider box:
                        box.size *= m_ColliderScale;
                        break;
                    case SphereCollider sphere:
                        sphere.radius *= m_ColliderScale;
                        break;
                    case CapsuleCollider capsule:
                        capsule.radius *= m_ColliderScale;
                        capsule.height *= m_ColliderScale;
                        break;
                }
            }
        }

        void EnsureCollider()
        {
            var colliders = GetComponentsInChildren<Collider>(true);
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

            var renderers = GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return;
            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            var colliderToAdd = gameObject.AddComponent<BoxCollider>();
            colliderToAdd.center = transform.InverseTransformPoint(bounds.center);
            var scale = transform.lossyScale;
            colliderToAdd.size = new Vector3(
                Divide(bounds.size.x, scale.x),
                Divide(bounds.size.y, scale.y),
                Divide(bounds.size.z, scale.z));
        }

        static float Divide(float value, float divisor) =>
            Mathf.Abs(divisor) > 0.0001f ? value / Mathf.Abs(divisor) : value;
    }
}
