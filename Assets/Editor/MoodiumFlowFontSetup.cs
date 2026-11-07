using System.IO;
using Moodium.Flow;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

public static class MoodiumFlowFontSetup
{
    const string ScenePath = "Assets/Samples/PolySpatial/Scenes/Meshing 1.unity";
    const string SourceFontPath = "Assets/Samples/PolySpatial/Shared/Fonts/Inter/Inter-Regular.ttf";
    const string FontFolder = "Assets/UI/Moodium/Fonts";
    const string FontAssetPath = FontFolder + "/Inter-Regular SDF.asset";

    [MenuItem("Moodium/Setup Flow Font")]
    public static void Setup()
    {
        if (!EnsureTmpEssentialResources())
        {
            EditorApplication.delayCall += Setup;
            return;
        }
        EnsureFolder("Assets/UI");
        EnsureFolder("Assets/UI/Moodium");
        EnsureFolder(FontFolder);

        var fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
        if (fontAsset == null)
            fontAsset = CreateFontAsset();

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var flow = Object.FindFirstObjectByType<MoodiumAppFlowController>();
        if (flow == null)
            throw new System.InvalidOperationException("MoodiumAppFlowController was not found in Meshing 1.");

        var serializedFlow = new SerializedObject(flow);
        serializedFlow.FindProperty("m_FontAsset").objectReferenceValue = fontAsset;
        serializedFlow.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(flow);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log($"MOODIUM_FLOW_FONT_READY={FontAssetPath}; shader={fontAsset.material.shader.name}");
    }

    static TMP_FontAsset CreateFontAsset()
    {
        var sourceFont = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);
        if (sourceFont == null)
            throw new FileNotFoundException("Inter source font was not found.", SourceFontPath);

        var fontAsset = TMP_FontAsset.CreateFontAsset(
            sourceFont,
            90,
            9,
            GlyphRenderMode.SDFAA,
            1024,
            1024,
            AtlasPopulationMode.Dynamic,
            true);
        if (fontAsset == null)
            throw new System.InvalidOperationException("Unable to create the Inter TMP font asset.");

        fontAsset.name = "Inter-Regular SDF";
        var mobileShader = Shader.Find("TextMeshPro/Mobile/Distance Field");
        if (mobileShader != null)
            fontAsset.material.shader = mobileShader;

        fontAsset.TryAddCharacters(
            "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 -_/.,:()",
            out var missingCharacters);
        if (!string.IsNullOrEmpty(missingCharacters))
            Debug.LogWarning($"Moodium font missing characters: {missingCharacters}");

        AssetDatabase.CreateAsset(fontAsset, FontAssetPath);
        foreach (var texture in fontAsset.atlasTextures)
        {
            if (texture != null && !AssetDatabase.Contains(texture))
                AssetDatabase.AddObjectToAsset(texture, fontAsset);
        }

        if (fontAsset.material != null && !AssetDatabase.Contains(fontAsset.material))
            AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);

        EditorUtility.SetDirty(fontAsset);
        AssetDatabase.SaveAssets();
        return fontAsset;
    }

    static bool EnsureTmpEssentialResources()
    {
        if (AssetDatabase.FindAssets("t:TMP_Settings").Length > 0)
            return true;

        var packagePath = Path.Combine(
            EditorApplication.applicationContentsPath,
            "Resources/PackageManager/BuiltInPackages/com.unity.ugui/Package Resources/TMP Essential Resources.unitypackage");
        if (!File.Exists(packagePath))
            throw new FileNotFoundException("TMP Essential Resources package was not found.", packagePath);

        AssetDatabase.ImportPackage(packagePath, false);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        return false;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;

        var slash = path.LastIndexOf('/');
        AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
    }
}
