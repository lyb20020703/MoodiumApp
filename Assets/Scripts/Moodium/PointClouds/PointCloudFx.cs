using UnityEngine;

namespace Moodium.PointClouds
{
    /// <summary>
    /// Pure effect math for point-cloud models (no Unity objects, EditMode-testable). The preview renders in
    /// Preview/ were made with the same formulas (Source/tools/pc_fx.py), so tuning one tunes the other.
    /// All distances are metres in the model root's local space.
    /// </summary>
    public static class PointCloudFx
    {
        // ---- Appear: points fly in from a loose cloud and settle from the bottom up ----
        public const float AppearSeconds = 2.2f;
        public const float AppearEachSeconds = .55f;
        public const float AppearSpread = .45f;
        public const float AppearLift = .25f;
        public const float AppearMinSize = .35f;

        // ---- Touch: points near a fingertip burst out of the surface, shrink to dust, then spring back ----
        public const float TouchRadius = .2f;
        public const float FoliageTouchRadius = .3f;
        public const float TouchPush = .34f;
        public const float TouchFloat = .12f;
        public const float TouchShrink = .55f;
        public const float GlowAt = .3f;
        public const float PressStiffness = 140f;
        public const float ReleaseStiffness = 16f;

        public static float Hash01(int index, int salt)
        {
            unchecked
            {
                var h = (uint)index * 2654435761u + (uint)salt * 97531u;
                h ^= h >> 16;
                h *= 0x45d9f3bu;
                h ^= h >> 16;
                return (h & 0xFFFF) / 65535f;
            }
        }

        public static float EaseOutCubic(float u)
        {
            u = Mathf.Clamp01(u);
            var v = 1f - u;
            return 1f - v * v * v;
        }

        /// <summary>Direction a point flies in from / scatters toward: mostly along the normal, randomised per point.</summary>
        public static Vector3 ScatterDirection(int index, Vector3 normal)
        {
            var r = new Vector3(Hash01(index, 1) * 2f - 1f, Hash01(index, 2) * 2f - 1f, Hash01(index, 3) * 2f - 1f);
            var d = normal * .7f + r;
            return d.sqrMagnitude < 1e-8f ? normal : d.normalized;
        }

        /// <summary>Start time (seconds) of a point's fly-in: bottom first, with a little randomness.</summary>
        public static float AppearDelay(float height01, float seed)
        {
            var window = AppearSeconds - AppearEachSeconds;
            return height01 * window * .85f + seed * window * .15f;
        }

        /// <summary>Position and size factor (0 = hidden) of a point <paramref name="t"/> seconds into the appear.</summary>
        public static void Appear(int index, Vector3 rest, Vector3 normal, float height01, float t, out Vector3 position, out float size)
        {
            var u = Mathf.Clamp01((t - AppearDelay(height01, Hash01(index, 4))) / AppearEachSeconds);
            var e = EaseOutCubic(u);
            var start = rest + ScatterDirection(index, normal) * AppearSpread + Vector3.up * AppearLift;
            position = Vector3.LerpUnclamped(start, rest, e);
            size = u > 0f ? AppearMinSize + (1f - AppearMinSize) * e : 0f;
        }

        /// <summary>How strongly a fingertip at <paramref name="hand"/> affects a point (0..1).</summary>
        public static float TouchWeight(Vector3 point, Vector3 hand, float radius)
        {
            var d = Vector3.Distance(point, hand);
            var w = Mathf.Clamp01(1f - d / Mathf.Max(1e-4f, radius));
            return w * Mathf.Sqrt(w); // w^1.5
        }

        /// <summary>
        /// Where a touched point wants to be (offset from rest): out of the surface and sideways away from the finger,
        /// lifted a little, scaled by the weight.
        /// </summary>
        public static Vector3 TouchOffset(int index, Vector3 point, Vector3 normal, Vector3 hand, float weight)
        {
            if (weight <= 0f) return Vector3.zero;
            var d = point - hand;
            var side = d - normal * Vector3.Dot(d, normal);
            side = side.sqrMagnitude < 1e-10f ? Vector3.zero : side.normalized;
            var dir = normal * 1.1f + side * .9f + ScatterDirection(index, normal) * .45f;
            dir = dir.sqrMagnitude < 1e-10f ? normal : dir.normalized;
            var seed = Hash01(index, 4);
            var offset = dir * (weight * TouchPush * (.55f + .9f * seed));
            offset.y += weight * TouchFloat * seed;
            return offset;
        }

        public static float TouchSize(float weight) => 1f - TouchShrink * Mathf.Clamp01(weight);

        /// <summary>Critically damped spring step (stable for any dt): pulls <paramref name="value"/> toward target.</summary>
        public static Vector3 SpringStep(Vector3 value, ref Vector3 velocity, Vector3 target, float stiffness, float dt)
        {
            var omega = Mathf.Sqrt(Mathf.Max(0f, stiffness));
            var x = omega * dt;
            var exp = 1f / (1f + x + .48f * x * x + .235f * x * x * x);
            var change = value - target;
            var temp = (velocity + omega * change) * dt;
            velocity = (velocity - omega * temp) * exp;
            return target + (change + temp) * exp;
        }

        /// <summary>Flip a normal so it points away from the model's vertical centre axis (double-sided leaf cards).</summary>
        public static Vector3 OrientOutward(Vector3 point, Vector3 normal, float medianHeight, float height)
        {
            var outward = new Vector3(point.x, .15f * height * Mathf.Sign(point.y - medianHeight), point.z);
            return Vector3.Dot(normal, outward) < 0f ? -normal : normal;
        }

        // ---- Colour atlas: soft round points ----
        // Every point owns two 4×4-texel blocks: block 2i = its own colour, block 2i + 1 = its glow colour. Alpha is a
        // soft round blob (BlobAlpha), so with bilinear sampling and URP/Unlit alpha clipping at threshold 0 —
        // alpha-to-coverage on visionOS — each square quad shows a soft round point, like a Gaussian splat.
        // Row 0 of the atlas is at the bottom (Texture2D.SetPixels32 order).

        /// <summary>Alpha of texel (x, y) in a 4×4 block: solid centre, soft edge, faint corners.</summary>
        public static float BlobAlpha(int x, int y)
        {
            var edgeX = x == 0 || x == 3;
            var edgeY = y == 0 || y == 3;
            return edgeX && edgeY ? .05f : edgeX || edgeY ? .35f : 1f;
        }

        /// <summary>
        /// UV of corner (cx, cy ∈ {0, 1}) of point <paramref name="index"/>'s own / glow block. The quad spans the block's
        /// outer texel centres (0.5 … 3.5), so bilinear sampling never reaches a neighbouring block.
        /// </summary>
        public static Vector2 BlockUV(int index, bool glow, int atlasSize, int cx, int cy)
        {
            var block = index * 2 + (glow ? 1 : 0);
            var perRow = atlasSize / 4;
            var bx = block % perRow;
            var by = block / perRow;
            return new Vector2((bx * 4 + (cx == 0 ? .5f : 3.5f)) / atlasSize, (by * 4 + (cy == 0 ? .5f : 3.5f)) / atlasSize);
        }
        public static Vector2 BlockUV(int index, bool glow, int atlasSize, int cx, int cy, bool sharedGlow)
        {
            var block = sharedGlow ? index + (glow ? 1 : 0) : index * 2 + (glow ? 1 : 0);
            var perRow = atlasSize / 4; var bx = block % perRow; var by = block / perRow;
            return new Vector2((bx * 4 + (cx == 0 ? .5f : 3.5f)) / atlasSize, (by * 4 + (cy == 0 ? .5f : 3.5f)) / atlasSize);
        }
        public static Vector2 BlockUV(int index, bool glow, int atlasSize, int cx, int cy, bool sharedGlow, int pointCount)
        {
            return BlockUV(glow && sharedGlow ? pointCount : index, false, atlasSize, cx, cy, false);
        }

        /// <summary>Corner order matches the quad: 0 = (−a, −b), 1 = (+a, −b), 2 = (+a, +b), 3 = (−a, +b).</summary>
        public static void WriteBlockUVs(Vector2[] uvs, int offset, int index, bool glow, int atlasSize)
        {
            uvs[offset] = BlockUV(index, glow, atlasSize, 0, 0);
            uvs[offset + 1] = BlockUV(index, glow, atlasSize, 1, 0);
            uvs[offset + 2] = BlockUV(index, glow, atlasSize, 1, 1);
            uvs[offset + 3] = BlockUV(index, glow, atlasSize, 0, 1);
        }
        public static void WriteBlockUVs(Vector2[] uvs, int offset, int index, bool glow, int atlasSize, bool sharedGlow)
        {
            uvs[offset] = BlockUV(index, glow, atlasSize, 0, 0, sharedGlow);
            uvs[offset + 1] = BlockUV(index, glow, atlasSize, 1, 0, sharedGlow);
            uvs[offset + 2] = BlockUV(index, glow, atlasSize, 1, 1, sharedGlow);
            uvs[offset + 3] = BlockUV(index, glow, atlasSize, 0, 1, sharedGlow);
        }
        public static void WriteBlockUVs(Vector2[] uvs, int offset, int index, bool glow, int atlasSize, bool sharedGlow, int pointCount)
        {
            uvs[offset] = BlockUV(index, glow, atlasSize, 0, 0, sharedGlow, pointCount);
            uvs[offset + 1] = BlockUV(index, glow, atlasSize, 1, 0, sharedGlow, pointCount);
            uvs[offset + 2] = BlockUV(index, glow, atlasSize, 1, 1, sharedGlow, pointCount);
            uvs[offset + 3] = BlockUV(index, glow, atlasSize, 0, 1, sharedGlow, pointCount);
        }

        /// <summary>Smallest power-of-two atlas with two 4×4 blocks per point (80 000 points → 2048²).</summary>
        public static int AtlasSizeFor(int pointCount)
        {
            var size = 4;
            while ((long)(size / 4) * (size / 4) < (long)pointCount * 2) size *= 2;
            return size;
        }
        public static int AtlasSizeFor(int pointCount, bool sharedGlow)
        {
            if (!sharedGlow) return AtlasSizeFor(pointCount);
            var size = 4;
            while ((long)(size / 4) * (size / 4) < pointCount + 1L) size *= 2;
            return size;
        }

        public static Color32 GlowColor(Color32 c) => new(
            (byte)(c.r * .45f + 255f * .55f), (byte)(c.g * .45f + 255f * .55f), (byte)(c.b * .45f + 255f * .55f), 255);

        /// <summary>
        /// The 4 corners of a point's quad: lying on the surface (facing along the normal, like a Gaussian splat's
        /// flat disc), turned by a per-point angle so the squares don't line up.
        /// </summary>
        public static void QuadAxes(int index, Vector3 normal, float halfSize, out Vector3 a, out Vector3 b)
        {
            var helper = Mathf.Abs(normal.y) > .95f ? Vector3.forward : Vector3.up;
            var t = Vector3.Cross(helper, normal).normalized;
            var u = Vector3.Cross(normal, t);
            var angle = Hash01(index, 5) * Mathf.PI * .5f;
            var ca = Mathf.Cos(angle);
            var sa = Mathf.Sin(angle);
            a = (t * ca + u * sa) * halfSize;
            b = (-t * sa + u * ca) * halfSize;
        }
    }
}
