using UnityEngine;

namespace Moodium.Reality
{
    public sealed class RealitySpatialTableManager : MonoBehaviour
    {
        [SerializeField] GameObject m_TablePrefab;
        [SerializeField, Min(0.5f)] float m_Distance = 0.95f;
        [SerializeField] float m_HeightOffset = -0.38f;

        GameObject m_TableInstance;

        public GameObject TableInstance => m_TableInstance;

        public void Configure(GameObject tablePrefab) => m_TablePrefab = tablePrefab;

        public void ShowTable(Camera camera)
        {
            if (m_TableInstance != null)
            {
                m_TableInstance.SetActive(true);
                return;
            }
            if (m_TablePrefab == null || camera == null)
            {
                Debug.LogWarning("[Moodium Reality Table] Table prefab or XR camera is missing.");
                return;
            }

            var forward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.01f)
                forward = camera.transform.forward.normalized;
            var position = camera.transform.position + forward * m_Distance + Vector3.up * m_HeightOffset;
            var rotation = Quaternion.LookRotation(forward, Vector3.up);
            m_TableInstance = Instantiate(m_TablePrefab, position, rotation);
            m_TableInstance.name = "Spatial Interactive Table (Reality Fixed)";
            var handle = m_TableInstance.GetComponentInChildren<Moodium.Flow.MoodiumWindowHandle>(true);
            if (handle != null)
                handle.Configure(m_TableInstance.transform);
            Debug.Log($"[Moodium Reality Table] Spawned fixed table at {position}, rotation={rotation.eulerAngles}.");
        }

        public void HideTable()
        {
            if (m_TableInstance == null)
                return;
            Destroy(m_TableInstance);
            m_TableInstance = null;
            Debug.Log("[Moodium Reality Table] Removed table on Reality mode exit.");
        }
    }
}
