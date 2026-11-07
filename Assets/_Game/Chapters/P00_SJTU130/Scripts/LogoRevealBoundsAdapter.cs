using UnityEngine;

namespace SJTU130
{
    /// <summary>
    /// Adapts the chapter reveal material to a mesh whose authored local axes differ
    /// from the original 130 logo mesh. The material instance remains local to this renderer.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Renderer), typeof(MeshFilter))]
    public sealed class LogoRevealBoundsAdapter : MonoBehaviour
    {
        private static readonly int RevealDirectionId = Shader.PropertyToID("_RevealDirection");
        private static readonly int RevealWorldOffsetId = Shader.PropertyToID("_RevealWorldOffset");
        private static readonly int RevealScaleId = Shader.PropertyToID("_RevealScale");
        private static readonly int RevealBiasId = Shader.PropertyToID("_RevealBias");

        [Tooltip("模型本地坐标中的显现方向。当前模型旋转后，-Z 对应世界向上。")]
        [SerializeField] private Vector3 localRevealDirection = Vector3.back;

        [Tooltip("显现边缘宽度占整个显现范围的比例。")]
        [SerializeField, Min(0.0001f)] private float edgeRatio = 0.0132f;

        private void Awake()
        {
            var meshFilter = GetComponent<MeshFilter>();
            var targetRenderer = GetComponent<Renderer>();
            if (meshFilter == null || meshFilter.sharedMesh == null || targetRenderer == null)
                return;

            Vector3 direction = localRevealDirection.sqrMagnitude > 0.000001f
                ? localRevealDirection.normalized
                : Vector3.up;

            Bounds bounds = meshFilter.sharedMesh.bounds;
            Vector3 center = bounds.center;
            Vector3 extents = bounds.extents;
            float minimum = float.PositiveInfinity;
            float maximum = float.NegativeInfinity;

            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 corner = center + Vector3.Scale(extents, new Vector3(x, y, z));
                float projection = Vector3.Dot(corner, direction);
                minimum = Mathf.Min(minimum, projection);
                maximum = Mathf.Max(maximum, projection);
            }

            float range = Mathf.Max(maximum - minimum, 0.0001f);
            Vector3 offset = direction * minimum;
            Material[] materials = targetRenderer.materials;

            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i];
                if (material == null || !material.HasProperty(RevealDirectionId))
                    continue;

                material.SetVector(RevealDirectionId, direction);
                if (material.HasProperty(RevealWorldOffsetId))
                    material.SetVector(RevealWorldOffsetId, offset);
                if (material.HasProperty(RevealScaleId))
                    material.SetFloat(RevealScaleId, range);
                if (material.HasProperty(RevealBiasId))
                    material.SetFloat(RevealBiasId, Mathf.Max(range * edgeRatio, 0.0001f));
            }
        }
    }
}
