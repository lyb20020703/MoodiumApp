using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace VFXViewer
{
    [DisallowMultipleComponent]
    public sealed class SceneToastPresenter : MonoBehaviour
    {
        [Header("消息源")]
        [SerializeField] private ExhibitPlacementManager m_StatusSource;

        [Header("视野位置")]
        [SerializeField] private Camera m_TargetCamera;
        [SerializeField] private float m_DistanceFromCamera = 0.85f;
        [SerializeField] private float m_VerticalOffset = -0.12f;

        [Header("显示")]
        [SerializeField] private float m_DefaultDuration = 2.0f;
        [SerializeField] private float m_FadeDuration = 0.12f;
        [SerializeField] private bool m_SuppressNonCriticalToastsInExperienceMode = true;
        [SerializeField] private bool m_EnableToastDebugLogs = true;

        [Header("外观")]
        [SerializeField] private Vector2 m_CanvasSize = new Vector2(960f, 180f);
        [SerializeField] private Vector2 m_BackgroundSize = new Vector2(880f, 110f);
        [SerializeField] private Vector2 m_MinBackgroundSize = new Vector2(320f, 96f);
        [SerializeField] private Vector2 m_TextPadding = new Vector2(40f, 22f);
        [SerializeField] private float m_WorldScale = 0.00045f;
        [SerializeField] private int m_FontSize = 48;

        private static SceneToastPresenter s_Instance;

        private Transform m_ToastRoot;
        private RectTransform m_BackgroundRect;
        private RectTransform m_MessageRect;
        private CanvasGroup m_CanvasGroup;
        private Text m_MessageText;
        private Coroutine m_ToastCoroutine;
        private bool m_IsSubscribed;
        private int m_CurrentToastPriority = int.MinValue;

        public static void Show(string message, float duration = -1f)
        {
            if (s_Instance == null || string.IsNullOrWhiteSpace(message))
                return;

            s_Instance.ShowToast(message, duration);
        }

        private void Awake()
        {
            if (s_Instance != null && s_Instance != this)
            {
                Debug.LogWarning("SceneToastPresenter 重复存在，已忽略后创建的实例。", this);
                enabled = false;
                return;
            }

            s_Instance = this;
            ResolveTargetCamera();
            EnsureToastVisual();
            HideImmediate();
        }

        private void Start()
        {
            TryResolveStatusSource();
            SubscribeToStatusSource();
        }

        private void OnEnable()
        {
            if (s_Instance == null)
                s_Instance = this;

            ResolveTargetCamera();
            EnsureToastVisual();
            TryResolveStatusSource();
            SubscribeToStatusSource();
        }

        private void OnDisable()
        {
            UnsubscribeFromStatusSource();

            if (m_ToastCoroutine != null)
            {
                StopCoroutine(m_ToastCoroutine);
                m_ToastCoroutine = null;
            }

            if (m_CanvasGroup != null)
                m_CanvasGroup.alpha = 0f;

            if (m_ToastRoot != null)
                m_ToastRoot.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (s_Instance == this)
                s_Instance = null;
        }

        public void ShowToastMessage(string message)
        {
            if (m_EnableToastDebugLogs)
                Debug.Log($"[SceneToast] incoming: {message}", this);

            if (ShouldSuppressToast(message))
            {
                if (m_EnableToastDebugLogs)
                    Debug.Log($"[SceneToast] suppressed: {message}", this);
                return;
            }

            ShowToast(message, m_DefaultDuration);
        }

        public void ShowToast(string message, float duration = -1f)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            int incomingPriority = GetToastPriority(message);
            if (m_ToastCoroutine != null && incomingPriority < m_CurrentToastPriority)
            {
                if (m_EnableToastDebugLogs)
                    Debug.Log($"[SceneToast] skipped lower priority: {message}", this);
                return;
            }

            if (m_EnableToastDebugLogs)
                Debug.Log($"[SceneToast] show: {message}", this);

            ResolveTargetCamera();
            EnsureToastVisual();
            PositionToastRoot();

            if (m_ToastCoroutine != null)
                StopCoroutine(m_ToastCoroutine);

            m_CurrentToastPriority = incomingPriority;
            m_ToastCoroutine = StartCoroutine(ShowToastRoutine(message, duration > 0f ? duration : m_DefaultDuration));
        }

        private void TryResolveStatusSource()
        {
            if (m_StatusSource == null)
                m_StatusSource = FindFirstObjectByType<ExhibitPlacementManager>();
        }

        private bool ShouldSuppressToast(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return true;

            if (!m_SuppressNonCriticalToastsInExperienceMode)
                return false;

            if (m_StatusSource == null || m_StatusSource.CurrentAppMode != ExhibitAppMode.Experience)
                return false;

            return !IsImportantExperienceToast(message);
        }

        private static bool IsImportantExperienceToast(string message)
        {
            return message.Contains("失败") ||
                   message.Contains("错误") ||
                   message.Contains("异常") ||
                   message.Contains("不可用") ||
                   message.Contains("无法");
        }

        private static int GetToastPriority(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return int.MinValue;

            if (message.Contains("失败") ||
                message.Contains("错误") ||
                message.Contains("异常") ||
                message.Contains("不可用") ||
                message.Contains("无法"))
            {
                return 300;
            }

            if (message.Contains("已固定"))
                return 220;

            if (message.Contains("正在重新创建空间锚点") ||
                message.Contains("正在创建空间锚点"))
            {
                return 180;
            }

            if (message.Contains("已脱离锚点"))
                return 160;

            if (message.Contains("已加载会话"))
                return 60;

            if (message.StartsWith("[布展模式]") || message.StartsWith("[体验模式]"))
                return 40;

            return 100;
        }

        private void SubscribeToStatusSource()
        {
            if (m_IsSubscribed || m_StatusSource == null)
                return;

            m_StatusSource.onStatusMessage.AddListener(ShowToastMessage);
            m_IsSubscribed = true;

            if (m_EnableToastDebugLogs)
                Debug.Log($"[SceneToast] subscribed to status source: {m_StatusSource.name}", this);
        }

        private void UnsubscribeFromStatusSource()
        {
            if (!m_IsSubscribed || m_StatusSource == null)
                return;

            m_StatusSource.onStatusMessage.RemoveListener(ShowToastMessage);
            m_IsSubscribed = false;
        }

        private void ResolveTargetCamera()
        {
            if (m_TargetCamera == null)
                m_TargetCamera = GetComponent<Camera>();

            if (m_TargetCamera == null)
                m_TargetCamera = Camera.main;

            if (m_EnableToastDebugLogs && m_TargetCamera == null)
                Debug.LogWarning("[SceneToast] target camera unresolved.", this);
        }

        private void EnsureToastVisual()
        {
            if (m_ToastRoot != null && m_CanvasGroup != null && m_MessageText != null)
                return;

            var root = new GameObject("SceneToastCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            root.layer = LayerMask.NameToLayer("UI");
            m_ToastRoot = root.transform;

            var rect = (RectTransform)m_ToastRoot;
            rect.SetParent(transform, false);
            rect.sizeDelta = m_CanvasSize;

            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 500;
            canvas.pixelPerfect = false;

            m_CanvasGroup = root.GetComponent<CanvasGroup>();
            m_CanvasGroup.alpha = 0f;
            m_CanvasGroup.interactable = false;
            m_CanvasGroup.blocksRaycasts = false;

            var background = CreateBackground(rect);
            CreateLabel(background);
            PositionToastRoot();
        }

        private RectTransform CreateBackground(RectTransform parent)
        {
            var background = new GameObject("Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            background.layer = parent.gameObject.layer;

            var backgroundRect = background.GetComponent<RectTransform>();
            backgroundRect.SetParent(parent, false);
            backgroundRect.sizeDelta = m_BackgroundSize;
            backgroundRect.anchorMin = new Vector2(0.5f, 0.5f);
            backgroundRect.anchorMax = new Vector2(0.5f, 0.5f);
            backgroundRect.pivot = new Vector2(0.5f, 0.5f);

            var image = background.GetComponent<Image>();
            image.color = new Color(0.05f, 0.08f, 0.12f, 0.88f);
            image.raycastTarget = false;

            m_BackgroundRect = backgroundRect;
            return backgroundRect;
        }

        private void CreateLabel(RectTransform parent)
        {
            var label = new GameObject("Message", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            label.layer = parent.gameObject.layer;

            var labelRect = label.GetComponent<RectTransform>();
            labelRect.SetParent(parent, false);
            labelRect.anchorMin = new Vector2(0.5f, 0.5f);
            labelRect.anchorMax = new Vector2(0.5f, 0.5f);
            labelRect.pivot = new Vector2(0.5f, 0.5f);

            var text = label.GetComponent<Text>();
            text.font = LoadBuiltinFont();
            text.fontSize = m_FontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 24;
            text.resizeTextMaxSize = m_FontSize;
            text.supportRichText = true;
            text.color = Color.white;
            text.raycastTarget = false;
            text.text = string.Empty;

            m_MessageRect = labelRect;
            m_MessageText = text;
        }

        private static Font LoadBuiltinFont()
        {
            var font = TryLoadBuiltinFont("LegacyRuntime.ttf");
            if (font != null)
                return font;

            return TryLoadBuiltinFont("Arial.ttf");
        }

        private static Font TryLoadBuiltinFont(string resourcePath)
        {
            try
            {
                return Resources.GetBuiltinResource<Font>(resourcePath);
            }
            catch (System.ArgumentException)
            {
                return null;
            }
        }

        private void PositionToastRoot()
        {
            if (m_ToastRoot == null)
                return;

            ResolveTargetCamera();
            if (m_TargetCamera == null)
                return;

            if (m_ToastRoot.parent != m_TargetCamera.transform)
                m_ToastRoot.SetParent(m_TargetCamera.transform, false);

            m_ToastRoot.localPosition = new Vector3(0f, m_VerticalOffset, m_DistanceFromCamera);
            m_ToastRoot.localRotation = Quaternion.identity;
            m_ToastRoot.localScale = Vector3.one * m_WorldScale;
        }

        private IEnumerator ShowToastRoutine(string message, float duration)
        {
            m_ToastRoot.gameObject.SetActive(true);
            RefreshLayoutForMessage(message);

            yield return FadeTo(1f, m_FadeDuration);
            yield return new WaitForSeconds(duration);
            yield return FadeTo(0f, m_FadeDuration);

            m_MessageText.text = string.Empty;
            m_ToastRoot.gameObject.SetActive(false);
            m_ToastCoroutine = null;
            m_CurrentToastPriority = int.MinValue;
        }

        private IEnumerator FadeTo(float targetAlpha, float duration)
        {
            if (m_CanvasGroup == null)
                yield break;

            float startAlpha = m_CanvasGroup.alpha;
            if (duration <= 0f)
            {
                m_CanvasGroup.alpha = targetAlpha;
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                m_CanvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, t);
                yield return null;
            }

            m_CanvasGroup.alpha = targetAlpha;
        }

        private void HideImmediate()
        {
            if (m_CanvasGroup != null)
                m_CanvasGroup.alpha = 0f;

            if (m_MessageText != null)
                m_MessageText.text = string.Empty;

            if (m_ToastRoot != null)
                m_ToastRoot.gameObject.SetActive(false);

            m_CurrentToastPriority = int.MinValue;
        }

        private void RefreshLayoutForMessage(string message)
        {
            if (m_MessageText == null || m_BackgroundRect == null || m_MessageRect == null)
                return;

            m_MessageText.text = message;

            float horizontalPadding = Mathf.Max(0f, m_TextPadding.x);
            float verticalPadding = Mathf.Max(0f, m_TextPadding.y);
            float maxBackgroundWidth = Mathf.Max(m_MinBackgroundSize.x, m_BackgroundSize.x);
            float maxTextWidth = Mathf.Max(64f, maxBackgroundWidth - (horizontalPadding * 2f));

            var widthSettings = m_MessageText.GetGenerationSettings(new Vector2(maxTextWidth, 0f));
            float preferredTextWidth = m_MessageText.cachedTextGeneratorForLayout.GetPreferredWidth(message, widthSettings) / Mathf.Max(1f, m_MessageText.pixelsPerUnit);
            float resolvedTextWidth = Mathf.Min(maxTextWidth, preferredTextWidth);

            float backgroundWidth = Mathf.Clamp(
                resolvedTextWidth + (horizontalPadding * 2f),
                m_MinBackgroundSize.x,
                maxBackgroundWidth);

            float resolvedLabelWidth = Mathf.Max(1f, backgroundWidth - (horizontalPadding * 2f));
            var heightSettings = m_MessageText.GetGenerationSettings(new Vector2(resolvedLabelWidth, 0f));
            float preferredTextHeight = m_MessageText.cachedTextGeneratorForLayout.GetPreferredHeight(message, heightSettings) / Mathf.Max(1f, m_MessageText.pixelsPerUnit);
            float backgroundHeight = Mathf.Max(m_MinBackgroundSize.y, preferredTextHeight + (verticalPadding * 2f));

            m_BackgroundRect.sizeDelta = new Vector2(backgroundWidth, backgroundHeight);
            m_MessageRect.sizeDelta = new Vector2(resolvedLabelWidth, Mathf.Max(1f, backgroundHeight - (verticalPadding * 2f)));
        }
    }
}
