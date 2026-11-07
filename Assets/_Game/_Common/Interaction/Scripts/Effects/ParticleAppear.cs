using System;
using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// 粒子出现效果：Play ParticleSystem，可选从 alpha 0 渐变到 1（通过 ColorOverLifetime 或 Renderer 材质）。
    /// </summary>
    public class ParticleAppear : MonoBehaviour, IAppearEffect
    {
        [Tooltip("出现效果作用的内容（含 ParticleSystem 的节点）。")]
        [SerializeField] private GameObject contentRoot;

        public GameObject ContentRoot => contentRoot;

        public void Appear(IContentHandle target, float duration, Action onComplete)
        {
            if (target?.Root == null)
            {
                onComplete?.Invoke();
                return;
            }

            var systems = target.Root.GetComponentsInChildren<ParticleSystem>(true);
            if (systems.Length == 0)
            {
                onComplete?.Invoke();
                return;
            }

            foreach (var ps in systems)
            {
                ps.Clear(true);
                ps.Simulate(0f, true, true);
                ps.Play(true);
            }

            if (duration > 0f)
            {
                var runner = target.Root.GetComponent<ParticleAppearRunner>();
                if (runner == null) runner = target.Root.AddComponent<ParticleAppearRunner>();
                runner.InvokeAfter(duration, onComplete);
            }
            else
            {
                onComplete?.Invoke();
            }
        }
    }

    internal class ParticleAppearRunner : MonoBehaviour
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
