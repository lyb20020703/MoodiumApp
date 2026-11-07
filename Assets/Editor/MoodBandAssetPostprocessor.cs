using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Applies the Mood Band (wrist UI v2) import conventions to every texture placed in
/// Assets/Resources/MoodiumWristUI/: Sprite type, 100 pixels per unit, 9-slice borders for
/// the card and chips, clamp wrap, no compression (gradients stay smooth), mipmaps on
/// (the UI is seen from 30–60 cm and shrinks a lot).
/// Re-import the folder (right click → Reimport) after copying the assets in.
/// </summary>
public sealed class MoodBandAssetPostprocessor : AssetPostprocessor
{
    const string Folder = "Assets/Resources/MoodiumWristUI/";

    // left, bottom, right, top — in source pixels.
    static readonly Dictionary<string, Vector4> SliceBorders = new()
    {
        { "wrist_card_glass", new Vector4(64, 64, 64, 64) },
        { "wrist_chip_glass", new Vector4(48, 48, 48, 48) },
        { "wrist_chip_dark", new Vector4(48, 48, 48, 48) },
        { "wrist_chip_solid", new Vector4(48, 48, 48, 48) },
        // v3 energy capsule: drawn at 2x (288 x 144, radius 72); the code sets pixelsPerUnitMultiplier = 2.
        { "wrist_capsule_dark", new Vector4(72, 72, 72, 72) },
    };

    void OnPreprocessTexture()
    {
        if (!assetPath.Replace('\\', '/').StartsWith(Folder))
            return;
        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 100f;
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
        settings.spriteAlignment = (int)SpriteAlignment.Center;
        importer.SetTextureSettings(settings);

        var name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
        importer.spriteBorder = SliceBorders.TryGetValue(name, out var border) ? border : Vector4.zero;
    }
}
