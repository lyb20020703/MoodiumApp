using System;
using UnityEngine;

namespace Moodium.Calibration
{
    public enum SensoryBagWorldKind
    {
        Candy,
        Nature
    }

    [Serializable]
    public struct SensoryBagCalibrationSettings
    {
        public SensoryBagCalibrationSettings(
            float defaultWidth,
            float defaultHeight,
            float defaultDepth,
            Vector2 defaultCenterOffset,
            float minWidth,
            float maxWidth,
            float minHeight,
            float maxHeight)
        {
            DefaultWidth = defaultWidth;
            DefaultHeight = defaultHeight;
            DefaultDepth = defaultDepth;
            DefaultCenterOffset = defaultCenterOffset;
            MinWidth = minWidth;
            MaxWidth = maxWidth;
            MinHeight = minHeight;
            MaxHeight = maxHeight;
        }

        public float DefaultWidth;
        public float DefaultHeight;
        public float DefaultDepth;
        public Vector2 DefaultCenterOffset;
        public float MinWidth;
        public float MaxWidth;
        public float MinHeight;
        public float MaxHeight;

        public float ClampWidth(float width) => Mathf.Clamp(width, MinWidth, Mathf.Max(MinWidth, MaxWidth));
        public float ClampHeight(float height) => Mathf.Clamp(height, MinHeight, Mathf.Max(MinHeight, MaxHeight));
    }

    [Serializable]
    public struct SensoryBagCalibrationData
    {
        [SerializeField] string m_ReferenceImageName;
        [SerializeField] SensoryBagWorldKind m_WorldKind;
        [SerializeField] float m_Width;
        [SerializeField] float m_Height;
        [SerializeField] float m_Depth;
        [SerializeField] Vector2 m_CenterOffset;
        [SerializeField] Vector3 m_PlaneLocalOffset;
        [SerializeField] Quaternion m_LocalOrientation;

        public string ReferenceImageName { get => m_ReferenceImageName; set => m_ReferenceImageName = value; }
        public SensoryBagWorldKind WorldKind { get => m_WorldKind; set => m_WorldKind = value; }
        public float Width { get => m_Width; set => m_Width = value; }
        public float Height { get => m_Height; set => m_Height = value; }
        public float Depth { get => m_Depth; set => m_Depth = value; }
        public Vector2 CenterOffset { get => m_CenterOffset; set => m_CenterOffset = value; }
        public Vector3 PlaneLocalOffset { get => m_PlaneLocalOffset; set => m_PlaneLocalOffset = value; }
        public Quaternion LocalOrientation { get => m_LocalOrientation; set => m_LocalOrientation = value; }

        public static SensoryBagCalibrationData CreateDefault(
            string referenceImageName,
            SensoryBagWorldKind worldKind,
            SensoryBagCalibrationSettings settings)
        {
            var data = new SensoryBagCalibrationData
            {
                ReferenceImageName = referenceImageName,
                WorldKind = worldKind,
                Width = settings.DefaultWidth,
                Height = settings.DefaultHeight,
                Depth = settings.DefaultDepth,
                CenterOffset = settings.DefaultCenterOffset,
                // Sticker is attached to the top of the real bag, not its supporting table.
                PlaneLocalOffset = Vector3.down * settings.DefaultDepth,
                // The reference image lives in the anchor's X/Z plane on visionOS.
                // Runtime calibration visuals are authored in local X/Y, therefore
                // rotate that visual plane onto the horizontal bag/table plane.
                LocalOrientation = Quaternion.Euler(90f, 0f, 0f)
            };
            data.ClampDimensions(settings);
            return data;
        }

        public void ClampDimensions(SensoryBagCalibrationSettings settings)
        {
            Width = settings.ClampWidth(Width);
            Height = settings.ClampHeight(Height);
        }
    }
}
