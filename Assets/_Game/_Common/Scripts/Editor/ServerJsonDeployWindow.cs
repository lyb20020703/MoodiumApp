using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace VFXViewer.Editor
{
    public sealed class ServerJsonDeployWindow : EditorWindow
    {
        private const string MenuPath = "工具/部署/上传服务器 JSON";
        private const string Title = "Server JSON Deploy";
        private const string DefaultHost = "8.130.186.87";
        private const int DefaultPort = 22;
        private const string DefaultUser = "admin";
        private const string DefaultPrivateKeyPath = "/Users/caps/.ssh/id_ed25519";
        private const string DefaultRemoteTempPath = "/tmp/museum_layout.json";
        private const string DefaultRemoteTargetPath = "/var/www/layout/museum_layout.json";

        private const string HostKey = "VFXViewer.ServerJsonDeploy.Host";
        private const string PortKey = "VFXViewer.ServerJsonDeploy.Port";
        private const string UserKey = "VFXViewer.ServerJsonDeploy.User";
        private const string PrivateKeyPathKey = "VFXViewer.ServerJsonDeploy.PrivateKeyPath";
        private const string LocalJsonPathKey = "VFXViewer.ServerJsonDeploy.LocalJsonPath";
        private const string RemoteTempPathKey = "VFXViewer.ServerJsonDeploy.RemoteTempPath";
        private const string RemoteTargetPathKey = "VFXViewer.ServerJsonDeploy.RemoteTargetPath";
        private const string RestartCommandKey = "VFXViewer.ServerJsonDeploy.RestartCommand";
        private const string BackupEnabledKey = "VFXViewer.ServerJsonDeploy.BackupEnabled";
        private const string AcceptNewHostKey = "VFXViewer.ServerJsonDeploy.AcceptNewHostKey";

        private string host = DefaultHost;
        private int port = DefaultPort;
        private string user = DefaultUser;
        private string privateKeyPath = DefaultPrivateKeyPath;
        private string localJsonPath = string.Empty;
        private string remoteTempPath = DefaultRemoteTempPath;
        private string remoteTargetPath = DefaultRemoteTargetPath;
        private string restartCommand = string.Empty;
        private bool backupEnabled = true;
        private bool acceptNewHostKey = true;

        private Vector2 scrollPosition;
        private string log = string.Empty;
        private bool isBusy;

        [MenuItem(MenuPath, priority = 401)]
        private static void Open()
        {
            var window = GetWindow<ServerJsonDeployWindow>(Title);
            window.minSize = new Vector2(640f, 520f);
            window.Show();
        }

        private void OnEnable()
        {
            host = EditorPrefs.GetString(HostKey, DefaultHost);
            port = EditorPrefs.GetInt(PortKey, DefaultPort);
            user = EditorPrefs.GetString(UserKey, DefaultUser);
            privateKeyPath = EditorPrefs.GetString(PrivateKeyPathKey, DefaultPrivateKeyPath);
            localJsonPath = EditorPrefs.GetString(LocalJsonPathKey, localJsonPath);
            remoteTempPath = EditorPrefs.GetString(RemoteTempPathKey, DefaultRemoteTempPath);
            remoteTargetPath = EditorPrefs.GetString(RemoteTargetPathKey, DefaultRemoteTargetPath);
            restartCommand = EditorPrefs.GetString(RestartCommandKey, restartCommand);
            backupEnabled = EditorPrefs.GetBool(BackupEnabledKey, backupEnabled);
            acceptNewHostKey = EditorPrefs.GetBool(AcceptNewHostKey, acceptNewHostKey);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("通过 SFTP 上传并用 SSH 覆盖服务器 JSON", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "建议本机先配置好 ssh key。工具会先把文件上传到临时路径，再通过 ssh 执行 sudo cp 覆盖目标文件。",
                MessageType.Info);

            using (new EditorGUI.DisabledScope(isBusy))
            {
                DrawConnectionSection();
                EditorGUILayout.Space(10f);
                DrawPathSection();
                EditorGUILayout.Space(10f);
                DrawOptionsSection();
                EditorGUILayout.Space(12f);

                if (GUILayout.Button("上传并替换", GUILayout.Height(34f)))
                {
                    Deploy();
                }
            }

            EditorGUILayout.Space(12f);
            EditorGUILayout.LabelField("日志", EditorStyles.boldLabel);
            using (var scroll = new EditorGUILayout.ScrollViewScope(scrollPosition, GUILayout.ExpandHeight(true)))
            {
                scrollPosition = scroll.scrollPosition;
                EditorGUILayout.TextArea(log, GUILayout.ExpandHeight(true));
            }
        }

        private void DrawConnectionSection()
        {
            EditorGUILayout.LabelField("连接信息", EditorStyles.boldLabel);
            host = EditorGUILayout.TextField("Host", host);
            port = EditorGUILayout.IntField("Port", port);
            user = EditorGUILayout.TextField("User", user);

            using (new EditorGUILayout.HorizontalScope())
            {
                privateKeyPath = EditorGUILayout.TextField("Private Key", privateKeyPath);
                if (GUILayout.Button("选择", GUILayout.Width(64f)))
                {
                    string selected = EditorUtility.OpenFilePanel("选择 SSH 私钥", GetInitialDirectory(privateKeyPath), string.Empty);
                    if (!string.IsNullOrWhiteSpace(selected))
                    {
                        privateKeyPath = selected;
                    }
                }
            }

            EditorGUILayout.HelpBox(
                $"默认服务器: {DefaultUser}@{DefaultHost}:{DefaultPort}\n默认私钥: {DefaultPrivateKeyPath}",
                MessageType.None);
        }

        private void DrawPathSection()
        {
            EditorGUILayout.LabelField("文件路径", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                localJsonPath = EditorGUILayout.TextField("Local JSON", localJsonPath);
                if (GUILayout.Button("选择", GUILayout.Width(64f)))
                {
                    string selected = EditorUtility.OpenFilePanel("选择本地 JSON", GetInitialDirectory(localJsonPath), "json");
                    if (!string.IsNullOrWhiteSpace(selected))
                    {
                        localJsonPath = selected;
                    }
                }
            }

            remoteTempPath = EditorGUILayout.TextField("Remote Temp", remoteTempPath);
            remoteTargetPath = EditorGUILayout.TextField("Remote Target", remoteTargetPath);
        }

        private void DrawOptionsSection()
        {
            EditorGUILayout.LabelField("选项", EditorStyles.boldLabel);
            backupEnabled = EditorGUILayout.ToggleLeft("覆盖前备份目标文件", backupEnabled);
            acceptNewHostKey = EditorGUILayout.ToggleLeft("首次连接自动接受 host key", acceptNewHostKey);
            restartCommand = EditorGUILayout.TextField("Restart Command", restartCommand);
        }

        private void Deploy()
        {
            if (!ValidateInputs(out string error))
            {
                EditorUtility.DisplayDialog("参数不完整", error, "知道了");
                return;
            }

            SavePrefs();
            isBusy = true;
            log = string.Empty;

            try
            {
                AppendLog("开始部署。");
                RunSftpUpload();
                RunRemoteReplace();
                AppendLog("部署完成。");
                EditorUtility.DisplayDialog("上传完成", "服务器 JSON 已替换完成。", "好");
            }
            catch (Exception ex)
            {
                AppendLog($"失败: {ex.Message}");
                UnityEngine.Debug.LogException(ex);
                EditorUtility.DisplayDialog("部署失败", ex.Message, "知道了");
            }
            finally
            {
                isBusy = false;
                Repaint();
            }
        }

        private void RunSftpUpload()
        {
            string remoteDirectory = PosixDirectoryName(remoteTempPath);
            string batchFile = Path.GetTempFileName();

            try
            {
                string normalizedLocalPath = localJsonPath.Replace("\"", "\\\"");
                string normalizedRemoteDirectory = remoteDirectory.Replace("\"", "\\\"");
                string normalizedRemoteTempPath = remoteTempPath.Replace("\"", "\\\"");

                File.WriteAllText(
                    batchFile,
                    $"-mkdir \"{normalizedRemoteDirectory}\"\nput \"{normalizedLocalPath}\" \"{normalizedRemoteTempPath}\"\n",
                    new UTF8Encoding(false));

                var arguments = BuildBaseCommandArguments(forSftp: true);
                arguments.Add("-b");
                arguments.Add(batchFile);
                arguments.Add($"{user}@{host}");

                RunCommand("sftp", arguments, "SFTP 上传");
            }
            finally
            {
                if (File.Exists(batchFile))
                {
                    File.Delete(batchFile);
                }
            }
        }

        private void RunRemoteReplace()
        {
            string backupCommand = backupEnabled
                ? $"if [ -f {ShellQuote(remoteTargetPath)} ]; then sudo cp {ShellQuote(remoteTargetPath)} {ShellQuote(remoteTargetPath + ".bak")}; fi"
                : string.Empty;

            string restart = string.IsNullOrWhiteSpace(restartCommand)
                ? string.Empty
                : restartCommand.Trim();

            var builder = new StringBuilder();
            builder.Append("set -e");
            builder.Append(" && ");
            builder.Append($"sudo mkdir -p {ShellQuote(PosixDirectoryName(remoteTargetPath))}");
            builder.Append(" && ");
            builder.Append($"sudo cp {ShellQuote(remoteTempPath)} {ShellQuote(remoteTargetPath)}");

            if (!string.IsNullOrWhiteSpace(backupCommand))
            {
                builder.Insert(0, backupCommand + " && ");
            }

            if (!string.IsNullOrWhiteSpace(restart))
            {
                builder.Append(" && ");
                builder.Append(restart);
            }

            var arguments = BuildBaseCommandArguments(forSftp: false);
            arguments.Add("-T");
            arguments.Add($"{user}@{host}");
            arguments.Add(builder.ToString());

            RunCommand("ssh", arguments, "SSH 覆盖");
        }

        private void RunCommand(string fileName, List<string> arguments, string stepName)
        {
            AppendLog($"[{stepName}] {fileName} {JoinArguments(arguments)}");

            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                throw new InvalidOperationException($"无法启动命令: {fileName}");
            }

            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (!string.IsNullOrWhiteSpace(stdout))
            {
                AppendLog(stdout.TrimEnd());
            }

            if (!string.IsNullOrWhiteSpace(stderr))
            {
                AppendLog(stderr.TrimEnd());
            }

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"{stepName} 失败，退出码 {process.ExitCode}。");
            }
        }

        private List<string> BuildBaseCommandArguments(bool forSftp)
        {
            var arguments = new List<string>
            {
                forSftp ? "-P" : "-p",
                port.ToString()
            };

            if (!string.IsNullOrWhiteSpace(privateKeyPath))
            {
                arguments.Add("-i");
                arguments.Add(privateKeyPath);
            }

            if (acceptNewHostKey)
            {
                arguments.Add("-o");
                arguments.Add("StrictHostKeyChecking=accept-new");
            }

            arguments.Add("-o");
            arguments.Add("BatchMode=yes");
            return arguments;
        }

        private bool ValidateInputs(out string error)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                error = "Host 不能为空。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(user))
            {
                error = "User 不能为空。";
                return false;
            }

            if (port <= 0)
            {
                error = "Port 必须大于 0。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(localJsonPath) || !File.Exists(localJsonPath))
            {
                error = "请先选择存在的本地 JSON 文件。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(remoteTempPath) || string.IsNullOrWhiteSpace(remoteTargetPath))
            {
                error = "Remote Temp 和 Remote Target 都不能为空。";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(privateKeyPath) && !File.Exists(privateKeyPath))
            {
                error = "SSH 私钥文件不存在。";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private void SavePrefs()
        {
            EditorPrefs.SetString(HostKey, host);
            EditorPrefs.SetInt(PortKey, port);
            EditorPrefs.SetString(UserKey, user);
            EditorPrefs.SetString(PrivateKeyPathKey, privateKeyPath);
            EditorPrefs.SetString(LocalJsonPathKey, localJsonPath);
            EditorPrefs.SetString(RemoteTempPathKey, remoteTempPath);
            EditorPrefs.SetString(RemoteTargetPathKey, remoteTargetPath);
            EditorPrefs.SetString(RestartCommandKey, restartCommand);
            EditorPrefs.SetBool(BackupEnabledKey, backupEnabled);
            EditorPrefs.SetBool(AcceptNewHostKey, acceptNewHostKey);
        }

        private void AppendLog(string message)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            log = string.IsNullOrWhiteSpace(log)
                ? $"[{timestamp}] {message}"
                : $"{log}\n[{timestamp}] {message}";
            scrollPosition.y = float.MaxValue;
            Repaint();
        }

        private static string GetInitialDirectory(string path)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                if (Directory.Exists(path))
                {
                    return path;
                }

                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                {
                    return directory;
                }
            }

            return Directory.GetCurrentDirectory();
        }

        private static string PosixDirectoryName(string path)
        {
            int slashIndex = path.LastIndexOf('/');
            if (slashIndex <= 0)
            {
                return "/";
            }

            return path.Substring(0, slashIndex);
        }

        private static string ShellQuote(string value)
        {
            return "'" + value.Replace("'", "'\"'\"'") + "'";
        }

        private static string JoinArguments(List<string> arguments)
        {
            var builder = new StringBuilder();

            for (int i = 0; i < arguments.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(' ');
                }

                string argument = arguments[i];
                builder.Append(argument.IndexOf(' ') >= 0 ? $"\"{argument}\"" : argument);
            }

            return builder.ToString();
        }
    }
}
