using UnityEngine;

namespace Moodium.Interaction
{
    [DisallowMultipleComponent]
    public sealed class SoftTouchDeformationController : MonoBehaviour
    {
        [Header("Soft candy deformation")]
        [Tooltip("Indent depth as a fraction of the model's largest local dimension.")]
        [Range(0f, 0.3f)] public float deformationStrength = 0.12f;
        [Tooltip("Touch radius as a fraction of the model's largest local dimension.")]
        [Range(0.05f, 1f)] public float deformationRadius = 0.45f;
        [Min(0.01f)] public float recoverySpeed = 7f;
        [Range(0f, 0.4f)] public float maxDeformation = 0.18f;

        [Header("Vision Pro pinch grab prototype")]
        [SerializeField] bool m_EnablePinchGrab;
        [Range(0.1f, 2f)] public float pinchStrength = 1f;
        [Range(0.05f, 1f)] public float grabRadius = 0.48f;
        [Range(1f, 40f)] public float elasticity = 18f;

        MeshFilter m_MeshFilter;
        Mesh m_RuntimeMesh;
        Vector3[] m_OriginalVertices;
        Vector3[] m_DeformedVertices;
        float m_OriginalModelSize;
        Vector3 m_TouchPositionWorld;
        Vector3 m_InitialPinchWorld;
        Vector3 m_GrabOffsetLocal;
        Vector3 m_TargetGrabOffsetLocal;
        Vector3 m_GrabVelocityLocal;
        float m_CurrentWeight;
        bool m_Touching;

        public bool IsTouching => m_Touching;
        public bool IsGrabbed => m_EnablePinchGrab && m_Touching;
        public float CurrentWeight => m_CurrentWeight;
        public Vector3 PinchWorldPosition { get; private set; }
        public Vector3 MeshHitPosition { get; private set; }

        // Shared by Creative Space and multi-mesh completion houses; identical smoothstep falloff.
        public static float ContactFalloff(float distance, float radius)
        {
            var weight = 1f - Mathf.Clamp01(distance / Mathf.Max(.0001f, radius));
            return weight * weight * (3f - 2f * weight);
        }

        void Awake()
        {
            PrepareMesh();
        }

        public void TouchBegin(Vector3 worldPosition)
        {
            if (!PrepareMesh())
                return;
            m_TouchPositionWorld = worldPosition;
            PinchWorldPosition = worldPosition;
            MeshHitPosition = worldPosition;
            m_InitialPinchWorld = worldPosition;
            m_TargetGrabOffsetLocal = Vector3.zero;
            m_GrabOffsetLocal = Vector3.zero;
            m_GrabVelocityLocal = Vector3.zero;
            m_Touching = true;
            Debug.Log($"[Soft Candy] Touch begin: {name}, pinch={PinchWorldPosition}, " +
                      $"meshHit={MeshHitPosition}, grab={m_EnablePinchGrab}");
        }

        public void TouchHold(Vector3 worldPosition)
        {
            if (!m_Touching)
                return;
            PinchWorldPosition = worldPosition;
            if (!m_EnablePinchGrab)
            {
                m_TouchPositionWorld = worldPosition;
                return;
            }

            var meshTransform = m_MeshFilter.transform;
            m_TargetGrabOffsetLocal =
                meshTransform.InverseTransformVector(worldPosition - m_InitialPinchWorld) * pinchStrength;
            var modelSize = GetModelSize();
            m_TargetGrabOffsetLocal = Vector3.ClampMagnitude(
                m_TargetGrabOffsetLocal,
                modelSize * maxDeformation);
        }

        public void TouchEnd()
        {
            if (!m_Touching)
                return;
            m_Touching = false;
            Debug.Log($"[Soft Candy] Touch end: {name}");
        }

        void Update()
        {
            if (m_RuntimeMesh == null)
                return;

            var target = m_Touching ? 1f : 0f;
            var speed = m_Touching ? recoverySpeed * 1.5f : recoverySpeed;
            m_CurrentWeight = Mathf.MoveTowards(m_CurrentWeight, target, speed * Time.deltaTime);
            UpdateElasticGrab();
            ApplyDeformation();
        }

        void UpdateElasticGrab()
        {
            if (!m_EnablePinchGrab)
                return;

            var target = m_Touching ? m_TargetGrabOffsetLocal : Vector3.zero;
            var deltaTime = Mathf.Min(Time.deltaTime, 1f / 30f);
            m_GrabVelocityLocal += (target - m_GrabOffsetLocal) * elasticity * deltaTime;
            m_GrabVelocityLocal *= Mathf.Exp(-recoverySpeed * deltaTime);
            m_GrabOffsetLocal += m_GrabVelocityLocal * deltaTime;

            if (!m_Touching && m_GrabOffsetLocal.sqrMagnitude < 0.00000001f &&
                m_GrabVelocityLocal.sqrMagnitude < 0.00000001f)
            {
                m_GrabOffsetLocal = Vector3.zero;
                m_GrabVelocityLocal = Vector3.zero;
            }
        }

        bool PrepareMesh()
        {
            if (m_RuntimeMesh != null)
                return true;

            m_MeshFilter = GetComponentInChildren<MeshFilter>(true);
            if (m_MeshFilter == null || m_MeshFilter.sharedMesh == null)
            {
                Debug.LogWarning($"[Soft Candy] No readable MeshFilter found on {name}.");
                return false;
            }

            m_RuntimeMesh = Instantiate(m_MeshFilter.sharedMesh);
            m_RuntimeMesh.name = m_MeshFilter.sharedMesh.name + " (Soft Candy Runtime)";
            m_MeshFilter.sharedMesh = m_RuntimeMesh;
            m_OriginalVertices = m_RuntimeMesh.vertices;
            m_DeformedVertices = new Vector3[m_OriginalVertices.Length];
            System.Array.Copy(m_OriginalVertices, m_DeformedVertices, m_OriginalVertices.Length);
            m_RuntimeMesh.MarkDynamic();
            m_OriginalModelSize = Mathf.Max(
                m_RuntimeMesh.bounds.size.x,
                Mathf.Max(m_RuntimeMesh.bounds.size.y, m_RuntimeMesh.bounds.size.z));
            return true;
        }

        void ApplyDeformation()
        {
            if (m_CurrentWeight <= 0.0001f && m_GrabOffsetLocal.sqrMagnitude <= 0.00000001f)
            {
                RestoreMesh();
                return;
            }

            var meshTransform = m_MeshFilter.transform;
            var touchLocal = meshTransform.InverseTransformPoint(m_TouchPositionWorld);
            var center = m_RuntimeMesh.bounds.center;
            var inward = center - touchLocal;
            if (inward.sqrMagnitude < 0.000001f)
                inward = Vector3.back;
            inward.Normalize();

            // Relative values remain visually stable after Creative Mode user scaling and
            // compensate for imported FBX hierarchies with large child transform scales.
            var modelSize = GetModelSize();
            var radiusLocal = modelSize * deformationRadius;
            var depthLocal = modelSize * Mathf.Min(deformationStrength, maxDeformation);
            var grabRadiusLocal = modelSize * grabRadius;

            for (var i = 0; i < m_OriginalVertices.Length; i++)
            {
                var distance = Vector3.Distance(m_OriginalVertices[i], touchLocal);
                var falloff = ContactFalloff(distance, radiusLocal);
                var grabFalloff = ContactFalloff(distance, grabRadiusLocal);
                var pressOffset = inward * (depthLocal * falloff * m_CurrentWeight);
                var grabOffset = m_EnablePinchGrab ? m_GrabOffsetLocal * grabFalloff : Vector3.zero;
                m_DeformedVertices[i] = m_OriginalVertices[i] + pressOffset + grabOffset;
            }

            m_RuntimeMesh.vertices = m_DeformedVertices;
            m_RuntimeMesh.RecalculateNormals();
            m_RuntimeMesh.RecalculateBounds();
        }

        float GetModelSize()
        {
            return Mathf.Max(0.0001f, m_OriginalModelSize);
        }

        void RestoreMesh()
        {
            if (m_RuntimeMesh == null || m_OriginalVertices == null)
                return;
            m_RuntimeMesh.vertices = m_OriginalVertices;
            m_RuntimeMesh.RecalculateNormals();
            m_RuntimeMesh.RecalculateBounds();
        }

        void OnDisable()
        {
            m_Touching = false;
            m_CurrentWeight = 0f;
            m_GrabOffsetLocal = Vector3.zero;
            m_TargetGrabOffsetLocal = Vector3.zero;
            m_GrabVelocityLocal = Vector3.zero;
            RestoreMesh();
        }

        void OnDestroy()
        {
            if (m_RuntimeMesh != null)
                Destroy(m_RuntimeMesh);
        }
    }
}
