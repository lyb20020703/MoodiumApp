using System;
using System.Collections.Generic;
using TMPro;
using Unity.PolySpatial;
using UnityEngine;
using UnityEngine.UI;

namespace Moodium.Calibration
{
    [DisallowMultipleComponent]
    public sealed class SensoryBagCalibrator : MonoBehaviour
    {
        [Header("Calibration")]
        [SerializeField, Min(0.01f)] float m_DefaultWidth = 0.40f;
        [SerializeField, Min(0.01f)] float m_DefaultHeight = 0.25f;
        [SerializeField, Min(0.005f)] float m_DefaultDepth = 0.08f;
        [SerializeField] Vector2 m_DefaultCenterOffset;
        [SerializeField, Min(0.01f)] float m_MinWidth = 0.08f;
        [SerializeField, Min(0.01f)] float m_MaxWidth = 0.6f;
        [SerializeField, Min(0.01f)] float m_MinHeight = 0.06f;
        [SerializeField, Min(0.01f)] float m_MaxHeight = 0.45f;

        [Header("Calibration UI")]
        [SerializeField, Min(0.001f)] float m_BorderThickness = 0.004f;
        [SerializeField, Min(0.005f)] float m_HandleSize = 0.018f;
        [SerializeField, Min(0.005f)] float m_HandleDepth = 0.02f;
        [SerializeField, Min(0.01f)] float m_ButtonWidth = 0.16f;
        [SerializeField, Min(0.01f)] float m_ButtonHeight = 0.06f;
        [SerializeField] Color m_BorderColor = new(0.92f, 0.98f, 1f, 1f);
        [SerializeField] Color m_HandleColor = new(1f, 1f, 1f, 1f);
        [SerializeField] Color m_ConfirmColor = new(14f / 255f, 20f / 255f, 41f / 255f, 230f / 255f);
        [SerializeField] Color m_ResetColor = new(14f / 255f, 20f / 255f, 41f / 255f, 230f / 255f);

        [Header("Calibration UI v2")]
        [SerializeField] Color m_CandyAccent = new(242f / 255f, 125f / 255f, 182f / 255f, 1f);
        [SerializeField] Color m_NatureAccent = new(85f / 255f, 196f / 255f, 127f / 255f, 1f);
        [Tooltip("Gap between the frame and the height control on the viewer's left, metres.")]
        [SerializeField, Min(0f)] float m_ElevatorGap = .06f;
        [Tooltip("Height of the height control's bottom above the table plane, metres.")]
        [SerializeField, Min(.01f)] float m_ElevatorLift = .04f;
        [Tooltip("Gap between the frame's far edge and the tip card / Confirm cluster, metres.")]
        [SerializeField, Min(0f)] float m_GuideGap = .06f;
        [Tooltip("Height of the tip card / Confirm cluster centre above the table plane, metres.")]
        [SerializeField, Min(0f)] float m_GuideLift = .14f;

        [Header("Debug (read only at runtime)")]
        [SerializeField] string m_ReferenceImageName;
        [SerializeField] string m_TrackingState = "Inactive";
        [SerializeField] float m_CurrentWidth;
        [SerializeField] float m_CurrentHeight;
        [SerializeField] Vector2 m_CurrentCenterOffset;
        [SerializeField] Vector3 m_CurrentAnchorPosition;
        [SerializeField] Vector3 m_CurrentAnchorEulerAngles;

        readonly Dictionary<int, CalibrationHandleKind> m_Drags = new();
        readonly Dictionary<int, Vector2> m_DragStartPointer = new();
        readonly Dictionary<int, Vector2> m_DragStartCenter = new();
        readonly Dictionary<int,float> m_DragStartNormal = new();
        readonly Dictionary<int,float> m_DragStartPlane = new();
        Transform m_HeightRail;
        TextMeshProUGUI m_HeightValueLabel;

        Transform m_Anchor;
        Quaternion m_InitialAnchorRotation;
        float m_RotationDegrees;
        Moodium.UI.CalibrationRotateDial m_RotateDial;
        Vector3 m_RotationStartVector;
        float m_RotationStartYaw;
        public float RotationDegrees => m_RotationDegrees;
        Transform m_FrameRoot;
        GameObject m_FrameBody;
        readonly Dictionary<CalibrationHandleKind, Transform> m_Visuals = new();
        SensoryBagCalibrationData m_DefaultData;
        SensoryBagCalibrationSettings m_Settings;
        SensoryBagCalibrationRectangle m_Rectangle;
        bool m_FrameVisible;
        bool m_Confirmed;
        TMP_FontAsset m_ChineseFont;
        TMP_FontAsset m_EnglishFont;
        Sprite m_RoundedButtonSprite;
        static Material s_RuntimeUiMaterial;

        public event Action<SensoryBagCalibrationData> CalibrationConfirmed;
        public Transform Anchor => m_Anchor;
        public Transform FrameRoot => m_FrameRoot;
        public SensoryBagCalibrationData CurrentData => m_Rectangle != null ? m_Rectangle.Data : m_DefaultData;
        public bool FrameVisible => m_FrameVisible;
        public bool IsConfirmed => m_Confirmed;
        public bool HasUserInteracted { get; private set; }
        public void NotifyUserInteraction() => HasUserInteracted = true;

        public void ConfigureFonts(TMP_FontAsset chineseFont, TMP_FontAsset englishFont)
        {
            m_ChineseFont = chineseFont;
            m_EnglishFont = englishFont;
        }

        public void ConfigureButtonStyle(Sprite roundedButtonSprite)
        {
            m_RoundedButtonSprite = roundedButtonSprite;
        }

        public SensoryBagCalibrationSettings CreateInspectorSettings() => new(
            m_DefaultWidth, m_DefaultHeight, m_DefaultDepth, m_DefaultCenterOffset,
            m_MinWidth, m_MaxWidth, m_MinHeight, m_MaxHeight);

        public void Begin(
            Transform anchor,
            SensoryBagCalibrationData initialData,
            SensoryBagCalibrationSettings settings)
        {
            m_Anchor = anchor;
            m_InitialAnchorRotation = anchor != null ? anchor.rotation : Quaternion.identity;
            m_RotationDegrees = 0f;
            HasUserInteracted = false;
            ClearDrags();
            m_Settings = settings;
            m_TrackingState = "Tracking";
            m_DefaultData = SensoryBagCalibrationData.CreateDefault(
                initialData.ReferenceImageName, initialData.WorldKind, settings);
            m_DefaultData.PlaneLocalOffset = initialData.PlaneLocalOffset;
            m_DefaultData.LocalOrientation = initialData.LocalOrientation;
            initialData.ClampDimensions(settings);
            m_Rectangle = new SensoryBagCalibrationRectangle(initialData, settings);
            m_Confirmed = false;
            EnsureVisuals();
            SetFrameVisible(true);
            RefreshVisuals();
            Debug.Log($"[Sensory Bag Calibration] Started {initialData.ReferenceImageName}: " +
                      $"{initialData.Width:F3}m × {initialData.Height:F3}m, offset={initialData.CenterOffset}.");
        }

        public void Begin(Transform anchor, SensoryBagCalibrationData initialData)
        {
            Begin(anchor, initialData, CreateInspectorSettings());
        }

        public void SetAnchor(Transform anchor)
        {
            m_Anchor = anchor;
            SyncFrameNow();
        }

        public void SetTrackingVisible(bool tracked)
        {
            if(!tracked)ClearDrags();
            m_TrackingState = tracked ? "Tracking" : "Temporarily lost";
            if (!m_Confirmed)
                SetFrameVisible(tracked);
        }

        public void MoveFrameToAnchorPlane(Vector2 centerOffset)
        {
            NotifyUserInteraction();
            if (m_Rectangle == null)
                return;
            m_Rectangle.MoveTo(centerOffset);
            RefreshVisuals();
        }

        public void ResizeFromHandle(CalibrationHandleKind kind, Vector2 anchorLocalPoint)
        {
            NotifyUserInteraction();
            if (m_Rectangle == null)
                return;
            m_Rectangle.ResizeFromHandle(kind, anchorLocalPoint);
            RefreshVisuals();
        }

        public void SetPlaneHeight(float offsetMeters)
        {
            NotifyUserInteraction();
            if(m_Rectangle==null||!m_FrameVisible||m_Confirmed)return;
            m_Rectangle.SetPlaneHeight(offsetMeters);RefreshVisuals();
        }

        public void AdjustPlaneHeight(float deltaMeters)
        {
            if(m_Rectangle!=null)SetPlaneHeight(m_Rectangle.Data.PlaneLocalOffset.y+deltaMeters);
        }

        public void BeginPointerDrag(int pointerId, CalibrationHandleKind kind, Vector3 worldPosition)
        {
            if (!m_FrameVisible || m_Confirmed || m_Rectangle == null || m_Anchor == null || m_Drags.Count > 0)
                return;
            var localPoint = SensoryBagCalibrationRectangle.ProjectWorldPointToAnchorPlane(m_Anchor, worldPosition);
            NotifyUserInteraction();
            m_Drags[pointerId] = kind;
            m_DragStartPointer[pointerId] = localPoint;
            m_DragStartCenter[pointerId] = m_Rectangle.Data.CenterOffset;
            m_DragStartNormal[pointerId]=m_Anchor.InverseTransformPoint(worldPosition).y;
            m_DragStartPlane[pointerId]=m_Rectangle.Data.PlaneLocalOffset.y;
            if(kind==CalibrationHandleKind.Rotate)
            {
                m_RotationStartVector=Vector3.ProjectOnPlane(worldPosition-m_FrameRoot.position,Vector3.up);
                m_RotationStartYaw=m_RotationDegrees;
            }
        }

        public void MovePointerDrag(int pointerId, Vector3 worldPosition)
        {
            if (!m_Drags.TryGetValue(pointerId, out var kind) || m_Anchor == null)
                return;
            var localPoint = SensoryBagCalibrationRectangle.ProjectWorldPointToAnchorPlane(m_Anchor, worldPosition);
            if(kind==CalibrationHandleKind.Rotate)
            {
                var current=Vector3.ProjectOnPlane(worldPosition-m_FrameRoot.position,Vector3.up);
                if(current.sqrMagnitude>.000001f&&m_RotationStartVector.sqrMagnitude>.000001f)
                {
                    var target=Moodium.UI.MoodiumUIRefreshLayout.SnapYaw(m_RotationStartYaw+Vector3.SignedAngle(m_RotationStartVector,current,Vector3.up));
                    ApplyFrameRotation(Mathf.DeltaAngle(m_RotationDegrees,target));
                }
                return;
            }
            if(kind==CalibrationHandleKind.Height)
            {
                SetPlaneHeight(m_DragStartPlane[pointerId]+m_Anchor.InverseTransformPoint(worldPosition).y-m_DragStartNormal[pointerId]);
                return;
            }
            if (kind == CalibrationHandleKind.Frame)
            {
                var delta = localPoint - m_DragStartPointer[pointerId];
                MoveFrameToAnchorPlane(m_DragStartCenter[pointerId] + delta);
            }
            else
                ResizeFromHandle(kind, localPoint);
        }

        public void EndPointerDrag(int pointerId)
        {
            m_Drags.Remove(pointerId);
            m_DragStartPointer.Remove(pointerId);
            m_DragStartCenter.Remove(pointerId);
            m_DragStartNormal.Remove(pointerId);m_DragStartPlane.Remove(pointerId);
        }

        void ClearDrags()
        {
            m_Drags.Clear();m_DragStartPointer.Clear();m_DragStartCenter.Clear();m_DragStartNormal.Clear();m_DragStartPlane.Clear();
        }

        public void ResetCalibration()
        {
            RotateFrame(-m_RotationDegrees);
            NotifyUserInteraction();
            ClearDrags();
            if (m_Rectangle == null)
                return;
            m_Rectangle = new SensoryBagCalibrationRectangle(m_DefaultData, m_Settings);
            m_Confirmed = false;
            SetFrameVisible(true);
            RefreshVisuals();
            Debug.Log($"[Sensory Bag Calibration] Reset {m_DefaultData.ReferenceImageName}.");
        }

        public void ConfirmCalibration()
        {
            if (m_Rectangle == null || m_Confirmed)
                return;
            NotifyUserInteraction();
            m_Confirmed = true;
            ClearDrags();
            SetFrameVisible(false);
            var data = m_Rectangle.Data;
            Debug.Log($"[Sensory Bag Calibration] Confirmed {data.ReferenceImageName}: " +
                      $"{data.Width:F3}m × {data.Height:F3}m, offset={data.CenterOffset}, depth={data.Depth:F3}m.");
            CalibrationConfirmed?.Invoke(data);
        }

        public void SyncFrameNow()
        {
            if (m_Anchor == null || m_FrameRoot == null || m_Rectangle == null)
                return;
            var data = m_Rectangle.Data;
            var planeOffset = data.PlaneLocalOffset;
            m_FrameRoot.SetPositionAndRotation(
                m_Anchor.TransformPoint(new Vector3(data.CenterOffset.x, planeOffset.y, data.CenterOffset.y)),
                m_Anchor.rotation * data.LocalOrientation);
            UpdateDebugData();

        }

        public void RotateFrame(float degrees)
        {
            if (m_Anchor == null || m_Rectangle == null || m_Confirmed || !m_FrameVisible) return;
            NotifyUserInteraction();
            ClearDrags();
            ApplyFrameRotation(degrees);
        }
        void ApplyFrameRotation(float degrees)
        {
            var data = m_Rectangle.Data;
            var center = m_Anchor.TransformPoint(new Vector3(data.CenterOffset.x, 0f, data.CenterOffset.y));
            m_RotationDegrees = Mathf.Repeat(m_RotationDegrees + degrees + 180f, 360f) - 180f;
            m_Anchor.rotation = Quaternion.AngleAxis(m_RotationDegrees, Vector3.up) * m_InitialAnchorRotation;
            // Rotate around the frame centre rather than swinging it around the anchor origin.
            m_Anchor.position += center - m_Anchor.TransformPoint(new Vector3(data.CenterOffset.x, 0f, data.CenterOffset.y));
            RefreshVisuals();
        }

        void LateUpdate()
        {
            SyncFrameNow();
            UpdateSpatialLayout();
            UpdateStateVisuals();
            if(m_RotateDial!=null&&m_Rectangle!=null)m_RotateDial.Refresh(CurrentData.Width,CurrentData.Height,m_RotationDegrees,
                m_Drags.ContainsValue(CalibrationHandleKind.Rotate),CurrentData.WorldKind==SensoryBagWorldKind.Nature,m_Viewer);
        }

        // ------------------------------------------------------------------ calibration UI v2 (visuals only)
        // Everything below only builds and places visuals. Pointer handling, data and events are unchanged:
        // every interactive object still carries a SensoryBagCalibrationHandle with the same kind/action.
        //
        // Layout (seen from the viewer):
        //   frame        thin glowing line on the TABLE, L-shaped corner brackets, grips on the edges
        //   left side    vertical "elevator": + on top, height in the middle, - at the bottom; drag it up/down
        //   behind       tip card "keep the frame flat on the table" with Reset / Confirm under it

        const string UiFolder = "MoodiumCalibrationUI/";
        const float Unit = .0005f; // UGUI canvas unit = 0.5 mm (same as the design board's pixels)
        const float ElevatorWidth = 76f, ElevatorHeight = 184f, StepOffset = 62f;
        const float CardHeight = 80f, ButtonHeight = 46f, ClusterGap = 12f, ButtonGap = 10f;
        const float ButtonLift = .003f;
        static readonly CalibrationHandleKind[] Corners =
            { CalibrationHandleKind.TopLeft, CalibrationHandleKind.TopRight, CalibrationHandleKind.BottomRight, CalibrationHandleKind.BottomLeft };
        static readonly CalibrationHandleKind[] Edges =
            { CalibrationHandleKind.Left, CalibrationHandleKind.Right, CalibrationHandleKind.Top, CalibrationHandleKind.Bottom };

        readonly Dictionary<CalibrationHandleKind, SpriteRenderer> m_Grips = new();
        readonly Dictionary<string, Sprite> m_Sprites = new();
        VisionOSSortingGroup m_Sorting;
        Transform m_PlaneRing;
        Image m_ElevatorBackground;
        Transform m_Card;
        RectTransform m_CardCanvas;
        Image m_CardBackground;
        Image m_CardIcon;
        TextMeshProUGUI m_CardTitle;
        Image m_ConfirmBackground;
        float m_ResetWidth = 120f, m_ConfirmWidth = 130f;
        Transform m_Viewer;
        bool m_English;

        void EnsureVisuals()
        {
            if (m_FrameRoot != null)
                return;
            m_English = PlayerPrefs.GetString("Moodium.Language", "Chinese").Equals("English", StringComparison.OrdinalIgnoreCase);
            var root = new GameObject("Calibration Frame");
            root.transform.SetParent(transform, false);
            m_FrameRoot = root.transform;
            m_Sorting = root.AddComponent<VisionOSSortingGroup>();

            // Invisible drag body: raycastable, never covers the real bag.
            m_FrameBody = GameObject.CreatePrimitive(PrimitiveType.Cube);
            m_FrameBody.name = "Frame Drag Surface";
            m_FrameBody.transform.SetParent(m_FrameRoot, false);
            m_FrameBody.GetComponent<Renderer>().enabled = false;
            m_FrameBody.AddComponent<SensoryBagCalibrationHandle>().Configure(this, CalibrationHandleKind.Frame);
            m_Visuals[CalibrationHandleKind.Frame] = m_FrameBody.transform;

            // Glowing border lines (also the edge drag targets) and the grips centred on them.
            CreateFrameSprite("Left Edge Handle", "calib_line", CalibrationHandleKind.Left, 1);
            CreateFrameSprite("Right Edge Handle", "calib_line", CalibrationHandleKind.Right, 1);
            CreateFrameSprite("Top Edge Handle", "calib_line", CalibrationHandleKind.Top, 1);
            CreateFrameSprite("Bottom Edge Handle", "calib_line", CalibrationHandleKind.Bottom, 1);
            foreach (var edge in Edges)
                m_Grips[edge] = CreateSprite(edge + " Edge Grip", "calib_grip", m_FrameRoot, 2);

            // L-shaped corner brackets. The sprite pivot is the frame corner itself.
            CreateFrameSprite("Top Left Corner Handle", "calib_corner", CalibrationHandleKind.TopLeft, 3);
            CreateFrameSprite("Top Right Corner Handle", "calib_corner", CalibrationHandleKind.TopRight, 3);
            CreateFrameSprite("Bottom Right Corner Handle", "calib_corner", CalibrationHandleKind.BottomRight, 3);
            CreateFrameSprite("Bottom Left Corner Handle", "calib_corner", CalibrationHandleKind.BottomLeft, 3);

            // Table height: dashed line from the elevator down to a ring on the table plane.
            m_HeightRail = CreateSprite("Table Height Rail", "calib_drop_line", m_FrameRoot, 5).transform;
            m_PlaneRing = CreateSprite("Table Plane Ring", "calib_plane_ring", m_FrameRoot, 1).transform;
            BuildElevator();
            BuildGuideCard();

            CreateAction("确认 / Confirm", m_ConfirmColor, CalibrationAction.Confirm);
            CreateAction("重置 / Reset", m_ResetColor, CalibrationAction.Reset);
            CreateAction("Raise Table", m_ResetColor, CalibrationAction.RaiseTable);
            CreateAction("Lower Table", m_ResetColor, CalibrationAction.LowerTable);
            m_RotateDial=m_FrameRoot.gameObject.AddComponent<Moodium.UI.CalibrationRotateDial>();
            m_RotateDial.Initialize(this,m_English?m_EnglishFont:m_ChineseFont);
        }

        Sprite LoadSprite(string spriteName)
        {
            if (m_Sprites.TryGetValue(spriteName, out var sprite))
                return sprite;
            sprite = Resources.Load<Sprite>(UiFolder + spriteName);
            if (sprite == null)
                Debug.LogWarning($"[Sensory Bag Calibration] Missing sprite Resources/{UiFolder}{spriteName}.png");
            m_Sprites[spriteName] = sprite;
            return sprite;
        }

        SpriteRenderer CreateSprite(string objectName, string spriteName, Transform parent, int order)
        {
            var go = new GameObject(objectName, typeof(SpriteRenderer));
            go.transform.SetParent(parent, false);
            var renderer = go.GetComponent<SpriteRenderer>();
            renderer.sprite = LoadSprite(spriteName);
            renderer.color = m_BorderColor;
            renderer.sortingOrder = order;
            m_Sorting.Renderers.Add(new VisionOSSortingGroup.RendererSorting { Renderer = go, Order = order, ApplyToDescendants = false });
            return renderer;
        }

        void CreateFrameSprite(string objectName, string spriteName, CalibrationHandleKind kind, int order)
        {
            var renderer = CreateSprite(objectName, spriteName, m_FrameRoot, order);
            renderer.gameObject.AddComponent<BoxCollider>();
            renderer.gameObject.AddComponent<SensoryBagCalibrationHandle>().Configure(this, kind);
            m_Visuals[kind] = renderer.transform;
        }

        RectTransform CreateCanvas(Transform parent, string objectName, float width, float height, int order)
        {
            var go = new GameObject(objectName, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * Unit;
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;
            canvas.sortingOrder = order;
            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        /// <summary>Vertical height control. Its body is the Height drag handle; + and - are separate buttons on top of it.</summary>
        void BuildElevator()
        {
            var go = new GameObject("Table Height Drag Handle", typeof(BoxCollider));
            go.transform.SetParent(m_FrameRoot, false);
            go.GetComponent<BoxCollider>().size = new Vector3(ElevatorWidth * Unit, ElevatorHeight * Unit, .02f);
            go.AddComponent<SensoryBagCalibrationHandle>().Configure(this, CalibrationHandleKind.Height);
            m_Visuals[CalibrationHandleKind.Height] = go.transform;

            var canvas = CreateCanvas(go.transform, "Elevator Visual", ElevatorWidth, ElevatorHeight, 10);
            m_ElevatorBackground = AddImage(canvas, "Background", "calib_elevator_dark", 0, 0, ElevatorWidth, ElevatorHeight, 48f / 38f);
            AddImage(canvas, "Chevron Up", "calib_icon_chevron_up", 30, 60, 16, 16, 1f);
            m_HeightValueLabel = AddText(canvas, "Table Height Readout", "", 0, 78, ElevatorWidth, 24, 18, true, TextAlignmentOptions.Center, Color.white);
            AddText(canvas, "Unit", m_English ? "cm" : "厘米", 0, 101, ElevatorWidth, 13, 11, false, TextAlignmentOptions.Center, new Color(1f, 1f, 1f, .7f));
            AddImage(canvas, "Chevron Down", "calib_icon_chevron_down", 30, 114, 16, 16, 1f);
        }

        /// <summary>Tip card behind the frame: pictogram + "keep the frame flat on the table". Sized to its title.</summary>
        void BuildGuideCard()
        {
            var card = new GameObject("Calibration Guide Card", typeof(RectTransform));
            card.transform.SetParent(m_FrameRoot, false);
            m_Card = card.transform;
            m_CardCanvas = CreateCanvas(m_Card, "Card Visual", 300f, CardHeight, 10);
            m_CardBackground = AddImage(m_CardCanvas, "Background", "calib_card_dark", 0, 0, 300f, CardHeight, 48f / 40f);
            m_CardIcon = AddImage(m_CardCanvas, "Icon", "calib_flat_icon_candy", 16, 12, 88, 56, 1f);
            var title = m_English ? "Keep the frame flat on the table" : "边框要贴平桌面";
            m_CardTitle = AddText(m_CardCanvas, "Title", title, 118, 0, 260, CardHeight, 19, true, TextAlignmentOptions.MidlineLeft, Color.white);
            var titleWidth = PreferredWidth(m_CardTitle, title, 19f);
            var width = 16f + 88f + 14f + titleWidth + 24f;
            m_CardCanvas.sizeDelta = new Vector2(width, CardHeight);
            Place(m_CardBackground.rectTransform, 0, 0, width, CardHeight);
            Place(m_CardTitle.rectTransform, 118, 0, titleWidth + 4f, CardHeight);
        }

        /// <summary>TextMeshPro's measured width, with a character-count fallback when no font is loaded (edit-mode tests).</summary>
        static float PreferredWidth(TMP_Text label, string text, float fontSize)
        {
            var measured = label.font != null ? label.GetPreferredValues(text).x : 0f;
            if (measured > 1f && !float.IsNaN(measured) && !float.IsInfinity(measured))
                return measured;
            var estimate = 0f;
            foreach (var c in text)
                estimate += c > 0x2E80 ? fontSize : fontSize * .55f;
            return estimate;
        }

        Image AddImage(RectTransform parent, string objectName, string spriteName, float x, float y, float w, float h, float multiplier)
        {
            var go = new GameObject(objectName, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Place(go.GetComponent<RectTransform>(), x, y, w, h);
            var image = go.GetComponent<Image>();
            image.raycastTarget = false;
            if (spriteName != null)
            {
                image.sprite = LoadSprite(spriteName);
                image.type = image.sprite != null && image.sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
                image.pixelsPerUnitMultiplier = multiplier;
            }
            return image;
        }

        TextMeshProUGUI AddText(RectTransform parent, string objectName, string text, float x, float y, float w, float h,
            float size, bool bold, TextAlignmentOptions alignment, Color color)
        {
            var go = new GameObject(objectName, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var label = go.AddComponent<TextMeshProUGUI>();
            Place(label.rectTransform, x, y, w, h);
            label.font = m_English ? m_EnglishFont : m_ChineseFont;
            label.text = text;
            label.fontSize = size;
            label.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            label.alignment = alignment;
            label.color = color;
            label.raycastTarget = false;
            return label;
        }

        static void Place(RectTransform rect, float x, float y, float w, float h)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(w, h);
        }

        void CreateAction(string text, Color color, CalibrationAction action)
        {
            var round = action is CalibrationAction.RaiseTable or CalibrationAction.LowerTable;
            var button = new GameObject(action switch
            {
                CalibrationAction.Confirm => "Calibration Confirm Button",
                CalibrationAction.Reset => "Calibration Reset Button",
                CalibrationAction.RaiseTable => "Calibration Raise Table Button",
                _ => "Calibration Lower Table Button"
            }, typeof(BoxCollider));
            button.transform.SetParent(m_FrameRoot, false);
            button.AddComponent<SensoryBagCalibrationHandle>().Configure(this, CalibrationHandleKind.Frame, action);

            if (round)
            {
                const float size = 44f;
                button.GetComponent<BoxCollider>().size = new Vector3((size + 6f) * Unit, (size + 6f) * Unit, .02f);
                var circle = CreateCanvas(button.transform, "Button Visual", size, size, 11);
                AddImage(circle, "Rounded Background", "calib_circle_glass", 0, 0, size, size, 1f);
                AddImage(circle, "Icon", action == CalibrationAction.RaiseTable ? "calib_icon_plus" : "calib_icon_minus", 12, 12, 20, 20, 1f);
                return;
            }

            var confirm = action == CalibrationAction.Confirm;
            var padding = confirm ? 22f : 18f;
            var labelText = confirm ? (m_English ? "Confirm" : "确认") : (m_English ? "Reset" : "重置");
            var canvas = CreateCanvas(button.transform, "Button Visual", 120f, ButtonHeight, 11);
            var background = AddImage(canvas, "Rounded Background", confirm ? "calib_btn_primary" : "calib_card_dark", 0, 0, 120f, ButtonHeight, 48f / 23f);
            if (confirm) m_ConfirmBackground = background;
            AddImage(canvas, "Icon", confirm ? "calib_icon_check" : "calib_icon_reset", padding, 14.5f, 17, 17, 1f);
            var label = CreateLabel(canvas);
            label.text = labelText;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            var labelWidth = PreferredWidth(label, labelText, 18f);
            var width = padding + 17f + 8f + labelWidth + padding;
            canvas.sizeDelta = new Vector2(width, ButtonHeight);
            Place(background.rectTransform, 0, 0, width, ButtonHeight);
            label.rectTransform.offsetMin = new Vector2(padding + 25f, 0f);
            label.rectTransform.offsetMax = new Vector2(-padding + 4f, 0f);
            button.GetComponent<BoxCollider>().size = new Vector3((width + 6f) * Unit, (ButtonHeight + 6f) * Unit, .02f);
            if (confirm) m_ConfirmWidth = width; else m_ResetWidth = width;
        }

        TextMeshProUGUI CreateLabel(RectTransform parent)
        {
            var label = new GameObject("Label", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            label.transform.SetParent(parent, false);
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;
            label.font = m_English ? m_EnglishFont : m_ChineseFont;
            label.fontSize = 18f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            label.raycastTarget = false;
            return label;
        }

        Color Accent => CurrentData.WorldKind == SensoryBagWorldKind.Nature ? m_NatureAccent : m_CandyAccent;

        void RefreshVisuals()
        {
            if (m_Rectangle == null || m_FrameRoot == null)
                return;
            var data = m_Rectangle.Data;
            var halfWidth = data.Width * .5f;
            var halfHeight = data.Height * .5f;

            PlaceEdge(CalibrationHandleKind.Left, new Vector3(-halfWidth, 0f, 0f), data.Height, 90f);
            PlaceEdge(CalibrationHandleKind.Right, new Vector3(halfWidth, 0f, 0f), data.Height, 90f);
            PlaceEdge(CalibrationHandleKind.Top, new Vector3(0f, halfHeight, 0f), data.Width, 0f);
            PlaceEdge(CalibrationHandleKind.Bottom, new Vector3(0f, -halfHeight, 0f), data.Width, 0f);
            PlaceCorner(CalibrationHandleKind.TopLeft, new Vector3(-halfWidth, halfHeight, -.001f), 0f);
            PlaceCorner(CalibrationHandleKind.TopRight, new Vector3(halfWidth, halfHeight, -.001f), -90f);
            PlaceCorner(CalibrationHandleKind.BottomRight, new Vector3(halfWidth, -halfHeight, -.001f), 180f);
            PlaceCorner(CalibrationHandleKind.BottomLeft, new Vector3(-halfWidth, -halfHeight, -.001f), 90f);

            if (m_FrameBody != null)
            {
                m_FrameBody.transform.localPosition = new Vector3(0f, 0f, .006f);
                m_FrameBody.transform.localScale = new Vector3(
                    Mathf.Max(.001f, data.Width - m_BorderThickness), Mathf.Max(.001f, data.Height - m_BorderThickness), .004f);
            }

            if (m_HeightValueLabel != null)
                m_HeightValueLabel.text = (data.PlaneLocalOffset.y * 100f).ToString("+0.0;-0.0;0.0", System.Globalization.CultureInfo.InvariantCulture);
            if (m_CardIcon != null)
                m_CardIcon.sprite = LoadSprite(data.WorldKind == SensoryBagWorldKind.Nature ? "calib_flat_icon_nature" : "calib_flat_icon_candy");
            if (m_ConfirmBackground != null) m_ConfirmBackground.color = Accent;
            SyncFrameNow();
            UpdateSpatialLayout();
        }

        void PlaceEdge(CalibrationHandleKind kind, Vector3 position, float length, float angle)
        {
            if (!m_Visuals.TryGetValue(kind, out var edge) || edge == null)
                return;
            var spriteLength = SpriteWidth(edge);
            edge.localPosition = position;
            edge.localRotation = Quaternion.Euler(0f, 0f, angle);
            edge.localScale = new Vector3(length / spriteLength, 1f, 1f);
            // Hit target: the full edge, 4 cm across, in the line's own (stretched) space.
            var collider = edge.GetComponent<BoxCollider>();
            collider.center = Vector3.zero;
            collider.size = new Vector3(spriteLength, .04f, .04f);
            if (m_Grips.TryGetValue(kind, out var grip) && grip != null)
            {
                grip.transform.localPosition = position + new Vector3(0f, 0f, -.0015f);
                grip.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
            }
        }

        void PlaceCorner(CalibrationHandleKind kind, Vector3 position, float angle)
        {
            if (!m_Visuals.TryGetValue(kind, out var corner) || corner == null)
                return;
            corner.localPosition = position;
            corner.localRotation = Quaternion.Euler(0f, 0f, angle);
            var collider = corner.GetComponent<BoxCollider>();
            collider.center = Vector3.zero;
            collider.size = new Vector3(.05f, .05f, .04f);
        }

        static float SpriteWidth(Transform t)
        {
            var renderer = t.GetComponent<SpriteRenderer>();
            return renderer != null && renderer.sprite != null ? Mathf.Max(.001f, renderer.sprite.bounds.size.x) : .016f;
        }

        /// <summary>
        /// Elevator on the viewer's left of the frame, tip card + Reset/Confirm behind the frame. Both stand upright and
        /// face the viewer. Everything stays a child of the frame root, so it moves with tracking and the table height.
        /// </summary>
        void UpdateSpatialLayout()
        {
            if (m_FrameRoot == null || m_Rectangle == null || !m_FrameRoot.gameObject.activeInHierarchy)
                return;
            if (m_Viewer == null && Camera.main != null)
                m_Viewer = Camera.main.transform;
            var data = m_Rectangle.Data;
            var up = -m_FrameRoot.forward; // frame local -Z points away from the table
            var center = m_FrameRoot.position;
            var right = m_Viewer != null ? Vector3.ProjectOnPlane(m_Viewer.right, up) : Vector3.zero;
            if (right.sqrMagnitude < 1e-4f) right = m_FrameRoot.right;
            right.Normalize();
            var away = m_Viewer != null ? Vector3.ProjectOnPlane(center - m_Viewer.position, up) : Vector3.zero;
            if (away.sqrMagnitude < 1e-4f) away = m_FrameRoot.up;
            away.Normalize();
            float Extent(Vector3 direction) =>
                Mathf.Abs(Vector3.Dot(direction, m_FrameRoot.right)) * data.Width * .5f +
                Mathf.Abs(Vector3.Dot(direction, m_FrameRoot.up)) * data.Height * .5f;
            Quaternion Facing(Vector3 position)
            {
                var toward = m_Viewer != null ? position - m_Viewer.position : away;
                return toward.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(toward.normalized, up) : m_FrameRoot.rotation;
            }

            // Elevator, its + / - buttons, the dashed line and the ring on the table.
            var basePoint = center - right * (Extent(right) + m_ElevatorGap + ElevatorWidth * Unit * .5f);
            var elevatorPoint = basePoint + up * (m_ElevatorLift + ElevatorHeight * Unit * .5f);
            var elevatorRotation = Facing(elevatorPoint);
            if (m_Visuals.TryGetValue(CalibrationHandleKind.Height, out var elevator) && elevator != null)
                elevator.SetPositionAndRotation(elevatorPoint, elevatorRotation);
            Put("Calibration Raise Table Button", elevatorPoint, elevatorRotation, 0f, StepOffset);
            Put("Calibration Lower Table Button", elevatorPoint, elevatorRotation, 0f, -StepOffset);
            if (m_HeightRail != null)
            {
                var railPoint = basePoint + up * (m_ElevatorLift * .5f);
                var railForward = Vector3.ProjectOnPlane(m_Viewer != null ? railPoint - m_Viewer.position : away, up);
                m_HeightRail.SetPositionAndRotation(railPoint,
                    railForward.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(railForward.normalized, up) : m_FrameRoot.rotation);
                var railSprite = m_HeightRail.GetComponent<SpriteRenderer>().sprite;
                var railLength = railSprite != null ? railSprite.bounds.size.y : .1f;
                m_HeightRail.localScale = new Vector3(1f, m_ElevatorLift / Mathf.Max(.001f, railLength), 1f);
            }
            if (m_PlaneRing != null)
                m_PlaneRing.SetPositionAndRotation(basePoint, m_FrameRoot.rotation);

            // Tip card with Reset / Confirm underneath, behind the frame.
            var clusterHeight = CardHeight + ClusterGap + ButtonHeight;
            var clusterPoint = center + away * (Extent(away) + m_GuideGap) + up * m_GuideLift;
            var clusterRotation = Facing(clusterPoint);
            if (m_Card != null)
                m_Card.SetPositionAndRotation(clusterPoint + clusterRotation * (new Vector3(0f, clusterHeight * .5f - CardHeight * .5f, 0f) * Unit), clusterRotation);
            var buttonsY = -(clusterHeight * .5f - ButtonHeight * .5f);
            var row = m_ResetWidth + ButtonGap + m_ConfirmWidth;
            Put("Calibration Reset Button", clusterPoint, clusterRotation, -row * .5f + m_ResetWidth * .5f, buttonsY);
            Put("Calibration Confirm Button", clusterPoint, clusterRotation, row * .5f - m_ConfirmWidth * .5f, buttonsY);
        }

        /// <summary>Places a direct child of the frame root at an offset (canvas units) on an upright panel.</summary>
        void Put(string childName, Vector3 origin, Quaternion rotation, float xUnits, float yUnits)
        {
            var child = m_FrameRoot.Find(childName);
            if (child != null)
                child.SetPositionAndRotation(origin + rotation * new Vector3(xUnits * Unit, yUnits * Unit, -ButtonLift), rotation);
        }

        /// <summary>Tints whatever is being dragged with the world colour; everything else stays cool white.</summary>
        void UpdateStateVisuals()
        {
            if (m_FrameRoot == null || !m_FrameRoot.gameObject.activeInHierarchy)
                return;
            var accent = Accent;
            var heightDrag = false;
            var frameDrag = false;
            var active = new HashSet<CalibrationHandleKind>();
            foreach (var kind in m_Drags.Values)
            {
                active.Add(kind);
                if (kind == CalibrationHandleKind.Height) heightDrag = true;
                else frameDrag = true;
            }
            var frameColor = frameDrag ? accent : m_BorderColor;
            foreach (var edge in Edges)
            {
                if (m_Visuals.TryGetValue(edge, out var line) && line != null && line.TryGetComponent<SpriteRenderer>(out var lineRenderer))
                    lineRenderer.color = frameColor;
                if (m_Grips.TryGetValue(edge, out var grip) && grip != null)
                {
                    grip.color = frameDrag ? accent : m_HandleColor;
                    grip.transform.localScale = Vector3.one * (active.Contains(edge) ? 1.14f : 1f);
                }
            }
            foreach (var corner in Corners)
            {
                if (!m_Visuals.TryGetValue(corner, out var bracket) || bracket == null) continue;
                if (bracket.TryGetComponent<SpriteRenderer>(out var bracketRenderer))
                    bracketRenderer.color = active.Contains(corner) ? accent : frameDrag ? frameColor : m_HandleColor;
                bracket.localScale = Vector3.one * (active.Contains(corner) ? 1.14f : 1f);
            }

            if (m_ElevatorBackground != null)
            {
                m_ElevatorBackground.sprite = LoadSprite(heightDrag ? "calib_elevator_light" : "calib_elevator_dark");
                m_ElevatorBackground.color = heightDrag ? accent : Color.white;
            }
            if (m_HeightRail != null) m_HeightRail.GetComponent<SpriteRenderer>().color = heightDrag ? accent : new Color(1f, 1f, 1f, .55f);
            if (m_PlaneRing != null) m_PlaneRing.GetComponent<SpriteRenderer>().color = heightDrag ? accent : new Color(1f, 1f, 1f, .6f);
        }

        void SetFrameVisible(bool visible)
        {
            m_FrameVisible = visible;
            if (m_FrameRoot != null)
                m_FrameRoot.gameObject.SetActive(visible);
        }

        void UpdateDebugData()
        {
            if (m_Rectangle != null)
            {
                var data = m_Rectangle.Data;
                m_ReferenceImageName = data.ReferenceImageName;
                m_CurrentWidth = data.Width;
                m_CurrentHeight = data.Height;
                m_CurrentCenterOffset = data.CenterOffset;
            }
            if (m_Anchor != null)
            {
                m_CurrentAnchorPosition = m_Anchor.position;
                m_CurrentAnchorEulerAngles = m_Anchor.eulerAngles;
            }
        }

        static void SetColor(Renderer renderer, Color color)
        {
            if (renderer == null)
                return;
            var propertyBlock = new MaterialPropertyBlock();
            propertyBlock.SetColor("_BaseColor", color);
            propertyBlock.SetColor("_Color", color);
            renderer.SetPropertyBlock(propertyBlock);
        }

        static Material GetRuntimeUiMaterial()
        {
            if (s_RuntimeUiMaterial != null)
                return s_RuntimeUiMaterial;
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            s_RuntimeUiMaterial = new Material(shader) { name = "Moodium Runtime Spatial UI" };
            return s_RuntimeUiMaterial;
        }

        void OnDestroy()
        {
            if (m_FrameRoot != null)
            {
                if (Application.isPlaying) Destroy(m_FrameRoot.gameObject);
                else DestroyImmediate(m_FrameRoot.gameObject);
            }
        }
    }
}
