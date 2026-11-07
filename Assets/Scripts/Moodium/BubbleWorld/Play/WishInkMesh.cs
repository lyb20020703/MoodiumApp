using System.Collections.Generic;
using Unity.PolySpatial;
using UnityEngine;
using UnityEngine.Rendering;

namespace Moodium.NatureWorld
{
    public sealed class WishInkMesh : MonoBehaviour
    {
        public const int PaperOrder = 4, GlowOrder = 5, InkOrder = 6;
        public const float InkLiftMetres = .0012f, GlowLiftMetres = .0008f;
        const int MaxActivePoints = 75;
        float inkScale = 1f, glowScale = 1f;
        public float InkScale => inkScale;
        public float GlowScale => glowScale;
        readonly List<Material> owned = new();
        readonly List<Mesh> meshes = new();
        Transform paper;
        WishPaperFrame frame;
        Mesh current, currentGlow, done, doneGlow;
        MeshRenderer currentRenderer, currentGlowRenderer, doneRenderer, doneGlowRenderer;

        public void Initialize(Transform strip, WishPaperFrame mapping, bool candy)
        {
            paper = strip; frame = mapping; transform.SetParent(strip, false);
            ColorUtility.TryParseHtmlString(candy ? "#C93D78" : "#3E2A16", out var color);
            // Both layers use the stock transparent Unlit path: erasing fades the main ink too.
            var ink = BubbleWorldData.CreateUnlit("Wish ink", null, true, CullMode.Off, color);
            var glow = BubblePlayVisuals.Material(candy ? "#FF78B4" : "#FFE98C", .35f);
            ink.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            owned.Add(ink); owned.Add(glow);
            current = Create("Current Ink", ink, InkOrder, out currentRenderer);
            currentGlow = Create("Current Ink Glow", glow, GlowOrder, out currentGlowRenderer);
            done = Create("Completed Ink", ink, InkOrder, out doneRenderer);
            doneGlow = Create("Completed Ink Glow", glow, GlowOrder, out doneGlowRenderer);
        }

        Mesh Create(string name, Material material, int order, out MeshRenderer renderer)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false);
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.MarkDynamic(); meshes.Add(mesh);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.allowOcclusionWhenDynamic = false;
            renderer.sortingOrder = order;
            Moodium.UI.UIRefreshVisuals.Register(paper, go, order);
            return mesh;
        }

        // The transient tip is not subject to journal decimation/smoothing: what the user is
        // drawing is visible now, while persisted points retain the original jitter filtering.
        public void Active(List<float[]> stroke, Vector2? tip = null)
        {
            var points = new List<float[]>();
            if (stroke != null) points.AddRange(stroke);
            if (tip.HasValue && points.Count > 0)
            {
                var last = points[points.Count - 1];
                if ((new Vector2(last[0], last[1]) - tip.Value).sqrMagnitude > .00000001f)
                    points.Add(new[] { tip.Value.x, tip.Value.y });
            }
            // Keep both the beginning and live endpoint without exceeding the 300-vertex budget.
            if (points.Count > MaxActivePoints)
            {
                var reduced = new List<float[]>(MaxActivePoints);
                for (int i = 0; i < MaxActivePoints; i++)
                    reduced.Add(points[Mathf.RoundToInt(i * (points.Count - 1f) / (MaxActivePoints - 1f))]);
                points = reduced;
            }
            var strokes = new List<List<float[]>> { points };
            Build(current, currentRenderer, strokes, false);
            Build(currentGlow, currentGlowRenderer, strokes, true);
        }

        public void Completed(List<List<float[]>> strokes)
        {
            Build(done, doneRenderer, strokes, false);
            Build(doneGlow, doneGlowRenderer, strokes, true);
        }
        public void Alpha(float value)
        {
            for (int i = 0; i < owned.Count; i++)
            {
                BubbleWorldData.SetAlpha(owned[i], Mathf.Clamp01(value) * (i == 1 ? .35f : 1f));
                PolySpatialObjectUtils.MarkDirty(owned[i]);
            }
        }
        /// <summary>Line width multipliers (1 = handwriting). Takes effect on the next Active / Completed call.</summary>
        public void SetWidth(float ink, float glow)
        {
            inkScale = Mathf.Max(.1f, ink); glowScale = Mathf.Max(.1f, glow);
        }
        public void Show(bool value)
        {
            currentRenderer.enabled = currentGlowRenderer.enabled = doneRenderer.enabled = doneGlowRenderer.enabled = value;
        }

        Vector3 OnPaper(Vector2 uv, float lift)
        {
            var normal = Vector3.Cross(paper.TransformVector(frame.UpAt(uv)), paper.TransformVector(frame.RightAt(uv))).normalized;
            // Use real metres even under the enlarged paper and FBX import transforms.
            return paper.InverseTransformPoint(paper.TransformPoint(frame.Surface(uv)) + normal * lift);
        }
        void Build(Mesh mesh, MeshRenderer renderer, List<List<float[]>> strokes, bool glow)
        {
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            float halfWidth = glow ? .0024f * glowScale : .0008f * inkScale;
            float lift = glow ? GlowLiftMetres : InkLiftMetres;
            foreach (var stroke in strokes)
            {
                if (stroke == null || stroke.Count == 0) continue;
                int first = vertices.Count;
                if (stroke.Count == 1)
                {
                    // First contact is a visible nib dot, not an empty mesh until movement.
                    var uv = new Vector2(stroke[0][0], stroke[0][1]);
                    var p = frame.Surface(uv);
                    var right = frame.RightAt(uv).normalized * halfWidth;
                    var up = frame.UpAt(uv).normalized * halfWidth;
                    foreach (var corner in new[] { p - right - up, p - right + up, p + right - up, p + right + up })
                        vertices.Add(OnPaper(frame.Project(corner), lift));
                    AddQuad(triangles, first); continue;
                }
                for (int i = 0; i < stroke.Count; i++)
                {
                    var uv = new Vector2(stroke[i][0], stroke[i][1]);
                    var p = frame.Surface(uv);
                    var before = stroke[Mathf.Max(0, i - 1)]; var after = stroke[Mathf.Min(stroke.Count - 1, i + 1)];
                    var tangent = frame.Surface(new Vector2(after[0], after[1])) - frame.Surface(new Vector2(before[0], before[1]));
                    if (tangent.sqrMagnitude < 1e-12f) tangent = frame.UpAt(uv);
                    var normal = Vector3.Cross(frame.UpAt(uv), frame.RightAt(uv)).normalized;
                    var side = Vector3.Cross(normal, tangent).normalized * halfWidth;
                    vertices.Add(OnPaper(frame.Project(p - side), lift));
                    vertices.Add(OnPaper(frame.Project(p + side), lift));
                    if (i > 0) AddQuad(triangles, first + (i - 1) * 2);
                }
            }
            mesh.Clear(); mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            // Explicitly notify the PolySpatial renderer after each live mesh update.
            PolySpatialObjectUtils.MarkDirty(renderer);
        }
        static void AddQuad(List<int> triangles, int a)
        {
            triangles.Add(a); triangles.Add(a + 1); triangles.Add(a + 2);
            triangles.Add(a + 1); triangles.Add(a + 3); triangles.Add(a + 2);
        }
        void OnDestroy()
        {
            foreach (var material in owned) BubblePlayVisuals.Release(material);
            foreach (var mesh in meshes) BubblePlayVisuals.Release(mesh);
        }
    }
}
