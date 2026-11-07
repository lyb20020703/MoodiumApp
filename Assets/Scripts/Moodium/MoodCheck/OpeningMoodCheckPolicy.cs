namespace Moodium.MoodCheck
{
    public static class OpeningMoodCheckPolicy
    {
        public static bool ShouldWait(bool moodCheckInProgress, bool skipRequested) =>
            moodCheckInProgress && !skipRequested;
    }
}
