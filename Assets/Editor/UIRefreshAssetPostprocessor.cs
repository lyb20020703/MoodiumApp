using UnityEditor;
using UnityEngine;

/// <summary>
/// Import settings for the UI refresh sprites (energy ring, calibration rotate dial, settings)
/// and the opening buttons. Same scale as the world guide cards: 1 design px = 0.42 / 520 m,
/// art exported at 1.5x, so Pixels Per Unit = 1857.142857 and every sprite comes out at real size.
/// Re-import both folders (right click → Reimport) after copying the PNGs in.
/// </summary>
public sealed class UIRefreshAssetPostprocessor : AssetPostprocessor
{
    static readonly string[] Folders =
    {
        "Assets/Resources/MoodiumUIRefresh/",
        "Assets/Resources/MoodiumOpening/",
        "Assets/Resources/MoodiumWishJournal/",   // Wish Journal (same scale)
    };

    public const float PixelsPerUnit = 1.5f / (0.42f / 520f); // 1857.142857

    public override int GetPostprocessOrder() => 100;

    void OnPreprocessTexture()
    {
        var path = assetPath.Replace('\\', '/');
        var match = false;
        foreach (var folder in Folders)
            match |= path.StartsWith(folder);
        if (!match)
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
        // The journal panel is 1170 px wide at 1.5x: keep it at full size.
        importer.maxTextureSize = path.StartsWith("Assets/Resources/MoodiumWishJournal/") ? 2048 : 1024;
        // Small soft gradients and glows: keep uncompressed so they do not band.
        importer.textureCompression = TextureImporterCompression.Uncompressed;

        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        settings.spriteAlignment = (int)SpriteAlignment.Center;
        importer.SetTextureSettings(settings);
        importer.spriteBorder = Vector4.zero;
    }
}
