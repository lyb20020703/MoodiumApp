using UnityEngine;
using UnityEngine.Rendering;

namespace Moodium.Opening
{
    /// <summary>Shared URP surface settings for HEVC videos carrying alpha.</summary>
    public readonly struct OpeningVideoSurfaceSettings
    {
        public static OpeningVideoSurfaceSettings TransparentAlpha => new(
            surfaceType: 1f,
            sourceBlend: (float)BlendMode.SrcAlpha,
            destinationBlend: (float)BlendMode.OneMinusSrcAlpha,
            depthWrite: 0f);

        public OpeningVideoSurfaceSettings(float surfaceType, float sourceBlend, float destinationBlend, float depthWrite)
        {
            SurfaceType = surfaceType;
            SourceBlend = sourceBlend;
            DestinationBlend = destinationBlend;
            DepthWrite = depthWrite;
        }

        public float SurfaceType { get; }
        public float SourceBlend { get; }
        public float DestinationBlend { get; }
        public float DepthWrite { get; }

        public void ApplyTo(Material material)
        {
            if (material == null || !material.HasProperty("_Surface"))
                return;
            material.SetFloat("_Surface", SurfaceType);
            material.SetFloat("_SrcBlend", SourceBlend);
            material.SetFloat("_DstBlend", DestinationBlend);
            material.SetFloat("_ZWrite", DepthWrite);
            material.renderQueue = (int)RenderQueue.Transparent;
        }
    }
}
