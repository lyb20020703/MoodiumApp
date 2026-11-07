using Moodium.NatureWorld;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class BubbleSplatMaterialTests
{
    [Test]
    public void ResourceContainsShaderAndCompleteParameterContract()
    {
        var material = Resources.Load<Material>(BubbleSplatMaterial.ResourcePath);
        Assert.That(material, Is.Not.Null);
        Assert.That(material.shader.name, Is.EqualTo(BubbleSplatMaterial.ShaderName));
        Assert.That(ShaderUtil.ShaderHasError(material.shader), Is.False);
        foreach (var property in new[] { "_BaseMap", "_BaseColor", "_Up", "_WindDir", "_EyeHeight",
                     "_WindStrength", "_Style", "_SelfAmount", "_HandA", "_HandB", "_HandAmount",
                     "_Eye", "_Boost", "_DebugTint", "_FloatStrength", "_InnerCos", "_OuterCos" })
            Assert.That(material.HasProperty(property), Is.True, property);
    }

    [Test]
    public void NestedWorldFadeDoesNotMutateOuterWorldOrResource()
    {
        var source = Resources.Load<Material>(BubbleSplatMaterial.ResourcePath);
        var original = source.GetColor("_BaseColor");
        var texture = new Texture2D(2, 2);
        var outer = BubbleSplatMaterial.Create(texture, 1.5f, false, true);
        var inner = BubbleSplatMaterial.Create(texture, 1.7f, true, true);
        try
        {
            outer.SetColor("_BaseColor", new Color(.2f, .4f, .6f, 1f));
            BubbleWorldData.SetAlpha(outer, .25f);
            var faded = outer.GetColor("_BaseColor");
            Assert.That(Vector4.Distance(faded, new Color(.2f, .4f, .6f, .25f)), Is.LessThan(.00001f));
            Assert.That(inner.GetColor("_BaseColor").a, Is.EqualTo(1f));
            Assert.That(source.GetColor("_BaseColor"), Is.EqualTo(original));
            Assert.That(inner.GetTexture("_BaseMap"), Is.SameAs(texture));
            Assert.That(inner.GetFloat("_Style"), Is.EqualTo(1f));
            Assert.That(outer.GetFloat("_Style"), Is.Zero);
            Assert.That(inner.GetFloat("_EyeHeight"), Is.EqualTo(1.7f));
            Assert.That(inner.GetFloat("_WindStrength"), Is.Zero);
            Assert.That(inner.GetVector("_HandAmount"), Is.EqualTo(Vector4.zero));
            Assert.That(inner.renderQueue, Is.EqualTo(3000));
        }
        finally
        {
            Object.DestroyImmediate(outer);
            Object.DestroyImmediate(inner);
            Object.DestroyImmediate(texture);
        }
    }

    [Test]
    public void MotionGraphConvertsToMaterialX()
    {
        BubbleSplatMotionSetup.ValidateConversion();
    }

    [Test]
    public void AllBakedWorldsHaveValidMotionChannelsAndBounds()
    {
        BubbleSplatBakeValidation.Validate();
    }
}
