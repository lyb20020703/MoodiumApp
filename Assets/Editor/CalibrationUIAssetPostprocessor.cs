using UnityEditor;
using UnityEngine;

/// <summary>
/// Import settings for Assets/Resources/MoodiumCalibrationUI/ (calibration UI v2).
/// - Frame sprites are drawn at 4 px per millimetre, so Pixels Per Unit = 4000 makes them real size.
/// - UGUI sprites (height control, tip card, buttons, icons) sit on a 0.5 mm canvas; they stay at 100 PPU and
///   the code sets pixelsPerUnitMultiplier, so only their 9-slice borders matter here.
/// - The corner bracket's pivot is the frame corner (4 mm inside the bracket's outer corner).
/// Re-import the folder (right click → Reimport) after copying the PNGs in.
/// </summary>
public sealed class CalibrationUIAssetPostprocessor : AssetPostprocessor
{
    const string Folder = "Assets/Resources/MoodiumCalibrationUI/";
    public const float FramePixelsPerUnit = 4000f;

    static bool IsCanvasSprite(string name) =>
        name.StartsWith("calib_elevator") || name.StartsWith("calib_card") || name.StartsWith("calib_btn") ||
        name.StartsWith("calib_circle") || name.StartsWith("calib_icon") || name.StartsWith("calib_flat_icon");

    static Vector4 Border(string name) => name switch
    {
        "calib_elevator_dark" => new Vector4(48, 48, 48, 48),
        "calib_elevator_light" => new Vector4(48, 48, 48, 48),
        "calib_card_dark" => new Vector4(48, 48, 48, 48),
        "calib_btn_primary" => new Vector4(48, 48, 48, 48),
        _ => Vector4.zero
    };

    void OnPreprocessTexture()
    {
        if (!assetPath.Replace('\\', '/').StartsWith(Folder))
            return;
        var name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = IsCanvasSprite(name) ? 100f : FramePixelsPerUnit;
        importer.alphaIsTransparency = true;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.sRGBTexture = true;
        importer.mipmapEnabled = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Trilinear;
        importer.textureCompression = TextureImporterCompression.Uncompressed;

        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        if (name == "calib_corner")
        {
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = new Vector2(36f / 220f, 1f - 36f / 220f);
        }
        else
        {
            settings.spriteAlignment = (int)SpriteAlignment.Center;
        }
        importer.SetTextureSettings(settings);
        importer.spriteBorder = Border(name);
    }
}
