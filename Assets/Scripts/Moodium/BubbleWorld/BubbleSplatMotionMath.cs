using UnityEngine;

namespace Moodium.NatureWorld
{
    /// <summary>CPU reference for the graph. GradientNoise matches Unity's LegacyMod editor path;
    /// MaterialX uses its native Perlin noise, so individual gust phases can differ on device.</summary>
    public static class BubbleSplatMotionMath
    {
        public const float MaxDisplacement = .8f;
        public const float CenterEncodingScale = 256f;
        public static float Smooth(float a, float b, float x)
        { var t = Mathf.Clamp01((x - a) / Mathf.Max(.00001f, b - a)); return t * t * (3 - 2 * t); }
        public static float Blend(float dt, float tau) => 1 - Mathf.Exp(-Mathf.Max(0, dt) / Mathf.Max(.0001f, tau));
        public static float Frac(float x) => x - Mathf.Floor(x);
        public static Vector3 SafeNormal(Vector3 v) => v / Mathf.Max(v.magnitude, .0001f);
        public static Color EncodeCenter(Vector3 p) => new Color(p.x / CenterEncodingScale + .5f,
            p.y / CenterEncodingScale + .5f, p.z / CenterEncodingScale + .5f, 1);
        public static Vector3 DecodeCenter(Color c) => new Vector3(c.r - .5f, c.g - .5f, c.b - .5f) * CenterEncodingScale;
        public static float Sway(Vector3 p, Color c, float eyeHeight, bool candy)
        {
            var h = p.y + eyeHeight;
            if (candy) return .35f + .65f * Smooth(.3f, 3f, h);
            var leafy = Smooth(0, .07f, c.g - .5f * (c.r + c.b));
            return Mathf.Max(.2f, Smooth(1.2f, 4, h) * Mathf.Lerp(.35f, 1, leafy),
                leafy * Smooth(.25f, 1, h) * .8f, leafy * (1 - Smooth(.3f, .8f, h)) * .3f);
        }
        static Vector2 Gradient(Vector2 p)
        {
            p.x %= 289; p.y %= 289;
            var x = (34 * p.x + 1) * p.x % 289 + p.y;
            x = (34 * x + 1) * x % 289;
            x = Frac(x / 41) * 2 - 1;
            return new Vector2(x - Mathf.Floor(x + .5f), Mathf.Abs(x) - .5f).normalized;
        }
        public static float Noise(Vector2 p)
        {
            var ip = new Vector2(Mathf.Floor(p.x), Mathf.Floor(p.y));
            var f = p - ip;
            var a = Vector2.Dot(Gradient(ip), f);
            var b = Vector2.Dot(Gradient(ip + Vector2.up), f - Vector2.up);
            var c = Vector2.Dot(Gradient(ip + Vector2.right), f - Vector2.right);
            var d = Vector2.Dot(Gradient(ip + Vector2.one), f - Vector2.one);
            var u = new Vector2(f.x * f.x * f.x * (f.x * (f.x * 6 - 15) + 10),
                f.y * f.y * f.y * (f.y * (f.y * 6 - 15) + 10));
            return Mathf.LerpUnclamped(Mathf.LerpUnclamped(a, b, u.y), Mathf.LerpUnclamped(c, d, u.y), u.x) + .5f;
        }
        public struct Parameters
        {
            public Vector3 Up, Wind, Eye;
            public Vector4 HandA, HandB;
            public Vector2 HandAmount;
            public float Strength, Style, Self, Boost, FloatStrength, InnerCos, OuterCos;
            public static Parameters Default => new Parameters { Up = Vector3.up, Wind = new Vector3(.8f, 0, .6f),
                Self = 1, Boost = 1, FloatStrength = .8f, InnerCos = Mathf.Cos(4 * Mathf.Deg2Rad),
                OuterCos = Mathf.Cos(9 * Mathf.Deg2Rad), HandA = new Vector4(0,0,0,.3f), HandB = new Vector4(0,0,0,.3f) };
        }
        public static float Near(float distance, float radius) => 1 - Smooth(.6f * radius, radius, distance);
        public static float Ray(Vector3 p, Vector3 h, Vector3 eye, float outerCos, float innerCos)
        {
            var toP = p - eye; var toH = h - eye;
            return Smooth(outerCos, innerCos, Vector3.Dot(SafeNormal(toP), SafeNormal(toH))) *
                Smooth(toH.magnitude, toH.magnitude + .4f, toP.magnitude);
        }
        public static Vector3 Hand(Vector3 p, float seed, float t, Vector4 hand, float amount, Parameters a, out float k)
        {
            var h = (Vector3)hand; var v = p - h;
            var near = Near(v.magnitude, Mathf.Max(.0001f, hand.w));
            k = Mathf.Clamp01(amount * Mathf.Max(near, .85f * Ray(p, h, a.Eye, a.OuterCos, a.InnerCos)));
            var toP = p - a.Eye; var axis = SafeNormal(h - a.Eye);
            var perp = SafeNormal(toP - Vector3.Dot(toP, axis) * axis + Vector3.one * .0001f);
            var outv = SafeNormal(Vector3.Lerp(perp, SafeNormal(v + Vector3.one * .0001f), near));
            var jit = new Vector3(Frac(seed * 7.13f), Frac(seed * 3.71f), Frac(seed * 5.37f)) * 2 - Vector3.one;
            var dir = SafeNormal(outv * .6f + a.Up * .45f + jit * .35f);
            var mag = Mathf.Min(.8f, k * k * (.05f + .012f * p.magnitude) * (.6f + .4f * Mathf.Sin(.7f * t + 6.283f * seed)));
            return dir * mag * a.Boost;
        }
        public static Vector3 Displace(Vector3 p, float seed, float sway, float t, Parameters a)
        {
            var r = p.magnitude; var s = new Vector2(p.x, p.z) / (1 + .15f * r) * 1.2f;
            var phase = Noise(s); var side = SafeNormal(Vector3.Cross(a.Up, a.Wind));
            var amplitude = a.Boost * a.Strength * Mathf.Min(.25f, .012f + .0035f * r) * sway;
            var gust = .55f + .45f * (.5f + .5f * Mathf.Sin(.23f * t) * Mathf.Sin(.37f * t + 1.3f));
            var wave = Noise(s * .5f - t * .9f * new Vector2(a.Wind.x, a.Wind.z));
            var sway2 = .5f * Mathf.Sin(1.7f * t + 6.283f * phase) + .25f * Mathf.Sin(2.9f * t + 10.68f * phase);
            var m = gust * (.6f * sway2 + .8f * (wave - .35f));
            var wind = amplitude * (a.Wind * m + side * (.15f * Mathf.Sin(6 * t + 6.283f * seed)) +
                a.Up * (.25f * Mathf.Sin(4.1f * t + 18.85f * phase)));
            var floating = amplitude * a.FloatStrength * (a.Up * Mathf.Sin(.9f * t + 6.283f * seed) +
                a.Wind * (.35f * Mathf.Sin(.6f * t + 6.283f * phase)) + side * (.35f * Mathf.Cos(.7f * t + 6.283f * phase)));
            var self = a.Boost * a.Self * a.Strength * (.004f + .0012f * r) * new Vector3(
                Mathf.Sin(1.1f * t + 6.283f * seed), .6f * Mathf.Sin(.8f * t + 12.57f * seed), Mathf.Cos(1.3f * t + 18.85f * seed));
            var displacement = Vector3.Lerp(wind, floating, a.Style) + self +
                Hand(p, seed, t, a.HandA, a.HandAmount.x, a, out _) + Hand(p, seed, t, a.HandB, a.HandAmount.y, a, out _);
            return displacement * Mathf.Min(1, MaxDisplacement / Mathf.Max(displacement.magnitude, .0001f));
        }
    }
}
