using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace VFXViewer
{
    public sealed class SpectatorConnectPanel : MonoBehaviour
    {
        private enum PanelMode
        {
            Home = 0,
            Root = 1
        }

        [Header("Structure")]
        [SerializeField] private GameObject m_HomePanel;
        [SerializeField] private GameObject m_RootPanel;
        [SerializeField] private GameObject m_HostRow;
        [SerializeField] private GameObject m_ManualRow;
        [SerializeField] private GameObject m_ConnectRow;

        [Header("Labels")]
        [SerializeField] private Text m_StatusText;
        [SerializeField] private Text m_RootText;
        [SerializeField] private Text m_BonjourText;
        [SerializeField] private Text m_PlacementText;

        [Header("Inputs")]
        [SerializeField] private InputField m_HostInput;

        [Header("Buttons")]
        [SerializeField] private Button m_CreateRootButton;
        [SerializeField] private Button m_DeleteRootButton;
        [SerializeField] private Button m_RotateLeftButton;
        [SerializeField] private Button m_ForwardButton;
        [SerializeField] private Button m_RotateRightButton;
        [SerializeField] private Button m_LeftButton;
        [SerializeField] private Button m_BackButton;
        [SerializeField] private Button m_RightButton;
        [SerializeField] private Button m_DownButton;
        [SerializeField] private Button m_UpButton;
        [SerializeField] private Button m_SaveHostButton;
        [SerializeField] private Button m_UseBonjourButton;
        [SerializeField] private Button m_ConnectButton;
        [SerializeField] private Button m_ReconnectButton;
        [SerializeField] private Button m_DisconnectButton;
        [SerializeField] private Button m_ExportBackupButton;
        [SerializeField] private Button m_ImportBackupButton;
        [SerializeField] private Button m_ExportLayoutButton;
        [SerializeField] private Button m_ImportLayoutButton;
        [SerializeField] private Button m_CapturePhotoButton;
        [SerializeField] private Button m_RecordVideoButton;
        [SerializeField] private Button m_ZoomOutButton;
        [SerializeField] private Button m_ZoomResetButton;
        [SerializeField] private Button m_ZoomInButton;
        [SerializeField] private Button m_RootButton;
        [SerializeField] private Button m_ReturnButton;

        private readonly List<Button> m_BonjourHostButtons = new List<Button>();
        private readonly List<string> m_BonjourHostButtonKeys = new List<string>();
        private SpectatorClientService m_Client;
        private bool m_CallbacksBound;
        private PanelMode m_CurrentPanelMode = PanelMode.Home;
        private bool m_HasInitializedPanelMode;

        public void Initialize(SpectatorClientService client)
        {
            m_Client = client;
            ResolveOptionalReferences();
            SyncDraftFromClient(force: true);
            EnsurePanelModeInitialized();
            ApplyConnectionModeVisibility();
            RefreshState();
        }

        private void Awake()
        {
            ResolveOptionalReferences();
            BindCallbacks();
            EnsurePanelModeInitialized();
            ApplyConnectionModeVisibility();
        }

        private void OnEnable()
        {
            ResolveOptionalReferences();
            BindCallbacks();
            EnsurePanelModeInitialized();
            ApplyConnectionModeVisibility();
        }

        private void Update()
        {
            if (m_Client == null)
                return;

            EnsureDraftDefaults();
            RefreshState();
        }

        private void BindCallbacks()
        {
            ResolveOptionalReferences();

            if (m_CallbacksBound)
                return;

            m_CallbacksBound = true;

            BindButton(m_CreateRootButton, OnCreateRootClicked);
            BindButton(m_DeleteRootButton, OnDeleteRootClicked);
            BindButton(m_SaveHostButton, OnSaveHostClicked);
            BindButton(m_UseBonjourButton, OnUseBonjourClicked);
            BindButton(m_ConnectButton, OnConnectClicked);
            BindButton(m_ReconnectButton, () => m_Client?.ConnectNow());
            BindButton(m_DisconnectButton, () => m_Client?.Disconnect());
            BindButton(m_ExportBackupButton, OnExportBackupClicked);
            BindButton(m_ImportBackupButton, OnImportBackupClicked);
            BindButton(m_ExportLayoutButton, OnExportLayoutClicked);
            BindButton(m_ImportLayoutButton, OnImportLayoutClicked);
            BindButton(m_CapturePhotoButton, () => m_Client?.CapturePhoto());
            BindButton(m_RecordVideoButton, () => m_Client?.ToggleVideoRecording());
            BindButton(m_ZoomOutButton, () => m_Client?.ZoomCameraOut());
            BindButton(m_ZoomResetButton, () => m_Client?.ResetCameraZoom());
            BindButton(m_ZoomInButton, () => m_Client?.ZoomCameraIn());
            BindButton(m_RootButton, OnRootButtonClicked);
            BindButton(m_ReturnButton, OnReturnButtonClicked);
            BindBonjourHostButtons();
            EnsureMoveButtonHandlers();
        }

        private static void BindButton(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button == null)
                return;

            button.onClick.RemoveAllListeners();
            if (action != null)
                button.onClick.AddListener(action);
        }

        private void EnsureMoveButtonHandlers()
        {
            EnsureMoveButtonHandler(m_RotateLeftButton, SpectatorHoldButton.HoldAction.RotateLeft);
            EnsureMoveButtonHandler(m_ForwardButton, SpectatorHoldButton.HoldAction.Forward);
            EnsureMoveButtonHandler(m_RotateRightButton, SpectatorHoldButton.HoldAction.RotateRight);
            EnsureMoveButtonHandler(m_LeftButton, SpectatorHoldButton.HoldAction.Left);
            EnsureMoveButtonHandler(m_BackButton, SpectatorHoldButton.HoldAction.Back);
            EnsureMoveButtonHandler(m_RightButton, SpectatorHoldButton.HoldAction.Right);
            EnsureMoveButtonHandler(m_DownButton, SpectatorHoldButton.HoldAction.Down);
            EnsureMoveButtonHandler(m_UpButton, SpectatorHoldButton.HoldAction.Up);
        }

        private void EnsureMoveButtonHandler(Button button, SpectatorHoldButton.HoldAction action)
        {
            if (button == null)
                return;

            BindButton(button, null);

            SpectatorHoldButton holdButton = button.GetComponent<SpectatorHoldButton>();
            if (holdButton == null)
                holdButton = button.gameObject.AddComponent<SpectatorHoldButton>();

            holdButton.Configure(this, action);
        }

        private void ResolveOptionalReferences()
        {
            m_HomePanel ??= FindChildGameObject("HomePanel");
            m_RootPanel ??= FindChildGameObject("RootPanel");
            m_HostRow ??= FindChildGameObject("HostRow");
            m_ManualRow ??= FindChildGameObject("ManualRow");
            m_ConnectRow ??= FindChildGameObject("ConnectRow");
            m_ExportBackupButton ??= FindChildButton("ExportBackupButton");
            m_ImportBackupButton ??= FindChildButton("ImportBackupButton");
            m_ExportLayoutButton ??= FindChildButton("ExportLayoutButton");
            m_ImportLayoutButton ??= FindChildButton("ImportLayoutButton");
            m_CapturePhotoButton ??= FindChildButton("CapturePhotoButton");
            m_RecordVideoButton ??= FindChildButton("RecordVideoButton");
            m_ZoomOutButton ??= FindChildButton("ZoomOutButton");
            m_ZoomResetButton ??= FindChildButton("ZoomResetButton");
            m_ZoomInButton ??= FindChildButton("ZoomInButton");
            m_RootButton ??= FindChildButton("RootButton");
            m_ReturnButton ??= FindChildButton("ReturnButton");
            ResolveBonjourHostButtons();

            // Some scene instances keep the row objects inside prefab instances where the names
            // aren't easy to resolve up front. Fall back to the buttons' immediate parents.
            m_HostRow ??= GetParentRowObject(m_HostInput != null ? m_HostInput.transform : null);
            m_ManualRow ??= GetSharedParentRowObject(
                m_SaveHostButton != null ? m_SaveHostButton.transform : null,
                m_UseBonjourButton != null ? m_UseBonjourButton.transform : null);
            m_ConnectRow ??= GetSharedParentRowObject(
                m_ConnectButton != null ? m_ConnectButton.transform : null,
                m_ReconnectButton != null ? m_ReconnectButton.transform : null,
                m_DisconnectButton != null ? m_DisconnectButton.transform : null);

            m_ExportLayoutButton ??= CloneButtonFromTemplate(m_ExportBackupButton, "ExportLayoutButton");
            m_ImportLayoutButton ??= CloneButtonFromTemplate(m_ImportBackupButton, "ImportLayoutButton");
        }

        private void ApplyConnectionModeVisibility()
        {
            bool showManualControls = m_Client != null && m_Client.SupportsManualEndpoint;

            SetActive(m_HostRow, showManualControls);
            SetActive(m_HostInput != null ? m_HostInput.gameObject : null, showManualControls);
            SetActive(m_SaveHostButton != null ? m_SaveHostButton.gameObject : null, showManualControls);
            SetActive(m_ConnectButton != null ? m_ConnectButton.gameObject : null, showManualControls);
            SetActive(m_UseBonjourButton != null ? m_UseBonjourButton.gameObject : null, true);

            if (m_ManualRow != null)
                m_ManualRow.SetActive(true);

            if (m_ConnectRow != null)
                m_ConnectRow.SetActive(true);
        }

        private void RefreshState()
        {
            ApplyConnectionModeVisibility();

            if (m_Client == null)
            {
                SetText(m_StatusText, "Status: waiting for client");
                SetText(m_RootText, "Root: missing");
                SetText(m_BonjourText, "Bonjour: not found");
                SetText(m_PlacementText, "Placement: waiting for spectator client.");
                SetButtonLabel(m_ExportBackupButton, "导出备份");
                SetButtonLabel(m_ImportBackupButton, "导入备份");
                SetButtonLabel(m_ExportLayoutButton, "导出布局");
                SetButtonLabel(m_ImportLayoutButton, "导入布局");
                SetButtonLabel(m_CapturePhotoButton, "拍照");
                SetButtonLabel(m_RecordVideoButton, "开始录像");
                SetButtonLabel(m_ZoomOutButton, "缩小");
                SetButtonLabel(m_ZoomResetButton, "1.0x");
                SetButtonLabel(m_ZoomInButton, "放大");
                SetButtonLabel(m_RootButton, "锚点修改");
                SetButtonLabel(m_ReturnButton, "返回");
                SetButtonLabel(m_UseBonjourButton, "刷新发现");
                SetButtonLabel(m_ReconnectButton, "重新连接");
                SetButtonLabel(m_DisconnectButton, "断开连接");
                RefreshBonjourHostButtons();
                SetInteractable(m_ExportBackupButton, false);
                SetInteractable(m_ImportBackupButton, false);
                SetInteractable(m_ExportLayoutButton, false);
                SetInteractable(m_ImportLayoutButton, false);
                SetInteractable(m_CapturePhotoButton, false);
                SetInteractable(m_RecordVideoButton, false);
                SetInteractable(m_ZoomOutButton, false);
                SetInteractable(m_ZoomResetButton, false);
                SetInteractable(m_ZoomInButton, false);
                return;
            }

            string rootState = !m_Client.HasRoot
                ? "missing"
                : m_Client.IsRootLocked ? "locked" : "unlocked";
            string discovered = BuildBonjourSummaryText();

            string connectionState = m_Client.IsConnected
                ? "已连接"
                : m_Client.IsConnecting
                    ? "连接中"
                    : "未连接";
            SetText(m_StatusText, $"连接: {connectionState} | 状态: {m_Client.LastStatusMessage}");
            SetText(m_RootText, $"Root: {rootState}");
            SetText(m_BonjourText, $"Bonjour: {discovered}");
            SetText(m_PlacementText, "Placement: tap Create Root to spawn the plan root marker and anchor it. Hold the move buttons to temporarily unlock, move, and re-lock on release.");
            SetButtonLabel(m_ExportBackupButton, "导出备份");
            SetButtonLabel(m_ImportBackupButton, "导入备份");
            SetButtonLabel(m_ExportLayoutButton, "导出布局");
            SetButtonLabel(m_ImportLayoutButton, "导入布局");
            SetButtonLabel(m_CapturePhotoButton, "拍照");
            SetButtonLabel(m_RecordVideoButton, m_Client.IsVideoRecording ? "停止录像" : "开始录像");
            SetButtonLabel(m_ZoomOutButton, "缩小");
            SetButtonLabel(m_ZoomResetButton, $"{m_Client.CameraZoomFactor:0.0}x");
            SetButtonLabel(m_ZoomInButton, "放大");
            SetButtonLabel(m_RootButton, "锚点修改");
            SetButtonLabel(m_ReturnButton, "返回");
            SetButtonLabel(m_UseBonjourButton, "刷新发现");
            SetButtonLabel(m_ReconnectButton, "重新连接");
            SetButtonLabel(m_DisconnectButton, "断开连接");

            bool canAdjustRoot = m_Client.HasRoot;
            bool supportsBackupTransfer = m_Client.SupportsWorldMapBackupTransfer;
            bool canExportBackup = supportsBackupTransfer && m_Client.CanExportWorldMapBackup;
            bool supportsLayoutTransfer = m_Client.SupportsLayoutJsonTransfer;
            bool supportsMediaCapture = m_Client.SupportsMediaCapture;

            SetButtonLabel(m_CreateRootButton, m_Client.HasRoot ? "重建 Root" : "创建 Root");

            SetInteractable(m_DeleteRootButton, canAdjustRoot);
            SetInteractable(m_RotateLeftButton, canAdjustRoot);
            SetInteractable(m_ForwardButton, canAdjustRoot);
            SetInteractable(m_RotateRightButton, canAdjustRoot);
            SetInteractable(m_LeftButton, canAdjustRoot);
            SetInteractable(m_BackButton, canAdjustRoot);
            SetInteractable(m_RightButton, canAdjustRoot);
            SetInteractable(m_DownButton, canAdjustRoot);
            SetInteractable(m_UpButton, canAdjustRoot);
            SetInteractable(m_UseBonjourButton, true);
            SetInteractable(m_ReconnectButton, true);
            SetInteractable(m_DisconnectButton, m_Client.IsConnected || m_Client.IsConnecting);
            SetInteractable(m_ExportBackupButton, canExportBackup);
            SetInteractable(m_ImportBackupButton, supportsBackupTransfer);
            // Keep layout transfer buttons clickable whenever the platform supports the document
            // bridge so the service can surface a concrete status message instead of silently
            // looking unresponsive when snapshot/connection prerequisites are missing.
            SetInteractable(m_ExportLayoutButton, supportsLayoutTransfer);
            SetInteractable(m_ImportLayoutButton, supportsLayoutTransfer);
            SetInteractable(m_CapturePhotoButton, supportsMediaCapture);
            SetInteractable(m_RecordVideoButton, supportsMediaCapture);
            SetInteractable(m_ZoomOutButton, supportsMediaCapture);
            SetInteractable(m_ZoomResetButton, supportsMediaCapture);
            SetInteractable(m_ZoomInButton, supportsMediaCapture);
            SetInteractable(m_RootButton, true);
            SetInteractable(m_ReturnButton, true);
            RefreshBonjourHostButtons();
        }

        private static void SetInteractable(Selectable selectable, bool interactable)
        {
            if (selectable != null)
                selectable.interactable = interactable;
        }

        private void OnCreateRootClicked()
        {
            m_Client?.CreateOrRecreateRoot();
        }

        private void OnDeleteRootClicked()
        {
            m_Client?.DeleteRoot();
        }

        private void OnSaveHostClicked()
        {
            m_Client?.RefreshBonjourAndReconnect();
        }

        private void OnUseBonjourClicked()
        {
            m_Client?.RefreshBonjourAndReconnect();
        }

        private void OnBonjourHostButtonClicked(int index)
        {
            if (index < 0 || index >= m_BonjourHostButtonKeys.Count)
                return;

            string hostKey = m_BonjourHostButtonKeys[index];
            if (string.IsNullOrWhiteSpace(hostKey))
                return;

            m_Client?.SelectBonjourHost(hostKey, reconnectImmediately: true);
        }

        private void OnConnectClicked()
        {
            m_Client?.RefreshBonjourAndReconnect();
        }

        private void OnExportBackupClicked()
        {
            m_Client?.ExportWorldMapBackup();
        }

        private void OnImportBackupClicked()
        {
            m_Client?.ImportWorldMapBackup();
        }

        private void OnExportLayoutClicked()
        {
            m_Client?.ExportCurrentLayoutJson();
        }

        private void OnImportLayoutClicked()
        {
            m_Client?.ImportLayoutJson();
        }

        private void OnRootButtonClicked()
        {
            SetPanelMode(PanelMode.Root);
        }

        private void OnReturnButtonClicked()
        {
            SetPanelMode(PanelMode.Home);
        }

        private void EnsureDraftDefaults()
        {
            if (m_Client == null)
                return;

            if (m_HostInput != null &&
                !m_HostInput.isFocused &&
                string.IsNullOrWhiteSpace(m_HostInput.text) &&
                !string.IsNullOrWhiteSpace(m_Client.ManualHostOverride))
            {
                m_HostInput.text = m_Client.ManualHostOverride;
            }

        }

        private void SyncDraftFromClient(bool force)
        {
            if (m_Client == null)
                return;

            if (m_HostInput != null && (force || !m_HostInput.isFocused))
                m_HostInput.text = m_Client.ManualHostOverride;

        }

        private string GetHostDraft()
        {
            return m_HostInput != null ? (m_HostInput.text ?? string.Empty).Trim() : string.Empty;
        }

        private void SetPanelMode(PanelMode mode)
        {
            m_CurrentPanelMode = mode;
            m_HasInitializedPanelMode = true;

            ApplyPanelMode();
        }

        private void EnsurePanelModeInitialized()
        {
            if (!m_HasInitializedPanelMode)
            {
                SetPanelMode(PanelMode.Home);
                return;
            }

            ApplyPanelMode();
        }

        private void ApplyPanelMode()
        {
            if (m_HomePanel != null)
                m_HomePanel.SetActive(m_CurrentPanelMode == PanelMode.Home);

            if (m_RootPanel != null)
                m_RootPanel.SetActive(m_CurrentPanelMode == PanelMode.Root);
        }

        private GameObject FindChildGameObject(string objectName)
        {
            if (string.IsNullOrWhiteSpace(objectName))
                return null;

            Transform[] children = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                Transform child = children[i];
                if (child == null || !string.Equals(child.name, objectName, System.StringComparison.Ordinal))
                    continue;

                return child.gameObject;
            }

            return null;
        }

        private Button FindChildButton(string objectName)
        {
            GameObject target = FindChildGameObject(objectName);
            return target != null ? target.GetComponent<Button>() : null;
        }

        private Button CloneButtonFromTemplate(Button template, string objectName)
        {
            if (template == null || template.transform.parent == null || string.IsNullOrWhiteSpace(objectName))
                return null;

            Button existing = FindChildButton(objectName);
            if (existing != null)
                return existing;

            GameObject clone = Instantiate(template.gameObject, template.transform.parent);
            clone.name = objectName;
            clone.transform.SetSiblingIndex(template.transform.GetSiblingIndex() + 1);
            return clone.GetComponent<Button>();
        }

        private void ResolveBonjourHostButtons()
        {
            Button[] buttons = GetComponentsInChildren<Button>(true);
            m_BonjourHostButtons.Clear();

            for (int i = 0; i < buttons.Length; i++)
            {
                Button button = buttons[i];
                if (button == null || string.IsNullOrWhiteSpace(button.name))
                    continue;

                if (!button.name.StartsWith("BonjourHostButton", System.StringComparison.Ordinal))
                    continue;

                m_BonjourHostButtons.Add(button);
            }

            m_BonjourHostButtons.Sort(static (left, right) =>
                string.Compare(left != null ? left.name : string.Empty, right != null ? right.name : string.Empty, System.StringComparison.Ordinal));

            while (m_BonjourHostButtonKeys.Count < m_BonjourHostButtons.Count)
                m_BonjourHostButtonKeys.Add(string.Empty);

            while (m_BonjourHostButtonKeys.Count > m_BonjourHostButtons.Count)
                m_BonjourHostButtonKeys.RemoveAt(m_BonjourHostButtonKeys.Count - 1);
        }

        private void BindBonjourHostButtons()
        {
            for (int i = 0; i < m_BonjourHostButtons.Count; i++)
            {
                int capturedIndex = i;
                BindButton(m_BonjourHostButtons[i], () => OnBonjourHostButtonClicked(capturedIndex));
            }
        }

        private void RefreshBonjourHostButtons()
        {
            for (int i = 0; i < m_BonjourHostButtons.Count; i++)
            {
                Button button = m_BonjourHostButtons[i];
                if (button == null)
                    continue;

                SpectatorBonjourHostInfo host = m_Client != null ? m_Client.GetDiscoveredBonjourHost(i) : null;
                bool hasHost = host != null;
                button.gameObject.SetActive(hasHost);

                if (!hasHost)
                {
                    if (i < m_BonjourHostButtonKeys.Count)
                        m_BonjourHostButtonKeys[i] = string.Empty;
                    continue;
                }

                if (i < m_BonjourHostButtonKeys.Count)
                    m_BonjourHostButtonKeys[i] = host.Key;

                bool isSelected = m_Client != null &&
                    !string.IsNullOrWhiteSpace(m_Client.SelectedBonjourHostKey) &&
                    string.Equals(m_Client.SelectedBonjourHostKey, host.Key, System.StringComparison.Ordinal);

                SetButtonLabel(button, BuildBonjourHostButtonLabel(host, isSelected));
                SetInteractable(button, host.HasResolvedEndpoint);
            }
        }

        private string BuildBonjourSummaryText()
        {
            if (m_Client == null || m_Client.DiscoveredBonjourHostCount <= 0)
                return "未发现";

            SpectatorBonjourHostInfo selectedHost = FindSelectedBonjourHost();
            if (selectedHost != null)
                return $"已选择 {selectedHost.DisplayName} ({selectedHost.hostName}:{selectedHost.port})";

            if (m_Client.DiscoveredBonjourHostCount > m_BonjourHostButtons.Count && m_BonjourHostButtons.Count > 0)
                return $"发现 {m_Client.DiscoveredBonjourHostCount} 台主机，显示前 {m_BonjourHostButtons.Count} 台";

            return $"发现 {m_Client.DiscoveredBonjourHostCount} 台主机";
        }

        private SpectatorBonjourHostInfo FindSelectedBonjourHost()
        {
            if (m_Client == null || string.IsNullOrWhiteSpace(m_Client.SelectedBonjourHostKey))
                return null;

            for (int i = 0; i < m_Client.DiscoveredBonjourHostCount; i++)
            {
                SpectatorBonjourHostInfo host = m_Client.GetDiscoveredBonjourHost(i);
                if (host != null && string.Equals(host.Key, m_Client.SelectedBonjourHostKey, System.StringComparison.Ordinal))
                    return host;
            }

            return null;
        }

        private static string BuildBonjourHostButtonLabel(SpectatorBonjourHostInfo host, bool isSelected)
        {
            if (host == null)
                return string.Empty;

            string title = string.IsNullOrWhiteSpace(host.DisplayName) ? "未命名主机" : host.DisplayName;
            string endpoint = host.HasResolvedEndpoint ? $"{host.hostName}:{host.port}" : "解析中...";
            return isSelected ? $"已选中\n{title}\n{endpoint}" : $"{title}\n{endpoint}";
        }

        private static GameObject GetParentRowObject(Transform child)
        {
            Transform current = child != null ? child.parent : null;
            while (current != null)
            {
                if (current.name != null &&
                    current.name.EndsWith("Row", System.StringComparison.Ordinal))
                {
                    return current.gameObject;
                }

                current = current.parent;
            }

            return null;
        }

        private static GameObject GetSharedParentRowObject(params Transform[] children)
        {
            if (children == null)
                return null;

            for (int i = 0; i < children.Length; i++)
            {
                GameObject rowObject = GetParentRowObject(children[i]);
                if (rowObject != null)
                    return rowObject;
            }

            return null;
        }

        private static void SetText(Text target, string value)
        {
            if (target != null)
                target.text = value;
        }

        private static void SetButtonLabel(Button button, string value)
        {
            if (button == null)
                return;

            Text label = button.GetComponentInChildren<Text>(true);
            if (label != null)
                label.text = value;
        }

        private static void SetActive(GameObject target, bool active)
        {
            if (target != null)
                target.SetActive(active);
        }

        public void BeginHeldRootAdjustment(SpectatorHoldButton.HoldAction action)
        {
            m_Client?.BeginHeldRootAdjustment(action);
        }

        public void EndHeldRootAdjustment(SpectatorHoldButton.HoldAction action)
        {
            m_Client?.EndHeldRootAdjustment(action);
        }
    }
}
