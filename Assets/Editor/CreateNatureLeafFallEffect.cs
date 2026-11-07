#if UNITY_EDITOR
using Moodium.NatureWorld;
using Moodium.Flow;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Moodium.EditorTools
{
    /// <summary>Builds a lightweight URP leaf-fall particle prefab from the supplied IvyStudio leaf texture.</summary>
    public static class CreateNatureLeafFallEffect
    {
        const string PrefabPath = "Assets/prefab/Effects/NatureLeafFall.prefab";
        const string MaterialPath = "Assets/Materials/Particles/NatureLeafFall.mat";
        const string TexturePath = "Assets/ARTnGAME/IvyStudio/Materials/Textures/Leaf 3.png";

        [MenuItem("Moodium/Build Nature Leaf Fall Effect")]
        public static void Build()
        {
            EnsureFolder("Assets/prefab/Effects");
            EnsureFolder("Assets/Materials/Particles");
            ConfigureTexture();
            var material = BuildMaterial();

            var root = new GameObject("NatureLeafFall");
            var controller = root.AddComponent<NatureLeafParticleController>();
            var leaves = BuildLeaves(root.transform, material);
            controller.Configure(leaves);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssignToOpenScene();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        static void ConfigureTexture()
        {
            if (AssetImporter.GetAtPath(TexturePath) is not TextureImporter importer)
                return;
            importer.textureType = TextureImporterType.Default;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.SaveAndReimport();
        }

        static Material BuildMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
                throw new System.InvalidOperationException("URP Particles/Unlit shader is unavailable.");
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader) { name = "NatureLeafFall" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            material.shader = shader;
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = 3000;
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 1f);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
            EditorUtility.SetDirty(material);
            return material;
        }

        static ParticleSystem BuildLeaves(Transform parent, Material material)
        {
            var leafObject = new GameObject("Falling Leaves");
            leafObject.transform.SetParent(parent, false);
            var particles = leafObject.AddComponent<ParticleSystem>();
            var renderer = leafObject.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            // Mesh particles preserve 3D tilt and roll. Billboard leaves stay
            // visibly upright even when rotation-over-lifetime is enabled.
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            renderer.sortMode = ParticleSystemSortMode.Distance;

            var main = particles.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            main.maxParticles = 110;
            main.startLifetime = new ParticleSystem.MinMaxCurve(5.5f, 8.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(.014f, .035f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(.01f, .04f);
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(.43f, .65f, .22f, .85f), new Color(.85f, .48f, .14f, .88f));
            main.gravityModifier = .08f;

            var emission = particles.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            var shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            // New leaves arrive both in front of and behind the viewer.
            shape.scale = new Vector3(5.5f, .6f, 5.5f);
            var velocity = particles.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(-.22f, .22f);
            velocity.y = new ParticleSystem.MinMaxCurve(-.42f, -.22f);
            velocity.z = new ParticleSystem.MinMaxCurve(-.22f, .22f);
            var noise = particles.noise;
            noise.enabled = true;
            noise.strength = new ParticleSystem.MinMaxCurve(.12f, .28f);
            noise.frequency = .55f;
            noise.scrollSpeed = .22f;
            noise.damping = true;
            noise.quality = ParticleSystemNoiseQuality.Low;
            var rotation = particles.rotationOverLifetime;
            rotation.enabled = true;
            rotation.separateAxes = true;
            rotation.x = new ParticleSystem.MinMaxCurve(-3.4f, 3.4f);
            rotation.y = new ParticleSystem.MinMaxCurve(-4.2f, 4.2f);
            rotation.z = new ParticleSystem.MinMaxCurve(-3.4f, 3.4f);
            return particles;
        }

        static void AssignToOpenScene()
        {
            var app = Object.FindFirstObjectByType<MoodiumAppFlowController>();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (app == null || prefab == null)
                return;
            var serialized = new SerializedObject(app);
            var property = serialized.FindProperty("m_NatureLeafFallPrefab");
            if (property == null)
                return;
            property.objectReferenceValue = prefab;
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
