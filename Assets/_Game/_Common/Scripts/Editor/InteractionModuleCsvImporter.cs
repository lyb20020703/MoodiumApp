using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Interaction;
using UnityEditor;
using UnityEngine;
using VFXViewer;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace VFXViewer.Editor
{
    /// <summary>
    /// 从 docs/interaction_modules_p00_p07.csv 生成模块 Prefab，并回写 ChapterPlacementPlan.asset。
    /// </summary>
    public static class InteractionModuleCsvImporter
    {
        private const string CsvRelativePath = "docs/interaction_modules_p00_p07.csv";
        private const string XlsxRelativePath = "docs/interaction_modules_p00_p07_feishu.xlsx";
        private const string PlanAssetPath = "Assets/_Game/_Common/Config/ChapterPlacementPlan.asset";
        private const string GeneratedFolderName = "GeneratedModules";
        private const float DefaultSpawnDistance = 1f;
        private const float DefaultTransitionDuration = 0.6f;

        [MenuItem("工具/交互/从飞书同步并生成模块与ChapterPlan", priority = 219)]
        public static void SyncFromFeishuAndImport()
        {
            if (!FeishuDocxTableSync.TrySyncInteractionTableToXlsx(out var outputTablePath, out var error))
            {
                Debug.LogError($"[InteractionModuleCsvImporter] 飞书同步失败: {error}");
                return;
            }

            Debug.Log($"[InteractionModuleCsvImporter] 飞书表格同步完成: {outputTablePath}");

            try
            {
                var rows = ReadRowsFromTableFile(outputTablePath);
                ApplyRows(rows);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[InteractionModuleCsvImporter] 执行失败: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private static void ImportFromCsv()
        {
            try
            {
                string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
                if (string.IsNullOrEmpty(projectRoot))
                {
                    Debug.LogError("[InteractionModuleCsvImporter] 无法定位项目根目录。");
                    return;
                }

                string xlsxPath = Path.Combine(projectRoot, XlsxRelativePath);
                string csvPath = Path.Combine(projectRoot, CsvRelativePath);

                List<ModuleRow> rows = null;
                if (File.Exists(xlsxPath))
                {
                    rows = ReadRowsFromXlsx(xlsxPath);
                    Debug.Log($"[InteractionModuleCsvImporter] 已读取 XLSX: {xlsxPath}");
                }
                else if (File.Exists(csvPath))
                {
                    rows = ReadRowsFromCsv(csvPath);
                    Debug.Log($"[InteractionModuleCsvImporter] 已读取 CSV: {csvPath}");
                }
                else
                {
                    Debug.LogError($"[InteractionModuleCsvImporter] 未找到输入文件：{xlsxPath} 或 {csvPath}");
                    return;
                }

                if (rows.Count == 0)
                {
                    Debug.LogWarning("[InteractionModuleCsvImporter] 表格无有效行。");
                    return;
                }

                ApplyRows(rows);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[InteractionModuleCsvImporter] 执行失败: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private static void ApplyRows(List<ModuleRow> rows)
        {
            if (rows == null || rows.Count == 0)
            {
                Debug.LogWarning("[InteractionModuleCsvImporter] 表格无有效行。");
                return;
            }

            var prefabByModuleId = GeneratePrefabs(rows);
            UpdatePlacementPlan(rows, prefabByModuleId);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[InteractionModuleCsvImporter] 完成：{prefabByModuleId.Count} 个模块 Prefab，ChapterPlacementPlan 已更新。");
        }

        private static Dictionary<string, GameObject> GeneratePrefabs(List<ModuleRow> rows)
        {
            var result = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                string chapterFolder = ResolveChapterFolder(row.chapterId);
                string prefabFolder = $"{chapterFolder}/Prefabs/{GeneratedFolderName}";
                EnsureAssetFolder(prefabFolder);

                string prefabPath = $"{prefabFolder}/{row.moduleId}.prefab";
                var prefab = CreateOrUpdateModulePrefab(prefabPath, row);
                if (prefab != null)
                    result[row.moduleId] = prefab;
            }
            return result;
        }

        private static GameObject CreateOrUpdateModulePrefab(string prefabPath, ModuleRow row)
        {
            bool loadExisting = File.Exists(prefabPath);
            GameObject root = null;
            try
            {
                root = loadExisting
                    ? PrefabUtility.LoadPrefabContents(prefabPath)
                    : new GameObject(row.moduleId);
                root.name = row.moduleId;

                // 基础标识，供 Sequencer / 布展系统识别。
                var info = root.GetComponent<ExhibitInfo>();
                if (info == null)
                    info = root.AddComponent<ExhibitInfo>();
                info.moduleId = row.moduleId;
                info.displayName = string.IsNullOrWhiteSpace(row.moduleName) ? row.moduleId : row.moduleName;
                info.instanceID = string.Empty;

                // 导游目标点。
                var waypointGo = FindChildByName(root.transform, "GuideWaypoint");
                if (waypointGo == null)
                {
                    waypointGo = new GameObject("GuideWaypoint");
                    waypointGo.transform.SetParent(root.transform, false);
                    waypointGo.transform.localPosition = new Vector3(0f, 0f, 0.8f);
                }
                if (waypointGo.GetComponent<GuideWaypoint>() == null)
                    waypointGo.AddComponent<GuideWaypoint>();

                // 触发占位区域 / 交互体。
                Collider enterCol = FindRegionCollider(root.transform, "StartRegion");
                Collider leaveCol = FindRegionCollider(root.transform, "EndRegion");
                XRBaseInteractable interactable = FindTriggerInteractable(root.transform);

                if (row.startTrigger == TriggerType.Enter || row.startTrigger == TriggerType.Leave)
                {
                    if (enterCol == null)
                        enterCol = CreateRegionCollider(root.transform, "StartRegion", row.startParam);
                }
                if (row.endTrigger == TriggerType.Enter || row.endTrigger == TriggerType.Leave)
                {
                    if (leaveCol == null)
                        leaveCol = CreateRegionCollider(root.transform, "EndRegion", row.endParam);
                }
                if (NeedsInteractable(row))
                {
                    if (interactable == null)
                        interactable = CreateTriggerInteractable(root.transform);
                }

                var module = root.GetComponent<InteractionModule>();
                if (module == null)
                    module = root.AddComponent<InteractionModule>();
                var so = new SerializedObject(module);

                // 占位内容（保证 InteractionModule 列表模式可运行）。
                var contentGo = ResolveOrCreateContentRoot(root.transform, so, row);

                so.FindProperty("startTriggerType").intValue = (int)row.startTrigger;
                so.FindProperty("endTriggerType").intValue = (int)row.endTrigger;
                so.FindProperty("allowRestartAfterEnd").boolValue = row.allowRestartAfterEnd;

                // 开始参数
                var startTimeDelay = so.FindProperty("startTimeDelay");
                if (startTimeDelay != null)
                    startTimeDelay.floatValue = row.startTrigger == TriggerType.Time ? ParseDuration(row.startParam, 1f) : 0f;
                var enterRegion = so.FindProperty("enterRegionCollider");
                if (enterRegion != null)
                    enterRegion.objectReferenceValue = enterCol;
                var touchInteractable = so.FindProperty("touchInteractable");
                if (touchInteractable != null)
                    touchInteractable.objectReferenceValue = row.startTrigger == TriggerType.Touch || row.endTrigger == TriggerType.Touch ? interactable : null;
                var startLongPressInteractable = so.FindProperty("startLongPressInteractable");
                if (startLongPressInteractable != null)
                    startLongPressInteractable.objectReferenceValue = row.startTrigger == TriggerType.LongPress ? interactable : null;
                var startLongPressHoldSec = so.FindProperty("startLongPressHoldSec");
                if (startLongPressHoldSec != null)
                    startLongPressHoldSec.floatValue = row.startTrigger == TriggerType.LongPress ? ParseLongPressSeconds(row.startParam, 1.2f) : 1f;

                // 结束参数
                var endTimeDelay = so.FindProperty("endTimeDelay");
                if (endTimeDelay != null)
                    endTimeDelay.floatValue = row.endTrigger == TriggerType.Time ? ParseDuration(row.endParam, 1f) : 0f;
                var leaveRegion = so.FindProperty("leaveRegionCollider");
                if (leaveRegion != null)
                    leaveRegion.objectReferenceValue = leaveCol;
                var endLongPressInteractable = so.FindProperty("endLongPressInteractable");
                if (endLongPressInteractable != null)
                    endLongPressInteractable.objectReferenceValue = row.endTrigger == TriggerType.LongPress ? interactable : null;
                var endLongPressHoldSec = so.FindProperty("endLongPressHoldSec");
                if (endLongPressHoldSec != null)
                    endLongPressHoldSec.floatValue = row.endTrigger == TriggerType.LongPress ? ParseLongPressSeconds(row.endParam, 1.2f) : 1f;

                // 开始效果列表：已有 prefab 时仅补齐缺失字段，避免覆盖已有条目配置。
                var appearEntries = so.FindProperty("appearEffectEntries");
                if (appearEntries != null)
                {
                    if (appearEntries.arraySize <= 0)
                        appearEntries.arraySize = 1;

                    var e = appearEntries.GetArrayElementAtIndex(0);
                    var contentRootProp = e.FindPropertyRelative("contentRoot");
                    if (contentRootProp != null && contentRootProp.objectReferenceValue == null)
                        contentRootProp.objectReferenceValue = contentGo;

                    if (!loadExisting)
                    {
                        e.FindPropertyRelative("delay").floatValue = 0f;
                        e.FindPropertyRelative("initialHidden").boolValue = true;
                        e.FindPropertyRelative("duration").floatValue = DefaultTransitionDuration;
                        e.FindPropertyRelative("effectType").enumValueIndex = (int)AppearEffectType.Show;
                    }
                }

                // 结束效果列表：已有 prefab 时仅补齐缺失字段，避免覆盖已有条目配置。
                var disappearEntries = so.FindProperty("disappearEffectEntries");
                if (disappearEntries != null)
                {
                    if (disappearEntries.arraySize <= 0)
                        disappearEntries.arraySize = 1;

                    var e = disappearEntries.GetArrayElementAtIndex(0);
                    var contentRootProp = e.FindPropertyRelative("contentRoot");
                    if (contentRootProp != null && contentRootProp.objectReferenceValue == null)
                        contentRootProp.objectReferenceValue = contentGo;

                    if (!loadExisting)
                    {
                        e.FindPropertyRelative("delay").floatValue = 0f;
                        e.FindPropertyRelative("duration").floatValue = DefaultTransitionDuration;
                        e.FindPropertyRelative("effectType").enumValueIndex = (int)DisappearEffectType.Hide;
                    }
                }

                so.ApplyModifiedPropertiesWithoutUndo();

                var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                return prefab;
            }
            finally
            {
                if (root != null)
                {
                    if (loadExisting)
                        PrefabUtility.UnloadPrefabContents(root);
                    else
                        UnityEngine.Object.DestroyImmediate(root);
                }
            }
        }

        private static GameObject ResolveOrCreateContentRoot(Transform root, SerializedObject moduleSo, ModuleRow row)
        {
            string contentName = FirstToken(row.appearSlots, ';');
            GameObject contentGo = null;

            if (!string.IsNullOrWhiteSpace(contentName))
                contentGo = FindChildByName(root, contentName);

            if (contentGo == null)
            {
                var appearEntries = moduleSo.FindProperty("appearEffectEntries");
                if (appearEntries != null && appearEntries.arraySize > 0)
                {
                    var existing = appearEntries.GetArrayElementAtIndex(0)?.FindPropertyRelative("contentRoot")?.objectReferenceValue as GameObject;
                    if (existing != null)
                        contentGo = existing;
                }
            }

            if (contentGo != null)
                return contentGo;

            if (string.IsNullOrWhiteSpace(contentName))
                contentName = $"{root.name}_Content";

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = contentName;
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3(0f, 1f, 0f);
            go.transform.localScale = new Vector3(0.6f, 0.6f, 0.6f);
            return go;
        }

        private static GameObject FindChildByName(Transform root, string name)
        {
            if (root == null || string.IsNullOrWhiteSpace(name)) return null;
            var all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != root && string.Equals(all[i].name, name, StringComparison.Ordinal))
                    return all[i].gameObject;
            }
            return null;
        }

        private static Collider FindRegionCollider(Transform root, string nodeNamePrefix)
        {
            if (root == null || string.IsNullOrWhiteSpace(nodeNamePrefix)) return null;
            var all = root.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < all.Length; i++)
            {
                var col = all[i];
                if (col == null) continue;
                if (col.transform == root) continue;
                if (!col.name.StartsWith(nodeNamePrefix, StringComparison.OrdinalIgnoreCase)) continue;
                if (col is BoxCollider box && box.isTrigger)
                    return col;
            }
            return null;
        }

        private static XRBaseInteractable FindTriggerInteractable(Transform root)
        {
            if (root == null) return null;
            var all = root.GetComponentsInChildren<XRBaseInteractable>(true);
            for (int i = 0; i < all.Length; i++)
            {
                var it = all[i];
                if (it == null) continue;
                if (string.Equals(it.name, "TriggerInteractable", StringComparison.Ordinal))
                    return it;
            }
            return null;
        }

        private static void UpdatePlacementPlan(List<ModuleRow> rows, Dictionary<string, GameObject> prefabByModuleId)
        {
            var plan = AssetDatabase.LoadAssetAtPath<ChapterPlacementPlan>(PlanAssetPath);
            if (plan == null)
            {
                Debug.LogError($"[InteractionModuleCsvImporter] 未找到 ChapterPlacementPlan: {PlanAssetPath}");
                return;
            }

            var chapters = rows
                .GroupBy(r => r.chapterId)
                .Select(g => new
                {
                    chapterId = g.Key,
                    chapterName = g.First().chapterName,
                    steps = g.OrderBy(x => x.moduleOrder).ToList()
                })
                .OrderBy(x => x.chapterId, StringComparer.OrdinalIgnoreCase)
                .ToList();

            plan.chapters = new List<ChapterPlacementChapter>();
            foreach (var chapter in chapters)
            {
                var c = new ChapterPlacementChapter
                {
                    chapterId = chapter.chapterId,
                    chapterName = chapter.chapterName,
                    steps = new List<ChapterPlacementStep>()
                };

                foreach (var step in chapter.steps)
                {
                    prefabByModuleId.TryGetValue(step.moduleId, out var prefab);
                    c.steps.Add(new ChapterPlacementStep
                    {
                        moduleId = step.moduleId,
                        moduleName = string.IsNullOrWhiteSpace(step.moduleName) ? step.moduleId : step.moduleName,
                        prefab = prefab,
                        spawnDistance = DefaultSpawnDistance,
                        fixedInstanceId = string.Empty
                    });
                }

                plan.chapters.Add(c);
            }

            EditorUtility.SetDirty(plan);
        }

        private static bool NeedsInteractable(ModuleRow row)
        {
            return row.startTrigger == TriggerType.Touch
                   || row.startTrigger == TriggerType.LongPress
                   || row.endTrigger == TriggerType.Touch
                   || row.endTrigger == TriggerType.LongPress;
        }

        private static Collider CreateRegionCollider(Transform root, string nodeName, string sourceToken)
        {
            var go = new GameObject(nodeName);
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3(0f, 1f, 0f);
            go.transform.localScale = new Vector3(1.5f, 2f, 1.5f);
            var col = go.AddComponent<BoxCollider>();
            col.isTrigger = true;
            if (!string.IsNullOrWhiteSpace(sourceToken))
                go.name = $"{nodeName}_{SanitizeName(sourceToken)}";
            return col;
        }

        private static XRBaseInteractable CreateTriggerInteractable(Transform root)
        {
            var go = new GameObject("TriggerInteractable");
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3(0f, 1f, 0.5f);
            var col = go.AddComponent<BoxCollider>();
            col.size = new Vector3(0.5f, 0.5f, 0.5f);
            return go.AddComponent<XRGrabInteractable>();
        }

        private static List<ModuleRow> ReadRows(string csvPath)
        {
            var lines = File.ReadAllLines(csvPath, Encoding.UTF8)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToList();
            if (lines.Count <= 1) return new List<ModuleRow>();

            var table = new List<List<string>>(lines.Count);
            for (int i = 0; i < lines.Count; i++)
                table.Add(ParseCsvLine(lines[i]));
            return ParseRowsFromTable(table);
        }

        private static List<ModuleRow> ReadRowsFromCsv(string csvPath)
        {
            return ReadRows(csvPath);
        }

        private static List<ModuleRow> ReadRowsFromTableFile(string tablePath)
        {
            string ext = Path.GetExtension(tablePath);
            if (string.Equals(ext, ".xlsx", StringComparison.OrdinalIgnoreCase))
                return ReadRowsFromXlsx(tablePath);
            if (string.Equals(ext, ".csv", StringComparison.OrdinalIgnoreCase))
                return ReadRowsFromCsv(tablePath);

            throw new InvalidOperationException($"不支持的表格格式: {tablePath}");
        }

        private static List<ModuleRow> ReadRowsFromXlsx(string xlsxPath)
        {
            using var stream = File.OpenRead(xlsxPath);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);

            var sheetEntry = zip.GetEntry("xl/worksheets/sheet1.xml");
            if (sheetEntry == null) return new List<ModuleRow>();

            var shared = ReadSharedStrings(zip);
            var table = new List<List<string>>();

            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            using (var sr = sheetEntry.Open())
            {
                var doc = XDocument.Load(sr);
                var rows = doc.Descendants(ns + "sheetData").Elements(ns + "row");
                foreach (var row in rows)
                {
                    var dict = new Dictionary<int, string>();
                    int maxCol = 0;
                    foreach (var cell in row.Elements(ns + "c"))
                    {
                        var cellRef = (string)cell.Attribute("r");
                        int col = ColumnIndexFromCellRef(cellRef);
                        if (col <= 0) continue;

                        string t = (string)cell.Attribute("t");
                        string value;
                        if (t == "s")
                        {
                            string idxText = cell.Element(ns + "v")?.Value;
                            if (int.TryParse(idxText, out var sIdx) && sIdx >= 0 && sIdx < shared.Count)
                                value = shared[sIdx];
                            else
                                value = string.Empty;
                        }
                        else if (t == "inlineStr")
                        {
                            value = cell.Element(ns + "is")?.Element(ns + "t")?.Value ?? string.Empty;
                        }
                        else
                        {
                            value = cell.Element(ns + "v")?.Value ?? string.Empty;
                        }

                        dict[col] = value;
                        if (col > maxCol) maxCol = col;
                    }

                    if (maxCol <= 0) continue;
                    var cols = new List<string>(maxCol);
                    for (int c = 1; c <= maxCol; c++)
                        cols.Add(dict.TryGetValue(c, out var v) ? v : string.Empty);
                    table.Add(cols);
                }
            }

            return ParseRowsFromTable(table);
        }

        private static List<string> ReadSharedStrings(ZipArchive zip)
        {
            var result = new List<string>();
            var entry = zip.GetEntry("xl/sharedStrings.xml");
            if (entry == null) return result;

            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            using var s = entry.Open();
            var doc = XDocument.Load(s);
            var items = doc.Descendants(ns + "si");
            foreach (var si in items)
            {
                // 兼容富文本：把所有 t 拼起来
                var texts = si.Descendants(ns + "t").Select(x => x.Value);
                result.Add(string.Concat(texts));
            }
            return result;
        }

        private static int ColumnIndexFromCellRef(string cellRef)
        {
            if (string.IsNullOrWhiteSpace(cellRef)) return 0;
            int col = 0;
            for (int i = 0; i < cellRef.Length; i++)
            {
                char ch = cellRef[i];
                if (ch >= 'A' && ch <= 'Z')
                {
                    col = col * 26 + (ch - 'A' + 1);
                }
                else if (ch >= 'a' && ch <= 'z')
                {
                    col = col * 26 + (ch - 'a' + 1);
                }
                else
                {
                    break;
                }
            }
            return col;
        }

        private static List<ModuleRow> ParseRowsFromTable(List<List<string>> table)
        {
            if (table == null || table.Count <= 1) return new List<ModuleRow>();

            var header = table[0] ?? new List<string>();
            int idxChapterId = IndexOf(header, "chapter_id");
            int idxChapterName = IndexOf(header, "chapter_name");
            int idxModuleOrder = IndexOf(header, "module_order");
            int idxModuleId = IndexOf(header, "module_id");
            int idxModuleName = IndexOf(header, "module_name");
            int idxStartTrigger = IndexOf(header, "start_trigger");
            int idxStartParam = IndexOf(header, "start_param");
            int idxEndTrigger = IndexOf(header, "end_trigger");
            int idxEndParam = IndexOf(header, "end_param");
            int idxAllowRestart = IndexOf(header, "allow_restart_after_end");
            int idxAppearSlots = IndexOf(header, "appear_slots");
            int idxDisappearSlots = IndexOf(header, "disappear_slots");
            int idxNotes = IndexOf(header, "notes");

            var rows = new List<ModuleRow>();
            for (int i = 1; i < table.Count; i++)
            {
                var cols = table[i] ?? new List<string>();
                if (cols.Count == 0) continue;

                string moduleId = GetCol(cols, idxModuleId);
                if (string.IsNullOrWhiteSpace(moduleId)) continue;

                rows.Add(new ModuleRow
                {
                    chapterId = GetCol(cols, idxChapterId),
                    chapterName = GetCol(cols, idxChapterName),
                    moduleOrder = ParseInt(GetCol(cols, idxModuleOrder), i),
                    moduleId = moduleId.Trim(),
                    moduleName = GetCol(cols, idxModuleName),
                    startTrigger = ParseTrigger(GetCol(cols, idxStartTrigger)),
                    startParam = GetCol(cols, idxStartParam),
                    endTrigger = ParseTrigger(GetCol(cols, idxEndTrigger)),
                    endParam = GetCol(cols, idxEndParam),
                    allowRestartAfterEnd = ParseBool(GetCol(cols, idxAllowRestart), false),
                    appearSlots = GetCol(cols, idxAppearSlots),
                    disappearSlots = GetCol(cols, idxDisappearSlots),
                    notes = GetCol(cols, idxNotes)
                });
            }

            return rows
                .OrderBy(r => r.chapterId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.moduleOrder)
                .ToList();
        }

        private static string ResolveChapterFolder(string chapterId)
        {
            string chaptersAbs = Path.Combine(Application.dataPath, "_Game/Chapters");
            if (Directory.Exists(chaptersAbs))
            {
                string[] dirs = Directory.GetDirectories(chaptersAbs, $"{chapterId}_*");
                if (dirs.Length > 0)
                {
                    string name = Path.GetFileName(dirs[0]);
                    return $"Assets/_Game/Chapters/{name}";
                }
            }

            string fallback = $"Assets/_Game/Chapters/{chapterId}_Auto";
            EnsureAssetFolder(fallback);
            return fallback;
        }

        private static void EnsureAssetFolder(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath)) return;

            string[] parts = folderPath.Split('/');
            if (parts.Length < 2 || parts[0] != "Assets") return;

            string current = "Assets";
            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private static TriggerType ParseTrigger(string triggerText)
        {
            if (string.IsNullOrWhiteSpace(triggerText)) return TriggerType.Time;
            switch (triggerText.Trim().ToLowerInvariant())
            {
                case "time": return TriggerType.Time;
                case "enter": return TriggerType.Enter;
                case "leave": return TriggerType.Leave;
                case "touch": return TriggerType.Touch;
                case "longpress": return TriggerType.LongPress;
                case "touchguide":
                case "touch_guide":
                case "guide_touch":
                    return TriggerType.TouchGuide;
                case "none":
                default:
                    return TriggerType.Time;
            }
        }

        private static float ParseDuration(string value, float fallback)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            if (float.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return Mathf.Max(0f, v);
            return fallback;
        }

        private static float ParseLongPressSeconds(string value, float fallback)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            int sep = value.IndexOf('|');
            string tail = sep >= 0 ? value.Substring(sep + 1) : value;
            if (float.TryParse(tail.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return Mathf.Max(0.01f, v);
            return fallback;
        }

        private static string FirstToken(string value, char sep)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var parts = value.Split(sep);
            if (parts.Length == 0) return string.Empty;
            return parts[0].Trim();
        }

        private static string SanitizeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "Token";
            var sb = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
                    sb.Append(c);
            }
            return sb.Length > 0 ? sb.ToString() : "Token";
        }

        private static bool ParseBool(string value, bool fallback)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            if (bool.TryParse(value.Trim(), out var v)) return v;
            if (value.Trim() == "0") return false;
            if (value.Trim() == "1") return true;
            return fallback;
        }

        private static int ParseInt(string value, int fallback)
        {
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) return v;
            return fallback;
        }

        private static int IndexOf(List<string> header, string colName)
        {
            for (int i = 0; i < header.Count; i++)
            {
                if (string.Equals(header[i], colName, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        private static string GetCol(List<string> cols, int index)
        {
            if (index < 0 || index >= cols.Count) return string.Empty;
            return cols[index];
        }

        private static List<string> ParseCsvLine(string line)
        {
            var result = new List<string>();
            if (line == null) return result;

            var sb = new StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (ch == '"')
                {
                    // 双引号转义："" -> "
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (ch == ',' && !inQuotes)
                {
                    result.Add(sb.ToString());
                    sb.Clear();
                }
                else
                {
                    sb.Append(ch);
                }
            }
            result.Add(sb.ToString());
            return result;
        }

        private class ModuleRow
        {
            public string chapterId;
            public string chapterName;
            public int moduleOrder;
            public string moduleId;
            public string moduleName;
            public TriggerType startTrigger;
            public string startParam;
            public TriggerType endTrigger;
            public string endParam;
            public bool allowRestartAfterEnd;
            public string appearSlots;
            public string disappearSlots;
            public string notes;
        }
    }
}
