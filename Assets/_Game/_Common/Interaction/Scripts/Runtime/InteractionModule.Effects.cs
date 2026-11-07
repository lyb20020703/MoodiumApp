using System;
using UnityEngine;

namespace Interaction
{
    public partial class InteractionModule
    {
        private void ApplyAppearConfig(AppearEffectEntry entry, IAppearEffect appear)
        {
            switch (entry.effectType)
            {
                case AppearEffectType.Show:
                case AppearEffectType.Hide:
                    break;
                case AppearEffectType.Transparency:
                    if (appear is TransparencyAppear ta) ta.SetConfig(entry.appearInitialTransparency, entry.appearTransparencyTarget);
                    break;
                case AppearEffectType.Shader:
                    if (appear is ShaderAppear sa) sa.SetConfig(entry.shaderAppearPropertyName ?? "_Reveal", entry.shaderAppearFrom, entry.shaderAppearTo);
                    break;
                case AppearEffectType.Transform:
                    if (appear is TransformAppear txa) txa.SetConfig(
                        entry.transformAppearAnimatePosition,
                        entry.transformAppearAnimateRotation,
                        entry.transformAppearAnimateScale,
                        entry.transformAppearPositionTo,
                        entry.transformAppearRotationTo,
                        entry.transformAppearScaleTo,
                        entry.transformAppearEase);
                    break;
                case AppearEffectType.KeyframeAnimation:
                    if (appear is KeyframeAnimationAppear ka) ka.SetConfig(entry.keyframeAppearStateOrTrigger ?? "Appear");
                    break;
            }
        }

        private void ApplyDisappearConfig(DisappearEffectEntry entry, IDisappearEffect disappear)
        {
            switch (entry.effectType)
            {
                case DisappearEffectType.Hide:
                case DisappearEffectType.Show:
                    break;
                case DisappearEffectType.Transparency:
                    if (disappear is TransparencyDisappear td) td.SetConfig(entry.endTransparencyTarget);
                    break;
                case DisappearEffectType.Shader:
                    if (disappear is ShaderDisappear sd) sd.SetConfig(entry.shaderDisappearPropertyName ?? "_Reveal", entry.shaderDisappearFrom, entry.shaderDisappearTo);
                    break;
                case DisappearEffectType.Transform:
                    if (disappear is TransformDisappear txd) txd.SetConfig(entry.transformDisappearAnimatePosition, entry.transformDisappearAnimateRotation, entry.transformDisappearAnimateScale,
                        entry.transformDisappearPositionTo, entry.transformDisappearRotationTo, entry.transformDisappearScaleTo, entry.transformDisappearEase);
                    break;
                case DisappearEffectType.KeyframeAnimation:
                    if (disappear is KeyframeAnimationDisappear kd) kd.SetConfig(entry.keyframeDisappearStateOrTrigger ?? "Disappear");
                    break;
            }
        }

        private static void ValidateAppearEntryForContent(ContentType type, ref AppearEffectType appearType)
        {
            AppearEffectType[] allowed = GetAllowedAppearTypes(type);
            if (Array.IndexOf(allowed, appearType) < 0)
            {
                Debug.LogWarning($"开始效果 {appearType} 不适用于内容类型 {type}，已自动替换为 {allowed[0]}。");
                appearType = allowed[0];
            }
        }

        private static void ValidateDisappearEntryForContent(ContentType type, ref DisappearEffectType disappearType)
        {
            DisappearEffectType[] allowed = GetAllowedDisappearTypes(type);
            if (Array.IndexOf(allowed, disappearType) < 0)
            {
                Debug.LogWarning($"结束效果 {disappearType} 不适用于内容类型 {type}，已自动替换为 {allowed[0]}。");
                disappearType = allowed[0];
            }
        }

        public static AppearEffectType[] GetAllowedAppearTypes(ContentType type)
        {
            switch (type)
            {
                case ContentType.Video:
                    return new[] { AppearEffectType.Show, AppearEffectType.Transparency, AppearEffectType.Transform, AppearEffectType.KeyframeAnimation, AppearEffectType.Hide };
                case ContentType.Particle:
                case ContentType.VFX:
                    return new[] { AppearEffectType.Show, AppearEffectType.Hide };
                case ContentType.Model:
                default:
                    return new[] { AppearEffectType.Show, AppearEffectType.Transparency, AppearEffectType.Shader, AppearEffectType.Transform, AppearEffectType.KeyframeAnimation, AppearEffectType.Hide };
            }
        }

        public static DisappearEffectType[] GetAllowedDisappearTypes(ContentType type)
        {
            switch (type)
            {
                case ContentType.Video:
                    return new[] { DisappearEffectType.Hide, DisappearEffectType.Show, DisappearEffectType.Transparency, DisappearEffectType.Transform, DisappearEffectType.KeyframeAnimation };
                case ContentType.Particle:
                case ContentType.VFX:
                    return new[] { DisappearEffectType.Hide, DisappearEffectType.Show };
                case ContentType.Model:
                default:
                    return new[] { DisappearEffectType.Hide, DisappearEffectType.Show, DisappearEffectType.Transparency, DisappearEffectType.Shader, DisappearEffectType.Transform, DisappearEffectType.KeyframeAnimation };
            }
        }

        private IAppearEffect GetOrAddAppearEffect(AppearEffectType type)
        {
            switch (type)
            {
                case AppearEffectType.Show: return GetOrAddComponent<CommonShowEffect>();
                case AppearEffectType.Hide: return GetOrAddComponent<CommonAppearHideEffect>();
                case AppearEffectType.Transparency: return GetOrAddComponent<TransparencyAppear>();
                case AppearEffectType.Shader: return GetOrAddComponent<ShaderAppear>();
                case AppearEffectType.Transform: return GetOrAddComponent<TransformAppear>();
                case AppearEffectType.KeyframeAnimation: return GetOrAddComponent<KeyframeAnimationAppear>();
                default: return GetOrAddComponent<CommonShowEffect>();
            }
        }

        private IDisappearEffect GetOrAddDisappearEffect(DisappearEffectType type)
        {
            switch (type)
            {
                case DisappearEffectType.Hide: return GetOrAddComponent<CommonHideEffect>();
                case DisappearEffectType.Show: return GetOrAddComponent<CommonHideEffect>();
                case DisappearEffectType.Transparency: return GetOrAddComponent<TransparencyDisappear>();
                case DisappearEffectType.Shader: return GetOrAddComponent<ShaderDisappear>();
                case DisappearEffectType.Transform: return GetOrAddComponent<TransformDisappear>();
                case DisappearEffectType.KeyframeAnimation: return GetOrAddComponent<KeyframeAnimationDisappear>();
                default: return GetOrAddComponent<CommonHideEffect>();
            }
        }

        private static void SetContentAlpha(GameObject root, float alpha)
        {
            if (root == null) return;

            var canvasGroups = root.GetComponentsInChildren<CanvasGroup>(true);
            if (canvasGroups != null && canvasGroups.Length > 0)
            {
                for (int i = 0; i < canvasGroups.Length; i++)
                {
                    var canvasGroup = canvasGroups[i];
                    if (canvasGroup != null)
                        canvasGroup.alpha = alpha;
                }
            }

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                    continue;

                if (alpha > 0.0001f)
                    renderer.forceRenderingOff = false;

                var materials = renderer.materials;
                for (int j = 0; j < materials.Length; j++)
                {
                    var material = materials[j];
                    if (material == null)
                        continue;

                    if (material.HasProperty("_Reveal"))
                    {
                        material.SetFloat("_Reveal", alpha <= 0f ? -0.001f : alpha);
                        continue;
                    }

                    string prop = MaterialAlphaHelper.GetColorPropertyName(material);
                    if (prop != null)
                    {
                        MaterialAlphaHelper.EnsureMaterialSupportsAlphaFade(material);
                        var c = MaterialAlphaHelper.GetColor(material, prop);
                        c.a = alpha;
                        MaterialAlphaHelper.SetColor(material, prop, c);
                    }
                }
            }
        }
    }
}
