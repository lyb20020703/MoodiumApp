using System;
using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// 开始阶段可用的「直接隐藏」效果：立即隐藏目标内容。
    /// </summary>
    public class CommonAppearHideEffect : MonoBehaviour, IAppearEffect
    {
        [SerializeField] private GameObject contentRoot;

        public GameObject ContentRoot => contentRoot;

        public void Appear(IContentHandle target, float duration, Action onComplete)
        {
            if (target?.Root != null && target.Root.activeSelf)
                target.Root.SetActive(false);
            onComplete?.Invoke();
        }
    }
}
