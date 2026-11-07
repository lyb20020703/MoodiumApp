using System;
using System.IO;
using UnityEngine;

namespace VFXViewer
{
    public partial class ChapterPlacementDirector
    {
        [Serializable]
        private class AppModeStateData
        {
            public string appMode;
        }

        [Header("本地持久化")]
        [SerializeField] private bool m_EnableAppModePersistence = true;
        [SerializeField] private string m_AppModeStateFileName = "app_state.json";

        private ExhibitAppMode ResolveStartupAppMode()
        {
            if (!ShouldUseAppModePersistence())
                return m_StartAppMode;

            if (!TryLoadPersistedAppMode(out ExhibitAppMode persistedMode))
                return m_StartAppMode;

            return persistedMode;
        }

        private void PersistCurrentAppMode()
        {
            if (!ShouldUseAppModePersistence())
                return;

            string path = GetAppModeStateFilePath();
            if (string.IsNullOrWhiteSpace(path))
                return;

            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                var data = new AppModeStateData
                {
                    appMode = m_CurrentAppMode.ToString()
                };

                File.WriteAllText(path, JsonUtility.ToJson(data, true));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to persist app mode to {path}: {ex.Message}", this);
            }
        }

        private bool TryLoadPersistedAppMode(out ExhibitAppMode mode)
        {
            mode = m_StartAppMode;

            string path = GetAppModeStateFilePath();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return false;

            try
            {
                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json))
                    return false;

                var data = JsonUtility.FromJson<AppModeStateData>(json);
                if (data == null || string.IsNullOrWhiteSpace(data.appMode))
                    return false;

                if (!Enum.TryParse(data.appMode, ignoreCase: true, out mode))
                    return false;

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to load app mode from {path}: {ex.Message}", this);
                return false;
            }
        }

        private string GetAppModeStateFilePath()
        {
            if (string.IsNullOrWhiteSpace(m_AppModeStateFileName))
                return string.Empty;

            return Path.Combine(Application.persistentDataPath, m_AppModeStateFileName);
        }

        private bool ShouldUseAppModePersistence()
        {
            if (!m_EnableAppModePersistence)
                return false;

            // Module-only test scenes should always follow their authored startup mode and
            // should not overwrite the shared runtime app-state used by placement scenes.
            return !(m_Spawner == null && m_EnableModuleAutoAdvanceWithoutSpawner);
        }
    }
}
