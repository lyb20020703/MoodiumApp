using UnityEngine;

namespace Moodium.Opening
{
    public sealed class IntroVideoHandGuideLayout
    {
        public Vector3 LeftLocalPosition { get; } = new(-0.52f, -0.04f, -0.015f);
        public Vector3 RightLocalPosition { get; } = new(0.52f, -0.04f, -0.015f);
        public float LeftHandLocalZ { get; } = -1.2f;
        public float RightHandLocalZ { get; } = 1.4f;
        public Vector3 LocalScale { get; } = new(0.16f, 0.16f, 0.16f);
    }
}
