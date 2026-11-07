#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEngine;

namespace VFXViewer.Editor
{
    internal static class SpectatorIOSBuildTools
    {
        private const string IPadSpectatorScenePath = "Assets/_Game/_Common/Scenes/IPadSpectatorScene.unity";
        private const string VisionHostScenePath = "Assets/_Game/_Common/Scenes/VisionHostScene.unity";
        private const string MainScenePath = "Assets/_Game/_Common/Scenes/Main.unity";
        private const string BonjourServiceType = "_vfxviewer-spectator._tcp";
        private const string ARKitPackageId = "com.unity.xr.arkit";
        private const string ARKitLoaderType = "UnityEngine.XR.ARKit.ARKitLoader";
        private const string ARKitLoaderDefine = "UNITY_XR_ARKIT_LOADER_ENABLED";
        private const string XRGeneralSettingsPerBuildTargetTypeName =
            "UnityEditor.XR.Management.XRGeneralSettingsPerBuildTarget, Unity.XR.Management.Editor";
        private const string XRPackageMetadataStoreTypeName =
            "UnityEditor.XR.Management.Metadata.XRPackageMetadataStore, Unity.XR.Management.Editor";
        private const string LocalNetworkUsageDescription =
            "用于在同一局域网中发现并连接 Vision Pro 主机，以同步展项布局、播放状态和虚拟手。";
        private const string CameraUsageDescription =
            "用于通过 iPhone 或 iPad 摄像头显示现实环境，并将 spectator 的虚拟内容叠加到画面中。";
        private const string PhotoLibraryAddUsageDescription =
            "用于把 spectator 端拍摄的照片保存到系统相册。";
        private const string MicrophoneUsageDescription =
            "用于在 spectator 端录像时可选录入现场讲解音频。";

        [MenuItem("工具/Spectator/iPad/配置 iPad Spectator 出包", priority = 320)]
        private static void ConfigureIPadSpectatorBuild()
        {
            if (!EnsureIOSBuildTarget())
                return;

            if (!EnsureIOSXRProviderConfiguration())
                return;

            ApplyIPadSpectatorSceneOrder();
            AssetDatabase.SaveAssets();
            Debug.Log("[SpectatorBuild] iPad spectator build settings configured.");
        }

        [MenuItem("工具/Spectator/iPad/导出 iPad Spectator Xcode 工程...", priority = 321)]
        private static void BuildIPadSpectatorXcodeProject()
        {
            if (!EnsureIOSBuildTarget())
                return;

            if (!EnsureIOSXRProviderConfiguration())
                return;

            ApplyIPadSpectatorSceneOrder();

            string outputPath = EditorUtility.SaveFolderPanel(
                "选择 iPad Spectator Xcode 工程输出目录",
                "Builds/iPadSpectator",
                "iPadSpectator");
            if (string.IsNullOrWhiteSpace(outputPath))
                return;

            string[] scenes = GetEnabledScenePaths();
            if (scenes.Length == 0)
            {
                Debug.LogError("[SpectatorBuild] No enabled scenes found for iPad spectator build.");
                return;
            }

            var buildPlayerOptions = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = BuildTarget.iOS,
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(buildPlayerOptions);
            if (report.summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[SpectatorBuild] iPad spectator Xcode project exported to: {outputPath}");
                EditorUtility.RevealInFinder(outputPath);
                return;
            }

            Debug.LogError(
                $"[SpectatorBuild] Build failed. result={report.summary.result}, errors={report.summary.totalErrors}");
        }

        private static bool EnsureIOSBuildTarget()
        {
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.iOS)
                return true;

            bool switched = EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.iOS, BuildTarget.iOS);
            if (!switched)
            {
                Debug.LogError("[SpectatorBuild] Failed to switch active build target to iOS.");
                return false;
            }

            return true;
        }

        private static bool EnsureIOSXRProviderConfiguration()
        {
            if (!IsArKitPackageInstalled())
            {
                Debug.LogError(
                    $"[SpectatorBuild] Missing {ARKitPackageId}. Unity must finish importing the Apple ARKit XR Plug-in before the iPad spectator build can show camera passthrough.");
                return false;
            }

            EnsureIOSPlayerSettings();

            object settingsPerBuildTarget = LoadXRGeneralSettingsPerBuildTarget();
            if (settingsPerBuildTarget == null)
            {
                Debug.LogError("[SpectatorBuild] Unable to load or create XRGeneralSettingsPerBuildTarget.");
                return false;
            }

            if (!InvokeBool(settingsPerBuildTarget, "HasSettingsForBuildTarget", BuildTargetGroup.iOS))
            {
                InvokeInstance(settingsPerBuildTarget, "CreateDefaultSettingsForBuildTarget", BuildTargetGroup.iOS);
                Debug.Log("[SpectatorBuild] Created default iOS XRGeneralSettings.");
            }

            if (!InvokeBool(settingsPerBuildTarget, "HasManagerSettingsForBuildTarget", BuildTargetGroup.iOS))
            {
                InvokeInstance(settingsPerBuildTarget, "CreateDefaultManagerSettingsForBuildTarget", BuildTargetGroup.iOS);
                Debug.Log("[SpectatorBuild] Created default iOS XRManagerSettings.");
            }

            object generalSettings = InvokeInstance(settingsPerBuildTarget, "SettingsForBuildTarget", BuildTargetGroup.iOS);
            object managerSettings = InvokeInstance(settingsPerBuildTarget, "ManagerSettingsForBuildTarget", BuildTargetGroup.iOS);
            if (generalSettings == null || managerSettings == null)
            {
                Debug.LogError("[SpectatorBuild] iOS XR settings exist but XRManagerSettings is still missing.");
                return false;
            }

            SetPropertyValue(generalSettings, "InitManagerOnStart", true);
            SetPropertyValue(managerSettings, "automaticLoading", true);
            SetPropertyValue(managerSettings, "automaticRunning", true);

            if (!IsLoaderAssigned(ARKitLoaderType, BuildTargetGroup.iOS))
            {
                if (!AssignLoader(managerSettings, ARKitLoaderType, BuildTargetGroup.iOS))
                {
                    Debug.LogError("[SpectatorBuild] Failed to assign the Apple ARKit loader to the iOS XR settings.");
                    return false;
                }

                Debug.Log("[SpectatorBuild] Assigned Apple ARKit loader to the iOS XR settings.");
            }

            if (generalSettings is UnityEngine.Object generalSettingsObject)
                EditorUtility.SetDirty(generalSettingsObject);
            if (managerSettings is UnityEngine.Object managerSettingsObject)
                EditorUtility.SetDirty(managerSettingsObject);
            AssetDatabase.SaveAssets();

            bool automaticLoading = GetPropertyValue<bool>(managerSettings, "automaticLoading");
            bool automaticRunning = GetPropertyValue<bool>(managerSettings, "automaticRunning");
            string scriptingDefines = GetScriptingDefineSymbols(BuildTargetGroup.iOS);
            Debug.Log(
                $"[SpectatorBuild] iOS XR config ready. loaderAssigned={IsLoaderAssigned(ARKitLoaderType, BuildTargetGroup.iOS)}, automaticLoading={automaticLoading}, automaticRunning={automaticRunning}, cameraUsageDescriptionSet={!string.IsNullOrWhiteSpace(PlayerSettings.iOS.cameraUsageDescription)}, defines={scriptingDefines}");
            return true;
        }

        private static void EnsureIOSPlayerSettings()
        {
            EnsureScriptingDefineSymbol(BuildTargetGroup.iOS, ARKitLoaderDefine);

            if (!string.Equals(PlayerSettings.iOS.cameraUsageDescription, CameraUsageDescription, StringComparison.Ordinal))
            {
                PlayerSettings.iOS.cameraUsageDescription = CameraUsageDescription;
                Debug.Log("[SpectatorBuild] Updated PlayerSettings.iOS.cameraUsageDescription for spectator AR passthrough.");
            }
        }

        private static bool IsArKitPackageInstalled()
        {
            return Type.GetType($"{ARKitLoaderType}, Unity.XR.ARKit", throwOnError: false) != null;
        }

        private static object LoadXRGeneralSettingsPerBuildTarget()
        {
            Type settingsType = FindType(XRGeneralSettingsPerBuildTargetTypeName);
            if (settingsType == null)
                return null;

            string[] guids = AssetDatabase.FindAssets("t:XRGeneralSettingsPerBuildTarget");
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath(path, settingsType);
                if (asset != null)
                    return asset;
            }

            MethodInfo getOrCreate = settingsType.GetMethod(
                "GetOrCreate",
                BindingFlags.Static | BindingFlags.NonPublic);
            return getOrCreate?.Invoke(null, null);
        }

        private static bool IsLoaderAssigned(string loaderTypeName, BuildTargetGroup buildTargetGroup)
        {
            Type metadataStoreType = FindType(XRPackageMetadataStoreTypeName);
            if (metadataStoreType == null)
                return false;

            object result = InvokeStatic(metadataStoreType, "IsLoaderAssigned", loaderTypeName, buildTargetGroup);
            return result is bool value && value;
        }

        private static bool AssignLoader(object managerSettings, string loaderTypeName, BuildTargetGroup buildTargetGroup)
        {
            Type metadataStoreType = FindType(XRPackageMetadataStoreTypeName);
            if (metadataStoreType == null)
                return false;

            object result = InvokeStatic(metadataStoreType, "AssignLoader", managerSettings, loaderTypeName, buildTargetGroup);
            return result is bool value && value;
        }

        private static Type FindType(string assemblyQualifiedName)
        {
            Type type = Type.GetType(assemblyQualifiedName, throwOnError: false);
            if (type != null)
                return type;

            string typeNameOnly = assemblyQualifiedName.Split(',')[0].Trim();
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                type = assemblies[i].GetType(typeNameOnly, throwOnError: false);
                if (type != null)
                    return type;
            }

            return null;
        }

        private static object InvokeInstance(object target, string methodName, params object[] args)
        {
            if (target == null)
                return null;

            MethodInfo method = FindMatchingMethod(
                target.GetType(),
                methodName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                args);
            return method?.Invoke(target, args);
        }

        private static object InvokeStatic(Type type, string methodName, params object[] args)
        {
            if (type == null)
                return null;

            MethodInfo method = FindMatchingMethod(
                type,
                methodName,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                args);
            return method?.Invoke(null, args);
        }

        private static bool InvokeBool(object target, string methodName, params object[] args)
        {
            object result = InvokeInstance(target, methodName, args);
            return result is bool value && value;
        }

        private static void SetPropertyValue(object target, string propertyName, object value)
        {
            if (target == null)
                return;

            PropertyInfo property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            property?.SetValue(target, value);
        }

        private static T GetPropertyValue<T>(object target, string propertyName)
        {
            if (target == null)
                return default;

            PropertyInfo property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            object value = property?.GetValue(target);
            return value is T typedValue ? typedValue : default;
        }

        private static MethodInfo FindMatchingMethod(Type type, string methodName, BindingFlags bindingFlags, object[] args)
        {
            MethodInfo[] methods = type.GetMethods(bindingFlags);
            MethodInfo bestMatch = null;
            int bestScore = int.MinValue;

            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (!string.Equals(method.Name, methodName, StringComparison.Ordinal))
                    continue;

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != args.Length)
                    continue;

                int score = 0;
                bool isMatch = true;
                for (int parameterIndex = 0; parameterIndex < parameters.Length; parameterIndex++)
                {
                    Type parameterType = parameters[parameterIndex].ParameterType;
                    object arg = args[parameterIndex];
                    if (parameterType.IsByRef)
                        parameterType = parameterType.GetElementType();

                    if (arg == null)
                    {
                        bool acceptsNull = !parameterType.IsValueType || Nullable.GetUnderlyingType(parameterType) != null;
                        if (!acceptsNull)
                        {
                            isMatch = false;
                            break;
                        }

                        continue;
                    }

                    Type argType = arg.GetType();
                    if (parameterType == argType)
                    {
                        score += 4;
                        continue;
                    }

                    if (parameterType.IsAssignableFrom(argType))
                    {
                        score += 2;
                        continue;
                    }

                    if (parameterType.IsEnum && argType.IsEnum && parameterType == argType)
                    {
                        score += 4;
                        continue;
                    }

                    isMatch = false;
                    break;
                }

                if (!isMatch)
                    continue;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestMatch = method;
                }
            }

            return bestMatch;
        }

        private static void ApplyIPadSpectatorSceneOrder()
        {
            var existing = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes ?? Array.Empty<EditorBuildSettingsScene>());
            var ordered = new List<EditorBuildSettingsScene>
            {
                ReuseOrCreateScene(existing, IPadSpectatorScenePath, enabled: true),
                ReuseOrCreateScene(existing, VisionHostScenePath, enabled: true),
                ReuseOrCreateScene(existing, MainScenePath, enabled: true),
            };

            for (int i = 0; i < existing.Count; i++)
            {
                EditorBuildSettingsScene scene = existing[i];
                if (scene == null || string.IsNullOrWhiteSpace(scene.path))
                    continue;

                if (string.Equals(scene.path, IPadSpectatorScenePath, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(scene.path, VisionHostScenePath, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(scene.path, MainScenePath, StringComparison.OrdinalIgnoreCase))
                    continue;

                ordered.Add(scene);
            }

            EditorBuildSettings.scenes = ordered.ToArray();
        }

        private static EditorBuildSettingsScene ReuseOrCreateScene(
            List<EditorBuildSettingsScene> scenes,
            string path,
            bool enabled)
        {
            for (int i = 0; i < scenes.Count; i++)
            {
                EditorBuildSettingsScene scene = scenes[i];
                if (scene == null || !string.Equals(scene.path, path, StringComparison.OrdinalIgnoreCase))
                    continue;

                scene.enabled = enabled;
                return scene;
            }

            return new EditorBuildSettingsScene(path, enabled);
        }

        private static string[] GetEnabledScenePaths()
        {
            var result = new List<string>();
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes ?? Array.Empty<EditorBuildSettingsScene>();
            for (int i = 0; i < scenes.Length; i++)
            {
                EditorBuildSettingsScene scene = scenes[i];
                if (scene != null && scene.enabled && !string.IsNullOrWhiteSpace(scene.path))
                    result.Add(scene.path);
            }

            return result.ToArray();
        }

        private static void EnsureScriptingDefineSymbol(BuildTargetGroup buildTargetGroup, string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return;

            string current = GetScriptingDefineSymbols(buildTargetGroup);
            var symbols = new List<string>();
            bool alreadyPresent = false;

            if (!string.IsNullOrWhiteSpace(current))
            {
                string[] split = current.Split(';');
                for (int i = 0; i < split.Length; i++)
                {
                    string entry = split[i]?.Trim();
                    if (string.IsNullOrWhiteSpace(entry))
                        continue;

                    if (string.Equals(entry, symbol, StringComparison.Ordinal))
                    {
                        alreadyPresent = true;
                        symbols.Add(entry);
                        continue;
                    }

                    if (!symbols.Contains(entry))
                        symbols.Add(entry);
                }
            }

            if (alreadyPresent)
                return;

            symbols.Add(symbol);
            string updated = string.Join(";", symbols);
            SetScriptingDefineSymbols(buildTargetGroup, updated);
            Debug.Log($"[SpectatorBuild] Added iOS scripting define: {symbol}. defines={updated}");
        }

        private static string GetScriptingDefineSymbols(BuildTargetGroup buildTargetGroup)
        {
#pragma warning disable 618
            return PlayerSettings.GetScriptingDefineSymbolsForGroup(buildTargetGroup) ?? string.Empty;
#pragma warning restore 618
        }

        private static void SetScriptingDefineSymbols(BuildTargetGroup buildTargetGroup, string symbols)
        {
#pragma warning disable 618
            PlayerSettings.SetScriptingDefineSymbolsForGroup(buildTargetGroup, symbols ?? string.Empty);
#pragma warning restore 618
        }

        [PostProcessBuild(1000)]
        private static void ApplyLocalNetworkPermissions(BuildTarget target, string pathToBuiltProject)
        {
            if (!IsAppleTargetWithLocalNetwork(target))
                return;

            string plistPath = Path.Combine(pathToBuiltProject, "Info.plist");
            if (!File.Exists(plistPath))
            {
                Debug.LogWarning($"[SpectatorBuild] Info.plist not found at {plistPath}");
                return;
            }

            var plist = new PlistDocument();
            plist.ReadFromString(File.ReadAllText(plistPath));
            PlistElementDict root = plist.root;

            root.SetString("NSLocalNetworkUsageDescription", LocalNetworkUsageDescription);
            root.SetString("NSCameraUsageDescription", CameraUsageDescription);
            root.SetString("NSPhotoLibraryAddUsageDescription", PhotoLibraryAddUsageDescription);
            root.SetString("NSMicrophoneUsageDescription", MicrophoneUsageDescription);

            PlistElementArray bonjourServices = GetOrCreateStringArray(root, "NSBonjourServices");
            EnsureStringArrayContains(bonjourServices, BonjourServiceType);

            File.WriteAllText(plistPath, plist.WriteToString());
            AddRequiredAppleFrameworks(pathToBuiltProject);
            Debug.Log("[SpectatorBuild] Applied Bonjour and local network permissions to Info.plist.");
        }

        private static void AddRequiredAppleFrameworks(string pathToBuiltProject)
        {
            string projectPath = PBXProject.GetPBXProjectPath(pathToBuiltProject);
            if (!File.Exists(projectPath))
            {
                Debug.LogWarning($"[SpectatorBuild] Xcode project file not found at {projectPath}");
                return;
            }

            var project = new PBXProject();
            project.ReadFromFile(projectPath);

            string mainTargetGuid = project.GetUnityMainTargetGuid();
            string frameworkTargetGuid = project.GetUnityFrameworkTargetGuid();

            AddFrameworkIfPossible(project, mainTargetGuid, "ARKit.framework");
            AddFrameworkIfPossible(project, mainTargetGuid, "AVFoundation.framework");
            AddFrameworkIfPossible(project, mainTargetGuid, "AVKit.framework");
            AddFrameworkIfPossible(project, mainTargetGuid, "Foundation.framework");
            AddFrameworkIfPossible(project, mainTargetGuid, "Photos.framework");
            AddFrameworkIfPossible(project, mainTargetGuid, "ReplayKit.framework");
            AddFrameworkIfPossible(project, mainTargetGuid, "UIKit.framework");
            AddFrameworkIfPossible(project, mainTargetGuid, "UniformTypeIdentifiers.framework");

            AddFrameworkIfPossible(project, frameworkTargetGuid, "ARKit.framework");
            AddFrameworkIfPossible(project, frameworkTargetGuid, "AVFoundation.framework");
            AddFrameworkIfPossible(project, frameworkTargetGuid, "AVKit.framework");
            AddFrameworkIfPossible(project, frameworkTargetGuid, "Foundation.framework");
            AddFrameworkIfPossible(project, frameworkTargetGuid, "Photos.framework");
            AddFrameworkIfPossible(project, frameworkTargetGuid, "ReplayKit.framework");
            AddFrameworkIfPossible(project, frameworkTargetGuid, "UIKit.framework");
            AddFrameworkIfPossible(project, frameworkTargetGuid, "UniformTypeIdentifiers.framework");

            project.WriteToFile(projectPath);
            Debug.Log("[SpectatorBuild] Added ARKit/AVFoundation/AVKit/Foundation/Photos/ReplayKit/UIKit/UniformTypeIdentifiers to the Xcode project.");
        }

        private static void AddFrameworkIfPossible(PBXProject project, string targetGuid, string frameworkName)
        {
            if (project == null || string.IsNullOrWhiteSpace(targetGuid) || string.IsNullOrWhiteSpace(frameworkName))
                return;

            project.AddFrameworkToProject(targetGuid, frameworkName, false);
        }

        private static bool IsAppleTargetWithLocalNetwork(BuildTarget target)
        {
            if (target == BuildTarget.iOS)
                return true;

#if UNITY_2022_3_OR_NEWER || UNITY_6000_0_OR_NEWER
            if (target == BuildTarget.VisionOS)
                return true;
#endif

            return false;
        }

        private static PlistElementArray GetOrCreateStringArray(PlistElementDict root, string key)
        {
            if (root.values.TryGetValue(key, out PlistElement element))
            {
                PlistElementArray existingArray = element.AsArray();
                if (existingArray != null)
                    return existingArray;

                root.values.Remove(key);
            }

            return root.CreateArray(key);
        }

        private static void EnsureStringArrayContains(PlistElementArray array, string value)
        {
            if (array == null || string.IsNullOrWhiteSpace(value))
                return;

            for (int i = 0; i < array.values.Count; i++)
            {
                if (array.values[i] is PlistElementString element &&
                    string.Equals(element.value, value, StringComparison.Ordinal))
                    return;
            }

            array.AddString(value);
        }
    }
}
#endif
