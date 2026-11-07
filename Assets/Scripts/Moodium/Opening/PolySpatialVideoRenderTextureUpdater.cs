using UnityEngine;

namespace Moodium.Opening
{
    /// <summary>
    /// Keeps a VideoPlayer RenderTexture synchronized with the PolySpatial host.
    /// PolySpatial does not automatically detect VideoPlayer texture updates.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PolySpatialVideoRenderTextureUpdater : MonoBehaviour
    {
        RenderTexture m_TargetTexture;

        public RenderTexture TargetTexture => m_TargetTexture;

        public void Configure(RenderTexture targetTexture)
        {
            m_TargetTexture = targetTexture;
        }

        public static bool ShouldTransfer(RenderTexture targetTexture)
        {
            return Application.isFocused && targetTexture != null && targetTexture.IsCreated();
        }

        void LateUpdate()
        {
            if (!ShouldTransfer(m_TargetTexture))
                return;

            Unity.PolySpatial.PolySpatialObjectUtils.MarkDirty(m_TargetTexture);
        }

        // No OnDisable reset: OpeningVideoSession.SetVisible(false) deactivates this object (world entry, the
        // no-bag carousel), and the texture was then never set again, so after SetVisible(true) Vision Pro kept
        // the last frame while the video ran on. A destroyed texture already fails ShouldTransfer.
    }
}
