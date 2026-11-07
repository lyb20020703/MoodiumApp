using System.Collections.Concurrent;
using UnityEngine;

namespace VFXViewer
{
    public sealed partial class SpectatorHostService
    {
        private sealed class PendingClientCommand
        {
            public ClientConnection connection;
            public SpectatorCommandMessage command;
        }

        private readonly ConcurrentQueue<PendingClientCommand> m_PendingClientCommands =
            new ConcurrentQueue<PendingClientCommand>();

        private void EnqueueClientCommand(ClientConnection connection, SpectatorCommandMessage command)
        {
            if (connection == null || command == null)
                return;

            m_PendingClientCommands.Enqueue(new PendingClientCommand
            {
                connection = connection,
                command = command,
            });
        }

        private void ProcessPendingClientCommands()
        {
            while (m_PendingClientCommands.TryDequeue(out PendingClientCommand pending))
            {
                if (pending == null || pending.connection == null || pending.command == null)
                    continue;

                HandlePendingClientCommand(pending);
            }
        }

        private void HandlePendingClientCommand(PendingClientCommand pending)
        {
            SpectatorCommandKind commandKind = (SpectatorCommandKind)pending.command.commandKind;
                switch (commandKind)
                {
                    case SpectatorCommandKind.ImportLayoutJson:
                        HandleImportLayoutJsonCommand(pending.connection, pending.command);
                        break;

                    case SpectatorCommandKind.ExportLayoutJson:
                        HandleExportLayoutJsonCommand(pending.connection);
                        break;

                    default:
                        SendCommandResult(
                            pending.connection,
                        commandKind,
                        success: false,
                        $"未支持的观演端命令：{commandKind}");
                    break;
            }
        }

        private void HandleImportLayoutJsonCommand(ClientConnection connection, SpectatorCommandMessage command)
        {
            ResolveReferences();
            if (m_PlacementManager == null)
            {
                SendCommandResult(connection, SpectatorCommandKind.ImportLayoutJson, success: false, "Vision 端未找到 ExhibitPlacementManager。");
                return;
            }

            SpectatorLayoutImportRequest request =
                SpectatorProtocol.DeserializePayload<SpectatorLayoutImportRequest>(command.payloadJson);
            if (request == null || string.IsNullOrWhiteSpace(request.layoutJson))
            {
                SendCommandResult(connection, SpectatorCommandKind.ImportLayoutJson, success: false, "收到的布局文件内容为空。");
                return;
            }

            bool success = m_PlacementManager.TryImportExternalLayoutJson(
                request.layoutJson,
                request.replaceExistingScene,
                request.rebuildIfPossible,
                out string statusMessage);

            string fileName = string.IsNullOrWhiteSpace(request.fileName) ? "museum_layout.json" : request.fileName.Trim();
            PlacementDebugFileLogger.Log(
                $"[SpectatorLayout] import file='{fileName}', success={success}, message={statusMessage}");
            SendCommandResult(connection, SpectatorCommandKind.ImportLayoutJson, success, statusMessage);

            if (success)
                m_NextSnapshotAt = 0f;
        }

        private void HandleExportLayoutJsonCommand(ClientConnection connection)
        {
            ResolveReferences();
            if (m_PlacementManager == null)
            {
                SendCommandResult(connection, SpectatorCommandKind.ExportLayoutJson, success: false, "Vision 端未找到 ExhibitPlacementManager。");
                return;
            }

            bool success = m_PlacementManager.TryExportSavedLayoutJson(
                out string fileName,
                out string layoutJson,
                out string statusMessage);

            string payloadJson = string.Empty;
            if (success)
            {
                var response = new SpectatorLayoutExportResponse
                {
                    fileName = fileName,
                    layoutJson = layoutJson,
                };
                payloadJson = JsonUtility.ToJson(response);
            }

            PlacementDebugFileLogger.Log(
                $"[SpectatorLayout] export file='{fileName}', success={success}, message={statusMessage}");
            SendCommandResult(connection, SpectatorCommandKind.ExportLayoutJson, success, statusMessage, payloadJson);
        }

        private void SendCommandResult(
            ClientConnection connection,
            SpectatorCommandKind commandKind,
            bool success,
            string message,
            string payloadJson = "")
        {
            if (connection == null || connection.tcpClient == null || !connection.tcpClient.Connected)
                return;

            var result = new SpectatorCommandResult
            {
                commandKind = (int)commandKind,
                success = success,
                message = message ?? string.Empty,
                payloadJson = payloadJson ?? string.Empty,
            };

            _ = SpectatorTransport.WriteEnvelopeAsync(
                connection.tcpClient.GetStream(),
                SpectatorMessageKind.CommandResult,
                result,
                connection.cancellation.Token);
        }
    }
}
