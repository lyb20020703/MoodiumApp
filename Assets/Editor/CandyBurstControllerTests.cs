using Moodium.CandyWorld;
using NUnit.Framework;

public sealed class CandyBurstControllerTests
{
    [TestCase(0,0,4,4)]
    [TestCase(30,0,4,2)]
    [TestCase(28,4,4,0)]
    [TestCase(40,0,4,0)]
    public void ContactBurstBudgetIncludesLiveAndNotYetSpawnedCandies(int live,int reserved,int requested,int expected)
    {
        var capacity=typeof(CandyBurstController).GetMethod("ContactBurstCapacity",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
        Assert.That(capacity.Invoke(null,new object[]{live,reserved,requested}),Is.EqualTo(expected));
    }

    [Test]
    public void ContactBurstRejectsFullSharedPoolAndDisabledEmitter()
    {
        var host=new UnityEngine.GameObject("Candy emitter");var source=new UnityEngine.GameObject("Contact prop");
        var prefab=new UnityEngine.GameObject("Star");
        try
        {
            var emitter=host.AddComponent<CandyBurstController>();emitter.Configure(new[]{prefab});
            var projectiles=(System.Collections.Generic.List<CandyProjectile>)typeof(CandyBurstController)
                .GetField("m_ActiveProjectiles",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(emitter);
            for(var i=0;i<32;i++)
            {
                var candy=new UnityEngine.GameObject("Existing candy");candy.transform.SetParent(source.transform);
                projectiles.Add(candy.AddComponent<CandyProjectile>());
            }
            Assert.That(emitter.BurstAtContact(UnityEngine.Vector3.one,source.transform),Is.False);
            projectiles.Clear();emitter.enabled=false;
            Assert.That(emitter.BurstAtContact(UnityEngine.Vector3.one,source.transform),Is.False);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(host);UnityEngine.Object.DestroyImmediate(source);UnityEngine.Object.DestroyImmediate(prefab);
        }
    }
    [Test]
    public void NatureCandyPoolDiagnostic_UsesCandyPoolOnlyForNatureWorldWhenEnabled()
    {
        Assert.That(CandyBurstController.ShouldUseNaturePrefabPool(true, true), Is.False);
        Assert.That(CandyBurstController.ShouldUseNaturePrefabPool(true, false), Is.True);
        Assert.That(CandyBurstController.ShouldUseNaturePrefabPool(false, true), Is.False);
    }

    [Test]
    public void NatureBurstWrapper_DisablesLegacyMaterialAndBoundsCompatibility()
    {
        Assert.That(CandyBurstController.ShouldApplyNatureBurstCompatibility(true, true, false), Is.False);
        Assert.That(CandyBurstController.ShouldApplyNatureBurstCompatibility(true, false, false), Is.True);
        Assert.That(CandyBurstController.ShouldApplyNatureBurstCompatibility(false, false, false), Is.False);
        Assert.That(CandyBurstController.ShouldApplyNatureBurstCompatibility(true, false, true), Is.False);
    }

    [TestCase("flower", NatureBurstDebugPrefabSelection.Flower, true)]
    [TestCase("Four-leaf clover", NatureBurstDebugPrefabSelection.Clover, true)]
    [TestCase("mushroom", NatureBurstDebugPrefabSelection.Mushroom, true)]
    [TestCase("mushroom", NatureBurstDebugPrefabSelection.Clover, false)]
    [TestCase("flower", NatureBurstDebugPrefabSelection.Random, true)]
    public void PureNatureBurstDebugSelection_OnlyKeepsTheRequestedPrefab(
        string prefabName, NatureBurstDebugPrefabSelection selection, bool expected)
    {
        Assert.That(CandyBurstController.MatchesNatureDebugPrefab(prefabName, selection), Is.EqualTo(expected));
    }

    [TestCase("flower", "Moodium/NatureBurst/Flower")]
    [TestCase("Four-leaf clover", "Moodium/NatureBurst/Clover")]
    [TestCase("mushroom", "Moodium/NatureBurst/Mushroom")]
    public void NatureBurstPrefab_ResolvesItsExplicitVisionOSSafeMaterial(string prefabName, string expectedPath)
    {
        Assert.That(CandyBurstController.GetNatureBurstMaterialResourcePath(prefabName), Is.EqualTo(expectedPath));
    }
}
