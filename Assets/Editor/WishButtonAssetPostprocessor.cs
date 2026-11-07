using UnityEditor;
using UnityEngine;

/// <summary>
/// Import settings for the wish-chime button sprites in Assets/Resources/MoodiumBubblePlay/WishButtons/.
/// Art is exported at 3x: the 220 x 76 design-px capsule is 660 x 228 px and must come out 0.090 x 0.031 m,
/// so Pixels Per Unit = 660 / 0.09. The whole sprite (with shadow margin) is 0.136 x 0.077 m. Pivot = centre.
/// Re-import the folder after copying the PNGs in.
/// </summary>
public sealed class WishButtonAssetPostprocessor : AssetPostprocessor
{
    const string Folder = "Assets/Resources/MoodiumBubblePlay/WishButtons/";
    public const float PixelsPerUnit = 660f / 0.09f; // 7333.333

    void OnPreprocessTexture()
    {
        if (!assetPath.Replace('\\', '/').StartsWith(Folder))
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
        importer.maxTextureSize = 1024;
        // Soft gradients: keep uncompressed so the glass does not band.
        importer.textureCompression = TextureImporterCompression.Uncompressed;

        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        settings.spriteAlignment = (int)SpriteAlignment.Center;
        importer.SetTextureSettings(settings);
        importer.spriteBorder = Vector4.zero;
    }
}
