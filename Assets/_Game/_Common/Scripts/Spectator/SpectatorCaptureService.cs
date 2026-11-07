using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.EventSystems;

namespace VFXViewer
{
    [Serializable]
    internal sealed class SpectatorCaptureBridgeEvent
    {
        public string type;
        public string message;
        public string filePath;
        public float value;
    }

    internal static class SpectatorCaptureBridge
    {
        public static bool IsSupported => !Application.isEditor && PlatformRuntime.IsIOSButNotVisionOS;

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void SpectatorCapture_InstallHardwareCaptureControls();

        [DllImport("__Internal")]
        private static extern void SpectatorCapture_SaveImageToPhotos(string filePath);

        [DllImport("__Internal")]
        private static extern void SpectatorCapture_CaptureCurrentViewToPhotos();

        [DllImport("__Internal")]
        private static extern void SpectatorCapture_StartVideoRecording(bool enableMicrophone);

        [DllImport("__Internal")]
        private static extern void SpectatorCapture_StopVideoRecording();

        [DllImport("__Internal")]
        private static extern void SpectatorCapture_SetCameraZoomFactor(double zoomFactor);

        [DllImport("__Internal")]
        private static extern IntPtr SpectatorCapture_CopyNextEventJson();

        [DllImport("__Internal")]
        private static extern void SpectatorCapture_FreeString(IntPtr buffer);
#endif

        public static void SaveImageToPhotos(string filePath)
        {
            if (!IsSupported)
            {
                Debug.LogWarning("[SpectatorCapture] Saving photos is only supported on iOS spectator builds.");
                return;
            }

#if UNITY_IOS && !UNITY_EDITOR
            SpectatorCapture_SaveImageToPhotos(filePath ?? string.Empty);
#endif
        }

        public static void CaptureCurrentViewToPhotos()
        {
            if (!IsSupported)
            {
                Debug.LogWarning("[SpectatorCapture] Native photo capture is only supported on iOS spectator builds.");
                return;
            }

#if UNITY_IOS && !UNITY_EDITOR
            SpectatorCapture_CaptureCurrentViewToPhotos();
#endif
        }

        public static void InstallHardwareCaptureControls()
        {
            if (!IsSupported)
                return;

#if UNITY_IOS && !UNITY_EDITOR
            SpectatorCapture_InstallHardwareCaptureControls();
#endif
        }

        public static void StartVideoRecording(bool enableMicrophone)
        {
            if (!IsSupported)
            {
                Debug.LogWarning("[SpectatorCapture] Video recording is only supported on iOS spectator builds.");
                return;
            }

#if UNITY_IOS && !UNITY_EDITOR
            SpectatorCapture_StartVideoRecording(enableMicrophone);
#endif
        }

        public static void StopVideoRecording()
        {
            if (!IsSupported)
            {
                Debug.LogWarning("[SpectatorCapture] Video recording is only supported on iOS spectator builds.");
                return;
            }

#if UNITY_IOS && !UNITY_EDITOR
            SpectatorCapture_StopVideoRecording();
#endif
        }

        public static void SetCameraZoomFactor(float zoomFactor)
        {
            if (!IsSupported)
            {
                Debug.LogWarning("[SpectatorCapture] Camera zoom is only supported on iOS spectator builds.");
                return;
            }

#if UNITY_IOS && !UNITY_EDITOR
            SpectatorCapture_SetCameraZoomFactor(zoomFactor);
#endif
        }

        public static bool TryDequeueEvent(out SpectatorCaptureBridgeEvent nextEvent)
        {
            nextEvent = null;
            if (!IsSupported)
                return false;

#if UNITY_IOS && !UNITY_EDITOR
            IntPtr pointer = SpectatorCapture_CopyNextEventJson();
            if (pointer == IntPtr.Zero)
                return false;

            try
            {
                string json = Marshal.PtrToStringAnsi(pointer);
                if (string.IsNullOrWhiteSpace(json))
                    return false;

                nextEvent = JsonUtility.FromJson<SpectatorCaptureBridgeEvent>(json);
                return nextEvent != null;
            }
            finally
            {
                SpectatorCapture_FreeString(pointer);
            }
#else
            return false;
#endif
        }
    }

    public sealed class SpectatorCaptureService : MonoBehaviour
    {
        private const bool EnableMicrophoneForRecording = false;
        private const float MinCameraZoomFactor = 1f;
        private const float MaxCameraZoomFactor = 4f;
        private const float CameraZoomButtonStep = 0.25f;
        private const float PinchZoomDeadZone = 0.01f;

        private struct ToastPresenterState
        {
            public SceneToastPresenter presenter;
            public bool wasEnabled;
        }

        private SpectatorClientService m_Client;
        private Coroutine m_PhotoCaptureRoutine;
        private Coroutine m_VideoStartRoutine;
        private bool m_IsRecordingVideo;
        private string m_LastStatusMessage = string.Empty;
        private readonly List<ToastPresenterState> m_ToastPresenterStates = new List<ToastPresenterState>();
        private SpectatorConnectPanel m_ConnectPanel;
        private int m_CaptureUiSuppressionDepth;
        private bool m_WasConnectPanelActive;
        private float m_CameraZoomFactor = MinCameraZoomFactor;
        private float m_DesiredCameraZoomFactor = MinCameraZoomFactor;
        private float m_LastPinchDistance;

        public bool IsSupported => SpectatorCaptureBridge.IsSupported;
        public bool IsRecordingVideo => m_IsRecordingVideo;
        public string LastStatusMessage => m_LastStatusMessage;
        public float CameraZoomFactor => m_CameraZoomFactor;

        public void Initialize(SpectatorClientService client)
        {
            m_Client = client;
            SpectatorCaptureBridge.InstallHardwareCaptureControls();
        }

        private void OnEnable()
        {
            SpectatorCaptureBridge.InstallHardwareCaptureControls();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
                return;

            SpectatorCaptureBridge.InstallHardwareCaptureControls();
        }

        private void Update()
        {
            ProcessBridgeEvents();
            HandlePinchZoom();
        }

        public void CapturePhoto()
        {
            if (!IsSupported)
            {
                PublishStatus("拍照仅支持 iOS 真机 spectator。");
                return;
            }

            if (m_PhotoCaptureRoutine != null)
            {
                PublishStatus("上一张照片还在处理中，请稍候。");
                return;
            }

            m_PhotoCaptureRoutine = StartCoroutine(CapturePhotoCoroutine());
        }

        public void StartVideoRecording()
        {
            if (!IsSupported)
            {
                PublishStatus("录像仅支持 iOS 真机 spectator。");
                return;
            }

            if (m_IsRecordingVideo)
            {
                PublishStatus("录像已经在进行中。");
                return;
            }

            if (m_VideoStartRoutine != null)
            {
                PublishStatus("录像正在启动，请稍候。");
                return;
            }

            m_VideoStartRoutine = StartCoroutine(StartVideoRecordingCoroutine());
        }

        public void StopVideoRecording()
        {
            if (!IsSupported)
            {
                PublishStatus("录像仅支持 iOS 真机 spectator。");
                return;
            }

            if (!m_IsRecordingVideo)
            {
                if (m_VideoStartRoutine != null)
                {
                    PublishStatus("录像正在启动，请稍候。");
                    return;
                }

                PublishStatus("当前没有正在进行的录像。");
                return;
            }

            PublishStatus("正在结束录像...");
            SpectatorCaptureBridge.StopVideoRecording();
        }

        public void ZoomCameraIn()
        {
            RequestCameraZoomFactor(m_DesiredCameraZoomFactor + CameraZoomButtonStep);
        }

        public void ZoomCameraOut()
        {
            RequestCameraZoomFactor(m_DesiredCameraZoomFactor - CameraZoomButtonStep);
        }

        public void ResetCameraZoom()
        {
            RequestCameraZoomFactor(MinCameraZoomFactor);
        }

        private IEnumerator CapturePhotoCoroutine()
        {
            PublishStatus("正在拍照...");
            PushCaptureUiSuppression();
            bool shouldPopSuppressionInFinally = true;
            try
            {
                yield return null;
                yield return new WaitForEndOfFrame();

#if UNITY_IOS && !UNITY_EDITOR
                shouldPopSuppressionInFinally = false;
                SpectatorCaptureBridge.CaptureCurrentViewToPhotos();
#else
                Texture2D screenshot = ScreenCapture.CaptureScreenshotAsTexture();
                if (screenshot == null)
                {
                    PublishStatus("拍照失败：没有拿到屏幕图像。", logToConsole: true);
                    yield break;
                }

                try
                {
                    byte[] pngBytes;
                    try
                    {
                        pngBytes = ImageConversion.EncodeToPNG(screenshot);
                    }
                    catch (Exception ex)
                    {
                        PublishStatus($"拍照失败：PNG 编码异常：{ex.Message}", logToConsole: true);
                        yield break;
                    }

                    if (pngBytes == null || pngBytes.Length == 0)
                    {
                        PublishStatus("拍照失败：PNG 编码为空。", logToConsole: true);
                        yield break;
                    }

                    string directory = Path.Combine(Application.temporaryCachePath, "SpectatorCapture");
                    Directory.CreateDirectory(directory);
                    string filePath = Path.Combine(
                        directory,
                        $"spectator_photo_{DateTime.Now:yyyyMMdd_HHmmss}.png");

                    File.WriteAllBytes(filePath, pngBytes);
                    SpectatorCaptureBridge.SaveImageToPhotos(filePath);
                }
                catch (Exception ex)
                {
                    PublishStatus($"拍照失败：{ex.Message}", logToConsole: true);
                    yield break;
                }
                finally
                {
                    Destroy(screenshot);
                }
#endif
            }
            finally
            {
                if (shouldPopSuppressionInFinally)
                    PopCaptureUiSuppression();
                m_PhotoCaptureRoutine = null;
            }

            PublishStatus("正在保存照片到系统相册...");
        }

        private IEnumerator StartVideoRecordingCoroutine()
        {
            PublishStatus("正在开始录像...");
            PushCaptureUiSuppression();

            try
            {
                yield return null;
                yield return new WaitForEndOfFrame();
                SpectatorCaptureBridge.StartVideoRecording(EnableMicrophoneForRecording);
            }
            finally
            {
                m_VideoStartRoutine = null;
            }
        }

        private void ProcessBridgeEvents()
        {
            while (SpectatorCaptureBridge.TryDequeueEvent(out SpectatorCaptureBridgeEvent nextEvent))
            {
                if (nextEvent == null)
                    continue;

                switch (nextEvent.type)
                {
                    case "photoSaved":
                        PopCaptureUiSuppression();
                        PublishStatus("照片已保存到系统相册。", logToConsole: true);
                        break;
                    case "photoPermissionDenied":
                        PopCaptureUiSuppression();
                        PublishStatus(string.IsNullOrWhiteSpace(nextEvent.message)
                            ? "无法保存照片，请在系统设置中允许访问照片。"
                            : nextEvent.message, logToConsole: true);
                        break;
                    case "photoSaveFailed":
                        PopCaptureUiSuppression();
                        PublishStatus(string.IsNullOrWhiteSpace(nextEvent.message)
                            ? "保存照片失败。"
                            : nextEvent.message, logToConsole: true);
                        break;
                    case "recordingStarted":
                        m_IsRecordingVideo = true;
                        PublishStatus("已开始录像，拍摄控件已隐藏。按音量减键或使用 iOS 系统录屏指示器可结束录像。", logToConsole: true);
                        break;
                    case "recordingStartFailed":
                        m_IsRecordingVideo = false;
                        PopCaptureUiSuppression();
                        PublishStatus(string.IsNullOrWhiteSpace(nextEvent.message)
                            ? "开始录像失败。"
                            : nextEvent.message, logToConsole: true);
                        break;
                    case "recordingStopped":
                        m_IsRecordingVideo = false;
                        PopCaptureUiSuppression();
                        PublishStatus(string.IsNullOrWhiteSpace(nextEvent.message)
                            ? "录像已结束。"
                            : nextEvent.message, logToConsole: true);
                        break;
                    case "recordingStopFailed":
                        m_IsRecordingVideo = false;
                        PopCaptureUiSuppression();
                        PublishStatus(string.IsNullOrWhiteSpace(nextEvent.message)
                            ? "结束录像失败。"
                            : nextEvent.message, logToConsole: true);
                        break;
                    case "recordingPreviewDismissed":
                        PopCaptureUiSuppression();
                        PublishStatus("录像预览已关闭。");
                        break;
                    case "cameraZoomChanged":
                        if (nextEvent.value > 0f)
                            m_CameraZoomFactor = ClampCameraZoomFactor(nextEvent.value);

                        m_DesiredCameraZoomFactor = m_CameraZoomFactor;
                        break;
                    case "cameraZoomUnsupported":
                    case "cameraZoomUnavailable":
                    case "cameraZoomFailed":
                        m_DesiredCameraZoomFactor = m_CameraZoomFactor;
                        PublishStatus(string.IsNullOrWhiteSpace(nextEvent.message)
                            ? "调整画面缩放失败。"
                            : nextEvent.message, logToConsole: true);
                        break;
                    case "captureButtonsInstalled":
                    case "captureButtonsUnsupported":
                    case "captureButtonsUnavailable":
                        if (!string.IsNullOrWhiteSpace(nextEvent.message))
                            PublishStatus(nextEvent.message, logToConsole: true);
                        break;
                    case "capturePrimaryTriggered":
                        CapturePhoto();
                        break;
                    case "captureSecondaryTriggered":
                        if (m_IsRecordingVideo)
                            StopVideoRecording();
                        else
                            StartVideoRecording();
                        break;
                    default:
                        if (!string.IsNullOrWhiteSpace(nextEvent.message))
                            PublishStatus(nextEvent.message, logToConsole: true);
                        break;
                }
            }
        }

        private void HandlePinchZoom()
        {
            if (!IsSupported)
            {
                m_LastPinchDistance = 0f;
                return;
            }

            if (Input.touchCount != 2)
            {
                m_LastPinchDistance = 0f;
                return;
            }

            Touch firstTouch = Input.GetTouch(0);
            Touch secondTouch = Input.GetTouch(1);
            if (firstTouch.phase == TouchPhase.Canceled ||
                secondTouch.phase == TouchPhase.Canceled ||
                firstTouch.phase == TouchPhase.Ended ||
                secondTouch.phase == TouchPhase.Ended ||
                IsTouchOverUi(firstTouch) ||
                IsTouchOverUi(secondTouch))
            {
                m_LastPinchDistance = 0f;
                return;
            }

            float currentDistance = Vector2.Distance(firstTouch.position, secondTouch.position);
            if (currentDistance <= Mathf.Epsilon)
            {
                m_LastPinchDistance = 0f;
                return;
            }

            if (firstTouch.phase == TouchPhase.Began ||
                secondTouch.phase == TouchPhase.Began ||
                m_LastPinchDistance <= Mathf.Epsilon)
            {
                m_LastPinchDistance = currentDistance;
                return;
            }

            float scale = currentDistance / m_LastPinchDistance;
            if (Mathf.Abs(scale - 1f) < PinchZoomDeadZone)
                return;

            RequestCameraZoomFactor(m_DesiredCameraZoomFactor * scale);
            m_LastPinchDistance = currentDistance;
        }

        private void RequestCameraZoomFactor(float zoomFactor)
        {
            if (!IsSupported)
            {
                PublishStatus("画面缩放仅支持 iOS 真机 spectator。");
                return;
            }

            float clampedZoomFactor = ClampCameraZoomFactor(zoomFactor);
            if (Mathf.Abs(clampedZoomFactor - m_DesiredCameraZoomFactor) < 0.001f &&
                Mathf.Abs(clampedZoomFactor - m_CameraZoomFactor) < 0.001f)
            {
                return;
            }

            m_DesiredCameraZoomFactor = clampedZoomFactor;
            SpectatorCaptureBridge.SetCameraZoomFactor(clampedZoomFactor);
        }

        private static float ClampCameraZoomFactor(float zoomFactor)
        {
            return Mathf.Clamp(zoomFactor, MinCameraZoomFactor, MaxCameraZoomFactor);
        }

        private static bool IsTouchOverUi(Touch touch)
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(touch.fingerId);
        }

        private void PublishStatus(string message, bool logToConsole = false)
        {
            m_LastStatusMessage = string.IsNullOrWhiteSpace(message) ? string.Empty : message.Trim();

            if (m_Client != null)
            {
                m_Client.ReportCaptureStatus(m_LastStatusMessage, logToConsole);
                return;
            }

            if (logToConsole && !string.IsNullOrEmpty(m_LastStatusMessage))
                Debug.Log($"[SpectatorCapture] {m_LastStatusMessage}", this);
        }

        private void PushCaptureUiSuppression()
        {
            m_CaptureUiSuppressionDepth++;
            if (m_CaptureUiSuppressionDepth > 1)
                return;

            if (m_ConnectPanel == null)
                m_ConnectPanel = FindFirstObjectByType<SpectatorConnectPanel>(FindObjectsInactive.Include);

            m_WasConnectPanelActive = m_ConnectPanel != null && m_ConnectPanel.gameObject.activeSelf;
            if (m_ConnectPanel != null && m_WasConnectPanelActive)
                m_ConnectPanel.gameObject.SetActive(false);

            m_ToastPresenterStates.Clear();
            SceneToastPresenter[] presenters = FindObjectsByType<SceneToastPresenter>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < presenters.Length; i++)
            {
                SceneToastPresenter presenter = presenters[i];
                if (presenter == null)
                    continue;

                m_ToastPresenterStates.Add(new ToastPresenterState
                {
                    presenter = presenter,
                    wasEnabled = presenter.enabled
                });

                if (presenter.enabled)
                    presenter.enabled = false;
            }
        }

        private void PopCaptureUiSuppression()
        {
            if (m_CaptureUiSuppressionDepth <= 0)
                return;

            m_CaptureUiSuppressionDepth--;
            if (m_CaptureUiSuppressionDepth > 0)
                return;

            if (m_ConnectPanel != null)
                m_ConnectPanel.gameObject.SetActive(m_WasConnectPanelActive);

            for (int i = 0; i < m_ToastPresenterStates.Count; i++)
            {
                ToastPresenterState state = m_ToastPresenterStates[i];
                if (state.presenter == null)
                    continue;

                state.presenter.enabled = state.wasEnabled;
            }

            m_ToastPresenterStates.Clear();
        }
    }
}
