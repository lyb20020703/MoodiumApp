using System.IO;
using UnityEditor;
using UnityEngine;
using TMPro;

public static class MoodiumModeCardSpriteSetup
{
    const string SpaceSource = "Assets/UI/Image/Space Creation- UI.png";
    const string RealitySource = "Assets/UI/Image/Augmented Reality-UI.png";
    const string ResourceFolder = "Assets/Resources/UI";
    const string SpaceResource = ResourceFolder + "/SpaceCreationUI.png";
    const string RealityResource = ResourceFolder + "/AugmentedRealityUI.png";

    [MenuItem("Moodium/Setup Mode Card Sprites")]
    public static void Setup()
    {
        EnsureFolder(ResourceFolder);
        ConfigureSprite(SpaceSource);
        ConfigureSprite(RealitySource);
        CreateRoundedResourceSprite(SpaceSource, SpaceResource);
        CreateRoundedResourceSprite(RealitySource, RealityResource);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        var space = Resources.Load<Sprite>("UI/SpaceCreationUI");
        var reality = Resources.Load<Sprite>("UI/AugmentedRealityUI");
        var chineseFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
            "Assets/UI/Moodium/Fonts/AlibabaPuHuiTi Moodium SDF.asset");
        if (chineseFont != null)
        {
            chineseFont.TryAddCharacters(
                "空间创造模式现实增强打造属于你的幻想世界释放无限创意赋予真实世界魔力开启感官新体验，。",
                out var missingCharacters);
            EditorUtility.SetDirty(chineseFont);
            if (!string.IsNullOrEmpty(missingCharacters))
                Debug.LogWarning($"[Moodium UI] Chinese font still misses: {missingCharacters}");
        }
        Debug.Log(
            $"MOODIUM_MODE_CARD_SPRITES_READY: spaceCreationSprite is null={space == null}; " +
            $"augmentedRealitySprite is null={reality == null}");
    }

    static void CreateRoundedResourceSprite(string source, string destination)
    {
        var sourceTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!ImageConversion.LoadImage(sourceTexture, File.ReadAllBytes(source), false))
            throw new IOException($"Could not decode mode card sprite at {source}.");

        var pixels = sourceTexture.GetPixels32();
        var width = sourceTexture.width;
        var height = sourceTexture.height;
        var radius = Mathf.RoundToInt(Mathf.Min(width, height) * 0.075f);
        const float feather = 3f;
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var cornerX = x < radius ? radius - x : x >= width - radius ? x - (width - radius - 1) : 0f;
            var cornerY = y < radius ? radius - y : y >= height - radius ? y - (height - radius - 1) : 0f;
            if (cornerX <= 0f || cornerY <= 0f)
                continue;
            var distance = Mathf.Sqrt(cornerX * cornerX + cornerY * cornerY);
            var coverage = Mathf.Clamp01((radius - distance + feather) / feather);
            var index = y * width + x;
            var color = pixels[index];
            color.a = (byte)Mathf.RoundToInt(color.a * coverage);
            pixels[index] = color;
        }
        sourceTexture.SetPixels32(pixels);
        sourceTexture.Apply(false, false);
        File.WriteAllBytes(destination, sourceTexture.EncodeToPNG());
        Object.DestroyImmediate(sourceTexture);
        AssetDatabase.ImportAsset(destination, ImportAssetOptions.ForceSynchronousImport);
        ConfigureSprite(destination);
    }

    static void ConfigureSprite(string path)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
            throw new FileNotFoundException("Texture importer was not found.", path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.sRGBTexture = true;
        importer.mipmapEnabled = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.maxTextureSize = 2048;
        importer.SaveAndReimport();
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;
        var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        if (!string.IsNullOrEmpty(parent))
            EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
