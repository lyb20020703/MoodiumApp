namespace Moodium.CandyWorld
{
    /// <summary>Requires a deliberate second wrist-button press before leaving an active world.</summary>
    public sealed class WristExitConfirmation
    {
        readonly float m_WindowSeconds;
        float m_ExpiresAt = float.NegativeInfinity;

        public WristExitConfirmation(float windowSeconds)
        {
            m_WindowSeconds = windowSeconds > 0f ? windowSeconds : 2f;
        }

        public bool IsArmed(float now) => now <= m_ExpiresAt;

        /// <returns>True only when this press confirms a previously armed exit.</returns>
        public bool Press(float now)
        {
            if (IsArmed(now))
            {
                Reset();
                return true;
            }

            m_ExpiresAt = now + m_WindowSeconds;
            return false;
        }

        public void Reset() => m_ExpiresAt = float.NegativeInfinity;
    }
}
