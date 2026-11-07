using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace VFXViewer
{
    [DefaultExecutionOrder(-9900)]
    public sealed class SpectatorHumanOcclusionGlobals : MonoBehaviour
    {
        private static readonly int HumanStencilPropertyId = Shader.PropertyToID("_HumanStencil");
        private static readonly int HumanDepthPropertyId = Shader.PropertyToID("_HumanDepth");
        private static readonly int DisplayTransformPropertyId = Shader.PropertyToID("_UnityDisplayTransform");
        private static readonly int HumanOcclusionEnabledPropertyId = Shader.PropertyToID("_SpectatorHumanOcclusionEnabled");

        private ARCameraManager m_CameraManager;
        private AROcclusionManager m_OcclusionManager;

        private void OnEnable()
        {
            if (!TryResolveManagers())
            {
                enabled = false;
                return;
            }

            m_CameraManager.frameReceived += OnCameraFrameReceived;
            m_OcclusionManager.frameReceived += OnOcclusionFrameReceived;
        }

        private void OnDisable()
        {
            if (m_CameraManager != null)
                m_CameraManager.frameReceived -= OnCameraFrameReceived;

            if (m_OcclusionManager != null)
                m_OcclusionManager.frameReceived -= OnOcclusionFrameReceived;

            Shader.SetGlobalFloat(HumanOcclusionEnabledPropertyId, 0f);
        }

        private bool TryResolveManagers()
        {
            if (m_CameraManager == null)
                m_CameraManager = GetComponent<ARCameraManager>();

            if (m_OcclusionManager == null)
                m_OcclusionManager = GetComponent<AROcclusionManager>();

            if (m_CameraManager != null && m_OcclusionManager != null)
                return true;

            Debug.LogWarning(
                "[SpectatorOcclusion] SpectatorHumanOcclusionGlobals requires both ARCameraManager and AROcclusionManager on the same camera.",
                this);
            return false;
        }

        private static void OnCameraFrameReceived(ARCameraFrameEventArgs eventArgs)
        {
            if (eventArgs.displayMatrix.HasValue)
                Shader.SetGlobalMatrix(DisplayTransformPropertyId, eventArgs.displayMatrix.Value);
        }

        private void OnOcclusionFrameReceived(AROcclusionFrameEventArgs eventArgs)
        {
            bool hasHumanStencil = false;
            bool hasHumanDepth = false;

            foreach (ARExternalTexture externalTexture in eventArgs.externalTextures)
            {
                if (externalTexture.texture == null)
                    continue;

                Shader.SetGlobalTexture(externalTexture.propertyId, externalTexture.texture);

                if (externalTexture.propertyId == HumanStencilPropertyId)
                    hasHumanStencil = true;
                else if (externalTexture.propertyId == HumanDepthPropertyId)
                    hasHumanDepth = true;
            }

            bool humanOcclusionEnabled =
                hasHumanStencil &&
                hasHumanDepth &&
                m_OcclusionManager != null &&
                m_OcclusionManager.currentHumanStencilMode.Enabled() &&
                m_OcclusionManager.currentHumanDepthMode.Enabled();

            Shader.SetGlobalFloat(HumanOcclusionEnabledPropertyId, humanOcclusionEnabled ? 1f : 0f);
        }
    }
}
