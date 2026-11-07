using System;
using UnityEngine;
using UnityEngine.VFX;

namespace Interaction
{
    /// <summary>
    /// VFX Graph 出现效果：Play VisualEffect。
    /// </summary>
    public class VFXAppear : MonoBehaviour, IAppearEffect
    {
        [Tooltip("出现效果作用的内容（含 VisualEffect 的节点）。")]
        [SerializeField] private GameObject contentRoot;

        public GameObject ContentRoot => contentRoot;

        public void Appear(IContentHandle target, float duration, Action onComplete)
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
                controller.Appear(duration, onComplete);
                return;
            }

            var vfx = target.Root.GetComponentInChildren<VisualEffect>(true);
            if (vfx != null)
                vfx.Play();
            if (duration > 0f)
            {
                var runner = target?.Root != null ? target.Root.GetComponent<VFXEffectRunner>() : null;
                if (runner == null && target?.Root != null) runner = target.Root.AddComponent<VFXEffectRunner>();
                if (runner != null) runner.InvokeAfter(duration, onComplete);
                else onComplete?.Invoke();
            }
            else
            {
                onComplete?.Invoke();
            }
        }
    }

    internal class VFXEffectRunner : MonoBehaviour
    {
        private float _timer;
        private Action _callback;

        public void InvokeAfter(float duration, Action callback)
        {
            enabled = true;
            _timer = duration;
            _callback = callback;
        }

        public void Cancel()
        {
            _timer = 0f;
            _callback = null;
        }

        private void Update()
        {
            if (_callback == null) return;
            _timer -= Time.deltaTime;
            if (_timer <= 0f)
            {
                var cb = _callback;
                _callback = null;
                cb?.Invoke();
            }
        }
    }
}
