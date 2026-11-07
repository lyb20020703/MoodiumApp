using System;
using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// Shader 消失效果：通过材质浮点参数从 0 过渡到 1 实现「消失」。
    /// </summary>
    public class ShaderDisappear : MonoBehaviour, IDisappearEffect
    {
        [Tooltip("结束效果作用的内容（含 Renderer 的节点）。")]
        [SerializeField] private GameObject contentRoot;
        [Tooltip("控制的 Shader 参数名（如 _Reveal、_Cutoff）")]
        [SerializeField] private string propertyName = "_Reveal";

        [Tooltip("消失时参数从 from 插值到 to")]
        [SerializeField] private float from = 0f;
        [SerializeField] private float to = 1f;

        public GameObject ContentRoot => contentRoot;

        /// <summary> 由 InteractionModule 等从配置注入参数。 </summary>
        public void SetConfig(string propName, float fromVal, float toVal)
        {
            propertyName = propName ?? "_Reveal";
            from = fromVal;
            to = toVal;
        }

        public void Disappear(IContentHandle target, float duration, Action onComplete)
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

            // 将协程挂在当前可见内容上，保证反向揭示完成后再由流程隐藏内容。
            var runner = target.Root.GetComponent<ShaderTweenRunner>();
            if (runner == null) runner = target.Root.AddComponent<ShaderTweenRunner>();
            runner.RunFloat(renderers, prop, startValue, endValue, duration, onComplete);
        }
    }
}
