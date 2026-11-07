using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace VFXViewer.Editor
{
    /// <summary>
    /// 通过飞书 OpenAPI 读取 docx 文档中的表格，并导出为项目内 XLSX。
    /// </summary>
    internal static class FeishuDocxTableSync
    {
        private const string DefaultDocUrl =
            "https://my.feishu.cn/wiki/ZRR2wm6PkiBai2kS179c7wJqnkc?from=from_copylink";

        private const string DefaultOutputXlsxRelativePath = "docs/interaction_modules_p00_p07_feishu.xlsx";
        private const string LocalConfigRelativePath = "UserSettings/feishu_openapi.local.json";

        private const string EditorKeyAppId = "VFXViewer.FeishuOpenApi.AppId";
        private const string EditorKeyAppSecret = "VFXViewer.FeishuOpenApi.AppSecret";
        private const string EditorKeyDocUrl = "VFXViewer.FeishuOpenApi.DocUrl";
        private const string EditorKeyTableIndex = "VFXViewer.FeishuOpenApi.TableIndex";
        private const string EditorKeyTableNameKeyword = "VFXViewer.FeishuOpenApi.TableNameKeyword";
        private const string EditorKeySpreadsheetLinkOrToken = "VFXViewer.FeishuOpenApi.SpreadsheetLinkOrToken";
        private const string EditorKeyOutputXlsx = "VFXViewer.FeishuOpenApi.OutputXlsxRelativePath";

        [MenuItem("工具/项目文档/配置飞书OpenAPI...", priority = 12)]
        private static void OpenSettingsWindow()
        {
            FeishuOpenApiSettingsWindow.Open();
        }

        internal static bool TrySyncInteractionTableToXlsx(out string outputXlsxPath, out string error)
        {
            var settings = LoadSettings();
            return TrySyncTableToXlsx(settings, out outputXlsxPath, out error);
        }

        private static bool TrySyncTableToXlsx(FeishuOpenApiSettings settings, out string outputXlsxPath, out string error)
        {
            outputXlsxPath = string.Empty;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(settings.appId) || string.IsNullOrWhiteSpace(settings.appSecret))
            {
                error = "请先在 工具/项目文档/配置飞书OpenAPI 中填写 app_id 与 app_secret。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(settings.docUrl))
            {
                error = "文档链接为空，请在配置中填写飞书 wiki/docx 链接。";
                return false;
            }

            if (!TryGetProjectRoot(out var projectRoot))
            {
                error = "无法定位项目根目录。";
                return false;
            }

            if (!TryResolveOutputPath(projectRoot, settings.outputXlsxRelativePath, out outputXlsxPath, out error))
                return false;

            string folder = Path.GetDirectoryName(outputXlsxPath);
            if (!string.IsNullOrWhiteSpace(folder))
                Directory.CreateDirectory(folder);

            if (!TryGetTenantAccessToken(settings.appId, settings.appSecret, out var accessToken, out error))
                return false;

            if (TryResolveSpreadsheetTokenOverride(settings.spreadsheetLinkOrToken, out var overrideToken, out var overrideResolveInfo))
            {
                if (TryExportSheetToXlsxViaValuesApi(
                        overrideToken,
                        accessToken,
                        outputXlsxPath,
                        out var overrideDetail,
                        out var overrideError))
                {
                    Debug.Log($"[FeishuDocxTableSync] 已通过手填链接/Token同步表格：{overrideDetail}，resolve={overrideResolveInfo}");
                    return true;
                }

                Debug.LogWarning($"[FeishuDocxTableSync] 手填链接/Token同步失败，将回退 block 扫描。resolve={overrideResolveInfo}, error={overrideError}");
            }

            if (TryResolveSpreadsheetTokenFromDocShareAnchor(settings.docUrl, accessToken, out var shareToken, out var shareInfo))
            {
                if (TryExportSheetToXlsxViaValuesApi(
                        shareToken,
                        accessToken,
                        outputXlsxPath,
                        out var shareDetail,
                        out var shareError))
                {
                    Debug.Log($"[FeishuDocxTableSync] 已通过 docUrl#share 解析同步表格：{shareDetail}，resolve={shareInfo}");
                    return true;
                }

                Debug.LogWarning($"[FeishuDocxTableSync] docUrl#share 解析后同步失败，将回退 block 扫描。resolve={shareInfo}, error={shareError}");
            }

            string documentToken = string.Empty;
            bool hasDocumentToken = false;

            if (TryResolveWikiNodeInfo(settings.docUrl, accessToken, out var wikiObjType, out var wikiObjToken, out var wikiInfoError))
            {
                if (IsSheetObjType(wikiObjType))
                {
                    if (TryExportSheetToXlsxViaValuesApi(
                            wikiObjToken,
                            accessToken,
                            outputXlsxPath,
                            out var wikiSheetDetail,
                            out var wikiSheetError))
                    {
                        Debug.Log($"[FeishuDocxTableSync] 已通过 wiki 节点(sheet)同步表格：{wikiSheetDetail}");
                        return true;
                    }

                    error = $"wiki 节点类型为 sheet，但 Sheets 同步失败：{wikiSheetError}";
                    return false;
                }

                if (IsDocObjType(wikiObjType) && !string.IsNullOrWhiteSpace(wikiObjToken))
                {
                    documentToken = wikiObjToken;
                    hasDocumentToken = true;
                }
            }
            else if (!string.IsNullOrWhiteSpace(wikiInfoError))
            {
                Debug.LogWarning($"[FeishuDocxTableSync] wiki 节点预解析失败，将回退原有解析：{wikiInfoError}");
            }

            if (!hasDocumentToken)
            {
                if (!TryResolveDocumentToken(settings.docUrl, accessToken, out documentToken, out error))
                    return false;
            }

            if (!TryFetchAllBlocks(documentToken, accessToken, out var blockById, out var tableBlockIds, out error))
                return false;

            // 当前项目约定：仅处理文档中嵌入的飞书表格（sheet）并下载为 xlsx。
            // 先补拉 children，避免 sheet block 在深层结构中遗漏。
            TryFetchBlocksByChildrenTraversal(documentToken, accessToken, blockById, tableBlockIds);

            string detail;
            if (TryDownloadFeishuSheetXlsxFromBlocks(
                    blockById,
                    accessToken,
                    outputXlsxPath,
                    settings.tableNameKeyword,
                    out detail))
            {
                Debug.Log($"[FeishuDocxTableSync] 已下载飞书表格为 XLSX：{detail}");
                return true;
            }

            string summary = BuildBlockTypeSummary(blockById, 12);
            error = $"未找到可下载的飞书表格（sheet）。已扫描 block 数={blockById.Count}，类型分布={summary}，下载尝试结果：{detail}";
            return false;
        }

        private static bool TryResolveWikiNodeInfo(
            string url,
            string accessToken,
            out string objType,
            out string objToken,
            out string error)
        {
            objType = string.Empty;
            objToken = string.Empty;
            error = string.Empty;

            if (!TryExtractWikiTokenFromUrl(url, out var wikiToken))
                return false;

            string api =
                $"https://open.feishu.cn/open-apis/wiki/v2/spaces/get_node?token={Uri.EscapeDataString(wikiToken)}";

            if (!TrySendRequest(api, "GET", null, accessToken, out var root, out var reqError))
            {
                error = reqError;
                return false;
            }

            if (!TryEnsureApiSuccess(root, out var apiError))
            {
                error = apiError;
                return false;
            }

            var node = root["data"]?["node"] as JObject ?? root["data"] as JObject;
            if (node == null)
            {
                error = "wiki 节点响应缺少 data.node";
                return false;
            }

            objType = (string)(node["obj_type"] ?? node["objType"] ?? string.Empty);
            objToken = (string)(node["obj_token"] ?? node["objToken"] ?? string.Empty);
            if (string.IsNullOrWhiteSpace(objToken))
            {
                error = "wiki 节点 obj_token 为空";
                return false;
            }

            return true;
        }

        private static bool TryExtractWikiTokenFromUrl(string url, out string wikiToken)
        {
            wikiToken = string.Empty;
            if (string.IsNullOrWhiteSpace(url))
                return false;

            var m = Regex.Match(url, @"/wiki/([A-Za-z0-9]+)", RegexOptions.IgnoreCase);
            if (!m.Success)
                return false;

            wikiToken = m.Groups[1].Value;
            return !string.IsNullOrWhiteSpace(wikiToken);
        }

        private static bool IsSheetObjType(string objType)
        {
            if (string.IsNullOrWhiteSpace(objType))
                return false;
            return objType.IndexOf("sheet", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsDocObjType(string objType)
        {
            if (string.IsNullOrWhiteSpace(objType))
                return false;
            return string.Equals(objType, "docx", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(objType, "doc", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryResolveSpreadsheetTokenOverride(
            string sheetLinkOrToken,
            out string spreadsheetToken,
            out string resolveInfo)
        {
            spreadsheetToken = string.Empty;
            resolveInfo = "empty";

            if (string.IsNullOrWhiteSpace(sheetLinkOrToken))
                return false;

            string raw = sheetLinkOrToken.Trim();
            raw = Uri.UnescapeDataString(raw);

            if (TryExtractTokenParamValue(raw, out var tokenValueFromRaw))
                raw = tokenValueFromRaw;

            if (TryExtractSpreadsheetTokenFromText(raw, out spreadsheetToken))
            {
                resolveInfo = "direct";
                return true;
            }

            if (TryResolveSpreadsheetTokenFromApplink(raw, out spreadsheetToken, out var appInfo))
            {
                resolveInfo = "applink:" + appInfo;
                return true;
            }

            // 对于无法识别的 token 字符串，仍作为候选向下传递（后续会尝试 wiki get_node 转换）。
            if (Regex.IsMatch(raw, @"^[A-Za-z0-9=+/_-]{8,}$"))
            {
                spreadsheetToken = raw;
                resolveInfo = "raw_fallback";
                return true;
            }

            resolveInfo = "unresolved";
            return false;
        }

        private static bool TryResolveSpreadsheetTokenFromDocShareAnchor(
            string docUrl,
            string accessToken,
            out string spreadsheetToken,
            out string detail)
        {
            spreadsheetToken = string.Empty;
            detail = string.Empty;
            if (string.IsNullOrWhiteSpace(docUrl)) return false;

            var match = Regex.Match(docUrl, @"#share-([A-Za-z0-9]+)", RegexOptions.IgnoreCase);
            if (!match.Success) return false;

            string shareToken = match.Groups[1].Value;
            if (string.IsNullOrWhiteSpace(shareToken)) return false;

            if (TryResolveKnowledgeBaseSheetObjToken(shareToken, accessToken, out spreadsheetToken, out detail))
            {
                detail = $"shareToken={shareToken}; {detail}";
                return true;
            }

            detail = $"shareToken={shareToken}; {detail}";
            return false;
        }

        private static bool TryExtractSpreadsheetTokenFromText(string text, out string spreadsheetToken)
        {
            spreadsheetToken = string.Empty;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            if (TryExtractTokenParamValue(text, out var tokenValue))
            {
                spreadsheetToken = tokenValue;
                return true;
            }

            var m = Regex.Match(text, @"/sheets/([A-Za-z0-9_-]+)", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                spreadsheetToken = m.Groups[1].Value;
                return !string.IsNullOrWhiteSpace(spreadsheetToken);
            }

            if (Regex.IsMatch(text, @"^[A-Za-z0-9=+/_-]{8,}$"))
            {
                spreadsheetToken = text;
                return true;
            }

            var queryToken = Regex.Match(text, @"[?&]token=([^&]+)", RegexOptions.IgnoreCase);
            if (queryToken.Success)
            {
                string token = Uri.UnescapeDataString(queryToken.Groups[1].Value);
                if (Regex.IsMatch(token, @"^[A-Za-z0-9=+/_-]{8,}$"))
                {
                    spreadsheetToken = token;
                    return true;
                }
            }

            return false;
        }

        private static bool TryResolveSpreadsheetTokenFromApplink(
            string applinkUrl,
            out string spreadsheetToken,
            out string info)
        {
            spreadsheetToken = string.Empty;
            info = string.Empty;
            if (string.IsNullOrWhiteSpace(applinkUrl)) return false;

            string requestUrl = applinkUrl.Trim();
            if (requestUrl.IndexOf("applink.feishu.cn", StringComparison.OrdinalIgnoreCase) < 0)
            {
                // 允许只填 applink 的 token 参数值（例如 AmB...%3D）。
                string maybeToken = Uri.UnescapeDataString(requestUrl);
                if (TryExtractTokenParamValue(maybeToken, out var extracted))
                    maybeToken = extracted;
                if (!Regex.IsMatch(maybeToken, @"^[A-Za-z0-9=+/_-]{8,}$"))
                    return false;

                requestUrl =
                    "https://applink.feishu.cn/client/message/link/open?token=" + Uri.EscapeDataString(maybeToken);
            }

            if (requestUrl.IndexOf("applink.feishu.cn", StringComparison.OrdinalIgnoreCase) < 0)
                return false;

            try
            {
                using (var request = UnityWebRequest.Get(requestUrl))
                {
                    request.redirectLimit = 8;
                    var op = request.SendWebRequest();
                    while (!op.isDone)
                    {
                    }

                    string finalUrl = request.url ?? string.Empty;
                    if (TryExtractSpreadsheetTokenFromText(finalUrl, out spreadsheetToken))
                    {
                        info = "finalUrl";
                        return true;
                    }

                    string text = request.downloadHandler?.text ?? string.Empty;
                    if (TryExtractSpreadsheetTokenFromText(text, out spreadsheetToken))
                    {
                        info = "htmlBody";
                        return true;
                    }

                    info = $"no_token_in_response_http_{request.responseCode}";
                    return false;
                }
            }
            catch (Exception ex)
            {
                info = "exception:" + ex.Message;
                return false;
            }
        }

        private static bool TryExtractTokenParamValue(string text, out string tokenValue)
        {
            tokenValue = string.Empty;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            string raw = text.Trim();
            var direct = Regex.Match(raw, @"^token=([^&]+)$", RegexOptions.IgnoreCase);
            if (direct.Success)
            {
                string v = Uri.UnescapeDataString(direct.Groups[1].Value).Trim();
                if (Regex.IsMatch(v, @"^[A-Za-z0-9=+/_-]{8,}$"))
                {
                    tokenValue = v;
                    return true;
                }
            }

            var query = Regex.Match(raw, @"[?&]token=([^&]+)", RegexOptions.IgnoreCase);
            if (query.Success)
            {
                string v = Uri.UnescapeDataString(query.Groups[1].Value).Trim();
                if (Regex.IsMatch(v, @"^[A-Za-z0-9=+/_-]{8,}$"))
                {
                    tokenValue = v;
                    return true;
                }
            }

            return false;
        }

        private static List<string> BuildParseOrder(
            List<string> tableBlockIds,
            int preferredIndex,
            string preferredNameKeyword,
            Dictionary<string, JObject> blockById)
        {
            var result = new List<string>();
            if (tableBlockIds == null || tableBlockIds.Count == 0)
                return result;

            int index = Mathf.Clamp(preferredIndex, 0, tableBlockIds.Count - 1);
            result.Add(tableBlockIds[index]);
            for (int i = 0; i < tableBlockIds.Count; i++)
            {
                if (i == index) continue;
                result.Add(tableBlockIds[i]);
            }

            result = result
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!string.IsNullOrWhiteSpace(preferredNameKeyword))
            {
                string keyword = preferredNameKeyword.Trim();
                var hit = new List<string>();
                var miss = new List<string>();

                for (int i = 0; i < result.Count; i++)
                {
                    string id = result[i];
                    if (TableBlockMatchesKeyword(id, keyword, blockById))
                        hit.Add(id);
                    else
                        miss.Add(id);
                }

                if (hit.Count > 0)
                {
                    hit.AddRange(miss);
                    result = hit;
                }
            }

            return result;
        }

        private static bool TableBlockMatchesKeyword(
            string tableBlockId,
            string keyword,
            Dictionary<string, JObject> blockById)
        {
            if (string.IsNullOrWhiteSpace(tableBlockId) || string.IsNullOrWhiteSpace(keyword))
                return false;
            if (blockById == null || !blockById.TryGetValue(tableBlockId, out var block) || block == null)
                return false;

            var candidates = new List<string>();
            candidates.Add((string)block["title"]);
            candidates.Add((string)block["name"]);
            candidates.Add((string)block["block_name"]);

            var tableMeta = block["table"] as JObject;
            if (tableMeta != null)
            {
                candidates.Add((string)tableMeta["title"]);
                candidates.Add((string)tableMeta["name"]);
                candidates.Add((string)tableMeta["block_name"]);
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                if (ContainsIgnoreCase(candidates[i], keyword))
                    return true;
            }

            return false;
        }

        private static bool ContainsIgnoreCase(string source, string keyword)
        {
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(keyword))
                return false;
            return source.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool TryGetTenantAccessToken(string appId, string appSecret, out string accessToken, out string error)
        {
            accessToken = string.Empty;
            error = string.Empty;

            const string url = "https://open.feishu.cn/open-apis/auth/v3/tenant_access_token/internal";
            var payload = new JObject
            {
                ["app_id"] = appId,
                ["app_secret"] = appSecret
            };

            if (!TrySendRequest(url, "POST", payload.ToString(), null, out var root, out error))
                return false;

            if (!TryEnsureApiSuccess(root, out error))
                return false;

            accessToken = (string)(root["tenant_access_token"] ?? root["app_access_token"]);
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                error = "鉴权成功但响应中没有 tenant_access_token。";
                return false;
            }

            return true;
        }

        private static bool TryResolveDocumentToken(string docUrl, string accessToken, out string documentToken, out string error)
        {
            documentToken = string.Empty;
            error = string.Empty;

            if (!TryExtractDocToken(docUrl, out var tokenType, out var rawToken))
            {
                error = "无法从链接中提取 token（支持 /wiki/{token} 或 /docx/{token}）。";
                return false;
            }

            if (tokenType == DocTokenType.Docx)
            {
                documentToken = rawToken;
                return true;
            }

            string url =
                $"https://open.feishu.cn/open-apis/wiki/v2/spaces/get_node?token={Uri.EscapeDataString(rawToken)}";

            if (!TrySendRequest(url, "GET", null, accessToken, out var root, out error))
                return false;

            if (!TryEnsureApiSuccess(root, out error))
                return false;

            var node = root["data"]?["node"] ?? root["data"];
            if (node == null)
            {
                error = "wiki 节点响应缺少 data.node。";
                return false;
            }

            string objType = (string)(node["obj_type"] ?? node["objType"]);
            string objToken = (string)(node["obj_token"] ?? node["objToken"]);

            if (string.IsNullOrWhiteSpace(objToken))
            {
                error = "wiki 节点中缺少 obj_token。";
                return false;
            }

            if (!string.Equals(objType, "docx", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(objType, "doc", StringComparison.OrdinalIgnoreCase))
            {
                error = $"当前 wiki 节点类型是 {objType}，不是 docx 文档。";
                return false;
            }

            documentToken = objToken;
            return true;
        }

        private static bool TryFetchAllBlocks(
            string documentToken,
            string accessToken,
            out Dictionary<string, JObject> blockById,
            out List<string> tableBlockIds,
            out string error)
        {
            blockById = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
            tableBlockIds = new List<string>();
            error = string.Empty;

            string pageToken = string.Empty;
            bool hasMore;

            do
            {
                string url =
                    $"https://open.feishu.cn/open-apis/docx/v1/documents/{Uri.EscapeDataString(documentToken)}/blocks?page_size=500";
                if (!string.IsNullOrWhiteSpace(pageToken))
                    url += $"&page_token={Uri.EscapeDataString(pageToken)}";

                if (!TrySendRequest(url, "GET", null, accessToken, out var root, out error))
                    return false;

                if (!TryEnsureApiSuccess(root, out error))
                    return false;

                var data = root["data"];
                var items = data?["items"] as JArray ?? data?["blocks"] as JArray;
                if (items != null)
                {
                    foreach (var item in items)
                    {
                        var block = NormalizeBlockObject(item);
                        if (block == null) continue;

                        string blockId = GetBlockId(block);
                        if (string.IsNullOrWhiteSpace(blockId)) continue;

                        blockById[blockId] = block;
                        if (IsTableBlock(block))
                            tableBlockIds.Add(blockId);
                    }
                }

                pageToken = (string)(data?["page_token"] ?? data?["next_page_token"] ?? string.Empty);
                hasMore = data?["has_more"]?.Value<bool>() ?? !string.IsNullOrWhiteSpace(pageToken);
            } while (hasMore);

            tableBlockIds = tableBlockIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return true;
        }

        private static void TryFetchBlocksByChildrenTraversal(
            string documentToken,
            string accessToken,
            Dictionary<string, JObject> blockById,
            List<string> tableBlockIds)
        {
            if (string.IsNullOrWhiteSpace(documentToken)) return;
            if (blockById == null || tableBlockIds == null) return;

            var rootCandidates = new List<string> { documentToken };

            // 尝试补充“可能是根块”的候选，兼容不同返回结构。
            foreach (var kv in blockById)
            {
                var parentId = GetParentId(kv.Value);
                if (string.IsNullOrWhiteSpace(parentId))
                    rootCandidates.Add(kv.Key);
            }

            var queue = new Queue<string>(rootCandidates.Distinct(StringComparer.OrdinalIgnoreCase));
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            while (queue.Count > 0)
            {
                string parentBlockId = queue.Dequeue();
                if (string.IsNullOrWhiteSpace(parentBlockId)) continue;
                if (!visited.Add(parentBlockId)) continue;

                string pageToken = string.Empty;
                bool hasMore;
                do
                {
                    string url =
                        $"https://open.feishu.cn/open-apis/docx/v1/documents/{Uri.EscapeDataString(documentToken)}/blocks/{Uri.EscapeDataString(parentBlockId)}/children?page_size=500";
                    if (!string.IsNullOrWhiteSpace(pageToken))
                        url += $"&page_token={Uri.EscapeDataString(pageToken)}";

                    if (!TrySendRequest(url, "GET", null, accessToken, out var root, out _))
                        break;

                    if (!TryEnsureApiSuccess(root, out _))
                        break;

                    var data = root["data"];
                    var items = data?["items"] as JArray ?? data?["blocks"] as JArray;
                    if (items != null)
                    {
                        foreach (var item in items)
                        {
                            var block = NormalizeBlockObject(item);
                            if (block == null) continue;

                            string blockId = GetBlockId(block);
                            if (string.IsNullOrWhiteSpace(blockId)) continue;

                            blockById[blockId] = block;
                            if (IsTableBlock(block))
                                tableBlockIds.Add(blockId);

                            queue.Enqueue(blockId);
                            var children = GetChildrenIds(block);
                            for (int i = 0; i < children.Count; i++)
                                queue.Enqueue(children[i]);
                        }
                    }

                    pageToken = (string)(data?["page_token"] ?? data?["next_page_token"] ?? string.Empty);
                    hasMore = data?["has_more"]?.Value<bool>() ?? !string.IsNullOrWhiteSpace(pageToken);
                } while (hasMore);
            }

            if (tableBlockIds.Count > 1)
            {
                var unique = tableBlockIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                tableBlockIds.Clear();
                tableBlockIds.AddRange(unique);
            }
        }

        private static void TryHydrateTableSubtreeForParsing(
            string documentToken,
            string accessToken,
            string tableBlockId,
            Dictionary<string, JObject> blockById,
            List<string> tableBlockIds)
        {
            if (string.IsNullOrWhiteSpace(tableBlockId)) return;
            if (blockById == null) return;

            // 目标：确保 table -> row -> cell -> paragraph/text 这一段都在本地 blockById 中可用。
            TryFetchChildrenRecursively(documentToken, accessToken, tableBlockId, blockById, tableBlockIds, 4096);
        }

        private static void TryFetchChildrenRecursively(
            string documentToken,
            string accessToken,
            string startBlockId,
            Dictionary<string, JObject> blockById,
            List<string> tableBlockIds,
            int maxVisited)
        {
            if (string.IsNullOrWhiteSpace(documentToken) || string.IsNullOrWhiteSpace(startBlockId)) return;
            if (blockById == null) return;
            if (maxVisited <= 0) maxVisited = 1024;

            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<string>();
            queue.Enqueue(startBlockId);

            while (queue.Count > 0 && visited.Count < maxVisited)
            {
                string parentId = queue.Dequeue();
                if (string.IsNullOrWhiteSpace(parentId)) continue;
                if (!visited.Add(parentId)) continue;

                string pageToken = string.Empty;
                bool hasMore;
                do
                {
                    string url =
                        $"https://open.feishu.cn/open-apis/docx/v1/documents/{Uri.EscapeDataString(documentToken)}/blocks/{Uri.EscapeDataString(parentId)}/children?page_size=500";
                    if (!string.IsNullOrWhiteSpace(pageToken))
                        url += $"&page_token={Uri.EscapeDataString(pageToken)}";

                    if (!TrySendRequest(url, "GET", null, accessToken, out var root, out _))
                        break;
                    if (!TryEnsureApiSuccess(root, out _))
                        break;

                    var data = root["data"];
                    var items = data?["items"] as JArray ?? data?["blocks"] as JArray;
                    if (items != null)
                    {
                        foreach (var item in items)
                        {
                            var block = NormalizeBlockObject(item);
                            if (block == null) continue;

                            string blockId = GetBlockId(block);
                            if (string.IsNullOrWhiteSpace(blockId)) continue;

                            blockById[blockId] = block;
                            if (tableBlockIds != null && IsTableBlock(block))
                                tableBlockIds.Add(blockId);

                            queue.Enqueue(blockId);
                            var children = GetChildrenIds(block);
                            for (int i = 0; i < children.Count; i++)
                                queue.Enqueue(children[i]);
                        }
                    }

                    pageToken = (string)(data?["page_token"] ?? data?["next_page_token"] ?? string.Empty);
                    hasMore = data?["has_more"]?.Value<bool>() ?? !string.IsNullOrWhiteSpace(pageToken);
                } while (hasMore);
            }
        }

        private static bool TryDownloadFeishuSheetXlsxFromBlocks(
            Dictionary<string, JObject> blockById,
            string accessToken,
            string outputXlsxPath,
            string preferredNameKeyword,
            out string detail)
        {
            detail = "未发现可下载的 sheet token。";
            if (blockById == null || blockById.Count == 0)
            {
                detail = "block 为空。";
                return false;
            }

            var candidates = ExtractFeishuSheetCandidates(blockById, preferredNameKeyword);
            if (candidates.Count == 0)
                return false;

            var errors = new List<string>();
            for (int i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                if (!TryDownloadFileBytesByToken(candidate.token, accessToken, out var bytes, out var downloadError))
                {
                    if (TryExportSheetToXlsxViaValuesApi(
                            candidate.token,
                            accessToken,
                            outputXlsxPath,
                            out var exportDetail,
                            out var exportError))
                    {
                        detail = $"name={candidate.name}, token={candidate.token}, block={candidate.sourceBlockId}, via=values_api({exportDetail})";
                        return true;
                    }

                    errors.Add($"token={candidate.token}: download={downloadError}; values_api={exportError}");
                    continue;
                }

                if (!IsXlsxBytes(bytes))
                {
                    if (TryExportSheetToXlsxViaValuesApi(
                            candidate.token,
                            accessToken,
                            outputXlsxPath,
                            out var exportDetail,
                            out var exportError))
                    {
                        detail = $"name={candidate.name}, token={candidate.token}, block={candidate.sourceBlockId}, via=values_api({exportDetail})";
                        return true;
                    }

                    errors.Add($"token={candidate.token}: 下载结果不是 xlsx 文件; values_api={exportError}");
                    continue;
                }

                File.WriteAllBytes(outputXlsxPath, bytes);
                detail = $"name={candidate.name}, token={candidate.token}, block={candidate.sourceBlockId}";
                return true;
            }

            string sampleTokens = string.Join(",", candidates.Take(5).Select(x => x.token));
            detail = $"候选={candidates.Count}，sampleTokens=[{sampleTokens}]，错误={string.Join(" | ", errors.Take(3))}";
            return false;
        }

        private static bool TryExportSheetToXlsxViaValuesApi(
            string spreadsheetToken,
            string accessToken,
            string outputXlsxPath,
            out string detail,
            out string error)
        {
            detail = string.Empty;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(spreadsheetToken))
            {
                error = "spreadsheet token 为空";
                return false;
            }

            string effectiveToken = spreadsheetToken;
            string tokenResolveInfo = "raw";

            if (!TryQuerySheetInfo(effectiveToken, accessToken, out var sheetIdOrTitle, out var sheetTitle, out var infoError))
            {
                if (TryResolveKnowledgeBaseSheetObjToken(
                        spreadsheetToken,
                        accessToken,
                        out var kbObjToken,
                        out var kbResolveInfo))
                {
                    effectiveToken = kbObjToken;
                    tokenResolveInfo = "wiki_obj_token:" + kbResolveInfo;
                    if (!TryQuerySheetInfo(effectiveToken, accessToken, out sheetIdOrTitle, out sheetTitle, out infoError))
                    {
                        error = $"query_sheet_info_failed_after_wiki_resolve({tokenResolveInfo}): {infoError}";
                        return false;
                    }
                }
                else
                {
                    error = $"query_sheet_info_failed: {infoError}; wiki_resolve_failed={kbResolveInfo}";
                    return false;
                }
            }

            if (!TryQuerySheetValues(effectiveToken, sheetIdOrTitle, accessToken, out var rows, out var valuesError))
            {
                error = $"query_values_failed: {valuesError}";
                return false;
            }

            if (rows == null || rows.Count == 0)
            {
                error = "sheet values 为空";
                return false;
            }

            WriteRowsToXlsx(outputXlsxPath, rows);
            detail = $"sheet={sheetTitle}, rows={rows.Count}, tokenSource={tokenResolveInfo}";
            return true;
        }

        private static bool TryResolveKnowledgeBaseSheetObjToken(
            string wikiNodeToken,
            string accessToken,
            out string spreadsheetToken,
            out string detail)
        {
            spreadsheetToken = string.Empty;
            detail = string.Empty;

            if (string.IsNullOrWhiteSpace(wikiNodeToken))
            {
                detail = "wiki token 为空";
                return false;
            }

            string url =
                $"https://open.feishu.cn/open-apis/wiki/v2/spaces/get_node?token={Uri.EscapeDataString(wikiNodeToken)}";

            if (!TrySendRequest(url, "GET", null, accessToken, out var root, out var reqError))
            {
                detail = "request_failed:" + reqError;
                return false;
            }

            if (!TryEnsureApiSuccess(root, out var apiError))
            {
                detail = "api_failed:" + apiError;
                return false;
            }

            var node = root["data"]?["node"] as JObject ?? root["data"] as JObject;
            if (node == null)
            {
                detail = "node_null";
                return false;
            }

            string objType = (string)(node["obj_type"] ?? node["objType"]);
            string objToken = (string)(node["obj_token"] ?? node["objToken"]);
            if (string.IsNullOrWhiteSpace(objToken))
            {
                detail = "obj_token_empty";
                return false;
            }

            spreadsheetToken = objToken;
            detail = "obj_type=" + (string.IsNullOrWhiteSpace(objType) ? "unknown" : objType);
            return true;
        }

        private static bool TryQuerySheetInfo(
            string spreadsheetToken,
            string accessToken,
            out string sheetIdOrTitle,
            out string sheetTitle,
            out string error)
        {
            sheetIdOrTitle = string.Empty;
            sheetTitle = string.Empty;
            error = string.Empty;

            string[] urls =
            {
                $"https://open.feishu.cn/open-apis/sheets/v3/spreadsheets/{Uri.EscapeDataString(spreadsheetToken)}/sheets/query",
                $"https://open.feishu.cn/open-apis/sheets/v2/spreadsheets/{Uri.EscapeDataString(spreadsheetToken)}/metainfo"
            };

            var errs = new List<string>();
            for (int i = 0; i < urls.Length; i++)
            {
                if (!TrySendRequest(urls[i], "GET", null, accessToken, out var root, out var reqError))
                {
                    errs.Add(reqError);
                    continue;
                }

                if (!TryEnsureApiSuccess(root, out var apiError))
                {
                    errs.Add(apiError);
                    continue;
                }

                if (TryParseFirstSheetInfo(root, out sheetIdOrTitle, out sheetTitle))
                    return true;

                errs.Add("响应里没有可用 sheet 信息");
            }

            error = string.Join(" | ", errs.Where(x => !string.IsNullOrWhiteSpace(x)));
            return false;
        }

        private static bool TryParseFirstSheetInfo(JObject root, out string sheetIdOrTitle, out string sheetTitle)
        {
            sheetIdOrTitle = string.Empty;
            sheetTitle = string.Empty;
            if (root == null) return false;

            var data = root["data"] as JObject;
            if (data == null) return false;

            JArray sheets = data["sheets"] as JArray;
            if (sheets == null)
            {
                var spreadsheet = data["spreadsheet"] as JObject;
                if (spreadsheet != null)
                    sheets = spreadsheet["sheets"] as JArray;
            }

            if (sheets == null || sheets.Count == 0) return false;

            var first = sheets[0] as JObject;
            if (first == null) return false;

            string sheetId = (string)(first["sheet_id"] ?? first["sheetId"] ?? first["id"]);
            sheetTitle = (string)(first["title"] ?? first["name"] ?? first["sheet_name"] ?? first["sheetName"]);

            sheetIdOrTitle = !string.IsNullOrWhiteSpace(sheetId)
                ? sheetId
                : (!string.IsNullOrWhiteSpace(sheetTitle) ? sheetTitle : string.Empty);

            return !string.IsNullOrWhiteSpace(sheetIdOrTitle);
        }

        private static bool TryQuerySheetValues(
            string spreadsheetToken,
            string sheetIdOrTitle,
            string accessToken,
            out List<List<string>> rows,
            out string error)
        {
            rows = null;
            error = string.Empty;

            string prefix = EscapeSheetRangePrefix(sheetIdOrTitle);
            string range = $"{prefix}!A1:ZZ2000";
            string encodedRange = Uri.EscapeDataString(range);

            string[] getUrls =
            {
                $"https://open.feishu.cn/open-apis/sheets/v2/spreadsheets/{Uri.EscapeDataString(spreadsheetToken)}/values/{encodedRange}",
                $"https://open.feishu.cn/open-apis/sheets/v3/spreadsheets/{Uri.EscapeDataString(spreadsheetToken)}/values/{encodedRange}"
            };

            var errs = new List<string>();
            for (int i = 0; i < getUrls.Length; i++)
            {
                if (!TrySendRequest(getUrls[i], "GET", null, accessToken, out var root, out var reqError))
                {
                    errs.Add(reqError);
                    continue;
                }

                if (!TryEnsureApiSuccess(root, out var apiError))
                {
                    errs.Add(apiError);
                    continue;
                }

                if (TryParseValuesFromResponse(root, out rows))
                    return true;

                errs.Add("GET values 响应无 values");
            }

            string batchUrl =
                $"https://open.feishu.cn/open-apis/sheets/v2/spreadsheets/{Uri.EscapeDataString(spreadsheetToken)}/values_batch_get";
            var payload = new JObject
            {
                ["ranges"] = new JArray { range }
            };

            if (TrySendRequest(batchUrl, "POST", payload.ToString(), accessToken, out var batchRoot, out var batchReqError))
            {
                if (TryEnsureApiSuccess(batchRoot, out var batchApiError))
                {
                    if (TryParseValuesFromResponse(batchRoot, out rows))
                        return true;

                    errs.Add("batch_get 响应无 values");
                }
                else
                {
                    errs.Add(batchApiError);
                }
            }
            else
            {
                errs.Add(batchReqError);
            }

            error = string.Join(" | ", errs.Where(x => !string.IsNullOrWhiteSpace(x)));
            return false;
        }

        private static string EscapeSheetRangePrefix(string sheetIdOrTitle)
        {
            if (string.IsNullOrWhiteSpace(sheetIdOrTitle))
                return "Sheet1";

            bool needQuote = sheetIdOrTitle.IndexOf(' ') >= 0
                             || sheetIdOrTitle.IndexOf('!') >= 0
                             || sheetIdOrTitle.IndexOf('\'') >= 0
                             || sheetIdOrTitle.IndexOf('-') >= 0
                             || sheetIdOrTitle.IndexOf('(') >= 0
                             || sheetIdOrTitle.IndexOf(')') >= 0;

            if (!needQuote)
                return sheetIdOrTitle;

            return "'" + sheetIdOrTitle.Replace("'", "''") + "'";
        }

        private static bool TryParseValuesFromResponse(JObject root, out List<List<string>> rows)
        {
            rows = null;
            if (root == null) return false;

            var data = root["data"] as JObject;
            if (data == null) return false;

            JToken valuesToken = data["values"];
            if (valuesToken == null)
            {
                var valueRange = data["valueRange"] as JObject;
                if (valueRange != null)
                    valuesToken = valueRange["values"];
            }

            if (valuesToken == null)
            {
                var valueRanges = data["valueRanges"] as JArray;
                if (valueRanges != null && valueRanges.Count > 0)
                    valuesToken = valueRanges[0]?["values"];
            }

            var valuesArray = valuesToken as JArray;
            if (valuesArray == null)
                return false;

            rows = new List<List<string>>(valuesArray.Count);
            foreach (var rowToken in valuesArray)
            {
                var row = new List<string>();
                if (rowToken is JArray rowArray)
                {
                    foreach (var cell in rowArray)
                        row.Add(cell?.ToString() ?? string.Empty);
                }
                else
                {
                    row.Add(rowToken?.ToString() ?? string.Empty);
                }

                rows.Add(row);
            }

            // 剔除末尾全空行，避免生成超大空白表格。
            for (int i = rows.Count - 1; i >= 0; i--)
            {
                bool allEmpty = true;
                var row = rows[i];
                for (int c = 0; c < row.Count; c++)
                {
                    if (!string.IsNullOrWhiteSpace(row[c]))
                    {
                        allEmpty = false;
                        break;
                    }
                }

                if (allEmpty)
                    rows.RemoveAt(i);
                else
                    break;
            }

            return rows.Count > 0;
        }

        private static List<EmbeddedFileCandidate> ExtractFeishuSheetCandidates(
            Dictionary<string, JObject> blockById,
            string preferredNameKeyword)
        {
            var result = new List<EmbeddedFileCandidate>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var kv in blockById)
            {
                string blockId = kv.Key;
                var block = kv.Value;
                if (block == null) continue;

                AddCandidateFromObject(block["sheet"] as JObject, blockId, "sheet", result, seen);

                // 兼容部分结构：sheet 信息可能直接平铺在 block 顶层。
                string directName = (string)(block["sheet_name"] ?? block["name"] ?? block["title"]);
                string directToken = (string)(block["sheet_token"] ?? block["sheetToken"] ?? block["spreadsheet_token"] ?? block["spreadsheetToken"]);
                AddCandidate(blockId, "sheet", directName, directToken, result, seen);

                // 从链接里提取 spreadsheet_token（最关键兜底）。
                string[] urlCandidates =
                {
                    (string)block["url"],
                    (string)block["link"],
                    (string)(block["sheet"]?["url"]),
                    (string)(block["sheet"]?["link"]),
                    (string)(block["sheet"]?["source_url"]),
                    (string)(block["sheet"]?["sourceUrl"])
                };

                for (int i = 0; i < urlCandidates.Length; i++)
                    AddCandidateFromSheetUrl(urlCandidates[i], blockId, directName, result, seen);

                // 递归扫描 sheet 元数据中的所有字符串，进一步提取 /sheets/{token}
                if (block["sheet"] != null)
                {
                    var texts = new List<string>();
                    CollectStringValues(block["sheet"], texts, 0, 4, 128);
                    for (int i = 0; i < texts.Count; i++)
                        AddCandidateFromSheetUrl(texts[i], blockId, directName, result, seen);
                }
            }

            if (!string.IsNullOrWhiteSpace(preferredNameKeyword))
            {
                string keyword = preferredNameKeyword.Trim();
                result = result
                    .OrderByDescending(x => ContainsIgnoreCase(x.name, keyword))
                    .ThenBy(x => x.name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            return result;
        }

        private static void AddCandidateFromSheetUrl(
            string urlText,
            string blockId,
            string fallbackName,
            List<EmbeddedFileCandidate> result,
            HashSet<string> seen)
        {
            if (string.IsNullOrWhiteSpace(urlText)) return;

            // 常见格式: https://xxx.feishu.cn/sheets/{spreadsheet_token}?sheet={sheetId}
            var match = Regex.Match(urlText, @"/sheets/([A-Za-z0-9_-]+)", RegexOptions.IgnoreCase);
            if (!match.Success) return;

            string spreadsheetToken = match.Groups[1].Value;
            if (string.IsNullOrWhiteSpace(spreadsheetToken)) return;
            AddCandidate(blockId, "sheet", fallbackName, spreadsheetToken, result, seen);
        }

        private static void CollectStringValues(
            JToken token,
            List<string> values,
            int depth,
            int maxDepth,
            int maxCount)
        {
            if (token == null || values == null) return;
            if (depth > maxDepth || values.Count >= maxCount) return;

            if (token.Type == JTokenType.String)
            {
                values.Add(token.Value<string>() ?? string.Empty);
                return;
            }

            if (token.Type == JTokenType.Object || token.Type == JTokenType.Array)
            {
                foreach (var child in token.Children())
                {
                    CollectStringValues(child, values, depth + 1, maxDepth, maxCount);
                    if (values.Count >= maxCount) break;
                }
            }
        }

        private static void AddCandidateFromObject(
            JObject obj,
            string blockId,
            string sourceTag,
            List<EmbeddedFileCandidate> result,
            HashSet<string> seen)
        {
            if (obj == null) return;

            string name = (string)(obj["name"] ?? obj["file_name"] ?? obj["fileName"] ?? obj["title"]);
            string token = (string)(obj["spreadsheet_token"] ?? obj["spreadsheetToken"] ?? obj["sheet_token"] ?? obj["sheetToken"] ?? obj["token"] ?? obj["file_token"] ?? obj["fileToken"] ?? obj["media_token"]);
            AddCandidate(blockId, sourceTag, name, token, result, seen);
        }

        private static void AddCandidate(
            string blockId,
            string sourceTag,
            string name,
            string token,
            List<EmbeddedFileCandidate> result,
            HashSet<string> seen)
        {
            if (string.IsNullOrWhiteSpace(token)) return;
            if (!seen.Add(token)) return;

            bool isXlsxByName = !string.IsNullOrWhiteSpace(name)
                                && name.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase);

            if (!isXlsxByName
                && !string.Equals(sourceTag, "sheet", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            result.Add(new EmbeddedFileCandidate
            {
                sourceBlockId = blockId,
                token = token,
                name = string.IsNullOrWhiteSpace(name) ? "(unknown)" : name
            });
        }

        private static bool TryDownloadFileBytesByToken(
            string fileToken,
            string accessToken,
            out byte[] data,
            out string error)
        {
            data = null;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(fileToken))
            {
                error = "file token 为空";
                return false;
            }

            string[] urls =
            {
                $"https://open.feishu.cn/open-apis/drive/v1/files/{Uri.EscapeDataString(fileToken)}/download",
                $"https://open.feishu.cn/open-apis/drive/v1/medias/{Uri.EscapeDataString(fileToken)}/download"
            };

            var errors = new List<string>();
            for (int i = 0; i < urls.Length; i++)
            {
                if (TrySendBinaryRequest(urls[i], accessToken, out data, out var reqError))
                    return true;

                errors.Add(reqError);
            }

            error = string.Join(" | ", errors.Where(x => !string.IsNullOrWhiteSpace(x)));
            return false;
        }

        private static bool IsXlsxBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 4) return false;

            try
            {
                using (var ms = new MemoryStream(bytes, false))
                using (var zip = new ZipArchive(ms, ZipArchiveMode.Read))
                {
                    return zip.GetEntry("xl/workbook.xml") != null;
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool TryExtractTableRows(
            Dictionary<string, JObject> blockById,
            string tableBlockId,
            out List<List<string>> rows,
            out string error)
        {
            rows = new List<List<string>>();
            error = string.Empty;

            if (!blockById.TryGetValue(tableBlockId, out var tableBlock))
            {
                error = $"未找到表格 block: {tableBlockId}";
                return false;
            }

            if (TryExtractRowsFromTableCells(tableBlock, blockById, out rows) && rows.Count > 0)
            {
                NormalizeRows(rows);
                return true;
            }

            if (TryExtractRowsFromRowChildren(tableBlock, blockById, out rows) && rows.Count > 0)
            {
                NormalizeRows(rows);
                return true;
            }

            error = $"无法识别表格结构（未找到 table.cells 或 row-children 结构）。tableBlock={DescribeTableBlockStructure(tableBlock)}";
            return false;
        }

        private static string DescribeTableBlockStructure(JObject tableBlock)
        {
            if (tableBlock == null) return "null";

            var topKeys = tableBlock.Properties().Select(p => p.Name).Take(12);
            int childrenCount = GetChildrenIds(tableBlock).Count;

            var tableMeta = tableBlock["table"] as JObject;
            if (tableMeta == null)
                return $"topKeys=[{string.Join("|", topKeys)}], children={childrenCount}, tableMeta=null";

            var tableKeys = tableMeta.Properties().Select(p => p.Name).Take(12);
            var cellsToken = tableMeta["cells"];
            string cellsType = cellsToken == null ? "null" : cellsToken.Type.ToString();
            int cellsArrayCount = cellsToken is JArray a ? a.Count : -1;
            int rowSize = GetIntValue(tableMeta, "row_size", "rows_size", "row_count", "rowCount", "rows");
            int colSize = GetIntValue(tableMeta, "column_size", "columns_size", "column_count", "columnCount", "cols", "columns");

            return
                $"topKeys=[{string.Join("|", topKeys)}], children={childrenCount}, tableKeys=[{string.Join("|", tableKeys)}], cellsType={cellsType}, cellsCount={cellsArrayCount}, row={rowSize}, col={colSize}";
        }

        private static bool TryExtractRowsFromTableCells(
            JObject tableBlock,
            Dictionary<string, JObject> blockById,
            out List<List<string>> rows)
        {
            rows = new List<List<string>>();
            var tableMeta = tableBlock["table"] as JObject;
            if (tableMeta == null) return false;

            var cellsToken = tableMeta["cells"];
            if (cellsToken == null) return false;

            if (cellsToken is JArray cellsRows && cellsRows.Count > 0 && cellsRows[0] is JArray)
            {
                foreach (var rowToken in cellsRows)
                {
                    var rowList = new List<string>();
                    if (rowToken is JArray rowArray)
                    {
                        foreach (var cellToken in rowArray)
                            rowList.Add(ExtractCellText(cellToken, blockById));
                    }

                    if (rowList.Count > 0)
                        rows.Add(rowList);
                }

                return rows.Count > 0;
            }

            var flatCellIds = ExtractCellIdList(cellsToken);
            if (flatCellIds.Count == 0) return false;

            int rowCount =
                GetIntValue(tableMeta, "row_size", "rows_size", "row_count", "rowCount", "rows");
            int columnCount =
                GetIntValue(tableMeta, "column_size", "columns_size", "column_count", "columnCount", "cols", "columns");

            var tableProperty = tableMeta["property"] as JObject;
            if (rowCount <= 0 && tableProperty != null)
                rowCount = GetIntValue(tableProperty, "row_size", "row_count", "rows");
            if (columnCount <= 0 && tableProperty != null)
                columnCount = GetIntValue(tableProperty, "column_size", "column_count", "cols", "columns");

            if (rowCount <= 0 && columnCount > 0)
                rowCount = Mathf.CeilToInt(flatCellIds.Count / (float)columnCount);
            if (columnCount <= 0 && rowCount > 0)
                columnCount = Mathf.CeilToInt(flatCellIds.Count / (float)rowCount);

            if (rowCount <= 0 || columnCount <= 0)
                return false;

            int cursor = 0;
            for (int r = 0; r < rowCount; r++)
            {
                var row = new List<string>();
                for (int c = 0; c < columnCount; c++)
                {
                    if (cursor < flatCellIds.Count)
                    {
                        row.Add(ExtractCellText(flatCellIds[cursor], blockById));
                        cursor++;
                    }
                    else
                    {
                        row.Add(string.Empty);
                    }
                }
                rows.Add(row);
            }

            return rows.Count > 0;
        }

        private static bool TryExtractRowsFromRowChildren(
            JObject tableBlock,
            Dictionary<string, JObject> blockById,
            out List<List<string>> rows)
        {
            rows = new List<List<string>>();
            var rowIds = GetChildrenIds(tableBlock);
            if (rowIds.Count == 0) return false;

            foreach (var rowId in rowIds)
            {
                if (!blockById.TryGetValue(rowId, out var rowBlock))
                    continue;

                var cellIds = GetChildrenIds(rowBlock);
                if (cellIds.Count == 0)
                {
                    var tableRow = rowBlock["table_row"] as JObject;
                    if (tableRow != null)
                        cellIds = ExtractCellIdList(tableRow["cells"]);
                }

                if (cellIds.Count == 0)
                    continue;

                var row = new List<string>(cellIds.Count);
                foreach (var cellId in cellIds)
                    row.Add(ExtractCellText(cellId, blockById));

                rows.Add(row);
            }

            return rows.Count > 0;
        }

        private static string ExtractCellText(JToken cellToken, Dictionary<string, JObject> blockById)
        {
            if (cellToken == null) return string.Empty;

            string cellId;
            if (cellToken.Type == JTokenType.String)
            {
                cellId = cellToken.Value<string>();
            }
            else if (cellToken.Type == JTokenType.Object)
            {
                cellId = (string)(cellToken["block_id"] ?? cellToken["blockId"] ?? cellToken["id"]);
            }
            else
            {
                cellId = string.Empty;
            }

            if (string.IsNullOrWhiteSpace(cellId))
                return CollectInlineText(cellToken).Trim();

            return ExtractBlockTextRecursive(cellId, blockById, new HashSet<string>(StringComparer.OrdinalIgnoreCase), 0)
                .Trim();
        }

        private static string ExtractBlockTextRecursive(
            string blockId,
            Dictionary<string, JObject> blockById,
            HashSet<string> visiting,
            int depth)
        {
            if (string.IsNullOrWhiteSpace(blockId))
                return string.Empty;
            if (depth > 32)
                return string.Empty;
            if (!visiting.Add(blockId))
                return string.Empty;
            if (!blockById.TryGetValue(blockId, out var block))
                return string.Empty;

            var chunks = new List<string>();
            string direct = CollectInlineText(block).Trim();
            if (!string.IsNullOrWhiteSpace(direct))
                chunks.Add(direct);

            var children = GetChildrenIds(block);
            foreach (var childId in children)
            {
                string child = ExtractBlockTextRecursive(childId, blockById, visiting, depth + 1).Trim();
                if (!string.IsNullOrWhiteSpace(child))
                    chunks.Add(child);
            }

            return string.Join("\n", chunks.Where(x => !string.IsNullOrWhiteSpace(x)));
        }

        private static string CollectInlineText(JToken token)
        {
            var parts = new List<string>();
            CollectInlineText(token, parts);
            return string.Concat(parts);
        }

        private static void CollectInlineText(JToken token, List<string> parts)
        {
            if (token == null) return;

            if (token.Type == JTokenType.Object)
            {
                var obj = (JObject)token;
                foreach (var prop in obj.Properties())
                {
                    if (prop.Value == null) continue;

                    if ((prop.Name.Equals("content", StringComparison.OrdinalIgnoreCase)
                         || prop.Name.Equals("text", StringComparison.OrdinalIgnoreCase)
                         || prop.Name.Equals("title", StringComparison.OrdinalIgnoreCase))
                        && prop.Value.Type == JTokenType.String)
                    {
                        parts.Add(prop.Value.Value<string>() ?? string.Empty);
                        continue;
                    }

                    if (prop.Name.Equals("children", StringComparison.OrdinalIgnoreCase))
                        continue;

                    CollectInlineText(prop.Value, parts);
                }
            }
            else if (token.Type == JTokenType.Array)
            {
                foreach (var child in token.Children())
                    CollectInlineText(child, parts);
            }
        }

        private static List<string> GetChildrenIds(JObject block)
        {
            var list = new List<string>();
            if (block == null) return list;

            if (block["children"] is JArray children)
            {
                foreach (var child in children)
                {
                    if (child.Type == JTokenType.String)
                        list.Add(child.Value<string>());
                    else if (child.Type == JTokenType.Object)
                    {
                        string id = (string)(child["block_id"] ?? child["blockId"] ?? child["id"]);
                        if (!string.IsNullOrWhiteSpace(id))
                            list.Add(id);
                    }
                }
            }

            return list;
        }

        private static List<string> ExtractCellIdList(JToken cellsToken)
        {
            var result = new List<string>();
            if (cellsToken == null) return result;

            if (cellsToken.Type == JTokenType.Array)
            {
                foreach (var t in cellsToken.Children())
                    result.AddRange(ExtractCellIdList(t));
                return result;
            }

            if (cellsToken.Type == JTokenType.String)
            {
                string id = cellsToken.Value<string>();
                if (!string.IsNullOrWhiteSpace(id))
                    result.Add(id);
                return result;
            }

            if (cellsToken.Type == JTokenType.Object)
            {
                string id = (string)(cellsToken["block_id"] ?? cellsToken["blockId"] ?? cellsToken["id"]);
                if (!string.IsNullOrWhiteSpace(id))
                    result.Add(id);
            }

            return result;
        }

        private static void NormalizeRows(List<List<string>> rows)
        {
            int maxCols = rows.Max(r => r?.Count ?? 0);
            if (maxCols <= 0) return;

            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i] == null)
                    rows[i] = new List<string>();
                while (rows[i].Count < maxCols)
                    rows[i].Add(string.Empty);
            }
        }

        private static JObject NormalizeBlockObject(JToken item)
        {
            var obj = item as JObject;
            if (obj == null) return null;

            if (obj["block"] is JObject nestedBlock)
                return nestedBlock;

            if (obj["data"]?["block"] is JObject deepBlock)
                return deepBlock;

            return obj;
        }

        private static string GetBlockId(JObject block)
        {
            return (string)(block["block_id"] ?? block["blockId"] ?? block["id"]);
        }

        private static bool IsTableBlock(JObject block)
        {
            if (block == null) return false;
            if (block["table"] != null) return true;

            var blockTypeToken = block["block_type"] ?? block["blockType"];
            if (blockTypeToken == null) return false;

            if (blockTypeToken.Type == JTokenType.Integer)
            {
                // 仅保留明确 table 类型，避免把 image 等类型误判为 table。
                int v = blockTypeToken.Value<int>();
                return v == 31;
            }

            if (blockTypeToken.Type == JTokenType.String)
            {
                string name = blockTypeToken.Value<string>();
                if (string.IsNullOrWhiteSpace(name)) return false;
                return string.Equals(name, "table", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(name, "table_block", StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        private static string GetParentId(JObject block)
        {
            if (block == null) return string.Empty;
            var direct = (string)(block["parent_id"] ?? block["parentId"]);
            if (!string.IsNullOrWhiteSpace(direct)) return direct;

            var parent = block["parent"] as JObject;
            if (parent != null)
                return (string)(parent["id"] ?? parent["block_id"] ?? string.Empty);

            return string.Empty;
        }

        private static string DescribeBlockType(JObject block)
        {
            if (block == null) return "unknown";
            if (block["table"] != null) return "table(meta)";
            if (block["table_row"] != null) return "table_row(meta)";
            if (block["sheet"] != null) return "sheet(meta)";
            if (block["bitable"] != null) return "bitable(meta)";

            var blockTypeToken = block["block_type"] ?? block["blockType"];
            if (blockTypeToken == null) return "unknown";
            if (blockTypeToken.Type == JTokenType.Integer)
                return "type_" + blockTypeToken.Value<int>();
            if (blockTypeToken.Type == JTokenType.String)
            {
                string text = blockTypeToken.Value<string>();
                return string.IsNullOrWhiteSpace(text) ? "unknown" : text;
            }

            return "unknown";
        }

        private static string BuildBlockTypeSummary(Dictionary<string, JObject> blockById, int maxItems)
        {
            if (blockById == null || blockById.Count == 0) return "empty";

            var counter = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in blockById)
            {
                string typeName = DescribeBlockType(kv.Value);
                if (!counter.ContainsKey(typeName))
                    counter[typeName] = 0;
                counter[typeName]++;
            }

            var parts = counter
                .OrderByDescending(x => x.Value)
                .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .Take(Mathf.Max(1, maxItems))
                .Select(x => $"{x.Key}:{x.Value}");

            return string.Join(", ", parts);
        }

        private static int GetIntValue(JObject obj, params string[] keys)
        {
            if (obj == null || keys == null || keys.Length == 0) return 0;

            foreach (var key in keys)
            {
                if (obj.TryGetValue(key, StringComparison.OrdinalIgnoreCase, out var token)
                    && token != null
                    && token.Type != JTokenType.Null)
                {
                    if (token.Type == JTokenType.Integer)
                        return token.Value<int>();
                    if (token.Type == JTokenType.String
                        && int.TryParse(token.Value<string>(), out var value))
                        return value;
                }
            }

            return 0;
        }

        private static void WriteRowsToXlsx(string outputXlsxPath, List<List<string>> rows)
        {
            if (File.Exists(outputXlsxPath))
                File.Delete(outputXlsxPath);

            using (var stream = new FileStream(outputXlsxPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                WriteZipEntry(
                    zip,
                    "[Content_Types].xml",
                    @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Types xmlns=""http://schemas.openxmlformats.org/package/2006/content-types"">
  <Default Extension=""rels"" ContentType=""application/vnd.openxmlformats-package.relationships+xml""/>
  <Default Extension=""xml"" ContentType=""application/xml""/>
  <Override PartName=""/xl/workbook.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml""/>
  <Override PartName=""/xl/worksheets/sheet1.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml""/>
</Types>");

                WriteZipEntry(
                    zip,
                    "_rels/.rels",
                    @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships"">
  <Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"" Target=""xl/workbook.xml""/>
</Relationships>");

                WriteZipEntry(
                    zip,
                    "xl/workbook.xml",
                    @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<workbook xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main"" xmlns:r=""http://schemas.openxmlformats.org/officeDocument/2006/relationships"">
  <sheets>
    <sheet name=""Sheet1"" sheetId=""1"" r:id=""rId1""/>
  </sheets>
</workbook>");

                WriteZipEntry(
                    zip,
                    "xl/_rels/workbook.xml.rels",
                    @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships"">
  <Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"" Target=""worksheets/sheet1.xml""/>
</Relationships>");

                WriteZipEntry(zip, "xl/worksheets/sheet1.xml", BuildSheetXml(rows));
            }
        }

        private static string BuildSheetXml(List<List<string>> rows)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
            sb.Append("<sheetData>");

            for (int r = 0; r < rows.Count; r++)
            {
                int rowIndex = r + 1;
                sb.Append("<row r=\"").Append(rowIndex).Append("\">");

                var row = rows[r];
                if (row != null)
                {
                    for (int c = 0; c < row.Count; c++)
                    {
                        string cellRef = ColumnNameFromIndex(c + 1) + rowIndex;
                        string value = EscapeXml(row[c] ?? string.Empty);

                        sb.Append("<c r=\"").Append(cellRef).Append("\" t=\"inlineStr\">");
                        sb.Append("<is><t xml:space=\"preserve\">").Append(value).Append("</t></is>");
                        sb.Append("</c>");
                    }
                }

                sb.Append("</row>");
            }

            sb.Append("</sheetData></worksheet>");
            return sb.ToString();
        }

        private static string ColumnNameFromIndex(int columnIndex)
        {
            if (columnIndex <= 0) return "A";

            var chars = new List<char>(4);
            int value = columnIndex;
            while (value > 0)
            {
                value--;
                chars.Add((char)('A' + (value % 26)));
                value /= 26;
            }

            chars.Reverse();
            return new string(chars.ToArray());
        }

        private static string EscapeXml(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            return value
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&apos;");
        }

        private static void WriteZipEntry(ZipArchive zip, string entryPath, string content)
        {
            var entry = zip.CreateEntry(entryPath);
            using (var stream = entry.Open())
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(content);
            }
        }

        private static bool TrySendBinaryRequest(
            string url,
            string bearerToken,
            out byte[] data,
            out string error)
        {
            data = null;
            error = string.Empty;

            using (var request = UnityWebRequest.Get(url))
            {
                if (!string.IsNullOrWhiteSpace(bearerToken))
                    request.SetRequestHeader("Authorization", $"Bearer {bearerToken}");

                var op = request.SendWebRequest();
                while (!op.isDone)
                {
                }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    string status = request.responseCode > 0 ? $"HTTP {request.responseCode}" : "网络错误";
                    error = $"请求失败：{status}，URL={url}，message={request.error}";
                    return false;
                }

                data = request.downloadHandler?.data;
                if (data == null || data.Length == 0)
                {
                    error = $"下载内容为空：URL={url}";
                    return false;
                }

                string contentType = request.GetResponseHeader("Content-Type");
                bool maybeJson = !string.IsNullOrWhiteSpace(contentType)
                                 && contentType.IndexOf("application/json", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!maybeJson && data.Length > 0 && data[0] == '{')
                    maybeJson = true;

                if (!maybeJson)
                    return true;

                try
                {
                    string text = Encoding.UTF8.GetString(data);
                    var root = JObject.Parse(text);
                    if (!TryEnsureApiSuccess(root, out var apiError))
                    {
                        error = $"下载接口返回错误：{apiError}";
                        data = null;
                        return false;
                    }
                }
                catch
                {
                    // JSON 解析失败时按二进制内容处理
                    return true;
                }

                return true;
            }
        }

        private static bool TrySendRequest(
            string url,
            string method,
            string jsonBody,
            string bearerToken,
            out JObject response,
            out string error)
        {
            response = null;
            error = string.Empty;

            using (var request = new UnityWebRequest(url, method)
            {
                downloadHandler = new DownloadHandlerBuffer()
            })
            {
                if (!string.IsNullOrWhiteSpace(jsonBody))
                {
                    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
                    request.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
                }

                if (!string.IsNullOrWhiteSpace(bearerToken))
                    request.SetRequestHeader("Authorization", $"Bearer {bearerToken}");

                var op = request.SendWebRequest();
                while (!op.isDone)
                {
                }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    string status = request.responseCode > 0 ? $"HTTP {request.responseCode}" : "网络错误";
                    error = $"请求失败：{status}，URL={url}，message={request.error}";
                    return false;
                }

                string text = request.downloadHandler?.text;
                if (string.IsNullOrWhiteSpace(text))
                {
                    error = $"响应为空：URL={url}";
                    return false;
                }

                try
                {
                    response = JObject.Parse(text);
                    return true;
                }
                catch (Exception ex)
                {
                    error = $"解析 JSON 失败：{ex.Message}，URL={url}";
                    return false;
                }
            }

        }

        private static bool TryEnsureApiSuccess(JObject root, out string error)
        {
            error = string.Empty;
            if (root == null)
            {
                error = "OpenAPI 响应为空。";
                return false;
            }

            int code = root["code"]?.Value<int>() ?? -1;
            if (code == 0)
                return true;

            string msg = (string)(root["msg"] ?? root["message"] ?? "unknown");
            error = $"OpenAPI 返回错误：code={code}, msg={msg}";
            return false;
        }

        private static bool TryExtractDocToken(string docUrl, out DocTokenType tokenType, out string token)
        {
            tokenType = DocTokenType.Unknown;
            token = string.Empty;
            if (string.IsNullOrWhiteSpace(docUrl))
                return false;

            var wikiMatch = Regex.Match(docUrl, @"/wiki/([A-Za-z0-9]+)", RegexOptions.IgnoreCase);
            if (wikiMatch.Success)
            {
                tokenType = DocTokenType.Wiki;
                token = wikiMatch.Groups[1].Value;
                return true;
            }

            var docxMatch = Regex.Match(docUrl, @"/docx/([A-Za-z0-9]+)", RegexOptions.IgnoreCase);
            if (docxMatch.Success)
            {
                tokenType = DocTokenType.Docx;
                token = docxMatch.Groups[1].Value;
                return true;
            }

            return false;
        }

        private static bool TryGetProjectRoot(out string projectRoot)
        {
            projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            return !string.IsNullOrWhiteSpace(projectRoot);
        }

        private static bool TryResolveOutputPath(
            string projectRoot,
            string outputXlsxRelativePath,
            out string outputXlsxPath,
            out string error)
        {
            outputXlsxPath = string.Empty;
            error = string.Empty;

            string relative = string.IsNullOrWhiteSpace(outputXlsxRelativePath)
                ? DefaultOutputXlsxRelativePath
                : outputXlsxRelativePath.Trim();

            if (Path.IsPathRooted(relative))
            {
                outputXlsxPath = relative;
            }
            else
            {
                outputXlsxPath = Path.GetFullPath(Path.Combine(projectRoot, relative));
            }

            if (!outputXlsxPath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
            {
                error = "输出路径必须位于项目目录内。";
                return false;
            }

            if (!string.Equals(Path.GetExtension(outputXlsxPath), ".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                error = "输出路径必须是 .xlsx 文件。";
                return false;
            }

            return true;
        }

        private static FeishuOpenApiSettings LoadSettings()
        {
            var settings = new FeishuOpenApiSettings
            {
                appId = EditorPrefs.GetString(EditorKeyAppId, string.Empty),
                appSecret = EditorPrefs.GetString(EditorKeyAppSecret, string.Empty),
                docUrl = EditorPrefs.GetString(EditorKeyDocUrl, DefaultDocUrl),
                tableIndex = Mathf.Max(0, EditorPrefs.GetInt(EditorKeyTableIndex, 0)),
                tableNameKeyword = EditorPrefs.GetString(EditorKeyTableNameKeyword, string.Empty),
                spreadsheetLinkOrToken = EditorPrefs.GetString(EditorKeySpreadsheetLinkOrToken, string.Empty),
                outputXlsxRelativePath = EditorPrefs.GetString(EditorKeyOutputXlsx, DefaultOutputXlsxRelativePath)
            };

            ApplyLocalConfigOverrides(settings);
            return settings;
        }

        private static void ApplyLocalConfigOverrides(FeishuOpenApiSettings settings)
        {
            if (settings == null) return;
            if (!TryGetProjectRoot(out var projectRoot)) return;

            string localConfigPath = Path.Combine(projectRoot, LocalConfigRelativePath);
            if (!File.Exists(localConfigPath)) return;

            try
            {
                var text = File.ReadAllText(localConfigPath, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(text)) return;

                var root = JObject.Parse(text);
                string appId = (string)root["appId"];
                string appSecret = (string)root["appSecret"];
                string docUrl = (string)root["docUrl"];
                string tableNameKeyword = (string)root["tableNameKeyword"];
                string spreadsheetLinkOrToken = (string)root["spreadsheetLinkOrToken"];
                string outputXlsxRelativePath = (string)root["outputXlsxRelativePath"];

                if (!string.IsNullOrWhiteSpace(appId))
                    settings.appId = appId;
                if (!string.IsNullOrWhiteSpace(appSecret))
                    settings.appSecret = appSecret;
                if (!string.IsNullOrWhiteSpace(docUrl))
                    settings.docUrl = docUrl;
                if (!string.IsNullOrWhiteSpace(tableNameKeyword))
                    settings.tableNameKeyword = tableNameKeyword;
                if (!string.IsNullOrWhiteSpace(spreadsheetLinkOrToken))
                    settings.spreadsheetLinkOrToken = spreadsheetLinkOrToken;
                if (!string.IsNullOrWhiteSpace(outputXlsxRelativePath))
                    settings.outputXlsxRelativePath = outputXlsxRelativePath;

                if (root["tableIndex"] != null)
                    settings.tableIndex = Mathf.Max(0, root["tableIndex"].Value<int>());
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FeishuDocxTableSync] 读取本地配置失败: {localConfigPath}, {ex.Message}");
            }
        }

        private static void SaveSettings(FeishuOpenApiSettings settings)
        {
            EditorPrefs.SetString(EditorKeyAppId, settings.appId ?? string.Empty);
            EditorPrefs.SetString(EditorKeyAppSecret, settings.appSecret ?? string.Empty);
            EditorPrefs.SetString(EditorKeyDocUrl, settings.docUrl ?? DefaultDocUrl);
            EditorPrefs.SetInt(EditorKeyTableIndex, Mathf.Max(0, settings.tableIndex));
            EditorPrefs.SetString(EditorKeyTableNameKeyword, settings.tableNameKeyword ?? string.Empty);
            EditorPrefs.SetString(EditorKeySpreadsheetLinkOrToken, settings.spreadsheetLinkOrToken ?? string.Empty);
            EditorPrefs.SetString(
                EditorKeyOutputXlsx,
                string.IsNullOrWhiteSpace(settings.outputXlsxRelativePath)
                    ? DefaultOutputXlsxRelativePath
                    : settings.outputXlsxRelativePath);
        }

        private enum DocTokenType
        {
            Unknown = 0,
            Wiki = 1,
            Docx = 2
        }

        private sealed class FeishuOpenApiSettings
        {
            public string appId;
            public string appSecret;
            public string docUrl;
            public int tableIndex;
            public string tableNameKeyword;
            public string spreadsheetLinkOrToken;
            public string outputXlsxRelativePath;
        }

        private sealed class EmbeddedFileCandidate
        {
            public string sourceBlockId;
            public string token;
            public string name;
        }

        private sealed class FeishuOpenApiSettingsWindow : EditorWindow
        {
            private FeishuOpenApiSettings m_Settings;

            internal static void Open()
            {
                var window = GetWindow<FeishuOpenApiSettingsWindow>("飞书OpenAPI配置");
                window.minSize = new Vector2(560f, 280f);
                window.Load();
                window.Show();
            }

            private void Load()
            {
                m_Settings = LoadSettings();
            }

            private void OnGUI()
            {
                if (m_Settings == null)
                    Load();

                EditorGUILayout.HelpBox(
                    "填写飞书自建应用的 app_id / app_secret，并提供 wiki 或 docx 链接。同步时会把指定表格导出为 XLSX。",
                    MessageType.Info);

                EditorGUILayout.Space(6f);
                m_Settings.appId = EditorGUILayout.TextField("App ID", m_Settings.appId ?? string.Empty);
                m_Settings.appSecret = EditorGUILayout.PasswordField("App Secret", m_Settings.appSecret ?? string.Empty);
                m_Settings.docUrl = EditorGUILayout.TextField("文档链接", m_Settings.docUrl ?? string.Empty);
                m_Settings.tableIndex = Mathf.Max(0, EditorGUILayout.IntField("表格索引", m_Settings.tableIndex));
                m_Settings.tableNameKeyword =
                    EditorGUILayout.TextField("表格名关键词(可选)", m_Settings.tableNameKeyword ?? string.Empty);
                m_Settings.spreadsheetLinkOrToken =
                    EditorGUILayout.TextField("电子表格链接/Token(可选)", m_Settings.spreadsheetLinkOrToken ?? string.Empty);
                m_Settings.outputXlsxRelativePath =
                    EditorGUILayout.TextField("XLSX输出路径", m_Settings.outputXlsxRelativePath ?? string.Empty);

                EditorGUILayout.Space(10f);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("恢复默认文档链接", GUILayout.Height(24f)))
                    {
                        m_Settings.docUrl = DefaultDocUrl;
                    }

                    if (GUILayout.Button("保存配置", GUILayout.Height(24f)))
                    {
                        SaveSettings(m_Settings);
                        ShowNotification(new GUIContent("已保存"));
                    }

                    if (GUILayout.Button("保存并同步", GUILayout.Height(24f)))
                    {
                        SaveSettings(m_Settings);
                        if (TrySyncInteractionTableToXlsx(out var outputXlsxPath, out var error))
                        {
                            Debug.Log($"[FeishuDocxTableSync] 同步成功：{outputXlsxPath}");
                            ShowNotification(new GUIContent("同步成功"));
                        }
                        else
                        {
                            Debug.LogError($"[FeishuDocxTableSync] 同步失败: {error}");
                            ShowNotification(new GUIContent("同步失败，见 Console"));
                        }
                    }
                }
            }
        }
    }
}
