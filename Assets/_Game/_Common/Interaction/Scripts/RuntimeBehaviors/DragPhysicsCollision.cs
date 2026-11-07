using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// 拖拽释放后参与物理：释放时启用/保持 Rigidbody 非 Kinematic，使其参与碰撞与重力。
    /// </summary>
    public class DragPhysicsCollision : MonoBehaviour, IDragReleaseBehavior
    {
        [Tooltip("释放时若没有 Rigidbody 则自动添加")]
        [SerializeField] private bool addRigidbodyIfMissing = true;

        [Tooltip("释放时是否启用重力")]
        [SerializeField] private bool useGravity = true;

        /// <summary> 由 InteractionModule 等从配置注入参数。 </summary>
        public void SetConfig(bool addRbIfMissing, bool gravity)
        {
            addRigidbodyIfMissing = addRbIfMissing;
            useGravity = gravity;
        }

        public void OnRelease(IContentHandle content)
        {
            if (content?.Root == null) return;

            var rb = content.Root.GetComponent<Rigidbody>();
            if (rb == null && addRigidbodyIfMissing)
            {
                rb = content.Root.AddComponent<Rigidbody>();
            }

            if (rb != null)
            {
                rb.isKinematic = false;
                rb.useGravity = useGravity;
            }
        }
    }
}
