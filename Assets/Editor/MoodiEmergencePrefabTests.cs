using System.Linq;
using Moodium.Opening;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class MoodiEmergencePrefabTests
{
    const string PrefabPath = "Assets/prefab/MoodiEmergence.prefab";
    const string ConfigPath = "Assets/Resources/MoodiumOpening/OpeningVideoSequenceConfig.asset";

    [Test]
    public void RuntimeMoodiPrefab_AndItsAuthoredEmergenceClip_AreAssigned()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var config = AssetDatabase.LoadAssetAtPath<OpeningVideoSequenceConfig>(ConfigPath);

        Assert.That(prefab, Is.Not.Null, "The runtime Moodi prefab must be created from Moodi_V2.fbx.");
        Assert.That(prefab.GetComponentInChildren<Animator>(true), Is.Not.Null,
            "The runtime Moodi prefab must include the Animator that receives the authored clip.");
        Assert.That(config, Is.Not.Null);
        Assert.That(config.MoodiEmergencePrefab, Is.EqualTo(prefab));
        Assert.That(config.MoodiEmergenceClip, Is.Not.Null);
        Assert.That(config.MoodiEmergenceClip.name, Does.Contain("Moodi_Reference_Performance_InPlace"));
    }
}
