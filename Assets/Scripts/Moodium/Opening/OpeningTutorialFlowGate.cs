namespace Moodium.Opening
{
    /// <summary>
    /// Prevents a failed tutorial-video handoff from being mistaken for a
    /// completed opening flow. A tutorial becomes required only after the user
    /// explicitly chooses a language.
    /// </summary>
    public sealed class OpeningTutorialFlowGate
    {
        bool m_TutorialCompleted;

        // The opening flow must be fail-closed: no early return from a video
        // coroutine may ever be interpreted as permission to open World
        // selection. Only the final tutorial branch explicitly completes it.
        public bool CanEnterWorldSelection => m_TutorialCompleted;

        public void MarkTutorialCompleted()
        {
            m_TutorialCompleted = true;
        }

        public void MarkTutorialFailed()
        {
            m_TutorialCompleted = false;
        }
    }
}
