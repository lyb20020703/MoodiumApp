using System;
using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// 粒子消失效果：停止发射并等待粒子自然消失，或立即 Stop + Clear。
    /// </summary>
    public class ParticleDisappear : MonoBehaviour, IDisappearEffect
    {
        [Tooltip("结束效果作用的内容（含 ParticleSystem 的节点）。")]
        [SerializeField] private GameObject contentRoot;
        [Tooltip("true = 仅 Stop 发射，等粒子播完；false = Stop 且 Clear 立即清空")]
        [SerializeField] private bool waitForParticles = true;

        public GameObject ContentRoot => contentRoot;

        /// <summary> 由 InteractionModule 等从配置注入参数。 </summary>
        public void SetConfig(bool wait) { waitForParticles = wait; }

        public void Disappear(IContentHandle target, float duration, Action onComplete)
        {
            bool shouldWaitForParticles = waitForParticles;

            if (target?.Root == null)
            {
                onComplete?.Invoke();
                return;
            }

            var systems = target.Root.GetComponentsInChildren<ParticleSystem>(true);
            foreach (var ps in systems)
            {
                ps.Stop(true, shouldWaitForParticles ? ParticleSystemStopBehavior.StopEmitting : ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            if (shouldWaitForParticles && duration > 0f)
            {
                var runner = target.Root.GetComponent<ParticleAppearRunner>();
                if (runner == null) runner = target.Root.AddComponent<ParticleAppearRunner>();
                runner.InvokeAfter(duration, onComplete);
            }
            else if (!shouldWaitForParticles || duration <= 0f)
            {
                onComplete?.Invoke();
            }
        }
    }
}
