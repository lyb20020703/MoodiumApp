using UnityEngine;

namespace Moodium.NatureWorld
{
    /// <summary>
    /// Light vine guidance (2026-11-03). There is no text: the vine text hint was removed by request.
    /// If the user has not planted a vine after a while, Moodi says one short line: first how to touch, later how to
    /// look and pinch. Each line plays once per world session, only after a few quiet seconds.
    /// The first vine the user plants gets a short happy line instead.
    /// Clips: Resources/MoodiumVoice/{zh|en}/moodi_vo_{V1|V2|V3}_{lang}.wav
    /// </summary>
    public sealed class NatureVineGuide
    {
        /// <summary>「用指尖碰碰墙壁或桌子，会长出藤蔓哦～」</summary>
        public const string TouchLine = "V1";
        /// <summary>「看着墙，捏一下手指，藤蔓也会长出来～」</summary>
        public const string PinchLine = "V2";
        /// <summary>「哇，藤蔓长出来啦！」</summary>
        public const string FirstVineLine = "V3";

        /// <summary>Plantable seconds without a vine before the touch line.</summary>
        public const float TouchDelay = 20f;
        /// <summary>Further plantable seconds without a vine before the pinch line.</summary>
        public const float PinchDelay = 45f;
        /// <summary>Seconds that Moodi, the bubble narrator and the help card must all have been quiet.</summary>
        public const float QuietSeconds = 3f;
        /// <summary>The happy line is dropped if it cannot start this soon after the first vine.</summary>
        public const float PraiseWindow = 4f;

        float m_Plantable, m_Quiet, m_SincePlanted, m_TouchSaidAt;
        int m_Step;
        bool m_Planted, m_PraiseDone;

        /// <summary>0: nothing said yet, 1: touch line said, 2: pinch line said too.</summary>
        public int Step => m_Step;
        public bool Planted => m_Planted;

        /// <summary>
        /// Advance by <paramref name="dt"/> seconds. Returns the line Moodi should say now, or null.
        /// Call <see cref="Said"/> once the line really started (or was queued), so it is not asked for again.
        /// </summary>
        public string Tick(float dt, bool plantable, int userVines, bool busy)
        {
            m_Quiet = busy ? 0f : m_Quiet + dt;
            if (userVines > 0)
            {
                if (!m_Planted) { m_Planted = true; m_SincePlanted = 0f; }
                else m_SincePlanted += dt;
                if (m_PraiseDone) return null;
                if (m_SincePlanted > PraiseWindow) { m_PraiseDone = true; return null; }
                return busy ? null : FirstVineLine;
            }
            if (!plantable || m_Step >= 2) return null;
            m_Plantable += dt;
            if (m_Quiet < QuietSeconds) return null;
            if (m_Step == 0 && m_Plantable >= TouchDelay) return TouchLine;
            if (m_Step == 1 && m_Plantable >= m_TouchSaidAt + PinchDelay) return PinchLine;
            return null;
        }

        public void Said(string id)
        {
            if (id == TouchLine) { m_Step = 1; m_TouchSaidAt = m_Plantable; }
            else if (id == PinchLine) m_Step = 2;
            else if (id == FirstVineLine) m_PraiseDone = true;
        }
    }

    /// <summary>
    /// A light sound as a vine starts growing (2026-11-03): soft leaf rustle and three small wooden notes, about 1.7 s.
    /// Three variants, never the same one twice in a row. Played in 3D where the vine starts.
    /// </summary>
    public static class NatureVineSound
    {
        public const string Folder = "MoodiumGrowth/";
        public static readonly string[] Clips = { "vine_grow_1", "vine_grow_2", "vine_grow_3" };
        /// <summary>Quieter than a tree growing in (0.7): many vines are planted one after another.</summary>
        public const float Volume = .5f;
        /// <summary>Vines starting closer together than this share one sound (the completion ring starts six in 1.5 s).</summary>
        public const float MinGap = .8f;

        public static string Resource(int index) => Folder + Clips[index];

        /// <summary>A variant index from <paramref name="random"/> (0..1), never <paramref name="last"/>.</summary>
        public static int Pick(int last, double random)
        {
            var index = Mathf.Clamp((int)(random * Clips.Length), 0, Clips.Length - 1);
            return index == last ? (index + 1) % Clips.Length : index;
        }

        public static bool CanPlay(float now, float lastPlayed) => now - lastPlayed >= MinGap;
    }

    /// <summary>
    /// The soft green spot that appears on a reachable wall or table while Moodi gives a vine tip, so the user knows
    /// where a touch will work. It is only placed on the scanned room mesh, never on the floor.
    /// </summary>
    public static class NatureVineGuideSpot
    {
        public const string SpriteResource = "MoodiumWalkTrail/walk_guide_glow";
        public static readonly Color Tint = new(.72f, 1f, .78f, 1f);
        public const float Size = .12f, Seconds = 12f, FadeIn = .6f, FadeOut = 1f, Cancel = .35f, Pulse = 1.8f;
        public const float MinReach = .3f, MaxReach = 2.5f, Lift = .006f;
        /// <summary>A surface this far below the eyes is the floor (standing eyes ~1.6 m, seated ~1.2 m; tables are higher).</summary>
        public const float FloorDrop = 1.05f;

        /// <summary>Opacity <paramref name="t"/> s after it appeared, or <paramref name="sinceCancel"/> s after it was cancelled (negative: not cancelled).</summary>
        public static float Alpha(float t, float sinceCancel)
        {
            if (t < 0f) return 0f;
            var envelope = Mathf.Min(Mathf.Clamp01(t / FadeIn), Mathf.Clamp01((Seconds - t) / FadeOut));
            var breathe = .65f + .35f * (.5f + .5f * Mathf.Cos(2f * Mathf.PI * t / Pulse));
            if (sinceCancel >= 0f) envelope = Mathf.Min(envelope, 1f - sinceCancel / Cancel);
            return Mathf.Clamp01(envelope) * breathe;
        }

        public static bool Finished(float t, float sinceCancel) => t >= Seconds || (sinceCancel >= 0f && sinceCancel >= Cancel);

        /// <summary>Rays fanned ahead of the eyes: straight, down toward a table, and to either side.</summary>
        public static Vector3[] Directions(Vector3 forward, Vector3 right)
        {
            if (right.sqrMagnitude < 1e-6f) right = Vector3.Cross(Vector3.up, forward);
            right = right.normalized;
            return new[]
            {
                forward,
                Quaternion.AngleAxis(20f, right) * forward,
                Quaternion.AngleAxis(40f, right) * forward,
                Quaternion.AngleAxis(-30f, Vector3.up) * forward,
                Quaternion.AngleAxis(30f, Vector3.up) * forward,
            };
        }

        /// <summary>A surface hit counts if it is within reach and not the floor.</summary>
        public static bool Usable(Vector3 eye, RaycastHit hit) =>
            hit.distance >= MinReach && hit.distance <= MaxReach && eye.y - hit.point.y <= FloorDrop;
    }
}
