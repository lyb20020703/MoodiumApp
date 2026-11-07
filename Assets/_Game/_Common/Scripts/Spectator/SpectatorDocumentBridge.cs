using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace VFXViewer
{
    [Serializable]
    internal sealed class SpectatorDocumentBridgeEvent
    {
        public string type;
        public string filePath;
        public string message;
    }

    internal static class SpectatorDocumentBridge
    {
        public static bool IsSupported => !Application.isEditor && PlatformRuntime.IsIOSButNotVisionOS;

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void SpectatorDocument_ExportJsonFile(string fileName, string jsonContents);

        [DllImport("__Internal")]
        private static extern void SpectatorDocument_BeginImportJsonFile();

        [DllImport("__Internal")]
        private static extern IntPtr SpectatorDocument_CopyNextEventJson();

        [DllImport("__Internal")]
        private static extern void SpectatorDocument_FreeString(IntPtr buffer);
#endif

        public static void ExportJsonFile(string fileName, string jsonContents)
        {
            if (!IsSupported)
            {
                Debug.LogWarning("[SpectatorDocument] Export is only supported on iOS spectator builds.");
                return;
            }

#if UNITY_IOS && !UNITY_EDITOR
            SpectatorDocument_ExportJsonFile(fileName ?? "spectator_root_worldmap_backup.json", jsonContents ?? "{}");
#endif
        }

        public static void BeginImportJsonFile()
        {
            if (!IsSupported)
            {
                Debug.LogWarning("[SpectatorDocument] Import is only supported on iOS spectator builds.");
                return;
            }

#if UNITY_IOS && !UNITY_EDITOR
            SpectatorDocument_BeginImportJsonFile();
#endif
        }

        public static bool TryDequeueEvent(out SpectatorDocumentBridgeEvent nextEvent)
        {
            nextEvent = null;
            if (!IsSupported)
                return false;

#if UNITY_IOS && !UNITY_EDITOR
            IntPtr pointer = SpectatorDocument_CopyNextEventJson();
            if (pointer == IntPtr.Zero)
                return false;

            try
            {
                string json = Marshal.PtrToStringAnsi(pointer);
                if (string.IsNullOrWhiteSpace(json))
                    return false;

                nextEvent = JsonUtility.FromJson<SpectatorDocumentBridgeEvent>(json);
                return nextEvent != null;
            }
            finally
            {
                SpectatorDocument_FreeString(pointer);
            }
#else
            return false;
#endif
        }
    }
}
