using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace VFXViewer
{
    public partial class ExhibitPlacementManager
    {
        [Header("Layout 同步")]
        [SerializeField] private string m_RemoteLayoutSyncUrl = string.Empty;
        [SerializeField] private int m_RemoteLayoutSyncTimeoutSeconds = 15;

        private Coroutine m_RemoteLayoutSyncCoroutine;

        [Serializable]
        private sealed class RemoteLayoutSyncEnvelope
        {
            public string layoutJson;
            public LayoutDataList layout;
            public RemoteLayoutSyncEnvelopeData data;
        }

        [Serializable]
        private sealed class RemoteLayoutSyncEnvelopeData
        {
            public string layoutJson;
            public LayoutDataList layout;
        }

        public bool IsRemoteLayoutSyncInProgress => m_RemoteLayoutSyncCoroutine != null;
        public bool HasRemoteLayoutSyncUrlConfigured => !string.IsNullOrWhiteSpace(m_RemoteLayoutSyncUrl);

        public bool TryImportExternalLayoutJson(
            string json,
            bool replaceExistingScene,
            bool rebuildIfPossible,
            out string statusMessage)
        {
            statusMessage = string.Empty;
            if (!TryDeserializeImportedLayoutJson(json, out LayoutDataList importedLayout, out string parseError))
            {
                statusMessage = parseError;
                return false;
            }

            if (!TryWritePersistentJsonFile(GetLayoutFilePath(), importedLayout, out string saveError))
            {
                statusMessage = $"布局文件写入失败：{saveError}";
                return false;
            }

            ApplyLayoutSnapshotToMemory(importedLayout);
            statusMessage = $"布局已保存到 {m_LayoutFileName}，共 {importedLayout.items.Count} 个展品。";
            return true;
        }

        public bool TryExportSavedLayoutJson(
            out string fileName,
            out string layoutJson,
            out string statusMessage)
        {
            fileName = m_LayoutFileName;
            layoutJson = string.Empty;
            statusMessage = string.Empty;

            string path = GetLayoutFilePath();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                statusMessage = $"Vision 端还没有已保存的 {m_LayoutFileName}。";
                return false;
            }

            try
            {
                layoutJson = File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                statusMessage = $"读取 Vision 本地布局文件失败：{ex.Message}";
                return false;
            }

            if (!TryDeserializeImportedLayoutJson(layoutJson, out LayoutDataList exportedLayout, out string parseError))
            {
                statusMessage = parseError.Replace("导入失败", "导出失败");
                return false;
            }

            statusMessage = $"已从 Vision 主机读取 {m_LayoutFileName}，共 {exportedLayout.items.Count} 个展品。";
            return true;
        }

        public bool TrySyncLayoutFromServer()
        {
            if (m_CurrentAppMode != ExhibitAppMode.Placement)
            {
                onStatusMessage?.Invoke("当前处于体验模式，无法从服务器同步 layout。");
                return false;
            }

            if (m_RemoteLayoutSyncCoroutine != null)
            {
                onStatusMessage?.Invoke("正在从服务器同步 layout，请稍候。");
                return false;
            }

            string requestUrl = string.IsNullOrWhiteSpace(m_RemoteLayoutSyncUrl)
                ? string.Empty
                : m_RemoteLayoutSyncUrl.Trim();
            if (string.IsNullOrEmpty(requestUrl))
            {
                onStatusMessage?.Invoke("请先在 ExhibitPlacementManager 上配置 Layout Server URL。");
                return false;
            }

            m_RemoteLayoutSyncCoroutine = StartCoroutine(SyncLayoutFromServerCoroutine(requestUrl));
            return true;
        }

        public bool TryClearSavedLayout()
        {
            if (m_CurrentAppMode != ExhibitAppMode.Placement)
            {
                onStatusMessage?.Invoke("当前处于体验模式，无法清空 layout。");
                return false;
            }

            if (m_RemoteLayoutSyncCoroutine != null)
            {
                onStatusMessage?.Invoke("正在从服务器同步 layout，请稍候。");
                return false;
            }

            if (m_LayoutRedeployCoroutine != null)
            {
                onStatusMessage?.Invoke("一键布展进行中，无法清空 layout。");
                return false;
            }

            if (m_IsSaving || m_PendingSave)
            {
                onStatusMessage?.Invoke("当前正在保存数据，无法清空 layout。");
                return false;
            }

            string path = GetLayoutFilePath();
            int previousLayoutCount = m_CachedLayout != null ? m_CachedLayout.Count : 0;
            bool hadFile = !string.IsNullOrWhiteSpace(path) && File.Exists(path);
            bool hadMemoryLayout = m_HasLayoutLoaded ||
                                   previousLayoutCount > 0 ||
                                   m_LayoutSnapshotByInstanceId.Count > 0 ||
                                   m_DirtyLayoutInstanceIds.Count > 0 ||
                                   m_RemovedLayoutInstanceIds.Count > 0;
            bool hadQueuedLayoutSave = m_HasQueuedSave && m_QueuedSaveLayout;

            if (!hadFile && !hadMemoryLayout && !hadQueuedLayoutSave)
            {
                onStatusMessage?.Invoke("当前没有已保存的 layout 数据可清空。");
                return false;
            }

            if (hadQueuedLayoutSave)
                m_QueuedSaveLayout = false;

            if (hadFile)
            {
                try
                {
                    File.Delete(path);
                }
                catch (Exception ex)
                {
                    string errorMessage = $"清空 layout 失败：{ex.Message}";
                    PlacementDebugFileLogger.Log(
                        $"[LayoutSync] local layout clear failed path={path}, error={ex.Message}");
                    onStatusMessage?.Invoke(errorMessage);
                    return false;
                }
            }

            m_CachedLayout.Clear();
            m_HasLayoutLoaded = false;
            ResetLayoutSnapshotCache();

            PlacementDebugFileLogger.Log(
                $"[LayoutSync] local layout cleared path={path}, hadFile={hadFile}, hadMemoryLayout={hadMemoryLayout}, " +
                $"hadQueuedLayoutSave={hadQueuedLayoutSave}, previousLayoutCount={previousLayoutCount}");
            onStatusMessage?.Invoke("已清空本地 layout 数据，当前场景不会自动撤展。");
            return true;
        }

        private IEnumerator SyncLayoutFromServerCoroutine(string requestUrl)
        {
            onStatusMessage?.Invoke("正在从服务器同步 layout...");
            PlacementDebugFileLogger.Log($"[LayoutSync] request start url={requestUrl}");

            using (var request = UnityWebRequest.Get(requestUrl))
            {
                request.timeout = Mathf.Max(1, m_RemoteLayoutSyncTimeoutSeconds);
                request.SetRequestHeader("Accept", "application/json");

                UnityWebRequestAsyncOperation sendOperation;
                try
                {
                    sendOperation = request.SendWebRequest();
                }
                catch (InvalidOperationException ex)
                {
                    bool isPlainHttp = !string.IsNullOrWhiteSpace(requestUrl) &&
                                       requestUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
                    string invalidRequestMessage = isPlainHttp
                        ? "从服务器同步 layout 失败：当前构建禁止纯 HTTP 请求。请重新构建应用，或改用 HTTPS 地址。"
                        : $"从服务器同步 layout 失败：{ex.Message}";

                    PlacementDebugFileLogger.Log(
                        $"[LayoutSync] request invalid url={requestUrl}, error={ex.Message}, plainHttp={isPlainHttp}");
                    onStatusMessage?.Invoke(invalidRequestMessage);
                    m_RemoteLayoutSyncCoroutine = null;
                    yield break;
                }

                yield return sendOperation;

                string responseText = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
                if (request.result != UnityWebRequest.Result.Success)
                {
                    PlacementDebugFileLogger.Log(
                        $"[LayoutSync] request failed url={requestUrl}, responseCode={request.responseCode}, error={request.error}");
                    onStatusMessage?.Invoke($"从服务器同步 layout 失败：{request.error}");
                    m_RemoteLayoutSyncCoroutine = null;
                    yield break;
                }

                if (!TryExtractLayoutJsonFromRemoteResponse(responseText, out string layoutJson, out string parseError))
                {
                    PlacementDebugFileLogger.Log(
                        $"[LayoutSync] response parse failed url={requestUrl}, responseCode={request.responseCode}, error={parseError}, bytes={(responseText != null ? responseText.Length : 0)}");
                    onStatusMessage?.Invoke($"从服务器同步 layout 失败：{parseError}");
                    m_RemoteLayoutSyncCoroutine = null;
                    yield break;
                }

                bool success = TryImportExternalLayoutJson(
                    layoutJson,
                    replaceExistingScene: false,
                    rebuildIfPossible: false,
                    out string statusMessage);
                PlacementDebugFileLogger.Log(
                    $"[LayoutSync] request completed url={requestUrl}, responseCode={request.responseCode}, success={success}, message={statusMessage}, bytes={(layoutJson != null ? layoutJson.Length : 0)}");

                if (!string.IsNullOrWhiteSpace(statusMessage))
                    onStatusMessage?.Invoke(statusMessage);
            }

            m_RemoteLayoutSyncCoroutine = null;
        }

        private static bool TryExtractLayoutJsonFromRemoteResponse(
            string responseText,
            out string layoutJson,
            out string errorMessage)
        {
            layoutJson = string.Empty;
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(responseText))
            {
                errorMessage = "服务器返回内容为空。";
                return false;
            }

            if (TryDeserializeImportedLayoutJson(responseText, out _, out _))
            {
                layoutJson = responseText;
                return true;
            }

            RemoteLayoutSyncEnvelope envelope = null;
            try
            {
                envelope = JsonUtility.FromJson<RemoteLayoutSyncEnvelope>(responseText);
            }
            catch
            {
                envelope = null;
            }

            if (TryExtractLayoutJsonFromEnvelope(envelope, out layoutJson))
                return true;

            errorMessage = "服务器返回的不是有效布局 JSON。";
            return false;
        }

        private static bool TryExtractLayoutJsonFromEnvelope(
            RemoteLayoutSyncEnvelope envelope,
            out string layoutJson)
        {
            layoutJson = string.Empty;
            if (envelope == null)
                return false;

            if (TryNormalizeRemoteLayoutPayload(envelope.layoutJson, envelope.layout, out layoutJson))
                return true;

            return envelope.data != null &&
                   TryNormalizeRemoteLayoutPayload(envelope.data.layoutJson, envelope.data.layout, out layoutJson);
        }

        private static bool TryNormalizeRemoteLayoutPayload(
            string embeddedLayoutJson,
            LayoutDataList embeddedLayout,
            out string layoutJson)
        {
            layoutJson = string.Empty;

            if (!string.IsNullOrWhiteSpace(embeddedLayoutJson) &&
                TryDeserializeImportedLayoutJson(embeddedLayoutJson, out _, out _))
            {
                layoutJson = embeddedLayoutJson;
                return true;
            }

            if (embeddedLayout == null || embeddedLayout.items == null || embeddedLayout.items.Count == 0)
                return false;

            layoutJson = JsonUtility.ToJson(embeddedLayout, ShouldPrettyPrintPersistentJson());
            return true;
        }

        private static bool TryDeserializeImportedLayoutJson(
            string json,
            out LayoutDataList importedLayout,
            out string errorMessage)
        {
            importedLayout = null;
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(json))
            {
                errorMessage = "导入失败：布局文件为空。";
                return false;
            }

            try
            {
                importedLayout = JsonUtility.FromJson<LayoutDataList>(json);
            }
            catch (Exception ex)
            {
                errorMessage = $"导入失败：布局文件解析异常。{ex.Message}";
                return false;
            }

            if (importedLayout == null || importedLayout.items == null || importedLayout.items.Count == 0)
            {
                errorMessage = "导入失败：布局文件中没有展品数据。";
                return false;
            }

            return true;
        }
    }
}
