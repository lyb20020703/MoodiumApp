using System.Collections.Generic;
using System.IO;
using Moodium.Interaction;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class MoodiumEpicParticleVisionOSSetup
{
    const string OutputRoot = "Assets/Prefab/Effects/VisionOS";
    const string MaterialRoot = "Assets/Materials/Particles/EpicVisionOS";
    const string NativePrefabPath = "Assets/Prefab/Effects/Moodium_NativeParticleTest.prefab";

    const string StunSource = "Assets/Epic Toon FX/Prefabs/Combat/Explosions (Misc)/StunExplosion.prefab";
    const string SparkleSource = "Assets/Epic Toon FX/Prefabs/Combat/Explosions/SparkleExplosion/SparkleExplosionPink.prefab";
    const string SparkSource = "Assets/Epic Toon FX/Prefabs/Combat/Explosions (Misc)/SparkExplosion.prefab";

    static readonly Dictionary<Material, Material> MaterialCache = new();

    [MenuItem("Moodium/Build Epic Toon FX visionOS Variants")]
    public static void Build()
    {
        EnsureFolder(OutputRoot);
        EnsureFolder(MaterialRoot);

        var stun = BuildVariant(StunSource, OutputRoot + "/StunExplosion_VisionOS.prefab");
        var sparkle = BuildVariant(SparkleSource, OutputRoot + "/SparkleExplosionPink_VisionOS.prefab");
        var spark = BuildVariant(SparkSource, OutputRoot + "/SparkExplosion_VisionOS.prefab");
        var native = AssetDatabase.LoadAssetAtPath<GameObject>(NativePrefabPath);

        // PolySpatial ReplicateProperties still renders these Epic billboard systems as
        // rectangles on device. Keep the adapted assets for future testing, but use only
        // the confirmed-visible soft round particle for Creative Space interactions.
        Bind("Assets/prefab/ChocolatePrefab.prefab", null, native);
        Bind("Assets/prefab/CookiePrefab.prefab", null, native);
        Bind("Assets/prefab/HeartPrefab.prefab", null, native);
        Bind("Assets/prefab/StarPrefab.prefab", null, native);
        Bind("Assets/prefab/Macaron.prefab", null, native);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("MOODIUM_CREATIVE_PARTICLES_READY: all five prefabs use the small round URP particle only; Epic variants retained but unbound.");
    }

    static GameObject BuildVariant(string sourcePath, string outputPath)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
        if (source == null)
            throw new FileNotFoundException("Epic Toon FX source prefab was not found.", sourcePath);

        var instance = PrefabUtility.InstantiatePrefab(source) as GameObject;
        if (instance == null)
            throw new UnityException("Could not instantiate " + sourcePath);

        try
        {
            instance.name = Path.GetFileNameWithoutExtension(outputPath);
            foreach (var particles in instance.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = particles.main;
                main.cullingMode = ParticleSystemCullingMode.Automatic;
                main.startLifetime = ScaleCurve(main.startLifetime, 0.72f);

                var emission = particles.emission;
                if (emission.burstCount > 0)
                {
                    var bursts = new ParticleSystem.Burst[emission.burstCount];
                    emission.GetBursts(bursts);
                    var count = 0f;
                    foreach (var burst in bursts)
                        count += burst.maxCount;
                    emission.SetBursts(System.Array.Empty<ParticleSystem.Burst>());
                    emission.rateOverTime = Mathf.Max(emission.rateOverTime.constantMax, count / 0.16f);
                }

                var renderer = particles.GetComponent<ParticleSystemRenderer>();
                if (renderer == null)
                    continue;
                renderer.enabled = true;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                if (renderer.renderMode == ParticleSystemRenderMode.Stretch)
                    renderer.renderMode = ParticleSystemRenderMode.Billboard;

                var materials = renderer.sharedMaterials;
                for (var i = 0; i < materials.Length; i++)
                    materials[i] = GetVisionOSMaterial(materials[i]);
                renderer.sharedMaterials = materials;
            }

            return PrefabUtility.SaveAsPrefabAsset(instance, outputPath);
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    static Material GetVisionOSMaterial(Material source)
    {
        if (source == null)
            return null;
        if (MaterialCache.TryGetValue(source, out var cached))
            return cached;

        var sourcePath = AssetDatabase.GetAssetPath(source);
        var guid = string.IsNullOrEmpty(sourcePath) ? source.GetInstanceID().ToString() : AssetDatabase.AssetPathToGUID(sourcePath);
        var outputPath = $"{MaterialRoot}/{Sanitize(source.name)}_{guid.Substring(0, Mathf.Min(8, guid.Length))}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(outputPath);
        if (material == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
                throw new UnityException("URP Particles/Unlit shader was not found.");
            material = new Material(shader) { name = Path.GetFileNameWithoutExtension(outputPath) };
            AssetDatabase.CreateAsset(material, outputPath);
        }

        var texture = source.HasProperty("_MainTex") ? source.GetTexture("_MainTex") : source.mainTexture;
        var color = source.HasProperty("_TintColor") ? source.GetColor("_TintColor") :
            source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white;
        if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
        if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);

        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", source.name.Contains("ADD") ? 2f : 0f);
        material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", source.name.Contains("ADD") ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite", 0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(material);
        MaterialCache[source] = material;
        return material;
    }

    static void Bind(string prefabPath, GameObject effect, GameObject native)
    {
        var root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            var feedback = root.GetComponent<TouchParticleFeedback>();
            if (feedback == null)
                feedback = root.AddComponent<TouchParticleFeedback>();
            var spawnPoint = root.transform.Find("ParticleSpawnPoint");
            feedback.Configure(spawnPoint, effect);
            feedback.ConfigureVisionOS(native, true, 0.35f);
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static ParticleSystem.MinMaxCurve ScaleCurve(ParticleSystem.MinMaxCurve source, float multiplier)
    {
        return source.mode switch
        {
            ParticleSystemCurveMode.Constant => new ParticleSystem.MinMaxCurve(source.constant * multiplier),
            ParticleSystemCurveMode.TwoConstants => new ParticleSystem.MinMaxCurve(source.constantMin * multiplier, source.constantMax * multiplier),
            ParticleSystemCurveMode.Curve => new ParticleSystem.MinMaxCurve(source.curveMultiplier * multiplier, source.curve),
            ParticleSystemCurveMode.TwoCurves => new ParticleSystem.MinMaxCurve(source.curveMultiplier * multiplier, source.curveMin, source.curveMax),
            _ => source
        };
    }

    static string Sanitize(string value)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
            value = value.Replace(invalid, '_');
        return value.Replace('/', '_');
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
