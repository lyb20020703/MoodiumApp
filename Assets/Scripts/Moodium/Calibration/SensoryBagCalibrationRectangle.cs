using UnityEngine;

namespace Moodium.Calibration
{
    public enum CalibrationHandleKind
    {
        Frame,
        Left,
        Right,
        Top,
        Bottom,
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight,
        Height,
        Rotate
    }

    /// <summary>Anchor-local two-dimensional bag bounds. It has no world-space mutable state.</summary>
    public sealed class SensoryBagCalibrationRectangle
    {
        SensoryBagCalibrationData m_Data;
        readonly SensoryBagCalibrationSettings m_Settings;

        public SensoryBagCalibrationRectangle(
            SensoryBagCalibrationData initialData,
            SensoryBagCalibrationSettings settings)
        {
            m_Data = initialData;
            m_Settings = settings;
            m_Data.ClampDimensions(settings);
        }

        public SensoryBagCalibrationData Data => m_Data;
        public float Left => m_Data.CenterOffset.x - m_Data.Width * 0.5f;
        public float Right => m_Data.CenterOffset.x + m_Data.Width * 0.5f;
        public float Bottom => m_Data.CenterOffset.y - m_Data.Height * 0.5f;
        public float Top => m_Data.CenterOffset.y + m_Data.Height * 0.5f;

        public void MoveTo(Vector2 centerOffset)
        {
            m_Data.CenterOffset = centerOffset;
        }

        public void SetPlaneHeight(float offset)
        {
            if(float.IsNaN(offset)||float.IsInfinity(offset))return;
            var plane=m_Data.PlaneLocalOffset;
            plane.y=Mathf.Clamp(offset,-.25f,.10f);
            m_Data.PlaneLocalOffset=plane;
        }

        public void ResizeFromHandle(CalibrationHandleKind handle, Vector2 point)
        {
            var left = Left;
            var right = Right;
            var bottom = Bottom;
            var top = Top;

            switch (handle)
            {
                case CalibrationHandleKind.Left:
                    left = ClampMovingLower(point.x, right, m_Settings.MinWidth, m_Settings.MaxWidth);
                    break;
                case CalibrationHandleKind.Right:
                    right = ClampMovingUpper(point.x, left, m_Settings.MinWidth, m_Settings.MaxWidth);
                    break;
                case CalibrationHandleKind.Bottom:
                    bottom = ClampMovingLower(point.y, top, m_Settings.MinHeight, m_Settings.MaxHeight);
                    break;
                case CalibrationHandleKind.Top:
                    top = ClampMovingUpper(point.y, bottom, m_Settings.MinHeight, m_Settings.MaxHeight);
                    break;
                case CalibrationHandleKind.TopLeft:
                    left = ClampMovingLower(point.x, right, m_Settings.MinWidth, m_Settings.MaxWidth);
                    top = ClampMovingUpper(point.y, bottom, m_Settings.MinHeight, m_Settings.MaxHeight);
                    break;
                case CalibrationHandleKind.TopRight:
                    right = ClampMovingUpper(point.x, left, m_Settings.MinWidth, m_Settings.MaxWidth);
                    top = ClampMovingUpper(point.y, bottom, m_Settings.MinHeight, m_Settings.MaxHeight);
                    break;
                case CalibrationHandleKind.BottomLeft:
                    left = ClampMovingLower(point.x, right, m_Settings.MinWidth, m_Settings.MaxWidth);
                    bottom = ClampMovingLower(point.y, top, m_Settings.MinHeight, m_Settings.MaxHeight);
                    break;
                case CalibrationHandleKind.BottomRight:
                    right = ClampMovingUpper(point.x, left, m_Settings.MinWidth, m_Settings.MaxWidth);
                    bottom = ClampMovingLower(point.y, top, m_Settings.MinHeight, m_Settings.MaxHeight);
                    break;
                case CalibrationHandleKind.Frame:
                default:
                    return;
            }

            m_Data.Width = right - left;
            m_Data.Height = top - bottom;
            m_Data.CenterOffset = new Vector2((left + right) * 0.5f, (bottom + top) * 0.5f);
            m_Data.ClampDimensions(m_Settings);
        }

        public static Vector2 ProjectWorldPointToAnchorPlane(Transform anchor, Vector3 worldPoint)
        {
            if (anchor == null)
                return Vector2.zero;
            var local = anchor.InverseTransformPoint(worldPoint);
            // visionOS image anchors use X/Z as the detected image surface. Y is
            // the surface normal, so calibration stays horizontal with a bag on a table.
            return new Vector2(local.x, local.z);
        }

        static float ClampMovingLower(float requested, float fixedUpper, float minSize, float maxSize)
        {
            var safeMin = Mathf.Max(0.001f, minSize);
            var safeMax = Mathf.Max(safeMin, maxSize);
            return Mathf.Clamp(requested, fixedUpper - safeMax, fixedUpper - safeMin);
        }

        static float ClampMovingUpper(float requested, float fixedLower, float minSize, float maxSize)
        {
            var safeMin = Mathf.Max(0.001f, minSize);
            var safeMax = Mathf.Max(safeMin, maxSize);
            return Mathf.Clamp(requested, fixedLower + safeMin, fixedLower + safeMax);
        }
    }
}
