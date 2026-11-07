namespace Moodium.Reality
{
    /// <summary>Small, deterministic policy for introducing each world's environment interaction.</summary>
    public static class WorldEnvironmentPromptPolicy
    {
        public const float CandyActivationProgress = .2f;

        public static bool ShouldShow(bool natureWorld, float progress, bool hasBagInteraction) =>
            natureWorld ? progress >= 1f : progress >= CandyActivationProgress;

        public static string Message(bool natureWorld, bool english)
        {
            if (natureWorld)
                return english
                    ? "Pinch a wall, table, or floor to help the vines grow."
                    : "在墙面、桌面或地面轻轻捏一下，让藤蔓生长。";
            return english
                ? "Tap a wall or table to crack the wrapper."
                : "轻触墙面或桌面，让糖纸裂开。";
        }
    }
}
