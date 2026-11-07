using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// 拖拽释放后归位：释放时将内容的位置与旋转恢复为记录时的初始值。
    /// </summary>
    public class DragReturnHome : MonoBehaviour, IDragReleaseBehavior
    {
        [Tooltip("若勾选，在 OnRelease 时从当前 Transform 重新记录「家」位置（用于动态设置锚点）")]
        [SerializeField] private bool recordHomeOnRelease;

        /// <summary> 由 InteractionModule 等从配置注入参数。 </summary>
        public void SetConfig(bool recordOnRelease) { recordHomeOnRelease = recordOnRelease; }

        private Vector3 _homePosition;
        private Quaternion _homeRotation;
        private bool _recorded;

        private void Awake()
        {
            RecordHome(transform);
        }

        public void RecordHome(Transform t)
        {
            if (t == null) return;
            _homePosition = t.position;
            _homeRotation = t.rotation;
            _recorded = true;
        }

        public void OnRelease(IContentHandle content)
        {
            if (content?.RootTransform == null) return;

            var t = content.RootTransform;
            if (recordHomeOnRelease)
            {
                RecordHome(t);
                return;
            }

            if (!_recorded)
                RecordHome(t);

            t.position = _homePosition;
            t.rotation = _homeRotation;

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
