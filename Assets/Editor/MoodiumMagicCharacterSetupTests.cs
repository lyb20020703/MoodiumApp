using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class MoodiumMagicCharacterSetupTests
{
    [Test]
    public void MagicMoodiAssetsUseOpaquePearlMaterialAndKeepFaceMaterialsSeparate()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/Materials/MoodiMagic/Moodi_MagicPearl_Lit.mat");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/prefab/Moodi_Magic.prefab");
        var demo = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/MoodiMagicDemo.unity");

        Assert.That(material, Is.Not.Null);
        Assert.That(material.shader, Is.Not.Null);
        Assert.That(material.shader.name, Is.EqualTo("Moodium/Magic Pearl Lit"));
        Assert.That(material.GetFloat("_Metallic"), Is.EqualTo(0f));
        Assert.That(material.GetFloat("_Smoothness"), Is.EqualTo(0.65f));
        Assert.That(prefab, Is.Not.Null);
        Assert.That(demo, Is.Not.Null);

        var targets = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .Where(r => r.name.Contains("Body") || r.name.Contains("Flipper") ||
                        r.name.Contains("Sprout") || r.name.Contains("Tail"))
            .ToArray();
        Assert.That(targets, Has.Length.EqualTo(5));
        Assert.That(targets.All(r => r.sharedMaterials.All(m => m == material)), Is.True);
        Assert.That(prefab.transform.Find("MoodiMagicSparkles"), Is.Not.Null);
        Assert.That(prefab.transform.Find("MoodiMagicSparkles").GetComponent<ParticleSystem>(), Is.Not.Null);
        Assert.That(prefab.transform.Find("MoodiMagicSparkles").GetComponent<ParticleSystem>().emission.rateOverTime.constant,
            Is.GreaterThan(0f));

        var eye = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .First(r => r.name.StartsWith("Eye."));
        Assert.That(eye.sharedMaterials.All(m => m != material), Is.True);
    }
}
