using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class MoodiumMagicCharacterSetup
{
    const string SourcePrefabPath = "Assets/prefab/Moodi.prefab";
    const string MagicPrefabPath = "Assets/prefab/Moodi_Magic.prefab";
    const string MaterialFolder = "Assets/Materials/MoodiMagic";
    const string MaterialPath = MaterialFolder + "/Moodi_MagicPearl_Lit.mat";
    const string ParticleMaterialPath = MaterialFolder + "/Moodi_MagicSparkle.mat";
    const string DemoScenePath = "Assets/Scenes/MoodiMagicDemo.unity";

    [MenuItem("Moodium/Build Moodi Magic Pearl Demo")]
    public static void Build()
    {
        EnsureFolder("Assets/Scenes");
        EnsureFolder(MaterialFolder);
        var magicMaterial = CreateMagicMaterial();
        var sparkleMaterial = CreateSparkleMaterial();
        var magicPrefab = CreateMagicPrefab(magicMaterial, sparkleMaterial);
        CreateDemoScene(magicPrefab);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Moodium Magic] Moodi_Magic.prefab and MoodiMagicDemo.unity built.");
    }

    static Material CreateMagicMaterial()
    {
        var shader = Shader.Find("Moodium/Magic Pearl Lit");
        if (shader == null) throw new MissingReferenceException("Moodium/Magic Pearl Lit shader is missing.");
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            material = new Material(shader) { name = "Moodi MagicPearl Lit" };
            AssetDatabase.CreateAsset(material, MaterialPath);
        }
        material.shader = shader;
        material.SetColor("_BaseColor", Hex("B990EF"));
        material.SetColor("_DarkColor", Hex("7650B0"));
        material.SetColor("_WideEdgeColor", Hex("FF9DE2"));
        material.SetColor("_NarrowEdgeColor", Hex("A6DFFF"));
        material.SetFloat("_Metallic", 0f);
        material.SetFloat("_Smoothness", 0.65f);
        material.SetFloat("_WideEdgeStrength", 0.28f);
        material.SetFloat("_NarrowEdgeStrength", 0.55f);
        material.SetFloat("_FresnelWidePower", 2f);
        material.SetFloat("_FresnelNarrowPower", 6f);
        material.SetFloat("_CenterEmission", 0.035f);
        material.SetFloat("_BreathAmplitude", 0.05f);
        material.SetFloat("_BreathSpeed", 0.24f);
        material.SetFloat("_SparkleDensity", 0.045f);
        material.SetFloat("_SparkleIntensity", 0.7f);
        material.SetFloat("_SparkleScale", 17f);
        material.SetFloat("_SparkleSpeed", 0.18f);
        return material;
    }

    static Material CreateSparkleMaterial()
    {
        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) throw new MissingReferenceException("URP particle shader is missing.");
        var material = AssetDatabase.LoadAssetAtPath<Material>(ParticleMaterialPath);
        if (material == null)
        {
            material = new Material(shader) { name = "Moodi Magic Sparkle" };
            AssetDatabase.CreateAsset(material, ParticleMaterialPath);
        }
        material.shader = shader;
        material.color = new Color(0.86f, 0.55f, 1f, 0.9f);
        return material;
    }

    static GameObject CreateMagicPrefab(Material magicMaterial, Material sparkleMaterial)
    {
        AssetDatabase.DeleteAsset(MagicPrefabPath);
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefabPath);
        if (source == null) throw new MissingReferenceException("Moodi.prefab is missing.");
        var root = PrefabUtility.InstantiatePrefab(source) as GameObject;
        root.name = "Moodi_Magic";

        foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var n = renderer.name;
            if (n.Contains("Body") || n.Contains("Flipper") || n.Contains("Sprout") || n.Contains("Tail"))
                renderer.sharedMaterials = renderer.sharedMaterials.Select(_ => magicMaterial).ToArray();
        }

        var sparkleRoot = new GameObject("MoodiMagicSparkles");
        sparkleRoot.transform.SetParent(root.transform, false);
        var particles = sparkleRoot.AddComponent<ParticleSystem>();
        var main = particles.main;
        main.playOnAwake = true;
        main.loop = true;
        main.duration = 4f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 3.2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.005f, 0.02f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.006f, 0.014f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(1f, 0.62f, 0.9f, 0.75f), new Color(0.58f, 0.82f, 1f, 0.75f));
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        var emission = particles.emission;
        emission.rateOverTime = 3.5f;
        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.62f;
        var color = particles.colorOverLifetime;
        color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.7f, 0.45f, 1f), 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.7f, 0.2f), new GradientAlphaKey(0f, 1f) });
        color.color = gradient;
        var rendererModule = sparkleRoot.GetComponent<ParticleSystemRenderer>();
        rendererModule.material = sparkleMaterial;
        rendererModule.renderMode = ParticleSystemRenderMode.Billboard;
        rendererModule.shadowCastingMode = ShadowCastingMode.Off;
        rendererModule.receiveShadows = false;
        particles.Play(true);

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, MagicPrefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    static void CreateDemoScene(GameObject magicPrefab)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.055f, 0.025f, 0.11f);
        RenderSettings.fog = false;

        var cameraObject = new GameObject("Moodi Magic Camera");
        var camera = cameraObject.AddComponent<Camera>();
        camera.transform.position = new Vector3(0f, 0.1f, -6.5f);
        camera.transform.LookAt(new Vector3(0f, 0.1f, 0f));
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.025f, 0.008f, 0.06f);

        var keyObject = new GameObject("Moodi Magic Key Light");
        var key = keyObject.AddComponent<Light>();
        key.type = LightType.Point;
        key.color = new Color(0.86f, 0.67f, 1f);
        key.intensity = 4f;
        key.range = 8f;
        key.transform.position = new Vector3(-2.4f, 2.4f, -3.2f);

        var fillObject = new GameObject("Moodi Magic Fill Light");
        var fill = fillObject.AddComponent<Light>();
        fill.type = LightType.Point;
        fill.color = new Color(0.45f, 0.75f, 1f);
        fill.intensity = 2.2f;
        fill.range = 7f;
        fill.transform.position = new Vector3(2.5f, 0.4f, -1.5f);

        var volumeObject = new GameObject("Moodi Magic Bloom");
        var volume = volumeObject.AddComponent<Volume>();
        volume.isGlobal = true;
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        var bloom = profile.Add<Bloom>(true);
        bloom.threshold.value = 1f;
        bloom.intensity.value = 0.3f;
        bloom.scatter.value = 0.65f;
        volume.profile = profile;

        var moodi = PrefabUtility.InstantiatePrefab(magicPrefab) as GameObject;
        moodi.name = "Moodi_Magic_Demo";
        moodi.transform.position = Vector3.zero;
        moodi.transform.localScale = Vector3.one * 0.42f;

        EditorSceneManager.SaveScene(scene, DemoScenePath);
    }

    static Color Hex(string value)
    {
        ColorUtility.TryParseHtmlString("#" + value, out var color);
        return color;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        var name = Path.GetFileName(path);
        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, name);
    }
}
