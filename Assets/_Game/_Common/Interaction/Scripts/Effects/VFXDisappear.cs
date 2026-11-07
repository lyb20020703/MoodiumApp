using System;
using UnityEngine;
using UnityEngine.VFX;

namespace Interaction
{
    /// <summary>
    /// VFX Graph 消失效果：Stop VisualEffect。
    /// </summary>
    public class VFXDisappear : MonoBehaviour, IDisappearEffect
    {
        [Tooltip("结束效果作用的内容（含 VisualEffect 的节点）。")]
        [SerializeField] private GameObject contentRoot;

        public GameObject ContentRoot => contentRoot;

        public void Disappear(IContentHandle target, float duration, Action onComplete)
        {
            if (target?.Root == null)
            {
                onComplete?.Invoke();
                return;
            }

            if (VFXRuntimeGuard.DisableUnsupportedVFX(target.Root, this))
            {
                onComplete?.Invoke();
                return;
            }

            var controller = target.Root.GetComponentInChildren<IVFXController>(true);
            if (controller != null)
            {
                controller.Disappear(duration, onComplete);
                return;
            }

            var vfx = target.Root.GetComponentInChildren<VisualEffect>(true);
            if (vfx != null) vfx.Stop();
            if (duration > 0f && target?.Root != null)
            {
                var runner = target.Root.GetComponent<VFXEffectRunner>();
                if (runner == null) runner = target.Root.AddComponent<VFXEffectRunner>();
                runner.InvokeAfter(duration, onComplete);
            }
            else
            {
                onComplete?.Invoke();
            }
        }
    }
}
