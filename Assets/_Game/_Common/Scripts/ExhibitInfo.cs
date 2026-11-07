using System;
using UnityEngine;

namespace VFXViewer
{
    [DisallowMultipleComponent]
    public class ExhibitInfo : MonoBehaviour
    {
        public static event Action<ExhibitInfo> Registered;
        public static event Action<ExhibitInfo> Unregistered;
        public static event Action<ExhibitInfo> Changed;

        public string moduleId;
        public string displayName;
        public string instanceID;
        public bool placementContentHidden;
        private bool m_IsRegistered;
        private bool m_HasLoggedDuplicateWarning;

        private void OnEnable()
        {
            if (!IsPrimaryComponentOnGameObject())
            {
                LogDuplicateWarningOnce();
                return;
            }

            m_IsRegistered = true;
            Registered?.Invoke(this);
        }

        private void OnDisable()
        {
            if (!m_IsRegistered)
                return;

            m_IsRegistered = false;
            Unregistered?.Invoke(this);
        }

        public void NotifyRegistryChanged()
        {
            if (!m_IsRegistered)
                return;

            Changed?.Invoke(this);
        }

        public bool SetPlacementContentHidden(bool hidden)
        {
            if (placementContentHidden == hidden)
                return false;

            placementContentHidden = hidden;
            NotifyRegistryChanged();
            return true;
        }

        private bool IsPrimaryComponentOnGameObject()
        {
            var components = GetComponents<ExhibitInfo>();
            if (components == null || components.Length <= 1)
                return true;

            for (int i = 0; i < components.Length; i++)
            {
                if (ReferenceEquals(components[i], this))
                    return i == 0;
            }

            return true;
        }

        private void LogDuplicateWarningOnce()
        {
            if (m_HasLoggedDuplicateWarning)
                return;

            m_HasLoggedDuplicateWarning = true;
            Debug.LogWarning(
                $"Duplicate ExhibitInfo detected on {name}. Only the first component will be used.",
                this);
        }
    }
}
