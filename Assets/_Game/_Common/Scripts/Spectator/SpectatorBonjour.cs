using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace VFXViewer
{
    internal sealed class SpectatorBonjourAdvertiser : IDisposable
    {
        private bool m_IsRunning;

        public void Start(string serviceName, string serviceType, int port)
        {
            if (m_IsRunning)
                Stop();

            m_IsRunning = true;
            Debug.Log($"[SpectatorBonjour] Starting advertiser. name={serviceName} type={serviceType} port={port}");
            SpectatorBonjourNative.StartAdvertiser(serviceName, serviceType, port);
        }

        public void Stop()
        {
            if (!m_IsRunning)
                return;

            m_IsRunning = false;
            Debug.Log("[SpectatorBonjour] Stopping advertiser.");
            SpectatorBonjourNative.StopAdvertiser();
        }

        public void Dispose()
        {
            Stop();
        }
    }

    internal sealed class SpectatorBonjourBrowser : IDisposable
    {
        private readonly List<SpectatorBonjourEvent> m_Events = new List<SpectatorBonjourEvent>();
        private bool m_IsRunning;

        public void Start(string serviceType)
        {
            if (m_IsRunning)
                Stop();

            m_IsRunning = true;
            Debug.Log($"[SpectatorBonjour] Starting browser. type={serviceType}");
            SpectatorBonjourNative.StartBrowser(serviceType);
        }

        public void Stop()
        {
            if (!m_IsRunning)
                return;

            m_IsRunning = false;
            Debug.Log("[SpectatorBonjour] Stopping browser.");
            SpectatorBonjourNative.StopBrowser();
            m_Events.Clear();
        }

        public IReadOnlyList<SpectatorBonjourEvent> PollEvents()
        {
            m_Events.Clear();
            while (SpectatorBonjourNative.TryDequeueEvent(out var nextEvent))
                m_Events.Add(nextEvent);
            return m_Events;
        }

        public void Dispose()
        {
            Stop();
        }
    }

    internal static class SpectatorBonjourNative
    {
#if (UNITY_IOS || UNITY_VISIONOS) && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void SpectatorBonjour_StartAdvertiser(string serviceName, string serviceType, int port);

        [DllImport("__Internal")]
        private static extern void SpectatorBonjour_StopAdvertiser();

        [DllImport("__Internal")]
        private static extern void SpectatorBonjour_StartBrowser(string serviceType);

        [DllImport("__Internal")]
        private static extern void SpectatorBonjour_StopBrowser();

        [DllImport("__Internal")]
        private static extern IntPtr SpectatorBonjour_CopyNextEventJson();

        [DllImport("__Internal")]
        private static extern void SpectatorBonjour_FreeString(IntPtr buffer);
#endif

        public static void StartAdvertiser(string serviceName, string serviceType, int port)
        {
#if (UNITY_IOS || UNITY_VISIONOS) && !UNITY_EDITOR
            SpectatorBonjour_StartAdvertiser(serviceName, serviceType, port);
#else
            Debug.Log($"[SpectatorBonjour] Advertiser stub start name={serviceName} type={serviceType} port={port}");
#endif
        }

        public static void StopAdvertiser()
        {
#if (UNITY_IOS || UNITY_VISIONOS) && !UNITY_EDITOR
            SpectatorBonjour_StopAdvertiser();
#endif
        }

        public static void StartBrowser(string serviceType)
        {
#if (UNITY_IOS || UNITY_VISIONOS) && !UNITY_EDITOR
            SpectatorBonjour_StartBrowser(serviceType);
#else
            Debug.Log($"[SpectatorBonjour] Browser stub start type={serviceType}");
#endif
        }

        public static void StopBrowser()
        {
#if (UNITY_IOS || UNITY_VISIONOS) && !UNITY_EDITOR
            SpectatorBonjour_StopBrowser();
#endif
        }

        public static bool TryDequeueEvent(out SpectatorBonjourEvent nextEvent)
        {
            nextEvent = null;
#if (UNITY_IOS || UNITY_VISIONOS) && !UNITY_EDITOR
            IntPtr pointer = SpectatorBonjour_CopyNextEventJson();
            if (pointer == IntPtr.Zero)
                return false;

            try
            {
                string json = Marshal.PtrToStringAnsi(pointer);
                if (string.IsNullOrWhiteSpace(json))
                    return false;

                nextEvent = JsonUtility.FromJson<SpectatorBonjourEvent>(json);
                return nextEvent != null;
            }
            finally
            {
                SpectatorBonjour_FreeString(pointer);
            }
#else
            return false;
#endif
        }
    }
}
