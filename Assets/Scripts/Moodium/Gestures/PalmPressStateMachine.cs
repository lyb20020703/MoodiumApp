using System;

namespace Moodium.Gestures
{
    /// <summary>Turns sustained palm contact into one activation per contact edge.</summary>
    public sealed class PalmPressStateMachine
    {
        readonly float m_CooldownSeconds;
        float m_NextAllowedTime = float.NegativeInfinity;
        bool m_WasTouching;

        public PalmPressStateMachine(float cooldownSeconds)
        {
            m_CooldownSeconds = Math.Max(0f, cooldownSeconds);
        }

        public bool Update(bool touching, float currentTime)
        {
            if (!touching)
            {
                m_WasTouching = false;
                return false;
            }

            if (m_WasTouching)
                return false;

            m_WasTouching = true;
            if (currentTime < m_NextAllowedTime)
                return false;

            m_NextAllowedTime = currentTime + m_CooldownSeconds;
            return true;
        }

        public void Reset()
        {
            m_WasTouching = false;
        }
    }
}
