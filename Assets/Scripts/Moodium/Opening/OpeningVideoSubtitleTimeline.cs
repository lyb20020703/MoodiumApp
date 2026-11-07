namespace Moodium.Opening
{
    /// <summary>
    /// Absolute 30 fps subtitle cues for the combined opening video.
    /// The timeline is intentionally independent from playback and branch logic:
    /// seeking the existing VideoPlayer is enough to select the matching cue.
    /// </summary>
    public static class OpeningVideoSubtitleTimeline
    {
        readonly struct Cue
        {
            public Cue(long startFrame, long endFrame, string text)
            {
                StartFrame = startFrame;
                EndFrame = endFrame;
                Text = text;
            }

            public long StartFrame { get; }
            public long EndFrame { get; }
            public string Text { get; }
        }

        static readonly Cue[] Cues =
        {
            new(OpeningVideoTimeline.ToFrame(0, 20, 28), OpeningVideoTimeline.ToFrame(0, 23, 13), "Hi, I'm Moodi"),

            new(OpeningVideoTimeline.ToFrame(0, 32, 19), OpeningVideoTimeline.ToFrame(0, 34, 24), "欢迎来到 Moodium 的世界。"),
            new(OpeningVideoTimeline.ToFrame(0, 35, 7), OpeningVideoTimeline.ToFrame(0, 38, 12), "接下来，我会带你用触碰和想象"),
            new(OpeningVideoTimeline.ToFrame(0, 38, 19), OpeningVideoTimeline.ToFrame(0, 41, 7), "探索每一个属于你的疗愈世界。"),

            new(OpeningVideoTimeline.ToFrame(0, 47, 21), OpeningVideoTimeline.ToFrame(0, 51, 4), "太好了！请先在感官袋里装入"),
            new(OpeningVideoTimeline.ToFrame(0, 51, 4), OpeningVideoTimeline.ToFrame(0, 53, 7), "让你感到舒服的触觉材料"),
            new(OpeningVideoTimeline.ToFrame(0, 53, 20), OpeningVideoTimeline.ToFrame(0, 57, 10), "然后，选择与你喜欢的世界对应的贴纸"),
            new(OpeningVideoTimeline.ToFrame(0, 57, 14), OpeningVideoTimeline.ToFrame(0, 58, 20), "贴在袋子上"),
            new(OpeningVideoTimeline.ToFrame(0, 59, 2), OpeningVideoTimeline.ToFrame(1, 1, 21), "准备好后，就可以开始体验了"),

            new(OpeningVideoTimeline.ToFrame(1, 8, 25), OpeningVideoTimeline.ToFrame(1, 11, 4), "没关系，你也可以使用"),
            new(OpeningVideoTimeline.ToFrame(1, 11, 4), OpeningVideoTimeline.ToFrame(1, 13, 7), "身边容易找到的袋子或材料"),
            new(OpeningVideoTimeline.ToFrame(1, 13, 13), OpeningVideoTimeline.ToFrame(1, 15, 11), "然后贴上对应的贴纸"),
            new(OpeningVideoTimeline.ToFrame(1, 15, 22), OpeningVideoTimeline.ToFrame(1, 19, 10), "准备好后，就可以开始探索 Moodium 的世界了"),

            new(OpeningVideoTimeline.ToFrame(1, 24, 14), OpeningVideoTimeline.ToFrame(1, 26, 23), "Welcome to the world of Moodium."),
            new(OpeningVideoTimeline.ToFrame(1, 27, 4), OpeningVideoTimeline.ToFrame(1, 30, 21), "From now on, I’ll guide you through calming worlds"),
            new(OpeningVideoTimeline.ToFrame(1, 30, 21), OpeningVideoTimeline.ToFrame(1, 32, 21), "made from touch and imagination."),

            new(OpeningVideoTimeline.ToFrame(1, 38, 28), OpeningVideoTimeline.ToFrame(1, 39, 19), "Great!"),
            new(OpeningVideoTimeline.ToFrame(1, 39, 19), OpeningVideoTimeline.ToFrame(1, 44, 17), "First, fill your sensory bag with a tactile material you’d like to use."),
            new(OpeningVideoTimeline.ToFrame(1, 44, 28), OpeningVideoTimeline.ToFrame(1, 47, 19), "Then, choose the sticker that matches the World"),
            new(OpeningVideoTimeline.ToFrame(1, 47, 19), OpeningVideoTimeline.ToFrame(1, 50, 11), "you want to enter and place it on the bag."),
            new(OpeningVideoTimeline.ToFrame(1, 50, 22), OpeningVideoTimeline.ToFrame(1, 51, 17), "When you’re ready,"),
            new(OpeningVideoTimeline.ToFrame(1, 51, 22), OpeningVideoTimeline.ToFrame(1, 53, 18), "you can start experiencing."),

            new(OpeningVideoTimeline.ToFrame(1, 59, 8), OpeningVideoTimeline.ToFrame(2, 0, 18), "That’s totally fine."),
            new(OpeningVideoTimeline.ToFrame(2, 0, 28), OpeningVideoTimeline.ToFrame(2, 4, 15), "You can also use any suitable bag or material around you."),
            new(OpeningVideoTimeline.ToFrame(2, 4, 23), OpeningVideoTimeline.ToFrame(2, 6, 27), "Then place a Moodium sticker on it."),
            new(OpeningVideoTimeline.ToFrame(2, 7, 2), OpeningVideoTimeline.ToFrame(2, 7, 26), "When you’re ready"),
            new(OpeningVideoTimeline.ToFrame(2, 8, 2), OpeningVideoTimeline.ToFrame(2, 9, 20), "you can start experiencing")
        };

        public static string GetTextAtFrame(long frame)
        {
            for (var i = 0; i < Cues.Length; i++)
            {
                if (frame >= Cues[i].StartFrame && frame < Cues[i].EndFrame)
                    return Cues[i].Text;
            }
            return string.Empty;
        }
    }
}
