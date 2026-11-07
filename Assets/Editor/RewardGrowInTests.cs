using Moodium.Rewards;
using NUnit.Framework;
using UnityEngine;

public sealed class RewardGrowInTests
{
    [Test]
    public void SnapBaseToSurface_PreservesFinalScaleAndPlacesRenderedBaseOnGround()
    {
        var root = GameObject.CreatePrimitive(PrimitiveType.Cube);
        root.transform.position = new Vector3(0f, 2f, 0f);
        root.transform.localScale = new Vector3(.4f, .6f, .8f);
        var expectedScale = root.transform.localScale;

        try
        {
            RewardGrowIn.SnapBaseToSurface(root, 0f);

            Assert.That(root.transform.localScale, Is.EqualTo(expectedScale));
            Assert.That(root.GetComponent<Renderer>().bounds.min.y, Is.EqualTo(0f).Within(.0001f));
            Assert.That(RewardGrowIn.EvaluateScale(.5f), Is.GreaterThan(1f));
            Assert.That(RewardGrowIn.EvaluateScale(1f), Is.EqualTo(1f).Within(.0001f));
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }
}
