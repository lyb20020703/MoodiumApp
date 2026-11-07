using System;
using Moodium.Interaction;
using UnityEngine;
using UnityEngine.Video;

namespace Moodium.Reality
{
    public interface ISqueezeProgressDisplay
    {
        void Configure(Camera camera, VideoClip candyClip, VideoClip natureClip);
        void Begin(bool natureWorld);
        void BindToSoft(ChocolateCapsuleInteraction capsule);
        void SetProgress(float normalizedProgress);
        void ResetAndHide();
        bool TryGetDomeCenter(out Vector3 center, out Vector3 up);
        void Splash(float amount);
        float Squash { get; }
        event Action<Vector3> Popped;
        void SetBubbleSuspended(bool suspended);
        bool TryGetBurstExclusion(out Vector3 center, out float radius);
        bool OverlapsBurstVideo(Bounds bounds);
    }
}
