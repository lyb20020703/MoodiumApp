using UnityEngine;

namespace Moodium.Rewards
{
    [DisallowMultipleComponent]
    public sealed class RewardOrb : MonoBehaviour
    {
        const float OrbDiameter = .07f;
        static Material s_GlassMaterial;
        [SerializeField] Transform m_MiniatureRoot;
        [SerializeField] float m_RotationDegreesPerSecond = 20f;
        [SerializeField] float m_FloatAmplitude = .004f;
        [SerializeField] float m_FloatPeriod = 1.4f;

        Vector3 m_InitialLocalPosition;
        float m_EnabledAt;

        /// <summary>Uses the project's PolySpatial transparent-white material for all reward glass.</summary>
        public static void ConfigureGlassMaterial(Material material) => s_GlassMaterial = material;

        public static RewardOrb Create(RewardEntry entry, bool natureWorld, Vector3 position, Transform parent = null)
        {
            var root = new GameObject("Reward Orb");
            if (parent != null)
                root.transform.SetParent(parent, false);
            root.transform.position = position;
            var orb = root.AddComponent<RewardOrb>();
            orb.BuildVisual(natureWorld);
            foreach (var prefab in entry.prefabs)
            {
                if (prefab == null)
                    continue;
                var miniature = Instantiate(prefab, root.transform);
                miniature.name = prefab.name + " (Orb Miniature)";
                PrepareMiniatureForOrb(miniature, entry.miniatureScaleOverride > 0f
                    ? entry.miniatureScaleOverride : .045f);
                orb.m_MiniatureRoot = miniature.transform;
                break;
            }
            return orb;
        }

        public static void PrepareMiniatureForOrb(GameObject miniature, float interiorDiameter)
        {
            if (miniature == null)
                return;

            foreach (var collider in miniature.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            foreach (var body in miniature.GetComponentsInChildren<Rigidbody>(true))
            {
                body.detectCollisions = false;
                body.isKinematic = true;
            }
            foreach (var particles in miniature.GetComponentsInChildren<ParticleSystem>(true))
            {
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var emission = particles.emission;
                emission.enabled = false;
            }
            foreach (var animator in miniature.GetComponentsInChildren<Animator>(true))
                animator.enabled = false;
            foreach (var behaviour in miniature.GetComponentsInChildren<MonoBehaviour>(true))
                if (behaviour is not RewardOrb)
                    behaviour.enabled = false;

            if (TryGetRendererBounds(miniature, out var bounds) && bounds.size.magnitude > .00001f)
                miniature.transform.localScale *= Mathf.Max(0f, interiorDiameter) / bounds.size.magnitude;
        }

        void BuildVisual(bool natureWorld)
        {
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "Glass Shell";
            sphere.transform.SetParent(transform, false);
            sphere.transform.localScale = Vector3.one * OrbDiameter;
            DestroyComponent(sphere.GetComponent<Collider>());
            var shell = sphere.GetComponent<Renderer>();
            ApplyGlass(shell, natureWorld ? new Color(.65f, .89f, .63f, .3f) : new Color(1f, .71f, .81f, .3f));

            var pedestal = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pedestal.name = "World Colour Pedestal";
            pedestal.transform.SetParent(transform, false);
            pedestal.transform.localPosition = Vector3.down * .018f;
            pedestal.transform.localScale = new Vector3(.025f, .002f, .025f);
            DestroyComponent(pedestal.GetComponent<Collider>());
            ApplyGlass(pedestal.GetComponent<Renderer>(), natureWorld ? new Color(.56f, .82f, .49f, .6f) : new Color(1f, .94f, .96f, .6f));
        }

        public static void ApplyGlass(Renderer renderer, Color color)
        {
            if (renderer == null)
                return;
            if (s_GlassMaterial != null)
                renderer.sharedMaterial = s_GlassMaterial;
            else
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                var fallback = new Material(shader) { color = color };
                fallback.SetFloat("_Smoothness", .95f);
                renderer.sharedMaterial = fallback;
            }
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetColor("_BaseColor", color);
            block.SetColor("_Color", color);
            renderer.SetPropertyBlock(block);
        }

        static void DestroyComponent(Object component)
        {
            if (component == null)
                return;
            if (Application.isPlaying)
                Destroy(component);
            else
                DestroyImmediate(component);
        }

        static bool TryGetRendererBounds(GameObject root, out Bounds combined)
        {
            combined = default;
            var found = false;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled)
                    continue;
                if (!found)
                {
                    combined = renderer.bounds;
                    found = true;
                }
                else
                    combined.Encapsulate(renderer.bounds);
            }
            return found;
        }

        void OnEnable()
        {
            m_InitialLocalPosition = transform.localPosition;
            m_EnabledAt = Time.unscaledTime;
        }

        void Update()
        {
            var elapsed = Time.unscaledTime - m_EnabledAt;
            if (m_MiniatureRoot != null)
                m_MiniatureRoot.Rotate(Vector3.up, m_RotationDegreesPerSecond * Time.unscaledDeltaTime, Space.Self);
            transform.localPosition = m_InitialLocalPosition + Vector3.up *
                (Mathf.Sin(elapsed / Mathf.Max(.01f, m_FloatPeriod) * Mathf.PI * 2f) * m_FloatAmplitude);
        }
    }
}
