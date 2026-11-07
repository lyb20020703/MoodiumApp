namespace Moodium.Opening
{
    public enum VideoLoopAction { None, Play, Seek }

    /// <summary>
    /// Decides when a looping video segment needs a nudge (Play) or a seek, from the frame number and isPlaying only.
    /// No Unity video calls, so it can be tested in EditMode.
    ///
    /// Why it exists: on visionOS, right after a seek, <c>frame</c>/<c>time</c>/<c>isPlaying</c> still show the old
    /// state for a while, and a <c>seekCompleted</c> from an earlier seek (the opening's own loops) can arrive late.
    /// The old loop trusted those values and kept seeking again every frame or two, which froze the picture on one
    /// frame. Here:
    /// - a seek counts as done only when a frame near the segment start is actually seen;
    /// - the wrap at the segment end happens only after that;
    /// - a short isPlaying=false never seeks, a long one only asks for Play;
    /// - a watchdog notices frames that stop advancing: Play first, then a new seek.
    /// </summary>
    public sealed class VideoSegmentLoopDriver
    {
        /// <summary>A seek may land a few frames early (key frames) or late.</summary>
        public const int LandingFramesBefore = 3, LandingFramesAfter = 15;

        readonly long m_Start, m_End;
        readonly float m_Settle, m_Stall, m_SeekTimeout;

        bool m_SeekPending;
        float m_SeekAt;
        bool m_Nudged;
        long m_LastFrame = long.MinValue;
        float m_LastAdvance;
        float m_NotPlayingSince = -1f;

        public VideoSegmentLoopDriver(long startFrame, long endFrame, float settleSeconds = .5f,
            float stallSeconds = 1.2f, float seekTimeoutSeconds = 2.5f)
        {
            m_Start = startFrame;
            m_End = endFrame;
            m_Settle = settleSeconds;
            m_Stall = stallSeconds;
            m_SeekTimeout = seekTimeoutSeconds;
        }

        public long StartFrame => m_Start;
        public long EndFrame => m_End;
        public bool SeekPending => m_SeekPending;

        public bool IsLanding(long frame) =>
            frame >= m_Start - LandingFramesBefore && frame < m_Start + LandingFramesAfter;

        /// <summary>Call right after asking the player to go to <see cref="StartFrame"/>.</summary>
        public void NotifySeekIssued(float now)
        {
            m_SeekPending = true;
            m_SeekAt = now;
            m_Nudged = false;
            m_LastFrame = long.MinValue;
            m_LastAdvance = now;
            m_NotPlayingSince = -1f;
        }

        public VideoLoopAction Tick(float now, long frame, bool isPlaying)
        {
            if (m_SeekPending)
            {
                // Confirmed only by seeing the frame, never by seekCompleted alone.
                if (now > m_SeekAt && IsLanding(frame))
                {
                    m_SeekPending = false;
                    m_Nudged = false;
                    m_LastFrame = frame;
                    m_LastAdvance = now;
                    m_NotPlayingSince = -1f;
                    return VideoLoopAction.None;
                }
                if (now - m_SeekAt < m_SeekTimeout) return VideoLoopAction.None;
                if (!m_Nudged)
                {
                    // Some backends present the sought frame only once playing: Play, then wait one stall window.
                    m_Nudged = true;
                    m_SeekAt = now - m_SeekTimeout + m_Stall;
                    return VideoLoopAction.Play;
                }
                return VideoLoopAction.Seek;
            }

            if (frame != m_LastFrame)
            {
                if (frame > m_LastFrame) { m_LastAdvance = now; m_Nudged = false; }
                m_LastFrame = frame;
            }

            // Inside the segment since the confirmed seek: wrap at the end; anything outside is a lost position.
            if (frame >= m_End || frame < m_Start - LandingFramesBefore) return VideoLoopAction.Seek;

            if (!isPlaying)
            {
                if (m_NotPlayingSince < 0f) m_NotPlayingSince = now;
                else if (now - m_NotPlayingSince >= m_Settle)
                {
                    m_NotPlayingSince = now;
                    return VideoLoopAction.Play;
                }
            }
            else m_NotPlayingSince = -1f;

            if (now - m_LastAdvance >= m_Stall)
            {
                if (!m_Nudged)
                {
                    m_Nudged = true;
                    m_LastAdvance = now;
                    return VideoLoopAction.Play;
                }
                return VideoLoopAction.Seek;
            }
            return VideoLoopAction.None;
        }
    }
}
