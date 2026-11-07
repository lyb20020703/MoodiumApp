using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// VFX 内容句柄。包装 VFX 根节点，统一实现 IContentHandle。
    /// </summary>
    public class VFXContentHandle : IContentHandle
    {
        public GameObject Root { get; }
        public Transform RootTransform { get; }
        public ContentType Type => ContentType.VFX;

        public VFXContentHandle(GameObject root)
        {
            Root = root != null ? root : throw new System.ArgumentNullException(nameof(root));
            RootTransform = Root.transform;
        }

        public T GetComponentForEffect<T>() where T : Component
        {
            return Root != null ? Root.GetComponentInChildren<T>(true) : null;
        }
    }
}

