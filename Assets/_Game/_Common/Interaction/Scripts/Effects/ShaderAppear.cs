using System;
using System.Collections.Generic;
using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// Shader 出现效果：通过材质浮点参数（如 _Reveal、_Cutoff）从 1 过渡到 0 实现「显现」。
    /// 需在 Shader 中定义对应参数（1=完全隐藏，0=完全显示）。
    /// </summary>
    public class ShaderAppear : MonoBehaviour, IAppearEffect
    {
        [Tooltip("出现效果作用的内容（含 Renderer 的节点）。")]
        [SerializeField] private GameObject contentRoot;
        [Tooltip("控制的 Shader 参数名（如 _Reveal、_Cutoff）")]
        [SerializeField] private string propertyName = "_Reveal";

        [Tooltip("出现时参数从 from 插值到 to")]
        [SerializeField] private float from = 1f;
        [SerializeField] private float to = 0f;

        public GameObject ContentRoot => contentRoot;

        /// <summary> 由 InteractionModule 等从配置注入参数。 </summary>
        public void SetConfig(string propName, float fromVal, float toVal)
        {
            propertyName = propName ?? "_Reveal";
            from = fromVal;
            to = toVal;
        }

        public void Appear(IContentHandle target, float duration, Action onComplete)
        {
            string prop = propertyName;
            float startValue = from;
            float endValue = to;

            if (target?.Root == null || string.IsNullOrEmpty(prop))
            {
                onComplete?.Invoke();
                return;
            }

            var renderers = target.Root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                onComplete?.Invoke();
                return;
            }

            // 模块根节点可能会在延迟效果启动前被停用；将协程挂在已经显示的内容节点上。
            var runner = target.Root.GetComponent<ShaderTweenRunner>();
            if (runner == null) runner = target.Root.AddComponent<ShaderTweenRunner>();
            runner.RunFloat(renderers, prop, startValue, endValue, duration, onComplete);
        }
    }

    internal class ShaderTweenRunner : MonoBehaviour
    {
        public void RunFloat(Renderer[] renderers, string prop, float from, float to, float duration, Action onComplete)
        {
            enabled = true;

            var materials = CollectMaterials(renderers, prop);
            if (materials.Count == 0)
            {
                onComplete?.Invoke();
                return;
            }

            if (duration <= 0f)
            {
                ApplyValue(materials, prop, to);
                onComplete?.Invoke();
                return;
            }

            StartCoroutine(TweenRoutine(materials, prop, from, to, duration, onComplete));
        }

        public void Cancel()
        {
            StopAllCoroutines();
            enabled = false;
        }

        private static List<Material> CollectMaterials(Renderer[] renderers, string prop)
        {
            var materials = new List<Material>();
            var seen = new HashSet<int>();

            if (renderers == null || string.IsNullOrEmpty(prop))
                return materials;

            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                    continue;

                var rendererMaterials = renderer.materials;
                for (int j = 0; j < rendererMaterials.Length; j++)
                {
                    var material = rendererMaterials[j];
                    if (material == null || !material.HasProperty(prop))
                        continue;

                    if (seen.Add(material.GetInstanceID()))
                        materials.Add(material);
                }
            }

            return materials;
        }

        private static void ApplyValue(IReadOnlyList<Material> materials, string prop, float value)
        {
            for (int i = 0; i < materials.Count; i++)
            {
                var material = materials[i];
                if (material != null)
                    material.SetFloat(prop, value);
            }
        }

        private System.Collections.IEnumerator TweenRoutine(IReadOnlyList<Material> materials, string prop, float from, float to, float duration, Action onComplete)
        {
            float elapsed = 0f;
            ApplyValue(materials, prop, from);

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float value = Mathf.Lerp(from, to, t);
                ApplyValue(materials, prop, value);
                yield return null;
            }

            ApplyValue(materials, prop, to);
            onComplete?.Invoke();
        }
    }
}
