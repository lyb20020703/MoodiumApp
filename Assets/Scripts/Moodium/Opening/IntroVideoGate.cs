namespace Moodium.Opening
{
    /// <summary>Owns the small, deterministic state machine for the interactive video preview.</summary>
    public sealed class IntroVideoGate
    {
        public double PreviewEndTime { get; }
        public double PreviewStartTime { get; }
        public bool IsActivated { get; private set; }
        public double ResumeTime => PreviewEndTime;
        bool m_PreviewSeekPending;

        public IntroVideoGate(double previewEndTime) : this(0d, previewEndTime)
        {
        }

        public IntroVideoGate(double previewStartTime, double previewEndTime)
        {
            PreviewStartTime = System.Math.Max(0d, previewStartTime);
            PreviewEndTime = System.Math.Max(PreviewStartTime + 0.01d, previewEndTime);
        }

        public bool ShouldLoopAt(double currentTime) => !IsActivated && currentTime >= PreviewEndTime;

        // A selection can be submitted after the coroutine yields and before
        // its next loop-condition check. Keep the loop alive until the caller
        // consumes that selection and explicitly activates the gate.
        public bool ShouldKeepSelectionPromptVisible(bool hasSelection) => !IsActivated;

        public bool TryBeginPreviewLoop(double currentTime)
        {
            if (m_PreviewSeekPending || !ShouldLoopAt(currentTime))
                return false;
            m_PreviewSeekPending = true;
            return true;
        }

        public bool TryCompletePreviewSeek(double currentTime)
        {
            // seekCompleted may be raised before VideoPlayer.time reflects the
            // requested frame. Keep the gate closed until playback is genuinely
            // back inside the preview segment.
            if (!m_PreviewSeekPending || currentTime < PreviewStartTime || currentTime >= PreviewEndTime)
                return false;
            m_PreviewSeekPending = false;
            return true;
        }

        public void Activate() => IsActivated = true;
    }
}
