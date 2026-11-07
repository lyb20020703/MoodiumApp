using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// 视频内容句柄。包装视频根节点，统一实现 IContentHandle。
    /// </summary>
    public class VideoContentHandle : IContentHandle
    {
        public GameObject Root { get; }
        public Transform RootTransform { get; }
        public ContentType Type => ContentType.Video;

        public VideoContentHandle(GameObject root)
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

