using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// 3D 模型内容句柄。包装模型根节点，统一实现 IContentHandle。
    /// </summary>
    public class ModelContentHandle : IContentHandle
    {
        public GameObject Root { get; }
        public Transform RootTransform { get; }
        public ContentType Type => ContentType.Model;

        public ModelContentHandle(GameObject root)
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
