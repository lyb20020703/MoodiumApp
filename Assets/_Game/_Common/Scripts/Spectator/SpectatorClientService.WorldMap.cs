using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
#if UNITY_IOS || UNITY_EDITOR
using Unity.Collections;
using UnityEngine.XR.ARKit;
#endif

namespace VFXViewer
{
    public sealed partial class SpectatorClientService
    {
        private const string RootNativeAnchorName = "VFXViewerSpectatorRoot";
        private const string WorldMapMetadataFileName = "spectator_root_worldmap.json";
        private const string WorldMapBytesFileName = "spectator_root_worldmap.bytes";
        private const string WorldMapBackupFileNamePrefix = "spectator_root_worldmap_backup";
        private const float WorldMapSaveRetryDelaySeconds = 1.5f;
        private const int WorldMapSaveMaxAttempts = 8;
        private const float WorldMapRestoreReadyTimeoutSeconds = 15f;
        private const float WorldMapRestoreTimeoutSeconds = 45f;
        private const float WorldMapRestoreTrackingGraceSeconds = 0.5f;
        private const float WorldMapRelaxedRestoreMinimumDelaySeconds = 10f;
        private const float WorldMapApproximateRefinementTimeoutSeconds = 30f;
        private const float WorldMapApproximateRefinementPositionThresholdMeters = 0.01f;
        private const float WorldMapApproximateRefinementRotationThresholdDegrees = 1f;
        private const float WorldMapNamedAnchorObservationTimeoutSeconds = 2.5f;
        private const float WorldMapNamedAnchorObservationPositionThresholdMeters = 0.03f;
        private const float WorldMapNamedAnchorObservationRotationThresholdDegrees = 3f;
        private const int WorldMapNamedAnchorObservationStabilizationFrames = 2;

        private ARSession m_ArSession;
        private Coroutine m_WorldMapSaveRoutine;
        private Coroutine m_WorldMapRestoreRoutine;
        private Coroutine m_WorldMapRefinementRoutine;
        private SavedSpectatorWorldMapMetadata m_SavedWorldMapMetadata;
        private bool m_HasLoadedWorldMapMetadata;
        private bool m_HasLoggedNamedAnchorObserverInstall;

        [Serializable]
        private sealed class SavedSpectatorWorldMapMetadata
        {
            public int version = 1;
            public string sceneName = string.Empty;
            public long savedUtcTicks;
            public string rootAnchorName = RootNativeAnchorName;
            public bool requireNamedRootAnchorRestore;
            public SerializablePose rootPose;
        }

        [Serializable]
        private sealed class PortableSpectatorWorldMapBackup
        {
            public int version = 1;
            public long exportedUtcTicks;
            public SavedSpectatorWorldMapMetadata metadata;
            public string worldMapBase64 = string.Empty;
        }

        private string WorldMapMetadataPath => Path.Combine(Application.persistentDataPath, WorldMapMetadataFileName);
        private string WorldMapBytesPath => Path.Combine(Application.persistentDataPath, WorldMapBytesFileName);

        private bool HasSavedWorldMapBackupForExport()
        {
            return TryGetSavedWorldMapMetadata(out _) && File.Exists(WorldMapBytesPath);
        }

        private void InitializeWorldMapPersistence()
        {
            ResolveArSession();
            LoadSavedWorldMapMetadataIfNeeded();

            bool supportsPersistence = SupportsWorldMapPersistence();
            bool metadataExists = File.Exists(WorldMapMetadataPath);
            bool bytesExists = File.Exists(WorldMapBytesPath);
            bool canAttemptRestore = CanAttemptWorldMapRestore();

            Debug.Log(
                $"[SpectatorWorldMap] Init. persistentDataPath='{Application.persistentDataPath}', " +
                $"supports={supportsPersistence}, metadataExists={metadataExists}, " +
                $"bytesExists={bytesExists}, canAttemptRestore={canAttemptRestore}, scene='{CurrentSceneName}', " +
                $"{DescribeTrackingStateForLog()}.");

            if (m_WorldMapRestoreRoutine == null && canAttemptRestore)
            {
                Debug.Log($"[SpectatorWorldMap] Found saved world map metadata at '{WorldMapMetadataPath}'. Starting restore flow.");
#if UNITY_IOS || UNITY_EDITOR
                m_WorldMapRestoreRoutine = StartCoroutine(RestoreRootFromSavedWorldMapCoroutine());
#else
                Debug.LogWarning("[SpectatorWorldMap] World map restore coroutine is unavailable on this platform.");
#endif
                return;
            }

            Debug.Log(
                $"[SpectatorWorldMap] Restore not started. existingRestoreRoutine={m_WorldMapRestoreRoutine != null}, " +
                $"hasRootManipulator={m_RootManipulator != null}, supports={supportsPersistence}, " +
                $"metadataExists={metadataExists}, bytesExists={bytesExists}.");
        }

        private void StopWorldMapOperations()
        {
            StopWorldMapSaveRoutine();
            CancelPendingWorldMapRestore();
            CancelPendingWorldMapRefinement();
        }

        private void StopWorldMapSaveRoutine()
        {
            if (m_WorldMapSaveRoutine == null)
                return;

            StopCoroutine(m_WorldMapSaveRoutine);
            m_WorldMapSaveRoutine = null;
        }

        private void CancelPendingWorldMapRestore()
        {
            if (m_WorldMapRestoreRoutine == null)
                return;

            StopCoroutine(m_WorldMapRestoreRoutine);
            m_WorldMapRestoreRoutine = null;
        }

        private void CancelPendingWorldMapRefinement()
        {
            if (m_WorldMapRefinementRoutine == null)
                return;

            StopCoroutine(m_WorldMapRefinementRoutine);
            m_WorldMapRefinementRoutine = null;
        }

        private void StartWorldMapSaveRoutine()
        {
#if UNITY_IOS || UNITY_EDITOR
            if (!SupportsWorldMapPersistence())
            {
                Debug.Log($"[SpectatorWorldMap] StartWorldMapSaveRoutine skipped because persistence is not supported. {DescribeTrackingStateForLog()}.", this);
                return;
            }

            StopWorldMapSaveRoutine();
            Debug.Log(
                $"[SpectatorWorldMap] Starting save routine. hasRoot={m_RootManipulator != null}, isRootLocked={m_RootManipulator != null && m_RootManipulator.IsLocked}, " +
                $"{DescribeTrackingStateForLog()}.",
                this);
            m_WorldMapSaveRoutine = StartCoroutine(CaptureAndSaveWorldMapCoroutine());
#endif
        }

        private void ResolveArSession()
        {
            if (m_ArSession != null)
                return;

            m_ArSession = FindFirstObjectByType<ARSession>(FindObjectsInactive.Include);
        }

        private bool EnsureNamedRootAnchorObserverInstalled()
        {
            ResolveArSession();
            if (!SpectatorRootNativeAnchorBridge.IsSupported || m_ArSession == null)
                return false;

            if (SpectatorRootNativeAnchorBridge.EnsureObserver(m_ArSession, out string errorMessage))
            {
                if (!m_HasLoggedNamedAnchorObserverInstall)
                {
                    m_HasLoggedNamedAnchorObserverInstall = true;
                    Debug.Log("[SpectatorWorldMap] Installed ARSessionDelegate proxy for named Root anchor observation.");
                }

                return true;
            }

            if (!string.IsNullOrWhiteSpace(errorMessage))
                Debug.LogWarning($"[SpectatorWorldMap] Failed to install named Root anchor observer: {errorMessage}");

            return false;
        }

        private bool CanAttemptWorldMapRestore()
        {
            if (!SupportsWorldMapPersistence())
                return false;

            if (m_RootManipulator != null)
                return false;

            return TryGetSavedWorldMapMetadata(out _);
        }

        private bool SupportsWorldMapPersistence()
        {
#if UNITY_IOS || UNITY_EDITOR
            return !Application.isEditor
                && PlatformRuntime.IsIOSButNotVisionOS
                && ARKitSessionSubsystem.worldMapSupported;
#else
            return false;
#endif
        }

        private bool TryGetSavedWorldMapMetadata(out SavedSpectatorWorldMapMetadata metadata)
        {
            LoadSavedWorldMapMetadataIfNeeded();
            metadata = m_SavedWorldMapMetadata;
            return metadata != null && File.Exists(WorldMapBytesPath);
        }

        private void LoadSavedWorldMapMetadataIfNeeded()
        {
            if (m_HasLoadedWorldMapMetadata)
                return;

            m_HasLoadedWorldMapMetadata = true;
            if (!File.Exists(WorldMapMetadataPath) || !File.Exists(WorldMapBytesPath))
            {
                Debug.Log(
                    $"[SpectatorWorldMap] No saved ARWorldMap files were found yet. " +
                    $"metadataExists={File.Exists(WorldMapMetadataPath)}, bytesExists={File.Exists(WorldMapBytesPath)}, " +
                    $"persistentDataPath='{Application.persistentDataPath}'. " +
                    "If the app was reinstalled, iOS cleared the saved spectator world map.");
                DeleteSavedWorldMapIfIncomplete();
                return;
            }

            try
            {
                string json = File.ReadAllText(WorldMapMetadataPath);
                SavedSpectatorWorldMapMetadata metadata = JsonUtility.FromJson<SavedSpectatorWorldMapMetadata>(json);
                if (metadata == null || metadata.version <= 0)
                {
                    Debug.LogWarning("[SpectatorWorldMap] Saved metadata was invalid. Clearing spectator world map files.");
                    DeleteSavedWorldMap();
                    return;
                }

                m_SavedWorldMapMetadata = metadata;
                Debug.Log(
                    $"[SpectatorWorldMap] Loaded metadata. scene='{metadata.sceneName}', savedUtcTicks={metadata.savedUtcTicks}, " +
                    $"rootAnchorName='{metadata.rootAnchorName}', requireNamedRootAnchorRestore={metadata.requireNamedRootAnchorRestore}, " +
                    $"rootPose={FormatPoseForLog(metadata.rootPose.ToPose())}, " +
                    $"bytesLength={(File.Exists(WorldMapBytesPath) ? new FileInfo(WorldMapBytesPath).Length : 0)}.",
                    this);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SpectatorWorldMap] Failed to read saved metadata: {ex.Message}");
                DeleteSavedWorldMap();
            }
        }

        private void DeleteSavedWorldMapIfIncomplete()
        {
            bool hasMetadata = File.Exists(WorldMapMetadataPath);
            bool hasBytes = File.Exists(WorldMapBytesPath);
            if (!hasMetadata && !hasBytes)
                return;

            DeleteSavedWorldMap();
        }

        private void DeleteSavedWorldMap()
        {
            Debug.Log(
                $"[SpectatorWorldMap] Deleting saved world map files. metadataExists={File.Exists(WorldMapMetadataPath)}, " +
                $"bytesExists={File.Exists(WorldMapBytesPath)}.",
                this);
            try
            {
                if (File.Exists(WorldMapMetadataPath))
                    File.Delete(WorldMapMetadataPath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SpectatorWorldMap] Failed to delete metadata file: {ex.Message}");
            }

            try
            {
                if (File.Exists(WorldMapBytesPath))
                    File.Delete(WorldMapBytesPath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SpectatorWorldMap] Failed to delete world map file: {ex.Message}");
            }

            m_SavedWorldMapMetadata = null;
            m_HasLoadedWorldMapMetadata = true;
        }

        private void PersistSavedWorldMap(byte[] worldMapBytes, Pose rootPose)
        {
            if (worldMapBytes == null || worldMapBytes.Length == 0)
                throw new ArgumentException("World map bytes were empty.", nameof(worldMapBytes));

            Directory.CreateDirectory(Application.persistentDataPath);

            SavedSpectatorWorldMapMetadata metadata = new SavedSpectatorWorldMapMetadata
            {
                version = 1,
                sceneName = CurrentSceneName,
                savedUtcTicks = DateTime.UtcNow.Ticks,
                rootAnchorName = RootNativeAnchorName,
                requireNamedRootAnchorRestore = false,
                rootPose = new SerializablePose(rootPose),
            };

            string metadataJson = JsonUtility.ToJson(metadata, true);
            WriteAllBytesAtomically(WorldMapBytesPath, worldMapBytes);
            WriteAllTextAtomically(WorldMapMetadataPath, metadataJson);
            m_SavedWorldMapMetadata = metadata;
            m_HasLoadedWorldMapMetadata = true;
            Debug.Log(
                $"[SpectatorWorldMap] Persisted world map. bytesLength={worldMapBytes.Length}, metadataPath='{WorldMapMetadataPath}', " +
                $"bytesPath='{WorldMapBytesPath}', scene='{metadata.sceneName}', rootAnchorName='{metadata.rootAnchorName}', " +
                $"requireNamedRootAnchorRestore={metadata.requireNamedRootAnchorRestore}, rootPose={FormatPoseForLog(rootPose)}.",
                this);
        }

        private static void WriteAllTextAtomically(string path, string contents)
        {
            string tempPath = path + ".tmp";
            File.WriteAllText(tempPath, contents);
            if (File.Exists(path))
                File.Delete(path);
            File.Move(tempPath, path);
        }

        private static void WriteAllBytesAtomically(string path, byte[] contents)
        {
            string tempPath = path + ".tmp";
            File.WriteAllBytes(tempPath, contents);
            if (File.Exists(path))
                File.Delete(path);
            File.Move(tempPath, path);
        }

        private bool TryGetCurrentRootPose(out Pose rootPose)
        {
            if (m_RootManipulator == null)
            {
                rootPose = default;
                return false;
            }

            Transform rootTransform = m_RootManipulator.transform;
            rootPose = new Pose(rootTransform.position, rootTransform.rotation);
            return true;
        }

        private void RestoreRootFromSavedWorldMap(
            Pose savedPose,
            bool approximateRestore = false,
            SavedSpectatorWorldMapMetadata metadata = null)
        {
            if (m_RootManipulator != null)
                return;

            Debug.Log(
                $"[SpectatorWorldMap] Restoring Root object from pose " +
                $"pos={savedPose.position}, rot={savedPose.rotation.eulerAngles}.");
            CreateOrRecreateRootInternal(savedPose, anchorImmediately: false, announceCreation: false);
            SetStatus(approximateRestore
                ? "World map restore stayed approximate. Restoring Root from the saved pose..."
                : "Saved ARWorldMap relocalized. Restoring Root anchor...");
            _ = LockRootWithAnchorAsync(
                captureWorldMapAfterLock: false,
                successMessage: approximateRestore
                    ? "Root restored from the saved pose approximation."
                    : "Root restored from saved ARWorldMap.");

            if (approximateRestore)
            {
#if UNITY_IOS || UNITY_EDITOR
                StartApproximateRestoreRefinement(metadata);
#endif
            }
        }

        private void ExportSavedWorldMapBackup()
        {
            if (!SpectatorDocumentBridge.IsSupported)
            {
                SetStatus("Backup export is only available on iPhone/iPad spectator builds.");
                return;
            }

            if (!TryBuildPortableWorldMapBackupJson(out string backupJson, out string fileName, out string errorMessage))
            {
                SetStatus(errorMessage);
                return;
            }

            m_DocumentExportMode = SpectatorDocumentTransferMode.WorldMapBackup;
            SpectatorDocumentBridge.ExportJsonFile(fileName, backupJson);
            SetStatus("Opening the iPhone/iPad share sheet for the spectator world map backup...");
        }

        private void BeginImportWorldMapBackup()
        {
            if (!SpectatorDocumentBridge.IsSupported)
            {
                SetStatus("Backup import is only available on iPhone/iPad spectator builds.");
                return;
            }

            m_DocumentImportMode = SpectatorDocumentTransferMode.WorldMapBackup;
            SpectatorDocumentBridge.BeginImportJsonFile();
            SetStatus("Choose a spectator world map backup JSON from Files.");
        }

        private void ProcessDocumentBridgeEvents()
        {
            while (SpectatorDocumentBridge.TryDequeueEvent(out SpectatorDocumentBridgeEvent nextEvent))
            {
                if (nextEvent == null || string.IsNullOrWhiteSpace(nextEvent.type))
                    continue;

                switch (nextEvent.type)
                {
                    case "exportCompleted":
                        SetStatus(m_DocumentExportMode == SpectatorDocumentTransferMode.LayoutJson
                            ? "布局文件导出完成。"
                            : "World map backup export finished.");
                        m_DocumentExportMode = SpectatorDocumentTransferMode.None;
                        break;

                    case "exportCancelled":
                        SetStatus(m_DocumentExportMode == SpectatorDocumentTransferMode.LayoutJson
                            ? "布局文件导出已取消。"
                            : "World map backup export cancelled.");
                        m_DocumentExportMode = SpectatorDocumentTransferMode.None;
                        break;

                    case "importCancelled":
                        SetStatus(m_DocumentImportMode == SpectatorDocumentTransferMode.LayoutJson
                            ? "布局文件导入已取消。"
                            : "World map backup import cancelled.");
                        m_DocumentImportMode = SpectatorDocumentTransferMode.None;
                        break;

                    case "importSucceeded":
                        SpectatorDocumentTransferMode importMode = m_DocumentImportMode;
                        m_DocumentImportMode = SpectatorDocumentTransferMode.None;
                        if (importMode == SpectatorDocumentTransferMode.LayoutJson)
                            ImportLayoutJsonFromFile(nextEvent.filePath);
                        else
                            ImportWorldMapBackupFromFile(nextEvent.filePath);
                        break;

                    case "error":
                        m_DocumentExportMode = SpectatorDocumentTransferMode.None;
                        m_DocumentImportMode = SpectatorDocumentTransferMode.None;
                        SetStatus(string.IsNullOrWhiteSpace(nextEvent.message)
                            ? "World map backup transfer failed."
                            : nextEvent.message);
                        break;
                }
            }
        }

        private bool TryBuildPortableWorldMapBackupJson(out string backupJson, out string fileName, out string errorMessage)
        {
            backupJson = null;
            fileName = null;
            errorMessage = null;

            if (!TryGetSavedWorldMapMetadata(out SavedSpectatorWorldMapMetadata metadata))
            {
                errorMessage = "No saved ARWorldMap is available yet. Lock Root and wait for the save to finish first.";
                return false;
            }

            byte[] worldMapBytes;
            try
            {
                worldMapBytes = File.ReadAllBytes(WorldMapBytesPath);
            }
            catch (Exception ex)
            {
                errorMessage = $"Failed to read the saved ARWorldMap bytes: {ex.Message}";
                return false;
            }

            if (worldMapBytes == null || worldMapBytes.Length == 0)
            {
                errorMessage = "The saved ARWorldMap bytes were empty. Lock Root again to create a fresh save.";
                return false;
            }

            var backup = new PortableSpectatorWorldMapBackup
            {
                version = 1,
                exportedUtcTicks = DateTime.UtcNow.Ticks,
                metadata = metadata,
                worldMapBase64 = Convert.ToBase64String(worldMapBytes),
            };

            backupJson = JsonUtility.ToJson(backup, true);
            fileName = $"{WorldMapBackupFileNamePrefix}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json";
            return true;
        }

        private void ImportWorldMapBackupFromFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                SetStatus("The selected backup file path was empty.");
                return;
            }

            try
            {
                string json = File.ReadAllText(filePath);
                PortableSpectatorWorldMapBackup backup = JsonUtility.FromJson<PortableSpectatorWorldMapBackup>(json);
                if (backup == null || backup.version <= 0 || backup.metadata == null || string.IsNullOrWhiteSpace(backup.worldMapBase64))
                {
                    SetStatus("The selected backup file was invalid.");
                    return;
                }

                byte[] worldMapBytes = Convert.FromBase64String(backup.worldMapBase64);
                if (worldMapBytes == null || worldMapBytes.Length == 0)
                {
                    SetStatus("The selected backup file did not contain ARWorldMap bytes.");
                    return;
                }

                StopWorldMapSaveRoutine();
                CancelPendingWorldMapRestore();
                PersistSavedWorldMap(worldMapBytes, backup.metadata.rootPose.ToPose());

                Debug.Log(
                    $"[SpectatorWorldMap] Imported backup from '{filePath}'. " +
                    $"worldMapBytes={worldMapBytes.Length}, scene='{backup.metadata.sceneName}'.");

                if (m_RootManipulator == null && SupportsWorldMapPersistence())
                {
                    SetStatus("World map backup imported. Starting ARWorldMap restore...");
#if UNITY_IOS || UNITY_EDITOR
                    m_WorldMapRestoreRoutine = StartCoroutine(RestoreRootFromSavedWorldMapCoroutine());
#else
                    SetStatus("World map backup imported, but ARWorldMap restore is unavailable on this platform.");
#endif
                }
                else
                {
                    SetStatus("World map backup imported. Relaunch the app or remove the current Root to restore from it.");
                }
            }
            catch (FormatException ex)
            {
                SetStatus($"The selected backup file was corrupted: {ex.Message}");
            }
            catch (Exception ex)
            {
                SetStatus($"Failed to import the selected backup file: {ex.Message}");
            }
            finally
            {
                try
                {
                    if (File.Exists(filePath))
                        File.Delete(filePath);
                }
                catch
                {
                    // Ignore temp file cleanup issues.
                }
            }
        }

#if UNITY_IOS || UNITY_EDITOR
        private ARKitSessionSubsystem GetARKitSessionSubsystem()
        {
            ResolveArSession();
            return m_ArSession != null ? m_ArSession.subsystem as ARKitSessionSubsystem : null;
        }

        private IEnumerator CaptureAndSaveWorldMapCoroutine()
        {
            try
            {
                if (!SupportsWorldMapPersistence() || !TryGetCurrentRootPose(out Pose rootPose))
                {
                    Debug.LogWarning(
                        $"[SpectatorWorldMap] Save routine exited early. supports={SupportsWorldMapPersistence()}, " +
                        $"hasRootPose={TryGetCurrentRootPose(out _)}, hasRootManipulator={m_RootManipulator != null}, " +
                        $"{DescribeTrackingStateForLog()}.",
                        this);
                    yield break;
                }

                yield return null;

                WaitForSecondsRealtime retryDelay = new WaitForSecondsRealtime(WorldMapSaveRetryDelaySeconds);
                for (int attempt = 1; attempt <= WorldMapSaveMaxAttempts; attempt++)
                {
                    if (m_RootManipulator == null || !m_RootManipulator.IsLocked || !TryGetCurrentRootPose(out rootPose))
                    {
                        Debug.LogWarning(
                            $"[SpectatorWorldMap] Save routine stopped because Root changed state during attempt {attempt}. " +
                            $"hasRoot={m_RootManipulator != null}, isLocked={m_RootManipulator != null && m_RootManipulator.IsLocked}.",
                            this);
                        yield break;
                    }

                    ARKitSessionSubsystem arKitSession = GetARKitSessionSubsystem();
                    if (arKitSession == null)
                    {
                        Debug.LogWarning("[SpectatorWorldMap] ARKitSessionSubsystem was unavailable; skipping world map save.");
                        yield break;
                    }

                    ARWorldMappingStatus mappingStatus = arKitSession.worldMappingStatus;
                    Debug.Log(
                        $"[SpectatorWorldMap] Save attempt {attempt}/{WorldMapSaveMaxAttempts}. mappingStatus={mappingStatus}, " +
                        $"{DescribeTrackingStateForLog()}, rootPose={FormatPoseForLog(rootPose)}.",
                        this);
                    if (mappingStatus < ARWorldMappingStatus.Extending)
                    {
                        SetStatus($"Root locked. Scan a bit more to save ARWorldMap... ({mappingStatus})");
                        yield return retryDelay;
                        continue;
                    }

                    ARWorldMapRequest request = default;
                    ARWorldMap worldMap = default;
                    NativeArray<byte> serializedBytes = default;
                    bool requestCreated = false;
                    bool worldMapCreated = false;
                    bool serializedArrayCreated = false;

                    try
                    {
                        bool syncedNamedRootAnchor = TrySyncNamedRootAnchorIntoCurrentSession(rootPose);
                        if (syncedNamedRootAnchor)
                            yield return WaitForNamedRootAnchorObservationBeforeSave(rootPose);
                        else
                            yield return null;

                        request = arKitSession.GetARWorldMapAsync();
                        requestCreated = true;
                        while (!request.status.IsDone())
                            yield return null;

                        if (request.status == ARWorldMapRequestStatus.Success)
                        {
                            worldMap = request.GetWorldMap();
                            worldMapCreated = true;
                            if (!worldMap.valid)
                            {
                                Debug.LogWarning("[SpectatorWorldMap] ARWorldMap request succeeded, but the returned map was invalid.");
                                yield break;
                            }

                            serializedBytes = worldMap.Serialize(Allocator.Temp);
                            serializedArrayCreated = true;
                            byte[] managedBytes = new byte[serializedBytes.Length];
                            for (int i = 0; i < serializedBytes.Length; i++)
                                managedBytes[i] = serializedBytes[i];

                            PersistSavedWorldMap(managedBytes, rootPose);
                            SetStatus("Root locked. ARWorldMap saved for next launch.");
                            Debug.Log(
                                $"[SpectatorWorldMap] Saved ARWorldMap ({managedBytes.Length} bytes) for scene '{CurrentSceneName}'. " +
                                $"metadataPath='{WorldMapMetadataPath}', bytesPath='{WorldMapBytesPath}'.");
                            yield break;
                        }

                        if (request.status == ARWorldMapRequestStatus.ErrorInsufficientFeatures)
                        {
                            SetStatus("Root locked. ARWorldMap needs a bit more scanning before it can be saved...");
                            yield return retryDelay;
                            continue;
                        }

                        SetStatus($"ARWorldMap save skipped: {request.status}");
                        Debug.LogWarning($"[SpectatorWorldMap] World map save failed with status {request.status}.");
                        yield break;
                    }
                    finally
                    {
                        if (serializedArrayCreated && serializedBytes.IsCreated)
                            serializedBytes.Dispose();
                        if (worldMapCreated)
                            worldMap.Dispose();
                        if (requestCreated)
                            request.Dispose();
                    }
                }

                SetStatus("ARWorldMap save did not finish. Move around the space and lock Root again.");
            }
            finally
            {
                m_WorldMapSaveRoutine = null;
            }
        }

        private IEnumerator RestoreRootFromSavedWorldMapCoroutine()
        {
            try
            {
                if (!TryGetSavedWorldMapMetadata(out SavedSpectatorWorldMapMetadata metadata))
                    yield break;

                bool expectsNamedRootAnchor = metadata != null && !string.IsNullOrWhiteSpace(metadata.rootAnchorName);
                bool requiresNamedRootAnchorRestore = expectsNamedRootAnchor && metadata != null && metadata.requireNamedRootAnchorRestore;
                bool announcedNamedAnchorWait = false;
                bool announcedRelaxedRestoreReadiness = false;
                Debug.Log(
                    $"[SpectatorWorldMap] Restore routine started. expectsNamedRootAnchor={expectsNamedRootAnchor}, " +
                    $"requiresNamedRootAnchorRestore={requiresNamedRootAnchorRestore}, " +
                    $"scene='{metadata.sceneName}', rootAnchorName='{metadata.rootAnchorName}', " +
                    $"savedRootPose={FormatPoseForLog(metadata.rootPose.ToPose())}, {DescribeTrackingStateForLog()}.",
                    this);

                float readyDeadline = Time.realtimeSinceStartup + WorldMapRestoreReadyTimeoutSeconds;
                SetStatus("Saved ARWorldMap found. Waiting for AR session readiness...");

                while (Time.realtimeSinceStartup < readyDeadline)
                {
                    if (m_RootManipulator != null)
                        yield break;

                    ARKitSessionSubsystem arKitSession = GetARKitSessionSubsystem();
                    bool sessionReady = arKitSession != null
                        && m_ArSession != null
                        && m_ArSession.enabled
                        && arKitSession.running
                        && ARSession.state != ARSessionState.None
                        && ARSession.state != ARSessionState.CheckingAvailability;

                    if (sessionReady)
                    {
                        Debug.Log($"[SpectatorWorldMap] AR session became ready for restore. {DescribeTrackingStateForLog()}.", this);
                        break;
                    }

                    yield return null;
                }

                ARKitSessionSubsystem sessionSubsystem = GetARKitSessionSubsystem();
                if (sessionSubsystem == null)
                {
                    SetStatus("Saved ARWorldMap found, but ARKit session was not ready. Create Root manually if needed.");
                    Debug.LogWarning($"[SpectatorWorldMap] Restore aborted because ARKitSessionSubsystem was still unavailable after the readiness timeout. {DescribeTrackingStateForLog()}.");
                    yield break;
                }

                EnsureNamedRootAnchorObserverInstalled();

                byte[] serializedMapBytes;
                try
                {
                    serializedMapBytes = File.ReadAllBytes(WorldMapBytesPath);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[SpectatorWorldMap] Failed to read saved ARWorldMap bytes: {ex.Message}");
                    DeleteSavedWorldMap();
                    SetStatus("Saved ARWorldMap could not be read. Create Root manually to make a new one.");
                    yield break;
                }

                if (serializedMapBytes == null || serializedMapBytes.Length == 0)
                {
                    DeleteSavedWorldMap();
                    SetStatus("Saved ARWorldMap was empty. Create Root manually to make a new one.");
                    yield break;
                }

                NativeArray<byte> serializedBytes = default;
                ARWorldMap worldMap = default;
                bool serializedArrayCreated = false;
                bool worldMapCreated = false;

                try
                {
                    serializedBytes = new NativeArray<byte>(serializedMapBytes.Length, Allocator.Temp);
                    serializedArrayCreated = true;
                    for (int i = 0; i < serializedMapBytes.Length; i++)
                        serializedBytes[i] = serializedMapBytes[i];

                    if (!ARWorldMap.TryDeserialize(serializedBytes, out worldMap) || !worldMap.valid)
                    {
                        Debug.LogWarning("[SpectatorWorldMap] Failed to deserialize the saved ARWorldMap. Clearing the corrupted files.");
                        DeleteSavedWorldMap();
                        SetStatus("Saved ARWorldMap was invalid. Create Root manually to make a new one.");
                        yield break;
                    }

                    worldMapCreated = true;
                    sessionSubsystem.ApplyWorldMap(worldMap);
                    if (m_ArSession != null)
                        m_ArSession.Reset();
                    else
                        sessionSubsystem.Reset();

                    Debug.Log(
                        $"[SpectatorWorldMap] Applied saved ARWorldMap ({serializedMapBytes.Length} bytes). Waiting for relocalization. " +
                        $"{DescribeTrackingStateForLog()}.",
                        this);
                }
                finally
                {
                    if (worldMapCreated)
                        worldMap.Dispose();
                    if (serializedArrayCreated && serializedBytes.IsCreated)
                        serializedBytes.Dispose();
                }

                float restoreDeadline = Time.realtimeSinceStartup + WorldMapRestoreTimeoutSeconds;
                float restoreAppliedAt = Time.realtimeSinceStartup;
                bool trackingRecoveredSinceApply = false;
                bool usedRelaxedRestoreReadiness = false;
                SetStatus("Saved ARWorldMap loaded. Move around the saved area to relocalize...");
                Debug.Log("[SpectatorWorldMap] Waiting for ARSession tracking recovery after applying saved world map.");

                while (Time.realtimeSinceStartup < restoreDeadline)
                {
                    if (m_RootManipulator != null)
                        yield break;

                    bool relaxedReadinessThisFrame = false;
                    if (IsWorldMapRestoreReady(restoreAppliedAt, out relaxedReadinessThisFrame))
                    {
                        trackingRecoveredSinceApply = true;
                        usedRelaxedRestoreReadiness |= relaxedReadinessThisFrame;

                        if (relaxedReadinessThisFrame && !announcedRelaxedRestoreReadiness)
                        {
                            announcedRelaxedRestoreReadiness = true;
                            Debug.Log(
                                $"[SpectatorWorldMap] Using relaxed restore readiness because ARSession is still initializing, " +
                                $"but the session is running and world mapping has recovered enough to place Root. {DescribeTrackingStateForLog()}.",
                                this);
                        }

                        if (expectsNamedRootAnchor)
                        {
                            if (TryResolveRestoredRootAnchorPose(metadata, out Pose namedAnchorPose))
                            {
                                Debug.Log($"[SpectatorWorldMap] Restored Root from named ARAnchor '{metadata.rootAnchorName}'.");
                                RestoreRootFromSavedWorldMap(namedAnchorPose, approximateRestore: false, metadata: metadata);
                                yield break;
                            }

                            if (!requiresNamedRootAnchorRestore)
                            {
                                Debug.Log(
                                    $"[SpectatorWorldMap] Named Root anchor '{metadata.rootAnchorName}' was not resolved. " +
                                    $"Falling back to the saved Root pose because this backup allows relaxed local restore.",
                                    this);
                                RestoreRootFromSavedWorldMap(metadata.rootPose.ToPose(), approximateRestore: true, metadata: metadata);
                                yield break;
                            }

                            if (!announcedNamedAnchorWait)
                            {
                                announcedNamedAnchorWait = true;
                                SetStatus("Saved ARWorldMap loaded. Waiting for the shared Root anchor to relocalize...");
                                Debug.Log(
                                    $"[SpectatorWorldMap] Tracking recovered, but named Root anchor '{metadata.rootAnchorName}' has not reappeared yet. " +
                                    $"Continuing to wait. {DescribeTrackingStateForLog()}.",
                                    this);
                            }
                        }
                        else
                        {
                            Debug.Log($"[SpectatorWorldMap] Falling back to saved Root pose metadata for restore.");
                            RestoreRootFromSavedWorldMap(metadata.rootPose.ToPose());
                            yield break;
                        }
                    }

                    yield return null;
                }

                if (expectsNamedRootAnchor)
                {
                    if (trackingRecoveredSinceApply)
                    {
                        Debug.LogWarning(
                            $"[SpectatorWorldMap] Named Root anchor '{metadata.rootAnchorName}' did not relocalize in time. " +
                            "Falling back to the saved Root pose metadata.");
                        SetStatus("Shared Root anchor did not relocalize in time. Falling back to the saved Root pose...");
                        RestoreRootFromSavedWorldMap(metadata.rootPose.ToPose(), approximateRestore: true, metadata: metadata);
                        yield break;
                    }

                    if (!requiresNamedRootAnchorRestore)
                    {
                        SetStatus("Saved ARWorldMap is still initializing. Restoring Root from the saved pose approximation...");
                        Debug.LogWarning(
                            $"[SpectatorWorldMap] Restore timed out before full ARSession tracking recovery. " +
                            $"Falling back to the saved Root pose because this backup allows relaxed local restore. {DescribeTrackingStateForLog()}.",
                            this);
                        RestoreRootFromSavedWorldMap(metadata.rootPose.ToPose(), approximateRestore: true, metadata: metadata);
                        yield break;
                    }

                    SetStatus("Saved ARWorldMap loaded, but the shared Root anchor did not relocalize in time. Create Root manually if needed.");
                    Debug.LogWarning(
                        $"[SpectatorWorldMap] Timed out waiting for named Root anchor '{metadata.rootAnchorName}'. " +
                        $"Not falling back to pose metadata because this backup expects shared anchor restore. {DescribeTrackingStateForLog()}.",
                        this);
                    yield break;
                }

                SetStatus("Saved ARWorldMap found, but relocalization timed out. Create Root manually if needed.");
                Debug.LogWarning($"[SpectatorWorldMap] Relocalization timed out without tracking recovery. {DescribeTrackingStateForLog()}.", this);
            }
            finally
            {
                m_WorldMapRestoreRoutine = null;
            }
        }

        private bool TrySyncNamedRootAnchorIntoCurrentSession(Pose rootPose)
        {
            if (!SpectatorRootNativeAnchorBridge.IsSupported || m_ArSession == null)
                return false;

            EnsureNamedRootAnchorObserverInstalled();

            if (SpectatorRootNativeAnchorBridge.TryUpsertNamedAnchor(m_ArSession, RootNativeAnchorName, rootPose, out string errorMessage))
            {
                Debug.Log($"[SpectatorWorldMap] Synced named Root anchor '{RootNativeAnchorName}' into the current ARSession before saving.");
                return true;
            }

            if (!string.IsNullOrWhiteSpace(errorMessage))
                Debug.LogWarning($"[SpectatorWorldMap] Failed to sync named Root anchor into ARSession: {errorMessage}");

            return false;
        }

        private bool TryResolveRestoredRootAnchorPose(SavedSpectatorWorldMapMetadata metadata, out Pose restoredPose)
        {
            restoredPose = default;
            if (!SpectatorRootNativeAnchorBridge.IsSupported || m_ArSession == null)
                return false;

            string anchorName = metadata != null && !string.IsNullOrWhiteSpace(metadata.rootAnchorName)
                ? metadata.rootAnchorName
                : RootNativeAnchorName;

            EnsureNamedRootAnchorObserverInstalled();

            string observedErrorMessage = string.Empty;
            string currentFrameErrorMessage = string.Empty;

            bool found = SpectatorRootNativeAnchorBridge.TryGetObservedNamedAnchorPose(m_ArSession, anchorName, out restoredPose, out observedErrorMessage);
            if (!found)
                found = SpectatorRootNativeAnchorBridge.TryGetNamedAnchorPose(m_ArSession, anchorName, out restoredPose, out currentFrameErrorMessage);

            if (found)
            {
                Debug.Log(
                    $"[SpectatorWorldMap] Named Root anchor '{anchorName}' pose resolved to " +
                    $"pos={restoredPose.position}, rot={restoredPose.rotation.eulerAngles}.");
            }
            if (!found)
            {
                if (!string.IsNullOrWhiteSpace(observedErrorMessage)
                    && !IsExpectedMissingNamedRootAnchorMessage(observedErrorMessage))
                {
                    Debug.LogWarning($"[SpectatorWorldMap] Named Root anchor observer lookup failed: {observedErrorMessage}");
                }

                if (!string.IsNullOrWhiteSpace(currentFrameErrorMessage)
                    && !IsExpectedMissingNamedRootAnchorMessage(currentFrameErrorMessage))
                {
                    Debug.LogWarning($"[SpectatorWorldMap] Named Root anchor restore lookup failed: {currentFrameErrorMessage}");
                }
            }

            return found;
        }

        private static bool IsExpectedMissingNamedRootAnchorMessage(string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(errorMessage))
                return false;

            return errorMessage.Contains("has not been observed by the ARSession delegate yet.", StringComparison.OrdinalIgnoreCase)
                || errorMessage.Contains("was not found in the relocalized ARSession.", StringComparison.OrdinalIgnoreCase);
        }

        private IEnumerator WaitForNamedRootAnchorObservationBeforeSave(Pose expectedRootPose)
        {
            if (!EnsureNamedRootAnchorObserverInstalled() || m_ArSession == null)
            {
                yield return null;
                yield break;
            }

            float deadline = Time.realtimeSinceStartup + WorldMapNamedAnchorObservationTimeoutSeconds;
            bool announcedWait = false;

            while (Time.realtimeSinceStartup < deadline)
            {
                if (m_RootManipulator == null || !m_RootManipulator.IsLocked)
                    yield break;

                if (SpectatorRootNativeAnchorBridge.TryGetObservedNamedAnchorPose(
                    m_ArSession,
                    RootNativeAnchorName,
                    out Pose observedPose,
                    out _))
                {
                    float positionDelta = Vector3.Distance(expectedRootPose.position, observedPose.position);
                    float rotationDelta = Quaternion.Angle(expectedRootPose.rotation, observedPose.rotation);
                    if (positionDelta <= WorldMapNamedAnchorObservationPositionThresholdMeters
                        && rotationDelta <= WorldMapNamedAnchorObservationRotationThresholdDegrees)
                    {
                        Debug.Log(
                            $"[SpectatorWorldMap] ARSessionDelegate observed named Root anchor '{RootNativeAnchorName}' " +
                            $"before saving. positionDelta={positionDelta:F4}m, rotationDelta={rotationDelta:F2}deg.");

                        for (int frame = 0; frame < WorldMapNamedAnchorObservationStabilizationFrames; frame++)
                            yield return null;

                        yield break;
                    }

                    if (!announcedWait)
                    {
                        announcedWait = true;
                        Debug.Log(
                            $"[SpectatorWorldMap] Waiting for observed named Root anchor '{RootNativeAnchorName}' to settle " +
                            $"before saving. currentDelta={positionDelta:F4}m/{rotationDelta:F2}deg.");
                    }
                }

                yield return null;
            }

            Debug.LogWarning(
                $"[SpectatorWorldMap] Timed out waiting for ARSessionDelegate to observe named Root anchor '{RootNativeAnchorName}' " +
                "before capturing ARWorldMap. Continuing with a best-effort save.");
        }

        private void StartApproximateRestoreRefinement(SavedSpectatorWorldMapMetadata metadata)
        {
            CancelPendingWorldMapRefinement();
            if (metadata == null || !SpectatorRootNativeAnchorBridge.IsSupported)
                return;

            m_WorldMapRefinementRoutine = StartCoroutine(RefineApproximateRestoredRootCoroutine(metadata));
        }

        private IEnumerator RefineApproximateRestoredRootCoroutine(SavedSpectatorWorldMapMetadata metadata)
        {
            try
            {
                float deadline = Time.realtimeSinceStartup + WorldMapApproximateRefinementTimeoutSeconds;
                bool announcedBackgroundRefinement = false;

                while (Time.realtimeSinceStartup < deadline)
                {
                    if (m_RootManipulator == null)
                        yield break;

                    if (m_IsLockingRoot || m_HeldRootAdjustment.HasValue)
                    {
                        yield return null;
                        continue;
                    }

                    if (!announcedBackgroundRefinement)
                    {
                        announcedBackgroundRefinement = true;
                        Debug.Log(
                            $"[SpectatorWorldMap] Approximate Root restore is active. Continuing to watch for named Root anchor '{metadata.rootAnchorName}' " +
                            "so the alignment can be refined in the background.",
                            this);
                    }

                    if (!m_RootManipulator.IsLocked || m_RootAnchor == null)
                    {
                        yield return null;
                        continue;
                    }

                    if (!TryResolveRestoredRootAnchorPose(metadata, out Pose refinedPose))
                    {
                        yield return null;
                        continue;
                    }

                    Pose currentPose = new Pose(m_RootManipulator.transform.position, m_RootManipulator.transform.rotation);
                    float positionDelta = Vector3.Distance(currentPose.position, refinedPose.position);
                    float rotationDelta = Quaternion.Angle(currentPose.rotation, refinedPose.rotation);
                    if (positionDelta <= WorldMapApproximateRefinementPositionThresholdMeters
                        && rotationDelta <= WorldMapApproximateRefinementRotationThresholdDegrees)
                    {
                        Debug.Log(
                            $"[SpectatorWorldMap] Approximate restore was already close to the relocalized named Root anchor. " +
                            $"positionDelta={positionDelta:F4}m, rotationDelta={rotationDelta:F2}deg.",
                            this);
                        yield break;
                    }

                    Debug.Log(
                        $"[SpectatorWorldMap] Refining approximate Root restore to the relocalized named anchor. " +
                        $"positionDelta={positionDelta:F4}m, rotationDelta={rotationDelta:F2}deg.",
                        this);
                    SetStatus("Relocalization improved. Refining Root alignment...");
                    StopWorldMapSaveRoutine();
                    ReleaseCurrentRootAnchor();
                    m_RootManipulator.SetLocked(false);
                    m_RootManipulator.transform.SetPositionAndRotation(refinedPose.position, refinedPose.rotation);
                    _ = LockRootWithAnchorAsync(
                        captureWorldMapAfterLock: false,
                        successMessage: "Root refined after relocalization.");
                    yield break;
                }

                Debug.Log(
                    $"[SpectatorWorldMap] Approximate restore refinement finished without resolving named Root anchor '{metadata.rootAnchorName}'.",
                    this);
            }
            finally
            {
                m_WorldMapRefinementRoutine = null;
            }
        }

        private bool IsWorldMapRestoreReady(float restoreAppliedAt, out bool usedRelaxedReadiness)
        {
            usedRelaxedReadiness = false;
            float timeSinceApply = Time.realtimeSinceStartup - restoreAppliedAt;
            if (timeSinceApply < WorldMapRestoreTrackingGraceSeconds)
                return false;

            if (ARSession.notTrackingReason == NotTrackingReason.Relocalizing)
                return false;

            if (ARSession.state == ARSessionState.SessionTracking)
                return true;

            ARKitSessionSubsystem arKitSession = GetARKitSessionSubsystem();
            if (arKitSession == null || !arKitSession.running)
                return false;

            if (ARSession.state == ARSessionState.SessionInitializing
                && timeSinceApply >= WorldMapRelaxedRestoreMinimumDelaySeconds
                && arKitSession.worldMappingStatus >= ARWorldMappingStatus.Mapped)
            {
                usedRelaxedReadiness = true;
                return true;
            }

            return false;
        }

#endif

        private string DescribeTrackingStateForLog()
        {
            ResolveArSession();
#if UNITY_IOS || UNITY_EDITOR
            ARKitSessionSubsystem arKitSession = GetARKitSessionSubsystem();
            return
                $"arSessionState={ARSession.state}, notTrackingReason={ARSession.notTrackingReason}, " +
                $"arSessionEnabled={(m_ArSession != null && m_ArSession.enabled)}, " +
                $"arKitSessionRunning={(arKitSession != null && arKitSession.running)}, " +
                $"worldMapSupported={ARKitSessionSubsystem.worldMapSupported}, " +
                $"worldMappingStatus={(arKitSession != null ? arKitSession.worldMappingStatus.ToString() : "<null>")}";
#else
            return
                $"arSessionState={ARSession.state}, notTrackingReason={ARSession.notTrackingReason}, " +
                $"arSessionEnabled={(m_ArSession != null && m_ArSession.enabled)}, " +
                "arKitSessionRunning=<unsupported>, worldMapSupported=<unsupported>, worldMappingStatus=<unsupported>";
#endif
        }
    }
}
