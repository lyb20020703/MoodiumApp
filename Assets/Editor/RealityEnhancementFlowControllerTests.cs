using Moodium.Reality;
using NUnit.Framework;
using UnityEngine;

public sealed class RealityEnhancementFlowControllerTests
{
    [Test]
    public void LegacyInitialGuideIsDisabledByDefault()
    {
        var root = new GameObject("Reality Enhancement Flow Test");
        try
        {
            var controller = root.AddComponent<RealityEnhancementFlowController>();
            Assert.That(controller.ShowLegacyInitialGuide, Is.False);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }
}
