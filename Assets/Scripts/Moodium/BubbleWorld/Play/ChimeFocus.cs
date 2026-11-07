using UnityEngine;

namespace Moodium.NatureWorld
{
    /// <summary>
    /// Pure rules that keep the follow bubbles (next-layer portal, exit/home bubble) away from a wish chime.
    /// See AGENT_TASK.md, part B. All positions are world space, metres; yaw in degrees (Unity: + = turn right).
    /// </summary>
    public static class ChimeFocus
    {
        /// <summary>No follow bubble may come closer than this to a blank or writing chime (horizontal distance).</summary>
        public const float KeepOutRadius = .35f;
        /// <summary>A hand this close to the blank chime starts focus (you are about to write).</summary>
        public const float HandNearChime = .25f;
        /// <summary>Focus stays on this long after its cause ends, so the bubble does not flick back mid-gesture.</summary>
        public const float TailSeconds = 1.2f;
        public const float ParkYawDegrees = 50f, ParkDistance = .6f, ParkDrop = .15f;
        public const float ParkScale = .7f, ParkAlpha = .55f;
        /// <summary>Only re-park when the parked bubble has drifted this far out of view (a real turn, not a glance).</summary>
        public const float ReparkAngle = 80f;
        public const float HoldSeconds = .6f;
        public const float UnlockGraceSeconds = .8f;
        public const float SlideSeconds = .6f, ReturnSeconds = .8f;

        /// <summary>Park on the side of the hand that is not writing. 0 = left hand, 1 = right hand, -1 = unknown (assume right-handed).</summary>
        public static float ParkYaw(float gazeYaw, int writerHand) =>
            gazeYaw + (writerHand == 0 ? ParkYawDegrees : -ParkYawDegrees);

        public static Vector3 ParkPosition(Vector3 eye, float gazeYaw, int writerHand) =>
            eye + Quaternion.Euler(0f, ParkYaw(gazeYaw, writerHand), 0f) * Vector3.forward * ParkDistance - Vector3.up * ParkDrop;

        public static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f; b.y = 0f;
            return Vector3.Distance(a, b);
        }

        /// <summary>
        /// Moves <paramref name="target"/> sideways around the eye (same distance and height) until the bubble,
        /// with radius <paramref name="bubbleRadius"/>, is outside the keep-out circle. Tries 10° steps,
        /// alternating, preferring the side the target is already on. Returns the target unchanged when it is clear.
        /// </summary>
        public static Vector3 AvoidKeepOut(Vector3 eye, Vector3 target, Vector3 keepCenter, float keepRadius, float bubbleRadius)
        {
            var limit = keepRadius + bubbleRadius;
            if (HorizontalDistance(target, keepCenter) >= limit) return target;
            var offset = target - eye;
            var toKeep = keepCenter - eye;
            var preferRight = Vector3.SignedAngle(new Vector3(toKeep.x, 0f, toKeep.z), new Vector3(offset.x, 0f, offset.z), Vector3.up) >= 0f;
            for (var step = 10f; step <= 120f; step += 10f)
            {
                for (var side = 0; side < 2; side++)
                {
                    var sign = (side == 0) == preferRight ? 1f : -1f;
                    var candidate = eye + Quaternion.Euler(0f, sign * step, 0f) * offset;
                    if (HorizontalDistance(candidate, keepCenter) >= limit) return candidate;
                }
            }
            return target;
        }

        /// <summary>True when the chime needs the space: writing, flying to its hook, or a hand reaching for the blank chime.</summary>
        public static bool RawFocus(bool writing, bool flying, bool blankExists, float nearestHandToBlank) =>
            writing || flying || (blankExists && nearestHandToBlank < HandNearChime);

        /// <summary>Applies the tail: returns the new "focus until" time.</summary>
        public static float NextFocusUntil(bool raw, float now, float focusUntil) => raw ? now + TailSeconds : focusUntil;

        public static bool Active(bool raw, float now, float focusUntil) => raw || now < focusUntil;
    }

    /// <summary>
    /// "Press and hold to leave" while a bubble is locked. Progress grows while the fingertip stays inside
    /// and resets the moment it leaves. Completes once.
    /// </summary>
    public struct BubbleHoldGate
    {
        float m_Held;
        bool m_Done;

        public float Progress => Mathf.Clamp01(m_Held / ChimeFocus.HoldSeconds);

        /// <summary>Returns true on the single frame the hold completes.</summary>
        public bool Update(bool touching, float deltaTime)
        {
            if (!touching) { m_Held = 0f; m_Done = false; return false; }
            if (m_Done) return false;
            m_Held += deltaTime;
            if (m_Held < ChimeFocus.HoldSeconds) return false;
            m_Done = true;
            return true;
        }

        public void Reset() { m_Held = 0f; m_Done = false; }
    }
}
