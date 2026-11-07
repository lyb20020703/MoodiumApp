using UnityEngine;
using UnityEngine.Rendering;

namespace Moodium.Reality
{
    /// <summary>Small native-renderable foil patches; never deforms or recooks the AR mesh.</summary>
    public sealed class CandyWrapperContactFeedback : MonoBehaviour
    {
        sealed class Patch
        {
            public Transform Root;
            public Material Material;
            public float Weight;
            public bool Held;
            public float Released;
            public Vector3 Point;
            public Vector3 Normal = Vector3.up;
        }

        readonly Patch[] m_Patches = new Patch[2];
        Mesh m_Mesh;
        AudioClip m_Rustle;
        AudioSource m_Audio;
        float m_FlashUntil;
        bool m_Ready;
        float m_Progress;
        float m_PreviewUntil;

        public void Initialize()
        {
            if (m_Ready) return;
            m_Ready = true;
            m_Mesh = BuildFoldMesh();
            for (var i = 0; i < m_Patches.Length; i++)
            {
                var root = new GameObject("Candy Foil Contact " + i);
                root.transform.SetParent(transform, false);
                root.AddComponent<MeshFilter>().sharedMesh = m_Mesh;
                var material = new Material(Shader.Find("Universal Render Pipeline/Lit"))
                    { name = "Candy Foil Contact Material " + i };
                ConfigureFoilMaterial(material);
                var renderer = root.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                root.SetActive(false);
                m_Patches[i] = new Patch { Root = root.transform, Material = material };
            }
            var sound = new GameObject("Candy Paper Rustle");
            sound.transform.SetParent(transform, false);
            m_Audio = sound.AddComponent<AudioSource>();
            m_Audio.playOnAwake = false;
            m_Audio.loop = true;
            m_Audio.spatialBlend = 1f;
            m_Audio.minDistance = .2f;
            m_Audio.maxDistance = 3f;
            m_Audio.volume = 0f;
            m_Rustle = BuildRustle();
            m_Audio.clip = m_Rustle;
        }

        public void SetProgress(float progress)
        {
            if (m_Progress < .999f && progress >= .999f) m_FlashUntil = Time.time + .65f;
            m_Progress = progress;
        }

        public void SetContact(int index, Vector4 press, Vector3 normal)
        {
            if (!m_Ready || index < 0 || index >= m_Patches.Length) return;
            if (index == 0 && Time.time < m_PreviewUntil) return;
            var patch = m_Patches[index];
            var held = press.w > .0001f;
            if (held)
            {
                var point = new Vector3(press.x, press.y, press.z);
                if (!patch.Held || Vector3.Distance(patch.Point, point) > .006f)
                {
                    patch.Point = point;
                    patch.Normal = normal.sqrMagnitude > .001f ? normal.normalized : Vector3.up;
                    patch.Root.SetPositionAndRotation(point + patch.Normal * .006f,
                        Quaternion.FromToRotation(Vector3.forward, patch.Normal));
                }
                if (patch.Weight <= .001f) patch.Weight = .001f;
            }
            if (patch.Held && !held) patch.Released = Time.time;
            patch.Held = held;
        }

        void Update()
        {
            if (!m_Ready) return;
            if (m_PreviewUntil > 0f && Time.time >= m_PreviewUntil)
            {
                m_PreviewUntil = 0f;
                SetContact(0, Vector4.zero, Vector3.up);
            }
            var anyHeld = false;
            var camera = Camera.main;
            foreach (var patch in m_Patches)
            {
                anyHeld |= patch.Held;
                patch.Weight = Mathf.MoveTowards(patch.Weight, patch.Held ? 1f : 0f,
                    Time.deltaTime * (patch.Held ? 5f : 1.4f));
                patch.Root.gameObject.SetActive(patch.Weight > .0001f);
                if (patch.Weight <= .0001f) continue;
                var release = patch.Held ? 0f : Mathf.Clamp01((Time.time - patch.Released) / .7f);
                // Expands after release to suggest paper relaxing, rather than a persistent stain.
                var radius = Mathf.Lerp(.045f, .14f, patch.Weight) + release * .07f;
                patch.Root.localScale = Vector3.one * radius;
                var grazing = camera == null ? .5f : 1f - Mathf.Abs(Vector3.Dot(patch.Normal,
                    (camera.transform.position - patch.Point).normalized));
                var angle = camera == null ? 0f : Vector3.Dot(camera.transform.right, patch.Normal);
                var pink = new Color(1f, .63f, .79f);
                var gold = new Color(1f, .88f, .58f);
                var color = Color.Lerp(pink, gold, Mathf.Clamp01(.5f + angle * .5f));
                color.a = patch.Weight * Mathf.Lerp(.12f, .32f, grazing);
                patch.Material.SetColor("_BaseColor", color);
                var flash = Mathf.Clamp01((m_FlashUntil - Time.time) / .65f);
                patch.Material.SetColor("_EmissionColor", color * (.03f + .12f * flash));
            }
            m_Audio.volume = Mathf.MoveTowards(m_Audio.volume, anyHeld ? .16f : 0f, Time.deltaTime * .8f);
            if (anyHeld)
            {
                var patch = m_Patches[0].Held ? m_Patches[0] : m_Patches[1];
                m_Audio.transform.position = patch.Point;
                if (!m_Audio.isPlaying) m_Audio.Play();
            }
            else if (m_Audio.volume <= .001f && m_Audio.isPlaying) m_Audio.Stop();
        }

        public void ClearFeedback()
        {
            m_PreviewUntil = 0f;
            m_Progress = 0f;
            m_FlashUntil = 0f;
            if (!m_Ready) return;
            foreach (var patch in m_Patches)
            {
                patch.Held = false;
                patch.Weight = 0f;
                patch.Root.gameObject.SetActive(false);
            }
            m_Audio.Stop();
            m_Audio.volume = 0f;
        }

        [ContextMenu("Preview Candy Foil Contact (Play Mode)")]
        void PreviewContact()
        {
            if (!Application.isPlaying || Camera.main == null) return;
            Initialize();
            enabled = true;
            m_PreviewUntil = 0f;
            var camera = Camera.main.transform;
            var point = camera.position + camera.forward * .65f;
            SetContact(0, new Vector4(point.x, point.y, point.z, .02f), -camera.forward);
            m_PreviewUntil = Time.time + 1.2f;
        }

        static void ConfigureFoilMaterial(Material material)
        {
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_EMISSION");
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.SetFloat("_Metallic", .18f);
            material.SetFloat("_Smoothness", .94f);
            material.renderQueue = 3010;
        }

        public static Mesh BuildFoldMesh()
        {
            // Each crease is a pair of narrow triangles, separated by clear space.
            const int folds = 18;
            var vertices = new Vector3[folds * 6];
            var triangles = new int[vertices.Length];
            var colors = new Color[vertices.Length];
            for (var i = 0; i < folds; i++)
            {
                var angle = i * Mathf.PI * 2f / folds;
                var direction = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                var side = new Vector3(-direction.y, direction.x, 0f);
                var inner = direction * (.1f + (i % 3) * .04f);
                var outer = direction * (i % 2 == 0 ? 1f : .78f);
                var ridge = direction * .48f + Vector3.forward * .018f;
                var k = i * 6;
                vertices[k] = inner;
                vertices[k + 1] = outer - side * .035f;
                vertices[k + 2] = ridge;
                vertices[k + 3] = ridge;
                vertices[k + 4] = outer + side * .035f;
                vertices[k + 5] = inner;
                for (var j = 0; j < 6; j++)
                {
                    triangles[k + j] = k + j;
                    colors[k + j] = Color.white;
                }
            }
            var mesh = new Mesh { name = "Candy Foil Creases (108 vertices)" };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.colors = colors;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static AudioClip BuildRustle()
        {
            const int rate = 22050;
            var data = new float[rate];
            var random = new System.Random(714);
            float previous = 0f;
            for (var i = 0; i < data.Length; i++)
            {
                var noise = (float)random.NextDouble() * 2f - 1f;
                var envelope = Mathf.Pow(.5f + .5f * Mathf.Sin(i * .0018f), 5f);
                var seam = Mathf.Min(1f, Mathf.Min(i, data.Length - 1 - i) / 220f);
                data[i] = (noise - previous * .8f) * envelope * seam * .22f;
                previous = noise;
            }
            var clip = AudioClip.Create("Candy Foil Rustle Prototype", data.Length, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        void OnDisable() => ClearFeedback();
        void OnDestroy()
        {
            if (m_Mesh != null) Destroy(m_Mesh);
            if (m_Rustle != null) Destroy(m_Rustle);
            foreach (var patch in m_Patches)
                if (patch != null && patch.Material != null) Destroy(patch.Material);
        }
    }
}
