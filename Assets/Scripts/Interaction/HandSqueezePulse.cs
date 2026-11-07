using UnityEngine;

namespace Moodium.Interaction
{
    /// <summary>
    /// Watches one hand signal while the hand rests on the sensory bag. A dip of <c>press</c> below the recent high counts
    /// as one squeeze; the signal has to come back up by <c>release</c> before the next one. It starts out "pressed",
    /// because the contact that began it was itself a squeeze, so the hand settling onto the bag never counts twice.
    /// The recent high is kept for <c>highHoldSeconds</c>, then slowly sinks (<c>highDecayPerSecond</c>), so a quick
    /// squeeze counts in full while a hand drifting down over seconds is not a squeeze.
    /// </summary>
    public sealed class SqueezePulseSignal
    {
        readonly float m_Press, m_Release, m_Smoothing, m_HighHold, m_HighDecay;
        bool m_Started, m_Pressed;
        float m_Value, m_High, m_Low, m_HighAge;

        public SqueezePulseSignal(float press, float release, float smoothingSeconds, float highHoldSeconds,
            float highDecayPerSecond)
        {
            m_HighHold = Mathf.Max(0f, highHoldSeconds);
            m_Press = Mathf.Max(.0001f, press);
            m_Release = Mathf.Max(.0001f, release);
            m_Smoothing = Mathf.Max(0f, smoothingSeconds);
            m_HighDecay = Mathf.Max(0f, highDecayPerSecond);
        }

        public bool Started => m_Started;
        public bool Pressed => m_Pressed;
        public float Value => m_Value;

        public void Reset() => m_Started = false;

        /// <summary>Counts as pressed from the current value on (used when another signal of the same hand fired).</summary>
        public void ForcePressed()
        {
            m_Pressed = true;
            m_Low = m_Value;
        }

        /// <returns>True on the frame a new squeeze starts; <paramref name="dip"/> is how far the signal fell.</returns>
        public bool Update(float raw, float deltaSeconds, out float dip)
        {
            dip = 0f;
            if (float.IsNaN(raw) || float.IsInfinity(raw)) return false;
            if (!m_Started)
            {
                m_Started = true;
                m_Value = m_High = m_Low = raw;
                m_Pressed = true;
                return false;
            }

            var dt = Mathf.Max(0f, deltaSeconds);
            var k = m_Smoothing <= 0f ? 1f : 1f - Mathf.Exp(-dt / m_Smoothing);
            m_Value += (raw - m_Value) * k;

            if (m_Pressed)
            {
                m_Low = Mathf.Min(m_Low, m_Value);
                if (m_Value >= m_Low + m_Release)
                {
                    m_Pressed = false;
                    m_High = m_Value;
                    m_HighAge = 0f;
                }
                return false;
            }

            if (m_Value >= m_High)
            {
                m_High = m_Value;
                m_HighAge = 0f;
            }
            else
            {
                m_HighAge += dt;
                if (m_HighAge > m_HighHold) m_High = Mathf.Max(m_Value, m_High - m_HighDecay * dt);
            }
            if (m_Value > m_High - m_Press) return false;
            dip = m_High - m_Value;
            m_Pressed = true;
            m_Low = m_Value;
            return true;
        }
    }

    /// <summary>What one tracked hand looks like this frame, in metres.</summary>
    public readonly struct HandSqueezeSample
    {
        /// <summary>Mean height of the palm and the index, middle and ring fingertips: pressing into the bag lowers it.</summary>
        public readonly float Height;
        /// <summary>Mean distance of the five fingertips from the palm: closing the fingers around the bag lowers it.</summary>
        public readonly float Spread;

        public HandSqueezeSample(float height, float spread)
        {
            Height = height;
            Spread = spread;
        }
    }

    /// <summary>
    /// A new squeeze by a hand that stays on the bag: either pressing down (<see cref="HandSqueezeSample.Height"/>) or
    /// closing the fingers (<see cref="HandSqueezeSample.Spread"/>). When one fires, the other is treated as pressed too,
    /// so one squeeze that does both is counted once.
    /// </summary>
    public sealed class HandSqueezePulse
    {
        // Thresholds of sensitivity level 3 (the default); SqueezeSensitivity holds the other levels.
        /// <summary>Pressing down this far (from the recent high) is a squeeze …</summary>
        public const float PressDepth = .006f;
        /// <summary>… and the hand has to come back up this far before the next one.</summary>
        public const float PressRebound = .004f;
        /// <summary>Fingertips closing this much closer to the palm is a squeeze …</summary>
        public const float CloseAmount = .008f;
        /// <summary>… and they have to open this far again before the next one.</summary>
        public const float CloseRebound = .005f;
        /// <summary>Light smoothing against hand-tracking jitter.</summary>
        public const float SmoothingSeconds = .04f;
        /// <summary>A squeeze has this long to reach its depth before the recent high starts to sink …</summary>
        public const float HighHoldSeconds = .5f;
        /// <summary>… at this rate: a hand drifting down slower than this is settling, not squeezing.</summary>
        public const float DriftPerSecond = .01f;

        SqueezePulseSignal m_Height = new(PressDepth, PressRebound, SmoothingSeconds, HighHoldSeconds, DriftPerSecond);
        SqueezePulseSignal m_Spread = new(CloseAmount, CloseRebound, SmoothingSeconds, HighHoldSeconds, DriftPerSecond);

        public SqueezePulseSignal Height => m_Height;
        public SqueezePulseSignal Spread => m_Spread;
        /// <summary>The sensitivity level in use (the constants above are level 3).</summary>
        public int Level { get; private set; } = SqueezeSensitivity.DefaultLevel;

        /// <summary>Switches to another sensitivity level's thresholds; the next sample starts the hand afresh.</summary>
        public void Configure(SqueezeSensitivity.Profile profile)
        {
            if (profile.Level == Level) return;
            Level = profile.Level;
            m_Height = new(profile.PressDepth, profile.PressRebound, SmoothingSeconds, HighHoldSeconds, DriftPerSecond);
            m_Spread = new(profile.CloseAmount, profile.CloseRebound, SmoothingSeconds, HighHoldSeconds, DriftPerSecond);
        }

        public void Reset()
        {
            m_Height.Reset();
            m_Spread.Reset();
        }

        /// <returns>True on the frame a new squeeze starts. <paramref name="reason"/> says which signal saw it.</returns>
        public bool Update(HandSqueezeSample sample, float deltaSeconds, out string reason, out float dip)
        {
            var pressed = m_Height.Update(sample.Height, deltaSeconds, out var pressDip);
            var closed = m_Spread.Update(sample.Spread, deltaSeconds, out var closeDip);
            reason = pressed ? "press" : closed ? "close" : null;
            dip = pressed ? pressDip : closeDip;
            if (!pressed && !closed) return false;
            m_Height.ForcePressed();
            m_Spread.ForcePressed();
            return true;
        }
    }
}
