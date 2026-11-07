using UnityEditor;
using UnityEngine;

/// <summary>
/// Import settings for every sprite in Assets/Resources/MoodiumOpening/.
/// Same scale as the world guide cards: 1 design px = 0.42 / 520 m, art exported at 1.5x,
/// so the 220 x 76 px capsule comes out at 0.178 x 0.061 m with no extra scaling.
/// Re-import the folder (right click → Reimport) after copying the PNGs in.
/// </summary>
public sealed class OpeningButtonAssetPostprocessor : AssetPostprocessor
{
    const string Folder = "Assets/Resources/MoodiumOpening/";
    public const float PixelsPerUnit = 1.5f / (0.42f / 520f); // 1857.142857

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
        // Small, soft gradients: keep uncompressed so the glass does not band.
        importer.textureCompression = TextureImporterCompression.Uncompressed;

        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        settings.spriteAlignment = (int)SpriteAlignment.Center;
        importer.SetTextureSettings(settings);
        importer.spriteBorder = Vector4.zero;
    }
}
