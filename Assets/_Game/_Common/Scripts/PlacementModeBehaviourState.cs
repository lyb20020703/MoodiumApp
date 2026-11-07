using System;
using System.Collections.Generic;
using UnityEngine;

namespace VFXViewer
{
    [DisallowMultipleComponent]
    public sealed class PlacementModeBehaviourState : MonoBehaviour
    {
        [Serializable]
        private struct Entry
        {
            public Behaviour behaviour;
            public bool originalEnabled;
        }

        [SerializeField] private List<Entry> m_Entries = new List<Entry>();

        public void CaptureIfNeeded(Behaviour behaviour)
        {
            if (behaviour == null)
                return;

            for (int i = 0; i < m_Entries.Count; i++)
            {
                if (m_Entries[i].behaviour == behaviour)
                    return;
            }

            m_Entries.Add(new Entry
            {
                behaviour = behaviour,
                originalEnabled = behaviour.enabled
            });
        }

        public bool TryGetOriginalEnabled(Behaviour behaviour, out bool enabled)
        {
            for (int i = 0; i < m_Entries.Count; i++)
            {
                if (m_Entries[i].behaviour != behaviour)
                    continue;

                enabled = m_Entries[i].originalEnabled;
                return true;
            }

            enabled = false;
            return false;
        }
    }
}
