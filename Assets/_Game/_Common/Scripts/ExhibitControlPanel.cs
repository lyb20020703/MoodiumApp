using System.Collections; // 必须引用：用于协程
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro; 
using UnityEngine.XR.Interaction.Toolkit.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using System.Collections.Generic;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace VFXViewer
{
    public partial class ExhibitControlPanel : MonoBehaviour
    {
        private const string PanelAppearAnimManagerTypeName = "InterfaceAnimManager";
        private const string PanelRootObjectName = "PanelRoot";
        private const string NestedPanelCanvasObjectName = "Panel";
        private const string PanelAnimContainerName = "Clean Welcome";
        private const string CurrentExhibitNameTextObjectName = "CurrentExhibitNameText";
        private const string CurrentExhibitIndexTextObjectName = "CurrentExhibitIndexText";
        private const string CurrentExhibitStateTextObjectName = "CurrentExhibitStateText";
        private const string ToggleContentVisibilityButtonObjectName = "ToggleContentButton";
        private const string DetailButtonObjectName = "DetailButton";
        private const string SyncLayoutFromServerButtonObjectName = "SyncLayoutFromServerButton";
        private const string ClearLayoutButtonObjectName = "ClearLayoutButton";
        private const string DetailPanelObjectName = "DetailPanel";
        private const string BatchActionDialogObjectName = "Dialog";
        private const string BatchActionDialogContentObjectName = "GameObject";
        private const string BatchActionDialogYesButtonObjectName = "YESButton";
        private const string BatchActionDialogNoButtonObjectName = "NOButton";
        private const string UiLayerName = "UI";
        private const int FallbackUiLayer = 5;
        [Header("核心引用")]
        [SerializeField] private ExhibitPlacementManager m_Spawner;
        [SerializeField] private ChapterPlacementDirector m_PlacementDirector;

        [Header("动态提示")]
        [SerializeField] private Text m_StatusText; // 当前展品状态
        [SerializeField] private Text m_CurrentExhibitText; // 当前展品名
        [SerializeField] private Text m_CurrentExhibitIndexText; // 当前展品编号

        [Header("布展操作 (单个)")]
        [SerializeField] private Button m_SpawnButton; 
        [SerializeField] private Button m_ToggleContentVisibilityButton;
        [SerializeField] private Button m_DeleteCurrentPlacementButton;

        [Header("详情面板")]
        [SerializeField] private Button m_DetailButton;
        [SerializeField] private GameObject m_MainPanelToHideWhenDetailOpen;
        [SerializeField] private GameObject m_DetailPanel;
        [SerializeField] private MonoBehaviour m_DetailPanelAppearAnimManager;

        [Header("展品切换")]
        [SerializeField] private Button m_SaveClosestButton;
        [SerializeField] private Button m_DeleteClosestButton;

        [Header("批量操作")]
        [SerializeField] private Button m_DeployAllButton;
        [SerializeField] private Button m_ClearSceneButton;
        [SerializeField] private Button m_SyncLayoutFromServerButton;
        [SerializeField] private Button m_ClearLayoutButton;
        [SerializeField] private bool m_EnableBatchPlacementActions = false;
        [SerializeField] private GameObject m_BatchActionDialog;
        [SerializeField] private Button m_BatchActionDialogYesButton;
        [SerializeField] private Button m_BatchActionDialogNoButton;
        [SerializeField] private Text m_BatchActionDialogText;
        [SerializeField] private TMP_Text m_BatchActionDialogTmpText;
        [SerializeField] private MonoBehaviour m_BatchActionDialogAppearAnimManager;

        [Header("模式切换")]
        [SerializeField] private Button m_EnterExperienceModeButton;

        [Header("字体")]
        [SerializeField] private Font m_PanelChineseFont;
        [SerializeField] private string[] m_PanelChineseFontFallbackNames =
        {
            "PingFang SC",
            "PingFangSC-Regular",
            "Hiragino Sans GB",
            "Heiti SC",
            "STHeitiSC-Light",
            "Arial Unicode MS",
        };

        [Header("体验锁定")]
        [SerializeField] private GameObject m_PanelRoot;
        [SerializeField] private MonoBehaviour m_PanelAppearAnimManager;
        [SerializeField] private bool m_HidePanelInExperienceMode = true;
        [SerializeField] private bool m_StartHiddenInPlacementMode = true;
        [SerializeField] private PlacementPanelPalmRelocator m_PanelPalmRelocator;
        private MonoBehaviour m_MainPanelAppearAnimManager;

        private string m_LastStatusMessage = "";
        private string m_LastCurrentExhibitLabel = "";
        private string m_LastCurrentExhibitIndexLabel = "";
        private string m_LastCurrentExhibitStatusLabel = "";
        private string m_LastSpawnButtonLabel = "";
        private string m_LastToggleContentVisibilityButtonLabel = "";
        private bool? m_LastPanelVisibleState;
        private bool m_PlacementVisibilityInitialized;
        private bool m_PlacementPanelHiddenByRecall;
        private Coroutine m_TemporaryPlacementPanelRoutine;
        private bool m_RuntimeToggleContentButtonCreated;
        private bool m_UiRefreshPending;
        private bool m_ForceUiRefreshPending;
        private bool m_PanelChineseFontResolved;
        private bool m_PanelChineseFontMissingLogged;
        private bool m_PanelChineseFontAppliedLogged;
        private ExhibitPlacementManager m_SubscribedSpawner;
        private Font m_RuntimePanelChineseFont;
        private readonly HashSet<int> m_ButtonLabelCacheInitialized = new HashSet<int>();
        private readonly Dictionary<int, TMP_Text> m_ButtonTmpLabelCache = new Dictionary<int, TMP_Text>();
        private readonly Dictionary<int, Text> m_ButtonLegacyLabelCache = new Dictionary<int, Text>();
        private readonly Dictionary<int, bool> m_RayInteractorOriginalUiInteractionStates = new Dictionary<int, bool>();
        private readonly Dictionary<int, bool> m_NearFarInteractorOriginalUiInteractionStates = new Dictionary<int, bool>();
        private readonly Dictionary<int, bool> m_PokeInteractorOriginalUiInteractionStates = new Dictionary<int, bool>();
        private PendingBatchAction m_PendingBatchAction = PendingBatchAction.None;
        private Coroutine m_BatchActionDialogTransitionRoutine;
        private const float BatchActionDialogTransitionTimeoutSeconds = 1.2f;

        private enum PendingBatchAction
        {
            None = 0,
            DeployAll = 1,
            ClearScene = 2,
            SyncLayoutFromServer = 3,
            ClearLayout = 4,
        }

#if UNITY_EDITOR
        [Header("Editor Game 调试")]
        [SerializeField] private bool m_EnableEditorGameDebug = true;
        [SerializeField] private bool m_EditorGameDebugVisible = true;
        [SerializeField] private KeyCode m_EditorGameDebugToggleKey = KeyCode.BackQuote;
        [SerializeField] private Rect m_EditorGameDebugRect = new Rect(16f, 16f, 560f, 190f);
#endif

        private void Awake()
        {
            ResolveRuntimeReferences();
        }

        private void OnEnable()
        {
            if (!Application.isPlaying)
                return;

            ResolveRuntimeReferences();
            SubscribeRuntimeEvents();
            RequestUiRefresh(force: true);
        }

        private void OnDisable()
        {
            if (!Application.isPlaying)
                return;

            UnsubscribeRuntimeEvents();
        }

        void Start()
        {
            ResolveRuntimeReferences();
            ResolveRuntimeButtons();
            EnsureStagePlacementUiCreated();
            SubscribeRuntimeEvents();

            if (m_Spawner == null) { Debug.LogError("UI 未绑定 Spawner"); return; }
            if (m_PlacementDirector == null || !m_PlacementDirector.IsGuidedModeReady)
            {
                Debug.LogError("UI 未绑定可用的 ChapterPlacementDirector。当前版本仅支持引导模式。");
                m_Spawner.onStatusMessage?.Invoke("引导模式未就绪：请绑定 ChapterPlacementDirector 与 ChapterPlacementPlan。");
            }

            // 1. 按钮绑定
            Bind(m_SpawnButton, OnSpawnClicked);
            Bind(m_ToggleContentVisibilityButton, OnToggleContentVisibilityClicked);
            Bind(m_DeleteCurrentPlacementButton, OnDeleteCurrentPlacementClicked);
            Bind(m_DetailButton, OnToggleDetailPanelClicked);
            Bind(m_SaveClosestButton, OnSaveClosestClicked);
            Bind(m_DeleteClosestButton, OnDeleteClosestClicked);
            Bind(m_DeployAllButton, OnDeployAllClicked);
            Bind(m_ClearSceneButton, OnClearSceneClicked);
            Bind(m_SyncLayoutFromServerButton, OnSyncLayoutFromServerClicked);
            Bind(m_ClearLayoutButton, OnClearLayoutClicked);
            Bind(m_EnterExperienceModeButton, OnEnterExperienceModeClicked);
            Bind(m_BatchActionDialogYesButton, OnConfirmBatchActionClicked);
            Bind(m_BatchActionDialogNoButton, OnCancelBatchActionClicked);
            EnsureButtonPointerDebugHooks();
            ApplyChineseButtonLabels();
            ApplyPanelChineseFont();
            HideBatchActionDialog(cancelPendingAction: true, immediate: true);
            RequestUiRefresh(force: true);
            ApplyPendingUiRefresh();
        }

        private void ResolveRuntimeReferences()
        {
            if (m_Spawner == null)
                m_Spawner = FindFirstObjectByType<ExhibitPlacementManager>(FindObjectsInactive.Include);

            if (m_PlacementDirector == null)
                m_PlacementDirector = FindFirstObjectByType<ChapterPlacementDirector>(FindObjectsInactive.Include);

            if (m_PanelPalmRelocator == null)
                m_PanelPalmRelocator = FindFirstObjectByType<PlacementPanelPalmRelocator>(FindObjectsInactive.Include);

            if (m_CurrentExhibitText == null)
                m_CurrentExhibitText = ResolveText(CurrentExhibitNameTextObjectName);

            if (m_CurrentExhibitIndexText == null)
                m_CurrentExhibitIndexText = ResolveText(CurrentExhibitIndexTextObjectName);

            if (m_StatusText == null)
                m_StatusText = ResolveText(CurrentExhibitStateTextObjectName);

            if (m_PanelRoot == null)
                m_PanelRoot = ResolvePanelRoot();

            if (m_DetailPanel == null)
            {
                Transform detailPanelTransform = FindNamedChildRecursive(transform, DetailPanelObjectName);
                m_DetailPanel = detailPanelTransform != null ? detailPanelTransform.gameObject : null;
            }

            if (m_BatchActionDialog == null)
            {
                Transform dialogTransform = FindNamedChildRecursive(transform, BatchActionDialogObjectName);
                m_BatchActionDialog = dialogTransform != null ? dialogTransform.gameObject : null;
            }

            if (m_DetailPanelAppearAnimManager == null)
                m_DetailPanelAppearAnimManager = ResolveDetailPanelAppearAnimManager();

            ResolveRuntimeBatchActionDialog();

            NormalizePanelAnimManagerReferences();

            EnsureCanvasSubtreeUsesUiLayer();
            DisableNestedPanelCanvasIfNeeded();
            EnsurePanelCanvasRaycasters();
            EnsurePanelGraphicRaycastTargets();
            EnsurePanelCanvasGroupRaycastFilters();
            ApplyPanelChineseFont();
        }

        private void EnsureCanvasSubtreeUsesUiLayer()
        {
            int uiLayer = LayerMask.NameToLayer(UiLayerName);
            if (uiLayer < 0)
                uiLayer = FallbackUiLayer;

            ApplyLayerRecursively(transform, uiLayer);
        }

        private static void ApplyLayerRecursively(Transform root, int layer)
        {
            if (root == null)
                return;

            if (root.gameObject.layer != layer)
                root.gameObject.layer = layer;

            for (int i = 0; i < root.childCount; i++)
                ApplyLayerRecursively(root.GetChild(i), layer);
        }

        private void DisableNestedPanelCanvasIfNeeded()
        {
            if (m_PanelRoot == null)
                return;

            Transform nestedPanel = FindNamedChildRecursive(m_PanelRoot.transform, NestedPanelCanvasObjectName);
            if (nestedPanel == null)
                return;

            if (nestedPanel.TryGetComponent(out Canvas nestedCanvas))
                nestedCanvas.enabled = false;

            if (nestedPanel.TryGetComponent(out CanvasScaler nestedScaler))
                nestedScaler.enabled = false;

            if (nestedPanel.TryGetComponent(out GraphicRaycaster nestedGraphicRaycaster))
                nestedGraphicRaycaster.enabled = false;

            if (nestedPanel.TryGetComponent(out TrackedDeviceGraphicRaycaster nestedTrackedRaycaster))
                nestedTrackedRaycaster.enabled = false;
        }

        private void EnsurePanelCanvasRaycasters()
        {
            if (m_PanelRoot == null)
                return;

#if ENABLE_INPUT_SYSTEM
            bool needsTrackedDeviceRaycaster = false;
            if (EventSystem.current != null && EventSystem.current.currentInputModule is InputSystemUIInputModule)
                needsTrackedDeviceRaycaster = true;
#endif

            var canvases = new List<Canvas>();
            var seenCanvasIds = new HashSet<int>();

            void AddCanvases(Canvas[] source)
            {
                if (source == null)
                    return;

                for (int i = 0; i < source.Length; i++)
                {
                    var canvas = source[i];
                    if (canvas == null)
                        continue;

                    int canvasId = canvas.GetInstanceID();
                    if (!seenCanvasIds.Add(canvasId))
                        continue;

                    canvases.Add(canvas);
                }
            }

            AddCanvases(m_PanelRoot.GetComponentsInParent<Canvas>(true));
            AddCanvases(m_PanelRoot.GetComponentsInChildren<Canvas>(true));

            for (int i = 0; i < canvases.Count; i++)
            {
                var canvas = canvases[i];
                if (canvas == null)
                    continue;

                if (canvas.renderMode == RenderMode.WorldSpace && canvas.worldCamera == null)
                {
                    Camera worldCamera = Camera.main;
                    if (worldCamera == null)
                        worldCamera = FindFirstObjectByType<Camera>(FindObjectsInactive.Include);

                    if (worldCamera != null)
                        canvas.worldCamera = worldCamera;
                }

                if (canvas.GetComponent<GraphicRaycaster>() == null)
                    canvas.gameObject.AddComponent<GraphicRaycaster>();

                if (canvas.GetComponent<TrackedDeviceGraphicRaycaster>() == null)
                    canvas.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();

#if ENABLE_INPUT_SYSTEM
                if (needsTrackedDeviceRaycaster && canvas.GetComponent<TrackedDeviceRaycaster>() == null)
                    canvas.gameObject.AddComponent<TrackedDeviceRaycaster>();
#endif
            }
        }

        private void EnsurePanelGraphicRaycastTargets()
        {
            if (m_PanelRoot == null)
                return;

            var graphics = m_PanelRoot.GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < graphics.Length; i++)
            {
                var graphic = graphics[i];
                if (graphic == null)
                    continue;

                // 只保留按钮等 Selectable 子树的 UI 射线，避免装饰框/说明文字挡住点击。
                bool isInteractiveGraphic = graphic.GetComponentInParent<Selectable>(true) != null;
                if (!isInteractiveGraphic)
                    graphic.raycastTarget = false;
            }
        }

        private void EnsurePanelCanvasGroupRaycastFilters()
        {
            if (m_PanelRoot == null)
                return;

            var canvasGroups = m_PanelRoot.GetComponentsInChildren<CanvasGroup>(true);
            for (int i = 0; i < canvasGroups.Length; i++)
            {
                var canvasGroup = canvasGroups[i];
                if (canvasGroup == null)
                    continue;

                bool isButtonSubtreeGroup = canvasGroup.GetComponentInParent<Selectable>(true) != null;
                bool containsInteractiveChildren = canvasGroup.GetComponentInChildren<Selectable>(true) != null;
                if (isButtonSubtreeGroup || containsInteractiveChildren)
                    continue;

                // 这里只避免纯装饰/信息层的 CanvasGroup 挡住 UI 射线。
                // 不要改 interactable；父级 CanvasGroup.interactable=false 会把整棵按钮子树一起禁掉。
                canvasGroup.blocksRaycasts = false;
            }
        }

        private void ResolveRuntimeButtons()
        {
            m_SpawnButton = ResolveButton(m_SpawnButton, "CreateButton");
            m_ToggleContentVisibilityButton = ResolveButton(m_ToggleContentVisibilityButton, ToggleContentVisibilityButtonObjectName, "VisibleButton", "ContentVisibilityButton", "HideContentButton");
            m_DeleteCurrentPlacementButton = ResolveButton(m_DeleteCurrentPlacementButton, "DeleteButton");
            m_DetailButton = ResolveButton(m_DetailButton, DetailButtonObjectName);
            m_SaveClosestButton = ResolveButton(m_SaveClosestButton, "SaveButton", "PrevButton");
            m_DeleteClosestButton = ResolveButton(m_DeleteClosestButton, "NextButton");
            m_DeployAllButton = ResolveButton(m_DeployAllButton, "RebuildButton");
            m_ClearSceneButton = ResolveButton(m_ClearSceneButton, "ReClearButton (1)", "ClearButton");
            m_SyncLayoutFromServerButton = ResolveButton(m_SyncLayoutFromServerButton, SyncLayoutFromServerButtonObjectName);
            m_ClearLayoutButton = ResolveButton(m_ClearLayoutButton, ClearLayoutButtonObjectName);
            m_EnterExperienceModeButton = ResolveButton(m_EnterExperienceModeButton, "ExperienceModeButton", "ExperienceButton");
            m_BatchActionDialogYesButton = ResolveButtonInRoot(m_BatchActionDialog, m_BatchActionDialogYesButton, BatchActionDialogYesButtonObjectName);
            m_BatchActionDialogNoButton = ResolveButtonInRoot(m_BatchActionDialog, m_BatchActionDialogNoButton, BatchActionDialogNoButtonObjectName);

            if (m_ToggleContentVisibilityButton == null)
                m_ToggleContentVisibilityButton = CreateRuntimeToggleContentVisibilityButton();

            ApplyPanelChineseFont();
        }

        private void ResolveRuntimeBatchActionDialog()
        {
            if (m_BatchActionDialog == null)
                return;

            if (m_BatchActionDialogYesButton == null)
                m_BatchActionDialogYesButton = ResolveButtonInRoot(m_BatchActionDialog, null, BatchActionDialogYesButtonObjectName);

            if (m_BatchActionDialogNoButton == null)
                m_BatchActionDialogNoButton = ResolveButtonInRoot(m_BatchActionDialog, null, BatchActionDialogNoButtonObjectName);

            if (m_BatchActionDialogAppearAnimManager == null)
                m_BatchActionDialogAppearAnimManager = ResolveAnimManagerForObject(m_BatchActionDialog);

            if (m_BatchActionDialogText == null && m_BatchActionDialogTmpText == null)
            {
                Transform contentRoot = FindNamedChildRecursive(m_BatchActionDialog.transform, BatchActionDialogContentObjectName);
                Transform searchRoot = contentRoot != null ? contentRoot : m_BatchActionDialog.transform;

                m_BatchActionDialogTmpText = searchRoot.GetComponentInChildren<TMP_Text>(true);
                if (m_BatchActionDialogTmpText == null)
                    m_BatchActionDialogText = searchRoot.GetComponentInChildren<Text>(true);
            }
        }

        private Button ResolveButton(Button current, params string[] buttonNames)
        {
            if (current != null)
                return current;

            var allButtons = GetComponentsInChildren<Button>(true);
            for (int i = 0; i < allButtons.Length; i++)
            {
                var button = allButtons[i];
                if (button == null)
                    continue;

                for (int nameIndex = 0; nameIndex < buttonNames.Length; nameIndex++)
                {
                    if (string.Equals(button.gameObject.name, buttonNames[nameIndex]))
                        return button;
                }

                if (buttonNames.Length == 0)
                    return button;
            }

            return null;
        }

        private void Update()
        {
            if (m_UiRefreshPending)
                ApplyPendingUiRefresh();

            bool primaryPlacementActionVisibilityDrifted = ShouldForcePrimaryPlacementActionVisibilityRefresh();
            if (primaryPlacementActionVisibilityDrifted)
                RefreshPrimaryPlacementActionButtonVisibility();

            bool batchActionButtonVisibilityDrifted = ShouldForceBatchActionButtonVisibilityRefresh();
            if (batchActionButtonVisibilityDrifted)
                RefreshBatchActionButtonState();

            bool detailPresentationDrifted = ShouldForceClosedDetailPanelRefresh();
            if (m_DetailPanelOpen || m_DetailPanelTransitionActive || detailPresentationDrifted)
                RefreshDetailPanelState(force: detailPresentationDrifted);

#if UNITY_EDITOR
            if (!Application.isPlaying) return;
            if (!m_EnableEditorGameDebug) return;
            if (Input.GetKeyDown(m_EditorGameDebugToggleKey))
                m_EditorGameDebugVisible = !m_EditorGameDebugVisible;
#endif
        }

        private void Bind(Button btn, UnityEngine.Events.UnityAction action)
        {
            if (btn != null) { btn.onClick.RemoveAllListeners(); btn.onClick.AddListener(action); }
        }

        private void ApplyChineseButtonLabels()
        {
            SetButtonLabel(m_SaveClosestButton, "上一项");
            SetButtonLabel(m_DeleteClosestButton, "下一项");
            SetButtonLabel(m_DeleteCurrentPlacementButton, "删除当前项");
            SetButtonLabel(m_DeployAllButton, "一键布展");
            SetButtonLabel(m_ClearSceneButton, "一键清展");
            SetButtonLabel(m_SyncLayoutFromServerButton, "同步 layout");
            SetButtonLabel(m_ClearLayoutButton, "清空 layout");
            SetButtonLabel(m_EnterExperienceModeButton, "体验模式");
            SetButtonLabel(m_ToggleContentVisibilityButton, "隐藏内容");
            SetButtonLabel(m_DetailButton, "详情面板");
        }

        private void EnsureButtonPointerDebugHooks()
        {
            AttachButtonDebugHook(m_SpawnButton, "放置当前展品");
            AttachButtonDebugHook(m_ToggleContentVisibilityButton, "隐藏内容 / 显示内容");
            AttachButtonDebugHook(m_DeleteCurrentPlacementButton, "删除当前展品");
            AttachButtonDebugHook(m_DetailButton, "详情面板");
            AttachButtonDebugHook(m_SaveClosestButton, "上个展品");
            AttachButtonDebugHook(m_DeleteClosestButton, "下个展品");
            AttachButtonDebugHook(m_DeployAllButton, "一键布展");
            AttachButtonDebugHook(m_ClearSceneButton, "一键清展");
            AttachButtonDebugHook(m_SyncLayoutFromServerButton, "从服务器同步 layout");
            AttachButtonDebugHook(m_ClearLayoutButton, "清空本地 layout");
            AttachButtonDebugHook(m_EnterExperienceModeButton, "体验模式");
            AttachButtonDebugHook(m_BatchActionDialogYesButton, "批量操作确认");
            AttachButtonDebugHook(m_BatchActionDialogNoButton, "批量操作取消");
        }

        private static void AttachButtonDebugHook(Button button, string label)
        {
            if (button == null)
                return;

            var hook = button.GetComponent<UIPointerDebugLogger>();
            if (hook == null)
                hook = button.gameObject.AddComponent<UIPointerDebugLogger>();

            hook.SetLabel(label);
        }

        private void SetButtonLabel(Button button, string text)
        {
            if (button == null || string.IsNullOrEmpty(text))
                return;

            ResolveButtonLabel(button, out var tmp, out var legacyText);
            if (tmp != null)
            {
                if (!string.Equals(tmp.text, text))
                    tmp.text = text;
                return;
            }

            if (legacyText != null)
            {
                ApplyPanelChineseFont(legacyText);
                if (!string.Equals(legacyText.text, text))
                    legacyText.text = text;
            }
        }

        private Font ResolvePanelChineseFont()
        {
            if (m_PanelChineseFont != null)
                return m_PanelChineseFont;

            if (m_PanelChineseFontResolved)
                return m_RuntimePanelChineseFont;

            m_PanelChineseFontResolved = true;
            if (m_PanelChineseFontFallbackNames != null)
            {
                for (int i = 0; i < m_PanelChineseFontFallbackNames.Length; i++)
                {
                    string fontName = m_PanelChineseFontFallbackNames[i];
                    if (string.IsNullOrWhiteSpace(fontName))
                        continue;

                    try
                    {
                        Font font = Font.CreateDynamicFontFromOSFont(fontName, 24);
                        if (font == null)
                            continue;

                        m_RuntimePanelChineseFont = font;
                        return m_RuntimePanelChineseFont;
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning($"[ExhibitControlPanel] 创建系统字体失败: {fontName}, {ex.Message}");
                    }
                }
            }

            if (!m_PanelChineseFontMissingLogged)
            {
                string names = m_PanelChineseFontFallbackNames == null || m_PanelChineseFontFallbackNames.Length == 0
                    ? "<none>"
                    : string.Join(", ", m_PanelChineseFontFallbackNames);
                string message = $"[PlacementUI] panel chinese font unavailable names={names}";
                Debug.LogWarning(message);
                PlacementDebugFileLogger.Log(message);
                m_PanelChineseFontMissingLogged = true;
            }

            return null;
        }

        private void ApplyPanelChineseFont()
        {
            Font font = ResolvePanelChineseFont();
            if (font == null)
                return;

            var texts = GetComponentsInChildren<Text>(true);
            for (int i = 0; i < texts.Length; i++)
                ApplyPanelChineseFont(texts[i], font);

            if (!m_PanelChineseFontAppliedLogged)
            {
                string message = $"[PlacementUI] panel chinese font applied font={font.name}";
                PlacementDebugFileLogger.Log(message);
                m_PanelChineseFontAppliedLogged = true;
            }
        }

        private void ApplyPanelChineseFont(Text text)
        {
            ApplyPanelChineseFont(text, ResolvePanelChineseFont());
        }

        private static void ApplyPanelChineseFont(Text text, Font font)
        {
            if (text == null || font == null)
                return;

            if (text.font == font)
                return;

            text.font = font;
            text.SetAllDirty();
        }

        private Button CreateRuntimeToggleContentVisibilityButton()
        {
            if (m_RuntimeToggleContentButtonCreated)
                return m_ToggleContentVisibilityButton;

            Button template = m_ClearSceneButton != null ? m_ClearSceneButton : m_DeployAllButton;
            Vector2 offset = new Vector2(0f, -36f);

            if (template == null)
            {
                template = m_SpawnButton;
                offset = new Vector2(0f, -52f);
            }

            if (template == null)
                return null;

            var clone = Instantiate(template.gameObject, template.transform.parent);
            clone.name = ToggleContentVisibilityButtonObjectName;

            if (clone.TryGetComponent(out RectTransform cloneRect) &&
                template.TryGetComponent(out RectTransform templateRect))
            {
                cloneRect.anchorMin = templateRect.anchorMin;
                cloneRect.anchorMax = templateRect.anchorMax;
                cloneRect.pivot = templateRect.pivot;
                cloneRect.sizeDelta = templateRect.sizeDelta;
                cloneRect.anchoredPosition = templateRect.anchoredPosition + offset;
                cloneRect.localRotation = templateRect.localRotation;
                cloneRect.localScale = templateRect.localScale;
            }

            m_RuntimeToggleContentButtonCreated = true;
            var button = clone.GetComponent<Button>();
            InvalidateButtonLabelCache(button);
            return button;
        }

        private void SubscribeRuntimeEvents()
        {
            if (m_Spawner == null)
                return;

            if (ReferenceEquals(m_SubscribedSpawner, m_Spawner))
                return;

            UnsubscribeRuntimeEvents();
            m_SubscribedSpawner = m_Spawner;
            m_SubscribedSpawner.onStatusMessage?.AddListener(HandleStatusMessage);
            m_SubscribedSpawner.onSelectionChanged?.AddListener(HandleSpawnerUiSignal);
            m_SubscribedSpawner.onPrefabChanged?.AddListener(HandleSpawnerUiSignal);
            m_SubscribedSpawner.onExhibitRestored?.AddListener(HandleSpawnerUiSignal);
        }

        private void UnsubscribeRuntimeEvents()
        {
            if (m_SubscribedSpawner == null)
                return;

            m_SubscribedSpawner.onStatusMessage?.RemoveListener(HandleStatusMessage);
            m_SubscribedSpawner.onSelectionChanged?.RemoveListener(HandleSpawnerUiSignal);
            m_SubscribedSpawner.onPrefabChanged?.RemoveListener(HandleSpawnerUiSignal);
            m_SubscribedSpawner.onExhibitRestored?.RemoveListener(HandleSpawnerUiSignal);
            m_SubscribedSpawner = null;
        }

        private void HandleSpawnerUiSignal(string _)
        {
            RequestUiRefresh();
        }

        private void RequestUiRefresh(bool force = false)
        {
            m_UiRefreshPending = true;
            m_ForceUiRefreshPending |= force;
        }

        private void ApplyPendingUiRefresh()
        {
            bool force = m_ForceUiRefreshPending;
            m_UiRefreshPending = false;
            m_ForceUiRefreshPending = false;

            RefreshStagePlacementUi(force);
            RefreshCurrentExhibitLabel(force);
            RefreshCurrentExhibitIndexLabel(force);
            RefreshCurrentExhibitStatusLabel(force);
            RefreshSpawnButtonLabel(force);
            RefreshToggleContentVisibilityButtonState(force);
            RefreshDeleteCurrentPlacementButtonState();
            RefreshPlacementNavigationButtonState();
            RefreshBatchActionButtonState(force);
            RefreshPanelVisibility(force);
            RefreshPrimaryPlacementActionButtonVisibility();
            RefreshDetailPanelState(force);
        }

        private void ResolveButtonLabel(Button button, out TMP_Text tmp, out Text legacyText)
        {
            tmp = null;
            legacyText = null;
            if (button == null)
                return;

            int buttonId = button.GetInstanceID();
            if (!m_ButtonLabelCacheInitialized.Contains(buttonId))
            {
                tmp = button.GetComponentInChildren<TMP_Text>(true);
                legacyText = tmp == null ? button.GetComponentInChildren<Text>(true) : null;
                m_ButtonLabelCacheInitialized.Add(buttonId);
                m_ButtonTmpLabelCache[buttonId] = tmp;
                m_ButtonLegacyLabelCache[buttonId] = legacyText;
                return;
            }

            m_ButtonTmpLabelCache.TryGetValue(buttonId, out tmp);
            m_ButtonLegacyLabelCache.TryGetValue(buttonId, out legacyText);
        }

        private void InvalidateButtonLabelCache(Button button)
        {
            if (button == null)
                return;

            int buttonId = button.GetInstanceID();
            m_ButtonLabelCacheInitialized.Remove(buttonId);
            m_ButtonTmpLabelCache.Remove(buttonId);
            m_ButtonLegacyLabelCache.Remove(buttonId);
        }

        private bool IsGuidedReady()
        {
            return m_PlacementDirector != null && m_PlacementDirector.IsGuidedModeReady;
        }

        private void RefreshPanelVisibility(bool force = false)
        {
            if (m_PanelRoot == null)
                return;

            bool wasActive = m_PanelRoot.activeSelf;

            if (m_PlacementDirector != null &&
                m_PlacementDirector.IsPlacementMode &&
                !m_PlacementVisibilityInitialized)
            {
                m_PlacementVisibilityInitialized = true;
                m_PlacementPanelHiddenByRecall = m_StartHiddenInPlacementMode;
            }

            bool shouldShow = true;
            if (m_HidePanelInExperienceMode &&
                m_PlacementDirector != null &&
                m_PlacementDirector.IsExperienceMode)
            {
                shouldShow = false;
            }
            else if (m_PlacementDirector != null &&
                     m_PlacementDirector.IsPlacementMode &&
                     m_PlacementPanelHiddenByRecall)
            {
                shouldShow = false;
            }

            bool visibilityChanged = !m_LastPanelVisibleState.HasValue || m_LastPanelVisibleState.Value != shouldShow;
            if (!force && !visibilityChanged)
                return;

            m_LastPanelVisibleState = shouldShow;

            if (!shouldShow)
            {
                HideBatchActionDialog(cancelPendingAction: true, immediate: true);
                ResetPanelAppearAnimationForHide();
            }

            m_PanelRoot.SetActive(shouldShow);

            if (shouldShow)
            {
                if (!wasActive || visibilityChanged)
                    PlayPanelAppearAnimationForPlacementRecall();

                RestoreClosedPanelPresentationAfterPanelShow();

                if (m_DetailPanelOpen || m_DetailPanelTransitionActive)
                    RefreshDetailPanelState(force: true);
                else
                    RefreshPanelTouchOnlyInteractionMode();

                return;
            }

            RefreshPanelTouchOnlyInteractionMode();
        }

        public void ShowPanelForPlacementRecall()
        {
            if (m_PanelRoot == null)
                return;

            PreparePanelForPlacementRecall();
            PlayPanelAppearAnimationForPlacementRecall();
        }

        public void ShowPanelTemporarily(float visibleSeconds)
        {
            if (m_PanelRoot == null)
                return;

            if (m_TemporaryPlacementPanelRoutine != null)
                StopCoroutine(m_TemporaryPlacementPanelRoutine);

            m_TemporaryPlacementPanelRoutine = StartCoroutine(ShowPanelTemporarilyCoroutine(Mathf.Max(0f, visibleSeconds)));
        }

        public void PreparePanelForPlacementRecall()
        {
            if (m_PanelRoot == null)
                return;

            m_PlacementVisibilityInitialized = true;
            m_PlacementPanelHiddenByRecall = false;
            RefreshPanelVisibility(force: true);
        }

        public bool IsPanelVisibleForPlacementRecall()
        {
            return m_PanelRoot != null &&
                   m_PanelRoot.activeInHierarchy &&
                   !m_PlacementPanelHiddenByRecall;
        }

        public bool BeginPanelRelocationTransition()
        {
            if (!IsPanelVisibleForPlacementRecall())
                return false;

            if (m_PanelAppearAnimManager == null)
            {
                m_PanelRoot.SetActive(false);
                return false;
            }

            string stateName = GetAnimManagerStateName(m_PanelAppearAnimManager);
            if (stateName == "disappearing" || stateName == "disappeared")
                return false;

            InvokeAnimManagerMethod(m_PanelAppearAnimManager, "startDisappear", false);
            return true;
        }

        public bool IsPanelRelocationTransitionComplete()
        {
            if (m_PanelAppearAnimManager == null)
                return m_PanelRoot == null || !m_PanelRoot.activeInHierarchy;

            return GetAnimManagerStateName(m_PanelAppearAnimManager) == "disappeared";
        }

        public void ForceCompletePanelRelocationTransitionForRecall()
        {
            if (m_PanelRoot == null)
                return;

            ResetPanelAppearAnimationForHide();
            m_PanelRoot.SetActive(false);
            RefreshPanelTouchOnlyInteractionMode();
        }

        internal void RefreshPanelTouchOnlyInteractionMode()
        {
            ApplyRayUiInteractionMode(IsTouchOnlyPanelInteractionActive());
        }

        private bool IsTouchOnlyPanelInteractionActive()
        {
            if (m_DetailPanel != null && m_DetailPanel.activeInHierarchy)
                return true;

            if (m_MainPanelToHideWhenDetailOpen != null && m_MainPanelToHideWhenDetailOpen.activeInHierarchy)
                return true;

            return m_PanelRoot != null && m_PanelRoot.activeInHierarchy;
        }

        private void ApplyRayUiInteractionMode(bool disableRayUiInteraction)
        {
            var rayInteractors = FindObjectsByType<XRRayInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < rayInteractors.Length; i++)
            {
                var interactor = rayInteractors[i];
                if (interactor == null)
                    continue;

                int id = interactor.GetInstanceID();
                if (!m_RayInteractorOriginalUiInteractionStates.ContainsKey(id))
                    m_RayInteractorOriginalUiInteractionStates[id] = interactor.enableUIInteraction;

                interactor.enableUIInteraction = disableRayUiInteraction
                    ? false
                    : m_RayInteractorOriginalUiInteractionStates[id];
            }

            var nearFarInteractors = FindObjectsByType<NearFarInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < nearFarInteractors.Length; i++)
            {
                var interactor = nearFarInteractors[i];
                if (interactor == null)
                    continue;

                int id = interactor.GetInstanceID();
                if (!m_NearFarInteractorOriginalUiInteractionStates.ContainsKey(id))
                    m_NearFarInteractorOriginalUiInteractionStates[id] = interactor.enableUIInteraction;

                interactor.enableUIInteraction = disableRayUiInteraction
                    ? false
                    : m_NearFarInteractorOriginalUiInteractionStates[id];
            }

            var pokeInteractors = FindObjectsByType<XRPokeInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < pokeInteractors.Length; i++)
            {
                var interactor = pokeInteractors[i];
                if (interactor == null)
                    continue;

                int id = interactor.GetInstanceID();
                if (!m_PokeInteractorOriginalUiInteractionStates.ContainsKey(id))
                    m_PokeInteractorOriginalUiInteractionStates[id] = interactor.enableUIInteraction;

                interactor.enableUIInteraction = disableRayUiInteraction
                    ? true
                    : m_PokeInteractorOriginalUiInteractionStates[id];
            }
        }

        private GameObject ResolvePanelRoot()
        {
            Transform panelRootTransform = FindNamedChildRecursive(transform, PanelRootObjectName);
            return panelRootTransform != null ? panelRootTransform.gameObject : null;
        }

        private MonoBehaviour ResolvePanelAppearAnimManager()
        {
            MonoBehaviour panelRootManager = ResolvePanelRootAnimManager();
            if (panelRootManager != null)
                return panelRootManager;

            if (m_PanelRoot != null)
            {
                Transform animContainer = FindNamedChildRecursive(m_PanelRoot.transform, PanelAnimContainerName);
                if (animContainer != null)
                {
                    var containerManager = animContainer.GetComponent(PanelAppearAnimManagerTypeName) as MonoBehaviour;
                    if (containerManager != null)
                        return containerManager;

                    var containerChildManager = FindNamedComponentInChildren(animContainer);
                    if (containerChildManager != null)
                        return containerChildManager;
                }

                var managerOnRoot = m_PanelRoot.GetComponent(PanelAppearAnimManagerTypeName) as MonoBehaviour;
                if (managerOnRoot != null)
                    return managerOnRoot;

                var managerInChildren = FindNamedComponentInChildren(m_PanelRoot.transform);
                if (managerInChildren != null)
                    return managerInChildren;
            }

            return FindNamedComponentInChildren(transform);
        }

        private void NormalizePanelAnimManagerReferences()
        {
            MonoBehaviour panelRootManager = ResolvePanelRootAnimManager();
            if (panelRootManager != null)
            {
                m_PanelAppearAnimManager = panelRootManager;
            }
            else if (m_PanelAppearAnimManager == null)
            {
                m_PanelAppearAnimManager = ResolvePanelAppearAnimManager();
            }

            m_MainPanelAppearAnimManager = ResolveExplicitMainPanelAnimManager();
        }

        private MonoBehaviour ResolvePanelRootAnimManager()
        {
            if (m_PanelRoot == null)
                return null;

            return m_PanelRoot.GetComponent(PanelAppearAnimManagerTypeName) as MonoBehaviour;
        }

        private MonoBehaviour ResolveExplicitMainPanelAnimManager()
        {
            if (m_MainPanelToHideWhenDetailOpen == null)
                return null;

            return ResolveAnimManagerForObject(m_MainPanelToHideWhenDetailOpen);
        }

        private MonoBehaviour ResolveDetailPanelAppearAnimManager()
        {
            return ResolveAnimManagerForObject(m_DetailPanel);
        }

        private MonoBehaviour ResolveMainPanelAnimManagerForDetailTransition()
        {
            if (m_MainPanelAppearAnimManager == null)
                m_MainPanelAppearAnimManager = ResolveExplicitMainPanelAnimManager();

            if (m_MainPanelAppearAnimManager != null)
                return m_MainPanelAppearAnimManager;

            return m_PanelAppearAnimManager;
        }

        private void RestorePrimaryInfoPresentationAfterDetailClose()
        {
            Transform infoRoot = ResolvePrimaryInfoRoot();
            if (infoRoot == null)
                return;

            Transform current = infoRoot;
            while (current != null)
            {
                if (current.gameObject.activeSelf == false)
                    current.gameObject.SetActive(true);

                if (m_MainPanelToHideWhenDetailOpen != null &&
                    current.gameObject == m_MainPanelToHideWhenDetailOpen)
                {
                    break;
                }

                current = current.parent;
            }
        }

        private Transform ResolvePrimaryInfoRoot()
        {
            Transform currentParent = m_CurrentExhibitText != null ? m_CurrentExhibitText.transform.parent : null;
            if (currentParent == null)
                return null;

            if (m_CurrentExhibitIndexText != null && m_CurrentExhibitIndexText.transform.parent != currentParent)
                return null;

            if (m_StatusText != null && m_StatusText.transform.parent != currentParent)
                return null;

            return currentParent;
        }

        private MonoBehaviour ResolveAnimManagerForObject(GameObject target)
        {
            if (target == null)
                return null;

            var managerOnRoot = target.GetComponent(PanelAppearAnimManagerTypeName) as MonoBehaviour;
            if (managerOnRoot != null)
                return managerOnRoot;

            return FindNamedComponentInChildren(target.transform);
        }

        public void PlayPanelAppearAnimationForPlacementRecall()
        {
            if (m_PanelAppearAnimManager == null)
                return;

            InvokeAnimManagerMethod(m_PanelAppearAnimManager, "startAppear", false);
        }

        public void PlayPanelDisappearAnimationForPlacementRecall()
        {
            if (m_PanelAppearAnimManager == null)
                return;

            InvokeAnimManagerMethod(m_PanelAppearAnimManager, "startDisappear", false);
        }

        private void ResetPanelAppearAnimationForHide()
        {
            if (m_PanelAppearAnimManager == null)
                return;

            InvokeAnimManagerMethod(m_PanelAppearAnimManager, "startDisappear", true);
        }

        private static MonoBehaviour FindNamedComponentInChildren(Transform root)
        {
            if (root == null)
                return null;

            var behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                var behaviour = behaviours[i];
                if (behaviour != null && behaviour.GetType().Name == PanelAppearAnimManagerTypeName)
                    return behaviour;
            }

            return null;
        }

        private static Transform FindNamedChildRecursive(Transform root, string childName)
        {
            if (root == null)
                return null;

            if (string.Equals(root.name, childName))
                return root;

            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                Transform found = FindNamedChildRecursive(child, childName);
                if (found != null)
                    return found;
            }

            return null;
        }

        private static void InvokeAnimManagerMethod(MonoBehaviour animManager, string methodName, bool direct)
        {
            if (animManager == null)
                return;

            MethodInfo method = animManager.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
            if (method == null)
                return;

            method.Invoke(animManager, new object[] { direct });
        }

        private static string GetAnimManagerStateName(MonoBehaviour animManager)
        {
            if (animManager == null)
                return string.Empty;

            FieldInfo field = animManager.GetType().GetField("currentState", BindingFlags.Instance | BindingFlags.Public);
            object stateValue = field != null ? field.GetValue(animManager) : null;
            return stateValue != null ? stateValue.ToString() : string.Empty;
        }

        private void RefreshCurrentExhibitLabel(bool force = false)
        {
            if (m_CurrentExhibitText == null) return;

            string nextLabel = BuildCurrentExhibitLabel();
            if (!force && string.Equals(m_LastCurrentExhibitLabel, nextLabel))
                return;

            m_LastCurrentExhibitLabel = nextLabel;
            m_CurrentExhibitText.text = nextLabel;
        }

        private void RefreshCurrentExhibitIndexLabel(bool force = false)
        {
            if (m_CurrentExhibitIndexText == null) return;

            string nextLabel = BuildCurrentExhibitIndexLabel();
            if (!force && string.Equals(m_LastCurrentExhibitIndexLabel, nextLabel))
                return;

            m_LastCurrentExhibitIndexLabel = nextLabel;
            m_CurrentExhibitIndexText.text = nextLabel;
        }

        private void RefreshCurrentExhibitStatusLabel(bool force = false)
        {
            if (m_StatusText == null) return;

            string nextLabel = BuildCurrentExhibitStatusLabel();
            if (!force && string.Equals(m_LastCurrentExhibitStatusLabel, nextLabel))
                return;

            m_LastCurrentExhibitStatusLabel = nextLabel;
            m_StatusText.text = nextLabel;
        }

        private void RefreshSpawnButtonLabel(bool force = false)
        {
            if (m_SpawnButton == null)
                return;

            string nextLabel = "放置当前展品";
            bool interactable = m_Spawner != null && m_PlacementDirector != null && m_PlacementDirector.IsPlacementMode;

            if (IsBasePlacementTabActive())
            {
                bool hasRoot = m_Spawner != null && m_Spawner.HasRootMarker;
                nextLabel = hasRoot ? "定位点已生成" : "生成定位点";
                interactable = m_Spawner != null &&
                               m_PlacementDirector != null &&
                               m_PlacementDirector.IsPlacementMode &&
                               !hasRoot;
            }
            else if (IsStagePlacementTabActive())
            {
                bool hasRoot = m_Spawner != null && m_Spawner.HasRootMarker;
                if (!TryGetSelectedStageModule(out _, out string stageModuleId, out _, out _, out _))
                {
                    nextLabel = "未配置展台";
                    interactable = false;
                }
                else if (m_Spawner != null && m_Spawner.TryGetStageModulePlaced(stageModuleId, out _))
                {
                    nextLabel = "展台已放置";
                    interactable = false;
                }
                else
                {
                    nextLabel = hasRoot ? "放置展台" : "请先生成定位点";
                    interactable = hasRoot;
                }
            }
            else if (m_PlacementDirector == null ||
                     !m_PlacementDirector.IsPlacementMode ||
                     !TryGetCurrentExhibitPresentation(out _, out _, out _, out _))
            {
                nextLabel = "无可放置展品";
                interactable = false;
            }
            else if (m_PlacementDirector.TryGetCurrentStepStatus(out var currentStatus) &&
                     currentStatus == ChapterPlacementStepStatus.RootMissing)
            {
                nextLabel = "生成定位点";
                interactable = m_Spawner != null;
            }
            else if (m_Spawner == null || !m_Spawner.HasRootMarker)
            {
                nextLabel = "请先生成定位点";
                interactable = false;
            }

            if (!force && string.Equals(m_LastSpawnButtonLabel, nextLabel))
            {
                if (m_SpawnButton.interactable != interactable)
                    m_SpawnButton.interactable = interactable;
                return;
            }

            m_LastSpawnButtonLabel = nextLabel;
            SetButtonLabel(m_SpawnButton, nextLabel);
            m_SpawnButton.interactable = interactable;
        }

        private void RefreshToggleContentVisibilityButtonState(bool force = false)
        {
            if (m_ToggleContentVisibilityButton == null)
                return;

            bool interactable = false;
            string nextLabel = "隐藏内容";

            if (IsBasePlacementTabActive())
            {
                nextLabel = "隐藏定位点";
                if (m_PlacementDirector != null &&
                    m_PlacementDirector.IsPlacementMode &&
                    m_Spawner != null &&
                    m_Spawner.HasRootMarker)
                {
                    bool isVisible = m_Spawner.IsRootMarkerVisible;
                    nextLabel = isVisible ? "隐藏定位点" : "显示定位点";
                    interactable = true;
                }
            }
            else if (IsStagePlacementTabActive())
            {
                nextLabel = "隐藏展台";
                if (m_PlacementDirector != null &&
                    m_PlacementDirector.IsPlacementMode &&
                    m_Spawner != null &&
                    TryGetSelectedStageModule(out _, out string moduleId, out _, out _, out _) &&
                    m_Spawner.TryGetStagePlacementContentVisibilityState(
                        moduleId,
                        out bool hasPlacedStage,
                        out bool canToggle,
                        out bool isHidden) &&
                    hasPlacedStage)
                {
                    nextLabel = isHidden ? "显示展台" : "隐藏展台";
                    interactable = canToggle;
                }
            }
            else if (IsExhibitPlacementTabActive() &&
                     m_PlacementDirector != null &&
                     m_Spawner != null &&
                     m_PlacementDirector.IsPlacementMode &&
                     m_Spawner.TryGetCurrentStepPlacementContentVisibilityState(
                        out bool hasPlacedExhibit,
                        out bool canToggle,
                        out bool isHidden) &&
                     hasPlacedExhibit)
            {
                nextLabel = isHidden ? "显示内容" : "隐藏内容";
                interactable = canToggle;
            }

            if (force || !string.Equals(m_LastToggleContentVisibilityButtonLabel, nextLabel))
            {
                m_LastToggleContentVisibilityButtonLabel = nextLabel;
                SetButtonLabel(m_ToggleContentVisibilityButton, nextLabel);
            }

            if (m_ToggleContentVisibilityButton.interactable != interactable)
                m_ToggleContentVisibilityButton.interactable = interactable;
        }

        private void RefreshDeleteCurrentPlacementButtonState()
        {
            if (m_DeleteCurrentPlacementButton == null)
                return;

            string label = "删除当前项";
            bool interactable = false;

            if (IsBasePlacementTabActive())
            {
                label = "删除定位点";
                interactable = m_Spawner != null && m_Spawner.HasRootMarker;
            }
            else if (IsStagePlacementTabActive())
            {
                label = "删除展台";
                interactable = m_Spawner != null && TryGetSelectedStagePlacementTarget(out _);
            }
            else
            {
                if (m_PlacementDirector != null &&
                    m_PlacementDirector.IsPlacementMode &&
                    m_PlacementDirector.TryGetCurrentStepStatus(out var currentStatus) &&
                    (currentStatus == ChapterPlacementStepStatus.RootMissing ||
                     currentStatus == ChapterPlacementStepStatus.RootUnsaved ||
                     currentStatus == ChapterPlacementStepStatus.RootSaved))
                {
                    label = "删除定位点";
                    interactable = m_Spawner != null && m_Spawner.HasRootMarker;
                }
                else
                {
                    label = "删除展品";
                    interactable = m_PlacementDirector != null &&
                                   m_PlacementDirector.IsPlacementMode &&
                                   m_Spawner != null &&
                                   m_Spawner.TryGetCurrentStepPlacedExhibitTransform(out _);
                }
            }

            SetButtonLabel(m_DeleteCurrentPlacementButton, label);
            if (m_DeleteCurrentPlacementButton.interactable != interactable)
                m_DeleteCurrentPlacementButton.interactable = interactable;
        }

        private void RefreshPlacementNavigationButtonState()
        {
            if (m_SaveClosestButton != null)
            {
                SetButtonLabel(m_SaveClosestButton, "上一项");
                m_SaveClosestButton.interactable = CanNavigateToPreviousPlacementItem();
            }

            if (m_DeleteClosestButton != null)
            {
                SetButtonLabel(m_DeleteClosestButton, "下一项");
                m_DeleteClosestButton.interactable = CanNavigateToNextPlacementItem();
            }
        }

        private void RefreshPrimaryPlacementActionButtonVisibility()
        {
            GetPrimaryPlacementActionVisibility(
                out bool showSpawnButton,
                out bool showToggleButton,
                out bool showDeleteButton);

            SetButtonVisible(m_SpawnButton, showSpawnButton);
            SetButtonVisible(m_ToggleContentVisibilityButton, showToggleButton);
            SetButtonVisible(m_DeleteCurrentPlacementButton, showDeleteButton);
        }

        private void GetPrimaryPlacementActionVisibility(
            out bool showSpawnButton,
            out bool showToggleButton,
            out bool showDeleteButton)
        {
            showSpawnButton = false;
            showToggleButton = false;
            showDeleteButton = false;

            if (m_PlacementDirector == null || !m_PlacementDirector.IsPlacementMode)
                return;

            if (IsBasePlacementTabActive())
            {
                bool hasRoot = m_Spawner != null && m_Spawner.HasRootMarker;
                showSpawnButton = !hasRoot;
                showToggleButton = hasRoot;
                showDeleteButton = hasRoot;
                return;
            }

            if (IsStagePlacementTabActive())
            {
                bool hasPlacedStage = TryGetSelectedStagePlacementTarget(out _);
                showSpawnButton = !hasPlacedStage;
                showToggleButton = hasPlacedStage;
                showDeleteButton = hasPlacedStage;
                return;
            }

            showSpawnButton = true;
            if (!m_PlacementDirector.TryGetCurrentStepStatus(out var currentStatus))
                return;

            showSpawnButton =
                currentStatus == ChapterPlacementStepStatus.RootMissing ||
                currentStatus == ChapterPlacementStepStatus.Unplaced;

            showToggleButton =
                currentStatus == ChapterPlacementStepStatus.PlacedUnsaved ||
                currentStatus == ChapterPlacementStepStatus.Saved;
            showDeleteButton =
                currentStatus == ChapterPlacementStepStatus.RootUnsaved ||
                currentStatus == ChapterPlacementStepStatus.RootSaved ||
                showToggleButton;
        }

        private bool ShouldForcePrimaryPlacementActionVisibilityRefresh()
        {
            GetPrimaryPlacementActionVisibility(
                out bool showSpawnButton,
                out bool showToggleButton,
                out bool showDeleteButton);

            return IsButtonVisibilityDrifted(m_SpawnButton, showSpawnButton) ||
                   IsButtonVisibilityDrifted(m_ToggleContentVisibilityButton, showToggleButton) ||
                   IsButtonVisibilityDrifted(m_DeleteCurrentPlacementButton, showDeleteButton);
        }

        private bool ShouldForceBatchActionButtonVisibilityRefresh()
        {
            bool shouldShow = ShouldShowBatchActionButtons();
            return IsButtonVisibilityDrifted(m_DeployAllButton, shouldShow) ||
                   IsButtonVisibilityDrifted(m_ClearSceneButton, shouldShow) ||
                   IsButtonVisibilityDrifted(m_SyncLayoutFromServerButton, shouldShow) ||
                   IsButtonVisibilityDrifted(m_ClearLayoutButton, shouldShow);
        }

        private static void SetButtonVisible(Button button, bool visible)
        {
            if (button == null || button.gameObject == null)
                return;

            if (button.gameObject.activeSelf != visible)
                button.gameObject.SetActive(visible);
        }

        private static bool IsButtonVisibilityDrifted(Button button, bool expectedVisible)
        {
            return button != null &&
                   button.gameObject != null &&
                   button.gameObject.activeSelf != expectedVisible;
        }

        private void RefreshBatchActionButtonState(bool force = false)
        {
            bool shouldShow = ShouldShowBatchActionButtons();
            SetButtonVisible(m_DeployAllButton, shouldShow);
            SetButtonVisible(m_ClearSceneButton, shouldShow);
            SetButtonVisible(m_SyncLayoutFromServerButton, shouldShow);
            SetButtonVisible(m_ClearLayoutButton, shouldShow);

            bool dialogOpen = IsBatchActionDialogVisible();

            bool interactable = shouldShow && !dialogOpen;

            if (m_DeployAllButton != null && (force || m_DeployAllButton.interactable != interactable))
                m_DeployAllButton.interactable = interactable;

            if (m_ClearSceneButton != null && (force || m_ClearSceneButton.interactable != interactable))
                m_ClearSceneButton.interactable = interactable;

            if (m_SyncLayoutFromServerButton != null)
            {
                bool isSyncing = m_Spawner != null && m_Spawner.IsRemoteLayoutSyncInProgress;
                string label = isSyncing ? "同步中..." : "同步 layout";
                SetButtonLabel(m_SyncLayoutFromServerButton, label);

                bool syncInteractable = interactable && !isSyncing;
                if (force || m_SyncLayoutFromServerButton.interactable != syncInteractable)
                    m_SyncLayoutFromServerButton.interactable = syncInteractable;
            }

            if (m_ClearLayoutButton != null)
            {
                bool isSyncing = m_Spawner != null && m_Spawner.IsRemoteLayoutSyncInProgress;
                bool hasLayout = m_Spawner != null && m_Spawner.HasUsableLayoutSnapshot();
                SetButtonLabel(m_ClearLayoutButton, "清空 layout");

                bool clearLayoutInteractable = interactable && !isSyncing && hasLayout;
                if (force || m_ClearLayoutButton.interactable != clearLayoutInteractable)
                    m_ClearLayoutButton.interactable = clearLayoutInteractable;
            }
        }

        private bool ShouldShowBatchActionButtons()
        {
            return m_PlacementDirector != null &&
                   m_PlacementDirector.IsPlacementMode &&
                   IsBasePlacementTabActive();
        }

        private string BuildCurrentExhibitLabel()
        {
            if (IsBasePlacementTabActive())
                return "当前定位点：定位点";

            if (IsStagePlacementTabActive())
            {
                if (!TryGetSelectedStageModule(out _, out _, out string displayName, out _, out _))
                    return "当前展台：-";

                return $"当前展台：{displayName}";
            }

            if (!TryGetCurrentExhibitPresentation(out string stepDisplayName, out _, out _, out _))
                return "当前展品：-";

            return $"当前展品：{stepDisplayName}";
        }

        private string BuildCurrentExhibitIndexLabel()
        {
            if (IsBasePlacementTabActive())
                return "编号：定位点";

            if (IsStagePlacementTabActive())
            {
                if (!TryGetSelectedStageModule(out _, out _, out _, out int stageNumber, out int totalStages))
                    return "编号：-- / --";

                int stageWidth = Mathf.Max(2, totalStages.ToString().Length);
                return $"编号：{stageNumber.ToString().PadLeft(stageWidth, '0')} / {totalStages.ToString().PadLeft(stageWidth, '0')}";
            }

            if (!TryGetCurrentExhibitPresentation(out _, out _, out int stepNumber, out int totalSteps))
                return "编号：-- / --";

            int width = Mathf.Max(2, totalSteps.ToString().Length);
            return $"编号：{stepNumber.ToString().PadLeft(width, '0')} / {totalSteps.ToString().PadLeft(width, '0')}";
        }

        private string BuildCurrentExhibitStatusLabel()
        {
            if (IsBasePlacementTabActive())
            {
                if (m_Spawner == null)
                    return "状态：未绑定 Spawner";

                if (!m_Spawner.HasRootMarker)
                    return "状态：未生成";

                bool isVisible = m_Spawner.IsRootMarkerVisible;
                if (!m_Spawner.HasSavedRootAnchor)
                    return isVisible ? "状态：已放置" : "状态：已放置（已隐藏）";

                return isVisible ? "状态：已就绪" : "状态：已就绪（已隐藏）";
            }

            if (IsStagePlacementTabActive())
            {
                if (!TryGetSelectedStageModule(out _, out string stageModuleId, out _, out _, out _))
                    return "状态：未配置展台";

                if (m_Spawner == null)
                    return "状态：未绑定 Spawner";

                if (!m_Spawner.HasRootMarker && !m_Spawner.TryGetStageModulePlaced(stageModuleId, out _))
                    return "状态：请先生成定位点";

                if (!m_Spawner.TryGetStagePlacementContentVisibilityState(
                        stageModuleId,
                        out bool hasPlacedStage,
                        out _,
                        out bool stageIsHidden) ||
                    !hasPlacedStage)
                {
                    return "状态：未放置";
                }

                return $"状态：已放置（内容{(stageIsHidden ? "已隐藏" : "已显示")}）";
            }

            if (m_PlacementDirector == null || !m_PlacementDirector.TryGetCurrentStepStatus(out var status))
                return "状态：-";

            string baseLabel;
            switch (status)
            {
                case ChapterPlacementStepStatus.RootMissing:
                    baseLabel = "状态：请先生成定位点";
                    break;
                case ChapterPlacementStepStatus.RootUnsaved:
                    baseLabel = "状态：已放置";
                    break;
                case ChapterPlacementStepStatus.RootSaved:
                    baseLabel = "状态：已就绪";
                    break;
                case ChapterPlacementStepStatus.PlacedUnsaved:
                case ChapterPlacementStepStatus.Saved:
                    baseLabel = "状态：已放置";
                    break;
                case ChapterPlacementStepStatus.Unplaced:
                    baseLabel = "状态：未放置";
                    break;
                default:
                    baseLabel = "状态：-";
                    break;
            }

            if (status != ChapterPlacementStepStatus.PlacedUnsaved &&
                status != ChapterPlacementStepStatus.Saved)
            {
                return baseLabel;
            }

            if (m_Spawner == null ||
                !m_Spawner.TryGetCurrentStepPlacementContentVisibilityState(
                    out bool hasPlacedExhibit,
                    out _,
                    out bool isHidden) ||
                !hasPlacedExhibit)
            {
                return baseLabel;
            }

            return $"{baseLabel}（内容{(isHidden ? "已隐藏" : "已显示")}）";
        }

        private bool CanNavigateToPreviousPlacementItem()
        {
            if (m_PlacementDirector == null || !m_PlacementDirector.IsPlacementMode)
                return false;

            if (IsBasePlacementTabActive())
                return false;

            if (IsStagePlacementTabActive())
                return true;

            if (!TryGetCurrentExhibitPresentation(out _, out _, out _, out _))
                return true;

            return true;
        }

        private bool CanNavigateToNextPlacementItem()
        {
            if (m_PlacementDirector == null || !m_PlacementDirector.IsPlacementMode)
                return false;

            if (IsBasePlacementTabActive())
                return HasConfiguredStageModules() || TryGetCurrentExhibitPresentation(out _, out _, out _, out _);

            if (IsStagePlacementTabActive())
            {
                int stageCount = GetConfiguredStageModuleCount();
                if (stageCount == 0)
                    return TryGetCurrentExhibitPresentation(out _, out _, out _, out _);

                return m_SelectedStageIndex < stageCount - 1 || TryGetCurrentExhibitPresentation(out _, out _, out _, out _);
            }

            if (!TryGetCurrentExhibitPresentation(out _, out _, out int stepNumber, out int totalSteps))
                return false;

            return stepNumber < totalSteps;
        }

        // 提供给 Editor 调试按钮与 UI Button 的统一入口
        public void OnSpawnClicked()
        {
            HideBatchActionDialog(cancelPendingAction: true);
            if (!IsGuidedReady())
            {
                m_Spawner?.onStatusMessage?.Invoke("引导模式未就绪，无法放置。");
                return;
            }

            if (IsBasePlacementTabActive())
            {
                if (m_Spawner == null)
                    return;

                if (!m_Spawner.HasRootMarker)
                    m_Spawner.TryCreateRootMarkerAtDefaultPose();

                RequestUiRefresh(force: true);
                return;
            }

            if (IsStagePlacementTabActive())
            {
                if (m_Spawner == null)
                    return;

                if (!TryGetSelectedStageModule(out _, out string moduleId, out _, out _, out _))
                {
                    m_Spawner.onStatusMessage?.Invoke("当前未配置展台，无法放置。");
                    return;
                }

                if (!m_Spawner.HasRootMarker && !m_Spawner.TryGetStageModulePlaced(moduleId, out _))
                {
                    m_Spawner.onStatusMessage?.Invoke("请先在“定位”页签生成定位点。");
                    return;
                }

                if (m_Spawner.TryGetStageModulePlaced(moduleId, out var existingStage))
                    m_Spawner.SetManualSelection(existingStage);
                else
                    m_Spawner.PlaceStageModule(moduleId, null, true);

                RequestUiRefresh(force: true);
                return;
            }

            if (m_Spawner != null &&
                !m_Spawner.HasRootMarker &&
                (!m_PlacementDirector.TryGetCurrentStepStatus(out var currentStatus) ||
                 currentStatus != ChapterPlacementStepStatus.RootMissing))
            {
                m_Spawner.onStatusMessage?.Invoke("请先在“定位”页签生成定位点。");
                return;
            }

            m_PlacementDirector.PlaceCurrentStep();
            RequestUiRefresh(force: true);
        }

        public void OnSaveClosestClicked()
        {
            HideBatchActionDialog(cancelPendingAction: true);
            if (!IsGuidedReady())
            {
                m_Spawner?.onStatusMessage?.Invoke("引导模式未就绪，无法切换到上个展品。");
                return;
            }

            if (IsBasePlacementTabActive())
                return;

            if (IsStagePlacementTabActive())
            {
                if (HasConfiguredStageModules() && m_SelectedStageIndex > 0)
                    m_SelectedStageIndex--;
                else
                    m_ActivePlacementTab = PlacementTabType.Base;

                SyncSelectionForActivePlacementTab();
                RequestUiRefresh(force: true);
                return;
            }

            if (!TryGetCurrentExhibitPresentation(out _, out _, out int stepNumber, out _))
            {
                if (HasConfiguredStageModules())
                    SelectLastStageTab();
                else
                    SetPlacementTab(PlacementTabType.Base);
                return;
            }

            if (stepNumber > 1)
            {
                m_PlacementDirector.MoveToPreviousStep();
                RequestUiRefresh(force: true);
                return;
            }

            if (HasConfiguredStageModules())
                SelectLastStageTab();
            else
                SetPlacementTab(PlacementTabType.Base);
        }

        public void OnDeleteClosestClicked()
        {
            HideBatchActionDialog(cancelPendingAction: true);
            if (!IsGuidedReady())
            {
                m_Spawner?.onStatusMessage?.Invoke("引导模式未就绪，无法切换到下个展品。");
                return;
            }

            if (IsBasePlacementTabActive())
            {
                SetPlacementTab(HasConfiguredStageModules() ? PlacementTabType.Stage : PlacementTabType.Exhibit);
                return;
            }

            if (IsStagePlacementTabActive())
            {
                int stageCount = GetConfiguredStageModuleCount();
                if (stageCount > 0 && m_SelectedStageIndex < stageCount - 1)
                {
                    m_SelectedStageIndex++;
                    SyncSelectionForActivePlacementTab();
                    RequestUiRefresh(force: true);
                    return;
                }

                SetPlacementTab(PlacementTabType.Exhibit);
                return;
            }

            if (!TryGetCurrentExhibitPresentation(out _, out _, out int stepNumber, out int totalSteps))
                return;

            if (stepNumber < totalSteps)
            {
                m_PlacementDirector.MoveToNextStep();
                RequestUiRefresh(force: true);
            }
        }

        public void OnDeleteCurrentPlacementClicked()
        {
            HideBatchActionDialog(cancelPendingAction: true);
            if (!IsGuidedReady())
            {
                m_Spawner?.onStatusMessage?.Invoke("引导模式未就绪，无法删除当前展品。");
                return;
            }

            if (IsBasePlacementTabActive())
            {
                m_Spawner?.TryDeleteRootMarker();
                RequestUiRefresh(force: true);
                return;
            }

            if (IsStagePlacementTabActive())
            {
                if (TryGetSelectedStageModule(out _, out string moduleId, out _, out _, out _))
                    m_Spawner?.DeleteStageModule(moduleId);
                RequestUiRefresh(force: true);
                return;
            }

            m_PlacementDirector.DeleteCurrentStepPlacement();
            RequestUiRefresh(force: true);
        }

        public void OnDeployAllClicked()
        {
            if (!m_EnableBatchPlacementActions)
            {
                m_Spawner?.onStatusMessage?.Invoke("一键布展暂未启用。");
                return;
            }

            if (!IsGuidedReady())
            {
                m_Spawner?.onStatusMessage?.Invoke("引导模式未就绪，无法一键布展。");
                return;
            }

            if (!ShowBatchActionDialog(PendingBatchAction.DeployAll))
                ExecutePendingBatchAction(PendingBatchAction.DeployAll);
        }

        public void OnClearSceneClicked()
        {
            if (!m_EnableBatchPlacementActions)
            {
                m_Spawner?.onStatusMessage?.Invoke("一键清展暂未启用。");
                return;
            }

            if (!IsGuidedReady())
            {
                m_Spawner?.onStatusMessage?.Invoke("引导模式未就绪，无法一键清展。");
                return;
            }

            if (!ShowBatchActionDialog(PendingBatchAction.ClearScene))
                ExecutePendingBatchAction(PendingBatchAction.ClearScene);
        }

        public void OnSyncLayoutFromServerClicked()
        {
            if (!IsGuidedReady())
            {
                m_Spawner?.onStatusMessage?.Invoke("引导模式未就绪，无法从服务器同步 layout。");
                return;
            }

            if (!ShowBatchActionDialog(PendingBatchAction.SyncLayoutFromServer))
                ExecutePendingBatchAction(PendingBatchAction.SyncLayoutFromServer);
        }

        public void OnClearLayoutClicked()
        {
            if (!IsGuidedReady())
            {
                m_Spawner?.onStatusMessage?.Invoke("引导模式未就绪，无法清空 layout。");
                return;
            }

            if (!ShowBatchActionDialog(PendingBatchAction.ClearLayout))
                ExecutePendingBatchAction(PendingBatchAction.ClearLayout);
        }

        public void OnEnterPlacementModeClicked()
        {
            HideBatchActionDialog(cancelPendingAction: true);
            if (!IsGuidedReady())
            {
                m_Spawner?.onStatusMessage?.Invoke("引导模式未就绪，无法切换到布展模式。");
                return;
            }
            m_PlacementDirector.EnterPlacementMode();
        }

        public void OnEnterExperienceModeClicked()
        {
            HideBatchActionDialog(cancelPendingAction: true);
            if (!IsGuidedReady())
            {
                m_Spawner?.onStatusMessage?.Invoke("引导模式未就绪，无法切换到体验模式。");
                return;
            }
            m_PlacementDirector.EnterExperienceMode();
        }

        public void OnEditorSimulatePanelGestureClicked()
        {
            if (m_PanelPalmRelocator != null && m_PanelPalmRelocator.TriggerRecallForEditorDebug())
                return;

            ShowPanelForPlacementRecall();
        }

        public void OnToggleContentVisibilityClicked()
        {
            HideBatchActionDialog(cancelPendingAction: true);
            PlacementDebugFileLogger.Log(
                $"[PlacementUI] click toggleContentVisibility guidedReady={IsGuidedReady()}, " +
                $"hasDirector={(m_PlacementDirector != null)}, placementMode={(m_PlacementDirector != null && m_PlacementDirector.IsPlacementMode)}, " +
                $"rootNavigation={(m_PlacementDirector != null && m_PlacementDirector.IsRootNavigationActive)}");
            if (!IsGuidedReady())
            {
                m_Spawner?.onStatusMessage?.Invoke("引导模式未就绪，无法切换当前内容显示。");
                return;
            }

            if (m_PlacementDirector == null || !m_PlacementDirector.IsPlacementMode)
            {
                m_Spawner?.onStatusMessage?.Invoke("当前步骤不支持切换内容显示。");
                return;
            }

            bool toggled = false;
            if (IsBasePlacementTabActive())
            {
                toggled = m_Spawner != null && m_Spawner.TrySetRootMarkerVisible(!m_Spawner.IsRootMarkerVisible);
            }
            else if (IsStagePlacementTabActive())
            {
                if (!TryGetSelectedStageModule(out _, out string moduleId, out _, out _, out _))
                {
                    m_Spawner?.onStatusMessage?.Invoke("当前未配置展台，无法切换内容显示。");
                    return;
                }

                toggled = m_Spawner != null && m_Spawner.TryToggleStagePlacementContentVisibility(moduleId);
            }
            else if (IsExhibitPlacementTabActive())
            {
                toggled = m_Spawner != null && m_Spawner.TryToggleCurrentStepPlacementContentVisibility();
            }
            else
            {
                m_Spawner?.onStatusMessage?.Invoke("当前没有可切换显示的对象。");
                return;
            }
            PlacementDebugFileLogger.Log($"[PlacementUI] click toggleContentVisibility result={toggled}");
            RequestUiRefresh(force: true);
        }

        private void HandleStatusMessage(string msg)
        {
            m_LastStatusMessage = msg ?? "";
            RequestUiRefresh();
        }

        private IEnumerator ShowPanelTemporarilyCoroutine(float visibleSeconds)
        {
            PreparePanelForPlacementRecall();
            PlayPanelAppearAnimationForPlacementRecall();

            yield return WaitForPanelAnimationState("appeared", 2f);
            yield return new WaitForSecondsRealtime(visibleSeconds);

            PlayPanelDisappearAnimationForPlacementRecall();
            yield return WaitForPanelAnimationState("disappeared", 2f);

            m_PlacementVisibilityInitialized = true;
            m_PlacementPanelHiddenByRecall = true;
            RefreshPanelVisibility(force: true);
            m_TemporaryPlacementPanelRoutine = null;
        }

        private IEnumerator WaitForPanelAnimationState(string expectedState, float timeoutSeconds)
        {
            if (m_PanelAppearAnimManager == null)
                yield break;

            float elapsed = 0f;
            while (elapsed < timeoutSeconds)
            {
                if (string.Equals(GetAnimManagerStateName(m_PanelAppearAnimManager), expectedState))
                    yield break;

                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        private void OnConfirmBatchActionClicked()
        {
            PendingBatchAction action = m_PendingBatchAction;
            if (m_BatchActionDialogTransitionRoutine != null)
                StopCoroutine(m_BatchActionDialogTransitionRoutine);

            m_BatchActionDialogTransitionRoutine = StartCoroutine(HideBatchActionDialogAndThen(action));
        }

        private void OnCancelBatchActionClicked()
        {
            HideBatchActionDialog(cancelPendingAction: true);
            m_Spawner?.onStatusMessage?.Invoke("已取消批量操作。");
        }

        private bool ShowBatchActionDialog(PendingBatchAction action)
        {
            ResolveRuntimeBatchActionDialog();
            if (m_BatchActionDialog == null)
                return false;

            if (m_BatchActionDialogTransitionRoutine != null)
            {
                StopCoroutine(m_BatchActionDialogTransitionRoutine);
                m_BatchActionDialogTransitionRoutine = null;
            }

            PrepareBatchActionDialogForAppear();
            m_PendingBatchAction = action;
            SetBatchActionDialogMessage(BuildBatchActionDialogMessage(action));
            SetBatchActionDialogVisible(true);
            if (m_BatchActionDialogAppearAnimManager != null)
                InvokeAnimManagerMethod(m_BatchActionDialogAppearAnimManager, "startAppear", false);
            RefreshBatchActionButtonState(force: true);
            return true;
        }

        private void HideBatchActionDialog(bool cancelPendingAction, bool immediate = false)
        {
            if (cancelPendingAction)
                m_PendingBatchAction = PendingBatchAction.None;

            if (m_BatchActionDialogTransitionRoutine != null)
            {
                StopCoroutine(m_BatchActionDialogTransitionRoutine);
                m_BatchActionDialogTransitionRoutine = null;
            }

            if (immediate || !Application.isPlaying)
            {
                ResetBatchActionDialogAppearAnimationForHide();
                SetBatchActionDialogVisible(false);
                RefreshBatchActionButtonState(force: true);
                return;
            }

            if (m_BatchActionDialog != null && m_BatchActionDialog.activeSelf)
                m_BatchActionDialogTransitionRoutine = StartCoroutine(HideBatchActionDialogCoroutine());
            else
            {
                ResetBatchActionDialogAppearAnimationForHide();
                SetBatchActionDialogVisible(false);
            }

            RefreshBatchActionButtonState(force: true);
        }

        private bool IsBatchActionDialogVisible()
        {
            return m_BatchActionDialog != null && m_BatchActionDialog.activeSelf;
        }

        private void SetBatchActionDialogVisible(bool visible)
        {
            if (m_BatchActionDialog == null)
                return;

            if (m_BatchActionDialog.activeSelf != visible)
                m_BatchActionDialog.SetActive(visible);
        }

        private void SetBatchActionDialogMessage(string message)
        {
            if (string.IsNullOrEmpty(message))
                return;

            if (m_BatchActionDialogTmpText != null)
            {
                if (!string.Equals(m_BatchActionDialogTmpText.text, message))
                    m_BatchActionDialogTmpText.text = message;
                return;
            }

            if (m_BatchActionDialogText != null && !string.Equals(m_BatchActionDialogText.text, message))
                m_BatchActionDialogText.text = message;
        }

        private static string BuildBatchActionDialogMessage(PendingBatchAction action)
        {
            switch (action)
            {
                case PendingBatchAction.DeployAll:
                    return "确定要执行一键布展操作吗？";
                case PendingBatchAction.ClearScene:
                    return "确定要执行一键清展操作吗？";
                case PendingBatchAction.SyncLayoutFromServer:
                    return "确定要从服务器同步 layout 吗？这会覆盖当前本地 layout 数据，但不会自动重摆。";
                case PendingBatchAction.ClearLayout:
                    return "确定要清空当前本地 layout 数据吗？这不会删除当前场景中的展品，也不会清空 session。";
                default:
                    return "确定要执行当前批量操作吗？";
            }
        }

        private void ExecutePendingBatchAction(PendingBatchAction action)
        {
            switch (action)
            {
                case PendingBatchAction.DeployAll:
                    m_PlacementDirector?.OneClickDeployFromLayout();
                    break;
                case PendingBatchAction.ClearScene:
                    m_PlacementDirector?.OneClickClearPlacement();
                    break;
                case PendingBatchAction.SyncLayoutFromServer:
                    m_PlacementDirector?.SyncLayoutFromServer();
                    RequestUiRefresh(force: true);
                    break;
                case PendingBatchAction.ClearLayout:
                    m_PlacementDirector?.ClearSavedLayout();
                    RequestUiRefresh(force: true);
                    break;
            }
        }

        private IEnumerator HideBatchActionDialogAndThen(PendingBatchAction action)
        {
            yield return HideBatchActionDialogCoroutine();
            ExecutePendingBatchAction(action);
        }

        private IEnumerator HideBatchActionDialogCoroutine()
        {
            if (m_BatchActionDialog == null)
                yield break;

            if (m_BatchActionDialog.activeSelf && m_BatchActionDialogAppearAnimManager != null)
            {
                InvokeAnimManagerMethod(m_BatchActionDialogAppearAnimManager, "startDisappear", false);
                yield return WaitForAnimManagerState(
                    m_BatchActionDialogAppearAnimManager,
                    "disappeared",
                    BatchActionDialogTransitionTimeoutSeconds);
            }

            ResetBatchActionDialogAppearAnimationForHide();
            SetBatchActionDialogVisible(false);
            m_BatchActionDialogTransitionRoutine = null;
            RefreshBatchActionButtonState(force: true);
        }

        private void PrepareBatchActionDialogForAppear()
        {
            if (m_BatchActionDialogAppearAnimManager == null)
                return;

            string stateName = GetAnimManagerStateName(m_BatchActionDialogAppearAnimManager);
            if (string.Equals(stateName, "appeared") ||
                string.Equals(stateName, "appearing") ||
                string.Equals(stateName, "disappearing"))
            {
                ResetBatchActionDialogAppearAnimationForHide();
            }
        }

        private void ResetBatchActionDialogAppearAnimationForHide()
        {
            if (m_BatchActionDialogAppearAnimManager == null)
                return;

            InvokeAnimManagerMethod(m_BatchActionDialogAppearAnimManager, "startDisappear", true);
        }

        private Text ResolveText(string objectName)
        {
            Transform child = FindNamedChildRecursive(transform, objectName);
            if (child == null)
                return null;

            return child.GetComponent<Text>();
        }

        private static Button ResolveButtonInRoot(GameObject root, Button current, params string[] buttonNames)
        {
            if (current != null || root == null)
                return current;

            var allButtons = root.GetComponentsInChildren<Button>(true);
            for (int i = 0; i < allButtons.Length; i++)
            {
                var button = allButtons[i];
                if (button == null)
                    continue;

                for (int nameIndex = 0; nameIndex < buttonNames.Length; nameIndex++)
                {
                    if (string.Equals(button.gameObject.name, buttonNames[nameIndex]))
                        return button;
                }
            }

            return null;
        }

        private void OnGUI()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) return;
            if (!m_EnableEditorGameDebug || !m_EditorGameDebugVisible) return;

            GUILayout.BeginArea(m_EditorGameDebugRect, GUI.skin.window);
            GUILayout.Label("布展调试面板（Game）");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("布展模式")) OnEnterPlacementModeClicked();
            if (GUILayout.Button("体验模式")) OnEnterExperienceModeClicked();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("模拟手势显示面板")) OnEditorSimulatePanelGestureClicked();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            string primaryActionLabel = "放置当前展品";
            if (m_Spawner != null &&
                m_PlacementDirector != null &&
                m_PlacementDirector.IsPlacementMode &&
                !m_Spawner.HasRootMarker)
            {
                primaryActionLabel = "生成定位点";
            }
            if (GUILayout.Button(primaryActionLabel)) OnSpawnClicked();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("隐藏内容 / 显示内容")) OnToggleContentVisibilityClicked();
            if (GUILayout.Button("删除当前展品")) OnDeleteCurrentPlacementClicked();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("上个展品")) OnSaveClosestClicked();
            if (GUILayout.Button("下个展品")) OnDeleteClosestClicked();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("一键布展")) OnDeployAllClicked();
            if (GUILayout.Button("一键清展")) OnClearSceneClicked();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("同步 layout")) OnSyncLayoutFromServerClicked();
            if (GUILayout.Button("清空 layout")) OnClearLayoutClicked();
            GUILayout.EndHorizontal();

            var wrapStyle = new GUIStyle(GUI.skin.label) { wordWrap = true };
            GUILayout.Space(4f);
            GUILayout.Label($"动态提示：{m_LastStatusMessage}", wrapStyle);
            GUILayout.Label(BuildCurrentExhibitLabel(), wrapStyle);

            string mode = m_PlacementDirector == null ? "未知" : (m_PlacementDirector.IsPlacementMode ? "布展" : "体验");
            GUILayout.Label($"当前模式: {mode} | 仅引导模式: {IsGuidedReady()} | 显隐快捷键: {m_EditorGameDebugToggleKey}");
            GUILayout.EndArea();
#endif
        }
    }

    internal sealed class UIPointerDebugLogger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerClickHandler
    {
        [SerializeField] private string m_Label;

        public void SetLabel(string label)
        {
            m_Label = label;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            Debug.Log($"[PanelUI] enter label={m_Label} object={name}");
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            Debug.Log($"[PanelUI] exit label={m_Label} object={name}");
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            Debug.Log($"[PanelUI] down label={m_Label} object={name}");
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            Debug.Log($"[PanelUI] click label={m_Label} object={name}");
        }
    }
}
