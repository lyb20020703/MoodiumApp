using UnityEditor;
using UnityEngine;

/// <summary>
/// Import settings for every sprite in Assets/Resources/MoodiumWorldGuide/.
/// Pixels Per Unit makes the sprites come out at their real size: a card face is 0.42 m wide
/// (the art is exported at 1.5x of a 520 px design, so 780 px = 0.42 m).
/// Re-import the folder (right click → Reimport) after copying the PNGs in.
/// </summary>
public sealed class WorldGuideAssetPostprocessor : AssetPostprocessor
{
    const string Folder = "Assets/Resources/MoodiumWorldGuide/";
    public const float PixelsPerUnit = 1.5f / (0.42f / 520f); // keep equal to WorldGuideCards.PixelsPerUnit

    void OnPreprocessTexture()
    {
        if (!assetPath.Replace('\\', '/').StartsWith(Folder))
            return;
        var importer = (TextureImporter)assetImporter;
        var name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
        var isFace = name.StartsWith("guide_card_") || name.StartsWith("guide_explore_");

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
        // Card faces are big; high-quality compression keeps text crisp at a quarter of the memory.
        // Buttons are small and stay uncompressed so their gradients do not band.
        importer.textureCompression = isFace ? TextureImporterCompression.CompressedHQ : TextureImporterCompression.Uncompressed;

        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        settings.spriteAlignment = (int)SpriteAlignment.Center;
        importer.SetTextureSettings(settings);
        importer.spriteBorder = Vector4.zero;
    }
}
