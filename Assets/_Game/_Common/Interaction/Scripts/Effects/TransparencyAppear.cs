using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Rendering;
using RenderHeads.Media.AVProVideo;

namespace Interaction
{
    /// <summary>
    /// 透明度出现效果：通过 CanvasGroup 或 Renderer 材质 alpha 从当前值过渡到目标透明度（目标为 0 时按时长淡出，目标为 1 时按时长淡入）。
    /// 支持 _Color 与 URP 常用 _BaseColor。
    /// </summary>
    public class TransparencyAppear : MonoBehaviour, IAppearEffect
    {
        [Tooltip("出现效果作用的内容。需带 CanvasGroup 或 Renderer 等以支持透明度。")]
        [SerializeField] private GameObject contentRoot;
        [SerializeField, Range(0f, 1f)] private float initialAlpha = 0f;
        [SerializeField, Range(0f, 1f)] private float targetAlpha = 1f;

        public GameObject ContentRoot => contentRoot;

        /// <summary>
        /// 由 InteractionModule 等从配置注入初始与目标透明度(0-1)。
        /// </summary>
        public void SetConfig(float initial, float target)
        {
            initialAlpha = Mathf.Clamp01(initial);
            targetAlpha = Mathf.Clamp01(target);
        }

        public void Appear(IContentHandle target, float duration, Action onComplete)
        {
            float localInitialAlpha = initialAlpha;
            float localTargetAlpha = targetAlpha;

            if (target?.Root == null)
            {
                Debug.Log("[TransparencyAppear] target.Root is null，直接完成");
                onComplete?.Invoke();
                return;
            }

            Debug.Log($"[TransparencyAppear] Appear start | root={target.Root.name} duration={duration} targetAlpha={localTargetAlpha}", target.Root);

            target.Root.SetActive(true);
            var allRenderers = target.Root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < allRenderers.Length; i++)
                allRenderers[i].forceRenderingOff = false;

            var canvasGroup = target.GetComponentForEffect<CanvasGroup>();
            if (canvasGroup != null)
            {
                Debug.Log("[TransparencyAppear] 使用 CanvasGroup 路径", canvasGroup);
                // 出现时从配置的初始透明度淡入至目标透明度
                canvasGroup.DOKill();
                if (duration <= 0f)
                {
                    canvasGroup.alpha = localInitialAlpha;
                    canvasGroup.alpha = localTargetAlpha;
                    onComplete?.Invoke();
                }
                else
                {
                    float tweenDuration = duration > 0f ? duration : 0.2f;
                    canvasGroup.alpha = localInitialAlpha;
                    DOTween
                        .To(() => canvasGroup.alpha, x => canvasGroup.alpha = x, localTargetAlpha, tweenDuration)
                        .SetTarget(canvasGroup)
                        .OnComplete(() =>
                        {
                            canvasGroup.alpha = localTargetAlpha;
                            onComplete?.Invoke();
                        });
                }
                return;
            }

            // 对视频内容优先寻找带 ApplyToMesh 的 Renderer（如 360 球体），再退回普通 Renderer
            Renderer renderer = null;
            if (target.Type == ContentType.Video)
            {
                var apply = target.GetComponentForEffect<ApplyToMesh>();
                if (apply != null)
                    renderer = apply.GetComponent<Renderer>();
            }
            if (renderer == null)
                renderer = target.GetComponentForEffect<Renderer>();
            if (renderer != null && renderer.sharedMaterial != null)
            {
                var mat = renderer.material;
                float tweenDuration = duration > 0f ? duration : 0.2f;

                // 优先支持 _Reveal（如 URP_InteractionTestReveal）：0=隐藏 1=显示，与透明度语义一致
                if (mat.HasProperty("_Reveal"))
                {
                    float revealInitial = localInitialAlpha <= 0f ? -0.001f : localInitialAlpha;
                    float revealTarget = localTargetAlpha <= 0f ? -0.001f : localTargetAlpha;
                    mat.SetFloat("_Reveal", revealInitial);
                    if (duration <= 0f)
                    {
                        mat.SetFloat("_Reveal", revealTarget);
                        onComplete?.Invoke();
                    }
                    else
                    {
                        DOTween.To(() => mat.GetFloat("_Reveal"), x => mat.SetFloat("_Reveal", x), revealTarget, tweenDuration)
                            .SetTarget(mat)
                            .OnComplete(() => onComplete?.Invoke());
                    }
                    return;
                }

                string prop = MaterialAlphaHelper.GetColorPropertyName(mat);
                if (prop != null)
                {
                    MaterialAlphaHelper.EnsureMaterialSupportsAlphaFade(mat);
                    if (duration <= 0f)
                    {
                        var c = MaterialAlphaHelper.GetColor(mat, prop);
                        c.a = localTargetAlpha;
                        MaterialAlphaHelper.SetColor(mat, prop, c);
                        onComplete?.Invoke();
                    }
                    else
                    {
                        var c = MaterialAlphaHelper.GetColor(mat, prop);
                        c.a = localInitialAlpha;
                        MaterialAlphaHelper.SetColor(mat, prop, c);
                        mat.DOFade(localTargetAlpha, prop, tweenDuration)
                           .SetTarget(mat)
                           .OnComplete(() =>
                           {
                               var finalColor = MaterialAlphaHelper.GetColor(mat, prop);
                               finalColor.a = localTargetAlpha;
                               MaterialAlphaHelper.SetColor(mat, prop, finalColor);
                               onComplete?.Invoke();
                           });
                    }
                    return;
                }
            }

            Debug.LogWarning($"[TransparencyAppear] 未在 {target.Root.name} 及其子节点找到 CanvasGroup 或带 _Color/_BaseColor 的 Renderer，无法执行透明度渐变。请确认内容根或其子物体上有 CanvasGroup 或使用支持透明度的材质。", target.Root);
            onComplete?.Invoke();
        }
    }

    /// <summary>
    /// 材质 alpha 辅助：统一按 _Color 或 _BaseColor 读写，兼容 Built-in 与 URP。
    /// </summary>
    public static class MaterialAlphaHelper
    {
        private const string PropColor = "_Color";
        private const string PropBaseColor = "_BaseColor";

        public static string GetColorPropertyName(Material mat)
        {
            if (mat == null) return null;

            // 对于 URP/Lit、URP/Unlit 等，实际使用的是 _BaseColor。
            // 一些旧 Shader 只用 _Color，因此优先尝试 _BaseColor，再回退到 _Color。
            if (mat.HasProperty(PropBaseColor)) return PropBaseColor;
            if (mat.HasProperty(PropColor)) return PropColor;
            return null;
        }

        public static Color GetColor(Material mat, string propertyName)
        {
            if (mat == null || string.IsNullOrEmpty(propertyName)) return Color.white;
            return mat.GetColor(propertyName);
        }

        public static void SetColor(Material mat, string propertyName, Color color)
        {
            if (mat == null || string.IsNullOrEmpty(propertyName)) return;
            mat.SetColor(propertyName, color);
        }

        /// <summary>
        /// 尽量将材质切到可透明混合状态，避免 Opaque 材质即使 alpha=0 仍可见。
        /// </summary>
        public static void EnsureMaterialSupportsAlphaFade(Material mat)
        {
            if (mat == null) return;

            // URP/HDRP 风格（Lit/Unlit 常见）
            if (mat.HasProperty("_Surface"))
            {
                mat.SetFloat("_Surface", 1f); // Transparent
                if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f); // Alpha
                if (mat.HasProperty("_BlendModePreserveSpecular")) mat.SetFloat("_BlendModePreserveSpecular", 0f);
                if (mat.HasProperty("_AlphaClip")) mat.SetFloat("_AlphaClip", 0f);
                if (mat.HasProperty("_Cutoff")) mat.SetFloat("_Cutoff", 0f);
                if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                if (mat.HasProperty("_SrcBlendAlpha")) mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                if (mat.HasProperty("_DstBlendAlpha")) mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = (int)RenderQueue.Transparent;
                return;
            }

            // Built-in Standard 风格
            if (mat.HasProperty("_Mode"))
            {
                mat.SetFloat("_Mode", 2f); // Fade
                if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.EnableKeyword("_ALPHABLEND_ON");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.renderQueue = (int)RenderQueue.Transparent;
            }
        }
    }
}
