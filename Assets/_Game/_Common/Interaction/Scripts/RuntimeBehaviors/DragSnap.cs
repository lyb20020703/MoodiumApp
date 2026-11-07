using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// 拖拽释放后固定：释放时将内容固定到当前位置（可选对齐到网格或最近锚点）。
    /// </summary>
    public class DragSnap : MonoBehaviour, IDragReleaseBehavior
    {
        [Tooltip("是否对齐到网格")]
        [SerializeField] private bool snapToGrid;

        [Tooltip("网格大小（世界单位）")]
        [SerializeField] private float gridSize = 0.5f;

        [Tooltip("释放时是否清零刚体速度")]
        [SerializeField] private bool zeroVelocity = true;

        /// <summary> 由 InteractionModule 等从配置注入参数。 </summary>
        public void SetConfig(bool snapGrid, float grid, bool zeroVel)
        {
            snapToGrid = snapGrid;
            gridSize = grid;
            zeroVelocity = zeroVel;
        }

        public void OnRelease(IContentHandle content)
        {
            if (content?.RootTransform == null) return;

            var t = content.RootTransform;
            if (snapToGrid && gridSize > 0f)
            {
                var p = t.position;
                p.x = Mathf.Round(p.x / gridSize) * gridSize;
                p.y = Mathf.Round(p.y / gridSize) * gridSize;
                p.z = Mathf.Round(p.z / gridSize) * gridSize;
                t.position = p;
            }

            if (zeroVelocity)
            {
                var rb = content.Root.GetComponent<Rigidbody>();
                if (rb != null)
                {
#if UNITY_2023_3_OR_NEWER
                    rb.linearVelocity = Vector3.zero;
#else
                    rb.velocity = Vector3.zero;
#endif
                    rb.angularVelocity = Vector3.zero;
                }
            }
        }
    }
}
