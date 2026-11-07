using System.Collections.Generic;
using UnityEngine;

namespace Moodium.NatureWorld
{
    /// <summary>
    /// Touching the real room with a fingertip plants a vine there (Nature world, after 100%), like touching a wall in
    /// the Ivy Studio reference on Vision Pro. Pure rules; NatureIvySpatialGrowthController samples the hands.
    /// Uses Unity-side physics against the scanned room colliders, so it does not depend on PolySpatial collider sync.
    /// </summary>
    public static class NatureVineTouch
    {
        /// <summary>A fingertip this close to the room mesh is touching it.</summary>
        public const float TouchDistance = .03f;
        /// <summary>The same hand has to move this far off the surface before its next touch counts.</summary>
        public const float ReleaseDistance = .06f;
        public const float HandCooldown = 1.2f;
        /// <summary>Minimum distance from that hand's previous vine, so one long touch does not stack vines.</summary>
        public const float MinSpacing = .18f;
        /// <summary>Squeezing the sensory bag on the table also brings fingers to the table: never plant next to it.</summary>
        public const float SoftKeepOut = .22f;
        /// <summary>At most this many vines grow at the same time (each reveal re-uploads its meshes every frame).</summary>
        public const int MaxGrowing = 3;

        static readonly Vector3[] s_Directions = { Vector3.down, Vector3.forward, Vector3.back, Vector3.left, Vector3.right, Vector3.up };

        /// <summary>
        /// Nearest room-mesh point within <paramref name="maxDistance"/> of <paramref name="tip"/>. Each probe starts 8 cm
        /// outside the tip, so a fingertip pressed slightly into the (coarse) room mesh still finds the surface.
        /// The hit normal faces the side the finger came from.
        /// </summary>
        public static bool TryFindSurface(Vector3 tip, IEnumerable<Collider> colliders, float maxDistance, out RaycastHit best)
        {
            best = default;
            var found = false;
            var bestDistance = maxDistance;
            foreach (var direction in s_Directions)
            {
                if (!NatureIvySurfaceResolver.TryRaycast(colliders, new Ray(tip - direction * .08f, direction), .08f + maxDistance, out var hit))
                    continue;
                var distance = Vector3.Distance(hit.point, tip);
                if (distance > bestDistance) continue;
                bestDistance = distance;
                best = hit;
                found = true;
            }
            return found;
        }

        /// <summary>Plant only when a touch begins, away from the bag and the hand's last vine, and not too many at once.</summary>
        public static bool CanPlant(bool touchBegan, float now, float lastPlant, Vector3 point, Vector3? lastPoint,
            Vector3? softOrigin, int growing)
        {
            if (!touchBegan || now - lastPlant < HandCooldown || growing >= MaxGrowing) return false;
            if (lastPoint.HasValue && Vector3.Distance(point, lastPoint.Value) < MinSpacing) return false;
            return !softOrigin.HasValue || Vector3.Distance(point, softOrigin.Value) >= SoftKeepOut;
        }
    }

    /// <summary>
    /// Reads the path back out of an Ivy Studio 3D branch tube (IvyGeneratorTREANT.generate3Dbranch): ring i holds
    /// <c>divisions</c> vertices around path point i, root first. Vertex k = divisions/4 lies along the surface normal,
    /// k = 0 along the side, so divisions must be a multiple of 4.
    /// </summary>
    public static class NatureVineRings
    {
        public static int Extract(Vector3[] vertices, int triangleIndexCount, int divisions,
            List<Vector3> centres, List<Vector3> normals, List<Vector3> sides)
        {
            centres.Clear(); normals.Clear(); sides.Clear();
            if (vertices == null || divisions < 4 || divisions % 4 != 0 || triangleIndexCount <= 0) return 0;
            var rings = Mathf.Min(triangleIndexCount / (6 * divisions) + 1, vertices.Length / divisions);
            for (var i = 0; i < rings; i++)
            {
                var centre = Vector3.zero;
                for (var k = 0; k < divisions; k++) centre += vertices[i * divisions + k];
                centre /= divisions;
                var normal = vertices[i * divisions + divisions / 4] - centre;
                var side = vertices[i * divisions] - centre;
                centres.Add(centre);
                normals.Add(normal.sqrMagnitude > 1e-12f ? normal.normalized : i > 0 ? normals[i - 1] : Vector3.up);
                sides.Add(side.sqrMagnitude > 1e-12f ? side.normalized : i > 0 ? sides[i - 1] : Vector3.right);
            }
            return rings;
        }
    }

    /// <summary>One leaf on a vine: stem point, tip direction and width direction (unit vectors), size in metres.</summary>
    public struct NatureVineLeaf
    {
        public float At;        // 0 = root .. 1 = tip of the branch: the leaf unfolds when the reveal passes it
        public Vector3 Stem, Tip, Width, Normal;
        public float Size;
        public int Texture;     // 0 or 1: the vine's two leaf textures
    }

    /// <summary>Leaves along a vine, built as one quad mesh per texture (cheap for PolySpatial, unlike a GameObject per leaf).</summary>
    public static class NatureVineLeaves
    {
        public const float Spacing = .042f, MinSize = .03f, MaxSize = .058f, Lift = .004f, UnfoldSeconds = .45f;

        public static void Plan(List<Vector3> centres, List<Vector3> normals, List<Vector3> sides, System.Random random, List<NatureVineLeaf> output)
        {
            output.Clear();
            var rings = centres.Count;
            if (rings < 2) return;
            var length = 0f;
            for (var i = 1; i < rings; i++) length += Vector3.Distance(centres[i - 1], centres[i]);
            if (length < .01f) return;
            var travelled = 0f;
            var next = Spacing * .4f;
            var side = random.Next(2) * 2 - 1;
            for (var i = 1; i < rings; i++)
            {
                var segment = Vector3.Distance(centres[i - 1], centres[i]);
                while (segment > 1e-5f && travelled + segment >= next)
                {
                    var t = (next - travelled) / segment;
                    var along = (centres[i] - centres[i - 1]) / segment;
                    var normal = Vector3.Slerp(normals[i - 1], normals[i], t).normalized;
                    var outward = Vector3.ProjectOnPlane(Vector3.Lerp(sides[i - 1], sides[i], t), normal);
                    if (outward.sqrMagnitude < 1e-8f) outward = Vector3.Cross(normal, along);
                    outward = outward.normalized * side;
                    var tip = (outward + along * Range(random, -.2f, .7f) + normal * Range(random, .35f, .8f)).normalized;
                    var plane = (normal - tip * Vector3.Dot(normal, tip)).normalized;
                    var at = (next / length);
                    output.Add(new NatureVineLeaf
                    {
                        At = Mathf.Clamp01(at),
                        Stem = Vector3.Lerp(centres[i - 1], centres[i], t) + normal * Lift,
                        Tip = tip,
                        Width = Vector3.Cross(plane, tip).normalized,
                        Normal = plane,
                        Size = Range(random, MinSize, MaxSize) * (1f - .35f * at),   // smaller toward the growing end
                        Texture = random.Next(2),
                    });
                    side = -side;
                    next += Spacing * Range(random, .75f, 1.25f);
                }
                travelled += segment;
            }
        }

        /// <summary>Leaf scale <paramref name="since"/> seconds after the reveal passed it: 0 → 1 with a small overshoot.</summary>
        public static float Unfold(float since)
        {
            if (since <= 0f) return 0f;
            var t = Mathf.Clamp01(since / UnfoldSeconds);
            const float c1 = 1.70158f, c3 = c1 + 1f;   // ease-out-back: a small overshoot, like a leaf opening
            return 1f + c3 * Mathf.Pow(t - 1f, 3) + c1 * Mathf.Pow(t - 1f, 2);
        }

        /// <summary>
        /// Rebuilds <paramref name="mesh"/> with the leaves that use <paramref name="texture"/>, each scaled by
        /// <paramref name="scales"/>[i] (0 = not yet). Vertices are in <paramref name="space"/>'s local space.
        /// </summary>
        public static void Build(Mesh mesh, List<NatureVineLeaf> leaves, float[] scales, int texture, Transform space,
            List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> triangles)
        {
            vertices.Clear(); normals.Clear(); uvs.Clear(); triangles.Clear();
            for (var i = 0; i < leaves.Count; i++)
            {
                var leaf = leaves[i];
                var s = scales[i];
                if (leaf.Texture != texture || s <= .001f) continue;
                var size = leaf.Size * s;
                var w = leaf.Width * (size * .5f);
                var up = leaf.Tip * size;
                var n = space.InverseTransformDirection(leaf.Normal);
                var b = vertices.Count;
                vertices.Add(space.InverseTransformPoint(leaf.Stem - w));
                vertices.Add(space.InverseTransformPoint(leaf.Stem + w));
                vertices.Add(space.InverseTransformPoint(leaf.Stem - w + up));
                vertices.Add(space.InverseTransformPoint(leaf.Stem + w + up));
                for (var k = 0; k < 4; k++) normals.Add(n);
                uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(1f, 0f)); uvs.Add(new Vector2(0f, 1f)); uvs.Add(new Vector2(1f, 1f));
                triangles.Add(b); triangles.Add(b + 2); triangles.Add(b + 1);
                triangles.Add(b + 1); triangles.Add(b + 2); triangles.Add(b + 3);
            }
            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
        }

        static float Range(System.Random random, float min, float max) => min + (float)random.NextDouble() * (max - min);
    }

    /// <summary>
    /// Where a leaf sits on the stem (2026-11-03). The leaf texture (Resources/MoodiumNature/NativeVine/Leaf 3.png) draws its
    /// own stalk, ending on the bottom edge at u = 0.532; the leaf mesh is Unity's Quad (1 x 1, pivot in the middle).
    /// So no extra petiole is built: the quad is placed so the drawn stalk's end touches the stem, and the leaf points
    /// outward to one side of the stem, leaning forward along the growth and lifted a little off the wall.
    /// </summary>
    public static class NatureVineLeafPose
    {
        /// <summary>The drawn stalk's end in the quad's local space (bottom edge, u = 0.532).</summary>
        public static readonly Vector3 StalkEnd = new(.032f, -.5f, 0f);
        /// <summary>How far a leaf leans forward from straight sideways, along the growth direction.</summary>
        public const float MinForward = 20f, MaxForward = 50f;
        /// <summary>How far the leaf tip lifts off the surface.</summary>
        public const float MinLift = 8f, MaxLift = 22f;
        /// <summary>Below this the plugin's own side choice is ambiguous and leaves alternate sides instead (metres).</summary>
        public const float SideTolerance = .001f;

        /// <summary>
        /// Unit direction from the stem out to the leaf tip, in the surface plane: to <paramref name="side"/>'s side of the
        /// stem (sign only), leaning <paramref name="forwardDegrees"/> toward <paramref name="tangent"/>. Zero if the stem
        /// runs along the normal.
        /// </summary>
        public static Vector3 Outward(Vector3 tangent, Vector3 normal, float side, float forwardDegrees)
        {
            var along = Vector3.ProjectOnPlane(tangent, normal);
            if (along.sqrMagnitude < 1e-8f || normal.sqrMagnitude < 1e-8f) return Vector3.zero;
            along.Normalize();
            var across = Vector3.Cross(normal.normalized, along) * (side < 0f ? -1f : 1f);
            var radians = forwardDegrees * Mathf.Deg2Rad;
            return (across * Mathf.Cos(radians) + along * Mathf.Sin(radians)).normalized;
        }

        /// <summary>The quad's +Y (texture up: stalk to tip) along <paramref name="outward"/>, its +Z along the surface
        /// normal, then the tip lifted <paramref name="liftDegrees"/> off the surface.</summary>
        public static Quaternion Rotation(Vector3 normal, Vector3 outward, float liftDegrees) =>
            Quaternion.LookRotation(normal, outward) * Quaternion.AngleAxis(liftDegrees, Vector3.right);

        /// <summary>The quad's centre so that its drawn stalk end lands on <paramref name="stalkEnd"/>.</summary>
        public static Vector3 Centre(Vector3 stalkEnd, Quaternion rotation, Vector3 worldScale) =>
            stalkEnd - rotation * Vector3.Scale(StalkEnd, worldScale);

        /// <summary>+1 / -1: the side of the stem the plugin put the leaf on, or alternating when it is on the stem line.</summary>
        public static float Side(Vector3 leafPosition, Vector3 joint, Vector3 tangent, Vector3 normal, int index)
        {
            var across = Vector3.Cross(normal, Vector3.ProjectOnPlane(tangent, normal));
            var side = across.sqrMagnitude < 1e-8f ? 0f : Vector3.Dot(leafPosition - joint, across.normalized);
            if (Mathf.Abs(side) < SideTolerance) return index % 2 == 0 ? 1f : -1f;
            return Mathf.Sign(side);
        }
    }

    /// <summary>The two vine looks in the reference: green ivy and red / orange maple.</summary>
    public static class NatureVineLook
    {
        public const string LeafFolder = "MoodiumNature/VineLeaves/";
        public const string HintFolder = "MoodiumNature/VineHint/";
        /// <summary>Share of vines that are maple (the rest are ivy).</summary>
        public const float MapleShare = .45f;
        public static string[] Textures(bool maple) => maple
            ? new[] { LeafFolder + "vine_leaf_maple_a", LeafFolder + "vine_leaf_maple_b" }
            : new[] { LeafFolder + "vine_leaf_ivy_a", LeafFolder + "vine_leaf_ivy_b" };
        public static Color Stem(bool maple) => maple ? new Color(.42f, .17f, .12f) : new Color(.33f, .30f, .17f);
        public static string HintSprite(bool english) => HintFolder + (english ? "vine_hint_en" : "vine_hint_zh");
        public static string HintText(bool english) => english ? "Touch a wall or table to grow vines" : "用手指碰一碰墙面或桌面，长出藤蔓";
    }
}
