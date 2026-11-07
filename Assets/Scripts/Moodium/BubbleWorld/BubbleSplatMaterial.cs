using UnityEngine;
using UnityEngine.Rendering;

namespace Moodium.NatureWorld
{
    /// <summary>Each world owns independent motion and fade parameters.</summary>
    public static class BubbleSplatMaterial
    {
        public const string ResourcePath = "MoodiumBubbleWorld/BubbleSplatMotion";
        public const string ShaderName = "Moodium/BubbleSplatMotion";

        /// <param name="motion">
        /// false (default, used by the bubble worlds): the URP/Unlit round-point material the user approved
        /// (0fbac1f-26bb73d). On visionOS, Unlit + alpha clip threshold 0 renders with alpha-to-coverage,
        /// which is what turns each textured quad into a soft round dot.
        /// true: the GPU-motion Shader Graph. It does not get alpha-to-coverage and its 4x4 texel blob is a
        /// square, so on device every point shows as a sharp square. Only enable it again after the graph
        /// computes a round mask from UV0 (see docs/verification/2026-10-30-visual-revert.md).
        /// </param>
        public static Material Create(Texture colors, float eyeHeight, bool candy, bool motion = false)
        {
            var source = motion ? Resources.Load<Material>(ResourcePath) : null;
            Material material;
            if (source != null && source.shader != null && source.shader.name == ShaderName)
            {
                material = Object.Instantiate(source);
                material.name = "Bubble World Points (GPU Motion)";
                material.SetTexture("_BaseMap", colors);
                material.SetColor("_BaseColor", Color.white);
                material.SetFloat("_EyeHeight", eyeHeight);
                material.SetFloat("_Style", candy ? 1f : 0f);
                material.SetFloat("_WindStrength", 0f);
                material.SetVector("_HandAmount", Vector4.zero);
                material.renderQueue = 3000;
            }
            else
            {
                if (motion) Debug.LogWarning("[Bubble Motion] Resources material missing or invalid; falling back to URP/Unlit.");
                material = BubbleWorldData.CreateUnlit("Bubble World Points", colors, true, CullMode.Off, Color.white);
                BubbleWorldData.ConfigureRoundPoints(material);
                BubbleWorldController.ConfigureTransparentPoints(material);
            }
            Debug.Log("[Bubble Motion] point shader = " + material.shader.name);
            return material;
        }
    }
}
