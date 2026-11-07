using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VFXViewer
{
    public sealed class SpectatorPeopleOcclusionRendererAdapter : MonoBehaviour
    {
        private const string LitReplacementShaderName = "Occlusion/OcclusionLitFast";
        private const string EffectsReplacementShaderName = "Occlusion/OcclusionUnlitTransparent";
        private const string IosHumanOcclusionOpaqueShaderResourcePath = "Shaders/SpectatorHumanOcclusionOpaque";
        private const string IosHumanOcclusionOpaqueShaderName = "VFXViewer/SpectatorHumanOcclusionOpaque";
        private const string IosHumanOcclusionTransparentShaderResourcePath = "Shaders/SpectatorHumanOcclusionTransparent";
        private const string IosHumanOcclusionTransparentShaderName = "VFXViewer/SpectatorHumanOcclusionTransparent";

        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int CutoffId = Shader.PropertyToID("_Cutoff");
        private static readonly int AlphaClipId = Shader.PropertyToID("_AlphaClip");
        private static readonly int SurfaceId = Shader.PropertyToID("_Surface");
        private static readonly int BlendId = Shader.PropertyToID("_Blend");
        private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
        private static readonly int SrcBlendAlphaId = Shader.PropertyToID("_SrcBlendAlpha");
        private static readonly int DstBlendAlphaId = Shader.PropertyToID("_DstBlendAlpha");
        private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");
        private static readonly int CullId = Shader.PropertyToID("_Cull");
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
        private static readonly int SpecColorId = Shader.PropertyToID("_SpecColor");
        private static readonly int SpecGlossMapId = Shader.PropertyToID("_SpecGlossMap");
        private static readonly int BumpMapId = Shader.PropertyToID("_BumpMap");
        private static readonly int BumpScaleId = Shader.PropertyToID("_BumpScale");
        private static readonly int EmissionMapId = Shader.PropertyToID("_EmissionMap");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly int UseVertexColorId = Shader.PropertyToID("_UseVertexColor");
        private static Shader s_IosHumanOcclusionOpaqueShader;
        private static Shader s_IosHumanOcclusionTransparentShader;

        private readonly List<Material> m_RuntimeMaterials = new List<Material>();
        private static bool s_HasLoggedEditorBypass;
        private static bool s_HasLoggedIosDirectPath;
        private bool m_Applied;

        private void OnEnable()
        {
            TryApply();
        }

        public void ApplyNow(string reason = null)
        {
            if (!string.IsNullOrWhiteSpace(reason))
                Debug.Log($"[SpectatorOcclusion] Forcing Root marker adaptation. reason={reason}", this);

            TryApply(force: true);
        }

        private void OnDestroy()
        {
            for (int i = 0; i < m_RuntimeMaterials.Count; i++)
            {
                Material material = m_RuntimeMaterials[i];
                if (material != null)
                    Destroy(material);
            }

            m_RuntimeMaterials.Clear();
        }

        private void TryApply(bool force = false)
        {
            if (!force && m_Applied)
                return;

            if (Application.isEditor)
            {
                if (!s_HasLoggedEditorBypass)
                {
                    s_HasLoggedEditorBypass = true;
                    Debug.Log(
                        "[SpectatorOcclusion] Skipping Root marker material adaptation in the editor. " +
                        "The direct iOS human-occlusion shader path only runs on-device.");
                }

                m_Applied = true;
                return;
            }

            bool useIosDirectHumanOcclusion = PlatformRuntime.IsIOSButNotVisionOS;
            Shader litShader = useIosDirectHumanOcclusion ? ResolveIosHumanOcclusionOpaqueShader() : Shader.Find(LitReplacementShaderName);
            Shader effectsShader = useIosDirectHumanOcclusion ? ResolveIosHumanOcclusionTransparentShader() : Shader.Find(EffectsReplacementShaderName);
            if (litShader == null && effectsShader == null)
            {
                Debug.LogWarning("[SpectatorOcclusion] Root marker occlusion adaptation skipped because replacement shaders were not found.");
                return;
            }

            if (useIosDirectHumanOcclusion && !s_HasLoggedIosDirectPath)
            {
                s_HasLoggedIosDirectPath = true;
                Debug.Log(
                    "[SpectatorOcclusion] Root marker on iOS spectator will use the direct human-occlusion shader path.");
            }

            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            Debug.Log($"[SpectatorOcclusion] Root marker '{name}' renderer scan started. rendererCount={renderers.Length}", this);
            int adaptedRendererCount = 0;

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (!TryGetReplacementShader(renderer, litShader, effectsShader, out Shader replacementShader))
                    continue;

                Material[] sharedMaterials = GetRendererMaterials(renderer);
                if (sharedMaterials.Length == 0)
                    continue;

                bool changed = false;
                for (int j = 0; j < sharedMaterials.Length; j++)
                {
                    Material sourceMaterial = sharedMaterials[j];
                    Material adaptedMaterial = CreateAdaptedMaterial(sourceMaterial, replacementShader);
                    if (adaptedMaterial == null || ReferenceEquals(sourceMaterial, adaptedMaterial))
                        continue;

                    sharedMaterials[j] = adaptedMaterial;
                    changed = true;
                }

                if (!changed)
                    continue;

                renderer.sharedMaterials = sharedMaterials;
                adaptedRendererCount++;
            }

            m_Applied = true;

            if (adaptedRendererCount > 0)
            {
                Debug.Log($"[SpectatorOcclusion] Root marker '{name}' switched to people-occlusion materials. renderers={adaptedRendererCount}");
            }
            else
            {
                Debug.LogWarning($"[SpectatorOcclusion] Root marker '{name}' did not adapt any renderer. Check renderer types/materials on the root prefab.", this);
            }
        }

        private Material CreateAdaptedMaterial(Material sourceMaterial, Shader replacementShader)
        {
            if (sourceMaterial == null || replacementShader == null)
                return sourceMaterial;

            Shader sourceShader = sourceMaterial.shader;
            if (ReferenceEquals(sourceShader, replacementShader) ||
                string.Equals(sourceShader != null ? sourceShader.name : string.Empty, replacementShader.name, StringComparison.Ordinal))
            {
                return sourceMaterial;
            }

            Material adaptedMaterial = new Material(replacementShader)
            {
                name = $"{sourceMaterial.name} [Root People Occlusion]"
            };

            CopyCommonSurfaceProperties(
                sourceMaterial,
                adaptedMaterial,
                IsEffectsShader(replacementShader) || IsIosHumanOcclusionTransparentShader(replacementShader),
                forceTransparentBlend: !IsIosHumanOcclusionOpaqueShader(replacementShader));
            m_RuntimeMaterials.Add(adaptedMaterial);
            return adaptedMaterial;
        }

        private static bool TryGetReplacementShader(Renderer renderer, Shader litShader, Shader effectsShader, out Shader replacementShader)
        {
            replacementShader = null;
            if (renderer == null)
                return false;

            if (renderer is MeshRenderer || renderer is SkinnedMeshRenderer)
            {
                replacementShader = litShader;
                return replacementShader != null;
            }

            string rendererType = renderer.GetType().Name;
            if (renderer is ParticleSystemRenderer ||
                renderer is TrailRenderer ||
                renderer is LineRenderer ||
                string.Equals(rendererType, nameof(SpriteRenderer), StringComparison.Ordinal) ||
                string.Equals(rendererType, "VFXRenderer", StringComparison.Ordinal))
            {
                replacementShader = effectsShader;
                return replacementShader != null;
            }

            return false;
        }

        private static Material[] GetRendererMaterials(Renderer renderer)
        {
            if (renderer == null)
                return Array.Empty<Material>();

            Material[] materials = renderer.sharedMaterials;
            if (materials != null && materials.Length > 0)
                return materials;

            Material sharedMaterial = renderer.sharedMaterial;
            return sharedMaterial != null ? new[] { sharedMaterial } : Array.Empty<Material>();
        }

        private static bool IsEffectsShader(Shader shader)
        {
            return shader != null && string.Equals(shader.name, EffectsReplacementShaderName, StringComparison.Ordinal);
        }

        private static bool IsIosHumanOcclusionOpaqueShader(Shader shader)
        {
            return shader != null && string.Equals(shader.name, IosHumanOcclusionOpaqueShaderName, StringComparison.Ordinal);
        }

        private static bool IsIosHumanOcclusionTransparentShader(Shader shader)
        {
            return shader != null && string.Equals(shader.name, IosHumanOcclusionTransparentShaderName, StringComparison.Ordinal);
        }

        private static Shader ResolveIosHumanOcclusionOpaqueShader()
        {
            if (s_IosHumanOcclusionOpaqueShader != null)
                return s_IosHumanOcclusionOpaqueShader;

            s_IosHumanOcclusionOpaqueShader = Resources.Load<Shader>(IosHumanOcclusionOpaqueShaderResourcePath);
            if (s_IosHumanOcclusionOpaqueShader == null)
                s_IosHumanOcclusionOpaqueShader = Shader.Find(IosHumanOcclusionOpaqueShaderName);

            return s_IosHumanOcclusionOpaqueShader;
        }

        private static Shader ResolveIosHumanOcclusionTransparentShader()
        {
            if (s_IosHumanOcclusionTransparentShader != null)
                return s_IosHumanOcclusionTransparentShader;

            s_IosHumanOcclusionTransparentShader = Resources.Load<Shader>(IosHumanOcclusionTransparentShaderResourcePath);
            if (s_IosHumanOcclusionTransparentShader == null)
                s_IosHumanOcclusionTransparentShader = Shader.Find(IosHumanOcclusionTransparentShaderName);

            return s_IosHumanOcclusionTransparentShader;
        }

        private static void CopyCommonSurfaceProperties(Material source, Material target, bool useVertexColor, bool forceTransparentBlend)
        {
            if (source == null || target == null)
                return;

            CopyTextureWithScaleAndOffset(
                source,
                target,
                BaseMapId,
                "_BaseMap",
                "_MainTex",
                "_BaseColorMap",
                "_UnlitColorMap",
                "_ColorMap",
                "_BaseColorTexture");

            Color baseColor = TryGetColor(source, "_BaseColor", out Color sourceBaseColor)
                ? sourceBaseColor
                : TryGetColor(source, "_Color", out Color sourceColor)
                    ? sourceColor
                    : TryGetColor(source, "_TintColor", out Color tintColor)
                        ? tintColor
                        : TryGetColor(source, "_UnlitColor", out Color unlitColor)
                            ? unlitColor
                            : source.color;

            if (target.HasProperty(BaseColorId))
                target.SetColor(BaseColorId, baseColor);

            if (target.HasProperty(UseVertexColorId))
                target.SetFloat(UseVertexColorId, useVertexColor ? 1f : 0f);

            if (target.HasProperty(SmoothnessId) && TryGetFloat(source, "_Smoothness", out float smoothness))
                target.SetFloat(SmoothnessId, smoothness);

            if (target.HasProperty(SpecColorId) && TryGetColor(source, "_SpecColor", out Color specColor))
                target.SetColor(SpecColorId, specColor);

            if (CopyTexture(source, target, SpecGlossMapId, "_SpecGlossMap"))
                target.EnableKeyword("_SPECGLOSSMAP");

            if (CopyTexture(source, target, BumpMapId, "_BumpMap"))
            {
                target.EnableKeyword("_NORMALMAP");
                if (target.HasProperty(BumpScaleId) && TryGetFloat(source, "_BumpScale", out float bumpScale))
                    target.SetFloat(BumpScaleId, bumpScale);
            }

            bool hasEmission = false;
            if (CopyTexture(source, target, EmissionMapId, "_EmissionMap"))
                hasEmission = true;

            if (target.HasProperty(EmissionColorId) && TryGetColor(source, "_EmissionColor", out Color emissionColor))
            {
                target.SetColor(EmissionColorId, emissionColor);
                if (emissionColor.maxColorComponent > 0.001f)
                    hasEmission = true;
            }

            if (hasEmission)
                target.EnableKeyword("_EMISSION");

            bool alphaClipEnabled = false;
            if (target.HasProperty(CutoffId) && TryGetFloat(source, "_Cutoff", out float cutoff))
            {
                target.SetFloat(CutoffId, cutoff);
                alphaClipEnabled = cutoff > 0.001f;
            }

            if (TryGetFloat(source, "_AlphaClip", out float alphaClip))
                alphaClipEnabled |= alphaClip > 0.5f;

            if (source.IsKeywordEnabled("_ALPHATEST_ON"))
                alphaClipEnabled = true;

            if (target.HasProperty(AlphaClipId))
                target.SetFloat(AlphaClipId, alphaClipEnabled ? 1f : 0f);
            if (alphaClipEnabled && target.HasProperty(AlphaClipId))
                target.EnableKeyword("_ALPHATEST_ON");

            if (target.HasProperty(CullId) && TryGetFloat(source, "_Cull", out float cullMode))
                target.SetFloat(CullId, cullMode);

            ConfigureBlendMode(source, target, baseColor.a, alphaClipEnabled, forceTransparentBlend);
        }

        private static void ConfigureBlendMode(Material source, Material target, float alpha, bool alphaClipEnabled, bool forceTransparentBlend)
        {
            bool hasExplicitSrcBlend = TryGetFloat(source, "_SrcBlend", out float srcBlend);
            bool hasExplicitDstBlend = TryGetFloat(source, "_DstBlend", out float dstBlend);
            bool hasExplicitSrcBlendAlpha = TryGetFloat(source, "_SrcBlendAlpha", out float srcBlendAlpha);
            bool hasExplicitDstBlendAlpha = TryGetFloat(source, "_DstBlendAlpha", out float dstBlendAlpha);
            bool hasExplicitSurface = TryGetFloat(source, "_Surface", out float sourceSurface);
            bool hasExplicitBlendMode = TryGetFloat(source, "_Blend", out float sourceBlendMode);

            if (forceTransparentBlend)
            {
                // ARFoundation's generic occlusion shaders attenuate visibility through output alpha.
                // To let that alpha actually hide spectator content behind people, always render
                // the adapted material as transparent instead of preserving the source opaque state.
                if (target.HasProperty(SurfaceId))
                    target.SetFloat(SurfaceId, hasExplicitSurface && sourceSurface > 0.5f ? sourceSurface : 1f);
                if (target.HasProperty(BlendId))
                    target.SetFloat(BlendId, hasExplicitBlendMode ? sourceBlendMode : 0f);
                if (target.HasProperty(SrcBlendId))
                    target.SetFloat(SrcBlendId, hasExplicitSrcBlend ? srcBlend : (float)BlendMode.SrcAlpha);
                if (target.HasProperty(DstBlendId))
                    target.SetFloat(DstBlendId, hasExplicitDstBlend ? dstBlend : (float)BlendMode.OneMinusSrcAlpha);
                if (target.HasProperty(SrcBlendAlphaId))
                    target.SetFloat(SrcBlendAlphaId, hasExplicitSrcBlendAlpha ? srcBlendAlpha : (float)BlendMode.One);
                if (target.HasProperty(DstBlendAlphaId))
                    target.SetFloat(DstBlendAlphaId, hasExplicitDstBlendAlpha ? dstBlendAlpha : (float)BlendMode.OneMinusSrcAlpha);
                if (target.HasProperty(ZWriteId))
                    target.SetFloat(ZWriteId, 0f);
                target.renderQueue = source.renderQueue >= (int)RenderQueue.Transparent
                    ? source.renderQueue
                    : (int)RenderQueue.Transparent;
                target.SetOverrideTag("RenderType", "Transparent");
                return;
            }

            if (target.HasProperty(SurfaceId))
                target.SetFloat(SurfaceId, hasExplicitSurface ? sourceSurface : 0f);
            if (target.HasProperty(BlendId))
                target.SetFloat(BlendId, hasExplicitBlendMode ? sourceBlendMode : 0f);
            if (target.HasProperty(SrcBlendId))
                target.SetFloat(SrcBlendId, hasExplicitSrcBlend ? srcBlend : (float)BlendMode.One);
            if (target.HasProperty(DstBlendId))
                target.SetFloat(DstBlendId, hasExplicitDstBlend ? dstBlend : (float)BlendMode.Zero);
            if (target.HasProperty(SrcBlendAlphaId))
                target.SetFloat(SrcBlendAlphaId, hasExplicitSrcBlendAlpha ? srcBlendAlpha : (float)BlendMode.One);
            if (target.HasProperty(DstBlendAlphaId))
                target.SetFloat(DstBlendAlphaId, hasExplicitDstBlendAlpha ? dstBlendAlpha : (float)BlendMode.Zero);
            if (target.HasProperty(ZWriteId))
            {
                if (TryGetFloat(source, "_ZWrite", out float sourceZWrite))
                    target.SetFloat(ZWriteId, sourceZWrite);
                else
                    target.SetFloat(ZWriteId, 1f);
            }

            target.renderQueue = source.renderQueue >= 0
                ? source.renderQueue
                : alphaClipEnabled
                    ? (int)RenderQueue.AlphaTest
                    : (int)RenderQueue.Geometry;
            target.SetOverrideTag("RenderType", alphaClipEnabled ? "TransparentCutout" : "Opaque");
        }

        private static void CopyTextureWithScaleAndOffset(Material source, Material target, int targetPropertyId, params string[] sourcePropertyNames)
        {
            if (source == null || target == null || sourcePropertyNames == null || !target.HasProperty(targetPropertyId))
                return;

            for (int i = 0; i < sourcePropertyNames.Length; i++)
            {
                string propertyName = sourcePropertyNames[i];
                if (string.IsNullOrEmpty(propertyName) || !source.HasProperty(propertyName))
                    continue;

                Texture texture = source.GetTexture(propertyName);
                if (texture == null)
                    continue;

                target.SetTexture(targetPropertyId, texture);
                target.SetTextureScale(BaseMapId, source.GetTextureScale(propertyName));
                target.SetTextureOffset(BaseMapId, source.GetTextureOffset(propertyName));
                return;
            }
        }

        private static bool CopyTexture(Material source, Material target, int targetPropertyId, string sourcePropertyName)
        {
            if (source == null ||
                target == null ||
                !target.HasProperty(targetPropertyId) ||
                string.IsNullOrEmpty(sourcePropertyName) ||
                !source.HasProperty(sourcePropertyName))
            {
                return false;
            }

            Texture texture = source.GetTexture(sourcePropertyName);
            if (texture == null)
                return false;

            target.SetTexture(targetPropertyId, texture);
            return true;
        }

        private static bool TryGetFloat(Material material, string propertyName, out float value)
        {
            if (material != null && !string.IsNullOrEmpty(propertyName) && material.HasProperty(propertyName))
            {
                value = material.GetFloat(propertyName);
                return true;
            }

            value = 0f;
            return false;
        }

        private static bool TryGetColor(Material material, string propertyName, out Color value)
        {
            if (material != null && !string.IsNullOrEmpty(propertyName) && material.HasProperty(propertyName))
            {
                value = material.GetColor(propertyName);
                return true;
            }

            value = default;
            return false;
        }
    }
}
