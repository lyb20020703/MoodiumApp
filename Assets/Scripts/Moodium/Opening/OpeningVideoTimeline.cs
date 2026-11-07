namespace Moodium.Opening
{
    /// <summary>
    /// Frame-accurate timeline for VideoPLUS.mp4. Timecodes are authored at 30 fps
    /// so the interaction boundaries remain stable across editor and visionOS.
    /// </summary>
    public static class OpeningVideoTimeline
    {
        public const int FramesPerSecond = 30;
        // Temporary diagnostic fallback: lets the combined-video branch be
        // tested without relying on spatial button input.
        public const float LanguageAutoSelectTimeoutSeconds = 30f;

        public readonly struct Segment
        {
            public Segment(long startFrame, long endFrame)
            {
                StartFrame = startFrame;
                EndFrame = endFrame;
            }

            public long StartFrame { get; }
            public long EndFrame { get; }
        }

        public readonly struct LanguageTimeline
        {
            public LanguageTimeline(long entryFrame, Segment questionLoop, Segment yes, Segment no)
            {
                EntryFrame = entryFrame;
                QuestionLoop = questionLoop;
                Yes = yes;
                No = no;
            }

            public long EntryFrame { get; }
            public Segment QuestionLoop { get; }
            public Segment Yes { get; }
            public Segment No { get; }
        }

        public static readonly Segment LanguageLoop = new(
            ToFrame(0, 24, 14),
            ToFrame(0, 32, 8));

        public static readonly LanguageTimeline Chinese = new(
            LanguageLoop.EndFrame,
            new Segment(ToFrame(0, 41, 10), ToFrame(0, 47, 0)),
            new Segment(ToFrame(0, 47, 0), ToFrame(1, 2, 13)),
            new Segment(ToFrame(1, 8, 18), ToFrame(1, 20, 9)));

        public static readonly LanguageTimeline English = new(
            ToFrame(1, 23, 17),
            new Segment(ToFrame(1, 33, 21), ToFrame(1, 38, 19)),
            new Segment(ToFrame(1, 38, 19), ToFrame(1, 53, 27)),
            new Segment(ToFrame(1, 58, 27), ToFrame(2, 10, 29)));

        // Shared post-tutorial visual waiting segment used while the user locates
        // a physical Moodium world sticker.
        public static readonly Segment StickerSelectionLoop = new(
            ToFrame(0, 41, 10),
            ToFrame(0, 46, 0));

        public static long ToFrame(int minutes, int seconds, int frames) =>
            ((long)minutes * 60L + seconds) * FramesPerSecond + frames;

        public static double FrameToSeconds(long frame) => frame / (double)FramesPerSecond;

        public static bool HasKnownInsufficientFrameCount(ulong availableFrameCount, long requiredFinalFrame) =>
            availableFrameCount > 0 && availableFrameCount <= (ulong)requiredFinalFrame;

    }
}
