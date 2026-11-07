#if UNITY_EDITOR
using Moodium.CandyWorld;
using Moodium.Flow;
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Moodium.EditorTools
{
    public static class CreateCandyFrostEffect
    {
        const string PrefabPath = "Assets/Prefab/Effects/CandyFrost.prefab";
        const string MaterialPath = "Assets/Materials/Particles/CandyFrost.mat";
        const string CircleTexturePath = "Assets/Moodium/Textures/MoodiumParticleCircle.png";
        const string CircleMaterialPath = "Assets/Moodium/Materials/MoodiumParticleCircle.mat";

        [DidReloadScripts]
        static void EnsureUnifiedParticleMaterialAfterReload()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                    return;
                BuildAndAssignUnifiedCircleMaterial();
            };
        }

        [MenuItem("Moodium/Rebuild Unified Round Particle Material")]
        public static void BuildAndAssignUnifiedCircleMaterial()
        {
            EnsureFolder("Assets/Moodium/Textures");
            EnsureFolder("Assets/Moodium/Materials");
            BuildCirclePng();

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(CircleTexturePath);
            if (texture == null)
            {
                Debug.LogError($"[Moodium Particle Material] Could not import {CircleTexturePath}.");
                return;
            }

            var source = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
            {
                Debug.LogError("[Moodium Particle Material] URP Particles/Unlit shader is unavailable.");
                return;
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(CircleMaterialPath);
            if (material == null)
            {
                material = source != null ? new Material(source) : new Material(shader);
                material.name = "MoodiumParticleCircle";
                AssetDatabase.CreateAsset(material, CircleMaterialPath);
            }
            material.shader = shader;
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = 3000;
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
            if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 1f);
            if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
            EditorUtility.SetDirty(material);

            foreach (var path in new[]
                     {
                         "Assets/Prefab/Effects/CandyFrost.prefab",
                         "Assets/Prefab/Effects/MoodiumInteractionParticle.prefab",
                         "Assets/Prefab/Effects/MoodiumTouchParticle.prefab"
                     })
                AssignMaterialToPrefab(path, material);

            AssetDatabase.SaveAssets();
            Debug.Log(
                $"[Moodium Particle Material] Unified round RGBA texture and URP material assigned: " +
                $"{CircleTexturePath}, {CircleMaterialPath}");
        }

        static void BuildCirclePng()
        {
            const int size = 256;
            if (!File.Exists(CircleTexturePath))
            {
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
                var pixels = new Color32[size * size];
                for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var nx = (x + 0.5f) / size * 2f - 1f;
                    var ny = (y + 0.5f) / size * 2f - 1f;
                    var distance = Mathf.Sqrt(nx * nx + ny * ny);
                    var alpha = Mathf.Clamp01(1f - distance);
                    alpha = Mathf.SmoothStep(0f, 1f, alpha);
                    alpha = Mathf.Pow(alpha, 0.72f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
                texture.SetPixels32(pixels);
                texture.Apply(false, false);
                File.WriteAllBytes(CircleTexturePath, texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
            }

            AssetDatabase.ImportAsset(CircleTexturePath, ImportAssetOptions.ForceUpdate);
            if (AssetImporter.GetAtPath(CircleTexturePath) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Default;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = true;
                importer.sRGBTexture = true;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
        }

        static void AssignMaterialToPrefab(string path, Material material)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                return;
            var root = PrefabUtility.LoadPrefabContents(path);
            foreach (var renderer in root.GetComponentsInChildren<ParticleSystemRenderer>(true))
                renderer.sharedMaterial = material;
            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
        }

        [MenuItem("Moodium/Build Candy Frost Effect")]
        public static void Build()
        {
            EnsureFolder("Assets/Prefab/Effects");
            EnsureFolder("Assets/Materials/Particles");

            var material = BuildMaterial();
            var root = new GameObject("CandyFrost");
            var controller = root.AddComponent<CandyFrostParticleController>();
            var dust = BuildParticles(root.transform, "Sugar Dust", material, false);
            var sparkles = BuildParticles(root.transform, "Sugar Sparkles", material, true);
            controller.Configure(dust, sparkles);

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssignToScene(prefab);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Candy Frost Builder] Created {PrefabPath} using {material.shader.name}.");
        }

        static Material BuildMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                         ?? Shader.Find("Universal Render Pipeline/Unlit")
                         ?? Shader.Find("Unlit/Transparent");
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            material.shader = shader;
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/Materials/Particles/Moodium_Particle_Circle.asset");
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 1f);
            material.renderQueue = 3000;
            EditorUtility.SetDirty(material);
            return material;
        }

        static ParticleSystem BuildParticles(Transform parent, string name, Material material, bool sparkle)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            child.transform.localPosition = new Vector3(0f, 1.05f, 0f);
            var particles = child.AddComponent<ParticleSystem>();
            var renderer = child.GetComponent<ParticleSystemRenderer>();
            renderer.material = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortMode = ParticleSystemSortMode.Distance;

            var main = particles.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            main.maxParticles = sparkle ? 48 : 180;
            main.startLifetime = sparkle
                ? new ParticleSystem.MinMaxCurve(1.8f, 3.2f)
                : new ParticleSystem.MinMaxCurve(4.2f, 6.8f);
            main.startSize = sparkle
                ? new ParticleSystem.MinMaxCurve(0.004f, 0.010f)
                : new ParticleSystem.MinMaxCurve(0.006f, 0.016f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.015f, 0.055f);
            main.startColor = sparkle
                ? new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 1f, 0.7f), new Color(0.78f, 0.68f, 1f, 0.8f))
                : new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 1f, 0.42f), new Color(0.96f, 0.76f, 1f, 0.58f));

            var emission = particles.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            var shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = sparkle ? new Vector3(1.25f, 0.55f, 1f) : new Vector3(1.55f, 0.22f, 1.15f);
            var velocity = particles.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(-0.025f, 0.025f);
            velocity.y = sparkle
                ? new ParticleSystem.MinMaxCurve(-0.025f, 0.035f)
                : new ParticleSystem.MinMaxCurve(-0.095f, -0.025f);
            velocity.z = new ParticleSystem.MinMaxCurve(-0.02f, 0.02f);
            var noise = particles.noise;
            noise.enabled = true;
            noise.strength = new ParticleSystem.MinMaxCurve(0.025f, 0.075f);
            noise.frequency = 0.28f;
            noise.scrollSpeed = 0.08f;
            noise.damping = true;
            noise.quality = ParticleSystemNoiseQuality.Low;
            var size = particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0f), new Keyframe(0.12f, 1f),
                new Keyframe(0.8f, 0.75f), new Keyframe(1f, 0f)));
            return particles;
        }

        static void AssignToScene(GameObject prefab)
        {
            var app = Object.FindFirstObjectByType<MoodiumAppFlowController>();
            if (app == null) return;
            var serialized = new SerializedObject(app);
            serialized.FindProperty("m_CandyFrostPrefab").objectReferenceValue = prefab;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(app);
            EditorSceneManager.MarkSceneDirty(app.gameObject.scene);
            EditorSceneManager.SaveScene(app.gameObject.scene);
        }

        static void EnsureFolder(string path)
        {
            var parts = path.Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
#endif
