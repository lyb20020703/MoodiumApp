using UnityEditor;
using UnityEngine;

/// <summary>
/// Import settings for the preset chip sprites in Assets/Resources/MoodiumBubblePlay/WishPresets/Chips/.
/// Same pixels-per-metre as the wish buttons (WishButtonAssetPostprocessor): the 228 x 58 design-px capsule,
/// exported at 3x, comes out 0.0933 x 0.0237 m; the whole sprite (with shadow margin) is 0.129 x 0.060 m.
/// Re-import the folder after copying the PNGs in.
/// </summary>
public sealed class WishPresetChipAssetPostprocessor : AssetPostprocessor
{
    const string Folder = "Assets/Resources/MoodiumBubblePlay/WishPresets/Chips/";

    void OnPreprocessTexture()
    {
        if (!assetPath.Replace('\\', '/').StartsWith(Folder))
            return;
        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = WishButtonAssetPostprocessor.PixelsPerUnit;
        importer.alphaIsTransparency = true;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.sRGBTexture = true;
        importer.mipmapEnabled = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Trilinear;
        importer.maxTextureSize = 1024;
        importer.textureCompression = TextureImporterCompression.Uncompressed;

        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        settings.spriteAlignment = (int)SpriteAlignment.Center;
        importer.SetTextureSettings(settings);
        importer.spriteBorder = Vector4.zero;
    }
}
