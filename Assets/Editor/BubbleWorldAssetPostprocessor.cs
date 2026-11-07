using UnityEditor;
using UnityEngine;

/// <summary>
/// Import rules for Assets/Resources/MoodiumBubbleWorld/:
/// point_colors — one texel per point, so it must stay exact: no compression, no mipmaps, point filter, no resize.
/// panorama — 2048×1024 sky, wraps horizontally, mipmapped.
/// panorama_soft — 64×32 blur of the panorama (the default soft sky): exact colours, smooth bilinear, no mipmaps.
/// bubble_film — the bubble's soft iridescent film, alpha kept.
/// Re-import the folder after copying the files in.
/// </summary>
public sealed class BubbleWorldAssetPostprocessor : AssetPostprocessor
{
    const string Folder = "Assets/Resources/MoodiumBubbleWorld/";

    void OnPreprocessTexture()
    {
        var path = assetPath.Replace('\\', '/');
        if (!path.StartsWith(Folder))
            return;
        var importer = (TextureImporter)assetImporter;
        var name = System.IO.Path.GetFileNameWithoutExtension(path);
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.isReadable = false;
        switch (name)
        {
            case "point_colors_round":
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Bilinear;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = 4096;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = false;
                break;
            case "point_colors":
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Point;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = 2048;
                importer.alphaSource = TextureImporterAlphaSource.None;
                break;
            case "panorama":
                importer.mipmapEnabled = true;
                importer.filterMode = FilterMode.Bilinear;
                importer.wrapModeU = TextureWrapMode.Repeat;
                importer.wrapModeV = TextureWrapMode.Clamp;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.maxTextureSize = 4096;
                importer.alphaSource = TextureImporterAlphaSource.None;
                break;
            case "panorama_soft":
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Bilinear;
                importer.wrapModeU = TextureWrapMode.Repeat;
                importer.wrapModeV = TextureWrapMode.Clamp;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = 64;
                importer.alphaSource = TextureImporterAlphaSource.None;
                break;
            case "bubble_film":
                importer.mipmapEnabled = true;
                importer.filterMode = FilterMode.Bilinear;
                importer.wrapModeU = TextureWrapMode.Repeat;
                importer.wrapModeV = TextureWrapMode.Clamp;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                break;
        }
    }
}
