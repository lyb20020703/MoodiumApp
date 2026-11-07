// Reference implementation for the executing agent (not part of the repo yet).
// Suggested location: Assets/Scripts/Moodium/Squeeze/RoundedRectBandMesh.cs
//
// Builds a flat band that follows a rounded rectangle (superellipse, exponent 3.4) in the XZ plane
// of the bag root. Used for every tabletop guide line:
//   rhythm ring      : halfSize = (bag/2 + 0.02) * k, innerOffset = -0.003, bandWidth = 0.006, texture rhythm_ring_band
//   breath tide      : halfSize = bag/2, innerOffset = 0.004, bandWidth = spread (0.012 -> 0.065), texture breath_tide_band
//   breath trace     : halfSize = bag/2, innerOffset = 0.017, bandWidth = 0.006, fill = progress, texture rhythm_ring_band
//   exhale ripples   : halfSize = bag/2, innerOffset = 0.005 -> 0.12, bandWidth = 0.004, texture rhythm_ring_band
// UV: u runs along the outline (tiled), v runs across the band (0 = inner edge, 1 = outer edge).
// Material: URP Unlit, Surface Type Transparent, texture Wrap Mode U = Repeat, V = Clamp.

using UnityEngine;

namespace Moodium.Squeeze
{
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class RoundedRectBandMesh : MonoBehaviour
    {
        const float k_Exponent = 3.4f;

        [SerializeField] Vector2 m_HalfSize = new Vector2(0.125f, 0.085f);   // x = half width, y = half length (towards the user is +Z)
        [SerializeField] float m_InnerOffset = 0.004f;
        [SerializeField] float m_BandWidth = 0.006f;
        [SerializeField, Range(0f, 1f)] float m_Fill = 1f;                    // grows symmetrically from the middle of the near (+Z) side
        [SerializeField, Min(16)] int m_Segments = 256;
        [SerializeField] float m_UTiling = 8f;

        Mesh m_Mesh;
        Vector3[] m_Vertices;
        Vector2[] m_Uvs;
        int[] m_Triangles;

        void Awake() => Rebuild();
        void OnValidate() { if (m_Mesh != null) Rebuild(); }

        public void Set(Vector2 halfSize, float innerOffset, float bandWidth, float fill)
        {
            if(m_HalfSize==halfSize&&Mathf.Approximately(m_InnerOffset,innerOffset)&&
                Mathf.Approximately(m_BandWidth,bandWidth)&&Mathf.Approximately(m_Fill,fill))return;
            m_HalfSize = halfSize;
            m_InnerOffset = innerOffset;
            m_BandWidth = bandWidth;
            m_Fill = Mathf.Clamp01(fill);
            Rebuild();
        }

        void Rebuild()
        {
            if (m_Mesh == null)
            {
                m_Mesh = new Mesh { name = "RoundedRectBand" };
                m_Mesh.MarkDynamic();
                GetComponent<MeshFilter>().sharedMesh = m_Mesh;
            }

            int steps = Mathf.Max(16,m_Segments);
            int vertexCount = (steps + 1) * 2;
            if (m_Vertices == null || m_Vertices.Length != vertexCount)
            {
                m_Vertices = new Vector3[vertexCount];
                m_Uvs = new Vector2[vertexCount];
                m_Triangles = new int[steps * 6];
                m_Mesh.Clear();
            }

            if (m_Fill <= 0f)
            {
                m_Mesh.Clear();
                return;
            }

            float start = Mathf.PI * 0.5f - Mathf.PI * m_Fill;     // t = PI/2 is the middle of the +Z (user) side
            float span = 2f * Mathf.PI * m_Fill;
            for (int i = 0; i <= steps; i++)
            {
                float t = start + span * i / steps;
                Vector2 p = Point(t);
                Vector2 n = Normal(t, p);
                Vector2 inner = p + n * m_InnerOffset;
                Vector2 outer = p + n * (m_InnerOffset + m_BandWidth);
                m_Vertices[i * 2] = new Vector3(inner.x, 0f, inner.y);
                m_Vertices[i * 2 + 1] = new Vector3(outer.x, 0f, outer.y);
                float u = m_UTiling * i / steps;
                m_Uvs[i * 2] = new Vector2(u, 0f);
                m_Uvs[i * 2 + 1] = new Vector2(u, 1f);
            }

            for (int i = 0; i < steps; i++)
            {
                int a = i * 2, b = a + 1, c = a + 2, d = a + 3;      // a/c inner, b/d outer
                int k = i * 6;
                // Clockwise when seen from above (+Y), so the band faces up.
                m_Triangles[k] = a; m_Triangles[k + 1] = c; m_Triangles[k + 2] = b;
                m_Triangles[k + 3] = b; m_Triangles[k + 4] = c; m_Triangles[k + 5] = d;
            }

            m_Mesh.vertices = m_Vertices;
            m_Mesh.uv = m_Uvs;
            m_Mesh.triangles = m_Triangles;
            m_Mesh.RecalculateNormals();
            m_Mesh.RecalculateBounds();
        }

        Vector2 Point(float t)
        {
            float c = Mathf.Cos(t), s = Mathf.Sin(t);
            return new Vector2(
                m_HalfSize.x * Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), 2f / k_Exponent),
                m_HalfSize.y * Mathf.Sign(s) * Mathf.Pow(Mathf.Abs(s), 2f / k_Exponent));
        }

        public static Vector3 Outline(Vector2 halfSize,float angle,float offset=0)
        {
            var c=Mathf.Cos(angle);var s=Mathf.Sin(angle);
            var point=new Vector3(halfSize.x*Mathf.Sign(c)*Mathf.Pow(Mathf.Abs(c),2f/3.4f),0,
                halfSize.y*Mathf.Sign(s)*Mathf.Pow(Mathf.Abs(s),2f/3.4f));
            return point+point.normalized*offset;
        }
        void OnDestroy(){if(m_Mesh!=null){if(Application.isPlaying)Destroy(m_Mesh);else DestroyImmediate(m_Mesh);}}

        Vector2 Normal(float t, Vector2 p)
        {
            Vector2 tangent = Point(t + 1e-3f) - Point(t - 1e-3f);
            Vector2 n = new Vector2(tangent.y, -tangent.x).normalized;
            return Vector2.Dot(n, p) < 0f ? -n : n;
        }
    }
}
