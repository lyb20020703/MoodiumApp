using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.XR.CoreUtils;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR;

namespace VFXViewer
{
    [DefaultExecutionOrder(-10000)]
    public sealed class SpectatorSceneBootstrap : MonoBehaviour
    {
        private const string RuntimeObjectName = "__SpectatorRuntime";
        private const string SpectatorArKitBackgroundShaderResourcePath = "Shaders/SpectatorARKitBackgroundAfterOpaquesFixed";
        private const string SpectatorArKitBackgroundShaderName = "VFXViewer/ARKitBackground/AfterOpaquesFixed";
        private const string ArKitHumanSegmentationKeyword = "ARKIT_HUMAN_SEGMENTATION_ENABLED";
        private const string ArKitEnvironmentDepthKeyword = "ARKIT_ENVIRONMENT_DEPTH_ENABLED";
        private const HumanSegmentationStencilMode SpectatorPeopleOcclusionStencilMode = HumanSegmentationStencilMode.Best;
        private const HumanSegmentationDepthMode SpectatorPeopleOcclusionDepthMode = HumanSegmentationDepthMode.Best;
        private const HumanSegmentationStencilMode SpectatorPeopleOcclusionFallbackStencilMode = HumanSegmentationStencilMode.Fastest;
        private const HumanSegmentationDepthMode SpectatorPeopleOcclusionFallbackDepthMode = HumanSegmentationDepthMode.Fastest;
        private static readonly int HumanStencilPropertyId = Shader.PropertyToID("_HumanStencil");
        private static readonly int HumanDepthPropertyId = Shader.PropertyToID("_HumanDepth");
        private static readonly int CameraForwardScalePropertyId = Shader.PropertyToID("_UnityCameraForwardScale");
        private static SpectatorSceneBootstrap s_Instance;
        private static Material s_SpectatorArKitBackgroundMaterial;
        private static bool s_HasLoggedMissingSpectatorArKitBackgroundShader;
        private static bool s_HasLoggedSpectatorArKitBackgroundOverride;

        private SpectatorHostService m_HostService;
        private SpectatorClientService m_ClientService;
        private SpectatorArDiagnostics m_ArDiagnostics;
        private Coroutine m_CameraAuthorizationRoutine;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (s_Instance != null)
            {
                s_Instance.RebuildForScene(SceneManager.GetActiveScene());
                return;
            }

            var root = new GameObject(RuntimeObjectName);
            DontDestroyOnLoad(root);
            s_Instance = root.AddComponent<SpectatorSceneBootstrap>();
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            RebuildForScene(SceneManager.GetActiveScene());
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            RebuildForScene(scene);
        }

        private void RebuildForScene(Scene scene)
        {
            if (!scene.IsValid())
                return;

            AppRole role = SpectatorRuntimeRole.ResolveRoleForScene(scene);
            DisableSceneRoleMismatches(role);

            if (role == AppRole.VisionHost)
            {
                EnsureHostService();
                if (m_ClientService != null)
                    m_ClientService.enabled = false;
            }
            else
            {
                EnsureClientService();
                EnsureSpectatorArView();
                EnsureArDiagnostics();
                EnsureCameraAuthorization();
                if (m_HostService != null)
                    m_HostService.enabled = false;
            }
        }

        private void EnsureHostService()
        {
            if (m_HostService == null)
                m_HostService = gameObject.GetComponent<SpectatorHostService>() ?? gameObject.AddComponent<SpectatorHostService>();

            m_HostService.enabled = true;
        }

        private void EnsureClientService()
        {
            if (m_ClientService == null)
                m_ClientService = gameObject.GetComponent<SpectatorClientService>() ?? gameObject.AddComponent<SpectatorClientService>();

            m_ClientService.enabled = true;
        }

        private void EnsureArDiagnostics()
        {
            if (m_ArDiagnostics == null)
                m_ArDiagnostics = gameObject.GetComponent<SpectatorArDiagnostics>() ?? gameObject.AddComponent<SpectatorArDiagnostics>();

            m_ArDiagnostics.enabled = true;
            m_ArDiagnostics.LogSnapshot("Bootstrap");
        }

        private void EnsureCameraAuthorization()
        {
            if (m_CameraAuthorizationRoutine != null)
                return;

            m_CameraAuthorizationRoutine = StartCoroutine(RequestCameraAuthorizationIfNeeded());
        }

        private IEnumerator RequestCameraAuthorizationIfNeeded()
        {
            if (Application.HasUserAuthorization(UserAuthorization.WebCam))
            {
                Debug.Log("[SpectatorAR] Webcam authorization already granted before startup.");
                m_ArDiagnostics?.LogSnapshot("CameraAuthAlreadyGranted");
                m_CameraAuthorizationRoutine = null;
                yield break;
            }

            Debug.Log("[SpectatorAR] Requesting webcam authorization.");
            yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);
            bool granted = Application.HasUserAuthorization(UserAuthorization.WebCam);
            Debug.Log($"[SpectatorAR] Webcam authorization request finished. granted={granted}");
            m_ArDiagnostics?.LogSnapshot("AfterCameraAuthRequest");
            m_CameraAuthorizationRoutine = null;
        }

        private static void EnsureSpectatorArView()
        {
            ARSession session = FindFirstObjectByType<ARSession>(FindObjectsInactive.Include);
            if (session != null && !session.enabled)
                session.enabled = true;

            if (session != null)
            {
                ARInputManager inputManager = session.GetComponent<ARInputManager>();
                if (inputManager == null)
                {
                    session.gameObject.AddComponent<ARInputManager>();
                    Debug.Log("[SpectatorAR] Added missing ARInputManager to ARSession.");
                }
            }

            XROrigin xrOrigin = FindFirstObjectByType<XROrigin>(FindObjectsInactive.Include);
            if (xrOrigin != null)
            {
                ARAnchorManager anchorManager = xrOrigin.GetComponent<ARAnchorManager>();
                if (anchorManager == null)
                {
                    anchorManager = xrOrigin.gameObject.AddComponent<ARAnchorManager>();
                    Debug.Log($"[SpectatorAR] Added ARAnchorManager to XROrigin '{xrOrigin.name}'.");
                }

                anchorManager.enabled = true;
            }
            else
            {
                Debug.LogWarning("[SpectatorAR] No XROrigin found for ARAnchorManager setup.");
            }

            Camera targetCamera = Camera.main;
            if (targetCamera == null)
                targetCamera = FindFirstObjectByType<Camera>(FindObjectsInactive.Include);

            if (targetCamera == null)
            {
                Debug.LogWarning("[Spectator] No camera found for AR spectator view.");
                return;
            }

            ARCameraManager cameraManager = targetCamera.GetComponent<ARCameraManager>();
            if (cameraManager == null)
            {
                cameraManager = targetCamera.gameObject.AddComponent<ARCameraManager>();
                Debug.Log($"[SpectatorAR] Added ARCameraManager to camera '{targetCamera.name}'.");
            }

            cameraManager.requestedFacingDirection = CameraFacingDirection.World;
            cameraManager.autoFocusRequested = true;
            cameraManager.enabled = true;

            ARCameraBackground cameraBackground = targetCamera.GetComponent<ARCameraBackground>();
            if (cameraBackground == null)
            {
                cameraBackground = targetCamera.gameObject.AddComponent<ARCameraBackground>();
                Debug.Log($"[SpectatorAR] Added ARCameraBackground to camera '{targetCamera.name}'.");
            }

            cameraBackground.enabled = true;
            EnsurePeopleOcclusion(targetCamera);
            Debug.Log(
                $"[SpectatorAR] Configured spectator AR view. sessionFound={session != null}, sessionEnabled={(session != null && session.enabled)}, camera={targetCamera.name}, cameraEnabled={targetCamera.enabled}, cameraManagerEnabled={cameraManager.enabled}, backgroundEnabled={cameraBackground.enabled}, webcamAuth={Application.HasUserAuthorization(UserAuthorization.WebCam)}");
        }

        private static void EnsurePeopleOcclusion(Camera targetCamera)
        {
            if (targetCamera == null)
                return;

            if (!PlatformRuntime.IsIOSButNotVisionOS && !Application.isEditor)
                return;

            AROcclusionManager occlusionManager = targetCamera.GetComponent<AROcclusionManager>();
            if (occlusionManager == null)
            {
                occlusionManager = targetCamera.gameObject.AddComponent<AROcclusionManager>();
                Debug.Log($"[SpectatorAR] Added AROcclusionManager to camera '{targetCamera.name}'.");
            }

            ARShaderOcclusion shaderOcclusion = targetCamera.GetComponent<ARShaderOcclusion>();
            if (shaderOcclusion != null && shaderOcclusion.enabled)
            {
                Debug.LogWarning(
                    $"[SpectatorAR] Disabled ARShaderOcclusion on camera '{targetCamera.name}'. " +
                    "iOS spectator uses AROcclusionManager + ARCameraBackground for people occlusion.");
                shaderOcclusion.enabled = false;
            }

            ARCameraBackground cameraBackground = targetCamera.GetComponent<ARCameraBackground>();
            if (cameraBackground != null)
            {
                ConfigurePeopleOcclusionBackgroundMaterial(cameraBackground);
            }

            SpectatorHumanOcclusionGlobals occlusionGlobals =
                targetCamera.GetComponent<SpectatorHumanOcclusionGlobals>() ??
                targetCamera.gameObject.AddComponent<SpectatorHumanOcclusionGlobals>();
            occlusionGlobals.enabled = true;

            occlusionManager.requestedHumanStencilMode = SpectatorPeopleOcclusionStencilMode;
            occlusionManager.requestedHumanDepthMode = SpectatorPeopleOcclusionDepthMode;
            occlusionManager.requestedEnvironmentDepthMode = EnvironmentDepthMode.Disabled;
            occlusionManager.requestedOcclusionPreferenceMode = OcclusionPreferenceMode.PreferHumanOcclusion;
            occlusionManager.environmentDepthTemporalSmoothingRequested = false;
            occlusionManager.enabled = true;

            LogPeopleOcclusionConfiguration(targetCamera, occlusionManager, cameraBackground, shaderOcclusion, "InitialConfig");
        }

        private static void ConfigurePeopleOcclusionBackgroundMaterial(ARCameraBackground cameraBackground)
        {
            if (cameraBackground == null)
                return;

            if (!PlatformRuntime.IsIOSButNotVisionOS || Application.isEditor)
            {
                cameraBackground.useCustomMaterial = false;
                cameraBackground.customMaterial = null;
                return;
            }

            Material patchedBackgroundMaterial = GetOrCreateSpectatorArKitBackgroundMaterial();
            if (patchedBackgroundMaterial == null)
            {
                cameraBackground.useCustomMaterial = false;
                cameraBackground.customMaterial = null;
                return;
            }

            cameraBackground.useCustomMaterial = true;
            cameraBackground.customMaterial = patchedBackgroundMaterial;

            if (!s_HasLoggedSpectatorArKitBackgroundOverride)
            {
                s_HasLoggedSpectatorArKitBackgroundOverride = true;
                Debug.Log(
                    "[SpectatorAR] Using patched ARKit AfterOpaques background material for iOS people occlusion.");
            }
        }

        private static Material GetOrCreateSpectatorArKitBackgroundMaterial()
        {
            if (s_SpectatorArKitBackgroundMaterial != null)
                return s_SpectatorArKitBackgroundMaterial;

            Shader shader = Resources.Load<Shader>(SpectatorArKitBackgroundShaderResourcePath);
            if (shader == null)
                shader = Shader.Find(SpectatorArKitBackgroundShaderName);

            if (shader == null)
            {
                if (!s_HasLoggedMissingSpectatorArKitBackgroundShader)
                {
                    s_HasLoggedMissingSpectatorArKitBackgroundShader = true;
                    Debug.LogWarning(
                        $"[SpectatorAR] Failed to load patched spectator ARKit background shader. " +
                        $"resourcePath='{SpectatorArKitBackgroundShaderResourcePath}', shaderName='{SpectatorArKitBackgroundShaderName}'.");
                }

                return null;
            }

            s_SpectatorArKitBackgroundMaterial = new Material(shader)
            {
                name = "Spectator ARKit Background (Fixed)",
                hideFlags = HideFlags.HideAndDontSave
            };
            return s_SpectatorArKitBackgroundMaterial;
        }

        private static void LogPeopleOcclusionConfiguration(
            Camera targetCamera,
            AROcclusionManager occlusionManager,
            ARCameraBackground cameraBackground,
            ARShaderOcclusion shaderOcclusion,
            string reason)
        {
            string stencilSupport = DescribeSupported(occlusionManager?.descriptor?.humanSegmentationStencilImageSupported);
            string depthSupport = DescribeSupported(occlusionManager?.descriptor?.humanSegmentationDepthImageSupported);
            string humanStencilDescriptor = DescribeOcclusionTextureDescriptor(occlusionManager, humanStencil: true);
            string humanDepthDescriptor = DescribeOcclusionTextureDescriptor(occlusionManager, humanStencil: false);
            Material backgroundMaterial = cameraBackground != null ? cameraBackground.material : null;
            Debug.Log(
                $"[SpectatorAR] {reason}: route=ARCameraBackground, camera={(targetCamera != null ? targetCamera.name : "<none>")}, " +
                $"stencilSupport={stencilSupport}, depthSupport={depthSupport}, " +
                $"requestedStencil={(occlusionManager != null ? occlusionManager.requestedHumanStencilMode.ToString() : "<none>")}, " +
                $"currentStencil={(occlusionManager != null ? occlusionManager.currentHumanStencilMode.ToString() : "<none>")}, " +
                $"requestedDepth={(occlusionManager != null ? occlusionManager.requestedHumanDepthMode.ToString() : "<none>")}, " +
                $"currentDepth={(occlusionManager != null ? occlusionManager.currentHumanDepthMode.ToString() : "<none>")}, " +
                $"requestedPreference={(occlusionManager != null ? occlusionManager.requestedOcclusionPreferenceMode.ToString() : "<none>")}, " +
                $"currentPreference={(occlusionManager != null ? occlusionManager.currentOcclusionPreferenceMode.ToString() : "<none>")}, " +
                $"backgroundEnabled={(cameraBackground != null && cameraBackground.enabled)}, " +
                $"backgroundRenderingEnabled={(cameraBackground != null && cameraBackground.backgroundRenderingEnabled)}, " +
                $"backgroundRenderMode={(cameraBackground != null ? cameraBackground.currentRenderingMode.ToString() : "<none>")}, " +
                $"backgroundUseCustomMaterial={(cameraBackground != null && cameraBackground.useCustomMaterial)}, " +
                $"backgroundShader={(backgroundMaterial != null && backgroundMaterial.shader != null ? backgroundMaterial.shader.name : "<none>")}, " +
                $"backgroundPassCount={(backgroundMaterial != null ? backgroundMaterial.passCount.ToString() : "<none>")}, " +
                $"backgroundHumanKeyword={DescribeMaterialKeyword(backgroundMaterial, ArKitHumanSegmentationKeyword)}, " +
                $"backgroundEnvDepthKeyword={DescribeMaterialKeyword(backgroundMaterial, ArKitEnvironmentDepthKeyword)}, " +
                $"backgroundHumanStencilTex={DescribeMaterialTexture(backgroundMaterial, HumanStencilPropertyId)}, " +
                $"backgroundHumanDepthTex={DescribeMaterialTexture(backgroundMaterial, HumanDepthPropertyId)}, " +
                $"backgroundCameraForwardScale={DescribeMaterialFloat(backgroundMaterial, CameraForwardScalePropertyId)}, " +
                $"cameraDepthTextureMode={(targetCamera != null ? targetCamera.depthTextureMode.ToString() : "<none>")}, " +
                $"humanStencilDesc={humanStencilDescriptor}, humanDepthDesc={humanDepthDescriptor}, " +
                $"shaderOcclusionPresent={(shaderOcclusion != null)}, shaderOcclusionEnabled={(shaderOcclusion != null && shaderOcclusion.enabled)}, " +
                $"platformIOS={PlatformRuntime.IsIOSButNotVisionOS}, editor={Application.isEditor}");
        }

        private static bool TryRelaxPeopleOcclusionConfiguration(
            Camera targetCamera,
            AROcclusionManager occlusionManager,
            string reason)
        {
            if (targetCamera == null || occlusionManager == null)
                return false;

            if (ARSession.state != ARSessionState.SessionTracking)
                return false;

            if (occlusionManager.descriptor?.humanSegmentationStencilImageSupported != Supported.Supported ||
                occlusionManager.descriptor?.humanSegmentationDepthImageSupported != Supported.Supported)
            {
                return false;
            }

            if (occlusionManager.currentHumanStencilMode.Enabled() &&
                occlusionManager.currentHumanDepthMode.Enabled())
            {
                return false;
            }

            if (occlusionManager.requestedHumanStencilMode == SpectatorPeopleOcclusionFallbackStencilMode &&
                occlusionManager.requestedHumanDepthMode == SpectatorPeopleOcclusionFallbackDepthMode)
            {
                return false;
            }

            occlusionManager.requestedHumanStencilMode = SpectatorPeopleOcclusionFallbackStencilMode;
            occlusionManager.requestedHumanDepthMode = SpectatorPeopleOcclusionFallbackDepthMode;
            occlusionManager.requestedOcclusionPreferenceMode = OcclusionPreferenceMode.PreferHumanOcclusion;

            Debug.LogWarning(
                $"[SpectatorAR] {reason}: human occlusion is still disabled while tracking is active. Falling back to stencil={SpectatorPeopleOcclusionFallbackStencilMode}, depth={SpectatorPeopleOcclusionFallbackDepthMode}.");
            LogPeopleOcclusionConfiguration(
                targetCamera,
                occlusionManager,
                targetCamera.GetComponent<ARCameraBackground>(),
                targetCamera.GetComponent<ARShaderOcclusion>(),
                "FallbackConfig");
            return true;
        }

        private static string DescribeOcclusionTextureDescriptor(AROcclusionManager occlusionManager, bool humanStencil)
        {
            if (occlusionManager == null || occlusionManager.subsystem == null)
                return "<no-subsystem>";

            bool success = humanStencil
                ? occlusionManager.subsystem.TryGetHumanStencil(out XRTextureDescriptor descriptor)
                : occlusionManager.subsystem.TryGetHumanDepth(out descriptor);

            if (!success)
                return "<unavailable>";

            return $"valid={descriptor.valid}, type={descriptor.textureType}, size={descriptor.width}x{descriptor.height}, propertyId={descriptor.propertyNameId}";
        }

        private static string DescribeSupported(Supported? supported)
        {
            return supported.HasValue ? supported.Value.ToString() : "<unknown>";
        }

        private static string DescribeMaterialKeyword(Material material, string keyword)
        {
            if (material == null)
                return "<no-material>";

            return material.IsKeywordEnabled(keyword) ? "Enabled" : "Disabled";
        }

        private static string DescribeMaterialTexture(Material material, int propertyId)
        {
            if (material == null)
                return "<no-material>";

            Texture texture = material.GetTexture(propertyId);
            if (texture == null)
                return "<unassigned>";

            return $"{texture.width}x{texture.height}";
        }

        private static string DescribeMaterialFloat(Material material, int propertyId)
        {
            if (material == null)
                return "<no-material>";

            if (!material.HasProperty(propertyId))
                return "<missing>";

            return material.GetFloat(propertyId).ToString("F4");
        }

        private static void DisableSceneRoleMismatches(AppRole role)
        {
            if (role != AppRole.IPadSpectator)
                return;

            DisableAll<ExhibitPlacementManager>();
            DisableAll<ChapterPlacementDirector>();
            DisableAll<AutoHandVisionOSBridge>();
            DisableAll<InteractionModuleManager>();
            HideAll<ExhibitControlPanel>();
            HideAll<PlacementPanelPalmRelocator>();
        }

        private static void DisableAll<T>() where T : Behaviour
        {
            T[] components = FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] != null)
                    components[i].enabled = false;
            }
        }

        private static void HideAll<T>() where T : Behaviour
        {
            T[] components = FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < components.Length; i++)
            {
                T component = components[i];
                if (component == null)
                    continue;

                component.enabled = false;
                if (component.GetComponentInChildren<SpectatorConnectPanel>(true) != null)
                    continue;

                if (component.gameObject.activeSelf)
                    component.gameObject.SetActive(false);
            }
        }

        private sealed class SpectatorArDiagnostics : MonoBehaviour
        {
            private readonly List<XRInputSubsystem> m_InputSubsystems = new();
            private readonly List<XRSessionSubsystem> m_SessionSubsystems = new();
            private readonly List<XRCameraSubsystem> m_CameraSubsystems = new();
            private Coroutine m_StartupRoutine;
            private bool m_HasRelaxedOcclusionConfiguration;

            private void OnEnable()
            {
                ARSession.stateChanged += OnArSessionStateChanged;
                if (m_StartupRoutine != null)
                    StopCoroutine(m_StartupRoutine);

                m_StartupRoutine = StartCoroutine(LogStartupSnapshots());
            }

            private void OnDisable()
            {
                ARSession.stateChanged -= OnArSessionStateChanged;
                if (m_StartupRoutine != null)
                {
                    StopCoroutine(m_StartupRoutine);
                    m_StartupRoutine = null;
                }
            }

            public void LogSnapshot(string reason)
            {
                SubsystemManager.GetSubsystems(m_InputSubsystems);
                SubsystemManager.GetSubsystems(m_SessionSubsystems);
                SubsystemManager.GetSubsystems(m_CameraSubsystems);

                bool inputRunning = false;
                for (int i = 0; i < m_InputSubsystems.Count; i++)
                {
                    if (m_InputSubsystems[i] != null && m_InputSubsystems[i].running)
                    {
                        inputRunning = true;
                        break;
                    }
                }

                XRSessionSubsystem sessionSubsystem = FindRunningSubsystem(m_SessionSubsystems);
                XRCameraSubsystem cameraSubsystem = FindRunningSubsystem(m_CameraSubsystems);

                ARSession session = FindFirstObjectByType<ARSession>(FindObjectsInactive.Include);
                Camera targetCamera = Camera.main;
                if (targetCamera == null)
                    targetCamera = FindFirstObjectByType<Camera>(FindObjectsInactive.Include);

                ARCameraManager cameraManager = targetCamera != null ? targetCamera.GetComponent<ARCameraManager>() : null;
                ARCameraBackground cameraBackground = targetCamera != null ? targetCamera.GetComponent<ARCameraBackground>() : null;
                AROcclusionManager occlusionManager = targetCamera != null ? targetCamera.GetComponent<AROcclusionManager>() : null;
                ARShaderOcclusion shaderOcclusion = targetCamera != null ? targetCamera.GetComponent<ARShaderOcclusion>() : null;
                string humanStencilDescriptor = DescribeOcclusionTextureDescriptor(occlusionManager, humanStencil: true);
                string humanDepthDescriptor = DescribeOcclusionTextureDescriptor(occlusionManager, humanStencil: false);
                Material backgroundMaterial = cameraBackground != null ? cameraBackground.material : null;

                Debug.Log(
                    $"[SpectatorAR] {reason}: sessionState={ARSession.state}, webcamAuth={Application.HasUserAuthorization(UserAuthorization.WebCam)}, " +
                    $"sessionSubsystems={m_SessionSubsystems.Count}, sessionRunning={(sessionSubsystem != null && sessionSubsystem.running)}, " +
                    $"cameraSubsystems={m_CameraSubsystems.Count}, cameraRunning={(cameraSubsystem != null && cameraSubsystem.running)}, " +
                    $"inputSubsystems={m_InputSubsystems.Count}, inputSubsystemRunning={inputRunning}, arSessionEnabled={(session != null && session.enabled)}, " +
                    $"camera={(targetCamera != null ? targetCamera.name : "<none>")}, cameraEnabled={(targetCamera != null && targetCamera.enabled)}, " +
                    $"cameraManagerEnabled={(cameraManager != null && cameraManager.enabled)}, " +
                    $"cameraBackgroundEnabled={(cameraBackground != null && cameraBackground.enabled)}, " +
                    $"backgroundRenderingEnabled={(cameraBackground != null && cameraBackground.backgroundRenderingEnabled)}, " +
                    $"backgroundRenderMode={(cameraBackground != null ? cameraBackground.currentRenderingMode.ToString() : "<none>")}, " +
                    $"backgroundUseCustomMaterial={(cameraBackground != null && cameraBackground.useCustomMaterial)}, " +
                    $"backgroundShader={(backgroundMaterial != null && backgroundMaterial.shader != null ? backgroundMaterial.shader.name : "<none>")}, " +
                    $"backgroundPassCount={(backgroundMaterial != null ? backgroundMaterial.passCount.ToString() : "<none>")}, " +
                    $"backgroundHumanKeyword={DescribeMaterialKeyword(backgroundMaterial, ArKitHumanSegmentationKeyword)}, " +
                    $"backgroundEnvDepthKeyword={DescribeMaterialKeyword(backgroundMaterial, ArKitEnvironmentDepthKeyword)}, " +
                    $"backgroundHumanStencilTex={DescribeMaterialTexture(backgroundMaterial, HumanStencilPropertyId)}, " +
                    $"backgroundHumanDepthTex={DescribeMaterialTexture(backgroundMaterial, HumanDepthPropertyId)}, " +
                    $"backgroundCameraForwardScale={DescribeMaterialFloat(backgroundMaterial, CameraForwardScalePropertyId)}, " +
                    $"cameraDepthTextureMode={(targetCamera != null ? targetCamera.depthTextureMode.ToString() : "<none>")}, " +
                    $"occlusionManagerEnabled={(occlusionManager != null && occlusionManager.enabled)}, " +
                    $"humanStencilSupport={DescribeSupported(occlusionManager?.descriptor?.humanSegmentationStencilImageSupported)}, " +
                    $"humanDepthSupport={DescribeSupported(occlusionManager?.descriptor?.humanSegmentationDepthImageSupported)}, " +
                    $"requestedHumanStencil={(occlusionManager != null ? occlusionManager.requestedHumanStencilMode.ToString() : "<none>")}, " +
                    $"currentHumanStencil={(occlusionManager != null ? occlusionManager.currentHumanStencilMode.ToString() : "<none>")}, " +
                    $"requestedHumanDepth={(occlusionManager != null ? occlusionManager.requestedHumanDepthMode.ToString() : "<none>")}, " +
                    $"currentHumanDepth={(occlusionManager != null ? occlusionManager.currentHumanDepthMode.ToString() : "<none>")}, " +
                    $"requestedOcclusionPreference={(occlusionManager != null ? occlusionManager.requestedOcclusionPreferenceMode.ToString() : "<none>")}, " +
                    $"currentOcclusionPreference={(occlusionManager != null ? occlusionManager.currentOcclusionPreferenceMode.ToString() : "<none>")}, " +
                    $"humanStencilDesc={humanStencilDescriptor}, humanDepthDesc={humanDepthDescriptor}, " +
                    $"shaderOcclusionPresent={(shaderOcclusion != null)}, shaderOcclusionEnabled={(shaderOcclusion != null && shaderOcclusion.enabled)}");

                if (!m_HasRelaxedOcclusionConfiguration &&
                    SpectatorSceneBootstrap.TryRelaxPeopleOcclusionConfiguration(targetCamera, occlusionManager, reason))
                {
                    m_HasRelaxedOcclusionConfiguration = true;
                }
            }

            private void OnArSessionStateChanged(ARSessionStateChangedEventArgs args)
            {
                Debug.Log($"[SpectatorAR] ARSession.stateChanged -> {args.state}");
                LogSnapshot("SessionStateChanged");
            }

            private IEnumerator LogStartupSnapshots()
            {
                yield return null;
                LogSnapshot("AfterFirstFrame");
                yield return new WaitForSeconds(1f);
                LogSnapshot("After1s");
                yield return new WaitForSeconds(2f);
                LogSnapshot("After3s");
                m_StartupRoutine = null;
            }

            private static T FindRunningSubsystem<T>(List<T> subsystems) where T : class
            {
                for (int i = 0; i < subsystems.Count; i++)
                {
                    if (subsystems[i] is IntegratedSubsystem subsystem && subsystem.running)
                        return subsystems[i];
                }

                for (int i = 0; i < subsystems.Count; i++)
                {
                    if (subsystems[i] != null)
                        return subsystems[i];
                }

                return null;
            }
        }
    }
}
