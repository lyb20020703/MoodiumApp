using Moodium.Interaction;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class ChocolateCapsuleAudioBindingTests
{
    const string PrefabPath = "Assets/prefab/Chocolate_Capsule.prefab";
    const string CandySoftPrefabPath = "Assets/prefab/CandySoft.prefab";
    const string NatureSoftPrefabPath = "Assets/prefab/NatureSoft.prefab";
    const string SqueezeClipPath = "Assets/Audio Effect/Chocolate ice cream-1s.wav";

    [Test]
    public void ChocolateCapsuleUsesOneSecondSqueezeClip()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var expectedClip = AssetDatabase.LoadAssetAtPath<AudioClip>(SqueezeClipPath);

        Assert.That(prefab, Is.Not.Null);
        Assert.That(expectedClip, Is.Not.Null);
        Assert.That(prefab.GetComponent<MoodiumTouchAudio>(), Is.Not.Null);
        Assert.That(prefab.GetComponent<MoodiumTouchAudio>().TouchSound, Is.SameAs(expectedClip));
    }

    [TestCase(CandySoftPrefabPath)]
    [TestCase(NatureSoftPrefabPath)]
    public void RealitySoftPrefabsUseOneSecondSqueezeClip(string prefabPath)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        var expectedClip = AssetDatabase.LoadAssetAtPath<AudioClip>(SqueezeClipPath);

        Assert.That(prefab, Is.Not.Null);
        Assert.That(expectedClip, Is.Not.Null);
        Assert.That(prefab.GetComponent<MoodiumTouchAudio>(), Is.Not.Null);
        Assert.That(prefab.GetComponent<MoodiumTouchAudio>().TouchSound, Is.SameAs(expectedClip));
        Assert.That(prefab.GetComponent<AudioSource>(), Is.Not.Null);
    }
}
