using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace VFXViewer
{
    public static class PlacementDebugFileLogger
    {
        private const string LogFileName = "placement_debug.log";
        private static readonly bool EnableFileLogging = true;

        private static readonly object s_Lock = new object();
        private static bool s_HasWrittenSessionHeader;

        public static string LogFilePath => Path.Combine(Application.persistentDataPath, LogFileName);
        public static bool IsLoggingEnabled => EnableFileLogging;

        public static void EnsureCreated()
        {
            if (!EnableFileLogging)
                return;

            try
            {
                lock (s_Lock)
                {
                    EnsureSessionHeader();
                }
            }
            catch
            {
                // Logging must never break runtime flow.
            }
        }

        public static void Log(string message)
        {
            if (!EnableFileLogging || string.IsNullOrWhiteSpace(message))
                return;

            try
            {
                lock (s_Lock)
                {
                    EnsureSessionHeader();
                    File.AppendAllText(
                        LogFilePath,
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}",
                        Encoding.UTF8);
                }
            }
            catch
            {
                // Logging must never break runtime flow.
            }
        }

        public static void Clear()
        {
            try
            {
                lock (s_Lock)
                {
                    if (File.Exists(LogFilePath))
                        File.Delete(LogFilePath);

                    s_HasWrittenSessionHeader = false;
                }
            }
            catch
            {
                // Ignore cleanup failure.
            }
        }

        private static void EnsureSessionHeader()
        {
            if (s_HasWrittenSessionHeader)
                return;

            string directory = Path.GetDirectoryName(LogFilePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.AppendAllText(
                LogFilePath,
                $"{Environment.NewLine}===== Session {DateTime.Now:yyyy-MM-dd HH:mm:ss} ====={Environment.NewLine}" +
                $"platform={Application.platform}, unity={Application.unityVersion}{Environment.NewLine}" +
                $"persistentDataPath={Application.persistentDataPath}{Environment.NewLine}",
                Encoding.UTF8);

            s_HasWrittenSessionHeader = true;
        }
    }
}
