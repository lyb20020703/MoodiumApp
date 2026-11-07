using System.Collections.Generic;
using UnityEngine;

namespace VFXViewer
{
    [DisallowMultipleComponent]
    public sealed class ExperienceGrabbableLayerState : MonoBehaviour
    {
        private readonly List<GameObject> m_Targets = new List<GameObject>();
        private readonly List<int> m_OriginalLayers = new List<int>();

        public void ApplyToColliders(int layer)
        {
            CaptureIfNeeded();

            for (int i = 0; i < m_Targets.Count; i++)
            {
                if (m_Targets[i] != null)
                    m_Targets[i].layer = layer;
            }
        }

        public void Restore()
        {
            for (int i = 0; i < m_Targets.Count; i++)
            {
                if (m_Targets[i] != null)
                    m_Targets[i].layer = m_OriginalLayers[i];
            }
        }

        private void CaptureIfNeeded()
        {
            if (m_Targets.Count > 0)
                return;

            var seen = new HashSet<GameObject>();
            var colliders = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider == null)
                    continue;

                GameObject target = collider.gameObject;
                if (!seen.Add(target))
                    continue;

                m_Targets.Add(target);
                m_OriginalLayers.Add(target.layer);
            }
        }
    }
}
