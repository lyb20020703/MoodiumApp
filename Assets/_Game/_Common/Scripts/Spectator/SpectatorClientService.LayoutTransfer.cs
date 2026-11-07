using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace VFXViewer
{
    public sealed partial class SpectatorClientService
    {
        private enum SpectatorDocumentTransferMode
        {
            None = 0,
            WorldMapBackup = 1,
            LayoutJson = 2,
        }

        private const string ExportedLayoutFileName = "museum_layout.json";

        private SpectatorDocumentTransferMode m_DocumentImportMode;
        private SpectatorDocumentTransferMode m_DocumentExportMode;
        private SharedLayoutSnapshot m_LastSharedLayoutSnapshot;

        public bool SupportsLayoutJsonTransfer => SpectatorDocumentBridge.IsSupported;
        public bool CanExportLayoutJson => SupportsLayoutJsonTransfer && (IsConnected || HasCachedLayoutSnapshot());
        public bool CanImportLayoutJson => SupportsLayoutJsonTransfer && IsConnected;

        public void ExportCurrentLayoutJson()
        {
            if (!SupportsLayoutJsonTransfer)
            {
                SetStatus("布局导出仅支持 iPhone/iPad 观演端设备。");
                return;
            }

            bool hasPreferredEndpoint = TryGetPreferredEndpoint(out string host, out int port);
            Debug.Log(
                $"[SpectatorLayout] Export requested. isConnected={IsConnected}, isConnecting={IsConnecting}, " +
                $"hasPreferredEndpoint={hasPreferredEndpoint}, preferredEndpoint={(hasPreferredEndpoint ? $"{host}:{port}" : "<none>")}, " +
                $"hasResolvedBonjour={TryGetResolvedSelectedBonjourEndpoint(out _, out _)}, hasCachedLayout={HasCachedLayoutSnapshot()}",
                this);

            if (IsConnected)
            {
                m_PendingLayoutExportAfterConnect = false;
                _ = SendCommandAsync(SpectatorCommandKind.ExportLayoutJson);
                SetStatus("正在向 Vision 主机请求已保存的 museum_layout.json...");
                return;
            }

            if (IsConnecting)
            {
                m_PendingLayoutExportAfterConnect = true;
                SetStatus(BuildPendingLayoutExportConnectionStatus(string.Empty, 0, alreadyConnecting: true));
                return;
            }

            if (hasPreferredEndpoint)
            {
                m_PendingLayoutExportAfterConnect = true;
                ConnectNow();
                SetStatus(BuildPendingLayoutExportConnectionStatus(host, port, alreadyConnecting: false));
                return;
            }

            if (!TryExportCachedLayoutJsonLocally(
                    "正在打开文件导出面板以保存当前已同步的 museum_layout.json。",
                    out string errorMessage))
            {
                SetStatus(BuildNoLayoutExportSourceStatus(errorMessage));
            }
        }

        public void ImportLayoutJson()
        {
            if (!SupportsLayoutJsonTransfer)
            {
                SetStatus("布局导入仅支持 iPhone/iPad 观演端设备。");
                return;
            }

            if (!IsConnected)
            {
                SetStatus("请先连接到目标 Vision 主机，再导入布局。");
                return;
            }

            m_DocumentImportMode = SpectatorDocumentTransferMode.LayoutJson;
            SpectatorDocumentBridge.BeginImportJsonFile();
            SetStatus("请选择一个 museum_layout.json 文件并发送到当前 Vision 主机。");
        }

        private void CacheSharedLayoutSnapshot(SharedLayoutSnapshot snapshot)
        {
            if (snapshot == null)
                return;

            m_LastSharedLayoutSnapshot = snapshot;
        }

        private bool HasCachedLayoutSnapshot()
        {
            return m_LastSharedLayoutSnapshot != null &&
                   m_LastSharedLayoutSnapshot.items != null &&
                   m_LastSharedLayoutSnapshot.items.Length > 0;
        }

        private bool TryBuildPortableLayoutJson(out string layoutJson, out string fileName, out string errorMessage)
        {
            layoutJson = null;
            fileName = ExportedLayoutFileName;
            errorMessage = string.Empty;

            if (m_LastSharedLayoutSnapshot == null || m_LastSharedLayoutSnapshot.items == null || m_LastSharedLayoutSnapshot.items.Length == 0)
            {
                errorMessage = "当前还没有收到可导出的 Vision 布局数据。";
                return false;
            }

            var layout = new LayoutDataList
            {
                hasSeparateRoot = true,
            };

            SharedLayoutItem[] items = m_LastSharedLayoutSnapshot.items;
            for (int i = 0; i < items.Length; i++)
            {
                SharedLayoutItem item = items[i];
                if (item == null || string.IsNullOrWhiteSpace(item.moduleId))
                    continue;

                layout.items.Add(new LayoutData
                {
                    instanceID = string.IsNullOrWhiteSpace(item.instanceId) ? $"{item.moduleId}::{i}" : item.instanceId.Trim(),
                    moduleId = item.moduleId.Trim(),
                    displayName = string.IsNullOrWhiteSpace(item.displayName) ? item.moduleId.Trim() : item.displayName.Trim(),
                    relativePosition = item.relativePosition.ToVector3(),
                    relativeRotation = item.relativeRotation.ToQuaternion(),
                    scale = item.scale.ToVector3(),
                    placementContentHidden = item.placementContentHidden,
                });
            }

            if (layout.items.Count == 0)
            {
                errorMessage = "当前布局里没有可导出的展品。";
                return false;
            }

            layoutJson = JsonUtility.ToJson(layout, true);
            return true;
        }

        private bool TryExportCachedLayoutJsonLocally(string openingStatusMessage, out string errorMessage)
        {
            if (!TryBuildPortableLayoutJson(out string layoutJson, out string fileName, out errorMessage))
                return false;

            BeginLayoutJsonDocumentExport(fileName, layoutJson, openingStatusMessage);
            errorMessage = string.Empty;
            return true;
        }

        private void BeginLayoutJsonDocumentExport(string fileName, string layoutJson, string openingStatusMessage)
        {
            m_DocumentExportMode = SpectatorDocumentTransferMode.LayoutJson;
            SpectatorDocumentBridge.ExportJsonFile(
                string.IsNullOrWhiteSpace(fileName) ? ExportedLayoutFileName : fileName,
                layoutJson ?? "{}");
            SetStatus(openingStatusMessage);
        }

        private void ImportLayoutJsonFromFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                SetStatus("选中的布局文件路径为空。");
                return;
            }

            if (!IsConnected || m_TcpClient == null || m_Cancellation == null)
            {
                SetStatus("当前没有连接到 Vision 主机，无法发送布局文件。");
                return;
            }

            try
            {
                string json = File.ReadAllText(filePath);
                if (!TryValidateImportedLayoutJson(json, out string errorMessage))
                {
                    SetStatus(errorMessage);
                    return;
                }

                var request = new SpectatorLayoutImportRequest
                {
                    fileName = Path.GetFileName(filePath),
                    layoutJson = json,
                    replaceExistingScene = false,
                    rebuildIfPossible = false,
                };

                _ = SendCommandAsync(SpectatorCommandKind.ImportLayoutJson, request);
                SetStatus("布局文件已发送到 Vision 主机，正在保存...");
            }
            catch (Exception ex)
            {
                SetStatus($"读取布局文件失败：{ex.Message}");
            }
        }

        private static bool TryValidateImportedLayoutJson(string json, out string errorMessage)
        {
            errorMessage = string.Empty;
            if (string.IsNullOrWhiteSpace(json))
            {
                errorMessage = "选中的布局文件为空。";
                return false;
            }

            try
            {
                LayoutDataList layout = JsonUtility.FromJson<LayoutDataList>(json);
                if (layout == null || layout.items == null || layout.items.Count == 0)
                {
                    errorMessage = "选中的文件不是有效的 museum_layout.json。";
                    return false;
                }
            }
            catch (Exception ex)
            {
                errorMessage = $"布局文件解析失败：{ex.Message}";
                return false;
            }

            return true;
        }

        private async Task SendCommandAsync<TPayload>(SpectatorCommandKind commandKind, TPayload payload)
        {
            if (!IsConnected || m_TcpClient == null || m_Cancellation == null)
            {
                SetStatus("当前没有连接到 Vision 主机。");
                return;
            }

            var command = new SpectatorCommandMessage
            {
                commandKind = (int)commandKind,
                payloadJson = payload == null ? string.Empty : JsonUtility.ToJson(payload),
            };

            try
            {
                await SpectatorTransport.WriteEnvelopeAsync(
                    m_TcpClient.GetStream(),
                    SpectatorMessageKind.Command,
                    command,
                    m_Cancellation.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                SetStatus($"发送布局命令失败：{ex.Message}");
            }
        }

        private Task SendCommandAsync(SpectatorCommandKind commandKind)
        {
            return SendCommandAsync<string>(commandKind, null);
        }

        private void TryRunPendingLayoutExportAfterConnect()
        {
            if (!m_PendingLayoutExportAfterConnect || !IsConnected)
                return;

            m_PendingLayoutExportAfterConnect = false;
            _ = SendCommandAsync(SpectatorCommandKind.ExportLayoutJson);
            SetStatus("已连接到 Vision 主机，正在请求已保存的 museum_layout.json...");
        }

        private string BuildPendingLayoutExportConnectionStatus(string host, int port, bool alreadyConnecting)
        {
            bool resolvedBonjour = TryGetResolvedSelectedBonjourEndpoint(out _, out _);
            string source = resolvedBonjour ? "Bonjour 已解析到 Vision 主机" : "已找到可连接的 Vision 主机";
            if (alreadyConnecting)
                return $"{source}，但当前 TCP 仍在连接中；连接成功后将自动导出布局。";

            string endpoint = !string.IsNullOrWhiteSpace(host) && port > 0 ? $" {host}:{port}" : string.Empty;
            return $"{source}{endpoint}，但当前 TCP 未连接；正在重连，连接成功后将自动导出布局。";
        }

        private string BuildNoLayoutExportSourceStatus(string layoutErrorMessage)
        {
            if (CountResolvedBonjourHosts() > 0)
                return $"Bonjour 已发现 Vision 主机，但当前没有可用的 TCP 连接地址，且{layoutErrorMessage}";

            return $"当前未发现可连接的 Vision 主机，且{layoutErrorMessage}";
        }

        private static string BuildConnectedVisionExportFailureStatus(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return "已连接 Vision，但导出布局失败。";

            string trimmed = message.Trim();
            if (trimmed.StartsWith("已连接", StringComparison.Ordinal))
                return trimmed;

            return $"已连接 Vision，但{trimmed}";
        }

        private void HandleCommandResult(SpectatorCommandResult result)
        {
            if (result == null)
                return;

            SpectatorCommandKind commandKind = (SpectatorCommandKind)result.commandKind;
            switch (commandKind)
            {
                case SpectatorCommandKind.ExportLayoutJson:
                    HandleExportLayoutJsonResult(result);
                    return;
            }

            string message = string.IsNullOrWhiteSpace(result.message)
                ? (result.success ? "Vision 主机已处理请求。" : "Vision 主机处理请求失败。")
                : result.message.Trim();
            SetStatus(message, logToConsole: true);
        }

        private void HandleExportLayoutJsonResult(SpectatorCommandResult result)
        {
            string message = string.IsNullOrWhiteSpace(result.message)
                ? (result.success ? "Vision 主机已处理布局导出请求。" : "Vision 主机处理布局导出请求失败。")
                : result.message.Trim();

            if (!result.success)
            {
                string fallbackStatus = BuildConnectedVisionExportFailureStatus(message);
                if (TryExportCachedLayoutJsonLocally(
                        $"{fallbackStatus} 已改用当前已同步布局打开导出面板。",
                        out _))
                {
                    Debug.LogWarning($"[SpectatorLayout] Host export failed, fell back to cached snapshot. reason={message}", this);
                    return;
                }

                SetStatus(fallbackStatus, logToConsole: true);
                return;
            }

            SpectatorLayoutExportResponse response =
                SpectatorProtocol.DeserializePayload<SpectatorLayoutExportResponse>(result.payloadJson);
            if (response == null || string.IsNullOrWhiteSpace(response.layoutJson))
            {
                SetStatus("Vision 返回的布局文件内容为空。", logToConsole: true);
                return;
            }

            if (!TryValidateImportedLayoutJson(response.layoutJson, out string errorMessage))
            {
                SetStatus($"Vision 返回的布局文件无效：{errorMessage}", logToConsole: true);
                return;
            }

            BeginLayoutJsonDocumentExport(
                response.fileName,
                response.layoutJson,
                "正在打开文件导出面板以保存来自 Vision 的 museum_layout.json。");
        }
    }
}
