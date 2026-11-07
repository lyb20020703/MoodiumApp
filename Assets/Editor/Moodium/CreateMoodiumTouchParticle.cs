#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Moodium.EditorTools
{
    public static class CreateMoodiumTouchParticle
    {
        const string PrefabPath = "Assets/Prefab/Effects/MoodiumTouchParticle.prefab";

        [MenuItem("Moodium/Build VisionOS Touch Particle")]
        public static void Build()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Materials/Particles/CandyFrost.mat");
            if (material == null)
            {
                Debug.LogError("[Moodium Touch VFX] CandyFrost URP particle material is missing.");
                return;
            }

            var root = new GameObject("MoodiumTouchParticle");
            var particles = root.AddComponent<ParticleSystem>();
            var renderer = root.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortMode = ParticleSystemSortMode.Distance;

            var main = particles.main;
            main.duration = 0.35f;
            main.loop = false;
            main.playOnAwake = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, 1.05f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.055f, 0.16f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.010f, 0.026f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 1f, 1f, 0.86f),
                new Color(0.92f, 0.78f, 1f, 0.72f));
            main.gravityModifier = new ParticleSystem.MinMaxCurve(-0.018f, 0.025f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            main.maxParticles = 40;

            var emission = particles.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 26, 32) });
            var shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.018f;
            shape.radiusThickness = 0.35f;
            var noise = particles.noise;
            noise.enabled = true;
            noise.quality = ParticleSystemNoiseQuality.Low;
            noise.strength = new ParticleSystem.MinMaxCurve(0.015f, 0.04f);
            noise.frequency = 0.35f;
            noise.scrollSpeed = 0.08f;
            var size = particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.25f), new Keyframe(0.12f, 1f),
                new Keyframe(0.72f, 0.72f), new Keyframe(1f, 0f)));

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssignManager(prefab);
            DisableLegacyPrefabParticleReferences();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Moodium Touch VFX] Built {PrefabPath}; Epic and native debug layers disabled.");
        }

        static void AssignManager(GameObject prefab)
        {
            var manager = Object.FindFirstObjectByType<Moodium.Interaction.MoodiumInteractionVFXManager>();
            if (manager == null) return;
            var serialized = new SerializedObject(manager);
            serialized.FindProperty("m_TouchBurstPrefab").objectReferenceValue = prefab;
            serialized.FindProperty("m_EpicVisionOSEffectPrefab").objectReferenceValue = null;
            serialized.FindProperty("m_EnableEpicEffect").boolValue = false;
            serialized.FindProperty("m_TouchBurstScale").floatValue = 1f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(manager);
            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
            EditorSceneManager.SaveScene(manager.gameObject.scene);
        }

        static void DisableLegacyPrefabParticleReferences()
        {
            foreach (var name in new[]
                     {
                         "ChocolatePrefab", "CookiePrefab", "HeartPrefab",
                         "StarPrefab", "Macaron", "Chocolate_Capsule"
                     })
            {
                var path = $"Assets/prefab/{name}.prefab";
                var root = PrefabUtility.LoadPrefabContents(path);
                foreach (var feedback in root.GetComponentsInChildren<Moodium.Interaction.TouchParticleFeedback>(true))
                {
                    var serialized = new SerializedObject(feedback);
                    serialized.FindProperty("m_ParticlePrefab").objectReferenceValue = null;
                    serialized.FindProperty("m_NativeBaselinePrefab").objectReferenceValue = null;
                    serialized.FindProperty("m_SpawnNativeBaseline").boolValue = false;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
#endif
