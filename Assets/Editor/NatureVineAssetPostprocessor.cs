using UnityEditor;
using UnityEngine;

/// <summary>
/// Import settings for the Nature-world vine art (export_vine.py in the handoff package):
/// - Assets/Resources/MoodiumNature/VineLeaves/: 256 px leaf textures (stem at the bottom), plain textures used as
///   _BaseMap on copies of NatureIvyLeaf.mat (URP Lit, alpha clip). Mipmapped, clamped, alpha from the PNG.
/// - Assets/Resources/MoodiumNature/VineHint/: one-line hint sprites, 1 px = 0.25 mm, so Pixels Per Unit = 4000.
/// Re-import both folders after copying the PNGs in.
/// </summary>
public sealed class NatureVineAssetPostprocessor : AssetPostprocessor
{
    const string Leaves = "Assets/Resources/MoodiumNature/VineLeaves/";
    const string Hint = "Assets/Resources/MoodiumNature/VineHint/";
    public const float HintPixelsPerUnit = 4000f;

    void OnPreprocessTexture()
    {
        var path = assetPath.Replace('\\', '/');
        var nativeLeaf = path.StartsWith("Assets/Resources/MoodiumNature/NativeVine/") || path == "Assets/ARTnGAME/IvyStudio/Materials/Textures/Leaf 3.png";
        var leaves = path.StartsWith(Leaves) || nativeLeaf;
        if (!leaves && !path.StartsWith(Hint))
            return;
        var importer = (TextureImporter)assetImporter;
        importer.alphaIsTransparency = true;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.sRGBTexture = true;
        importer.mipmapEnabled = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Trilinear;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        if (leaves)
        {
            importer.textureType = TextureImporterType.Default;
            importer.maxTextureSize = nativeLeaf ? 2048 : 256;
            return;
        }
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = HintPixelsPerUnit;
        importer.maxTextureSize = 1024;
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        settings.spriteAlignment = (int)SpriteAlignment.Center;
        importer.SetTextureSettings(settings);
        importer.spriteBorder = Vector4.zero;
    }
}
