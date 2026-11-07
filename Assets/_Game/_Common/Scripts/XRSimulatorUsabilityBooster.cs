using System;
using System.Reflection;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Improves XR simulator ergonomics in Play Mode.
/// Main focus: easier XR Origin piloting from desktop input.
/// </summary>
[DefaultExecutionOrder(10000)]
public sealed class XRSimulatorUsabilityBooster : MonoBehaviour
{
    static XRSimulatorUsabilityBooster s_Instance;

    XRInteractionSimulator m_InteractionSimulator;
    XRDeviceSimulator m_DeviceSimulator;
    SimulatedDeviceLifecycleManager m_Lifecycle;
    MethodInfo m_SwitchDeviceModeMethod;
    bool m_SimulatorBaselineCaptured;
    UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation.Space m_InteractionSimulatorBaselineTranslateSpace;
    XRDeviceSimulator.Space m_DeviceSimulatorBaselineKeyboardTranslateSpace;
    XRDeviceSimulator.Space m_DeviceSimulatorBaselineMouseTranslateSpace;
    SimulatedDeviceLifecycleManager.DeviceMode m_SimulatorBaselineDeviceMode;
    bool m_BoosterTouchedSimulatorState;

    XROrigin m_XROrigin;
    Transform m_OriginTransform;
    Transform m_ViewTransform;
    CharacterController m_CharacterController;
    int m_ActiveSceneHandle = int.MinValue;

    bool m_OriginPoseInitialized;
    Vector3 m_InitialOriginPosition;
    Quaternion m_InitialOriginRotation;
    bool m_SpawnPoseCached;
    Vector3 m_CachedSpawnPosition;
    Quaternion m_CachedSpawnRotation;
    int m_ForceSpawnFramesRemaining;

    bool m_StickyPilotMode = false;
    bool m_CursorLockedByBooster;
    int m_SkipYawFrames;

    float m_RigYawSensitivity = 0.12f;
    float m_MaxYawPerFrame = 4.5f;
    float m_ManualVerticalSpeed = 3.5f;
    float m_GravityAcceleration = -9.81f;
    float m_VerticalVelocity;

    const float k_RigMoveSpeed = 1.8f;
    const float k_ControllerHeight = 1.8f;
    const float k_EditorHeadHeight = 0.8f;
    const float k_ControllerRadius = 0.22f;
    const float k_GroundedStickVelocity = -2.0f;
    const string k_SpawnPointName = "XROriginSpawnPoint";
    const string k_TargetSceneNamePrefix = "ProcedureTest";
    const int k_ForceSpawnFrames = 40;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (s_Instance != null)
            return;

        var go = new GameObject("XR Simulator Usability Booster");
        s_Instance = go.AddComponent<XRSimulatorUsabilityBooster>();
        DontDestroyOnLoad(go);
    }

    void Update()
    {
        HandleActiveSceneChange();
        if (!IsTargetSceneActive())
        {
            EnsureSimulator();
            ResetBoosterStateOutsideTarget();
            return;
        }

        EnsureSimulator();
        EnsureOrigin();

#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        if (kb == null || mouse == null)
            return;

        if (kb.f6Key.wasPressedThisFrame)
            m_StickyPilotMode = !m_StickyPilotMode;
        if (kb.escapeKey.wasPressedThisFrame && m_StickyPilotMode)
            m_StickyPilotMode = false;

        if (kb.f7Key.wasPressedThisFrame)
            ResetOriginPose();

        if (kb.f9Key.wasPressedThisFrame)
            TryCacheSpawnPointAndApply(force: true);

        if (kb.f2Key.wasPressedThisFrame)
            TryToggleDeviceMode();

        if (kb.f3Key.wasPressedThisFrame && HasAnySimulator())
            ToggleSimulatorTranslateSpace();

        var rightMouseHeld = mouse.rightButton.isPressed;
        if (mouse.rightButton.wasPressedThisFrame)
            m_SkipYawFrames = Mathf.Max(m_SkipYawFrames, 1);

        var yawActive = m_StickyPilotMode || rightMouseHeld;
        var moveActive = kb.wKey.isPressed || kb.aKey.isPressed || kb.sKey.isPressed || kb.dKey.isPressed ||
            kb.spaceKey.isPressed || kb.qKey.isPressed || kb.eKey.isPressed;
        var pilotActive = moveActive || yawActive;
        // 仅粘性驾驶模式锁鼠标，右键按住拖拽不锁，避免从任意点击位置开始时出现首帧跳变。
        UpdateCursorLockForPilotMode(m_StickyPilotMode);
        if (pilotActive && m_OriginTransform != null)
            UpdateOriginPilot(kb, mouse, yawActive);
#endif
    }

    void LateUpdate()
    {
        if (!IsTargetSceneActive())
            return;

        if (m_OriginTransform == null || !m_SpawnPoseCached || m_ForceSpawnFramesRemaining <= 0)
            return;

        m_OriginTransform.SetPositionAndRotation(m_CachedSpawnPosition, m_CachedSpawnRotation);
        m_ForceSpawnFramesRemaining--;
    }

    void EnsureSimulator()
    {
        if (m_InteractionSimulator == null)
            m_InteractionSimulator = XRInteractionSimulator.instance ?? FindFirstObjectByType<XRInteractionSimulator>();

        if (m_DeviceSimulator == null)
            m_DeviceSimulator = XRDeviceSimulator.instance ?? FindFirstObjectByType<XRDeviceSimulator>();

        if (!HasAnySimulator())
            return;

        if (m_Lifecycle == null)
        {
            if (m_InteractionSimulator != null)
                m_Lifecycle = m_InteractionSimulator.deviceLifecycleManager;

            if (m_Lifecycle == null)
                m_Lifecycle = FindFirstObjectByType<SimulatedDeviceLifecycleManager>();
        }

        if (m_SwitchDeviceModeMethod == null && m_Lifecycle != null)
        {
            m_SwitchDeviceModeMethod = typeof(SimulatedDeviceLifecycleManager).GetMethod(
                "SwitchDeviceMode",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }

        if (!m_SimulatorBaselineCaptured)
        {
            if (m_InteractionSimulator != null)
                m_InteractionSimulatorBaselineTranslateSpace = m_InteractionSimulator.translateSpace;

            if (m_DeviceSimulator != null)
            {
                m_DeviceSimulatorBaselineKeyboardTranslateSpace = m_DeviceSimulator.keyboardTranslateSpace;
                m_DeviceSimulatorBaselineMouseTranslateSpace = m_DeviceSimulator.mouseTranslateSpace;
            }

            if (m_Lifecycle != null)
                m_SimulatorBaselineDeviceMode = m_Lifecycle.deviceMode;

            m_SimulatorBaselineCaptured = true;
        }
    }

    bool HasAnySimulator()
    {
        return m_InteractionSimulator != null || m_DeviceSimulator != null;
    }

    void EnsureOrigin()
    {
        if (m_XROrigin == null)
            m_XROrigin = FindTargetXROrigin();

        if (m_XROrigin == null)
            return;

        m_OriginTransform = m_XROrigin.Origin != null ? m_XROrigin.Origin.transform : m_XROrigin.transform;
        m_ViewTransform = m_XROrigin.Camera != null ? m_XROrigin.Camera.transform : m_OriginTransform;
        ApplyEditorOnlyHeadHeightOverride();
        EnsureCharacterController();
        SyncCharacterControllerToHead();

        if (m_OriginPoseInitialized || m_OriginTransform == null)
            return;

        TryCacheSpawnPointAndApply(force: false);
        m_InitialOriginPosition = m_OriginTransform.position;
        m_InitialOriginRotation = m_OriginTransform.rotation;
        m_OriginPoseInitialized = true;
    }

    XROrigin FindTargetXROrigin()
    {
        var origins = FindObjectsByType<XROrigin>(FindObjectsSortMode.None);
        if (origins == null || origins.Length == 0)
            return null;

        var activeScene = SceneManager.GetActiveScene();
        var mainCamera = Camera.main;

        // 1) Active scene + exact main camera binding
        if (mainCamera != null)
        {
            foreach (var origin in origins)
            {
                if (origin == null)
                    continue;

                if (origin.gameObject.scene == activeScene && origin.Camera == mainCamera)
                    return origin;
            }
        }

        // 2) Any origin in active scene
        foreach (var origin in origins)
        {
            if (origin != null && origin.gameObject.scene == activeScene)
                return origin;
        }

        // 3) Fallback: origin bound to main camera in any scene
        if (mainCamera != null)
        {
            foreach (var origin in origins)
            {
                if (origin != null && origin.Camera == mainCamera)
                    return origin;
            }
        }

        // 4) Last resort
        return origins[0];
    }

#if ENABLE_INPUT_SYSTEM
    void UpdateOriginPilot(Keyboard kb, Mouse mouse, bool yawActive)
    {
        if (!IsTargetSceneActive())
            return;

        var moveX = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
        var moveY = ((kb.spaceKey.isPressed || kb.eKey.isPressed) ? 1f : 0f) - (kb.qKey.isPressed ? 1f : 0f);
        var moveZ = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);

        var forward = m_ViewTransform != null ? Vector3.ProjectOnPlane(m_ViewTransform.forward, Vector3.up) : m_OriginTransform.forward;
        if (forward.sqrMagnitude < 0.0001f)
            forward = m_OriginTransform.forward;
        forward.Normalize();

        var right = Vector3.Cross(Vector3.up, forward).normalized;
        var moveDir = right * moveX + Vector3.up * moveY + forward * moveZ;

        var speed = k_RigMoveSpeed;
        if (m_CharacterController != null)
        {
            var horizontalMove = new Vector3(moveDir.x, 0f, moveDir.z);
            if (horizontalMove.sqrMagnitude > 1f)
                horizontalMove.Normalize();

            if (m_CharacterController.isGrounded && m_VerticalVelocity < 0f)
                m_VerticalVelocity = k_GroundedStickVelocity;

            m_VerticalVelocity += m_GravityAcceleration * Time.unscaledDeltaTime;
            if (Mathf.Abs(moveY) > 0.01f)
                m_VerticalVelocity = moveY * m_ManualVerticalSpeed;

            var displacement = (horizontalMove * speed + Vector3.up * m_VerticalVelocity) * Time.unscaledDeltaTime;
            var collisionFlags = m_CharacterController.Move(displacement);

            if ((collisionFlags & CollisionFlags.Above) != 0 && m_VerticalVelocity > 0f)
                m_VerticalVelocity = 0f;
            if ((collisionFlags & CollisionFlags.Below) != 0 && m_VerticalVelocity < 0f)
                m_VerticalVelocity = k_GroundedStickVelocity;
        }
        else
        {
            m_OriginTransform.position += moveDir * (speed * Time.unscaledDeltaTime);
        }

        if (!yawActive)
            return;

        if (m_SkipYawFrames > 0)
        {
            m_SkipYawFrames--;
            return;
        }

        var yawDelta = mouse.delta.ReadValue().x * m_RigYawSensitivity;
        yawDelta = Mathf.Clamp(yawDelta, -m_MaxYawPerFrame, m_MaxYawPerFrame);
        if (Mathf.Abs(yawDelta) > 0.001f)
            m_OriginTransform.Rotate(Vector3.up, yawDelta, UnityEngine.Space.World);
    }
#endif

    void ResetOriginPose()
    {
        if (!m_OriginPoseInitialized || m_OriginTransform == null)
            return;

        m_OriginTransform.SetPositionAndRotation(m_InitialOriginPosition, m_InitialOriginRotation);
        m_VerticalVelocity = 0f;
    }

    void OnDisable()
    {
        ReleaseCursorLockIfNeeded();
    }

    void OnDestroy()
    {
        ReleaseCursorLockIfNeeded();
    }

    void EnsureCharacterController()
    {
        if (!Application.isEditor || !IsTargetSceneActive() || m_OriginTransform == null)
            return;

        if (m_CharacterController != null && m_CharacterController.gameObject == m_OriginTransform.gameObject)
            return;

        m_CharacterController = m_OriginTransform.GetComponent<CharacterController>();
        if (m_CharacterController == null)
            return;

        m_CharacterController.minMoveDistance = 0f;
    }

    void HandleActiveSceneChange()
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid())
            return;

        if (scene.handle == m_ActiveSceneHandle)
            return;

        m_ActiveSceneHandle = scene.handle;
        m_XROrigin = null;
        m_OriginTransform = null;
        m_ViewTransform = null;
        m_CharacterController = null;
        m_OriginPoseInitialized = false;
        m_SpawnPoseCached = false;
        m_ForceSpawnFramesRemaining = 0;
        m_SkipYawFrames = 0;
    }

    static bool IsTargetSceneActive()
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || string.IsNullOrEmpty(scene.name))
            return false;

#if UNITY_EDITOR
        if (Application.isEditor)
            return true;
#endif

        return scene.name.StartsWith(k_TargetSceneNamePrefix, StringComparison.Ordinal);
    }

    void ResetBoosterStateOutsideTarget()
    {
        m_StickyPilotMode = false;
        m_VerticalVelocity = 0f;
        m_SkipYawFrames = 0;
        UpdateCursorLockForPilotMode(false);
        RestoreSimulatorBaselineIfNeeded();
    }

    void RestoreSimulatorBaselineIfNeeded()
    {
        if (!m_BoosterTouchedSimulatorState || !m_SimulatorBaselineCaptured)
            return;

        if (m_InteractionSimulator != null && m_InteractionSimulator.translateSpace != m_InteractionSimulatorBaselineTranslateSpace)
            m_InteractionSimulator.translateSpace = m_InteractionSimulatorBaselineTranslateSpace;

        if (m_DeviceSimulator != null)
        {
            if (m_DeviceSimulator.keyboardTranslateSpace != m_DeviceSimulatorBaselineKeyboardTranslateSpace)
                m_DeviceSimulator.keyboardTranslateSpace = m_DeviceSimulatorBaselineKeyboardTranslateSpace;

            if (m_DeviceSimulator.mouseTranslateSpace != m_DeviceSimulatorBaselineMouseTranslateSpace)
                m_DeviceSimulator.mouseTranslateSpace = m_DeviceSimulatorBaselineMouseTranslateSpace;
        }

        if (m_Lifecycle != null && m_Lifecycle.deviceMode != m_SimulatorBaselineDeviceMode && m_SwitchDeviceModeMethod != null)
        {
            try
            {
                m_SwitchDeviceModeMethod.Invoke(m_Lifecycle, null);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"XRSimulatorUsabilityBooster: failed to restore simulator device mode. {ex.Message}", this);
            }
        }

        var translateSpaceRestored =
            (m_InteractionSimulator == null || m_InteractionSimulator.translateSpace == m_InteractionSimulatorBaselineTranslateSpace) &&
            (m_DeviceSimulator == null ||
                (m_DeviceSimulator.keyboardTranslateSpace == m_DeviceSimulatorBaselineKeyboardTranslateSpace &&
                 m_DeviceSimulator.mouseTranslateSpace == m_DeviceSimulatorBaselineMouseTranslateSpace));

        var deviceModeRestored = m_Lifecycle == null || m_Lifecycle.deviceMode == m_SimulatorBaselineDeviceMode;
        if (translateSpaceRestored && deviceModeRestored)
        {
            m_BoosterTouchedSimulatorState = false;
        }
    }

    void UpdateCursorLockForPilotMode(bool pilotActive)
    {
        if (!Application.isEditor)
            return;

        if (pilotActive)
        {
            if (m_CursorLockedByBooster)
                return;

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            m_CursorLockedByBooster = true;
            m_SkipYawFrames = 2;
            return;
        }

        ReleaseCursorLockIfNeeded();
    }

    void ReleaseCursorLockIfNeeded()
    {
        if (!m_CursorLockedByBooster)
            return;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        m_CursorLockedByBooster = false;
        m_SkipYawFrames = 1;
    }

    void TryCacheSpawnPointAndApply(bool force)
    {
        if (m_OriginTransform == null)
            return;

        if (!m_SpawnPoseCached || force)
        {
            var spawn = FindSpawnPointInActiveScene();
            if (spawn != null)
            {
                m_CachedSpawnPosition = spawn.position;
                m_CachedSpawnRotation = spawn.rotation;
                m_SpawnPoseCached = true;
            }
        }

        if (!m_SpawnPoseCached)
            return;

        // Prefer moving/aligning the camera viewpoint itself to the spawn point.
        // This handles cases where tracked camera has a local offset under XR Origin.
        var moved = m_XROrigin != null && m_XROrigin.MoveCameraToWorldLocation(m_CachedSpawnPosition);
        var desiredForward = Vector3.ProjectOnPlane(m_CachedSpawnRotation * Vector3.forward, Vector3.up);
        if (desiredForward.sqrMagnitude > 0.0001f && m_XROrigin != null)
        {
            desiredForward.Normalize();
            m_XROrigin.MatchOriginUpCameraForward(Vector3.up, desiredForward);
        }

        if (!moved)
            m_OriginTransform.SetPositionAndRotation(m_CachedSpawnPosition, m_CachedSpawnRotation);

        m_VerticalVelocity = 0f;
        m_ForceSpawnFramesRemaining = k_ForceSpawnFrames;
    }

    Transform FindSpawnPointInActiveScene()
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
            return null;

        var roots = scene.GetRootGameObjects();
        foreach (var root in roots)
        {
            var found = FindChildByNameRecursive(root.transform, k_SpawnPointName);
            if (found != null)
                return found;
        }

        return null;
    }

    static Transform FindChildByNameRecursive(Transform parent, string targetName)
    {
        if (parent.name == targetName)
            return parent;

        for (var i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i);
            var found = FindChildByNameRecursive(child, targetName);
            if (found != null)
                return found;
        }

        return null;
    }

    void SyncCharacterControllerToHead()
    {
        if (m_CharacterController == null || m_ViewTransform == null || m_OriginTransform == null)
            return;

        var localHeadPos = m_OriginTransform.InverseTransformPoint(m_ViewTransform.position);
        m_CharacterController.radius = k_ControllerRadius;
        m_CharacterController.height = k_ControllerHeight;
        m_CharacterController.center = new Vector3(localHeadPos.x, k_ControllerHeight * 0.5f, localHeadPos.z);
        m_CharacterController.stepOffset = Mathf.Min(0.35f, k_ControllerHeight - 0.05f);
    }

    void ApplyEditorOnlyHeadHeightOverride()
    {
#if UNITY_EDITOR
        if (!Application.isEditor || !IsTargetSceneActive() || m_XROrigin == null)
            return;

        if (Mathf.Abs(m_XROrigin.CameraYOffset - k_EditorHeadHeight) > 0.001f)
            m_XROrigin.CameraYOffset = k_EditorHeadHeight;
#endif
    }

    void TryToggleDeviceMode()
    {
        if (m_Lifecycle == null || m_SwitchDeviceModeMethod == null)
            return;

        try
        {
            m_SwitchDeviceModeMethod.Invoke(m_Lifecycle, null);
            m_BoosterTouchedSimulatorState = true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"XRSimulatorUsabilityBooster: failed to toggle device mode. {ex.Message}", this);
        }
    }

    void ToggleSimulatorTranslateSpace()
    {
        if (m_InteractionSimulator != null)
            m_InteractionSimulator.translateSpace = NextInteractionSimulatorSpace(m_InteractionSimulator.translateSpace);

        if (m_DeviceSimulator != null)
        {
            var next = NextDeviceSimulatorSpace(m_DeviceSimulator.keyboardTranslateSpace);
            m_DeviceSimulator.keyboardTranslateSpace = next;
            m_DeviceSimulator.mouseTranslateSpace = next;
        }

        m_BoosterTouchedSimulatorState = true;
    }

    static UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation.Space NextInteractionSimulatorSpace(UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation.Space current)
    {
        if (current == UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation.Space.Local)
            return UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation.Space.Parent;

        if (current == UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation.Space.Parent)
            return UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation.Space.Screen;

        return UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation.Space.Local;
    }

    static XRDeviceSimulator.Space NextDeviceSimulatorSpace(XRDeviceSimulator.Space current)
    {
        if (current == XRDeviceSimulator.Space.Local)
            return XRDeviceSimulator.Space.Parent;

        if (current == XRDeviceSimulator.Space.Parent)
            return XRDeviceSimulator.Space.Screen;

        return XRDeviceSimulator.Space.Local;
    }

}
