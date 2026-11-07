using UnityEngine;

namespace Moodium.Flow
{
    public static class WorldCarouselLayout
    {
        public static int ResolveSwipe(int selected, float displacement, float threshold, int count = 2) =>
            Mathf.Abs(displacement) < threshold ? selected : Mathf.Clamp(selected + (displacement < 0 ? 1 : -1), 0, count - 1);

        public static float Ease(float t) => 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);

        public static Vector3 Position(int index, float selection, float spacing) =>
            new((index - selection) * spacing, 0, Mathf.Abs(index - selection) * .06f);

        public static float Scale(int index, float selection, float sideScale) =>
            Mathf.Lerp(1, sideScale, Mathf.Clamp01(Mathf.Abs(index - selection)));

        public static float Alpha(int index, float selection, float sideAlpha) =>
            Mathf.Lerp(1, sideAlpha, Mathf.Clamp01(Mathf.Abs(index - selection)));
    }
}
