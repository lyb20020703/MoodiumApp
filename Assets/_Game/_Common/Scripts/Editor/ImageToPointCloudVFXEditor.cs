// 选图 → SHARP → PLY → PCX Texture → PositionMap/ColorMap → VFX Prefab

using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.VFX;
using Pcx;

namespace Game.Editor
{
    public class ImageToPointCloudVFXEditor : EditorWindow
    {
        const int PlyContainerTypeTexture = 2; // Pcx.PlyImporter.ContainerType.Texture
        const string PositionMapName = "PositionMap";
        const string ColorMapName = "ColorMap";

        static readonly string[] OutputFolderLabels = { "Common", "P00", "P01", "P02", "P03", "P04", "P05", "P06", "P07" };
        static readonly string[] OutputFolderPaths = {
            "Assets/_Game/_Common/Data",
            "Assets/_Game/Chapters/P00_Identity/Data",
            "Assets/_Game/Chapters/P01_SJTU130/Data",
            "Assets/_Game/Chapters/P02_Return/Data",
            "Assets/_Game/Chapters/P03_Startup/Data",
            "Assets/_Game/Chapters/P04_SelfReliance/Data",
            "Assets/_Game/Chapters/P05_Perseverance/Data",
            "Assets/_Game/Chapters/P06_Sword/Data",
            "Assets/_Game/Chapters/P07_Ending/Data"
        };
        static readonly string[] PrefabFolderPaths = {
            "Assets/_Game/_Common/Prefabs",
            "Assets/_Game/Chapters/P00_Identity/Prefabs",
            "Assets/_Game/Chapters/P01_SJTU130/Prefabs",
            "Assets/_Game/Chapters/P02_Return/Prefabs",
            "Assets/_Game/Chapters/P03_Startup/Prefabs",
            "Assets/_Game/Chapters/P04_SelfReliance/Prefabs",
            "Assets/_Game/Chapters/P05_Perseverance/Prefabs",
            "Assets/_Game/Chapters/P06_Sword/Prefabs",
            "Assets/_Game/Chapters/P07_Ending/Prefabs"
        };
        static readonly string[] VFXFolderPaths = {
            "Assets/_Game/_Common/VFX",
            "Assets/_Game/Chapters/P00_Identity/Art/VFX",
            "Assets/_Game/Chapters/P01_SJTU130/Art/VFX",
            "Assets/_Game/Chapters/P02_Return/Art/VFX",
            "Assets/_Game/Chapters/P03_Startup/Art/VFX",
            "Assets/_Game/Chapters/P04_SelfReliance/Art/VFX",
            "Assets/_Game/Chapters/P05_Perseverance/Art/VFX",
            "Assets/_Game/Chapters/P06_Sword/Art/VFX",
            "Assets/_Game/Chapters/P07_Ending/Art/VFX"
        };

        [SerializeField] string _inputImagePath = "";
        [SerializeField] int _outputFolderIndex = 0;
        [SerializeField] VisualEffectAsset _vfxTemplate;

        string _lastError;
        Vector2 _scroll;

        [MenuItem("工具/图像转点云VFX")]
        static void Open()
        {
            var w = GetWindow<ImageToPointCloudVFXEditor>(false, "图像 → 点云VFX");
            w.minSize = new Vector2(360, 320);
        }

        void OnEnable()
        {
            if (_vfxTemplate == null)
                _vfxTemplate = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>("Assets/_Game/_Common/VFX/PointCloud_Template.vfx");
        }

        void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("输入图片", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            _inputImagePath = EditorGUILayout.TextField(_inputImagePath);
            if (GUILayout.Button("浏览", GUILayout.Width(60)))
            {
                string path = EditorUtility.OpenFilePanel("选择图片", string.IsNullOrEmpty(_inputImagePath) ? "" : Path.GetDirectoryName(_inputImagePath), "png,jpg,jpeg,bmp,tga");
                if (!string.IsNullOrEmpty(path))
                    _inputImagePath = path;
            }
            EditorGUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(_inputImagePath) && !File.Exists(_inputImagePath))
                EditorGUILayout.HelpBox("文件不存在。", MessageType.Warning);
            EditorGUILayout.Space(4);

            EditorGUILayout.LabelField("输出", EditorStyles.boldLabel);
            _outputFolderIndex = EditorGUILayout.Popup("输出目录", _outputFolderIndex, OutputFolderLabels);
            EditorGUILayout.Space(4);

            EditorGUILayout.LabelField("VFX", EditorStyles.boldLabel);
            _vfxTemplate = (VisualEffectAsset)EditorGUILayout.ObjectField("VFX 模板", _vfxTemplate, typeof(VisualEffectAsset), false);
            EditorGUILayout.Space(8);

            bool hasInput = !string.IsNullOrWhiteSpace(_inputImagePath) && File.Exists(_inputImagePath);
            GUI.enabled = hasInput && _vfxTemplate != null;
            if (GUILayout.Button("运行：图片 → SHARP → PLY → 预制体", GUILayout.Height(32)))
                RunPipeline();
            GUI.enabled = true;

            if (!string.IsNullOrEmpty(_lastError))
            {
                EditorGUILayout.Space(6);
                EditorGUILayout.HelpBox(_lastError, MessageType.Error);
            }

            EditorGUILayout.EndScrollView();
        }

        void RunPipeline()
        {
            _lastError = null;

            if (string.IsNullOrWhiteSpace(_inputImagePath) || !File.Exists(_inputImagePath))
            {
                _lastError = "请选择输入图片（点击“浏览”）。";
                return;
            }
            if (_vfxTemplate == null)
            {
                _lastError = "请指定 VFX 模板（例如 PointCloud_Template）。";
                return;
            }

            int idx = Mathf.Clamp(_outputFolderIndex, 0, OutputFolderPaths.Length - 1);
            string outputDirAsset = OutputFolderPaths[idx];
            string finalDataDir = Path.Combine(Application.dataPath, "..", outputDirAsset).Replace("\\", "/");
            finalDataDir = Path.GetFullPath(finalDataDir);

            string baseName = Path.GetFileNameWithoutExtension(_inputImagePath);
            if (string.IsNullOrEmpty(baseName)) baseName = "input";

            // 使用临时目录运行 SHARP，不把输入图片复制到 Data 目录
            string tempDir = Path.Combine(Application.temporaryCachePath, "SharpTemp_" + System.Guid.NewGuid().ToString("N").Substring(0, 8));
            if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);
            string tempImagePath = Path.Combine(tempDir, Path.GetFileName(_inputImagePath));
            File.Copy(_inputImagePath, tempImagePath, true);

            string sharpExe = ResolveSharpExecutable();
            if (sharpExe == null)
            {
                _lastError = "未找到 sharp：通过 shell 的 PATH 未解析到。请在终端执行 which sharp 确认可用，并确保 Unity 能调用到登录 shell。";
                try { Directory.Delete(tempDir, true); } catch { }
                return;
            }
            if (!RunSharpPredict(sharpExe, tempDir, tempDir, out string sharpError))
            {
                _lastError = "SHARP 运行失败：" + sharpError;
                try { Directory.Delete(tempDir, true); } catch { }
                return;
            }

            string plyPathOnDisk = Path.Combine(tempDir, baseName + ".ply");
            if (!File.Exists(plyPathOnDisk))
            {
                _lastError = "SHARP 生成后未找到 PLY：" + plyPathOnDisk;
                return;
            }

            string assetsOutputDir = outputDirAsset.TrimEnd('/', '\\').Replace("\\", "/");
            string targetPlyAssetPath = Path.Combine(assetsOutputDir, baseName + ".ply").Replace("\\", "/");

            EnsureFolderRecursive(assetsOutputDir);
            File.Copy(plyPathOnDisk, Path.Combine(Application.dataPath, "..", targetPlyAssetPath).Replace("\\", "/"), true);

            // 清理临时目录（删除输入图片和临时PLY）
            try { Directory.Delete(tempDir, true); } catch { }

            AssetDatabase.ImportAsset(targetPlyAssetPath, ImportAssetOptions.ForceUpdate);

            // C: 设置 PLY 为 Texture 容器并重新导入
            if (!SetPlyContainerTypeAndReimport(targetPlyAssetPath, out string importError))
            {
                _lastError = "PLY 以 Texture 方式导入失败：" + importError;
                return;
            }

            // D: 获取 BakedPointCloud
            var baked = AssetDatabase.LoadAssetAtPath<Pcx.BakedPointCloud>(targetPlyAssetPath);
            if (baked == null)
            {
                _lastError = "未找到 BakedPointCloud：" + targetPlyAssetPath;
                return;
            }

            if (baked.positionMap == null || baked.colorMap == null)
            {
                _lastError = "BakedPointCloud 缺少 positionMap 或 colorMap。";
                return;
            }

            // E: 按模版复制为新 VFX 文件（PointCloud_输入图片名.vfx），保存到与 Output Directory 对应的 VFX 目录
            string templatePath = AssetDatabase.GetAssetPath(_vfxTemplate);
            if (string.IsNullOrEmpty(templatePath))
            {
                _lastError = "未能获取 VFX 模板资源路径。";
                return;
            }
            int vfxIdx = Mathf.Clamp(idx, 0, VFXFolderPaths.Length - 1);
            string vfxDir = VFXFolderPaths[vfxIdx].Replace("\\", "/");
            string newVfxPath = vfxDir + "/PointCloud_" + baseName + ".vfx";
            EnsureFolderRecursive(vfxDir);
            if (!AssetDatabase.CopyAsset(templatePath, newVfxPath))
            {
                _lastError = "复制 VFX 模板失败：" + newVfxPath;
                return;
            }
            AssetDatabase.ImportAsset(newVfxPath, ImportAssetOptions.ForceUpdate);
            var newVfxAsset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(newVfxPath);
            if (newVfxAsset == null)
            {
                _lastError = "加载新 VFX 资源失败：" + newVfxPath;
                return;
            }

            // F: 创建 Prefab，引用新 VFX，并挂上 PointCloudController
            if (!CreatePrefabWithVFX(baked, baseName, idx, newVfxAsset, out string prefabError, out string prefabPath))
            {
                _lastError = "创建 Prefab 失败：" + prefabError;
                return;
            }

            _lastError = null;
            EditorUtility.DisplayDialog("完成", "流程完成。\nVFX: " + newVfxPath + "\nPLY: " + targetPlyAssetPath + "\nPrefab: " + prefabPath, "确定");
        }

        /// <summary>通过登录 shell 的 which sharp 解析路径（兼容 conda、PATH 中的 sharp），失败则返回 null。</summary>
        static string ResolveSharpExecutable()
        {
            string whichPath = RunWhichSharp();
            if (!string.IsNullOrWhiteSpace(whichPath) && File.Exists(whichPath))
                return whichPath.Trim();
            return null;
        }

        static string RunWhichSharp()
        {
            try
            {
                string shell = Environment.OSVersion.Platform == PlatformID.Win32NT ? "cmd.exe" : "/bin/bash";
                string args = Environment.OSVersion.Platform == PlatformID.Win32NT
                    ? "/c where sharp"
                    : "-lc \"which sharp 2>/dev/null\"";
                var startInfo = new ProcessStartInfo
                {
                    FileName = shell,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using (var p = Process.Start(startInfo))
                {
                    if (p == null) return null;
                    string outLine = p.StandardOutput.ReadLine();
                    p.WaitForExit(2000);
                    return p.ExitCode == 0 && !string.IsNullOrWhiteSpace(outLine) ? outLine.Trim() : null;
                }
            }
            catch { return null; }
        }

        static bool RunSharpPredict(string sharpExe, string inputDir, string outputDir, out string error)
        {
            error = null;
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = sharpExe,
                    Arguments = $"predict -i \"{inputDir}\" -o \"{outputDir}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = outputDir
                };

                using (var p = Process.Start(startInfo))
                {
                    if (p == null)
                    {
                        error = "无法启动 sharp。请确认已加入 PATH（pip install ml-sharp）。";
                        return false;
                    }
                    string stderr = p.StandardError.ReadToEnd();
                    p.WaitForExit(120000);
                    if (p.ExitCode != 0)
                    {
                        error = string.IsNullOrEmpty(stderr) ? "退出码 " + p.ExitCode : stderr;
                        return false;
                    }
                }
                return true;
            }
            catch (System.Exception e)
            {
                error = e.Message;
                return false;
            }
        }

        static bool SetPlyContainerTypeAndReimport(string plyAssetPath, out string error)
        {
            error = null;
            var importer = AssetImporter.GetAtPath(plyAssetPath) as UnityEditor.AssetImporters.ScriptedImporter;
            if (importer == null)
            {
                error = "不是 ScriptedImporter 或未找到 PLY：" + plyAssetPath;
                return false;
            }
            var so = new SerializedObject(importer);
            var prop = so.FindProperty("_containerType");
            if (prop == null)
            {
                error = "PlyImporter 缺少 _containerType。";
                return false;
            }
            prop.intValue = PlyContainerTypeTexture;
            so.ApplyModifiedPropertiesWithoutUndo();
            importer.SaveAndReimport();
            return true;
        }

        bool CreatePrefabWithVFX(Pcx.BakedPointCloud baked, string baseName, int folderIndex, VisualEffectAsset vfxAsset, out string error, out string outPrefabPath)
        {
            error = null;
            outPrefabPath = null;
            int pi = Mathf.Clamp(folderIndex, 0, PrefabFolderPaths.Length - 1);
            string prefabDir = PrefabFolderPaths[pi].Replace("\\", "/");
            string prefabName = "PointCloud_" + baseName;
            string prefabPath = prefabDir + "/" + prefabName + ".prefab";

            EnsureFolderRecursive(prefabDir);

            var go = new GameObject(prefabName);
            var vfx = go.AddComponent<VisualEffect>();
            vfx.visualEffectAsset = vfxAsset;
            vfx.SetTexture(PositionMapName, baked.positionMap);
            vfx.SetTexture(ColorMapName, baked.colorMap);
            vfx.SetFloat("AssembleWeight", 1f);
            var controller = go.AddComponent<PointCloudController>();
            controller.vfx = vfx;

            Undo.RegisterCreatedObjectUndo(go, "创建点云预制体");
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            DestroyImmediate(go);
            if (prefab == null)
            {
                error = "保存预制体失败：" + prefabPath;
                return false;
            }

            // 确保 Prefab 中的 VisualEffect 参数被正确保存
            var prefabVfx = prefab.GetComponent<VisualEffect>();
            if (prefabVfx != null)
            {
                prefabVfx.SetTexture(PositionMapName, baked.positionMap);
                prefabVfx.SetTexture(ColorMapName, baked.colorMap);
                prefabVfx.SetFloat("AssembleWeight", 1f);
                EditorUtility.SetDirty(prefab);
            }
            var prefabController = prefab.GetComponent<PointCloudController>();
            if (prefabController != null && prefabVfx != null)
            {
                prefabController.vfx = prefabVfx;
                EditorUtility.SetDirty(prefab);
            }

            AssetDatabase.SaveAssets();
            outPrefabPath = prefabPath;
            return true;
        }

        static void EnsureFolderRecursive(string path)
        {
            path = path.Replace("\\", "/").TrimEnd('/');
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/"))
                return;
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
