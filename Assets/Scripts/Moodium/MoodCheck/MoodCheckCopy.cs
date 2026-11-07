using System.Collections.Generic;

namespace Moodium.MoodCheck
{
    public static class MoodCheckCopy
    {
        readonly struct Pair
        {
            public Pair(string chinese, string english) { Chinese = chinese; English = english; }
            public string Chinese { get; }
            public string English { get; }
        }

        static readonly Dictionary<string, Pair> s_Copy = new()
        {
            ["explore.ring"] = new("找到地上的光环站进去，或把手伸进袋子旁的小光环，会有惊喜", "Step into a ring of light on the floor, or put your hand in the little ring by the bag — something special awaits"),
            ["calib.searching"] = new("正在找你的桌面…", "Looking for your table…"),
            ["bag.switch.toNone"] = new("我没有感官袋", "I don't have a sensory bag"),
            ["bag.switch.toHas"] = new("我有感官袋", "I have a sensory bag"),
            ["settings.bag.title"] = new("感官袋", "Sensory bag"),
            ["prompt.entry"] = new("不知道选哪个？测测心情", "Not sure? Check your mood"),
            ["plate.kicker"] = new("Moodi 想知道", "Moodi wants to know"),
            ["plate.title.pre"] = new("你现在的心情怎么样？", "How are you feeling right now?"),
            ["plate.title.post"] = new("现在呢？", "And now?"),
            ["plate.hint"] = new("拖动 Moodi，或直接点一个心情", "Drag Moodi, or tap a mood"),
            ["axis.up"] = new("精力充沛", "Energized"),
            ["axis.down"] = new("有点累", "Tired"),
            ["axis.left"] = new("不太好", "Not great"),
            ["axis.right"] = new("还不错", "Pretty good"),
            ["mood.happy"] = new("开心 · 愉快、有活力", "Happy · cheerful, energetic"),
            ["mood.calm"] = new("平静 · 放松、安稳", "Calm · relaxed, settled"),
            ["mood.neutral"] = new("一般 · 说不上好坏", "Okay · somewhere in between"),
            ["mood.low"] = new("低落 · 有点累、提不起劲", "Low · tired, a bit down"),
            ["mood.tense"] = new("烦躁 · 紧张、有压力", "Tense · stressed, on edge"),
            ["confirm.title"] = new("你现在感觉{mood}，对吗？", "So you're feeling {mood}?"),
            ["confirm.yes"] = new("对", "That's right"),
            ["confirm.adjust"] = new("再调调", "Adjust"),
            ["skip"] = new("跳过", "Skip"),
            ["rec.happy"] = new("好心情要放大！去糖果世界撒点糖吧", "Let's make a good mood even better in Candy World"),
            ["rec.calm"] = new("这份平静很珍贵，去森林里慢慢待一会儿", "Let's keep this calm going in Nature World"),
            ["rec.neutral"] = new("都可以哦，要不先去森林里走走？", "Either is fine. How about Nature World?"),
            ["rec.low"] = new("给自己一点甜吧，轻轻捏一捏，充充电", "A little sweetness might help. Let's try Candy World"),
            ["rec.tense"] = new("先跟着袋子慢慢呼吸，让自己松下来", "Let's slow down and breathe together in Nature World"),
            ["rec.today"] = new("现在：{mood}", "Now: {mood}"),
            ["rec.sticker"] = new("找到这张贴纸，贴在袋子上", "Find this sticker and put it on your bag"),
            ["rec.sticker.sub"] = new("贴好后看向它，就能进入", "Once it's on, look at it to enter"),
            ["rec.other"] = new("也想去另一个？完全可以", "Prefer the other one? That's fine too"),
            ["rec.cta"] = new("去找这张贴纸", "Find this sticker"),
            ["rec.retry"] = new("再测一次", "Check again"),
            ["rec.short.happy"] = new("好心情，再加点甜", "Add a little sweetness"),
            ["rec.short.calm"] = new("把这份平静带进森林", "Take this calm into the woods"),
            ["rec.short.neutral"] = new("先去森林走走吧", "Maybe a walk in the woods"),
            ["rec.short.low"] = new("给自己一点甜吧", "A little sweetness might help"),
            ["rec.short.tense"] = new("去森林里慢慢呼吸", "Breathe slowly in the forest"),
            ["tag.pick"] = new("Moodi 推荐", "Moodi's pick"),
            ["compare.kicker"] = new("在{world}待了一会儿之后", "After some time in {world}"),
            ["compare.better"] = new("感觉轻松了一些呢", "Seems a little lighter now"),
            ["compare.same"] = new("谢谢你陪自己待了一会儿", "Thanks for spending a moment with yourself"),
            ["compare.worse"] = new("没关系，有些日子就是这样。想再待一会儿也可以", "That's okay. Some days are like that. Stay a little longer if you like"),
            ["compare.before"] = new("进入前", "Before"),
            ["compare.now"] = new("现在", "Now"),
            ["compare.time"] = new("停留", "Time spent"),
            ["compare.squeezes"] = new("慢慢捏了", "Squeezes"),
            ["compare.privacy"] = new("这条记录只保存在这台设备上", "This record stays on this device"),
            ["compare.continue"] = new("继续", "Continue")
        };

        public static string Get(string key, bool english) =>
            s_Copy.TryGetValue(key, out var pair) ? (english ? pair.English : pair.Chinese) : string.Empty;

        public static string Format(string key, bool english, params object[] replacements)
        {
            var value = Get(key, english);
            foreach (var replacement in replacements)
            {
                if (replacement is System.ValueTuple<string, string> pair)
                    value = value.Replace("{" + pair.Item1 + "}", pair.Item2);
            }
            return value;
        }
    }
}
