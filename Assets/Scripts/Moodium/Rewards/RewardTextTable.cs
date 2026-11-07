using System;
using System.Collections.Generic;

namespace Moodium.Rewards
{
    public static class RewardTextTable
    {
        static readonly Dictionary<string, (string Chinese, string English)> Copy = new()
        {
            ["orb.firstHint"] = ("Moodi 送你的礼物收进手链了，也长在你的房间里啦", "Moodi's gift is on your bracelet, and it's growing in your room too"),
            ["bracelet.full"] = ("4 件礼物都收齐啦，看看你的房间吧", "All 4 gifts collected. Take a look around your room"),
            ["gem.label"] = ("第 {0} 件 · {1}", "Gift {0} · {1}")
        };

        public static string Get(string key, string language, params object[] arguments)
        {
            if (!Copy.TryGetValue(key, out var localized))
                return string.Empty;

            var template = string.Equals(language, "English", StringComparison.OrdinalIgnoreCase)
                ? localized.English
                : localized.Chinese;
            return arguments == null || arguments.Length == 0 ? template : string.Format(template, arguments);
        }
    }
}
