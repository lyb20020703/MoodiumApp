using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Interaction
{
    /// <summary>
    /// Vision Pro 上手部触摸由 XRPokeInteractor 处理，其默认只与带 XRPokeFilter 的 Interactable 产生 Hover。
    /// 触摸/长按触发器在「使用 Hover」时会调用此方法，为交互体自动添加 XRPokeFilter（若缺失且有 Collider），使 Poke 能正确 Hover。
    /// </summary>
    public static class VisionProHoverHelper
    {
        /// <summary>
        /// 若交互体上尚无 XRPokeFilter 且存在 Collider，则自动添加 XRPokeFilter 并绑定，使 Vision Pro 上 Poke Interactor 能产生 Hover。
        /// </summary>
        public static void EnsurePokeFilterForVisionProHover(XRBaseInteractable interactable)
        {
            if (interactable == null) return;
            if (interactable.GetComponent<XRPokeFilter>() != null) return;

            var col = interactable.GetComponentInChildren<Collider>();
            if (col == null)
            {
                Debug.LogWarning("[Interaction] 使用 Hover（手部接触）时，触摸/长按交互体或其子物体需有 Collider，否则 Vision Pro 上 Poke 无法命中。请为交互体添加 Collider。", interactable);
                return;
            }

            var filter = interactable.gameObject.AddComponent<XRPokeFilter>();
            filter.pokeInteractable = interactable;
            filter.pokeCollider = col;
        }
    }
}
