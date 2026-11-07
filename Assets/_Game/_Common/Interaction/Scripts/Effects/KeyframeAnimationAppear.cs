using System;
using UnityEngine;
using RenderHeads.Media.AVProVideo;

namespace Interaction
{
    /// <summary>
    /// 关键帧动画出现效果：播放 Animator 的指定状态。
    /// </summary>
    public class KeyframeAnimationAppear : MonoBehaviour, IAppearEffect
    {
        [Tooltip("出现效果作用的内容（含 Animator 的节点）。")]
        [SerializeField] private GameObject contentRoot;
        [Tooltip("使用 Animator 时填写状态名")]
        [SerializeField] private string animatorStateOrTrigger = "Appear";

        public GameObject ContentRoot => contentRoot;

        /// <summary> 由 InteractionModule 等从配置注入参数，仅支持 Animator 状态名。 </summary>
        public void SetConfig(string stateName)
        {
            animatorStateOrTrigger = string.IsNullOrEmpty(stateName) ? "Appear" : stateName;
        }

        public void Appear(IContentHandle target, float duration, Action onComplete)
        {
            string stateName = animatorStateOrTrigger;

            if (target?.Root == null)
            {
                onComplete?.Invoke();
                return;
            }

            Animator animator = null;
            if (target.Type == ContentType.Video)
            {
                var apply = target.GetComponentForEffect<ApplyToMesh>();
                if (apply != null)
                    animator = apply.GetComponentInChildren<Animator>(true);
            }
            if (animator == null)
                animator = target.Root.GetComponentInChildren<Animator>(true);

            if (animator != null && !string.IsNullOrEmpty(stateName))
            {
                AnimatorActivationController activationController = animator.GetComponent<AnimatorActivationController>();
                if (activationController != null)
                    activationController.TryPlay(stateName);
                else
                    animator.Play(stateName);
            }

            // 出现目前不依赖回调时长，直接回调即可（若后续需要可改为按状态长度推算）
            onComplete?.Invoke();
        }
    }

    internal class KeyframeAnimationRunner : MonoBehaviour
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
