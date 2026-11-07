using UnityEditor;
using UnityEngine;

/// <summary>
/// Import settings for the bubble-choice tags and ring in Assets/Resources/MoodiumBubbleWorld/Choice/ (export_choice.py):
/// exported at 1 px = 0.25 mm, so Pixels Per Unit = 4000 (BubbleChoiceRules scales the tags to 0.7 on top).
/// Pivot = centre. Re-import the folder after copying the PNGs in.
/// </summary>
public sealed class BubbleChoiceAssetPostprocessor : AssetPostprocessor
{
    const string Folder = "Assets/Resources/MoodiumBubbleWorld/Choice/";
    public const float PixelsPerUnit = 4000f;

    public override int GetPostprocessOrder() => 100;

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
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        settings.spriteAlignment = (int)SpriteAlignment.Center;
        importer.SetTextureSettings(settings);
        importer.spriteBorder = Vector4.zero;
    }
}
