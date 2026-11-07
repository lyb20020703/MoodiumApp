using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// 粒子内容句柄。包装粒子根节点，统一实现 IContentHandle。
    /// </summary>
    public class ParticleContentHandle : IContentHandle
    {
        public GameObject Root { get; }
        public Transform RootTransform { get; }
        public ContentType Type => ContentType.Particle;

        public ParticleContentHandle(GameObject root)
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

