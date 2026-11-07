namespace Moodium.Opening
{
    /// <summary>
    /// Separates a choice submission from the video seek that follows it.
    /// Every input source receives the same one-frame handoff. Do not wait for
    /// an EnhancedTouch touchId to disappear: PolySpatial spatial pointers can
    /// keep that touch alive after Press, which previously made manual choices
    /// behave differently from the working timeout path on Vision Pro.
    /// </summary>
    public sealed class OpeningVideoSpatialChoiceGate
    {
        int m_SelectionFrame = -1;
        int m_SelectingTouchId = -1;

        public bool HasSelection => m_SelectionFrame >= 0;
        public int SelectingTouchId => m_SelectingTouchId;

        public void SelectFromSpatialTouch(int frame, int touchId)
        {
            if (HasSelection)
                return;
            m_SelectionFrame = frame;
            m_SelectingTouchId = touchId;
        }

        public void SelectAutomatically(int frame)
        {
            if (HasSelection)
                return;
            m_SelectionFrame = frame;
        }

        public bool IsReadyForVideoTransition(int frame, bool selectingTouchIsActive)
        {
            return HasSelection && frame > m_SelectionFrame;
        }
    }
}
