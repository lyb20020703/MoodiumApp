#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEngine;

namespace VFXViewer.Editor
{
    internal static class AVProAppleLinkerPostProcess
    {
        private const string AVProVideoPluginName = "AVProVideo.xcframework";
        private const string AVProVideoProjectPath = "Libraries/AVProVideo/AVProVideo.xcframework";

        [PostProcessBuild(2000)]
        public static void EnsureAVProVideoIsLinked(BuildTarget target, string pathToBuiltProject)
        {
            if (!TryGetApplePluginAssetPath(target, out string pluginAssetPath))
                return;

            string projectPath = GetXcodeProjectPath(target, pathToBuiltProject);
            if (string.IsNullOrEmpty(projectPath) || !File.Exists(projectPath))
            {
                Debug.LogWarning($"[AVProVideoLinker] Xcode project file not found at {projectPath}");
                return;
            }

            var project = new PBXProject();
            project.ReadFromFile(projectPath);

            string unityFrameworkTargetGuid = project.GetUnityFrameworkTargetGuid();
            if (string.IsNullOrEmpty(unityFrameworkTargetGuid))
            {
                Debug.LogWarning("[AVProVideoLinker] UnityFramework target was not found.");
                return;
            }

            string xcodePluginPath = FindExistingXcodePluginPath(project, target);
            if (string.IsNullOrEmpty(xcodePluginPath))
            {
                xcodePluginPath = AVProVideoProjectPath;
            }

            string fullXcodePluginPath = Path.Combine(pathToBuiltProject, xcodePluginPath);
            if (!Directory.Exists(fullXcodePluginPath))
                CopyDirectory(pluginAssetPath, fullXcodePluginPath);

            StripIgnoredFiles(fullXcodePluginPath);

            string pluginGuid = project.FindFileGuidByProjectPath(xcodePluginPath);
            if (string.IsNullOrEmpty(pluginGuid))
                pluginGuid = project.AddFile(xcodePluginPath, xcodePluginPath);

            string frameworksBuildPhaseGuid = project.GetFrameworksBuildPhaseByTarget(unityFrameworkTargetGuid);
            string projectText = File.ReadAllText(projectPath);
            if (!BuildPhaseContainsFile(projectText, frameworksBuildPhaseGuid, AVProVideoPluginName))
            {
                project.AddFileToBuildSection(unityFrameworkTargetGuid, frameworksBuildPhaseGuid, pluginGuid);
                Debug.Log($"[AVProVideoLinker] Linked {xcodePluginPath} to UnityFramework.");
            }
            else
            {
                Debug.Log($"[AVProVideoLinker] {AVProVideoPluginName} is already linked to UnityFramework.");
            }

            project.WriteToFile(projectPath);
        }

        private static bool TryGetApplePluginAssetPath(BuildTarget target, out string pluginAssetPath)
        {
            string platformFolder;
            switch (target)
            {
                case BuildTarget.iOS:
                    platformFolder = "iOS";
                    break;
                case BuildTarget.tvOS:
                    platformFolder = "tvOS";
                    break;
#if UNITY_2022_3_OR_NEWER || UNITY_6000_0_OR_NEWER
                case BuildTarget.VisionOS:
                    platformFolder = "visionOS";
                    break;
#endif
                default:
                    pluginAssetPath = null;
                    return false;
            }

            pluginAssetPath = $"Assets/Plugins/AVProVideo/Runtime/Plugins/{platformFolder}/{AVProVideoPluginName}";
            if (Directory.Exists(pluginAssetPath))
                return true;

            Debug.LogError($"[AVProVideoLinker] Missing AVPro Video native plugin at {pluginAssetPath}");
            return false;
        }

        private static string GetXcodeProjectPath(BuildTarget target, string pathToBuiltProject)
        {
#if UNITY_2022_3_OR_NEWER || UNITY_6000_0_OR_NEWER
            if (target == BuildTarget.VisionOS)
                return Path.Combine(pathToBuiltProject, "Unity-VisionOS.xcodeproj", "project.pbxproj");
#endif
            return PBXProject.GetPBXProjectPath(pathToBuiltProject);
        }

        private static string FindExistingXcodePluginPath(PBXProject project, BuildTarget target)
        {
            string preferredPathPart = GetPreferredPathPart(target);
            string fallbackPath = null;
            IReadOnlyList<string> paths = project.GetRealPathsOfAllFiles(PBXSourceTree.Source);

            foreach (string path in paths)
            {
                string normalizedPath = NormalizeProjectPath(path);
                if (!normalizedPath.EndsWith(AVProVideoPluginName, StringComparison.Ordinal))
                    continue;

                if (fallbackPath == null)
                    fallbackPath = normalizedPath;

                if (!string.IsNullOrEmpty(preferredPathPart) &&
                    normalizedPath.IndexOf(preferredPathPart, StringComparison.OrdinalIgnoreCase) >= 0)
                    return normalizedPath;
            }

            return fallbackPath;
        }

        private static string GetPreferredPathPart(BuildTarget target)
        {
            switch (target)
            {
                case BuildTarget.iOS:
                    return "/iOS/";
                case BuildTarget.tvOS:
                    return "/tvOS/";
#if UNITY_2022_3_OR_NEWER || UNITY_6000_0_OR_NEWER
                case BuildTarget.VisionOS:
                    return "/visionOS/";
#endif
                default:
                    return null;
            }
        }

        private static bool BuildPhaseContainsFile(string projectText, string buildPhaseGuid, string fileName)
        {
            if (string.IsNullOrEmpty(projectText) ||
                string.IsNullOrEmpty(buildPhaseGuid) ||
                string.IsNullOrEmpty(fileName))
                return false;

            string phaseHeader = $"{buildPhaseGuid} /* Frameworks */ = {{";
            int phaseStart = projectText.IndexOf(phaseHeader, StringComparison.Ordinal);
            if (phaseStart < 0)
                phaseStart = projectText.IndexOf($"{buildPhaseGuid} = {{", StringComparison.Ordinal);
            if (phaseStart < 0)
                return false;

            int phaseEnd = projectText.IndexOf("};", phaseStart, StringComparison.Ordinal);
            if (phaseEnd < 0)
                return false;

            string phaseBlock = projectText.Substring(phaseStart, phaseEnd - phaseStart);
            return phaseBlock.IndexOf(fileName, StringComparison.Ordinal) >= 0;
        }

        private static void CopyDirectory(string sourcePath, string destinationPath)
        {
            var source = new DirectoryInfo(sourcePath);
            var destination = new DirectoryInfo(destinationPath);

            if (!source.Exists)
                throw new DirectoryNotFoundException(source.FullName);

            Directory.CreateDirectory(destination.FullName);

            foreach (DirectoryInfo sourceDirectory in source.GetDirectories())
            {
                DirectoryInfo destinationDirectory = destination.CreateSubdirectory(sourceDirectory.Name);
                CopyDirectory(sourceDirectory.FullName, destinationDirectory.FullName);
            }

            foreach (FileInfo sourceFile in source.GetFiles())
            {
                if (ShouldIgnoreCopiedFile(sourceFile))
                    continue;

                sourceFile.CopyTo(Path.Combine(destination.FullName, sourceFile.Name), true);
            }
        }

        private static void StripIgnoredFiles(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
                return;

            foreach (string filePath in Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories))
            {
                string fileName = Path.GetFileName(filePath);
                if (string.Equals(fileName, ".DS_Store", StringComparison.Ordinal) ||
                    string.Equals(Path.GetExtension(filePath), ".meta", StringComparison.Ordinal))
                    File.Delete(filePath);
            }
        }

        private static bool ShouldIgnoreCopiedFile(FileInfo file)
        {
            return string.Equals(file.Name, ".DS_Store", StringComparison.Ordinal) ||
                   string.Equals(file.Extension, ".meta", StringComparison.Ordinal);
        }

        private static string NormalizeProjectPath(string path)
        {
            return path?.Replace('\\', '/');
        }
    }
}
#endif
