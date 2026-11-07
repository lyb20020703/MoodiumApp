using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

[InitializeOnLoad]
public static class MoodiumOpeningMaterialSetup
{
    const string Root = "Assets/Model/Moodium_Opening";
    const string MaterialDir = Root + "/Materials";
    const string StampPath = "Library/MoodiumOpeningMaterialsApplied_v2.stamp";

    static MoodiumOpeningMaterialSetup()
    {
        EditorApplication.delayCall += AutoRun;
    }

    static void AutoRun()
    {
        if (!File.Exists(StampPath) || !AssetDatabase.IsValidFolder(MaterialDir))
            Rebuild();
    }

    [MenuItem("Tools/Moodium/Rebuild Opening Materials")]
    public static void Rebuild()
    {
        try
        {
            EnsureFolder(MaterialDir);
            ConfigureTextureImporters();

            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null) throw new InvalidOperationException("URP/Lit shader was not found.");

            Material candy = CreateOrLoad("Candy_DreamGlass", lit);
            ConfigureCandy(candy);

            Material sprite = CreateOrLoad("Sprite_Emission", lit);
            ConfigureOpaqueEmission(sprite,
                Root + "/Textures/Sprite/BaseColor.png",
                Root + "/Textures/Sprite/Emission.png",
                new Color(1.0f, 0.82f, 1.0f, 1.0f), 1.7f, 0.48f);

            Material logo = CreateOrLoad("Moodium_Logo_Emission", lit);
            ConfigureOpaqueEmission(logo,
                Root + "/Textures/Logo/BaseColor.png",
                Root + "/Textures/Logo/Emission.png",
                Color.white, 1.35f, 0.58f);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            Remap(Root + "/FBX/Candy_Animation.fbx", "Candy_Wrapper_Iridescent", candy);
            Remap(Root + "/FBX/Sprite_Animation.fbx", "Sprite_Baked_Source", sprite);
            Remap(Root + "/FBX/Moodium_Logo.fbx", "Moodium_Purple_Pink_Gradient", logo);
            RemapAll(Root + "/FBX/Moodium_Opening_All.fbx", candy, sprite, logo);

            File.WriteAllText(StampPath, DateTime.UtcNow.ToString("O"));
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("[Moodium] Opening materials created and remapped to all FBX assets.");
        }
        catch (Exception e)
        {
            Debug.LogError("[Moodium] Material setup failed: " + e);
        }
    }

    static void EnsureFolder(string path)
    {
        string current = "Assets";
        foreach (string segment in path.Substring("Assets/".Length).Split('/'))
        {
            string next = current + "/" + segment;
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, segment);
            current = next;
        }
    }

    static Material CreateOrLoad(string name, Shader shader)
    {
        string path = MaterialDir + "/" + name + ".mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(mat, path);
        }
        else mat.shader = shader;
        return mat;
    }

    static Texture2D T(string path) => AssetDatabase.LoadAssetAtPath<Texture2D>(path);

    static void ConfigureCandy(Material mat)
    {
        Texture2D baseMap = T(Root + "/Textures/Candy/BaseColor.png");
        Texture2D normal = T(Root + "/Textures/Candy/Normal.png");
        Texture2D emission = T(Root + "/Textures/Candy/Emission.png");

        mat.SetTexture("_BaseMap", baseMap);
        mat.SetTexture("_MainTex", baseMap);
        mat.SetTexture("_BumpMap", normal);
        mat.SetTexture("_EmissionMap", emission);
        mat.SetColor("_BaseColor", new Color(1f, 0.78f, 1f, 0.72f));
        mat.SetColor("_Color", new Color(1f, 0.78f, 1f, 0.72f));
        mat.SetColor("_EmissionColor", new Color(1.45f, 0.42f, 1.25f, 1f));
        mat.SetFloat("_Metallic", 0.03f);
        mat.SetFloat("_Smoothness", 0.88f);
        mat.SetFloat("_BumpScale", 0.35f);
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.SetFloat("_Cull", (float)CullMode.Off);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.renderQueue = (int)RenderQueue.Transparent;
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.EnableKeyword("_NORMALMAP");
        mat.EnableKeyword("_EMISSION");
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.doubleSidedGI = true;
        EditorUtility.SetDirty(mat);
    }

    static void ConfigureOpaqueEmission(Material mat, string basePath, string emissionPath,
        Color tint, float emissionStrength, float smoothness)
    {
        Texture2D baseMap = T(basePath);
        Texture2D emission = T(emissionPath);
        mat.SetTexture("_BaseMap", baseMap);
        mat.SetTexture("_MainTex", baseMap);
        mat.SetTexture("_EmissionMap", emission);
        mat.SetColor("_BaseColor", tint);
        mat.SetColor("_Color", tint);
        mat.SetColor("_EmissionColor", tint * emissionStrength);
        mat.SetFloat("_Metallic", 0f);
        mat.SetFloat("_Smoothness", smoothness);
        mat.SetFloat("_Surface", 0f);
        mat.SetFloat("_SrcBlend", (float)BlendMode.One);
        mat.SetFloat("_DstBlend", (float)BlendMode.Zero);
        mat.SetFloat("_ZWrite", 1f);
        mat.SetFloat("_Cull", (float)CullMode.Back);
        mat.SetOverrideTag("RenderType", "Opaque");
        mat.renderQueue = -1;
        mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.EnableKeyword("_EMISSION");
        mat.doubleSidedGI = false;
        EditorUtility.SetDirty(mat);
    }

    static void ConfigureTextureImporters()
    {
        ConfigureTexture(Root + "/Textures/Candy/BaseColor.png", true, false, true);
        ConfigureTexture(Root + "/Textures/Candy/Normal.png", false, true, false);
        ConfigureTexture(Root + "/Textures/Candy/Metallic.png", false, false, false);
        ConfigureTexture(Root + "/Textures/Candy/Roughness.png", false, false, false);
        ConfigureTexture(Root + "/Textures/Candy/Emission.png", true, false, false);
        ConfigureTexture(Root + "/Textures/Sprite/BaseColor.png", true, false, false);
        ConfigureTexture(Root + "/Textures/Sprite/Emission.png", true, false, false);
        ConfigureTexture(Root + "/Textures/Logo/BaseColor.png", true, false, false);
        ConfigureTexture(Root + "/Textures/Logo/Emission.png", true, false, false);
    }

    static void ConfigureTexture(string path, bool sRGB, bool normal, bool alphaTransparency)
    {
        TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) return;
        ti.sRGBTexture = sRGB;
        ti.alphaIsTransparency = alphaTransparency;
        ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        ti.mipmapEnabled = true;
        ti.textureCompression = TextureImporterCompression.CompressedHQ;
        ti.SaveAndReimport();
    }

    static void Remap(string fbxPath, string sourceMaterialName, Material target)
    {
        ModelImporter importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        if (importer == null) throw new InvalidOperationException("Missing ModelImporter: " + fbxPath);
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        importer.materialLocation = ModelImporterMaterialLocation.External;
        importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), sourceMaterialName), target);
        importer.SaveAndReimport();
    }

    static void RemapAll(string fbxPath, Material candy, Material sprite, Material logo)
    {
        ModelImporter importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        if (importer == null) throw new InvalidOperationException("Missing ModelImporter: " + fbxPath);
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        importer.materialLocation = ModelImporterMaterialLocation.External;
        importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "Candy_Wrapper_Iridescent"), candy);
        importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "Sprite_Baked_Source"), sprite);
        importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "Moodium_Purple_Pink_Gradient"), logo);
        importer.SaveAndReimport();
    }
}
