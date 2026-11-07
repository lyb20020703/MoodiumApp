using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace VFXViewer
{
    public sealed class SpectatorBonjourHostInfo
    {
        public string Key = string.Empty;
        public string serviceName = string.Empty;
        public string serviceType = string.Empty;
        public string hostName = string.Empty;
        public int port;

        public bool HasResolvedEndpoint => !string.IsNullOrWhiteSpace(hostName) && port > 0;

        public string DisplayName => string.IsNullOrWhiteSpace(serviceName) ? hostName : serviceName;
    }

    public sealed class SpectatorLocalLogCapture : MonoBehaviour
    {
        private static readonly string[] CapturedPrefixes =
        {
            "[Spectator",
            "[SceneToast]"
        };

        private bool m_Subscribed;

        private void OnEnable()
        {
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void Subscribe()
        {
            if (m_Subscribed)
                return;

            PlacementDebugFileLogger.EnsureCreated();
            Application.logMessageReceivedThreaded += HandleLogMessageReceived;
            m_Subscribed = true;
            PlacementDebugFileLogger.Log("[SpectatorLogCapture] Started capturing spectator logs to local file.");
        }

        private void Unsubscribe()
        {
            if (!m_Subscribed)
                return;

            Application.logMessageReceivedThreaded -= HandleLogMessageReceived;
            m_Subscribed = false;
            PlacementDebugFileLogger.Log("[SpectatorLogCapture] Stopped capturing spectator logs.");
        }

        private static void HandleLogMessageReceived(string condition, string stackTrace, LogType type)
        {
            if (string.IsNullOrWhiteSpace(condition) || !ShouldCapture(condition))
                return;

            string formatted = $"[UnityLog:{type}] {condition}";
            if ((type == LogType.Error || type == LogType.Assert || type == LogType.Exception) &&
                !string.IsNullOrWhiteSpace(stackTrace))
            {
                formatted = $"{formatted}{Environment.NewLine}{stackTrace}";
            }

            PlacementDebugFileLogger.Log(formatted);
        }

        private static bool ShouldCapture(string condition)
        {
            for (int i = 0; i < CapturedPrefixes.Length; i++)
            {
                if (condition.StartsWith(CapturedPrefixes[i], StringComparison.Ordinal))
                    return true;
            }

            return false;
        }
    }

    public sealed partial class SpectatorClientService : MonoBehaviour
    {
        public const int DefaultSpectatorPort = 45830;

        private const string ManualHostPlayerPrefsKey = "VFXViewer.Spectator.ManualHost";
        private const string ManualPortPlayerPrefsKey = "VFXViewer.Spectator.ManualPort";
        private const float RootTranslationButtonStep = 0.01f;
        private const float RootYawButtonStep = 5f;
        private const float HeldRootAdjustmentRepeatSeconds = 0.08f;
        private const float HeldRootAdjustmentAccelerationDelaySeconds = 0.35f;
        private const float HeldRootAdjustmentAccelerationDurationSeconds = 1.2f;
        private const float HeldRootAdjustmentMaxTranslationStep = 0.03f;

        [SerializeField] private string serviceType = "_vfxviewer-spectator._tcp";
        [SerializeField] private string editorFallbackHost = "127.0.0.1";
        [SerializeField] private int editorFallbackPort = DefaultSpectatorPort;
        [SerializeField] private float reconnectDelaySeconds = 2f;
        [SerializeField] private float connectTimeoutSeconds = 5f;
        [SerializeField] private bool preferManualEndpoint = false;
        [SerializeField] private bool showManualConnectOverlay = true;
        [SerializeField] private string manualHostOverride = "";
        [SerializeField] private int manualPortOverride = DefaultSpectatorPort;

        private readonly ConcurrentQueue<SpectatorEnvelope> m_IncomingEnvelopes = new ConcurrentQueue<SpectatorEnvelope>();
        private readonly ConcurrentQueue<string> m_LogMessages = new ConcurrentQueue<string>();
        private readonly List<SpectatorBonjourHostInfo> m_DiscoveredBonjourHosts = new List<SpectatorBonjourHostInfo>();

        private SpectatorBonjourBrowser m_Browser;
        private TcpClient m_TcpClient;
        private CancellationTokenSource m_Cancellation;
        private Task m_ReadLoopTask;
        private SpectatorRootManipulator m_RootManipulator;
        private SpectatorContentRuntime m_ContentRuntime;
        private SpectatorCaptureService m_CaptureService;
        private SpectatorLocalLogCapture m_LocalLogCapture;
        private SpectatorConnectPanel m_ConnectPanel;
        private ARAnchorManager m_AnchorManager;
        private ARAnchor m_RootAnchor;
        private FullSceneSnapshot m_PendingSnapshot;
        private float m_NextReconnectAt;
        private string m_ResolvedHost;
        private int m_ResolvedPort;
        private string m_SelectedBonjourHostKey = string.Empty;
        private bool m_IsConnecting;
        private bool m_PendingLayoutExportAfterConnect;
        private bool m_IsLockingRoot;
        private SpectatorHoldButton.HoldAction? m_HeldRootAdjustment;
        private float m_HeldRootAdjustmentStartedAt;
        private float m_NextHeldRootAdjustmentAt;
        private bool m_ShouldRelockAfterHeldAdjustment;
        private string m_LastStatusMessage = "Waiting for host...";
        private int m_BonjourHostsRevision;
        private int m_AutoRecoveryRefreshRequested;
        private string m_AutoRecoveryRefreshMessage = string.Empty;
        private bool m_IsLifecyclePaused;
        private string m_ActiveSceneName = string.Empty;

        public string ManualHostOverride => manualHostOverride;
        public int ManualPortOverride => manualPortOverride;
        public string ResolvedHost => m_ResolvedHost;
        public int ResolvedPort => m_ResolvedPort;
        public int BonjourHostsRevision => m_BonjourHostsRevision;
        public int DiscoveredBonjourHostCount => m_DiscoveredBonjourHosts.Count;
        public string SelectedBonjourHostKey => m_SelectedBonjourHostKey;
        public bool IsConnected => m_TcpClient != null && m_TcpClient.Connected;
        public bool IsConnecting => m_IsConnecting;
        public string LastStatusMessage => m_LastStatusMessage;
        public bool SupportsManualEndpoint => Application.isEditor;
        public bool HasManualEndpointConfigured => SupportsManualEndpoint && IsEndpointConfigured(manualHostOverride, manualPortOverride);
        public bool HasRoot => m_RootManipulator != null;
        public bool IsRootLocked => m_RootManipulator != null && m_RootManipulator.IsLocked;
        public bool CanExportWorldMapBackup => HasSavedWorldMapBackupForExport();
        public bool SupportsWorldMapBackupTransfer => SpectatorDocumentBridge.IsSupported;
        public bool SupportsMediaCapture => m_CaptureService != null && m_CaptureService.IsSupported;
        public bool IsVideoRecording => m_CaptureService != null && m_CaptureService.IsRecordingVideo;
        public float CameraZoomFactor => m_CaptureService != null ? m_CaptureService.CameraZoomFactor : 1f;

        private void OnEnable()
        {
            if (SpectatorRuntimeRole.CurrentRole != AppRole.IPadSpectator || !PlatformRuntime.SupportsSpectatorClientFeatures)
            {
                enabled = false;
                return;
            }

            RefreshActiveSceneNameCache();
            SceneManager.activeSceneChanged += HandleActiveSceneChanged;
            LoadManualEndpoint();
            EnsureRuntimeObjects();
            InitializeWorldMapPersistence();
            StartDiscovery();
        }

        private void OnDisable()
        {
            SceneManager.activeSceneChanged -= HandleActiveSceneChanged;
            StopClient();
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            HandleLifecyclePauseChanged(pauseStatus, pauseStatus ? "pause" : "resume");
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            HandleLifecyclePauseChanged(!hasFocus, hasFocus ? "focus-gained" : "focus-lost");
        }

        private void HandleActiveSceneChanged(Scene previousScene, Scene nextScene)
        {
            RefreshActiveSceneNameCache(nextScene);
        }

        private void RefreshActiveSceneNameCache()
        {
            RefreshActiveSceneNameCache(SceneManager.GetActiveScene());
        }

        private void RefreshActiveSceneNameCache(Scene scene)
        {
            m_ActiveSceneName = scene.IsValid() ? scene.name : string.Empty;
        }

        private string CurrentSceneName => m_ActiveSceneName;

        private void Update()
        {
            FlushLogs();
            ProcessAutoRecoveryRefreshRequests();
            ProcessBonjourEvents();
            ProcessDocumentBridgeEvents();
            TryConnectIfNeeded();
            ProcessIncomingEnvelopes();
            TickHeldRootAdjustment();

            if (m_RootManipulator != null && m_RootManipulator.IsLocked && m_PendingSnapshot != null)
            {
                m_ContentRuntime.ApplyFullSnapshot(m_PendingSnapshot);
                m_PendingSnapshot = null;
            }
        }

        private void EnsureRuntimeObjects()
        {
            if (m_ContentRuntime == null)
                m_ContentRuntime = gameObject.GetComponent<SpectatorContentRuntime>() ?? gameObject.AddComponent<SpectatorContentRuntime>();

            if (m_CaptureService == null)
                m_CaptureService = gameObject.GetComponent<SpectatorCaptureService>() ?? gameObject.AddComponent<SpectatorCaptureService>();

            if (m_LocalLogCapture == null)
                m_LocalLogCapture = gameObject.GetComponent<SpectatorLocalLogCapture>() ?? gameObject.AddComponent<SpectatorLocalLogCapture>();

            m_CaptureService.Initialize(this);
            ResolveAnchorManager();

            if (showManualConnectOverlay)
            {
                if (m_ConnectPanel == null)
                    m_ConnectPanel = FindFirstObjectByType<SpectatorConnectPanel>(FindObjectsInactive.Include);

                if (m_ConnectPanel != null)
                    m_ConnectPanel.Initialize(this);
                else
                    Debug.LogWarning("[SpectatorClient] No SpectatorConnectPanel was found in the scene.");
            }
        }

        private GameObject CreateSpectatorRootObject()
        {
            GameObject rootPrefab = ResolveRootPrefabFromPlan();
            GameObject root = rootPrefab != null
                ? Instantiate(rootPrefab)
                : new GameObject("SpectatorRoot");

            root.name = "SpectatorRoot";
            PrepareRootForSpectator(root);
            return root;
        }

        private static GameObject ResolveRootPrefabFromPlan()
        {
            ChapterPlacementDirector director = FindFirstObjectByType<ChapterPlacementDirector>(FindObjectsInactive.Include);
            ChapterPlacementPlan plan = director != null ? director.Plan : null;
            if (plan == null || plan.rootSettings == null)
                return null;

            return plan.rootSettings.markerPrefab;
        }

        private static void PrepareRootForSpectator(GameObject root)
        {
            if (root == null)
                return;

            SpectatorPeopleOcclusionRendererAdapter occlusionAdapter =
                root.GetComponent<SpectatorPeopleOcclusionRendererAdapter>() ??
                root.AddComponent<SpectatorPeopleOcclusionRendererAdapter>();
            occlusionAdapter.ApplyNow("PrepareRootForSpectator");

            PlacementRootMarker[] rootMarkers = root.GetComponentsInChildren<PlacementRootMarker>(true);
            for (int i = 0; i < rootMarkers.Length; i++)
            {
                if (rootMarkers[i] != null)
                    rootMarkers[i].enabled = false;
            }

            XRGrabInteractable[] interactables = root.GetComponentsInChildren<XRGrabInteractable>(true);
            for (int i = 0; i < interactables.Length; i++)
            {
                if (interactables[i] != null)
                    interactables[i].enabled = false;
            }

            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null)
                    colliders[i].enabled = false;
            }

            Rigidbody[] rigidbodies = root.GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < rigidbodies.Length; i++)
            {
                Rigidbody body = rigidbodies[i];
                if (body == null)
                    continue;

                body.isKinematic = true;
                body.useGravity = false;
                body.detectCollisions = false;
            }

            ExhibitPlacementManager[] placementManagers = root.GetComponentsInChildren<ExhibitPlacementManager>(true);
            for (int i = 0; i < placementManagers.Length; i++)
            {
                if (placementManagers[i] != null)
                    placementManagers[i].enabled = false;
            }

            ChapterPlacementDirector[] directors = root.GetComponentsInChildren<ChapterPlacementDirector>(true);
            for (int i = 0; i < directors.Length; i++)
            {
                if (directors[i] != null)
                    directors[i].enabled = false;
            }

            AutoHandVisionOSBridge[] handBridges = root.GetComponentsInChildren<AutoHandVisionOSBridge>(true);
            for (int i = 0; i < handBridges.Length; i++)
            {
                if (handBridges[i] != null)
                    handBridges[i].enabled = false;
            }

            ExhibitControlPanel[] controlPanels = root.GetComponentsInChildren<ExhibitControlPanel>(true);
            for (int i = 0; i < controlPanels.Length; i++)
            {
                ExhibitControlPanel panel = controlPanels[i];
                if (panel == null)
                    continue;

                panel.enabled = false;
                panel.gameObject.SetActive(false);
            }

            PlacementPanelPalmRelocator[] palmRelocators = root.GetComponentsInChildren<PlacementPanelPalmRelocator>(true);
            for (int i = 0; i < palmRelocators.Length; i++)
            {
                PlacementPanelPalmRelocator relocator = palmRelocators[i];
                if (relocator == null)
                    continue;

                relocator.enabled = false;
                relocator.gameObject.SetActive(false);
            }
        }

        private void StartDiscovery()
        {
            if (m_Browser == null)
                m_Browser = new SpectatorBonjourBrowser();

            m_Browser.Start(serviceType);

            if (Application.isEditor && !HasManualEndpointConfigured)
            {
                m_ResolvedHost = editorFallbackHost;
                m_ResolvedPort = editorFallbackPort;
                SetStatus($"Editor fallback ready: {m_ResolvedHost}:{m_ResolvedPort}", logToConsole: true);
                return;
            }

            SetStatus("等待通过 Bonjour 发现主机...");
        }

        private void StopClient()
        {
            StopWorldMapOperations();

            m_Browser?.Dispose();
            m_Browser = null;

            if (m_Cancellation != null)
            {
                m_Cancellation.Cancel();
                m_Cancellation.Dispose();
                m_Cancellation = null;
            }

            try
            {
                m_TcpClient?.Close();
            }
            catch
            {
                // Ignore shutdown issues.
            }

            m_TcpClient = null;
            m_ReadLoopTask = null;
            m_IncomingEnvelopes.Clear();
            m_PendingSnapshot = null;
            m_IsConnecting = false;
            ClearDiscoveredBonjourHosts(keepSelection: false);
        }

        private void ProcessBonjourEvents()
        {
            if (m_Browser == null)
                return;

            var events = m_Browser.PollEvents();
            for (int i = 0; i < events.Count; i++)
            {
                SpectatorBonjourEvent nextEvent = events[i];
                if (nextEvent == null)
                    continue;

                SpectatorBonjourEventType type = (SpectatorBonjourEventType)nextEvent.type;
                LogBonjourEvent(type, nextEvent);
                if (type == SpectatorBonjourEventType.ServiceResolved || type == SpectatorBonjourEventType.ServiceFound)
                {
                    UpsertDiscoveredBonjourHost(nextEvent);
                }
                else if (type == SpectatorBonjourEventType.ServiceLost)
                {
                    RemoveDiscoveredBonjourHost(nextEvent);
                }
            }
        }

        private static void LogBonjourEvent(SpectatorBonjourEventType type, SpectatorBonjourEvent nextEvent)
        {
            if (nextEvent == null)
                return;

            string summary =
                $"serviceName={nextEvent.serviceName}, serviceType={nextEvent.serviceType}, hostName={nextEvent.hostName}, port={nextEvent.port}, error={nextEvent.error}";

            switch (type)
            {
                case SpectatorBonjourEventType.ServiceFound:
                case SpectatorBonjourEventType.ServiceResolved:
                case SpectatorBonjourEventType.AdvertiserStarted:
                    Debug.Log($"[SpectatorBonjour] Event {type}: {summary}");
                    break;

                case SpectatorBonjourEventType.ServiceLost:
                case SpectatorBonjourEventType.AdvertiserStopped:
                    Debug.Log($"[SpectatorBonjour] Event {type}: {summary}");
                    break;

                case SpectatorBonjourEventType.Error:
                    Debug.LogWarning($"[SpectatorBonjour] Event {type}: {summary}");
                    break;
            }
        }

        private void TryConnectIfNeeded()
        {
            if (m_TcpClient != null && m_TcpClient.Connected)
                return;

            if (m_IsConnecting)
                return;

            if (Time.unscaledTime < m_NextReconnectAt)
                return;

            if (!TryGetPreferredEndpoint(out string host, out int port))
                return;

            m_NextReconnectAt = Time.unscaledTime + Mathf.Max(0.5f, reconnectDelaySeconds);
            _ = ConnectAsync(host, port);
        }

        private async Task ConnectAsync(string host, int port)
        {
            StopTransportOnly();
            m_IsConnecting = true;
            SetStatus($"Connecting to {host}:{port}...", logToConsole: true);

            try
            {
                m_Cancellation = new CancellationTokenSource();
                Task connectTask;
                if (IPAddress.TryParse(host, out IPAddress ipAddress))
                {
                    m_TcpClient = new TcpClient(ipAddress.AddressFamily);
                    connectTask = m_TcpClient.ConnectAsync(ipAddress, port);
                }
                else
                {
                    m_TcpClient = new TcpClient();
                    connectTask = m_TcpClient.ConnectAsync(host, port);
                }

                Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(Mathf.Max(1f, connectTimeoutSeconds)), m_Cancellation.Token);
                Task completedTask = await Task.WhenAny(connectTask, timeoutTask).ConfigureAwait(false);
                if (completedTask != connectTask)
                    throw new TimeoutException($"Timed out after {Mathf.Max(1f, connectTimeoutSeconds):0.#}s");

                await connectTask.ConfigureAwait(false);
                m_TcpClient.NoDelay = true;
                m_ReadLoopTask = ReadLoopAsync(m_Cancellation.Token);

                var hello = new SpectatorHello
                {
                    protocolVersion = 1,
                    assetManifestVersion = "1",
                    deviceRole = AppRole.IPadSpectator.ToString(),
                    sessionId = Guid.NewGuid().ToString("N"),
                    sceneName = CurrentSceneName,
                };

                await SpectatorTransport.WriteEnvelopeAsync(
                    m_TcpClient.GetStream(),
                    SpectatorMessageKind.Hello,
                    hello,
                    m_Cancellation.Token).ConfigureAwait(false);

                SetStatus($"Connected to {host}:{port}", logToConsole: true);
                TryRunPendingLayoutExportAfterConnect();
            }
            catch (TimeoutException ex)
            {
                SetStatus($"Connect timed out: {ex.Message}. Check same Wi-Fi and Local Network permission.", logToConsole: true);
                StopTransportOnly();
            }
            catch (Exception ex)
            {
                SetStatus(BuildConnectFailureMessage(ex), logToConsole: true);
                StopTransportOnly();
            }
            finally
            {
                m_IsConnecting = false;
            }
        }

        private static string BuildConnectFailureMessage(Exception exception)
        {
            if (exception is AggregateException aggregateException && aggregateException.InnerException != null)
                exception = aggregateException.InnerException;

            if (exception is SocketException socketException)
            {
                switch (socketException.SocketErrorCode)
                {
                    case SocketError.HostUnreachable:
                    case SocketError.NetworkUnreachable:
                        return "Connect failed: No route to host. On iPhone this usually means Local Network permission is off, Bonjour has not resolved a reachable host yet, or the devices are on different Wi-Fi networks.";

                    case SocketError.ConnectionRefused:
                        return "Connect failed: Connection refused. The host app is not listening yet.";

                    case SocketError.TimedOut:
                        return "Connect failed: Timed out. Check same Wi-Fi and Local Network permission.";
                }
            }

            return $"Connect failed: {exception.Message}";
        }

        private async Task ReadLoopAsync(CancellationToken cancellationToken)
        {
            bool shouldAutoRecover = false;
            const string disconnectMessage = "连接已断开，正在重新发现主机...";

            try
            {
                NetworkStream stream = m_TcpClient.GetStream();
                while (!cancellationToken.IsCancellationRequested && m_TcpClient != null && m_TcpClient.Connected)
                {
                    string frame = await SpectatorTransport.ReadFrameAsync(stream, cancellationToken).ConfigureAwait(false);
                    if (frame == null)
                    {
                        shouldAutoRecover = !cancellationToken.IsCancellationRequested;
                        break;
                    }

                    if (!SpectatorProtocol.TryDeserializeEnvelope(frame, out SpectatorEnvelope envelope))
                        continue;

                    m_IncomingEnvelopes.Enqueue(envelope);
                }

                if (!cancellationToken.IsCancellationRequested)
                    shouldAutoRecover = true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Expected when Disconnect() or StopTransportOnly() cancels the read loop.
            }
            catch (Exception ex)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    SetStatus($"Read loop ended: {ex.Message}", logToConsole: true);
                    shouldAutoRecover = true;
                }
            }
            finally
            {
                StopTransportOnly();
                if (shouldAutoRecover)
                {
                    SetStatus(disconnectMessage, logToConsole: true);
                    RequestAutoRecoveryRefresh(disconnectMessage);
                }
            }
        }

        private void ProcessIncomingEnvelopes()
        {
            while (m_IncomingEnvelopes.TryDequeue(out SpectatorEnvelope envelope))
            {
                SpectatorMessageKind kind = (SpectatorMessageKind)envelope.kind;
                switch (kind)
                {
                    case SpectatorMessageKind.Hello:
                        break;

                    case SpectatorMessageKind.FullSceneSnapshot:
                        HandleFullSnapshot(SpectatorProtocol.DeserializePayload<FullSceneSnapshot>(envelope.jsonPayload));
                        break;

                    case SpectatorMessageKind.DeltaEventBatch:
                        HandleDeltaBatch(SpectatorProtocol.DeserializePayload<DeltaEventBatch>(envelope.jsonPayload));
                        break;

                    case SpectatorMessageKind.Heartbeat:
                        break;

                    case SpectatorMessageKind.CommandResult:
                        HandleCommandResult(SpectatorProtocol.DeserializePayload<SpectatorCommandResult>(envelope.jsonPayload));
                        break;
                }
            }
        }

        private void HandleFullSnapshot(FullSceneSnapshot snapshot)
        {
            if (snapshot == null)
                return;

            CacheSharedLayoutSnapshot(snapshot.sharedLayout);

            if (m_RootManipulator != null && m_RootManipulator.IsLocked)
                m_ContentRuntime.ApplyFullSnapshot(snapshot);
            else
                m_PendingSnapshot = snapshot;
        }

        private void HandleDeltaBatch(DeltaEventBatch batch)
        {
            if (batch == null || batch.events == null || batch.events.Length == 0)
                return;

            for (int i = 0; i < batch.events.Length; i++)
            {
                DeltaEvent nextEvent = batch.events[i];
                if (nextEvent == null || (SpectatorDeltaKind)nextEvent.kind != SpectatorDeltaKind.LayoutChanged)
                    continue;

                CacheSharedLayoutSnapshot(SpectatorProtocol.DeserializePayload<SharedLayoutSnapshot>(nextEvent.payloadJson));
            }

            if (m_RootManipulator == null || !m_RootManipulator.IsLocked)
                return;

            for (int i = 0; i < batch.events.Length; i++)
            {
                DeltaEvent nextEvent = batch.events[i];
                if (nextEvent != null)
                    m_ContentRuntime.ApplyDeltaEvent(nextEvent);
            }
        }

        private void StopTransportOnly()
        {
            if (m_Cancellation != null)
            {
                m_Cancellation.Cancel();
                m_Cancellation.Dispose();
                m_Cancellation = null;
            }

            try
            {
                m_TcpClient?.Close();
            }
            catch
            {
                // Ignore socket shutdown errors.
            }

            m_TcpClient = null;
            m_ReadLoopTask = null;
            m_IsConnecting = false;
        }

        private void FlushLogs()
        {
            while (m_LogMessages.TryDequeue(out string message))
                Debug.Log(message, this);
        }

        public void SaveManualEndpoint(string host, int port)
        {
            if (!SupportsManualEndpoint)
            {
                manualHostOverride = string.Empty;
                manualPortOverride = DefaultSpectatorPort;
                PlayerPrefs.DeleteKey(ManualHostPlayerPrefsKey);
                PlayerPrefs.DeleteKey(ManualPortPlayerPrefsKey);
                PlayerPrefs.Save();
                SetStatus("Manual IP connect is disabled. Waiting for Bonjour discovery...");
                return;
            }

            manualHostOverride = string.IsNullOrWhiteSpace(host) ? string.Empty : host.Trim();
            manualPortOverride = DefaultSpectatorPort;

            if (string.IsNullOrEmpty(manualHostOverride))
            {
                PlayerPrefs.DeleteKey(ManualHostPlayerPrefsKey);
                PlayerPrefs.DeleteKey(ManualPortPlayerPrefsKey);
                PlayerPrefs.Save();
                SetStatus("Manual host cleared. Waiting for discovery...");
                return;
            }

            PlayerPrefs.SetString(ManualHostPlayerPrefsKey, manualHostOverride);
            PlayerPrefs.SetInt(ManualPortPlayerPrefsKey, manualPortOverride);
            PlayerPrefs.Save();
            SetStatus($"Manual host ready: {manualHostOverride}:{manualPortOverride}");
        }

        public void ClearManualEndpoint()
        {
            SaveManualEndpoint(string.Empty, editorFallbackPort);
            SetStatus("Manual host cleared. Waiting for Bonjour discovery...");
        }

        public void ConnectNow()
        {
            if (IsConnected || IsConnecting)
                StopTransportOnly();

            m_NextReconnectAt = 0f;
        }

        public void ConnectToManualEndpoint(string host, int port)
        {
            if (!SupportsManualEndpoint)
            {
                RefreshBonjourAndReconnect();
                return;
            }

            SaveManualEndpoint(host, port);
            ConnectNow();
        }

        public void RefreshBonjourAndReconnect()
        {
            RefreshBonjourAndReconnectInternal("Refreshing Bonjour discovery...");
        }

        private void RefreshBonjourAndReconnectInternal(string statusMessage)
        {
            if (m_Browser != null)
            {
                m_Browser.Dispose();
                m_Browser = null;
            }

            m_ResolvedHost = string.Empty;
            m_ResolvedPort = 0;
            ClearDiscoveredBonjourHosts(keepSelection: true);
            StopTransportOnly();
            StartDiscovery();
            m_NextReconnectAt = 0f;
            SetStatus(statusMessage, logToConsole: true);
        }

        public void SetRootLocked(bool locked)
        {
            if (m_RootManipulator == null)
            {
                SetStatus("Create Root first.");
                return;
            }

            if (locked)
            {
                if (m_IsLockingRoot)
                    return;

                _ = LockRootWithAnchorAsync();
            }
            else
            {
                StopWorldMapSaveRoutine();
                CancelPendingWorldMapRefinement();
                ReleaseCurrentRootAnchor();
                m_RootManipulator.SetLocked(false);
                SetStatus("Root unlocked. Use the buttons to reposition it.");
            }
        }

        public void ResetRootPose()
        {
            if (m_RootManipulator == null)
            {
                SetStatus("Create Root first.");
                return;
            }

            StopWorldMapSaveRoutine();
            CancelPendingWorldMapRefinement();
            ReleaseCurrentRootAnchor();
            m_RootManipulator.SetLocked(false);
            m_RootManipulator.ResetPose();
            SetStatus("Root reset. Use the buttons to reposition it.");
        }

        public void CreateOrRecreateRoot()
        {
            Debug.Log(
                $"[SpectatorRoot] CreateOrRecreateRoot requested. hasExistingRoot={m_RootManipulator != null}, " +
                $"isRootLocked={IsRootLocked}, hasAnchor={m_RootAnchor != null}, " +
                $"savedMetadataExists={File.Exists(WorldMapMetadataPath)}, savedBytesExists={File.Exists(WorldMapBytesPath)}.",
                this);
            CancelPendingWorldMapRestore();
            CancelPendingWorldMapRefinement();
            StopWorldMapSaveRoutine();
            EndHeldRootAdjustmentInternal(relockIfNeeded: false);
            CreateOrRecreateRootInternal(null, anchorImmediately: true, announceCreation: true);
        }

        public void DeleteRoot()
        {
            if (m_RootManipulator == null)
            {
                SetStatus("Create Root first.");
                return;
            }

            Debug.Log(
                $"[SpectatorRoot] DeleteRoot requested. hasAnchor={m_RootAnchor != null}, " +
                $"savedMetadataExists={File.Exists(WorldMapMetadataPath)}, savedBytesExists={File.Exists(WorldMapBytesPath)}.",
                this);
            CancelPendingWorldMapRestore();
            CancelPendingWorldMapRefinement();
            StopWorldMapSaveRoutine();
            EndHeldRootAdjustmentInternal(relockIfNeeded: false);
            ReleaseCurrentRootAnchor();

            if (m_RootManipulator != null)
                Destroy(m_RootManipulator.gameObject);

            m_RootManipulator = null;
            m_RootAnchor = null;
            m_PendingSnapshot = null;
            m_ContentRuntime?.ClearRuntimeContent();
            DeleteSavedWorldMap();
            SetStatus("Root deleted.");
        }

        private void CreateOrRecreateRootInternal(Pose? initialPose, bool anchorImmediately, bool announceCreation)
        {
            Transform existingRoot = m_RootManipulator != null ? m_RootManipulator.transform : null;
            ReleaseCurrentRootAnchor();
            GameObject root = CreateSpectatorRootObject();
            root.transform.SetParent(transform, false);

            m_RootManipulator = root.GetComponent<SpectatorRootManipulator>() ?? root.AddComponent<SpectatorRootManipulator>();
            m_RootManipulator.SetLocked(false);
            if (initialPose.HasValue)
            {
                Pose pose = initialPose.Value;
                m_RootManipulator.transform.SetPositionAndRotation(pose.position, pose.rotation);
            }
            else
            {
                m_RootManipulator.ResetPose();
            }

            if (m_ContentRuntime == null)
                m_ContentRuntime = gameObject.GetComponent<SpectatorContentRuntime>() ?? gameObject.AddComponent<SpectatorContentRuntime>();

            if (existingRoot == null)
                m_ContentRuntime.Initialize(m_RootManipulator.transform);
            else
                m_ContentRuntime.SetRootTransform(m_RootManipulator.transform);

            if (existingRoot != null)
                Destroy(existingRoot.gameObject);

            Pose effectivePose = new Pose(m_RootManipulator.transform.position, m_RootManipulator.transform.rotation);
            Debug.Log(
                $"[SpectatorRoot] CreateOrRecreateRootInternal completed. hadExistingRoot={existingRoot != null}, " +
                $"anchorImmediately={anchorImmediately}, announceCreation={announceCreation}, {FormatPoseForLog(effectivePose)}.",
                this);

            if (announceCreation)
            {
                SetStatus(existingRoot == null
                    ? "Root created. Creating spatial anchor..."
                    : "Root recreated. Creating spatial anchor...");
            }

            if (anchorImmediately)
                _ = LockRootWithAnchorAsync();
        }

        public void NudgeRootForward(bool forward)
        {
            if (m_RootManipulator == null)
            {
                SetStatus("Create Root first.");
                return;
            }

            if (m_RootManipulator.IsLocked)
            {
                SetStatus("Unlock Root before moving it.");
                return;
            }

            m_RootManipulator.NudgeForward(forward ? RootTranslationButtonStep : -RootTranslationButtonStep);
            SetStatus($"Root nudged {(forward ? "forward" : "backward")}.");
        }

        public void NudgeRootRight(bool right)
        {
            if (m_RootManipulator == null)
            {
                SetStatus("Create Root first.");
                return;
            }

            if (m_RootManipulator.IsLocked)
            {
                SetStatus("Unlock Root before moving it.");
                return;
            }

            m_RootManipulator.NudgeRight(right ? RootTranslationButtonStep : -RootTranslationButtonStep);
            SetStatus($"Root nudged {(right ? "right" : "left")}.");
        }

        public void NudgeRootUp(bool up)
        {
            if (m_RootManipulator == null)
            {
                SetStatus("Create Root first.");
                return;
            }

            if (m_RootManipulator.IsLocked)
            {
                SetStatus("Unlock Root before moving it.");
                return;
            }

            m_RootManipulator.NudgeUp(up ? RootTranslationButtonStep : -RootTranslationButtonStep);
            SetStatus($"Root nudged {(up ? "up" : "down")}.");
        }

        public void NudgeRootYaw(bool clockwise)
        {
            if (m_RootManipulator == null)
            {
                SetStatus("Create Root first.");
                return;
            }

            if (m_RootManipulator.IsLocked)
            {
                SetStatus("Unlock Root before rotating it.");
                return;
            }

            m_RootManipulator.NudgeYaw(clockwise ? RootYawButtonStep : -RootYawButtonStep);
            SetStatus($"Root rotated {(clockwise ? "right" : "left")}.");
        }

        public void BeginHeldRootAdjustment(SpectatorHoldButton.HoldAction action)
        {
            if (m_RootManipulator == null)
            {
                SetStatus("Create Root first.");
                return;
            }

            if (m_IsLockingRoot)
            {
                SetStatus("Root is locking. Please wait.");
                return;
            }

            if (!m_HeldRootAdjustment.HasValue)
            {
                bool wasLocked = m_RootManipulator.IsLocked || m_RootAnchor != null;
                m_ShouldRelockAfterHeldAdjustment = wasLocked;
                m_HeldRootAdjustmentStartedAt = Time.unscaledTime;
                if (wasLocked)
                {
                    StopWorldMapSaveRoutine();
                    CancelPendingWorldMapRefinement();
                    ReleaseCurrentRootAnchor();
                    m_RootManipulator.SetLocked(false);
                }
            }

            m_HeldRootAdjustment = action;
            m_NextHeldRootAdjustmentAt = Time.unscaledTime + HeldRootAdjustmentRepeatSeconds;
            ApplyHeldRootAdjustment(action, announce: false);
        }

        public void EndHeldRootAdjustment(SpectatorHoldButton.HoldAction action)
        {
            if (!m_HeldRootAdjustment.HasValue || m_HeldRootAdjustment.Value != action)
                return;

            EndHeldRootAdjustmentInternal(relockIfNeeded: true);
        }

        private void ResolveAnchorManager()
        {
            if (m_AnchorManager != null)
                return;

            m_AnchorManager = FindFirstObjectByType<ARAnchorManager>(FindObjectsInactive.Include);
        }

        private async Task LockRootWithAnchorAsync(bool captureWorldMapAfterLock = true, string successMessage = null)
        {
            if (m_RootManipulator == null)
                return;

            ResolveAnchorManager();
            if (m_AnchorManager == null || !m_AnchorManager.isActiveAndEnabled)
            {
                SetStatus("Lock Root failed: no ARAnchorManager is available.");
                return;
            }

            m_IsLockingRoot = true;
            SetStatus("Creating spatial anchor for Root...");

            try
            {
                ReleaseCurrentRootAnchor();
                Transform rootTransform = m_RootManipulator.transform;
                Pose rootPose = new Pose(rootTransform.position, rootTransform.rotation);
                Debug.Log(
                    $"[SpectatorRoot] LockRootWithAnchorAsync started. captureWorldMapAfterLock={captureWorldMapAfterLock}, " +
                    $"{FormatPoseForLog(rootPose)}.",
                    this);
                var result = await m_AnchorManager.TryAddAnchorAsync(rootPose);
                if (!result.status.IsSuccess() || result.value == null)
                {
                    SetStatus($"Lock Root failed: anchor creation status={result.status}");
                    Debug.LogWarning($"[SpectatorRoot] Failed to create ARAnchor. status={result.status}.", this);
                    return;
                }

                m_RootAnchor = result.value;
                m_RootAnchor.gameObject.name = "SpectatorRootAnchor";
                Vector3 requestedPosition = rootPose.position;
                Quaternion requestedRotation = rootPose.rotation;
                Vector3 anchorPosition = m_RootAnchor.transform.position;
                Quaternion anchorRotation = m_RootAnchor.transform.rotation;
                float positionDelta = Vector3.Distance(requestedPosition, anchorPosition);
                float rotationDelta = Quaternion.Angle(requestedRotation, anchorRotation);
                Debug.Log(
                    $"[SpectatorRoot] ARAnchor created. requestedPos={requestedPosition}, requestedRot={requestedRotation.eulerAngles}, " +
                    $"anchorPos={anchorPosition}, anchorRot={anchorRotation.eulerAngles}, " +
                    $"positionDelta={positionDelta:F4}m, rotationDelta={rotationDelta:F2}deg.");
                rootTransform.SetParent(m_RootAnchor.transform, true);
                rootTransform.localPosition = Vector3.zero;
                rootTransform.localRotation = Quaternion.identity;
                m_RootManipulator.SetLocked(true);
                SetStatus(string.IsNullOrWhiteSpace(successMessage)
                    ? captureWorldMapAfterLock
                        ? "Root locked with spatial anchor. Saving ARWorldMap..."
                        : "Root locked with spatial anchor. Applying spectator content."
                    : successMessage);
                if (m_PendingSnapshot != null)
                {
                    m_ContentRuntime.ApplyFullSnapshot(m_PendingSnapshot);
                    m_PendingSnapshot = null;
                }

                if (captureWorldMapAfterLock)
                    StartWorldMapSaveRoutine();
            }
            catch (Exception ex)
            {
                SetStatus($"Lock Root failed: {ex.Message}");
                Debug.LogWarning($"[SpectatorRoot] LockRootWithAnchorAsync exception: {ex}", this);
            }
            finally
            {
                m_IsLockingRoot = false;
            }
        }

        private void ReleaseCurrentRootAnchor()
        {
            if (m_RootAnchor == null)
                return;

            if (m_RootManipulator != null)
                m_RootManipulator.transform.SetParent(transform, true);

            Debug.Log(
                $"[SpectatorRoot] Releasing current ARAnchor '{m_RootAnchor.gameObject.name}'. " +
                $"anchorPose={FormatPoseForLog(new Pose(m_RootAnchor.transform.position, m_RootAnchor.transform.rotation))}.",
                this);
            Destroy(m_RootAnchor.gameObject);
            m_RootAnchor = null;
        }

        private void TickHeldRootAdjustment()
        {
            if (!m_HeldRootAdjustment.HasValue || m_RootManipulator == null)
                return;

            if (m_IsLockingRoot)
                return;

            if (Time.unscaledTime < m_NextHeldRootAdjustmentAt)
                return;

            ApplyHeldRootAdjustment(m_HeldRootAdjustment.Value, announce: false);
            m_NextHeldRootAdjustmentAt = Time.unscaledTime + HeldRootAdjustmentRepeatSeconds;
        }

        private void EndHeldRootAdjustmentInternal(bool relockIfNeeded)
        {
            bool shouldRelock = relockIfNeeded && m_ShouldRelockAfterHeldAdjustment && m_RootManipulator != null;
            m_HeldRootAdjustment = null;
            m_HeldRootAdjustmentStartedAt = 0f;
            m_ShouldRelockAfterHeldAdjustment = false;

            if (shouldRelock)
                _ = LockRootWithAnchorAsync(captureWorldMapAfterLock: true, successMessage: "Root moved and re-locked.");
        }

        private void ApplyHeldRootAdjustment(SpectatorHoldButton.HoldAction action, bool announce)
        {
            if (m_RootManipulator == null)
                return;

            float translationStep = GetHeldTranslationStep(action);
            switch (action)
            {
                case SpectatorHoldButton.HoldAction.RotateLeft:
                    m_RootManipulator.NudgeYaw(-RootYawButtonStep);
                    if (announce)
                        SetStatus("Root rotated left.");
                    break;

                case SpectatorHoldButton.HoldAction.Forward:
                    m_RootManipulator.NudgeForward(translationStep);
                    if (announce)
                        SetStatus("Root nudged forward.");
                    break;

                case SpectatorHoldButton.HoldAction.RotateRight:
                    m_RootManipulator.NudgeYaw(RootYawButtonStep);
                    if (announce)
                        SetStatus("Root rotated right.");
                    break;

                case SpectatorHoldButton.HoldAction.Left:
                    m_RootManipulator.NudgeRight(-translationStep);
                    if (announce)
                        SetStatus("Root nudged left.");
                    break;

                case SpectatorHoldButton.HoldAction.Back:
                    m_RootManipulator.NudgeForward(-translationStep);
                    if (announce)
                        SetStatus("Root nudged backward.");
                    break;

                case SpectatorHoldButton.HoldAction.Right:
                    m_RootManipulator.NudgeRight(translationStep);
                    if (announce)
                        SetStatus("Root nudged right.");
                    break;

                case SpectatorHoldButton.HoldAction.Down:
                    m_RootManipulator.NudgeUp(-translationStep);
                    if (announce)
                        SetStatus("Root nudged down.");
                    break;

                case SpectatorHoldButton.HoldAction.Up:
                    m_RootManipulator.NudgeUp(translationStep);
                    if (announce)
                        SetStatus("Root nudged up.");
                    break;
            }
        }

        private float GetHeldTranslationStep(SpectatorHoldButton.HoldAction action)
        {
            if (!IsTranslationHoldAction(action))
                return RootTranslationButtonStep;

            if (!m_HeldRootAdjustment.HasValue || m_HeldRootAdjustment.Value != action)
                return RootTranslationButtonStep;

            float heldSeconds = Mathf.Max(0f, Time.unscaledTime - m_HeldRootAdjustmentStartedAt);
            if (heldSeconds <= HeldRootAdjustmentAccelerationDelaySeconds)
                return RootTranslationButtonStep;

            float normalized = Mathf.Clamp01(
                (heldSeconds - HeldRootAdjustmentAccelerationDelaySeconds) /
                Mathf.Max(0.01f, HeldRootAdjustmentAccelerationDurationSeconds));

            return Mathf.Lerp(
                RootTranslationButtonStep,
                HeldRootAdjustmentMaxTranslationStep,
                normalized);
        }

        private static bool IsTranslationHoldAction(SpectatorHoldButton.HoldAction action)
        {
            switch (action)
            {
                case SpectatorHoldButton.HoldAction.Forward:
                case SpectatorHoldButton.HoldAction.Left:
                case SpectatorHoldButton.HoldAction.Back:
                case SpectatorHoldButton.HoldAction.Right:
                case SpectatorHoldButton.HoldAction.Down:
                case SpectatorHoldButton.HoldAction.Up:
                    return true;
                default:
                    return false;
            }
        }

        public void Disconnect()
        {
            m_PendingLayoutExportAfterConnect = false;
            StopTransportOnly();
            SetStatus("Disconnected.");
        }

        public void ExportWorldMapBackup()
        {
            ExportSavedWorldMapBackup();
        }

        public void ImportWorldMapBackup()
        {
            BeginImportWorldMapBackup();
        }

        private void LoadManualEndpoint()
        {
            if (!SupportsManualEndpoint)
            {
                manualHostOverride = string.Empty;
                manualPortOverride = DefaultSpectatorPort;
                PlayerPrefs.DeleteKey(ManualHostPlayerPrefsKey);
                PlayerPrefs.DeleteKey(ManualPortPlayerPrefsKey);
                PlayerPrefs.Save();
                return;
            }

            if (string.IsNullOrWhiteSpace(manualHostOverride))
            {
                manualHostOverride = PlayerPrefs.GetString(ManualHostPlayerPrefsKey, string.Empty);
            }

            manualPortOverride = DefaultSpectatorPort;

            if (HasManualEndpointConfigured)
                SetStatus($"Manual host ready: {manualHostOverride}:{manualPortOverride}");
        }

        private bool TryGetPreferredEndpoint(out string host, out int port)
        {
            RefreshResolvedEndpointFromBonjourSelection();

            if (TryGetResolvedSelectedBonjourEndpoint(out host, out port))
                return true;

            if (SupportsManualEndpoint && preferManualEndpoint && HasManualEndpointConfigured)
            {
                host = manualHostOverride;
                port = manualPortOverride;
                return true;
            }

            if (Application.isEditor && IsEndpointConfigured(editorFallbackHost, editorFallbackPort))
            {
                host = editorFallbackHost;
                port = editorFallbackPort;
                return true;
            }

            host = string.Empty;
            port = 0;
            return false;
        }

        private static bool IsEndpointConfigured(string host, int port)
        {
            return !string.IsNullOrWhiteSpace(host) && port > 0;
        }

        public SpectatorBonjourHostInfo GetDiscoveredBonjourHost(int index)
        {
            if (index < 0 || index >= m_DiscoveredBonjourHosts.Count)
                return null;

            return m_DiscoveredBonjourHosts[index];
        }

        public void SelectBonjourHost(string hostKey, bool reconnectImmediately = true)
        {
            SpectatorBonjourHostInfo host = FindDiscoveredBonjourHostByKey(hostKey);
            if (host == null)
            {
                SetStatus("所选主机已不可用。");
                return;
            }

            m_SelectedBonjourHostKey = host.Key;
            RefreshResolvedEndpointFromBonjourSelection();
            m_BonjourHostsRevision++;

            if (host.HasResolvedEndpoint)
            {
                SetStatus($"已选择主机: {host.DisplayName} ({host.hostName}:{host.port})", logToConsole: true);
                if (reconnectImmediately)
                    ConnectNow();
            }
            else
            {
                SetStatus($"已选择主机: {host.DisplayName}，等待 Bonjour 解析地址。");
            }
        }

        private void SetStatus(string message, bool logToConsole = false)
        {
            m_LastStatusMessage = string.IsNullOrWhiteSpace(message) ? string.Empty : message.Trim();
            if (logToConsole && !string.IsNullOrEmpty(m_LastStatusMessage))
                m_LogMessages.Enqueue($"[SpectatorClient] {m_LastStatusMessage}");
        }

        internal void ReportCaptureStatus(string message, bool logToConsole = false)
        {
            SetStatus(message, logToConsole);
        }

        public void CapturePhoto()
        {
            if (m_CaptureService == null)
            {
                SetStatus("拍照功能尚未初始化。");
                return;
            }

            m_CaptureService.CapturePhoto();
        }

        public void ToggleVideoRecording()
        {
            if (m_CaptureService == null)
            {
                SetStatus("录像功能尚未初始化。");
                return;
            }

            if (m_CaptureService.IsRecordingVideo)
                m_CaptureService.StopVideoRecording();
            else
                m_CaptureService.StartVideoRecording();
        }

        public void ZoomCameraIn()
        {
            if (m_CaptureService == null)
            {
                SetStatus("画面缩放功能尚未初始化。");
                return;
            }

            m_CaptureService.ZoomCameraIn();
        }

        public void ZoomCameraOut()
        {
            if (m_CaptureService == null)
            {
                SetStatus("画面缩放功能尚未初始化。");
                return;
            }

            m_CaptureService.ZoomCameraOut();
        }

        public void ResetCameraZoom()
        {
            if (m_CaptureService == null)
            {
                SetStatus("画面缩放功能尚未初始化。");
                return;
            }

            m_CaptureService.ResetCameraZoom();
        }

        private void HandleLifecyclePauseChanged(bool paused, string reason)
        {
            if (SpectatorRuntimeRole.CurrentRole != AppRole.IPadSpectator || !PlatformRuntime.SupportsSpectatorClientFeatures)
                return;

            if (m_IsLifecyclePaused == paused)
                return;

            m_IsLifecyclePaused = paused;
            Debug.Log($"[SpectatorClient] Lifecycle state changed. paused={paused} reason={reason}", this);

            if (paused)
            {
                StopTransportOnly();
                return;
            }

            RequestAutoRecoveryRefresh("应用已恢复，正在重新发现主机...");
        }

        private void RequestAutoRecoveryRefresh(string message)
        {
            m_AutoRecoveryRefreshMessage = string.IsNullOrWhiteSpace(message)
                ? "正在重新发现主机..."
                : message.Trim();
            Interlocked.Exchange(ref m_AutoRecoveryRefreshRequested, 1);
        }

        private void ProcessAutoRecoveryRefreshRequests()
        {
            if (Interlocked.Exchange(ref m_AutoRecoveryRefreshRequested, 0) == 0)
                return;

            if (m_IsLifecyclePaused)
                return;

            string message = string.IsNullOrWhiteSpace(m_AutoRecoveryRefreshMessage)
                ? "正在重新发现主机..."
                : m_AutoRecoveryRefreshMessage;
            RefreshBonjourAndReconnectInternal(message);
        }

        private void UpsertDiscoveredBonjourHost(SpectatorBonjourEvent nextEvent)
        {
            if (nextEvent == null)
                return;

            string key = BuildBonjourHostKey(nextEvent.serviceName, nextEvent.serviceType);
            SpectatorBonjourHostInfo host = FindDiscoveredBonjourHostByKey(key);
            bool changed = false;

            if (host == null)
            {
                host = new SpectatorBonjourHostInfo
                {
                    Key = key,
                    serviceName = nextEvent.serviceName ?? string.Empty,
                    serviceType = nextEvent.serviceType ?? string.Empty,
                    hostName = nextEvent.hostName ?? string.Empty,
                    port = nextEvent.port,
                };
                m_DiscoveredBonjourHosts.Add(host);
                changed = true;
            }
            else
            {
                changed |= UpdateBonjourHostField(ref host.serviceName, nextEvent.serviceName);
                changed |= UpdateBonjourHostField(ref host.serviceType, nextEvent.serviceType);
                changed |= UpdateBonjourHostField(ref host.hostName, nextEvent.hostName);
                if (host.port != nextEvent.port && nextEvent.port >= 0)
                {
                    host.port = nextEvent.port;
                    changed = true;
                }
            }

            if (!changed)
                return;

            SortDiscoveredBonjourHosts();
            RefreshResolvedEndpointFromBonjourSelection();
            m_BonjourHostsRevision++;

            if (CountResolvedBonjourHosts() > 1 && string.IsNullOrWhiteSpace(m_SelectedBonjourHostKey))
            {
                SetStatus($"已发现 {CountResolvedBonjourHosts()} 台主机，请从列表中选择。");
            }
            else if (TryGetResolvedSelectedBonjourEndpoint(out string hostName, out int port))
            {
                SetStatus($"已发现主机: {hostName}:{port}");
            }
        }

        private void RemoveDiscoveredBonjourHost(SpectatorBonjourEvent nextEvent)
        {
            if (nextEvent == null)
                return;

            string key = BuildBonjourHostKey(nextEvent.serviceName, nextEvent.serviceType);
            int index = FindDiscoveredBonjourHostIndexByKey(key);
            if (index < 0)
                return;

            bool removedSelectedHost = string.Equals(m_DiscoveredBonjourHosts[index].Key, m_SelectedBonjourHostKey, StringComparison.Ordinal);
            m_DiscoveredBonjourHosts.RemoveAt(index);

            if (removedSelectedHost)
                m_SelectedBonjourHostKey = string.Empty;

            RefreshResolvedEndpointFromBonjourSelection();
            m_BonjourHostsRevision++;

            if (m_DiscoveredBonjourHosts.Count == 0)
                SetStatus("等待通过 Bonjour 发现主机...");
            else if (CountResolvedBonjourHosts() > 1 && string.IsNullOrWhiteSpace(m_SelectedBonjourHostKey))
                SetStatus($"已发现 {CountResolvedBonjourHosts()} 台主机，请从列表中选择。");
        }

        private void ClearDiscoveredBonjourHosts(bool keepSelection)
        {
            if (m_DiscoveredBonjourHosts.Count == 0 && (keepSelection || string.IsNullOrWhiteSpace(m_SelectedBonjourHostKey)))
                return;

            m_DiscoveredBonjourHosts.Clear();
            if (!keepSelection)
                m_SelectedBonjourHostKey = string.Empty;

            m_ResolvedHost = string.Empty;
            m_ResolvedPort = 0;
            m_BonjourHostsRevision++;
        }

        private void RefreshResolvedEndpointFromBonjourSelection()
        {
            SpectatorBonjourHostInfo preferredHost = FindSelectedResolvedBonjourHost();
            if (preferredHost != null)
            {
                m_ResolvedHost = preferredHost.hostName;
                m_ResolvedPort = preferredHost.port;
                return;
            }

            if (CountResolvedBonjourHosts() == 1)
            {
                SpectatorBonjourHostInfo onlyResolvedHost = FindFirstResolvedBonjourHost();
                m_ResolvedHost = onlyResolvedHost != null ? onlyResolvedHost.hostName : string.Empty;
                m_ResolvedPort = onlyResolvedHost != null ? onlyResolvedHost.port : 0;
                return;
            }

            m_ResolvedHost = string.Empty;
            m_ResolvedPort = 0;
        }

        private bool TryGetResolvedSelectedBonjourEndpoint(out string host, out int port)
        {
            SpectatorBonjourHostInfo preferredHost = FindSelectedResolvedBonjourHost();
            if (preferredHost != null)
            {
                host = preferredHost.hostName;
                port = preferredHost.port;
                return true;
            }

            if (CountResolvedBonjourHosts() == 1)
            {
                SpectatorBonjourHostInfo onlyResolvedHost = FindFirstResolvedBonjourHost();
                if (onlyResolvedHost != null)
                {
                    host = onlyResolvedHost.hostName;
                    port = onlyResolvedHost.port;
                    return true;
                }
            }

            host = string.Empty;
            port = 0;
            return false;
        }

        private SpectatorBonjourHostInfo FindSelectedResolvedBonjourHost()
        {
            if (!string.IsNullOrWhiteSpace(m_SelectedBonjourHostKey))
            {
                SpectatorBonjourHostInfo selectedHost = FindDiscoveredBonjourHostByKey(m_SelectedBonjourHostKey);
                if (selectedHost != null && selectedHost.HasResolvedEndpoint)
                    return selectedHost;
            }

            return null;
        }

        private SpectatorBonjourHostInfo FindFirstResolvedBonjourHost()
        {
            for (int i = 0; i < m_DiscoveredBonjourHosts.Count; i++)
            {
                SpectatorBonjourHostInfo host = m_DiscoveredBonjourHosts[i];
                if (host != null && host.HasResolvedEndpoint)
                    return host;
            }

            return null;
        }

        private int CountResolvedBonjourHosts()
        {
            int count = 0;
            for (int i = 0; i < m_DiscoveredBonjourHosts.Count; i++)
            {
                SpectatorBonjourHostInfo host = m_DiscoveredBonjourHosts[i];
                if (host != null && host.HasResolvedEndpoint)
                    count++;
            }

            return count;
        }

        private SpectatorBonjourHostInfo FindDiscoveredBonjourHostByKey(string key)
        {
            int index = FindDiscoveredBonjourHostIndexByKey(key);
            return index >= 0 ? m_DiscoveredBonjourHosts[index] : null;
        }

        private int FindDiscoveredBonjourHostIndexByKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return -1;

            for (int i = 0; i < m_DiscoveredBonjourHosts.Count; i++)
            {
                SpectatorBonjourHostInfo host = m_DiscoveredBonjourHosts[i];
                if (host != null && string.Equals(host.Key, key, StringComparison.Ordinal))
                    return i;
            }

            return -1;
        }

        private void SortDiscoveredBonjourHosts()
        {
            m_DiscoveredBonjourHosts.Sort(static (left, right) =>
            {
                string leftName = left != null ? left.DisplayName : string.Empty;
                string rightName = right != null ? right.DisplayName : string.Empty;
                return string.Compare(leftName, rightName, StringComparison.OrdinalIgnoreCase);
            });
        }

        private static bool UpdateBonjourHostField(ref string currentValue, string nextValue)
        {
            string normalizedNext = nextValue ?? string.Empty;
            if (string.Equals(currentValue, normalizedNext, StringComparison.Ordinal))
                return false;

            currentValue = normalizedNext;
            return true;
        }

        private static string BuildBonjourHostKey(string serviceName, string serviceType)
        {
            string normalizedServiceName = string.IsNullOrWhiteSpace(serviceName) ? "unknown" : serviceName.Trim();
            string normalizedServiceType = string.IsNullOrWhiteSpace(serviceType) ? "unknown" : serviceType.Trim();
            return $"{normalizedServiceType}::{normalizedServiceName}";
        }

        private static string FormatPoseForLog(Pose pose)
        {
            return $"pos={pose.position}, rotEuler={pose.rotation.eulerAngles}";
        }
    }

    public sealed class SpectatorManualConnectOverlay : MonoBehaviour
    {
        private const float MobileUiScale = 1.75f;
        private const float DesktopUiScale = 1.0f;

        private SpectatorClientService m_Client;
        private TouchScreenKeyboard m_HostKeyboard;
        private TouchScreenKeyboard m_PortKeyboard;
        private string m_HostDraft = string.Empty;
        private string m_PortDraft = "45830";
        private bool m_Collapsed;
        private Vector2 m_ScrollPosition;
        private GUIStyle m_WindowLabelStyle;
        private GUIStyle m_BodyLabelStyle;
        private GUIStyle m_TextFieldStyle;
        private GUIStyle m_ButtonStyle;

        public void Initialize(SpectatorClientService client)
        {
            m_Client = client;
            SyncDraftFromClient();
            EnsureDraftDefaults();
        }

        private void Update()
        {
            PumpKeyboard(ref m_HostKeyboard, ref m_HostDraft, normalizePort: false);
            PumpKeyboard(ref m_PortKeyboard, ref m_PortDraft, normalizePort: true);
            EnsureDraftDefaults();
        }

        private void OnGUI()
        {
            if (m_Client == null)
                return;

            EnsureStyles();
            EnsureDraftDefaults();

            Rect safeArea = Screen.safeArea;
            float uiScale = Application.isMobilePlatform ? MobileUiScale : DesktopUiScale;
            float width = Mathf.Min(720f, (safeArea.width - 16f) / uiScale);
            float maxHeight = Mathf.Max(180f, (safeArea.height - 16f) / uiScale);
            float height = m_Collapsed ? 88f : Mathf.Min(420f, maxHeight);
            Rect area = new Rect((safeArea.x + 12f) / uiScale, (safeArea.y + 12f) / uiScale, width, height);

            Matrix4x4 previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(uiScale, uiScale, 1f));

            GUILayout.BeginArea(area, GUI.skin.window);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Spectator Connect", m_WindowLabelStyle);
            if (GUILayout.Button(m_Collapsed ? "Expand" : "Collapse", m_ButtonStyle, GUILayout.Width(168f), GUILayout.Height(60f)))
                m_Collapsed = !m_Collapsed;
            GUILayout.EndHorizontal();

            if (m_Collapsed)
            {
                GUILayout.EndArea();
                GUI.matrix = previousMatrix;
                return;
            }

            float scrollHeight = Mathf.Max(120f, area.height - 78f);
            m_ScrollPosition = GUILayout.BeginScrollView(m_ScrollPosition, false, true, GUILayout.Height(scrollHeight));

            GUILayout.Label($"Status: {m_Client.LastStatusMessage}", m_BodyLabelStyle);
            string rootState = !m_Client.HasRoot
                ? "missing"
                : m_Client.IsRootLocked ? "locked" : "unlocked";
            GUILayout.Label($"Root: {rootState}", m_BodyLabelStyle);

            string discovered = !string.IsNullOrWhiteSpace(m_Client.ResolvedHost)
                ? $"{m_Client.ResolvedHost}:{m_Client.ResolvedPort}"
                : "not found";
            GUILayout.Label($"Bonjour: {discovered}", m_BodyLabelStyle);
            GUILayout.Label("Placement: tap Create Root to spawn the plan root marker and anchor it. Unlock Root when you want to adjust it, then lock again to re-anchor.", m_BodyLabelStyle);

            if (Application.isEditor)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Host", m_BodyLabelStyle, GUILayout.Width(64f));
                m_HostDraft = GUILayout.TextField(m_HostDraft, m_TextFieldStyle, GUILayout.Height(52f));
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("Port", m_BodyLabelStyle, GUILayout.Width(64f));
                m_PortDraft = GUILayout.TextField(m_PortDraft, m_TextFieldStyle, GUILayout.Height(52f));
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.Label($"Manual Host: {m_HostDraft}", m_BodyLabelStyle);
                GUILayout.Label($"Manual Port: {m_PortDraft}", m_BodyLabelStyle);

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Edit Host", m_ButtonStyle, GUILayout.Height(70f)))
                    m_HostKeyboard = TouchScreenKeyboard.Open(m_HostDraft, TouchScreenKeyboardType.URL, false, false, false, false, "Host IP");
                if (GUILayout.Button("Edit Port", m_ButtonStyle, GUILayout.Height(70f)))
                    m_PortKeyboard = TouchScreenKeyboard.Open(m_PortDraft, TouchScreenKeyboardType.NumberPad, false, false, false, false, "Port");
                GUILayout.EndHorizontal();
            }

            bool canAdjustRoot = m_Client.HasRoot;

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(m_Client.HasRoot ? "Recreate Root" : "Create Root", m_ButtonStyle, GUILayout.Height(70f)))
                m_Client.CreateOrRecreateRoot();

            GUI.enabled = canAdjustRoot;
            if (GUILayout.Button(m_Client.IsRootLocked ? "Unlock Root" : "Lock Root", m_ButtonStyle, GUILayout.Height(70f)))
                m_Client.SetRootLocked(!m_Client.IsRootLocked);

            if (GUILayout.Button("Reset Root", m_ButtonStyle, GUILayout.Height(70f)))
                m_Client.ResetRootPose();
            GUILayout.EndHorizontal();
            GUI.enabled = canAdjustRoot;

            GUILayout.BeginHorizontal();
            GUI.enabled = m_Client.SupportsWorldMapBackupTransfer && m_Client.CanExportWorldMapBackup;
            if (GUILayout.Button("Export Backup", m_ButtonStyle, GUILayout.Height(66f)))
                m_Client.ExportWorldMapBackup();
            GUI.enabled = m_Client.SupportsWorldMapBackupTransfer;
            if (GUILayout.Button("Import Backup", m_ButtonStyle, GUILayout.Height(66f)))
                m_Client.ImportWorldMapBackup();
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUI.enabled = m_Client.CanExportLayoutJson;
            if (GUILayout.Button("Export Layout", m_ButtonStyle, GUILayout.Height(66f)))
                m_Client.ExportCurrentLayoutJson();
            GUI.enabled = m_Client.CanImportLayoutJson;
            if (GUILayout.Button("Import Layout", m_ButtonStyle, GUILayout.Height(66f)))
                m_Client.ImportLayoutJson();
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Rotate Left", m_ButtonStyle, GUILayout.Height(64f)))
                m_Client.NudgeRootYaw(clockwise: false);
            if (GUILayout.Button("Forward", m_ButtonStyle, GUILayout.Height(64f)))
                m_Client.NudgeRootForward(forward: true);
            if (GUILayout.Button("Rotate Right", m_ButtonStyle, GUILayout.Height(64f)))
                m_Client.NudgeRootYaw(clockwise: true);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Left", m_ButtonStyle, GUILayout.Height(64f)))
                m_Client.NudgeRootRight(right: false);
            if (GUILayout.Button("Back", m_ButtonStyle, GUILayout.Height(64f)))
                m_Client.NudgeRootForward(forward: false);
            if (GUILayout.Button("Right", m_ButtonStyle, GUILayout.Height(64f)))
                m_Client.NudgeRootRight(right: true);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Down", m_ButtonStyle, GUILayout.Height(64f)))
                m_Client.NudgeRootUp(up: false);
            if (GUILayout.Button("Up", m_ButtonStyle, GUILayout.Height(64f)))
                m_Client.NudgeRootUp(up: true);
            GUILayout.EndHorizontal();
            GUI.enabled = true;

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Save Host", m_ButtonStyle, GUILayout.Height(66f)))
            {
                if (TryGetConnectPort(out int port))
                {
                    m_Client.SaveManualEndpoint(m_HostDraft, port);
                    SyncDraftFromClient();
                }
            }

            if (GUILayout.Button("Use Bonjour", m_ButtonStyle, GUILayout.Height(66f)))
            {
                m_Client.ClearManualEndpoint();
                SyncDraftFromClient();
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            int connectPort = 0;
            bool canConnectManual = !string.IsNullOrWhiteSpace(m_HostDraft) && TryGetConnectPort(out connectPort);
            GUI.enabled = canConnectManual;
            if (GUILayout.Button("Connect", m_ButtonStyle, GUILayout.Height(74f)))
                m_Client.ConnectToManualEndpoint(m_HostDraft, connectPort);
            GUI.enabled = true;

            if (GUILayout.Button("Reconnect", m_ButtonStyle, GUILayout.Height(74f)))
                m_Client.ConnectNow();

            if (GUILayout.Button("Disconnect", m_ButtonStyle, GUILayout.Height(74f)))
                m_Client.Disconnect();
            GUILayout.EndHorizontal();

            if (!Application.isEditor)
            {
                GUILayout.Space(8f);
                if (GUILayout.Button("Open iPhone Settings", m_ButtonStyle, GUILayout.Height(66f)))
                    Application.OpenURL("app-settings:");
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
            GUI.matrix = previousMatrix;
        }

        private void EnsureStyles()
        {
            if (m_WindowLabelStyle == null)
            {
                m_WindowLabelStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 26,
                    fontStyle = FontStyle.Bold,
                    wordWrap = true,
                };
            }

            if (m_BodyLabelStyle == null)
            {
                m_BodyLabelStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 22,
                    wordWrap = true,
                };
            }

            if (m_TextFieldStyle == null)
            {
                m_TextFieldStyle = new GUIStyle(GUI.skin.textField)
                {
                    fontSize = 22,
                };
            }

            if (m_ButtonStyle == null)
            {
                m_ButtonStyle = new GUIStyle(GUI.skin.button)
                {
                    fontSize = 22,
                    wordWrap = true,
                };
            }
        }

        private void PumpKeyboard(ref TouchScreenKeyboard keyboard, ref string target, bool normalizePort)
        {
            if (keyboard == null)
                return;

            if (keyboard.status == TouchScreenKeyboard.Status.Visible)
                return;

            if (keyboard.status == TouchScreenKeyboard.Status.Done)
            {
                target = keyboard.text ?? string.Empty;
                if (normalizePort && int.TryParse(target, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int parsed))
                    target = Mathf.Clamp(parsed, 1, 65535).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            keyboard = null;
        }

        private bool TryParsePort(out int port)
        {
            if (!int.TryParse(m_PortDraft, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out port))
                return false;

            return port > 0 && port <= 65535;
        }

        private bool TryGetConnectPort(out int port)
        {
            if (TryParsePort(out port))
                return true;

            if (m_Client != null && m_Client.ManualPortOverride > 0 && m_Client.ManualPortOverride <= 65535)
            {
                port = m_Client.ManualPortOverride;
                m_PortDraft = port.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return true;
            }

            port = 45830;
            m_PortDraft = port.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }

        private void EnsureDraftDefaults()
        {
            if (m_Client == null)
                return;

            if (string.IsNullOrWhiteSpace(m_HostDraft) && !string.IsNullOrWhiteSpace(m_Client.ManualHostOverride))
                m_HostDraft = m_Client.ManualHostOverride;

            if (!TryParsePort(out _))
                m_PortDraft = (m_Client.ManualPortOverride > 0 ? m_Client.ManualPortOverride : 45830)
                    .ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private void SyncDraftFromClient()
        {
            if (m_Client == null)
                return;

            m_HostDraft = m_Client.ManualHostOverride;
            m_PortDraft = m_Client.ManualPortOverride.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
