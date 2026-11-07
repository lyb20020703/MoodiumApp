using System;
using System.IO;
using Moodium.Interaction;
using UnityEditor;
using UnityEngine;

public static class MoodiumPrefabFeedbackSetup
{
    const string ChocolatePath = "Assets/prefab/ChocolatePrefab.prefab";
    const string CookiePath = "Assets/prefab/CookiePrefab.prefab";
    const string HeartPath = "Assets/prefab/HeartPrefab.prefab";
    const string StarPath = "Assets/prefab/StarPrefab.prefab";
    const string MacaronPath = "Assets/prefab/Macaron.prefab";

    const string StarParticlePath =
        "Assets/Epic Toon FX/Prefabs/Combat/Explosions (Misc)/StunExplosion.prefab";
    const string HeartParticlePath =
        "Assets/Epic Toon FX/Prefabs/Combat/Explosions/SparkleExplosion/SparkleExplosionPink.prefab";
    const string CookieParticlePath =
        "Assets/Epic Toon FX/Prefabs/Combat/Explosions (Misc)/SparkExplosion.prefab";

    [MenuItem("Moodium/Setup Prefab Touch Feedback")]
    public static void Setup()
    {
        var starParticle = RequirePrefab(StarParticlePath);
        var heartParticle = RequirePrefab(HeartParticlePath);
        var cookieParticle = RequirePrefab(CookieParticlePath);

        ConfigurePrefab(ChocolatePath, null);
        ConfigurePrefab(CookiePath, cookieParticle);
        ConfigurePrefab(HeartPath, heartParticle);
        ConfigurePrefab(StarPath, starParticle);
        ConfigurePrefab(MacaronPath, null);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log(
            "MOODIUM_PREFAB_FEEDBACK_READY=" +
            "Chocolate:none;Cookie:SparkExplosion;Heart:SparkleExplosionPink;" +
            "Star:StunExplosion;Macaron:none");
    }

    static void ConfigurePrefab(string prefabPath, GameObject particlePrefab)
    {
        var root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            var originalScale = root.transform.localScale;
            var spawnPoint = root.transform.Find("ParticleSpawnPoint");
            if (spawnPoint == null)
            {
                var spawnObject = new GameObject("ParticleSpawnPoint");
                spawnPoint = spawnObject.transform;
                spawnPoint.SetParent(root.transform, false);
            }

            spawnPoint.localPosition = CalculateLocalRendererCenter(root);
            spawnPoint.localRotation = Quaternion.identity;
            spawnPoint.localScale = Vector3.one;

            var softFeedback = root.GetComponent<SoftTouchFeedback>();
            if (softFeedback == null)
                softFeedback = root.AddComponent<SoftTouchFeedback>();

            var particleFeedback = root.GetComponent<TouchParticleFeedback>();
            if (particleFeedback == null)
                particleFeedback = root.AddComponent<TouchParticleFeedback>();
            particleFeedback.Configure(spawnPoint, particlePrefab);

            root.transform.localScale = originalScale;
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Debug.Log(
                $"[Moodium Prefab Feedback] {root.name}: center={spawnPoint.localPosition}, " +
                $"particle={(particlePrefab != null ? particlePrefab.name : "none")}, scale={originalScale}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static Vector3 CalculateLocalRendererCenter(GameObject root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return Vector3.zero;

        var bounds = renderers[0].bounds;
        for (var i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        return root.transform.InverseTransformPoint(bounds.center);
    }

    static GameObject RequirePrefab(string path)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null)
            throw new FileNotFoundException("Required particle prefab was not found.", path);
        return prefab;
    }
}
