using System.IO;
using Moodium.Interaction;
using UnityEditor;
using UnityEngine;

public static class MoodiumParticleDiagnosticSetup
{
    const string MaterialFolder = "Assets/Materials/Particles";
    const string PrefabFolder = "Assets/Prefab/Effects";
    const string DebugMaterialPath = MaterialFolder + "/Moodium_Particle_URP_Test.mat";
    const string CircleTexturePath = MaterialFolder + "/Moodium_Particle_Circle.asset";
    const string NativeMaterialPath = MaterialFolder + "/Moodium_NativeParticle_White.mat";
    const string NativePrefabPath = PrefabFolder + "/Moodium_NativeParticleTest.prefab";

    static readonly string[] InteractivePrefabs =
    {
        "Assets/prefab/ChocolatePrefab.prefab",
        "Assets/prefab/CookiePrefab.prefab",
        "Assets/prefab/HeartPrefab.prefab",
        "Assets/prefab/StarPrefab.prefab",
        "Assets/prefab/Macaron.prefab"
    };

    [MenuItem("Moodium/Setup visionOS Particle Diagnostics")]
    public static void Setup()
    {
        EnsureFolder(MaterialFolder);
        EnsureFolder(PrefabFolder);
        var circle = CreateCircleTexture();
        var nativeMaterial = CreateParticleMaterial(NativeMaterialPath, circle, Color.white);
        var nativePrefab = CreateNativeParticlePrefab(nativeMaterial);

        foreach (var prefabPath in InteractivePrefabs)
            ConfigurePrefab(prefabPath, nativePrefab);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log(
            "MOODIUM_PARTICLE_DIAGNOSTICS_READY: small URP circle baseline enabled; debug marker disabled.");
    }

    static Material CreateParticleMaterial(string path, Texture texture, Color color)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            var template = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Samples/PolySpatial/BalloonGallery/Materials/PopParticle.mat");
            if (template == null)
                throw new FileNotFoundException("PolySpatial URP particle material template was not found.");
            material = new Material(template) { name = Path.GetFileNameWithoutExtension(path) };
            AssetDatabase.CreateAsset(material, path);
        }
        if (material.shader == null || material.shader.name != "Universal Render Pipeline/Particles/Unlit")
            material.shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (material.HasProperty("_BaseMap"))
            material.SetTexture("_BaseMap", texture);
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);
        material.renderQueue = 3000;
        EditorUtility.SetDirty(material);
        return material;
    }

    static Texture2D CreateCircleTexture()
    {
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(CircleTexturePath);
        if (texture != null)
            return texture;
        const int size = 64;
        texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "Moodium Particle Circle",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        var pixels = new Color[size * size];
        var center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var distance = Vector2.Distance(new Vector2(x, y), center) / (size * 0.5f);
            var alpha = 1f - Mathf.SmoothStep(0.72f, 1f, distance);
            pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
        }
        texture.SetPixels(pixels);
        texture.Apply();
        AssetDatabase.CreateAsset(texture, CircleTexturePath);
        return texture;
    }

    static GameObject CreateNativeParticlePrefab(Material material)
    {
        var root = new GameObject("Moodium_NativeParticleTest");
        try
        {
            var particles = root.AddComponent<ParticleSystem>();
            var main = particles.main;
            main.duration = 0.1f;
            main.loop = false;
            main.playOnAwake = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.22f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.0035f, 0.008f);
            main.startColor = Color.white;
            main.maxParticles = 8;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.cullingMode = ParticleSystemCullingMode.Automatic;

            var emission = particles.emission;
            emission.enabled = true;
            emission.rateOverTime = 55f;
            emission.SetBursts(System.Array.Empty<ParticleSystem.Burst>());

            var shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.007f;

            var renderer = root.GetComponent<ParticleSystemRenderer>();
            renderer.enabled = true;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = material;
            return PrefabUtility.SaveAsPrefabAsset(root, NativePrefabPath);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    static void ConfigurePrefab(string prefabPath, GameObject nativePrefab)
    {
        var root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            var feedback = root.GetComponent<TouchParticleFeedback>();
            if (feedback == null)
                feedback = root.AddComponent<TouchParticleFeedback>();
            feedback.ConfigureVisionOS(nativePrefab, true, 0.35f);
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static void EnsureFolder(string path)
    {
        var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        var name = Path.GetFileName(path);
        if (!AssetDatabase.IsValidFolder(path) && !string.IsNullOrEmpty(parent))
        {
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
