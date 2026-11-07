using UnityEngine;

namespace Moodium.NatureWorld
{
    /// <summary>
    /// Writing on a wish chime's paper with one index finger, like drawing on a phone (pinching still works too).
    /// The paper is virtual: nothing stops the finger, so pen-down and pen-up use separate distances (hysteresis), and
    /// a stroke only starts when the fingertip comes to the paper from the front (the side facing the viewer): a hand
    /// that drifts in from behind or from the side never draws. Distances are signed, metres: + in front of the paper.
    /// </summary>
    public static class WishFingerPen
    {
        /// <summary>Pen down when the fingertip is this close in front of the paper, or pushed this far through it.</summary>
        public const float DownFront = .008f, DownBehind = -.015f;
        /// <summary>Pen up once it is this far in front again, or this far behind (the finger went through).</summary>
        public const float LiftFront = .018f, LiftBehind = -.03f;
        /// <summary>The finger has to be at least this far in front first, so a stroke starts by approaching from the front.</summary>
        public const float ArmFront = .012f;
        /// <summary>Paper edges: uv may go this far outside 0..1 while a stroke continues (a stroke never starts outside).</summary>
        public const float EdgeSlack = .03f;
        /// <summary>Touching the paper of a blank chime opens it: fingertip within this distance of the paper, in front or slightly through.</summary>
        public const float OpenFront = .015f, OpenBehind = -.02f;

        public static bool InBounds(Vector2 uv, float slack = 0f) =>
            uv.x >= -slack && uv.x <= 1f + slack && uv.y >= -slack && uv.y <= 1f + slack;

        /// <summary>
        /// One frame for one hand. <paramref name="down"/>: the pen is down now; <paramref name="armed"/>: the fingertip has been
        /// in front of the paper since it last left. Returns the new pen state; updates <paramref name="armed"/>.
        /// </summary>
        public static bool Next(bool down, ref bool armed, float signed, Vector2 uv)
        {
            if (down)
            {
                if (InBounds(uv, EdgeSlack) && signed <= LiftFront && signed >= LiftBehind) return true;
                armed = signed > ArmFront;
                return false;
            }
            if (!InBounds(uv))
            {
                armed = signed > ArmFront;
                return false;
            }
            if (signed > ArmFront) { armed = true; return false; }
            if (signed < LiftBehind) { armed = false; return false; }
            return armed && signed <= DownFront && signed >= DownBehind;
        }

        /// <summary>A fingertip touching the paper of a blank chime (opens it for writing).</summary>
        public static bool Touching(float signed, Vector2 uv) => InBounds(uv) && signed <= OpenFront && signed >= OpenBehind;
    }
}
