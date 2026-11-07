using System;
using UnityEngine;
using RenderHeads.Media.AVProVideo;

namespace Interaction
{
    /// <summary>
    /// 关键帧动画消失效果：播放 Animator 的指定状态。
    /// </summary>
    public class KeyframeAnimationDisappear : MonoBehaviour, IDisappearEffect
    {
        [Tooltip("结束效果作用的内容（含 Animator 的节点）。")]
        [SerializeField] private GameObject contentRoot;
        [Tooltip("使用 Animator 时填写状态名")]
        [SerializeField] private string animatorStateOrTrigger = "Disappear";

        public GameObject ContentRoot => contentRoot;

        /// <summary> 由 InteractionModule 等从配置注入参数，仅支持 Animator 状态名。 </summary>
        public void SetConfig(string stateName)
        {
            animatorStateOrTrigger = string.IsNullOrEmpty(stateName) ? "Disappear" : stateName;
        }

        public void Disappear(IContentHandle target, float duration, Action onComplete)
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
                {
                    activationController.TryPlay(stateName, onComplete);
                    return;
                }

                animator.Play(stateName);
            }

            // 结束需要在动画播完后再隐藏，这里尝试用状态名匹配动画片段长度；匹配不到则立刻回调
            float clipLength = 0f;
            if (animator != null && animator.runtimeAnimatorController != null)
            {
                var clips = animator.runtimeAnimatorController.animationClips;
                if (clips != null)
                {
                    for (int i = 0; i < clips.Length; i++)
                    {
                        var c = clips[i];
                        if (c != null && c.name == stateName)
                        {
                            clipLength = c.length;
                            break;
                        }
                    }
                }
            }

            if (clipLength > 0f)
            {
                // 计时器可以挂在 Animator 所在物体上，避免根节点被隐藏影响 Update
                var runnerGO = animator != null ? animator.gameObject : target.Root;
                var runner = runnerGO.GetComponent<KeyframeAnimationRunner>();
                if (runner == null) runner = runnerGO.AddComponent<KeyframeAnimationRunner>();
                runner.InvokeAfter(clipLength, onComplete);
            }
            else
            {
                onComplete?.Invoke();
            }
        }
    }
}
