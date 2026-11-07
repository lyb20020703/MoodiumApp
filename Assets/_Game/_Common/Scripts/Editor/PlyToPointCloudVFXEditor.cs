// PLY → PCX BakedPointCloud → PointCloud VFX Prefab

using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.VFX;
using Pcx;

namespace Game.Editor
{
    public class PlyToPointCloudVFXEditor : EditorWindow
    {
        const int PlyContainerTypeTexture = 2; // Pcx.PlyImporter.ContainerType.Texture
        const string PositionMapName = "PositionMap";
        const string ColorMapName = "ColorMap";

        // 与 ImageToPointCloudVFXEditor 保持一致的目录映射
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

        [SerializeField] string _inputPlyPath = "";
        [SerializeField] int _outputFolderIndex = 0;
        [SerializeField] VisualEffectAsset _vfxTemplate;

        string _lastError;
        Vector2 _scroll;

        [MenuItem("工具/PLY转点云VFX")]
        static void Open()
        {
            var w = GetWindow<PlyToPointCloudVFXEditor>(false, "PLY → 点云VFX");
            w.minSize = new Vector2(380, 260);
        }

        void OnEnable()
        {
            if (_vfxTemplate == null)
                _vfxTemplate = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>("Assets/_Game/_Common/VFX/PointCloud_Template.vfx");
        }

        void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            // Input
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("输入 PLY", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            _inputPlyPath = EditorGUILayout.TextField(_inputPlyPath);
            if (GUILayout.Button("浏览", GUILayout.Width(60)))
            {
                string path = EditorUtility.OpenFilePanel("选择 PLY", string.IsNullOrEmpty(_inputPlyPath) ? "" : Path.GetDirectoryName(_inputPlyPath), "ply");
                if (!string.IsNullOrEmpty(path))
                    _inputPlyPath = path;
            }
            EditorGUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(_inputPlyPath) && !File.Exists(_inputPlyPath))
                EditorGUILayout.HelpBox("文件不存在。", MessageType.Warning);
            EditorGUILayout.Space(4);

            // Output
            EditorGUILayout.LabelField("输出", EditorStyles.boldLabel);
            _outputFolderIndex = EditorGUILayout.Popup("输出目录", _outputFolderIndex, OutputFolderLabels);
            EditorGUILayout.Space(4);

            // VFX
            EditorGUILayout.LabelField("VFX", EditorStyles.boldLabel);
            _vfxTemplate = (VisualEffectAsset)EditorGUILayout.ObjectField("VFX 模板", _vfxTemplate, typeof(VisualEffectAsset), false);
            EditorGUILayout.Space(8);

            bool hasInput = !string.IsNullOrWhiteSpace(_inputPlyPath) &&
                            File.Exists(_inputPlyPath) &&
                            _inputPlyPath.EndsWith(".ply", StringComparison.OrdinalIgnoreCase);

            GUI.enabled = hasInput && _vfxTemplate != null;
            if (GUILayout.Button("运行：PLY → VFX 预制体", GUILayout.Height(32)))
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

            if (string.IsNullOrWhiteSpace(_inputPlyPath) ||
                !File.Exists(_inputPlyPath) ||
                !_inputPlyPath.EndsWith(".ply", StringComparison.OrdinalIgnoreCase))
            {
                _lastError = "请选择有效的 .ply 文件（点击“浏览”）。";
                return;
            }

            if (_vfxTemplate == null)
            {
                _lastError = "请指定 VFX 模板（例如 PointCloud_Template）。";
                return;
            }

            int idx = Mathf.Clamp(_outputFolderIndex, 0, OutputFolderPaths.Length - 1);
            string dataDirAsset = OutputFolderPaths[idx];
            string dataDirFull = Path.GetFullPath(Path.Combine(Application.dataPath, "..", dataDirAsset));

            string fileName = Path.GetFileName(_inputPlyPath);
            string baseName = Path.GetFileNameWithoutExtension(_inputPlyPath);
            string targetPlyAssetPath = Path.Combine(dataDirAsset, fileName).Replace("\\", "/");
            string targetPlyFull = Path.Combine(Application.dataPath, "..", targetPlyAssetPath).Replace("\\", "/");

            // 确保目标 Data 目录存在
            EnsureFolder(dataDirAsset);

            // 将外部 PLY 拷贝到目标 Data 目录（每次覆盖即可）
            try
            {
                File.Copy(_inputPlyPath, targetPlyFull, true);
                AssetDatabase.ImportAsset(targetPlyAssetPath, ImportAssetOptions.ForceUpdate);
            }
            catch (Exception e)
            {
                _lastError = "拷贝 PLY 失败：" + e.Message;
                return;
            }

            // 设置 PLY 为 Texture 容器并重新导入
            if (!SetPlyContainerTypeAndReimport(targetPlyAssetPath, out string importError))
            {
                _lastError = "PLY 以 Texture 方式导入失败：" + importError;
                return;
            }

            // 获取 BakedPointCloud（BakedPointCloud.Initialize 已在内部做了坐标修正）
            var baked = AssetDatabase.LoadAssetAtPath<BakedPointCloud>(targetPlyAssetPath);
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

            // 基于模板生成新的 VFX 资源
            string templatePath = AssetDatabase.GetAssetPath(_vfxTemplate);
            if (string.IsNullOrEmpty(templatePath))
            {
                _lastError = "未能获取 VFX 模板资源路径。";
                return;
            }

            string vfxDir = VFXFolderPaths[idx].Replace("\\", "/");
            EnsureFolder(vfxDir);
            string newVfxPath = vfxDir + "/PointCloud_" + baseName + ".vfx";
            if (!AssetDatabase.CopyAsset(templatePath, newVfxPath))
            {
                _lastError = "复制 VFX 模板失败：" + newVfxPath;
                return;
            }
            AssetDatabase.ImportAsset(newVfxPath, ImportAssetOptions.ForceUpdate);

            var newVfx = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(newVfxPath);
            if (newVfx == null)
            {
                _lastError = "加载 VFX 资源失败：" + newVfxPath;
                return;
            }

            // 创建 Prefab
            if (!CreatePrefabWithVFX(baked, baseName, idx, newVfx, out string prefabError, out string prefabPath))
            {
                _lastError = "创建 Prefab 失败：" + prefabError;
                return;
            }

            _lastError = null;
            EditorUtility.DisplayDialog("完成",
                "PLY → VFX 预制体完成。\nVFX: " + newVfxPath + "\nPLY: " + targetPlyAssetPath + "\nPrefab: " + prefabPath,
                "确定");
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

        static bool CreatePrefabWithVFX(BakedPointCloud baked, string baseName, int folderIndex,
            VisualEffectAsset vfxAsset, out string error, out string outPrefabPath)
        {
            error = null;
            outPrefabPath = null;

            int pi = Mathf.Clamp(folderIndex, 0, PrefabFolderPaths.Length - 1);
            string prefabDir = PrefabFolderPaths[pi].Replace("\\", "/");
            string prefabName = "PointCloud_" + baseName;
            string prefabPath = prefabDir + "/" + prefabName + ".prefab";

            EnsureFolder(prefabDir);

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
            UnityEngine.Object.DestroyImmediate(go);

            if (prefab == null)
            {
                error = "保存预制体失败：" + prefabPath;
                return false;
            }

            var prefabVfx = prefab.GetComponent<VisualEffect>();
            if (prefabVfx != null)
            {
                prefabVfx.SetTexture(PositionMapName, baked.positionMap);
                prefabVfx.SetTexture(ColorMapName, baked.colorMap);
                prefabVfx.SetFloat("AssembleWeight", 1f);
                EditorUtility.SetDirty(prefab);
            }

            var prefabController = prefab.GetComponent<PointCloudController>();
            if (prefabController != null)
            {
                prefabController.vfx = prefabVfx;
                EditorUtility.SetDirty(prefabController);
            }

            outPrefabPath = prefabPath;
            AssetDatabase.SaveAssets();
            return true;
        }

        static void EnsureFolder(string path)
        {
            path = path.Replace("\\", "/").TrimEnd('/');
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/"))
                return;

            var parts = path.Split('/');
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
