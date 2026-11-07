using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using Object = UnityEngine.Object;

public static class LxgwWenKaiTmpFontGenerator
{
    private const string SourceFontPath = "Assets/_Game/_Common/Font/LXGWWenKaiMono-Regular.ttf";
    private const string OutputFontAssetPath = "Assets/_Game/_Common/Font/LXGWWenKaiMono-Regular SDF.asset";
    private const string ProjectTextRoot = "Assets/_Game";
    private const int SamplingPointSize = 90;
    private const int AtlasPadding = 9;
    private const int AtlasSize = 2048;

    [MenuItem("Tools/VFXViewer/TextMeshPro/Generate LXGW WenKai Mono TMP Font")]
    public static void GenerateFromMenu()
    {
        Generate(logToConsole: true);
    }

    public static void GenerateBatchmode()
    {
        try
        {
            var result = Generate(logToConsole: true);
            EditorApplication.Exit(result ? 0 : 1);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    private static bool Generate(bool logToConsole)
    {
        if (TMP_Settings.instance == null)
        {
            Debug.LogError("TMP Settings not found. Please import TMP Essential Resources before generating the font asset.");
            return false;
        }

        Font sourceFont = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);
        if (sourceFont == null)
        {
            Debug.LogError($"Unable to load source font at {SourceFontPath}.");
            return false;
        }

        TMP_FontAsset generatedFontAsset = TMP_FontAsset.CreateFontAsset(
            sourceFont,
            SamplingPointSize,
            AtlasPadding,
            GlyphRenderMode.SDFAA,
            AtlasSize,
            AtlasSize,
            AtlasPopulationMode.Dynamic,
            true);

        if (generatedFontAsset == null)
        {
            Debug.LogError("TMP font asset creation failed.");
            return false;
        }

        generatedFontAsset.name = Path.GetFileNameWithoutExtension(OutputFontAssetPath);
        generatedFontAsset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
        generatedFontAsset.isMultiAtlasTexturesEnabled = true;
        TMP_FontAsset persistedFontAsset = SaveOrUpdateFontAsset(generatedFontAsset);

        string prewarmCharacters = CollectProjectCharacters();
        string missingCharacters = string.Empty;
        bool addedAllCharacters = string.IsNullOrEmpty(prewarmCharacters) ||
                                 persistedFontAsset.TryAddCharacters(prewarmCharacters, out missingCharacters);

        SyncSubAssets(persistedFontAsset);

        EditorUtility.SetDirty(persistedFontAsset);
        if (persistedFontAsset.material != null)
        {
            EditorUtility.SetDirty(persistedFontAsset.material);
        }

        foreach (Texture2D atlasTexture in persistedFontAsset.atlasTextures ?? Array.Empty<Texture2D>())
        {
            if (atlasTexture != null)
            {
                EditorUtility.SetDirty(atlasTexture);
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(OutputFontAssetPath, ImportAssetOptions.ForceUpdate);

        TMP_FontAsset savedFontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(OutputFontAssetPath);
        if (savedFontAsset == null)
        {
            Debug.LogError($"Generated TMP font asset was not found at {OutputFontAssetPath}.");
            return false;
        }

        savedFontAsset.ReadFontAssetDefinition();
        TMPro_EventManager.ON_FONT_PROPERTY_CHANGED(true, savedFontAsset);

        if (logToConsole)
        {
            int atlasCount = savedFontAsset.atlasTextures?.Count(texture => texture != null) ?? 0;
            int characterCount = savedFontAsset.characterTable?.Count ?? 0;
            string missingMessage = string.IsNullOrEmpty(missingCharacters) ? "none" : missingCharacters.Length.ToString();
            Debug.Log(
                $"Generated TMP font asset at {OutputFontAssetPath}. " +
                $"Characters baked: {characterCount}, Atlases: {atlasCount}, Missing characters: {missingMessage}, " +
                $"All requested baked: {addedAllCharacters}.");
        }

        return true;
    }

    private static TMP_FontAsset SaveOrUpdateFontAsset(TMP_FontAsset generatedFontAsset)
    {
        string outputDirectory = Path.GetDirectoryName(OutputFontAssetPath);
        if (!string.IsNullOrEmpty(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        TMP_FontAsset existingFontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(OutputFontAssetPath);
        if (existingFontAsset == null)
        {
            AssetDatabase.CreateAsset(generatedFontAsset, OutputFontAssetPath);
            SyncSubAssets(generatedFontAsset);
            return generatedFontAsset;
        }

        RemoveSubAssets(existingFontAsset);
        EditorUtility.CopySerialized(generatedFontAsset, existingFontAsset);
        Object.DestroyImmediate(generatedFontAsset);

        existingFontAsset.name = Path.GetFileNameWithoutExtension(OutputFontAssetPath);
        SyncSubAssets(existingFontAsset);
        return existingFontAsset;
    }

    private static void RemoveSubAssets(TMP_FontAsset fontAsset)
    {
        foreach (Object assetObject in AssetDatabase.LoadAllAssetsAtPath(OutputFontAssetPath))
        {
            if (assetObject == null || assetObject == fontAsset)
            {
                continue;
            }

            Object.DestroyImmediate(assetObject, true);
        }
    }

    private static void SyncSubAssets(TMP_FontAsset fontAsset)
    {
        if (fontAsset.atlasTextures != null)
        {
            for (int i = 0; i < fontAsset.atlasTextures.Length; i++)
            {
                Texture2D atlasTexture = fontAsset.atlasTextures[i];
                if (atlasTexture == null)
                {
                    continue;
                }

                atlasTexture.hideFlags = HideFlags.None;
                atlasTexture.name = i == 0
                    ? $"{fontAsset.name} Atlas"
                    : $"{fontAsset.name} Atlas {i}";

                if (AssetDatabase.GetAssetPath(atlasTexture) != OutputFontAssetPath)
                {
                    AssetDatabase.AddObjectToAsset(atlasTexture, fontAsset);
                }
            }
        }

        if (fontAsset.material != null)
        {
            fontAsset.material.hideFlags = HideFlags.None;
            fontAsset.material.name = $"{fontAsset.name} Material";

            if (AssetDatabase.GetAssetPath(fontAsset.material) != OutputFontAssetPath)
            {
                AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
            }
        }
    }

    private static string CollectProjectCharacters()
    {
        var characters = new HashSet<char>();

        for (int codePoint = 32; codePoint <= 126; codePoint++)
        {
            characters.Add((char)codePoint);
        }

        string absoluteRoot = Path.Combine(Directory.GetCurrentDirectory(), ProjectTextRoot);
        if (!Directory.Exists(absoluteRoot))
        {
            return new string(characters.OrderBy(character => character).ToArray());
        }

        foreach (string filePath in Directory.EnumerateFiles(absoluteRoot, "*", SearchOption.AllDirectories))
        {
            string extension = Path.GetExtension(filePath).ToLowerInvariant();
            if (!ShouldScan(extension))
            {
                continue;
            }

            string content;
            try
            {
                content = File.ReadAllText(filePath);
            }
            catch
            {
                continue;
            }

            foreach (char character in content)
            {
                if (character == '\n' || character == '\r' || character == '\t')
                {
                    continue;
                }

                if (IsSupportedCharacter(character))
                {
                    characters.Add(character);
                }
            }
        }

        return new string(characters.OrderBy(character => character).ToArray());
    }

    private static bool ShouldScan(string extension)
    {
        switch (extension)
        {
            case ".asset":
            case ".cs":
            case ".json":
            case ".md":
            case ".prefab":
            case ".txt":
            case ".unity":
            case ".uss":
            case ".uxml":
                return true;
            default:
                return false;
        }
    }

    private static bool IsSupportedCharacter(char character)
    {
        int codePoint = character;
        return codePoint is >= 32 and <= 126
            || codePoint is >= 0x3000 and <= 0x303F
            || codePoint is >= 0x4E00 and <= 0x9FFF
            || codePoint is >= 0xFF00 and <= 0xFFEF;
    }
}
