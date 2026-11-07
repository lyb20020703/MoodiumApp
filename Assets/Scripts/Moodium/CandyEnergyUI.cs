using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace Moodium.CandyWorld
{
    public sealed class CandyEnergyUI : MonoBehaviour
    {
        const int RingSegments = 72;
        const float RingOuterRadius = 0.09f;
        const float RingInnerRadius = 0.073f;

        static readonly Color LowEnergyColor = new(0.48f, 0.55f, 1f, 1f);
        static readonly Color HighEnergyColor = new(1f, 0.43f, 0.83f, 1f);

        Transform m_Camera;
        TMP_Text m_Percentage;
        Mesh m_FillMesh;
        Material m_FillMaterial;
        Transform m_ProgressHead;
        Renderer m_ProgressHeadRenderer;
        float m_TargetProgress;
        float m_DisplayedProgress;
        Vector3 m_BaseScale;
        bool m_Built;

        public void Build(Camera camera, GameObject glassPanelPrefab, TMP_FontAsset font)
        {
            m_Camera = camera != null ? camera.transform : null;
            m_BaseScale = transform.localScale;

            if (glassPanelPrefab != null)
            {
                var glass = Instantiate(glassPanelPrefab, transform, false);
                glass.name = "Candy Energy Floating Glass Widget";
                glass.transform.localScale = new Vector3(0.30f / 0.72f, 0.36f / 0.43f, 1f);
                foreach (var label in glass.GetComponentsInChildren<TMP_Text>(true))
                    label.gameObject.SetActive(false);
                foreach (var renderer in glass.GetComponentsInChildren<MeshRenderer>(true))
                    renderer.sortingOrder += 20;
            }

            CreateText("Candy Energy", new Vector3(0f, 0.132f, -0.047f), 2.35f, font, new Vector2(2.8f, 0.55f));
            m_Percentage = CreateText("0%", new Vector3(0f, -0.012f, -0.052f), 4.1f, font, new Vector2(2.3f, 0.8f));

            CreateRing("Energy Ring Track", 1f, new Color(0.25f, 0.28f, 0.48f, 0.42f), -0.041f, out _);
            CreateRing("Energy Ring Progress", 0.001f, LowEnergyColor, -0.052f, out m_FillMesh);

            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "Energy Progress Glow Point";
            head.transform.SetParent(transform, false);
            head.transform.localScale = Vector3.one * 0.014f;
            Destroy(head.GetComponent<Collider>());
            m_ProgressHead = head.transform;
            m_ProgressHeadRenderer = head.GetComponent<Renderer>();
            m_ProgressHeadRenderer.sharedMaterial = CreateMaterial("Candy Energy Head", LowEnergyColor, true);
            m_ProgressHeadRenderer.sortingOrder = 35;
            head.SetActive(false);

            m_Built = true;
            SetProgress(0f, 0f);
            UpdateRingVisual(0f);
        }

        public void SetProgress(float energy, float normalized)
        {
            m_TargetProgress = Mathf.Clamp01(normalized);
            if (m_Percentage != null)
                m_Percentage.text = $"{Mathf.RoundToInt(energy)}%";
        }

        void LateUpdate()
        {
            if (m_Camera != null)
            {
                var forward = Vector3.ProjectOnPlane(m_Camera.forward, Vector3.up).normalized;
                if (forward.sqrMagnitude < 0.01f)
                    forward = m_Camera.forward.normalized;
                // Keep the compact HUD above the tracked object interaction zone and
                // leave a clear vertical corridor to the independent bottom Back button.
                var target = m_Camera.position + forward * 0.95f + Vector3.up * 0.26f;
                transform.position = Vector3.Lerp(transform.position, target, 1f - Mathf.Exp(-6f * Time.deltaTime));
                transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            }

            if (!m_Built)
                return;
            var previous = m_DisplayedProgress;
            m_DisplayedProgress = Mathf.Lerp(
                m_DisplayedProgress,
                m_TargetProgress,
                1f - Mathf.Exp(-7.5f * Time.deltaTime));
            if (Mathf.Abs(previous - m_DisplayedProgress) > 0.0001f)
                UpdateRingVisual(m_DisplayedProgress);

            if (m_TargetProgress >= 0.999f)
            {
                var pulse = 1f + (Mathf.Sin(Time.unscaledTime * 5.5f) * 0.5f + 0.5f) * 0.035f;
                transform.localScale = m_BaseScale * pulse;
                if (m_ProgressHead != null)
                    m_ProgressHead.localScale = Vector3.one * (0.014f + 0.004f * pulse);
            }
            else
            {
                transform.localScale = Vector3.Lerp(transform.localScale, m_BaseScale, 1f - Mathf.Exp(-8f * Time.deltaTime));
            }
        }

        void CreateRing(string objectName, float progress, Color color, float z, out Mesh mesh)
        {
            var ring = new GameObject(objectName, typeof(MeshFilter), typeof(MeshRenderer));
            ring.transform.SetParent(transform, false);
            ring.transform.localPosition = new Vector3(0f, -0.012f, z);
            mesh = new Mesh { name = $"{objectName} Mesh" };
            BuildRingMesh(mesh, progress);
            ring.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = ring.GetComponent<MeshRenderer>();
            var material = CreateMaterial($"{objectName} Material", color, objectName.Contains("Progress"));
            renderer.sharedMaterial = material;
            renderer.sortingOrder = objectName.Contains("Progress") ? 32 : 28;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            if (objectName.Contains("Progress"))
                m_FillMaterial = material;
        }

        void UpdateRingVisual(float progress)
        {
            if (m_FillMesh == null)
                return;
            BuildRingMesh(m_FillMesh, Mathf.Max(0.001f, progress));
            var color = Color.Lerp(LowEnergyColor, HighEnergyColor, Mathf.SmoothStep(0f, 1f, progress));
            SetMaterialColor(m_FillMaterial, color);

            if (m_ProgressHead != null)
            {
                m_ProgressHead.gameObject.SetActive(progress > 0.006f);
                var angle = Mathf.PI * 0.5f - progress * Mathf.PI * 2f;
                var radius = (RingOuterRadius + RingInnerRadius) * 0.5f;
                m_ProgressHead.localPosition = new Vector3(
                    Mathf.Cos(angle) * radius,
                    -0.012f + Mathf.Sin(angle) * radius,
                    -0.059f);
                SetMaterialColor(m_ProgressHeadRenderer.sharedMaterial, color);
            }
        }

        static void BuildRingMesh(Mesh mesh, float progress)
        {
            var clamped = Mathf.Clamp01(progress);
            var usedSegments = Mathf.Max(1, Mathf.CeilToInt(RingSegments * clamped));
            var vertices = new Vector3[(usedSegments + 1) * 2];
            var triangles = new int[usedSegments * 6];

            for (var i = 0; i <= usedSegments; i++)
            {
                var t = Mathf.Min((float)i / RingSegments, clamped);
                var angle = Mathf.PI * 0.5f - t * Mathf.PI * 2f;
                var direction = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                vertices[i * 2] = direction * RingInnerRadius;
                vertices[i * 2 + 1] = direction * RingOuterRadius;
            }
            for (var i = 0; i < usedSegments; i++)
            {
                var vertex = i * 2;
                var triangle = i * 6;
                triangles[triangle] = vertex;
                triangles[triangle + 1] = vertex + 1;
                triangles[triangle + 2] = vertex + 3;
                triangles[triangle + 3] = vertex;
                triangles[triangle + 4] = vertex + 3;
                triangles[triangle + 5] = vertex + 2;
            }
            mesh.Clear();
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
        }

        TMP_Text CreateText(string value, Vector3 position, float size, TMP_FontAsset font, Vector2 rectSize)
        {
            var item = new GameObject($"Text - {value}");
            item.transform.SetParent(transform, false);
            item.transform.localPosition = position;
            item.transform.localScale = Vector3.one * 0.1f;
            var text = item.AddComponent<TextMeshPro>();
            if (font != null)
                text.font = font;
            text.text = value;
            text.fontSize = size;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.rectTransform.sizeDelta = rectSize;
            text.renderer.sortingOrder = 40;
            return text;
        }

        static Material CreateMaterial(string materialName, Color color, bool emission)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            if (shader == null)
                return null;
            var material = new Material(shader) { name = materialName };
            SetMaterialColor(material, color);
            if (emission && material.HasProperty("_EmissionColor"))
                material.SetColor("_EmissionColor", color * 1.5f);
            return material;
        }

        static void SetMaterialColor(Material material, Color color)
        {
            if (material == null)
                return;
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
            if (material.HasProperty("_EmissionColor"))
                material.SetColor("_EmissionColor", color * 1.5f);
        }
    }
}
