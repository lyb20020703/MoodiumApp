using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Autohand;
using Interaction;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Hands;

namespace VFXViewer
{
    public sealed partial class SpectatorHostService : MonoBehaviour
    {
        private sealed class ClientConnection
        {
            public TcpClient tcpClient;
            public CancellationTokenSource cancellation;
            public Task readLoopTask;
            public bool receivedHello;
        }

        [SerializeField] private string serviceType = "_vfxviewer-spectator._tcp";
        [SerializeField] private int port = 45830;
        [SerializeField] private float snapshotIntervalSeconds = 0.1f;
        [SerializeField] private float heartbeatIntervalSeconds = 1f;

        private readonly List<ClientConnection> m_Clients = new List<ClientConnection>();
        private readonly List<DeltaEvent> m_PendingDeltaEvents = new List<DeltaEvent>();

        private TcpListener m_Listener;
        private CancellationTokenSource m_ListenerCancellation;
        private Task m_AcceptLoopTask;
        private SpectatorBonjourAdvertiser m_Advertiser;

        private ExhibitPlacementManager m_PlacementManager;
        private ChapterPlacementDirector m_Director;
        private string m_SessionId;
        private long m_NextEventId = 1;
        private float m_NextSnapshotAt;
        private float m_NextHeartbeatAt;
        private string m_LastLayoutJson = string.Empty;
        private string m_LastPlaybackJson = string.Empty;
        private string m_LastHandJson = string.Empty;
        private int m_LayoutRevision;
        private int m_PlaybackRevision;
        private int m_HandRevision;
        private int m_LeftHandSequence;
        private int m_RightHandSequence;
        private bool m_IsLifecyclePaused;
        private string m_ActiveSceneName = string.Empty;

        private void OnEnable()
        {
            if (SpectatorRuntimeRole.CurrentRole != AppRole.VisionHost || !PlatformRuntime.SupportsVisionHostFeatures)
            {
                enabled = false;
                return;
            }

            RefreshActiveSceneNameCache();
            SceneManager.activeSceneChanged += HandleActiveSceneChanged;
            ResolveReferences();
            StartHost();
        }

        private void OnDisable()
        {
            SceneManager.activeSceneChanged -= HandleActiveSceneChanged;
            StopHost();
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
            if (m_IsLifecyclePaused || m_Listener == null)
                return;

            ProcessPendingClientCommands();

            if (Time.unscaledTime >= m_NextSnapshotAt)
            {
                m_NextSnapshotAt = Time.unscaledTime + Mathf.Max(0.03f, snapshotIntervalSeconds);
                CaptureAndQueueDeltaEvents();
                if (m_PendingDeltaEvents.Count > 0)
                    BroadcastDeltaEvents();
            }

            if (Time.unscaledTime >= m_NextHeartbeatAt)
            {
                m_NextHeartbeatAt = Time.unscaledTime + Mathf.Max(0.2f, heartbeatIntervalSeconds);
                BroadcastHeartbeat();
            }

            CleanupDisconnectedClients();
        }

        private void ResolveReferences()
        {
            m_PlacementManager = FindFirstObjectByType<ExhibitPlacementManager>(FindObjectsInactive.Include);
            m_Director = FindFirstObjectByType<ChapterPlacementDirector>(FindObjectsInactive.Include);
            m_SessionId = Guid.NewGuid().ToString("N");
        }

        private void HandleLifecyclePauseChanged(bool paused, string reason)
        {
            if (SpectatorRuntimeRole.CurrentRole != AppRole.VisionHost || !PlatformRuntime.SupportsVisionHostFeatures)
                return;

            if (m_IsLifecyclePaused == paused)
                return;

            m_IsLifecyclePaused = paused;
            Debug.Log($"[SpectatorHost] Lifecycle state changed. paused={paused} reason={reason}", this);

            if (paused)
            {
                StopHost();
                return;
            }

            ResolveReferences();
            StartHost();
            m_NextSnapshotAt = 0f;
            m_NextHeartbeatAt = 0f;
        }

        private void StartHost()
        {
            if (m_Listener != null)
                return;

            try
            {
                m_ListenerCancellation = new CancellationTokenSource();
                m_Listener = new TcpListener(IPAddress.Any, port);
                m_Listener.Start();
                m_AcceptLoopTask = AcceptLoopAsync(m_ListenerCancellation.Token);

                m_Advertiser = new SpectatorBonjourAdvertiser();
                m_Advertiser.Start(SystemInfo.deviceName, serviceType, port);
                string ipSummary = GetLocalIpv4Summary(out bool hasUsableLanIpv4);
                if (string.IsNullOrEmpty(ipSummary))
                {
                    Debug.Log($"[SpectatorHost] Started on port {port}. No IPv4 address detected yet.", this);
                }
                else if (hasUsableLanIpv4)
                {
                    Debug.Log($"[SpectatorHost] Started on port {port}. Local IPv4: {ipSummary}", this);
                }
                else
                {
                    Debug.LogWarning(
                        $"[SpectatorHost] Started on port {port}, but no usable LAN IPv4 was found. Detected non-routable IPv4: {ipSummary}. Manual IP connect will not work until Vision Pro joins a normal Wi-Fi network.",
                        this);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SpectatorHost] Failed to start: {ex.Message}", this);
                StopHost();
            }
        }

        private static string GetLocalIpv4Summary(out bool hasUsableLanIpv4)
        {
            hasUsableLanIpv4 = false;
            try
            {
                var lanAddresses = new List<string>();
                var fallbackAddresses = new List<string>();
                foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (networkInterface == null ||
                        networkInterface.OperationalStatus != OperationalStatus.Up ||
                        networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    {
                        continue;
                    }

                    IPInterfaceProperties properties = networkInterface.GetIPProperties();
                    if (properties == null)
                        continue;

                    foreach (UnicastIPAddressInformation unicast in properties.UnicastAddresses)
                    {
                        IPAddress address = unicast?.Address;
                        if (address == null || address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address))
                            continue;

                        string addressText = address.ToString();
                        if (IsRfc1918PrivateAddress(address))
                        {
                            if (!lanAddresses.Contains(addressText))
                                lanAddresses.Add(addressText);
                            continue;
                        }

                        if (IsLinkLocalAddress(address))
                            continue;

                        if (!fallbackAddresses.Contains(addressText))
                            fallbackAddresses.Add(addressText);
                    }
                }

                if (lanAddresses.Count > 0)
                {
                    hasUsableLanIpv4 = true;
                    return string.Join(", ", lanAddresses);
                }

                return fallbackAddresses.Count > 0 ? string.Join(", ", fallbackAddresses) : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool IsLinkLocalAddress(IPAddress address)
        {
            byte[] bytes = address.GetAddressBytes();
            return bytes.Length == 4 && bytes[0] == 169 && bytes[1] == 254;
        }

        private static bool IsRfc1918PrivateAddress(IPAddress address)
        {
            byte[] bytes = address.GetAddressBytes();
            if (bytes.Length != 4)
                return false;

            if (bytes[0] == 10)
                return true;

            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                return true;

            return bytes[0] == 192 && bytes[1] == 168;
        }

        private void StopHost()
        {
            m_Advertiser?.Dispose();
            m_Advertiser = null;

            if (m_ListenerCancellation != null)
            {
                m_ListenerCancellation.Cancel();
                m_ListenerCancellation.Dispose();
                m_ListenerCancellation = null;
            }

            if (m_Listener != null)
            {
                m_Listener.Stop();
                m_Listener = null;
            }

            for (int i = 0; i < m_Clients.Count; i++)
                DisconnectClient(m_Clients[i]);
            m_Clients.Clear();
            m_PendingDeltaEvents.Clear();
        }

        private async Task AcceptLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && m_Listener != null)
            {
                TcpClient tcpClient = null;
                try
                {
                    tcpClient = await m_Listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    tcpClient.NoDelay = true;

                    var connection = new ClientConnection
                    {
                        tcpClient = tcpClient,
                        cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken),
                    };

                    lock (m_Clients)
                        m_Clients.Add(connection);

                    Debug.Log($"[SpectatorHost] TCP client connected from {tcpClient.Client.RemoteEndPoint}", this);
                    connection.readLoopTask = HandleClientAsync(connection);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (!cancellationToken.IsCancellationRequested)
                        Debug.LogWarning($"[SpectatorHost] Accept failed: {ex.Message}", this);
                    tcpClient?.Close();
                }
            }
        }

        private async Task HandleClientAsync(ClientConnection connection)
        {
            try
            {
                NetworkStream stream = connection.tcpClient.GetStream();
                while (!connection.cancellation.IsCancellationRequested && connection.tcpClient.Connected)
                {
                    string frame = await SpectatorTransport.ReadFrameAsync(stream, connection.cancellation.Token).ConfigureAwait(false);
                    if (frame == null)
                        break;

                    if (!SpectatorProtocol.TryDeserializeEnvelope(frame, out SpectatorEnvelope envelope))
                        continue;

                    SpectatorMessageKind kind = (SpectatorMessageKind)envelope.kind;
                    if (kind == SpectatorMessageKind.Hello)
                    {
                        connection.receivedHello = true;
                        Debug.Log($"[SpectatorHost] Received hello from {connection.tcpClient.Client.RemoteEndPoint}", this);
                        await SendHelloAsync(connection).ConfigureAwait(false);
                        await SendFullSceneSnapshotAsync(connection).ConfigureAwait(false);
                        continue;
                    }

                    if (kind == SpectatorMessageKind.Command)
                    {
                        EnqueueClientCommand(
                            connection,
                            SpectatorProtocol.DeserializePayload<SpectatorCommandMessage>(envelope.jsonPayload));
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.Log($"[SpectatorHost] Client disconnected: {ex.Message}", this);
            }
            finally
            {
                DisconnectClient(connection);
            }
        }

        private async Task SendHelloAsync(ClientConnection connection)
        {
            if (connection == null || connection.tcpClient == null || !connection.tcpClient.Connected)
                return;

            var hello = new SpectatorHello
            {
                protocolVersion = 1,
                assetManifestVersion = "1",
                deviceRole = AppRole.VisionHost.ToString(),
                sessionId = m_SessionId,
                sceneName = CurrentSceneName,
            };

            await SpectatorTransport.WriteEnvelopeAsync(
                connection.tcpClient.GetStream(),
                SpectatorMessageKind.Hello,
                hello,
                connection.cancellation.Token).ConfigureAwait(false);
        }

        private async Task SendFullSceneSnapshotAsync(ClientConnection connection)
        {
            if (connection == null || connection.tcpClient == null || !connection.tcpClient.Connected)
                return;

            FullSceneSnapshot snapshot = CaptureFullSnapshot();
            await SpectatorTransport.WriteEnvelopeAsync(
                connection.tcpClient.GetStream(),
                SpectatorMessageKind.FullSceneSnapshot,
                snapshot,
                connection.cancellation.Token).ConfigureAwait(false);
        }

        private void CaptureAndQueueDeltaEvents()
        {
            long now = SpectatorProtocol.NowUnixMilliseconds();

            SharedLayoutSnapshot layout = m_PlacementManager != null
                ? m_PlacementManager.CaptureSharedLayoutSnapshot(m_LayoutRevision)
                : new SharedLayoutSnapshot();
            QueueSnapshotDeltaIfChanged(ref m_LastLayoutJson, ref m_LayoutRevision, SpectatorDeltaKind.LayoutChanged, layout, now);

            PlaybackSnapshot playback = CapturePlaybackSnapshot(now);
            QueueSnapshotDeltaIfChanged(ref m_LastPlaybackJson, ref m_PlaybackRevision, SpectatorDeltaKind.PlaybackChanged, playback, now);

            var hands = CaptureRemoteHands(now);
            var handBatch = new RemoteHandBatch { remoteHands = hands };
            QueueSnapshotDeltaIfChanged(ref m_LastHandJson, ref m_HandRevision, SpectatorDeltaKind.HandStateChanged, handBatch, now);
        }

        private void QueueSnapshotDeltaIfChanged<T>(
            ref string lastJson,
            ref int revision,
            SpectatorDeltaKind deltaKind,
            T payload,
            long hostTimestamp)
        {
            if (payload == null)
                return;

            string nextJson = JsonUtility.ToJson(payload);
            if (string.Equals(lastJson, nextJson, StringComparison.Ordinal))
                return;

            revision++;
            ApplyRevision(payload, revision);
            nextJson = JsonUtility.ToJson(payload);
            lastJson = nextJson;

            m_PendingDeltaEvents.Add(new DeltaEvent
            {
                eventId = m_NextEventId++,
                kind = (int)deltaKind,
                payloadJson = nextJson,
                hostTimestamp = hostTimestamp,
            });
        }

        private static void ApplyRevision<T>(T payload, int revision)
        {
            switch (payload)
            {
                case SharedLayoutSnapshot layout:
                    layout.layoutRevision = revision;
                    break;
                case PlaybackSnapshot playback:
                    playback.playbackRevision = revision;
                    break;
                case RemoteHandBatch:
                    break;
            }
        }

        private PlaybackSnapshot CapturePlaybackSnapshot(long hostTimestamp)
        {
            var moduleStates = new List<ModuleRemoteStateSnapshot>();
            var activeModuleIds = new List<string>();
            var modules = InteractionModule.AllInstances;
            for (int i = 0; i < modules.Count; i++)
            {
                InteractionModule module = modules[i];
                if (module == null || !module.gameObject.scene.IsValid())
                    continue;

                string moduleId = module.ModuleId;
                if (string.IsNullOrWhiteSpace(moduleId))
                    continue;

                ModuleRemoteStateSnapshot state = module.CaptureRemotePlaybackState(0L);
                moduleStates.Add(state);
                if (module.CurrentPhase != InteractionPhase.Start || module.PendingEndWhileAppearing || module.PendingStartWhileInitial)
                    activeModuleIds.Add(moduleId);
            }

            moduleStates.Sort((left, right) => string.Compare(left.moduleId, right.moduleId, StringComparison.OrdinalIgnoreCase));
            activeModuleIds.Sort(StringComparer.OrdinalIgnoreCase);

            return new PlaybackSnapshot
            {
                playbackRevision = m_PlaybackRevision,
                appMode = (int)(m_Director != null ? m_Director.CurrentAppMode : ExhibitAppMode.Placement),
                currentFlatIndex = m_Director != null ? m_Director.SpectatorCurrentFlatIndex : 0,
                currentGroupIndex = m_Director != null ? m_Director.SpectatorCurrentGroupIndex : 0,
                activeModuleIds = activeModuleIds.ToArray(),
                moduleStates = moduleStates.ToArray(),
            };
        }

        private RemoteHandState[] CaptureRemoteHands(long hostTimestamp)
        {
            var hands = FindObjectsByType<Hand>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            Hand bestLeftHand = null;
            Hand bestRightHand = null;
            RemoteHandState left = null;
            RemoteHandState right = null;
            for (int i = 0; i < hands.Length; i++)
            {
                Hand hand = hands[i];
                if (!IsRuntimeDrivenHand(hand))
                    continue;

                RemoteHandState next = CaptureRemoteHand(hand, hostTimestamp);
                if (next == null)
                    continue;

                if ((SpectatorHandedness)next.handedness == SpectatorHandedness.Left)
                {
                    if (left == null || IsPreferredRemoteHandState(hand, next, bestLeftHand, left))
                    {
                        bestLeftHand = hand;
                        left = next;
                    }
                }

                if ((SpectatorHandedness)next.handedness == SpectatorHandedness.Right)
                {
                    if (right == null || IsPreferredRemoteHandState(hand, next, bestRightHand, right))
                    {
                        bestRightHand = hand;
                        right = next;
                    }
                }
            }

            var result = new List<RemoteHandState>(2);
            if (left != null)
                result.Add(left);
            if (right != null)
                result.Add(right);
            return result.ToArray();
        }

        private RemoteHandState CaptureRemoteHand(Hand hand, long hostTimestamp)
        {
            if (hand == null)
                return null;

            var tracking = hand.GetComponent<OpenXRAutoHandTracking>();
            bool isTracked =
                (tracking != null && tracking.handTrackingActive) ||
                HasVisibleRenderer(hand) ||
                hand.enableMovement;

            string heldInstanceId = string.Empty;
            bool hasHeldObjectRelativePose = false;
            SerializablePose heldObjectRelativePose = default;
            if (hand.holdingObj != null)
            {
                ExhibitInfo info = hand.holdingObj.GetComponentInParent<ExhibitInfo>(true);
                if (info != null && !string.IsNullOrWhiteSpace(info.instanceID))
                {
                    heldInstanceId = info.instanceID.Trim();

                    if (TryCaptureRelativePose(info.transform, out Pose relativePose))
                    {
                        hasHeldObjectRelativePose = true;
                        heldObjectRelativePose = new SerializablePose(relativePose);
                    }
                }
            }

            var state = new RemoteHandState
            {
                handedness = hand.left ? (int)SpectatorHandedness.Left : (int)SpectatorHandedness.Right,
                isTracked = isTracked,
                rootPose = new SerializablePose(new Pose(hand.transform.position, hand.transform.rotation)),
                wristPose = new SerializablePose(new Pose(
                    hand.palmTransform != null ? hand.palmTransform.position : hand.transform.position,
                    hand.palmTransform != null ? hand.palmTransform.rotation : hand.transform.rotation)),
                fingerCurls = CaptureFingerCurls(hand),
                isGrabbing = hand.IsGrabbing() || hand.IsHolding(),
                heldObjectInstanceId = heldInstanceId,
                hasHeldObjectRelativePose = hasHeldObjectRelativePose,
                heldObjectRelativePose = heldObjectRelativePose,
                sequence = hand.left ? ++m_LeftHandSequence : ++m_RightHandSequence,
                hostTimestamp = hostTimestamp,
            };

            return state;
        }

        private bool TryCaptureRelativePose(Transform target, out Pose relativePose)
        {
            relativePose = default;
            if (target == null || m_PlacementManager == null || !m_PlacementManager.TryGetSpectatorRootPose(out Pose rootPose))
                return false;

            Quaternion inverseRootRotation = Quaternion.Inverse(rootPose.rotation);
            Vector3 localPosition = inverseRootRotation * (target.position - rootPose.position);
            Quaternion localRotation = inverseRootRotation * target.rotation;
            relativePose = new Pose(localPosition, localRotation);
            return true;
        }

        private static bool IsRuntimeDrivenHand(Hand hand)
        {
            if (hand == null)
                return false;

            string handName = hand.gameObject.name;
            if (handName.Contains("Projection", StringComparison.OrdinalIgnoreCase))
                return false;

            return hand.follow != null || hand.palmTransform != null;
        }

        private static bool HasVisibleRenderer(Hand hand)
        {
            if (hand == null)
                return false;

            Renderer[] renderers = hand.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                    continue;

                if (!renderer.enabled)
                    continue;

                if (!renderer.gameObject.activeInHierarchy)
                    continue;

                return true;
            }

            return false;
        }

        private static bool IsPreferredRemoteHandState(
            Hand candidateHand,
            RemoteHandState candidateState,
            Hand currentHand,
            RemoteHandState currentState)
        {
            int candidatePriority = GetRuntimeHandPriority(candidateHand, candidateState);
            int currentPriority = GetRuntimeHandPriority(currentHand, currentState);
            return candidatePriority > currentPriority;
        }

        private static int GetRuntimeHandPriority(Hand hand, RemoteHandState state)
        {
            if (hand == null)
                return int.MinValue;

            int priority = GetRuntimeHandPriority(state);
            string handName = hand.gameObject.name;
            string followName = hand.follow != null ? hand.follow.name : string.Empty;

            if (hand.gameObject.activeSelf)
                priority += 100;
            if (hand.gameObject.activeInHierarchy)
                priority += 20;
            if (handName.Contains("RobotHand", StringComparison.OrdinalIgnoreCase))
                priority += 1000;
            if (followName.Contains("OpenXR", StringComparison.OrdinalIgnoreCase))
                priority += 300;
            if (handName.Contains("Classic Hand", StringComparison.OrdinalIgnoreCase))
                priority -= 100;

            return priority;
        }

        private static int GetRuntimeHandPriority(RemoteHandState state)
        {
            if (state == null)
                return int.MinValue;

            int priority = 0;
            if (state.isTracked)
                priority += 2000;
            if (state.isGrabbing)
                priority += 200;
            if (!string.IsNullOrWhiteSpace(state.heldObjectInstanceId))
                priority += 100;
            return priority;
        }

        private static float[] CaptureFingerCurls(Hand hand)
        {
            var curls = new float[5];
            Finger[] fingers = hand.fingers ?? Array.Empty<Finger>();
            for (int i = 0; i < fingers.Length; i++)
            {
                Finger finger = fingers[i];
                if (finger == null)
                    continue;

                int index = (int)finger.fingerType;
                if (index < 0 || index >= curls.Length)
                    continue;

                curls[index] = Mathf.Clamp01(hand.gripOffset + finger.GetCurrentBend());
            }

            return curls;
        }

        private FullSceneSnapshot CaptureFullSnapshot()
        {
            long now = SpectatorProtocol.NowUnixMilliseconds();
            SharedLayoutSnapshot layout = m_PlacementManager != null
                ? m_PlacementManager.CaptureSharedLayoutSnapshot(m_LayoutRevision)
                : new SharedLayoutSnapshot();
            layout.layoutRevision = m_LayoutRevision;

            PlaybackSnapshot playback = CapturePlaybackSnapshot(now);
            playback.playbackRevision = m_PlaybackRevision;

            RemoteHandState[] hands = CaptureRemoteHands(now);
            return new FullSceneSnapshot
            {
                layoutRevision = m_LayoutRevision,
                playbackRevision = m_PlaybackRevision,
                handRevision = m_HandRevision,
                sharedLayout = layout,
                playbackSnapshot = playback,
                remoteHands = hands,
            };
        }

        private void BroadcastDeltaEvents()
        {
            if (m_PendingDeltaEvents.Count == 0)
                return;

            var batch = new DeltaEventBatch
            {
                events = m_PendingDeltaEvents.ToArray(),
            };

            ClientConnection[] clients;
            lock (m_Clients)
                clients = m_Clients.ToArray();

            for (int i = 0; i < clients.Length; i++)
            {
                ClientConnection connection = clients[i];
                if (connection == null || !connection.receivedHello || !connection.tcpClient.Connected)
                    continue;

                _ = SpectatorTransport.WriteEnvelopeAsync(
                    connection.tcpClient.GetStream(),
                    SpectatorMessageKind.DeltaEventBatch,
                    batch,
                    connection.cancellation.Token);
            }

            m_PendingDeltaEvents.Clear();
        }

        private void BroadcastHeartbeat()
        {
            var heartbeat = new SpectatorHeartbeat
            {
                hostTimestamp = SpectatorProtocol.NowUnixMilliseconds(),
            };

            ClientConnection[] clients;
            lock (m_Clients)
                clients = m_Clients.ToArray();

            for (int i = 0; i < clients.Length; i++)
            {
                ClientConnection connection = clients[i];
                if (connection == null || !connection.tcpClient.Connected)
                    continue;

                _ = SpectatorTransport.WriteEnvelopeAsync(
                    connection.tcpClient.GetStream(),
                    SpectatorMessageKind.Heartbeat,
                    heartbeat,
                    connection.cancellation.Token);
            }
        }

        private void CleanupDisconnectedClients()
        {
            lock (m_Clients)
            {
                for (int i = m_Clients.Count - 1; i >= 0; i--)
                {
                    ClientConnection connection = m_Clients[i];
                    if (connection == null || connection.tcpClient == null || !connection.tcpClient.Connected)
                    {
                        DisconnectClient(connection);
                        m_Clients.RemoveAt(i);
                    }
                }
            }
        }

        private void DisconnectClient(ClientConnection connection)
        {
            if (connection == null)
                return;

            try
            {
                connection.cancellation?.Cancel();
                connection.tcpClient?.Close();
            }
            catch
            {
                // Ignore transport shutdown errors.
            }
        }

        [Serializable]
        private sealed class RemoteHandBatch
        {
            public RemoteHandState[] remoteHands = Array.Empty<RemoteHandState>();
        }
    }
}
