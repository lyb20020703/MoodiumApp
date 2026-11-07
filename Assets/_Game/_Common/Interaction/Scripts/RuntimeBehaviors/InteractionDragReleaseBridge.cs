using UnityEngine;
using VFXViewer;

namespace Interaction
{
    /// <summary>
    /// 将统一抓取生命周期桥接到交互模块的拖拽释放行为。
    /// </summary>
    [RequireComponent(typeof(GrabLifecycleRelay))]
    public class InteractionDragReleaseBridge : MonoBehaviour
    {
        [Tooltip("内容（用于构建 IContentHandle）。为空则用本物体")]
        [SerializeField] private GameObject contentRoot;

        private GrabLifecycleRelay _relay;

        private void Awake()
        {
            _relay = GetComponent<GrabLifecycleRelay>();
        }

        private void OnEnable()
        {
            if (_relay == null)
                _relay = GetComponent<GrabLifecycleRelay>();

            if (_relay != null)
                _relay.GrabEnded += OnGrabEnded;
        }

        private void OnDisable()
        {
            if (_relay != null)
                _relay.GrabEnded -= OnGrabEnded;
        }

        private void OnDestroy()
        {
            if (_relay != null)
                _relay.GrabEnded -= OnGrabEnded;
        }

        private void OnGrabEnded()
        {
            GameObject root = contentRoot != null ? contentRoot : gameObject;
            // 使用统一工厂根据内容结构创建句柄，自动推断内容类型
            var handle = ContentHandleFactory.Create(root);
            InteractionModule.NotifyDragReleasedStatic(handle);
        }
    }
}
