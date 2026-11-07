using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Interaction;

namespace Interaction.Editor
{
    public class InteractionModuleMonitorWindow : EditorWindow
    {
        private Vector2 _scroll;
        private bool _showXZMap = true;
        private bool _autoRefresh = true;
        private float _refreshInterval = 0.5f;
        private double _lastRefreshTime;
        private Vector2 _mapPan;
        private bool _isPanning;
        private float _mapZoom = 1f;

        [MenuItem("Window/Interaction/Module Monitor")]
        public static void ShowWindow()
        {
            var window = GetWindow<InteractionModuleMonitorWindow>("交互模块监控");
            window.minSize = new Vector2(420, 320);
        }

        private void OnEnable()
        {
            _lastRefreshTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
        }

        private void OnEditorUpdate()
        {
            if (!_autoRefresh) return;
            if (EditorApplication.timeSinceStartup - _lastRefreshTime >= _refreshInterval)
            {
                _lastRefreshTime = EditorApplication.timeSinceStartup;
                Repaint();
            }
        }

        private void OnGUI()
        {
            DrawToolbar();
            var modules = FindModules();
            DrawSummary(modules);
            if (_showXZMap)
                DrawXZMap(modules);
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("刷新", EditorStyles.toolbarButton, GUILayout.Width(60)))
                Repaint();
            _showXZMap = GUILayout.Toggle(_showXZMap, "XZ 平面", EditorStyles.toolbarButton);
            _autoRefresh = GUILayout.Toggle(_autoRefresh, "自动刷新", EditorStyles.toolbarButton);
            if (GUILayout.Button("重置视图", EditorStyles.toolbarButton, GUILayout.Width(80)))
            {
                _mapPan = Vector2.zero;
                _mapZoom = 1f;
                Repaint();
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("刷新间隔(秒)", GUILayout.Width(80));
            _refreshInterval = EditorGUILayout.FloatField(_refreshInterval, GUILayout.Width(50));
            if (_refreshInterval < 0.1f) _refreshInterval = 0.1f;
            EditorGUILayout.EndHorizontal();
        }

        private static List<InteractionModule> FindModules()
        {
            var list = new List<InteractionModule>();
            var found = Object.FindObjectsOfType<InteractionModule>(true);
            for (int i = 0; i < found.Length; i++)
                list.Add(found[i]);
            return list;
        }

        private void DrawSummary(List<InteractionModule> modules)
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField($"模块数量: {modules.Count}");
            EditorGUILayout.EndVertical();
        }

        private void DrawXZMap(List<InteractionModule> modules)
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("XZ 平面分布", EditorStyles.boldLabel);

            float mapHeight = Mathf.Max(200f, position.height * 0.5f);
            var rect = GUILayoutUtility.GetRect(10, mapHeight, GUILayout.ExpandWidth(true));
            HandleMapInteraction(rect);
            DrawMapBackground(rect, _mapPan, _mapZoom);
            DrawMapPoints(rect, modules, _mapPan, _mapZoom);

            EditorGUILayout.EndVertical();
        }

        private static void DrawMapBackground(Rect rect, Vector2 pan, float zoom)
        {
            var bg = EditorGUIUtility.isProSkin ? new Color(0.15f, 0.15f, 0.15f, 1f) : new Color(0.9f, 0.9f, 0.9f, 1f);
            EditorGUI.DrawRect(rect, bg);
            Handles.BeginGUI();
            Handles.color = new Color(1f, 1f, 1f, 0.08f);
            float stepX = rect.width / 5f * zoom;
            float stepY = rect.height / 5f * zoom;
            float offsetX = pan.x % stepX;
            float offsetY = pan.y % stepY;
            for (int i = -1; i <= 5; i++)
            {
                float x = rect.x + i * stepX + offsetX;
                float y = rect.y + i * stepY + offsetY;
                Handles.DrawLine(new Vector3(x, rect.y), new Vector3(x, rect.y + rect.height));
                Handles.DrawLine(new Vector3(rect.x, y), new Vector3(rect.x + rect.width, y));
            }
            Handles.EndGUI();
        }

        private static void DrawMapPoints(Rect rect, List<InteractionModule> modules, Vector2 pan, float zoom)
        {
            if (modules.Count == 0) return;
            var bounds = GetBounds(modules);
            float maxSize = Mathf.Max(bounds.size.x, bounds.size.z);
            if (maxSize < 0.01f) maxSize = 1f;
            var center = bounds.center;

            var placed = new List<Rect>();
            Handles.BeginGUI();
            for (int i = 0; i < modules.Count; i++)
            {
                var m = modules[i];
                if (m == null) continue;
                Vector3 pos = m.transform.position;
                float nx = (pos.x - center.x) / maxSize * 0.5f + 0.5f;
                float nz = (pos.z - center.z) / maxSize * 0.5f + 0.5f;
                float x = rect.x + (nx * rect.width - rect.width * 0.5f) * zoom + rect.width * 0.5f + pan.x;
                float y = rect.y + ((1f - nz) * rect.height - rect.height * 0.5f) * zoom + rect.height * 0.5f + pan.y;

                string phase = Application.isPlaying ? GetLocalizedPhaseName(m.CurrentPhase) : "未运行";
                string label =
                    $"{m.name}\n" +
                    $"初始内容: {m.InitialEntryCount} | 开始触发: {GetLocalizedTriggerName(m.StartTriggerType)}\n" +
                    $"开始内容: {m.AppearEntryCount} | 结束触发: {GetLocalizedTriggerName(m.EndTriggerType)}\n" +
                    $"结束内容: {m.DisappearEntryCount}\n" +
                    $"循环: {(m.AllowRestartAfterEnd ? "是" : "否")} | 阶段: {phase}";

                int fontSize = 16;
                var style = new GUIStyle(EditorStyles.label)
                {
                    fontSize = fontSize,
                    normal = { textColor = Color.white }
                };
                float minW = 220f;
                float maxW = 320f;
                float pad = 6f;
                var content = new GUIContent(label);
                float textWidth = Mathf.Clamp(style.CalcSize(content).x + pad * 2f, minW, maxW);
                float textHeight = style.CalcHeight(content, textWidth - pad * 2f) + pad * 2f;
                float minH = 90f;
                float baseH = Mathf.Max(textHeight, minH);
                float baseW = textWidth;
                float w = baseW;
                float h = baseH;
                var boxRect = new Rect(x + pad, y - pad, w, h);
                var phaseColor = GetPhaseColor(m);
                var bg = new Color(phaseColor.r * 0.25f + 0.1f, phaseColor.g * 0.25f + 0.1f, phaseColor.b * 0.25f + 0.1f, 0.95f);
                var border = new Color(phaseColor.r, phaseColor.g, phaseColor.b, 0.9f);
                Handles.BeginGUI();
                Matrix4x4 prev = GUI.matrix;
                var pivot = new Vector2(x, y);
                GUI.matrix = Matrix4x4.TRS(pivot, Quaternion.identity, new Vector3(zoom, zoom, 1f)) * Matrix4x4.TRS(-pivot, Quaternion.identity, Vector3.one);

                var boxClipped = ClipRect(boxRect, rect);
                if (boxClipped.width > 1f && boxClipped.height > 1f)
                {
                    EditorGUI.DrawRect(boxClipped, bg);
                    EditorGUI.DrawRect(new Rect(boxClipped.x, boxClipped.y, boxClipped.width, 1f), border);
                    EditorGUI.DrawRect(new Rect(boxClipped.x, boxClipped.yMax - 1f, boxClipped.width, 1f), border);
                    EditorGUI.DrawRect(new Rect(boxClipped.x, boxClipped.y, 1f, boxClipped.height), border);
                    EditorGUI.DrawRect(new Rect(boxClipped.xMax - 1f, boxClipped.y, 1f, boxClipped.height), border);
                    var textRect = new Rect(boxRect.x + 4f, boxRect.y + 2f, boxRect.width - 8f, boxRect.height - 4f);
                    var clipped = ClipRect(textRect, rect);
                    if (clipped.width > 2f && clipped.height > 2f)
                        EditorGUI.LabelField(clipped, label, style);
                }
                GUI.matrix = prev;
                Handles.EndGUI();
                placed.Add(boxRect);
            }
            Handles.EndGUI();
        }

        private static Rect ClipRect(Rect rect, Rect bounds)
        {
            float xMin = Mathf.Max(rect.xMin, bounds.xMin);
            float yMin = Mathf.Max(rect.yMin, bounds.yMin);
            float xMax = Mathf.Min(rect.xMax, bounds.xMax);
            float yMax = Mathf.Min(rect.yMax, bounds.yMax);
            float w = Mathf.Max(0f, xMax - xMin);
            float h = Mathf.Max(0f, yMax - yMin);
            return new Rect(xMin, yMin, w, h);
        }

        private void HandleMapInteraction(Rect rect)
        {
            var evt = Event.current;
            if (evt == null) return;

            if (evt.type == EventType.MouseDown && evt.button == 0 && rect.Contains(evt.mousePosition))
            {
                _isPanning = true;
                evt.Use();
            }
            else if (evt.type == EventType.MouseDrag && _isPanning)
            {
                _mapPan += evt.delta;
                evt.Use();
                Repaint();
            }
            else if (evt.type == EventType.MouseUp && evt.button == 0 && _isPanning)
            {
                _isPanning = false;
                evt.Use();
            }
            else if (evt.type == EventType.ScrollWheel && rect.Contains(evt.mousePosition))
            {
                float delta = -evt.delta.y * 0.05f;
                float next = Mathf.Clamp(_mapZoom + delta, 0.5f, 2.5f);
                if (!Mathf.Approximately(next, _mapZoom))
                {
                    _mapZoom = next;
                    evt.Use();
                    Repaint();
                }
            }
        }

        private static Bounds GetBounds(List<InteractionModule> modules)
        {
            bool has = false;
            Bounds b = new Bounds(Vector3.zero, Vector3.zero);
            for (int i = 0; i < modules.Count; i++)
            {
                var m = modules[i];
                if (m == null) continue;
                if (!has)
                {
                    b = new Bounds(m.transform.position, Vector3.zero);
                    has = true;
                }
                else
                {
                    b.Encapsulate(m.transform.position);
                }
            }
            if (!has) b = new Bounds(Vector3.zero, Vector3.one);
            return b;
        }

        private static Color GetPhaseColor(InteractionModule module)
        {
            if (!Application.isPlaying) return new Color(0.6f, 0.6f, 0.6f, 1f);
            switch (module.CurrentPhase)
            {
                case InteractionPhase.Initial: return new Color(0.6f, 0.75f, 1f, 1f);
                case InteractionPhase.Start: return new Color(0.3f, 0.8f, 0.4f, 1f);
                case InteractionPhase.Appearing: return new Color(0.3f, 0.6f, 0.95f, 1f);
                case InteractionPhase.InProgress: return new Color(0.95f, 0.8f, 0.2f, 1f);
                case InteractionPhase.End: return new Color(0.95f, 0.4f, 0.3f, 1f);
                default: return Color.white;
            }
        }

        private static string GetLocalizedPhaseName(InteractionPhase phase)
        {
            switch (phase)
            {
                case InteractionPhase.Initial: return "初始";
                case InteractionPhase.Start: return "开始";
                case InteractionPhase.Appearing: return "出现中";
                case InteractionPhase.InProgress: return "进行中";
                case InteractionPhase.End: return "结束";
                default: return phase.ToString();
            }
        }

        private static string GetLocalizedTriggerName(TriggerType type)
        {
            switch (type)
            {
                case TriggerType.Time: return "时间";
                case TriggerType.Enter: return "进入";
                case TriggerType.Leave: return "离开";
                case TriggerType.Touch: return "触摸";
                case TriggerType.LongPress: return "长按";
                case TriggerType.TouchGuide: return "触摸导游";
                default: return type.ToString();
            }
        }

    }
}
