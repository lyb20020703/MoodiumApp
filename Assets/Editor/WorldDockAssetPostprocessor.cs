using UnityEditor;
using UnityEngine;

/// <summary>
/// Import settings for the floating world dock sprites in Assets/Resources/MoodiumWorldDock/.
/// Buttons are exported at 3x with 26 design px = 1 cm, so 780 px = 0.10 m: PPU = 7800.
/// A button sprite (1140 x 648 px) is 0.146 x 0.083 m; the glass capsule inside is 0.100 x 0.037 m.
/// Re-import the folder after copying the PNGs in.
/// </summary>
public sealed class WorldDockAssetPostprocessor : AssetPostprocessor
{
    const string Folder = "Assets/Resources/MoodiumWorldDock/";
    public const float PixelsPerUnit = 780f / .10f;          // 7800

    void OnPreprocessTexture()
    {
        var path = assetPath.Replace('\\', '/');
        if (!path.StartsWith(Folder))
            return;
        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = PixelsPerUnit;
        importer.alphaIsTransparency = true;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.sRGBTexture = true;
        importer.mipmapEnabled = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Trilinear;
        importer.maxTextureSize = 2048;
        importer.textureCompression = TextureImporterCompression.Uncompressed;

        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        settings.spriteAlignment = (int)SpriteAlignment.Center;
        importer.SetTextureSettings(settings);
        importer.spriteBorder = Vector4.zero;
    }
}
