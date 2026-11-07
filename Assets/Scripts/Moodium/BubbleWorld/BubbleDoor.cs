using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Moodium.NatureWorld
{
    /// <summary>
    /// The invisible booth around the viewer while they stand in front of the door, in the door's room frame: x from
    /// <see cref="Left"/> to <see cref="Right"/>, z from <see cref="Back"/> to the door plane, floor to ceiling. It grows
    /// with the viewer (in 10 cm steps) so walking around in the room never takes them out of it.
    /// </summary>
    public struct DoorBooth : IEquatable<DoorBooth>
    {
        public float Left, Right, Back;
        public static DoorBooth Default => new() { Left = -BubbleDoorLayout.HalfWidth, Right = BubbleDoorLayout.HalfWidth, Back = -BubbleDoorLayout.BackDepth };
        public bool Equals(DoorBooth other) => Left == other.Left && Right == other.Right && Back == other.Back;
        public override bool Equals(object obj) => obj is DoorBooth other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Left, Right, Back);
    }

    /// <summary>
    /// Geometry and rules of the mercury door (门入口 2026-11-02 b), in the room's own frame: origin on the floor under the
    /// viewer's head when the door was placed, +z = toward the door, the door plane at z = door distance. Pure (EditMode tests).
    /// </summary>
    public static class BubbleDoorLayout
    {
        /// <summary>The smallest booth: x in ±HalfWidth, from BackDepth behind the viewer to the door, floor to Height.</summary>
        public const float HalfWidth = 1.2f, BackDepth = 1f, Height = 2.6f, FloorLift = .02f;
        /// <summary>The booth keeps this much space beside / behind the head, rounded out to <see cref="BoothStep"/>.</summary>
        public const float SidePad = 1f, BackPad = .9f, BoothStep = .1f;
        /// <summary>The door stands this far in front of the viewer.</summary>
        public const float DoorDistance = 1.25f;
        /// <summary>The door: an upright oval, centre this high above the floor, half-width and half-height when open.</summary>
        public const float CenterHeight = 1.1f, HalfOpenWidth = .5f, HalfOpenHeight = .9f;
        /// <summary>The scan's eye point sits this far behind the door, so walking through puts the viewer near it.</summary>
        public const float EyeBeyondDoor = .6f;
        /// <summary>
        /// The liquid lip: tube radius (it swells and thins round the door between <see cref="RimThin"/> and
        /// <see cref="RimThick"/> of it), and how far its centre line sits outside the hole. The thinnest lip still
        /// covers the hole's hard edge.
        /// </summary>
        public const float RimRadius = .085f, RimThin = .6f, RimThick = 1.42f, RimOffset = .025f;
        /// <summary>The folded door is a droplet: the hole shrinks to this and the lip closes over it.</summary>
        public const float DropletHole = .005f, DropletTube = .07f, DropletAgitation = .2f;
        /// <summary>Crossing the door plane uses this margin (metres) so a lean at the door does not flicker.</summary>
        public const float Margin = .05f;
        /// <summary>A fingertip this far past the door plane, inside the oval, is "reaching in".</summary>
        public const float ReachDepth = .04f;
        /// <summary>Gliding in, the door stops this far behind the head; gliding out, it settles DoorDistance in front again.</summary>
        public const float GlidePast = .35f;
        public const float ArriveSeconds = .9f, OpenSeconds = 1.1f, FoldSeconds = .55f, GlideSeconds = 1.2f, CloseSeconds = .5f, ParkSeconds = .8f;
        /// <summary>Dragging the door: it stays this near / far from the viewer, and only the ring grabs it (not the opening).</summary>
        public const float MinDoorDistance = .8f, MaxDoorDistance = 2.5f, GrabRing = .78f, DragStart = .03f;
        /// <summary>The halo outside the lip: a translucent mirror skirt that bulges toward the viewer and fades out, with a soft glow.</summary>
        public const float SkirtWidth = .26f, SkirtBulge = .05f, SkirtInset = .7f;
        public const int SkirtRings = 8;
        public const int HoleSegments = 64, RimSegments = 96, RimSides = 16;

        public static Vector3 DoorCenter(float doorZ) => new(0f, CenterHeight, doorZ);

        /// <summary>One frame of "is the head on the room side of the door plane": switch once clearly across.</summary>
        public static bool NextInRoom(bool inRoom, Vector3 head, float doorZ) =>
            inRoom ? head.z < doorZ + Margin : head.z < doorZ - Margin;

        /// <summary>The booth for a head at <paramref name="head"/> (room frame): never smaller than the default.</summary>
        public static DoorBooth BoothFor(Vector3 head)
        {
            var booth = DoorBooth.Default;
            booth.Left = Mathf.Min(booth.Left, Mathf.Floor((head.x - SidePad) / BoothStep) * BoothStep);
            booth.Right = Mathf.Max(booth.Right, Mathf.Ceil((head.x + SidePad) / BoothStep) * BoothStep);
            booth.Back = Mathf.Min(booth.Back, Mathf.Floor((head.z - BackPad) / BoothStep) * BoothStep);
            return booth;
        }

        /// <summary>Inside the booth (with <paramref name="grow"/> extra all round) and in front of the door plane.</summary>
        public static bool InBooth(Vector3 p, float doorZ, DoorBooth booth, float grow) =>
            p.x > booth.Left - grow && p.x < booth.Right + grow && p.z > booth.Back - grow && p.z < doorZ + grow && p.y < Height + grow;

        /// <summary>Inside the door oval (x, y in the room frame) with half sizes a, b.</summary>
        public static bool InOval(float x, float y, float a, float b)
        {
            if (a <= 0f || b <= 0f) return false;
            var dx = x / a; var dy = (y - CenterHeight) / b;
            return dx * dx + dy * dy <= 1f;
        }

        /// <summary>A fingertip (room frame) has gone through the open door.</summary>
        public static bool ReachingIn(Vector3 tip, float doorZ, float a, float b) =>
            tip.z > doorZ + ReachDepth && tip.z < doorZ + .5f && InOval(tip.x, tip.y, a, b);


        /// <summary>Door half sizes while it tears open from the droplet (t 0..1), overshooting a little; height a beat later.</summary>
        public static Vector2 OpenSize(float t, float fromHole = DropletHole)
        {
            t = Mathf.Clamp01(t);
            var w = BackOut(Mathf.Clamp01(t / .8f), 1.4f);
            var h = BackOut(Mathf.Clamp01((t - .15f) / .85f), .9f);
            return new Vector2(Mathf.LerpUnclamped(fromHole, HalfOpenWidth, w), Mathf.LerpUnclamped(fromHole, HalfOpenHeight, h));
        }

        /// <summary>Folding (t 0..1): the open hole closes into the droplet, slowly first, then quickly.</summary>
        public static Vector2 FoldSize(float t, Vector2 from)
        {
            var k = 1f - Mathf.Clamp01(t);
            return Vector2.Lerp(new Vector2(DropletHole, DropletHole), from, k * k);
        }

        static float BackOut(float t, float s)
        {
            t -= 1f;
            return t * t * ((s + 1f) * t + s) + 1f;
        }

        /// <summary>Door plane while gliding (t 0..1) from <paramref name="fromZ"/> to <paramref name="toZ"/>.</summary>
        public static float GlideZ(float t, float fromZ, float toZ)
        {
            t = Mathf.Clamp01(t);
            return Mathf.Lerp(fromZ, toZ, t * t * (3f - 2f * t));
        }

        /// <summary>Gliding in ends just behind the head; gliding out ends a door distance in front of it.</summary>
        public static float GlideTarget(bool intoWorld, float headZ) => intoWorld ? headZ - GlidePast : headZ + DoorDistance;

        /// <summary>
        /// Point layers with points inside the booth (a little grown) while the viewer stands in the room: they would
        /// float around the viewer in the room, so they wait and grow in once the viewer is through.
        /// </summary>
        public static bool[] HeldLayers(IReadOnlyList<Vector3[]> samples, IReadOnlyList<Matrix4x4> layerToRoom, float doorZ, DoorBooth booth)
        {
            var held = new bool[samples.Count];
            for (var l = 0; l < samples.Count; l++)
            {
                var points = samples[l];
                if (points == null || l >= layerToRoom.Count) continue;
                var m = layerToRoom[l];
                foreach (var p in points)
                {
                    var q = m.MultiplyPoint3x4(p);
                    if (q.y > FloorLift && InBooth(q, doorZ, booth, .15f)) { held[l] = true; break; }
                }
            }
            return held;
        }

        public static bool[] HeldLayers(IReadOnlyList<Vector3[]> samples, IReadOnlyList<Matrix4x4> layerToRoom, float doorZ) =>
            HeldLayers(samples, layerToRoom, doorZ, DoorBooth.Default);

        /// <summary>Is a grab at <paramref name="p"/> (room frame) on the ring (or outside it), not in the opening?</summary>
        public static bool OnRing(Vector3 p, float a, float b)
        {
            if (a <= 0f || b <= 0f) return true;
            var dx = p.x / a; var dy = (p.y - CenterHeight) / b;
            return dx * dx + dy * dy >= GrabRing * GrabRing;
        }

        /// <summary>
        /// Where a dragged door stands: its centre follows <paramref name="wanted"/> on the floor plane, kept between
        /// MinDoorDistance and MaxDoorDistance from the head, facing the head. Height never changes. Returns the door
        /// frame's origin (on the floor, <paramref name="doorZ"/> behind the centre) and rotation.
        /// </summary>
        public static (Vector3 origin, Quaternion yaw) Dragged(Vector3 head, Vector3 wanted, float floorY, float doorZ, Vector3 fallbackForward)
        {
            var flat = wanted - head; flat.y = 0f;
            var dir = flat.sqrMagnitude > 1e-6f ? flat.normalized : new Vector3(fallbackForward.x, 0f, fallbackForward.z).normalized;
            var distance = Mathf.Clamp(flat.magnitude, MinDoorDistance, MaxDoorDistance);
            var yaw = Quaternion.LookRotation(dir, Vector3.up);
            var centre = head + dir * distance;
            return (new Vector3(centre.x, floorY, centre.z) - dir * doorZ, yaw);
        }

        // ---------- the liquid lip ----------

        /// <summary>How thick the lip is at <paramref name="angle"/> round the door: two slow lobes drifting round it.</summary>
        public static float Lobe(float angle, float time) =>
            1f + .26f * Mathf.Sin(2f * angle + .4f * time + .3f) + .14f * Mathf.Sin(3f * angle - .55f * time + 1.9f);

        /// <summary>
        /// Liquid wobble at <paramref name="angle"/>, time <paramref name="time"/>: (radial push in metres, thickness
        /// factor of <see cref="RimRadius"/>, clamped to RimThin..RimThick). <paramref name="agitation"/> 0..1 scales the
        /// ripples; pokes (x = angle, y = strength 0..1, z = seconds since the touch) swell the lip and run ripples outward.
        /// </summary>
        public static Vector2 Wobble(float angle, float time, float agitation, IReadOnlyList<Vector3> pokes)
        {
            agitation = Mathf.Clamp01(agitation);
            var calm = .004f + .016f * agitation;
            var radial = calm * (.55f * Mathf.Sin(3f * angle + 1.3f * time) + .3f * Mathf.Sin(5f * angle - 2.1f * time + 1f) + .15f * Mathf.Sin(9f * angle + 3.7f * time + 2f));
            var thick = Lobe(angle, time) + (.06f + .14f * agitation) * Mathf.Sin(4f * angle - 1.7f * time + .5f);
            if (pokes != null)
                foreach (var poke in pokes)
                {
                    var delta = Mathf.DeltaAngle(angle * Mathf.Rad2Deg, poke.x * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                    var bump = Mathf.Exp(-delta * delta / .06f) * poke.y;
                    var ripple = Mathf.Sin(14f * Mathf.Abs(delta) - 9f * poke.z) * Mathf.Exp(-Mathf.Abs(delta) * 2.5f) * poke.y;
                    thick += .45f * bump + .15f * ripple;
                    radial -= .012f * bump;
                }
            return new Vector2(radial, Mathf.Clamp(thick, RimThin, RimThick));
        }

        /// <summary>
        /// The lip round an oval (half sizes a, b) centred at <paramref name="centre"/> (room frame, in the door plane):
        /// a tube of radius <paramref name="tube"/> × the wobble's thickness. With <paramref name="eye"/> (room frame) the
        /// UVs carry the view angle for the thin-film shell: u = how grazing the surface is seen (0 facing, 1 edge-on),
        /// v = colour phase round the door.
        /// </summary>
        public static void Rim(Mesh mesh, Vector3 centre, float a, float b, float tube, float time, float agitation, IReadOnlyList<Vector3> pokes,
            Vector3? eye, List<Vector3> v, List<Vector3> nrm, List<Vector2> uv, List<int> t)
        {
            v.Clear(); nrm.Clear(); uv.Clear();
            int n = RimSegments, m = RimSides;
            for (var i = 0; i <= n; i++)
            {
                var angle = i * Mathf.PI * 2f / n;
                var wob = Wobble(angle, time, agitation, pokes);
                var ra = a + RimOffset + wob.x; var rb = b + RimOffset + wob.x;
                var c = centre + new Vector3(ra * Mathf.Cos(angle), rb * Mathf.Sin(angle), 0f);
                var outward = new Vector3(rb * Mathf.Cos(angle), ra * Mathf.Sin(angle), 0f).normalized;
                var radius = Mathf.Max(.005f, tube * wob.y);
                for (var j = 0; j <= m; j++)
                {
                    var phi = j * Mathf.PI * 2f / m;
                    var dir = outward * Mathf.Cos(phi) + Vector3.forward * Mathf.Sin(phi);
                    var p = c + dir * radius;
                    v.Add(p); nrm.Add(dir);
                    if (eye.HasValue)
                    {
                        var view = eye.Value - p;
                        var facing = view.sqrMagnitude > 1e-8f ? Mathf.Abs(Vector3.Dot(dir, view.normalized)) : 1f;
                        // Keep u off the texture's edges so bilinear filtering never wraps round.
                        uv.Add(new Vector2(.04f + .92f * (1f - facing), i / (float)n * 3f + j / (float)m * .25f));
                    }
                    else uv.Add(new Vector2(i / (float)n * 6f, j / (float)m));
                }
            }
            if (t.Count == 0)
                for (var i = 0; i < n; i++)
                    for (var j = 0; j < m; j++)
                    {
                        int p = i * (m + 1) + j, q = (i + 1) * (m + 1) + j;
                        // Outward-facing: cross(along the oval, round the tube) points along dir (checked by the tests).
                        t.AddRange(new[] { p, q, p + 1, q, q + 1, p + 1 });
                    }
            mesh.SetVertices(v); mesh.SetNormals(nrm); mesh.SetUVs(0, uv);
            if (mesh.GetIndexCount(0) != t.Count) mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
        }

        /// <summary>
        /// Thin-film colours for the shell over the lip, 64 (view angle) × 16 (colour phase): clear where the lip faces
        /// the viewer, a soft oil-slick rainbow where it is seen edge-on, like the rim of a soap film on mercury.
        /// </summary>
        public static Texture2D FilmTexture()
        {
            const int w = 64, h = 16;
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "Mercury Door Film", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    var grazing = (x + .5f) / w;
                    var c = Color.HSVToRGB(Mathf.Repeat(y / (float)h + grazing * .35f, 1f), .55f, 1f);
                    c.a = FilmAlpha(grazing);
                    texture.SetPixel(x, y, c);
                }
            texture.Apply(false, true);
            return texture;
        }

        /// <summary>Film opacity by view angle (0 facing … 1 edge-on).</summary>
        public static float FilmAlpha(float grazing)
        {
            var k = Mathf.Clamp01((grazing - .45f) / .5f);
            return .75f * k * k * (3f - 2f * k);
        }

        /// <summary>
        /// The halo outside the lip (same oval and wobble as the lip): rings from just under the lip's outer edge out to
        /// SkirtWidth beyond it, bulging toward the viewer near the lip and flattening out, with slow ripples running
        /// outward. Seen from the room it mirrors the room, warped, and glows faintly, fading into the real room.
        /// u = 0 at the lip … 1 at the outer edge.
        /// </summary>
        public static void Skirt(Mesh mesh, Vector3 centre, float a, float b, float time, float agitation, IReadOnlyList<Vector3> pokes,
            List<Vector3> v, List<Vector3> nrm, List<Vector2> uv, List<int> t)
        {
            v.Clear(); nrm.Clear(); uv.Clear();
            int n = RimSegments, m = SkirtRings;
            for (var i = 0; i <= n; i++)
            {
                var angle = i * Mathf.PI * 2f / n;
                var wob = Wobble(angle, time, agitation, pokes);
                var ra = a + RimOffset + wob.x; var rb = b + RimOffset + wob.x;
                var ring = centre + new Vector3(ra * Mathf.Cos(angle), rb * Mathf.Sin(angle), 0f);
                var outward = new Vector3(rb * Mathf.Cos(angle), ra * Mathf.Sin(angle), 0f).normalized;
                var start = RimRadius * wob.y * SkirtInset;
                for (var j = 0; j <= m; j++)
                {
                    var s = j / (float)m;
                    var ripple = .008f * (1f - s) * Mathf.Sin(14f * s - 2.2f * time + angle * 3f);
                    // 4 mm toward the room at least, so the wall's occluder never cuts the outer edge.
                    var bulge = .004f + SkirtBulge * (1f - s) * (1f - s) + ripple;
                    var slope = 2f * SkirtBulge * (1f - s);
                    v.Add(ring + outward * (start + s * SkirtWidth) + Vector3.back * bulge);
                    nrm.Add((Vector3.back * SkirtWidth + outward * slope).normalized);
                    uv.Add(new Vector2(.03f + .94f * s, i / (float)n * 2f));
                }
            }
            if (t.Count == 0)
                for (var i = 0; i < n; i++)
                    for (var j = 0; j < m; j++)
                    {
                        int p = i * (m + 1) + j, q = (i + 1) * (m + 1) + j;
                        t.AddRange(new[] { p, q, p + 1, q, q + 1, p + 1 });
                    }
            mesh.SetVertices(v); mesh.SetNormals(nrm); mesh.SetUVs(0, uv);
            if (mesh.GetIndexCount(0) != t.Count) mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
        }

        /// <summary>Halo opacity from the lip (0) to the outer edge (1): strongest at the lip, gone at the edge.</summary>
        public static float SkirtAlpha(float s)
        {
            var k = 1f - Mathf.Clamp01(s);
            return .55f * k * k * Mathf.Sqrt(k);
        }

        /// <summary>64 (lip → edge) × 8 (colour phase): near-white with a faint cool / oil-slick tint; alpha <see cref="SkirtAlpha"/>.</summary>
        public static Texture2D SkirtTexture()
        {
            const int w = 64, h = 8;
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "Mercury Door Halo", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    var s = (x + .5f) / w;
                    var tint = Color.HSVToRGB(Mathf.Repeat(y / (float)h + s * .5f, 1f), .18f * (1f - s), 1f);
                    tint.a = SkirtAlpha(s);
                    texture.SetPixel(x, y, tint);
                }
            texture.Apply(false, true);
            return texture;
        }

        // ---------- occluders ----------

        /// <summary>The booth's faces, all facing inward, with an oval hole (half sizes a, b) in the front wall at doorZ.</summary>
        public static void Walls(Mesh mesh, float doorZ, float a, float b, DoorBooth booth)
        {
            float l = booth.Left, r = booth.Right, k = booth.Back, h = Height, f = FloorLift, d = doorZ;
            var v = new List<Vector3>(256); var t = new List<int>(768);
            Quad(v, t, new(l, f, k), new(r, f, k), new(r, f, d), new(l, f, d), Vector3.up);       // floor
            Quad(v, t, new(l, h, k), new(r, h, k), new(r, h, d), new(l, h, d), Vector3.down);     // ceiling
            Quad(v, t, new(l, f, k), new(l, h, k), new(l, h, d), new(l, f, d), Vector3.right);    // left
            Quad(v, t, new(r, f, k), new(r, h, k), new(r, h, d), new(r, f, d), Vector3.left);     // right
            Quad(v, t, new(l, f, k), new(r, f, k), new(r, h, k), new(l, h, k), Vector3.forward);  // back
            FrontWall(v, t, d, a, b, l, r);
            Fill(mesh, v, t);
        }

        public static void Walls(Mesh mesh, float doorZ, float a, float b) => Walls(mesh, doorZ, a, b, DoorBooth.Default);

        /// <summary>
        /// Front wall minus the oval: one sector per hole segment, from the oval out to the wall's edge along the ray
        /// from the oval's centre (with the wall's corners added where a sector spans one). Each sector is the wall ∩
        /// a wedge beyond the oval's chord, so it is convex and a fan from its first oval point covers it exactly.
        /// </summary>
        static void FrontWall(List<Vector3> v, List<int> t, float d, float a, float b, float left, float right)
        {
            float f = FloorLift, h = Height, cy = CenterHeight;
            var hole = ClampHole(a, b); a = hole.x; b = hole.y;
            var corners = new[] { new Vector2(right, h), new Vector2(left, h), new Vector2(left, f), new Vector2(right, f) };
            var cornerAngles = new float[4];
            for (var c = 0; c < 4; c++) cornerAngles[c] = Angle(corners[c].x, corners[c].y - cy);
            var n = HoleSegments;
            for (var i = 0; i < n; i++)
            {
                var t0 = i * Mathf.PI * 2f / n; var t1 = (i + 1) * Mathf.PI * 2f / n;
                var e0 = new Vector2(a * Mathf.Cos(t0), cy + b * Mathf.Sin(t0));
                var e1 = new Vector2(a * Mathf.Cos(t1), cy + b * Mathf.Sin(t1));
                var outer = new List<Vector2> { WallEdge(e0.x, e0.y - cy, left, right) };
                float a0 = Angle(e0.x, e0.y - cy), a1 = Angle(e1.x, e1.y - cy);
                if (a1 <= a0) a1 += Mathf.PI * 2f;
                var inSector = new List<(float angle, Vector2 corner)>();
                for (var c = 0; c < 4; c++)
                {
                    var ca = cornerAngles[c];
                    if (ca <= a0) ca += Mathf.PI * 2f;
                    if (ca > a0 && ca < a1) inSector.Add((ca, corners[c]));
                }
                inSector.Sort((x, y) => x.angle.CompareTo(y.angle));
                foreach (var corner in inSector) outer.Add(corner.corner);
                outer.Add(WallEdge(e1.x, e1.y - cy, left, right));
                for (var j = 0; j + 1 < outer.Count; j++)
                    Tri(v, t, new(e0.x, e0.y, d), new(outer[j].x, outer[j].y, d), new(outer[j + 1].x, outer[j + 1].y, d), Vector3.back);
                var last = outer[outer.Count - 1];
                Tri(v, t, new(e0.x, e0.y, d), new(last.x, last.y, d), new(e1.x, e1.y, d), Vector3.back);
            }
        }

        static float Angle(float x, float y)
        {
            var angle = Mathf.Atan2(y, x);
            return angle < 0f ? angle + Mathf.PI * 2f : angle;
        }

        /// <summary>Where the ray from the oval's centre in direction (dx, dy) leaves the front wall.</summary>
        static Vector2 WallEdge(float dx, float dy, float left, float right)
        {
            float cy = CenterHeight, s = float.PositiveInfinity;
            if (dx > 1e-6f) s = Mathf.Min(s, right / dx);
            if (dx < -1e-6f) s = Mathf.Min(s, left / dx);
            if (dy > 1e-6f) s = Mathf.Min(s, (Height - cy) / dy);
            if (dy < -1e-6f) s = Mathf.Min(s, (FloorLift - cy) / dy);
            return new Vector2(dx * s, cy + dy * s);
        }

        /// <summary>The opening overshoots a little: the hole stays inside the wall (the lip may overshoot, which looks liquid).</summary>
        public static Vector2 ClampHole(float a, float b) => new(
            Mathf.Clamp(a, .002f, HalfWidth - .01f),
            Mathf.Clamp(b, .002f, Mathf.Min(CenterHeight - FloorLift, Height - CenterHeight) - .01f));

        /// <summary>The door opening seen from the world side: hides the world behind it, so the room shows through.</summary>
        public static void BackDoor(Mesh mesh, float doorZ, float a, float b)
        {
            var hole = ClampHole(a, b); a = hole.x; b = hole.y;
            var v = new List<Vector3>(HoleSegments + 1); var t = new List<int>(HoleSegments * 3);
            v.Add(new Vector3(0f, CenterHeight, doorZ));
            for (var i = 0; i < HoleSegments; i++)
            {
                var angle = i * Mathf.PI * 2f / HoleSegments;
                v.Add(new Vector3(a * Mathf.Cos(angle), CenterHeight + b * Mathf.Sin(angle), doorZ));
            }
            for (var i = 0; i < HoleSegments; i++)
            {
                int p = 1 + i, q = 1 + (i + 1) % HoleSegments;
                var cross = Vector3.Cross(v[p] - v[0], v[q] - v[0]);
                if (Vector3.Dot(cross, Vector3.forward) > 0f) t.AddRange(new[] { 0, p, q }); else t.AddRange(new[] { 0, q, p });
            }
            Fill(mesh, v, t);
        }

        static void Quad(List<Vector3> v, List<int> t, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 facing)
        {
            Tri(v, t, a, b, c, facing);
            Tri(v, t, a, c, d, facing);
        }

        /// <summary>Unity front faces wind clockwise as seen by the viewer: cross(v1 − v0, v2 − v0) points at them.</summary>
        static void Tri(List<Vector3> v, List<int> t, Vector3 a, Vector3 b, Vector3 c, Vector3 facing)
        {
            var cross = Vector3.Cross(b - a, c - a);
            if (cross.sqrMagnitude < 1e-12f) return;
            var i = v.Count;
            v.Add(a); v.Add(b); v.Add(c);
            if (Vector3.Dot(cross, facing) > 0f) t.AddRange(new[] { i, i + 1, i + 2 }); else t.AddRange(new[] { i, i + 2, i + 1 });
        }

        static void Fill(Mesh mesh, List<Vector3> v, List<int> t)
        {
            mesh.Clear();
            mesh.SetVertices(v);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }
    }

    /// <summary>
    /// The mercury door: a fat ring of liquid metal standing in the room, the bubble world at full size behind it and
    /// the real room everywhere else. It arrives as a droplet, tears open, waits; walking through (or reaching in) enters
    /// the world, walking back out returns to the room, the world still waiting behind the door. The ring can be pinched
    /// and dragged across the floor (never up or down). Closing it (the little cross, or walking away) folds it into a
    /// droplet that flies back to its parking spot beside the sensory bag; a touch on the parked droplet opens it again in
    /// front of the viewer. A soft mirror halo round the lip glows and reflects the room, warped.
    /// PolySpatial has no RealityKit portal, so "only through the door" is an occlusion material (AR/Basic Occlusion →
    /// RealityKit OcclusionMaterial) on an invisible booth around the viewer with the door cut out of its front wall.
    /// Occlusion draws on device and in the visionOS simulator only, never in the Editor.
    /// </summary>
    public sealed class BubbleDoor : MonoBehaviour
    {
        public enum Stage { Arriving, Opening, Open, Gliding, Folding, Droplet, Parking, Parked, Closing }

        public Stage State { get; private set; }
        /// <summary>The head is on the room side (the world shows only through the door).</summary>
        public bool InRoom { get; private set; } = true;
        /// <summary>The world may be drawn: the door is open (or opening, gliding, folding). Droplet / parked / arriving: hide it.</summary>
        public bool ShowsWorld => State == Stage.Opening || State == Stage.Open || State == Stage.Gliding || State == Stage.Folding;
        /// <summary>Folded away beside the sensory bag (or flying there): the room is free for walking.</summary>
        public bool IsParked => State == Stage.Parking || State == Stage.Parked || (State == Stage.Folding && m_ParkAfterFold);
        /// <summary>Set by the owner while the world is not ready (loading, switching): the door folds and stays a droplet in place.</summary>
        public bool HoldClosed { get; set; }
        /// <summary>Stepped by its owner (so the world and the walls change in the same frame); otherwise by its own Update.</summary>
        public bool Driven { get; set; }
        /// <summary>The ring is being dragged.</summary>
        public bool Moving { get; private set; }
        public bool CanMove => State == Stage.Open && InRoom;
        public float DoorZ => m_DoorZ;
        public Vector2 HalfSize => m_Size;
        public Transform Room => transform;
        public Collider Collider => m_Collider;
        public DoorBooth Booth => m_Booth;
        public Vector3 ParkPosition => m_ParkWorld;
        /// <summary>The middle of the ring or the droplet (world space).</summary>
        public Vector3 CentreWorld => transform.TransformPoint(m_Centre);
        /// <summary>Changes whenever the booth or the door frame moved (the owner then re-checks which layers wait).</summary>
        public int Version { get; private set; }
        /// <summary>The viewer crossed the door plane: true = into the world, false = back into the room.</summary>
        public event Action<bool> WentThrough;

        Func<int, Vector3?> m_Tip;
        Transform m_Viewer;
        Material m_Occluder, m_Chrome, m_Film, m_Halo;
        Texture2D m_FilmTexture, m_HaloTexture;
        Mesh m_WallMesh, m_BackMesh, m_RimMesh, m_FilmMesh, m_HaloMesh;
        GameObject m_Walls, m_Back, m_HaloObject;
        BoxCollider m_Collider;
        float m_DoorZ = BubbleDoorLayout.DoorDistance, m_Agitation = 1f, m_Time, m_StateTime, m_GlideFrom, m_GlideTo, m_FilmScroll, m_ArriveSeconds;
        Vector2 m_Size, m_FoldFrom, m_BuiltSize;
        Vector3 m_Centre, m_ArriveFrom, m_ParkFrom, m_ParkLocal, m_ParkWorld, m_LastHead, m_GrabOffset;
        float m_BuiltZ = float.NaN;
        DoorBooth m_Booth = DoorBooth.Default, m_BuiltBooth;
        bool m_GlideRequested, m_ParkAfterFold, m_UnparkRequested, m_HasPark;
        readonly Vector3[] m_Pokes = new Vector3[2];
        readonly bool[] m_WasNear = new bool[2];
        readonly List<Vector3> m_PokeList = new(2);
        readonly List<Vector3> m_V = new(), m_N = new();
        readonly List<Vector2> m_UV = new();
        readonly List<int> m_RimTris = new(), m_FilmTris = new(), m_HaloTris = new();

        /// <summary>The occlusion material, or null when the project does not have it (then use the bubble entry instead).</summary>
        public static Material CreateOccluder()
        {
            var material = BubblePortalProbe.CreateOccluder(out var found);
            if (found) return material;
            BubblePlayVisuals.Release(material);
            return null;
        }

        /// <summary>
        /// A door <see cref="BubbleDoorLayout.DoorDistance"/> in front of a viewer standing at <paramref name="head"/>
        /// looking along <paramref name="yaw"/>, floor at <paramref name="floorY"/>. It arrives as a droplet flying from
        /// <paramref name="from"/> (world space), which is also where it parks when closed. Returns null (and logs)
        /// without the occlusion material.
        /// </summary>
        public static BubbleDoor Create(Transform parent, Vector3 head, Quaternion yaw, float floorY, Transform viewer, Func<int, Vector3?> tip,
            Action pressed, Vector3 from)
        {
            var occluder = CreateOccluder();
            if (occluder == null)
            {
                Debug.LogWarning("[Bubble Door] AR occlusion material missing (Moodium ▸ Portal Probe ▸ Create Occluder Material): using the bubble entry.");
                return null;
            }
            var go = new GameObject("Mercury Door");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(new Vector3(head.x, floorY, head.z), yaw);
            var door = go.AddComponent<BubbleDoor>();
            door.Build(occluder, viewer, tip, pressed, from);
            return door;
        }

        void Build(Material occluder, Transform viewer, Func<int, Vector3?> tip, Action pressed, Vector3 from)
        {
            m_Occluder = occluder; m_Viewer = viewer; m_Tip = tip;
            m_Chrome = Chrome();
            m_FilmTexture = BubbleDoorLayout.FilmTexture();
            m_Film = BubbleWorldData.CreateUnlit("Mercury Door Film", m_FilmTexture, true, CullMode.Back, Color.white);
            m_Film.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m_HaloTexture = BubbleDoorLayout.SkirtTexture();
            m_Halo = HaloMaterial(m_HaloTexture);
            m_WallMesh = new Mesh { name = "Mercury Door Walls" };
            m_BackMesh = new Mesh { name = "Mercury Door Back" };
            m_RimMesh = new Mesh { name = "Mercury Door Rim" };
            m_FilmMesh = new Mesh { name = "Mercury Door Film" };
            m_HaloMesh = new Mesh { name = "Mercury Door Halo" };
            m_RimMesh.MarkDynamic(); m_FilmMesh.MarkDynamic(); m_HaloMesh.MarkDynamic();
            m_Walls = Part("Walls (occluder)", m_WallMesh, m_Occluder);
            m_Back = Part("Back Door (occluder)", m_BackMesh, m_Occluder);
            Part("Rim (mercury)", m_RimMesh, m_Chrome);
            Part("Rim Film", m_FilmMesh, m_Film);
            m_HaloObject = Part("Rim Halo", m_HaloMesh, m_Halo);
            var hit = new GameObject("Door Touch");
            hit.transform.SetParent(transform, false);
            m_Collider = hit.AddComponent<BoxCollider>();
            m_Collider.enabled = false;
            hit.AddComponent<BubbleDoorHandle>().Initialize(this, pressed);
            m_Size = new Vector2(BubbleDoorLayout.DropletHole, BubbleDoorLayout.DropletHole);
            InRoom = true;
            m_ParkWorld = from; m_HasPark = true;
            m_LastHead = viewer != null ? viewer.position : transform.position + Vector3.up * 1.6f;
            BeginArrive(transform.InverseTransformPoint(from));
            Rebuild(null);
            Apply();
        }

        /// <summary>Full metal, mirror smooth: on visionOS RealityKit lights it with the real room (IBL), so it mirrors the room.</summary>
        public static Material Chrome()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) return BubbleWorldData.CreateUnlit("Mercury Door Chrome", null, false, CullMode.Back, new Color(.86f, .88f, .92f, 1f));
            var material = new Material(shader) { name = "Mercury Door Chrome" };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", new Color(.97f, .97f, .98f, 1f));
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 1f);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", .97f);
            if (material.HasProperty("_EnvironmentReflections")) material.SetFloat("_EnvironmentReflections", 1f);
            if (material.HasProperty("_SpecularHighlights")) material.SetFloat("_SpecularHighlights", 1f);
            return material;
        }

        /// <summary>
        /// The halo: a translucent mirror (URP/Lit, transparent, metallic, smooth) so it shows the room's reflection,
        /// warped by its curve, over the real room, plus a faint emissive glow. Its opacity comes from the texture.
        /// </summary>
        static Material HaloMaterial(Texture2D texture)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                var unlit = BubbleWorldData.CreateUnlit("Mercury Door Halo", texture, true, CullMode.Back, Color.white);
                unlit.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                return unlit;
            }
            var material = new Material(shader) { name = "Mercury Door Halo" };
            material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
            if (material.HasProperty("_AlphaClip")) material.SetFloat("_AlphaClip", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = (int)RenderQueue.Transparent;
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 1f);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", .9f);
            if (material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                if (material.HasProperty("_EmissionMap")) material.SetTexture("_EmissionMap", texture);
                material.SetColor("_EmissionColor", new Color(.32f, .34f, .4f, 1f));
            }
            return material;
        }

        GameObject Part(string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go;
        }

        /// <summary>The glowing mirror halo round the lip (Inspector: BubbleWorldController ▸ Door Halo).</summary>
        public void SetHalo(bool on) { if (m_HaloObject != null) m_HaloObject.SetActive(on); }

        /// <summary>Where the folded droplet waits (world space): beside the sensory bag, where the entry first appeared.</summary>
        public void SetPark(Vector3 world) { m_ParkWorld = world; m_HasPark = true; }

        void Enter(Stage stage) { State = stage; m_StateTime = 0f; }

        void BeginArrive(Vector3 fromLocal)
        {
            m_ArriveFrom = fromLocal;
            var target = BubbleDoorLayout.DoorCenter(m_DoorZ);
            m_ArriveSeconds = Mathf.Clamp(Vector3.Distance(fromLocal, target) / 1.5f, .25f, BubbleDoorLayout.ArriveSeconds);
            m_Centre = fromLocal;
            m_Size = new Vector2(BubbleDoorLayout.DropletHole, BubbleDoorLayout.DropletHole);
            Enter(Stage.Arriving);
        }

        /// <summary>
        /// A touch or a pinch on the door that did not drag it: on the parked droplet, it flies out and opens in front of
        /// the viewer. The open door ignores taps (it is dragged by its ring; walk or reach in to enter).
        /// </summary>
        public void Press()
        {
            if (State == Stage.Parked || State == Stage.Parking) m_UnparkRequested = true;
        }

        /// <summary>The little cross on the door: fold into a droplet and fly back to the parking spot.</summary>
        public void RequestPark()
        {
            if (!InRoom || State == Stage.Closing || IsParked) return;
            if (Moving) EndMove();
            if (State == Stage.Open || State == Stage.Opening) { BeginFold(); m_ParkAfterFold = true; }
            else if (State == Stage.Droplet || State == Stage.Arriving) BeginPark();
            else if (State == Stage.Folding) m_ParkAfterFold = true;
            Debug.Log("[Bubble Door] closing: the droplet flies back to its spot by the sensory bag");
        }

        /// <summary>A fingertip through the open door: the door comes over the viewer and they are inside.</summary>
        public void RequestGlide()
        {
            if (InRoom && !Moving && (State == Stage.Open || State == Stage.Opening)) m_GlideRequested = true;
        }

        /// <summary>From inside the world (the home bubble): the door passes back over the viewer and stands in front of them.</summary>
        public void RequestGlideBack()
        {
            if (!InRoom && State == Stage.Open) m_GlideRequested = true;
        }

        /// <summary>Shrinks the door shut and removes it. The walls go at once, so nothing behind them stays hidden.</summary>
        public void Close()
        {
            if (State == Stage.Closing) return;
            Moving = false;
            m_FoldFrom = m_Size;
            Enter(Stage.Closing);
            m_Walls.SetActive(false); m_Back.SetActive(false);
            if (m_Collider != null) m_Collider.enabled = false;
        }

        // ---------- dragging ----------

        /// <summary>A pinch on the ring at <paramref name="grab"/> (world) starts a drag; a pinch in the opening does not.</summary>
        public bool BeginMove(Vector3 grab)
        {
            if (!CanMove || !BubbleDoorLayout.OnRing(transform.InverseTransformPoint(grab), m_Size.x, m_Size.y)) return false;
            var centre = transform.TransformPoint(BubbleDoorLayout.DoorCenter(m_DoorZ));
            m_GrabOffset = centre - grab; m_GrabOffset.y = 0f;
            m_GlideRequested = false;
            Moving = true;
            m_Agitation = Mathf.Max(m_Agitation, .5f);
            Debug.Log("[Bubble Door] the ring is grabbed: dragging the door over the floor");
            return true;
        }

        /// <summary>The pinch moved to <paramref name="pointer"/> (world): the door slides there on the floor, facing the viewer.</summary>
        public void MoveTo(Vector3 pointer)
        {
            if (!Moving) return;
            if (!CanMove) { EndMove(); return; }
            var placed = BubbleDoorLayout.Dragged(m_LastHead, pointer + m_GrabOffset, transform.position.y, m_DoorZ, transform.forward);
            transform.SetPositionAndRotation(placed.origin, placed.yaw);
            m_Agitation = Mathf.Max(m_Agitation, .4f);
            Version++;
        }

        public void EndMove()
        {
            if (!Moving) return;
            Moving = false;
            Version++;
            Debug.Log("[Bubble Door] door placed");
        }

        bool WantsHold => HoldClosed;

        /// <summary>Advances the door by <paramref name="dt"/> with the head at <paramref name="head"/> (world space).</summary>
        public void Step(float dt, Vector3 head, Vector3? gaze = null)
        {
            m_Time += dt; m_StateTime += dt;
            m_LastHead = head;
            var local = transform.InverseTransformPoint(head);
            switch (State)
            {
                case Stage.Arriving:
                {
                    var u = m_StateTime / m_ArriveSeconds;
                    var c = Mathf.Clamp01(u);
                    m_Centre = Vector3.Lerp(m_ArriveFrom, BubbleDoorLayout.DoorCenter(m_DoorZ), c * c * (3f - 2f * c)) + Vector3.up * (Mathf.Sin(c * Mathf.PI) * .08f);
                    m_Agitation = BubbleDoorLayout.DropletAgitation;
                    if (u >= 1f) { m_Centre = BubbleDoorLayout.DoorCenter(m_DoorZ); Enter(WantsHold ? Stage.Droplet : Stage.Opening); }
                    break;
                }
                case Stage.Opening:
                    m_Centre = BubbleDoorLayout.DoorCenter(m_DoorZ);
                    m_Size = BubbleDoorLayout.OpenSize(m_StateTime / BubbleDoorLayout.OpenSeconds);
                    m_Agitation = 1f;
                    if (InRoom && WantsHold) BeginFold();
                    else if (m_StateTime >= BubbleDoorLayout.OpenSeconds) { m_Size = new Vector2(BubbleDoorLayout.HalfOpenWidth, BubbleDoorLayout.HalfOpenHeight); Enter(Stage.Open); }
                    break;
                case Stage.Open:
                    m_Agitation = Mathf.MoveTowards(m_Agitation, .15f, dt * .8f);
                    m_Centre = BubbleDoorLayout.DoorCenter(m_DoorZ);
                    if (InRoom && WantsHold) BeginFold();
                    else if (m_GlideRequested && !Moving) StartGlide(local);
                    break;
                case Stage.Gliding:
                    m_Agitation = Mathf.MoveTowards(m_Agitation, .7f, dt * 2f);
                    m_DoorZ = BubbleDoorLayout.GlideZ(m_StateTime / BubbleDoorLayout.GlideSeconds, m_GlideFrom, m_GlideTo);
                    m_Centre = BubbleDoorLayout.DoorCenter(m_DoorZ);
                    if (m_StateTime >= BubbleDoorLayout.GlideSeconds) { Enter(Stage.Open); Version++; }
                    break;
                case Stage.Folding:
                    m_Size = BubbleDoorLayout.FoldSize(m_StateTime / BubbleDoorLayout.FoldSeconds, m_FoldFrom);
                    m_Agitation = .8f;
                    if (m_StateTime >= BubbleDoorLayout.FoldSeconds) { if (m_ParkAfterFold) BeginPark(); else Enter(Stage.Droplet); }
                    break;
                case Stage.Droplet:
                    m_Size = new Vector2(BubbleDoorLayout.DropletHole, BubbleDoorLayout.DropletHole);
                    m_Centre = BubbleDoorLayout.DoorCenter(m_DoorZ) + Vector3.up * (Mathf.Sin(m_Time * 1.9f) * .015f);
                    m_Agitation = BubbleDoorLayout.DropletAgitation;
                    if (!WantsHold) Unfold(local, gaze, false);
                    break;
                case Stage.Parking:
                {
                    var c = Mathf.Clamp01(m_StateTime / BubbleDoorLayout.ParkSeconds);
                    m_Centre = Vector3.Lerp(m_ParkFrom, m_ParkLocal, c * c * (3f - 2f * c)) + Vector3.up * (Mathf.Sin(c * Mathf.PI) * .12f);
                    m_Size = new Vector2(BubbleDoorLayout.DropletHole, BubbleDoorLayout.DropletHole);
                    m_Agitation = BubbleDoorLayout.DropletAgitation;
                    if (c >= 1f) Enter(Stage.Parked);
                    break;
                }
                case Stage.Parked:
                    m_Size = new Vector2(BubbleDoorLayout.DropletHole, BubbleDoorLayout.DropletHole);
                    m_Centre = m_ParkLocal + Vector3.up * (Mathf.Sin(m_Time * 1.6f) * .012f);
                    m_Agitation = BubbleDoorLayout.DropletAgitation;
                    if (m_UnparkRequested) { m_UnparkRequested = false; Unfold(local, gaze, true); }
                    break;
                case Stage.Closing:
                    var k = 1f - Mathf.Clamp01(m_StateTime / BubbleDoorLayout.CloseSeconds);
                    m_Size = m_FoldFrom * (k * k);
                    m_Agitation = 1f;
                    if (k <= 0f) { gameObject.SetActive(false); BubblePlayVisuals.Release(gameObject); return; }
                    break;
            }
            local = transform.InverseTransformPoint(head);   // Unfold may have moved the door
            if (ShowsWorld && State != Stage.Folding)
            {
                var next = BubbleDoorLayout.NextInRoom(InRoom, local, m_DoorZ);
                if (next != InRoom)
                {
                    InRoom = next;
                    if (Moving) EndMove();
                    if (State == Stage.Opening && !next) { m_Size = new Vector2(BubbleDoorLayout.HalfOpenWidth, BubbleDoorLayout.HalfOpenHeight); Enter(Stage.Open); }
                    Debug.Log(next ? "[Bubble Door] back out into the room: the world waits behind the door" : $"[Bubble Door] through the door into the world ({State})");
                    WentThrough?.Invoke(!next);
                    if (this == null || State == Stage.Closing) return;
                }
            }
            if (InRoom)
            {
                var booth = BubbleDoorLayout.BoothFor(local);
                if (!booth.Equals(m_Booth)) { m_Booth = booth; Version++; }
            }
            StepHands(dt);
            Rebuild(local);
            Apply();
        }

        void BeginFold()
        {
            m_FoldFrom = m_Size;
            m_GlideRequested = false;
            m_ParkAfterFold = false;
            if (Moving) EndMove();
            Enter(Stage.Folding);
        }

        void BeginPark()
        {
            m_ParkAfterFold = false;
            m_ParkFrom = m_Centre;
            m_ParkLocal = transform.InverseTransformPoint(m_HasPark ? m_ParkWorld : transform.TransformPoint(m_Centre));
            m_Size = new Vector2(BubbleDoorLayout.DropletHole, BubbleDoorLayout.DropletHole);
            Enter(Stage.Parking);
        }

        /// <summary>
        /// Opens the droplet again, a door distance in front of the viewer with the booth round them. From the parking spot
        /// it opens where the viewer looks (<paramref name="faceGaze"/>); a droplet held in place opens toward the viewer.
        /// </summary>
        void Unfold(Vector3 headLocal, Vector3? gaze, bool faceGaze)
        {
            var droplet = transform.TransformPoint(m_Centre);
            var head = transform.TransformPoint(headLocal);
            var toward = droplet - head; toward.y = 0f;
            if (faceGaze || toward.sqrMagnitude < .09f)
            {
                var g = gaze ?? transform.forward; g.y = 0f;
                toward = g.sqrMagnitude > 1e-6f ? g : transform.forward;
            }
            transform.SetPositionAndRotation(new Vector3(head.x, transform.position.y, head.z), Quaternion.LookRotation(toward.normalized, Vector3.up));
            m_DoorZ = BubbleDoorLayout.DoorDistance;
            InRoom = true;
            m_Booth = BubbleDoorLayout.BoothFor(Vector3.zero);
            Version++;
            BeginArrive(transform.InverseTransformPoint(droplet));
            Debug.Log(faceGaze ? "[Bubble Door] the parked droplet flies out and opens where the viewer looks" : "[Bubble Door] the droplet opens into the door again");
        }

        void StartGlide(Vector3 headLocal)
        {
            m_GlideFrom = m_DoorZ; m_GlideTo = BubbleDoorLayout.GlideTarget(InRoom, headLocal.z);
            m_GlideRequested = false;
            Enter(Stage.Gliding);
            Debug.Log(InRoom ? "[Bubble Door] the door comes over the viewer" : "[Bubble Door] the door passes back over the viewer");
        }

        readonly bool[] m_DropletTouchArmed = new bool[2];

        void StepHands(float dt)
        {
            m_PokeList.Clear();
            for (var hand = 0; hand < 2; hand++)
            {
                var tip = m_Tip?.Invoke(hand);
                if (State != Stage.Parked || !tip.HasValue)
                    m_DropletTouchArmed[hand] = false;
                else
                {
                    var distance = Vector3.Distance(tip.Value, CentreWorld);
                    if (distance > .12f)
                        m_DropletTouchArmed[hand] = true;
                    else if (distance <= .09f && m_DropletTouchArmed[hand])
                    {
                        m_DropletTouchArmed[hand] = false;
                        Press();
                    }
                }
                var poke = m_Pokes[hand];
                var near = false;
                if (tip.HasValue)
                {
                    var p = transform.InverseTransformPoint(tip.Value);
                    if (State == Stage.Open && InRoom && !Moving && BubbleDoorLayout.ReachingIn(p, m_DoorZ, m_Size.x, m_Size.y))
                    {
                        if (!m_GlideRequested) Debug.Log("[Bubble Door] a hand reached through the door");
                        RequestGlide();
                    }
                    // Distance to the lip's centre line: the angle on the oval where the fingertip points from its centre.
                    var angle = Mathf.Atan2((p.y - m_Centre.y) / Mathf.Max(.01f, m_Size.y), (p.x - m_Centre.x) / Mathf.Max(.01f, m_Size.x));
                    var onRim = m_Centre + new Vector3((m_Size.x + BubbleDoorLayout.RimOffset) * Mathf.Cos(angle), (m_Size.y + BubbleDoorLayout.RimOffset) * Mathf.Sin(angle), 0f);
                    var distance = Vector3.Distance(p, onRim);
                    near = distance < .15f;
                    if (near)
                    {
                        if (!m_WasNear[hand]) poke.z = 0f;   // a fresh touch starts a fresh ripple
                        poke.x = angle;
                        poke.y = Mathf.MoveTowards(poke.y, Mathf.InverseLerp(.15f, .03f, distance), dt * 6f);
                        m_Agitation = Mathf.Max(m_Agitation, .35f * poke.y);
                    }
                }
                if (!near) poke.y = Mathf.MoveTowards(poke.y, 0f, dt * 1.5f);
                poke.z += dt;
                m_Pokes[hand] = poke; m_WasNear[hand] = near;
                if (poke.y > .001f) m_PokeList.Add(poke);
            }
        }

        void Rebuild(Vector3? eyeLocal)
        {
            // The walls change only when the door opens, glides, folds or the booth grows; the lip moves every frame.
            if (m_DoorZ != m_BuiltZ || m_Size != m_BuiltSize || !m_Booth.Equals(m_BuiltBooth))
            {
                BubbleDoorLayout.Walls(m_WallMesh, m_DoorZ, m_Size.x, m_Size.y, m_Booth);
                BubbleDoorLayout.BackDoor(m_BackMesh, m_DoorZ, m_Size.x, m_Size.y);
                m_BuiltZ = m_DoorZ; m_BuiltSize = m_Size; m_BuiltBooth = m_Booth;
            }
            var droplet = State == Stage.Arriving || State == Stage.Droplet || State == Stage.Parking || State == Stage.Parked;
            var tube = droplet ? BubbleDoorLayout.DropletTube : BubbleDoorLayout.RimRadius;
            BubbleDoorLayout.Rim(m_RimMesh, m_Centre, m_Size.x, m_Size.y, tube, m_Time, m_Agitation, m_PokeList, null, m_V, m_N, m_UV, m_RimTris);
            BubbleDoorLayout.Rim(m_FilmMesh, m_Centre, m_Size.x, m_Size.y, tube * 1.03f, m_Time, m_Agitation, m_PokeList, eyeLocal ?? m_Centre + Vector3.back * 2f, m_V, m_N, m_UV, m_FilmTris);
            // The halo belongs to the open door only (a droplet has no room for it).
            var halo = !droplet && State != Stage.Closing && m_Size.x > .1f;
            if (m_HaloObject != null && m_HaloObject.activeSelf)
            {
                var r = m_HaloObject.GetComponent<MeshRenderer>();
                if (r.enabled != halo) r.enabled = halo;
                if (halo) BubbleDoorLayout.Skirt(m_HaloMesh, m_Centre, m_Size.x, m_Size.y, m_Time, m_Agitation, m_PokeList, m_V, m_N, m_UV, m_HaloTris);
            }
            m_FilmScroll = Mathf.Repeat(m_FilmScroll + Time.deltaTime * (.03f + .12f * m_Agitation), 1f);
            if (m_Film != null && m_Film.HasProperty("_BaseMap")) m_Film.SetTextureOffset("_BaseMap", new Vector2(0f, m_FilmScroll));
            if (m_Halo != null && m_Halo.HasProperty("_BaseMap")) m_Halo.SetTextureOffset("_BaseMap", new Vector2(0f, m_FilmScroll * .5f));
            if (m_Collider != null)
            {
                var open = State == Stage.Open && InRoom;
                m_Collider.enabled = open || State == Stage.Parked || State == Stage.Parking || State == Stage.Droplet;
                m_Collider.center = m_Centre;
                m_Collider.size = open ? new Vector3(m_Size.x * 2f + .3f, m_Size.y * 2f + .3f, .12f) : Vector3.one * .36f;
            }
        }

        // In the room the walls hide the world everywhere but the door; through it, the walls go (they would show a box
        // of the room) and the back door hides the world inside the oval, so the room shows through. Swapped by
        // activation, so it does not depend on how the occlusion material culls faces. Droplet / parked: no occluders at
        // all (the owner hides the world), so nothing else in the room is hidden while the door is folded.
        void Apply()
        {
            if (State == Stage.Closing) return;
            var on = ShowsWorld;
            m_Walls.SetActive(on && InRoom);
            m_Back.SetActive(on && !InRoom);
        }

        void Update()
        {
            if (!Driven && m_Viewer != null) Step(Time.deltaTime, m_Viewer.position, m_Viewer.forward);
        }

        void OnDestroy()
        {
            BubblePlayVisuals.Release(m_Occluder);
            BubblePlayVisuals.Release(m_Chrome);
            BubblePlayVisuals.Release(m_Film);
            BubblePlayVisuals.Release(m_Halo);
            BubblePlayVisuals.Release(m_FilmTexture);
            BubblePlayVisuals.Release(m_HaloTexture);
            BubblePlayVisuals.Release(m_WallMesh);
            BubblePlayVisuals.Release(m_BackMesh);
            BubblePlayVisuals.Release(m_RimMesh);
            BubblePlayVisuals.Release(m_FilmMesh);
            BubblePlayVisuals.Release(m_HaloMesh);
        }
    }

    /// <summary>
    /// The door's grab surface for <see cref="Moodium.Flow.MoodiumSpatialInteractionController"/> (pinch, look + pinch,
    /// or the editor mouse): a pinch on the ring that moves more than <see cref="BubbleDoorLayout.DragStart"/> drags the
    /// door over the floor; a pinch that stays put is a tap (<see cref="BubbleDoor.Press"/>: opens the parked droplet).
    /// </summary>
    public sealed class BubbleDoorHandle : MonoBehaviour
    {
        BubbleDoor m_Door;
        Action m_Pressed;
        int m_Pointer = int.MinValue;
        Vector3 m_Start;
        bool m_Dragging;

        public bool CanInteract => m_Door != null && isActiveAndEnabled;
        public bool Dragging => m_Dragging;

        public bool TryOpenParked()
        {
            if (!CanInteract || (m_Door.State != BubbleDoor.Stage.Parked && m_Door.State != BubbleDoor.Stage.Parking))
                return false;
            m_Door.Press();
            return true;
        }

        public void Initialize(BubbleDoor door, Action pressed) { m_Door = door; m_Pressed = pressed; }

        public void PointerStarted(int id, Vector3 position)
        {
            if (m_Pointer != int.MinValue || m_Door == null) return;
            m_Pointer = id; m_Start = position; m_Dragging = false;
        }

        public void PointerMoved(int id, Vector3 position)
        {
            if (id != m_Pointer || m_Door == null) return;
            if (!m_Dragging)
            {
                var moved = position - m_Start; moved.y = 0f;
                if (moved.magnitude < BubbleDoorLayout.DragStart || !m_Door.CanMove) return;
                m_Dragging = m_Door.BeginMove(m_Start);
                if (!m_Dragging) { m_Pointer = int.MinValue; return; }   // pinched in the opening: not a grab
            }
            m_Door.MoveTo(position);
        }

        public void PointerEnded(int id)
        {
            if (id != m_Pointer) return;
            m_Pointer = int.MinValue;
            if (m_Door == null) return;
            if (m_Dragging) { m_Dragging = false; m_Door.EndMove(); return; }
            m_Door.Press();
            m_Pressed?.Invoke();
        }

        public void CancelPointer(int id)
        {
            if (id != m_Pointer) return;
            m_Pointer = int.MinValue;
            if (m_Dragging && m_Door != null) m_Door.EndMove();
            m_Dragging = false;
        }
    }
}
