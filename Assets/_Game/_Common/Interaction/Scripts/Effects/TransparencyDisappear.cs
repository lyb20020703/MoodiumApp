using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// 透明度结束效果：通过 CanvasGroup 或 Renderer 材质 alpha 从当前值过渡到目标透明度。
    /// </summary>
    public class TransparencyDisappear : MonoBehaviour, IDisappearEffect
    {
        [Tooltip("结束效果作用的内容。需带 CanvasGroup 或 Renderer 等以支持透明度。")]
        [SerializeField] private GameObject contentRoot;
        [SerializeField, Range(0f, 1f)] private float targetAlpha = 0f;

        public GameObject ContentRoot => contentRoot;

        /// <summary> 由 InteractionModule 等从配置注入目标透明度(0-1)。 </summary>
        public void SetConfig(float target) { targetAlpha = Mathf.Clamp01(target); }

        public void Disappear(IContentHandle target, float duration, Action onComplete)
        {
            float localTargetAlpha = targetAlpha;

            if (target?.Root == null)
            {
                Debug.Log("[TransparencyDisappear] target.Root is null，直接完成");
                onComplete?.Invoke();
                return;
            }

            var canvasGroups = target.Root.GetComponentsInChildren<CanvasGroup>(true);
            if (canvasGroups != null && canvasGroups.Length > 0)
            {
                Debug.Log("[TransparencyDisappear] 使用 CanvasGroup 路径", target.Root);
                for (int i = 0; i < canvasGroups.Length; i++)
                    canvasGroups[i].DOKill();

                if (duration <= 0f)
                {
                    for (int i = 0; i < canvasGroups.Length; i++)
                        canvasGroups[i].alpha = localTargetAlpha;
                    onComplete?.Invoke();
                }
                else
                {
                    var seq = DOTween.Sequence().SetTarget(target.Root);
                    for (int i = 0; i < canvasGroups.Length; i++)
                    {
                        var canvasGroup = canvasGroups[i];
                        seq.Join(
                            DOTween
                                .To(() => canvasGroup.alpha, x => canvasGroup.alpha = x, localTargetAlpha, duration)
                                .SetTarget(canvasGroup));
                    }
                    seq.OnComplete(() =>
                    {
                        for (int i = 0; i < canvasGroups.Length; i++)
                            canvasGroups[i].alpha = localTargetAlpha;
                        onComplete?.Invoke();
                    });
                }
                return;
            }

            var materials = new List<(Material mat, string prop)>();
            var revealMats = new List<Material>();
            var renderers = target.Root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null || r.sharedMaterials == null || r.sharedMaterials.Length == 0) continue;
                var mats = r.materials;
                for (int j = 0; j < mats.Length; j++)
                {
                    var mat = mats[j];
                    if (mat == null) continue;
                    if (mat.HasProperty("_Reveal"))
                    {
                        if (!revealMats.Contains(mat)) revealMats.Add(mat);
                        continue;
                    }
                    string prop = MaterialAlphaHelper.GetColorPropertyName(mat);
                    if (prop != null) materials.Add((mat, prop));
                }
            }

            if (revealMats.Count == 0 && materials.Count == 0)
            {
                Debug.LogWarning($"[TransparencyDisappear] 未在 {target.Root.name} 及其子节点找到 CanvasGroup 或带 _Color/_BaseColor/_Reveal 的 Renderer，无法执行透明度渐变。", target.Root);
                onComplete?.Invoke();
                return;
            }

            if (duration <= 0f)
            {
                float revealTarget = localTargetAlpha <= 0f ? -0.001f : localTargetAlpha;
                for (int i = 0; i < revealMats.Count; i++)
                    revealMats[i].SetFloat("_Reveal", revealTarget);
                for (int i = 0; i < materials.Count; i++)
                {
                    var (mat, prop) = materials[i];
                    MaterialAlphaHelper.EnsureMaterialSupportsAlphaFade(mat);
                    var c = MaterialAlphaHelper.GetColor(mat, prop);
                    c.a = localTargetAlpha;
                    MaterialAlphaHelper.SetColor(mat, prop, c);
                }
                if (localTargetAlpha <= 0.0001f)
                {
                    for (int i = 0; i < renderers.Length; i++)
                        renderers[i].forceRenderingOff = true;
                }
                onComplete?.Invoke();
                return;
            }

            var sequence = DOTween.Sequence();
            float revealTweenTarget = localTargetAlpha <= 0f ? -0.001f : localTargetAlpha;
            for (int i = 0; i < revealMats.Count; i++)
            {
                var mat = revealMats[i];
                sequence.Join(
                    DOTween
                        .To(() => mat.GetFloat("_Reveal"), x => mat.SetFloat("_Reveal", x), revealTweenTarget, duration)
                        .SetTarget(mat));
            }
            for (int i = 0; i < materials.Count; i++)
            {
                var (mat, prop) = materials[i];
                MaterialAlphaHelper.EnsureMaterialSupportsAlphaFade(mat);
                sequence.Join(mat.DOFade(localTargetAlpha, prop, duration).SetTarget(mat));
            }
            sequence.OnComplete(() =>
            {
                for (int i = 0; i < revealMats.Count; i++)
                    revealMats[i].SetFloat("_Reveal", revealTweenTarget);
                for (int i = 0; i < materials.Count; i++)
                {
                    var (mat, prop) = materials[i];
                    var finalColor = MaterialAlphaHelper.GetColor(mat, prop);
                    finalColor.a = localTargetAlpha;
                    MaterialAlphaHelper.SetColor(mat, prop, finalColor);
                }
                if (localTargetAlpha <= 0.0001f)
                {
                    for (int i = 0; i < renderers.Length; i++)
                        renderers[i].forceRenderingOff = true;
                }
                onComplete?.Invoke();
            });
        }
    }
}
