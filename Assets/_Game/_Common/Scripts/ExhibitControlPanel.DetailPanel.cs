using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace VFXViewer
{
    public partial class ExhibitControlPanel
    {
        private const string RuntimeDetailPanelRootName = "__RuntimeDetailPanelRoot";
        private const string RuntimeDetailCurrentRotationTextName = "__DetailCurrentRotationText";
        private const string RuntimeDetailInputRotationTextName = "__DetailInputRotationText";
        private const string RuntimeDetailHintTextName = "__DetailHintText";
        private const string ExistingDetailValueContainerName = "GameObject (1)";
        private const string ExistingDetailReturnButtonName = "ReturnButton";
        private const string ExistingDetailConfirmButtonName = "ConfirmButton";
        private const string ExistingDetailDeleteButtonName = "DeleteButton";
        private const string ExistingDetailLegacyDeleteButtonName = "ClearButton";
        private const string ExistingDetailDigitButtonPrefix = "Num";
        private const float DetailPanelTransitionTimeoutSeconds = 2f;
        private const float RuntimeDetailPanelWidth = 360f;
        private const float RuntimeDetailPanelHeight = 380f;
        private const float RuntimeDetailButtonWidth = 72f;
        private const float RuntimeDetailButtonHeight = 46f;

        private Text m_DetailCurrentRotationText;
        private Text m_DetailInputRotationText;
        private Text m_DetailHintText;
        private string m_DetailInputBuffer = string.Empty;
        private bool m_DetailPanelOpen;
        private int m_DetailPanelBoundTargetId = int.MinValue;
        private bool m_DetailUsesCustomLayout;
        private bool m_DetailPanelUiInitialized;
        private bool m_DetailPanelTransitionActive;
        private Coroutine m_DetailPanelTransitionRoutine;
        private readonly Dictionary<Transform, bool> m_MainPanelChildActiveStates = new Dictionary<Transform, bool>();
        private bool m_ExplicitMainPanelActiveBeforeDetailOpen;
        private bool m_ExplicitMainPanelActiveStateCaptured;

        private void RefreshDetailPanelState(bool force = false)
        {
            bool canOpen = CanOpenDetailPanel();
            if (m_DetailButton != null && m_DetailButton.interactable != canOpen)
                m_DetailButton.interactable = canOpen;

            if (!canOpen && m_DetailPanelOpen)
                CloseDetailPanel();

            if (m_DetailPanel == null)
                return;

            if (!m_DetailPanelTransitionActive && m_DetailPanel.activeSelf != m_DetailPanelOpen)
            {
                if (!m_DetailPanelOpen)
                    ResetDetailPanelAppearAnimationForHide();

                m_DetailPanel.SetActive(m_DetailPanelOpen);
            }

            if (!m_DetailPanelTransitionActive)
                RefreshDetailPanelPresentation();

            if (!m_DetailPanelOpen)
                return;

            EnsureRuntimeDetailPanelBuilt();

            if (!TryGetCurrentDetailTarget(out var target))
            {
                ApplyDetailTexts("当前旋转：--", "输入角度：--", "当前步骤尚未放置，无法编辑旋转。");
                return;
            }

            int targetId = target.GetInstanceID();
            if (force || m_DetailPanelBoundTargetId != targetId)
            {
                m_DetailPanelBoundTargetId = targetId;
                ResetDetailInputBufferFromCurrentRotation();
            }

            if (!TryGetCurrentDetailTargetYaw(out float yawDegrees))
            {
                ApplyDetailTexts("当前旋转：--", "输入角度：--", GetDetailUnavailableMessage());
                return;
            }

            ApplyDetailTexts(
                $"当前旋转 Y：{FormatSignedAngle(yawDegrees)}°",
                $"输入角度：{(string.IsNullOrEmpty(m_DetailInputBuffer) ? "--" : m_DetailInputBuffer)}",
                "通过数字键盘输入 Y 轴角度，点击“应用”后保存。");
        }

        private bool ShouldForceClosedDetailPanelRefresh()
        {
            return m_DetailPanel != null &&
                   !m_DetailPanelOpen &&
                   !m_DetailPanelTransitionActive &&
                   m_DetailPanel.activeSelf;
        }

        private void RestoreClosedPanelPresentationAfterPanelShow()
        {
            if (m_DetailPanelOpen || m_DetailPanelTransitionActive)
                return;

            if (m_DetailPanel != null && m_DetailPanel.activeSelf)
            {
                ResetDetailPanelAppearAnimationForHide();
                m_DetailPanel.SetActive(false);
            }

            RestoreMainPanelPresentation();
            RestorePrimaryInfoPresentationAfterDetailClose();
        }

        public void OnToggleDetailPanelClicked()
        {
            HideBatchActionDialog(cancelPendingAction: true);
            bool placementMode = m_PlacementDirector != null && m_PlacementDirector.IsPlacementMode;
            PlacementDebugFileLogger.Log(
                $"[PlacementUI] click toggleDetailPanel guidedReady={IsGuidedReady()}, " +
                $"hasDirector={(m_PlacementDirector != null)}, placementMode={placementMode}, " +
                $"rootNavigation={(m_PlacementDirector != null && m_PlacementDirector.IsRootNavigationActive)}, " +
                $"detailOpen={m_DetailPanelOpen}");
            if (!IsGuidedReady())
            {
                m_Spawner?.onStatusMessage?.Invoke("引导模式未就绪，无法打开详情面板。");
                return;
            }

            if (m_PlacementDirector == null || !m_PlacementDirector.IsPlacementMode)
            {
                m_Spawner?.onStatusMessage?.Invoke("当前步骤不支持编辑展品旋转。");
                return;
            }

            if (!CanOpenDetailPanel())
            {
                m_Spawner?.onStatusMessage?.Invoke(GetDetailUnavailableMessage());
                return;
            }

            if (m_DetailPanelOpen)
            {
                CloseDetailPanel();
                return;
            }

            OpenDetailPanel();
        }

        private void OpenDetailPanel()
        {
            EnsureDetailPanelReference();
            EnsureRuntimeDetailPanelBuilt();
            StartDetailPanelTransition(open: true);
        }

        private void CloseDetailPanel()
        {
            StartDetailPanelTransition(open: false);
        }

        private bool CanOpenDetailPanel()
        {
            return m_PlacementDirector != null &&
                   m_PlacementDirector.IsPlacementMode &&
                   TryGetCurrentDetailTarget(out _);
        }

        private bool TryGetCurrentDetailTarget(out Transform target)
        {
            target = null;
            if (m_Spawner == null)
                return false;

            if (IsBasePlacementTabActive())
                return m_Spawner.TryGetRootMarkerTransform(out target);

            if (IsStagePlacementTabActive())
                return TryGetSelectedStagePlacementTarget(out target);

            return m_Spawner.TryGetCurrentStepPlacedExhibitTransform(out target);
        }

        private bool TryGetCurrentDetailTargetYaw(out float yawDegrees)
        {
            yawDegrees = 0f;
            if (m_Spawner == null)
                return false;

            if (IsBasePlacementTabActive())
                return m_Spawner.TryGetRootMarkerYaw(out yawDegrees);

            if (IsStagePlacementTabActive())
            {
                if (!TryGetSelectedStageModule(out _, out string moduleId, out _, out _, out _))
                    return false;

                return m_Spawner.TryGetStageModuleYaw(moduleId, out yawDegrees);
            }

            return m_Spawner.TryGetCurrentStepPlacedExhibitYaw(out yawDegrees);
        }

        private bool TrySetCurrentDetailTargetYaw(float yawDegrees)
        {
            if (m_Spawner == null)
                return false;

            if (IsBasePlacementTabActive())
                return m_Spawner.TrySetRootMarkerYaw(yawDegrees);

            if (IsStagePlacementTabActive())
            {
                if (!TryGetSelectedStageModule(out _, out string moduleId, out _, out _, out _))
                    return false;

                return m_Spawner.TrySetStageModuleYaw(moduleId, yawDegrees);
            }

            return m_Spawner.TrySetCurrentStepPlacedExhibitYaw(yawDegrees);
        }

        private string GetDetailUnavailableMessage()
        {
            if (IsBasePlacementTabActive())
                return "当前没有可编辑的定位点。";

            if (IsStagePlacementTabActive())
            {
                if (!TryGetSelectedStageModule(out _, out _, out _, out _, out _))
                    return "当前未配置展台。";

                return "当前展台尚未放置，无法编辑旋转。";
            }

            return "当前步骤尚未放置，无法编辑旋转。";
        }

        private void ResetDetailInputBufferFromCurrentRotation()
        {
            if (TryGetCurrentDetailTargetYaw(out float yawDegrees))
                m_DetailInputBuffer = FormatSignedAngle(yawDegrees);
            else
                m_DetailInputBuffer = string.Empty;
        }

        private void ApplyDetailTexts(string currentRotationText, string inputText, string hintText)
        {
            if (m_DetailUsesCustomLayout)
            {
                if (m_DetailInputRotationText != null)
                    m_DetailInputRotationText.text = string.IsNullOrEmpty(m_DetailInputBuffer) ? "--" : m_DetailInputBuffer;

                return;
            }

            if (m_DetailCurrentRotationText != null)
                m_DetailCurrentRotationText.text = currentRotationText;

            if (m_DetailInputRotationText != null)
                m_DetailInputRotationText.text = inputText;

            if (m_DetailHintText != null)
                m_DetailHintText.text = hintText;
        }

        private void EnsureDetailPanelReference()
        {
            if (m_DetailPanel != null)
                return;

            Transform detailPanelTransform = FindNamedChildRecursive(transform, DetailPanelObjectName);
            if (detailPanelTransform != null)
            {
                m_DetailPanel = detailPanelTransform.gameObject;
                return;
            }

            Transform parent = m_PanelRoot != null ? m_PanelRoot.transform : transform;
            var panelObject = new GameObject(DetailPanelObjectName, typeof(RectTransform));
            panelObject.transform.SetParent(parent, false);
            m_DetailPanel = panelObject;
            m_DetailPanelAppearAnimManager = ResolveDetailPanelAppearAnimManager();
        }

        private void EnsureRuntimeDetailPanelBuilt()
        {
            EnsureDetailPanelReference();
            if (m_DetailPanel == null)
                return;

            if (m_DetailPanelAppearAnimManager == null)
                m_DetailPanelAppearAnimManager = ResolveDetailPanelAppearAnimManager();

            if (TryBindExistingDetailPanelLayout())
                return;

            Transform runtimeRoot = FindNamedChildRecursive(m_DetailPanel.transform, RuntimeDetailPanelRootName);
            if (runtimeRoot == null)
                runtimeRoot = BuildRuntimeDetailPanel(m_DetailPanel.transform);

            if (runtimeRoot == null)
                return;

            m_DetailCurrentRotationText = FindTextInChildren(runtimeRoot, RuntimeDetailCurrentRotationTextName);
            m_DetailInputRotationText = FindTextInChildren(runtimeRoot, RuntimeDetailInputRotationTextName);
            m_DetailHintText = FindTextInChildren(runtimeRoot, RuntimeDetailHintTextName);
            m_DetailUsesCustomLayout = false;
            m_DetailPanelUiInitialized = true;
        }

        private bool TryBindExistingDetailPanelLayout()
        {
            if (m_DetailPanelUiInitialized)
                return m_DetailUsesCustomLayout;

            if (m_DetailPanel == null)
                return false;

            Transform valueContainer = FindDirectChildByName(m_DetailPanel.transform, ExistingDetailValueContainerName);
            Text valueText = valueContainer != null ? valueContainer.GetComponentInChildren<Text>(true) : null;
            Button returnButton = FindButtonInDetailPanel(ExistingDetailReturnButtonName);
            Button confirmButton = FindButtonInDetailPanel(ExistingDetailConfirmButtonName);
            Button deleteButton = FindFirstButtonInDetailPanel(
                ExistingDetailDeleteButtonName,
                ExistingDetailLegacyDeleteButtonName);

            int digitButtonCount = 0;
            for (int digit = 0; digit <= 9; digit++)
            {
                if (FindButtonInDetailPanel($"{ExistingDetailDigitButtonPrefix}{digit}") != null)
                    digitButtonCount++;
            }

            if (valueText == null || returnButton == null || confirmButton == null || digitButtonCount == 0)
            {
                m_DetailUsesCustomLayout = false;
                m_DetailPanelUiInitialized = true;
                return false;
            }

            m_DetailCurrentRotationText = null;
            m_DetailInputRotationText = valueText;
            m_DetailHintText = null;

            Bind(returnButton, CloseDetailPanel);
            AttachButtonDebugHook(returnButton, "详情返回");

            Bind(confirmButton, () => HandleDetailKeyPressed("应用"));
            AttachButtonDebugHook(confirmButton, "详情确认");

            if (deleteButton != null)
            {
                Bind(deleteButton, () => HandleDetailKeyPressed("删"));
                AttachButtonDebugHook(deleteButton, "详情删除");
            }

            for (int digit = 0; digit <= 9; digit++)
            {
                string digitText = digit.ToString(CultureInfo.InvariantCulture);
                Button digitButton = FindButtonInDetailPanel($"{ExistingDetailDigitButtonPrefix}{digit}");
                if (digitButton == null)
                    continue;

                Bind(digitButton, () => HandleDetailKeyPressed(digitText));
                AttachButtonDebugHook(digitButton, $"DetailKey:{digitText}");
            }

            m_DetailUsesCustomLayout = true;
            m_DetailPanelUiInitialized = true;
            return true;
        }

        private Button FindButtonInDetailPanel(string buttonName)
        {
            if (m_DetailPanel == null)
                return null;

            Transform buttonTransform = FindNamedChildRecursive(m_DetailPanel.transform, buttonName);
            return buttonTransform != null ? buttonTransform.GetComponent<Button>() : null;
        }

        private Button FindFirstButtonInDetailPanel(params string[] buttonNames)
        {
            if (buttonNames == null)
                return null;

            for (int i = 0; i < buttonNames.Length; i++)
            {
                string buttonName = buttonNames[i];
                if (string.IsNullOrEmpty(buttonName))
                    continue;

                Button button = FindButtonInDetailPanel(buttonName);
                if (button != null)
                    return button;
            }

            return null;
        }

        private static Transform FindDirectChildByName(Transform root, string childName)
        {
            if (root == null)
                return null;

            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (string.Equals(child.name, childName))
                    return child;
            }

            return null;
        }

        private void RefreshDetailPanelPresentation()
        {
            if (m_DetailPanel == null)
                return;

            if (!m_DetailPanelOpen)
            {
                RestoreMainPanelPresentation();
                return;
            }

            Transform detailTransform = m_DetailPanel.transform;

            if (m_MainPanelToHideWhenDetailOpen != null &&
                m_MainPanelToHideWhenDetailOpen != m_DetailPanel &&
                !detailTransform.IsChildOf(m_MainPanelToHideWhenDetailOpen.transform))
            {
                if (!m_ExplicitMainPanelActiveStateCaptured)
                {
                    m_ExplicitMainPanelActiveBeforeDetailOpen = m_MainPanelToHideWhenDetailOpen.activeSelf;
                    m_ExplicitMainPanelActiveStateCaptured = true;
                }

                if (m_MainPanelToHideWhenDetailOpen.activeSelf)
                    m_MainPanelToHideWhenDetailOpen.SetActive(false);

                if (!m_DetailPanel.activeSelf)
                    m_DetailPanel.SetActive(true);

                RefreshPanelTouchOnlyInteractionMode();
                return;
            }

            if (m_PanelRoot == null)
                return;

            if (detailTransform.parent == m_PanelRoot.transform)
            {
                CacheAndHideMainPanelChildren(detailTransform);
            }
            else
            {
                m_PanelRoot.SetActive(false);
            }

            RefreshPanelTouchOnlyInteractionMode();
        }

        private void StartDetailPanelTransition(bool open)
        {
            if (m_DetailPanelTransitionRoutine != null)
            {
                StopCoroutine(m_DetailPanelTransitionRoutine);
                m_DetailPanelTransitionRoutine = null;
            }

            m_DetailPanelTransitionRoutine = StartCoroutine(DetailPanelTransitionCoroutine(open));
        }

        private IEnumerator DetailPanelTransitionCoroutine(bool open)
        {
            m_DetailPanelTransitionActive = true;
            EnsureDetailPanelReference();
            EnsureRuntimeDetailPanelBuilt();

            if (open)
            {
                m_DetailPanelOpen = true;
                m_DetailPanelBoundTargetId = int.MinValue;
                ResetDetailInputBufferFromCurrentRotation();

                bool usesExplicitMainPanel = UsesExplicitMainPanelForDetailTransition();
                GameObject mainPanel = usesExplicitMainPanel ? m_MainPanelToHideWhenDetailOpen : null;
                MonoBehaviour mainPanelAnimManager = usesExplicitMainPanel ? ResolveMainPanelAnimManagerForDetailTransition() : null;

                if (mainPanel != null && mainPanel.activeSelf)
                {
                    if (!m_ExplicitMainPanelActiveStateCaptured)
                    {
                        m_ExplicitMainPanelActiveBeforeDetailOpen = mainPanel.activeSelf;
                        m_ExplicitMainPanelActiveStateCaptured = true;
                    }

                    if (mainPanelAnimManager != null)
                        InvokeAnimManagerMethod(mainPanelAnimManager, "startDisappear", true);

                    mainPanel.SetActive(false);
                }

                if (m_DetailPanel != null)
                {
                    if (!m_DetailPanel.activeSelf)
                        m_DetailPanel.SetActive(true);

                    if (usesExplicitMainPanel)
                    {
                        RefreshPanelTouchOnlyInteractionMode();
                    }
                    else
                    {
                        RefreshDetailPanelPresentation();
                    }

                    if (m_DetailPanelAppearAnimManager != null)
                        InvokeAnimManagerMethod(m_DetailPanelAppearAnimManager, "startAppear", true);
                }

                RefreshPanelTouchOnlyInteractionMode();
            }
            else
            {
                if (m_DetailPanel != null && m_DetailPanel.activeSelf && m_DetailPanelAppearAnimManager != null)
                    InvokeAnimManagerMethod(m_DetailPanelAppearAnimManager, "startDisappear", true);

                m_DetailPanelOpen = false;

                if (m_DetailPanel != null && m_DetailPanel.activeSelf)
                {
                    ResetDetailPanelAppearAnimationForHide();
                    m_DetailPanel.SetActive(false);
                }

                RestoreMainPanelPresentation();

                if (UsesExplicitMainPanelForDetailTransition())
                {
                    MonoBehaviour mainPanelAnimManager = ResolveMainPanelAnimManagerForDetailTransition();
                    if (m_MainPanelToHideWhenDetailOpen != null &&
                        m_MainPanelToHideWhenDetailOpen.activeInHierarchy &&
                        mainPanelAnimManager != null)
                        InvokeAnimManagerMethod(mainPanelAnimManager, "startAppear", true);
                }

                RestorePrimaryInfoPresentationAfterDetailClose();
                RefreshPanelTouchOnlyInteractionMode();
            }

            m_DetailPanelTransitionActive = false;
            m_DetailPanelTransitionRoutine = null;
            RefreshDetailPanelState(force: true);
            RequestUiRefresh(force: true);
            yield break;
        }

        private bool UsesExplicitMainPanelForDetailTransition()
        {
            return m_MainPanelToHideWhenDetailOpen != null &&
                   m_MainPanelToHideWhenDetailOpen != m_DetailPanel &&
                   m_DetailPanel != null &&
                   !m_DetailPanel.transform.IsChildOf(m_MainPanelToHideWhenDetailOpen.transform);
        }

        private void CacheAndHideMainPanelChildren(Transform detailTransform)
        {
            if (m_PanelRoot == null)
                return;

            for (int i = 0; i < m_PanelRoot.transform.childCount; i++)
            {
                Transform child = m_PanelRoot.transform.GetChild(i);
                if (child == null || child == detailTransform)
                    continue;

                if (!m_MainPanelChildActiveStates.ContainsKey(child))
                    m_MainPanelChildActiveStates[child] = child.gameObject.activeSelf;

                if (child.gameObject.activeSelf)
                    child.gameObject.SetActive(false);
            }
        }

        private void RestoreMainPanelPresentation()
        {
            if (m_DetailPanel == null)
                return;

            Transform detailTransform = m_DetailPanel.transform;

            if (m_MainPanelToHideWhenDetailOpen != null &&
                m_MainPanelToHideWhenDetailOpen != m_DetailPanel &&
                !detailTransform.IsChildOf(m_MainPanelToHideWhenDetailOpen.transform))
            {
                if (m_ExplicitMainPanelActiveStateCaptured &&
                    m_MainPanelToHideWhenDetailOpen.activeSelf != m_ExplicitMainPanelActiveBeforeDetailOpen)
                {
                    m_MainPanelToHideWhenDetailOpen.SetActive(m_ExplicitMainPanelActiveBeforeDetailOpen);
                }

                m_ExplicitMainPanelActiveStateCaptured = false;
                RefreshPanelTouchOnlyInteractionMode();
                return;
            }

            if (m_PanelRoot == null)
                return;

            if (detailTransform.parent == m_PanelRoot.transform)
            {
                RestoreCachedMainPanelChildren();
            }
            else if (!m_PanelRoot.activeSelf)
            {
                m_PanelRoot.SetActive(true);
            }

            RefreshPanelTouchOnlyInteractionMode();
        }

        private void RestoreCachedMainPanelChildren()
        {
            foreach (var entry in m_MainPanelChildActiveStates)
            {
                if (entry.Key != null && entry.Key.gameObject.activeSelf != entry.Value)
                    entry.Key.gameObject.SetActive(entry.Value);
            }

            m_MainPanelChildActiveStates.Clear();
        }

        private void ResetDetailPanelAppearAnimationForHide()
        {
            if (m_DetailPanelAppearAnimManager == null)
                return;

            InvokeAnimManagerMethod(m_DetailPanelAppearAnimManager, "startDisappear", true);
        }

        private IEnumerator WaitForAnimManagerState(MonoBehaviour animManager, string expectedState, float timeoutSeconds)
        {
            if (animManager == null)
                yield break;

            float elapsed = 0f;
            while (elapsed < timeoutSeconds)
            {
                if (string.Equals(GetAnimManagerStateName(animManager), expectedState))
                    yield break;

                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        private Transform BuildRuntimeDetailPanel(Transform parent)
        {
            if (parent == null)
                return null;

            if (parent.TryGetComponent(out RectTransform panelRect))
            {
                panelRect.anchorMin = new Vector2(0.5f, 0.5f);
                panelRect.anchorMax = new Vector2(0.5f, 0.5f);
                panelRect.pivot = new Vector2(0.5f, 0.5f);
                panelRect.anchoredPosition = new Vector2(0f, -46f);
                panelRect.sizeDelta = new Vector2(RuntimeDetailPanelWidth, RuntimeDetailPanelHeight);
            }

            var background = parent.GetComponent<Image>();
            if (background == null)
                background = parent.gameObject.AddComponent<Image>();
            background.color = new Color(0.04f, 0.08f, 0.12f, 0.92f);
            background.raycastTarget = true;

            var runtimeRoot = new GameObject(RuntimeDetailPanelRootName, typeof(RectTransform)).transform;
            runtimeRoot.SetParent(parent, false);
            ApplyLayerRecursively(runtimeRoot, parent.gameObject.layer);

            RectTransform runtimeRect = runtimeRoot as RectTransform;
            if (runtimeRect != null)
            {
                runtimeRect.anchorMin = Vector2.zero;
                runtimeRect.anchorMax = Vector2.one;
                runtimeRect.offsetMin = new Vector2(12f, 12f);
                runtimeRect.offsetMax = new Vector2(-12f, -12f);
            }

            CreateRuntimeText(runtimeRoot, "__DetailTitleText", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -18f), new Vector2(280f, 32f), 24, TextAnchor.MiddleCenter, "旋转详情", Color.white);
            CreateRuntimeText(runtimeRoot, RuntimeDetailCurrentRotationTextName, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -58f), new Vector2(300f, 28f), 20, TextAnchor.MiddleCenter, "当前旋转：--", new Color(0.8f, 0.95f, 1f, 1f));
            CreateRuntimeText(runtimeRoot, RuntimeDetailInputRotationTextName, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -92f), new Vector2(300f, 28f), 20, TextAnchor.MiddleCenter, "输入角度：--", new Color(0.46f, 0.85f, 1f, 1f));
            CreateRuntimeText(runtimeRoot, RuntimeDetailHintTextName, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -122f), new Vector2(320f, 30f), 14, TextAnchor.MiddleCenter, "通过数字键盘输入 Y 轴角度。", new Color(0.68f, 0.78f, 0.86f, 1f));

            string[,] keys =
            {
                { "7", "8", "9", "删" },
                { "4", "5", "6", "清" },
                { "1", "2", "3", "-" },
                { ".", "0", "应用", "关闭" }
            };

            Vector2 start = new Vector2(-116f, -176f);
            float stepX = 78f;
            float stepY = 54f;
            for (int row = 0; row < keys.GetLength(0); row++)
            {
                for (int col = 0; col < keys.GetLength(1); col++)
                {
                    string label = keys[row, col];
                    Vector2 position = new Vector2(start.x + col * stepX, start.y - row * stepY);
                    CreateRuntimeKeyButton(runtimeRoot, label, position);
                }
            }

            return runtimeRoot;
        }

        private void CreateRuntimeKeyButton(Transform parent, string label, Vector2 anchoredPosition)
        {
            Button button = CreateRuntimeButtonClone(parent);
            if (button == null)
                return;

            button.name = $"__DetailKey_{label}";
            if (button.TryGetComponent(out RectTransform rect))
            {
                rect.anchorMin = new Vector2(0.5f, 1f);
                rect.anchorMax = new Vector2(0.5f, 1f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = anchoredPosition;
                rect.sizeDelta = new Vector2(RuntimeDetailButtonWidth, RuntimeDetailButtonHeight);
            }

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => HandleDetailKeyPressed(label));
            SetButtonLabel(button, label);
            AttachButtonDebugHook(button, $"DetailKey:{label}");
        }

        private Button CreateRuntimeButtonClone(Transform parent)
        {
            Button template = m_DetailButton != null ? m_DetailButton : m_ToggleContentVisibilityButton;
            if (template == null)
                template = m_ClearSceneButton != null ? m_ClearSceneButton : m_SpawnButton;

            if (template != null)
            {
                GameObject clone = Instantiate(template.gameObject, parent);
                ApplyLayerRecursively(clone.transform, parent.gameObject.layer);
                clone.SetActive(true);
                return clone.GetComponent<Button>();
            }

            var buttonObject = new GameObject("RuntimeButton", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            ApplyLayerRecursively(buttonObject.transform, parent.gameObject.layer);

            var image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.08f, 0.2f, 0.32f, 1f);
            image.raycastTarget = true;

            CreateRuntimeText(
                buttonObject.transform,
                "__Label",
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero,
                20,
                TextAnchor.MiddleCenter,
                string.Empty,
                Color.white);

            return buttonObject.GetComponent<Button>();
        }

        private Text CreateRuntimeText(
            Transform parent,
            string name,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 anchoredPosition,
            Vector2 sizeDelta,
            int fontSize,
            TextAnchor alignment,
            string content,
            Color color)
        {
            var textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);
            ApplyLayerRecursively(textObject.transform, parent.gameObject.layer);

            var rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;

            var text = textObject.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.color = color;
            text.text = content;
            text.raycastTarget = false;
            return text;
        }

        private static Text FindTextInChildren(Transform root, string childName)
        {
            Transform child = FindNamedChildRecursive(root, childName);
            return child != null ? child.GetComponent<Text>() : null;
        }

        private void HandleDetailKeyPressed(string key)
        {
            bool forceRefresh = false;

            switch (key)
            {
                case "删":
                    if (!string.IsNullOrEmpty(m_DetailInputBuffer))
                        m_DetailInputBuffer = m_DetailInputBuffer.Substring(0, m_DetailInputBuffer.Length - 1);
                    break;
                case "清":
                    m_DetailInputBuffer = string.Empty;
                    break;
                case "-":
                    if (string.IsNullOrEmpty(m_DetailInputBuffer))
                    {
                        m_DetailInputBuffer = "-";
                    }
                    else if (m_DetailInputBuffer.StartsWith("-", System.StringComparison.Ordinal))
                    {
                        m_DetailInputBuffer = m_DetailInputBuffer.Substring(1);
                    }
                    else
                    {
                        m_DetailInputBuffer = "-" + m_DetailInputBuffer;
                    }
                    break;
                case "应用":
                    ApplyDetailRotationInput();
                    forceRefresh = true;
                    break;
                case "关闭":
                    CloseDetailPanel();
                    break;
                default:
                    if (key.Length == 1 && char.IsDigit(key[0]) && m_DetailInputBuffer.Length < 12)
                        m_DetailInputBuffer += key;
                    break;
            }

            if (m_DetailPanelOpen)
                RefreshDetailPanelState(force: forceRefresh);
        }

        private void ApplyDetailRotationInput()
        {
            if (string.IsNullOrWhiteSpace(m_DetailInputBuffer) ||
                m_DetailInputBuffer == "-" ||
                m_DetailInputBuffer == "." ||
                m_DetailInputBuffer == "-.")
            {
                m_Spawner?.onStatusMessage?.Invoke("请输入有效的旋转角度。");
                return;
            }

            if (!int.TryParse(m_DetailInputBuffer, NumberStyles.Integer, CultureInfo.InvariantCulture, out int yawDegrees))
            {
                m_Spawner?.onStatusMessage?.Invoke("旋转角度格式无效。");
                return;
            }

            if (!TrySetCurrentDetailTargetYaw(yawDegrees))
                return;

            ResetDetailInputBufferFromCurrentRotation();
            RefreshDetailPanelState();
        }

        private static string FormatSignedAngle(float angle)
        {
            int normalized = Mathf.RoundToInt(Mathf.DeltaAngle(0f, angle));
            return normalized.ToString(CultureInfo.InvariantCulture);
        }
    }
}
