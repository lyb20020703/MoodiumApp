using System;
using NUnit.Framework;
using UnityEngine;

public sealed class PolySpatialVideoRenderTextureUpdaterTests
{
    [Test]
    public void Updater_OnlyTransfersACreatedRenderTexture()
    {
        var updaterType = Type.GetType(
            "Moodium.Opening.PolySpatialVideoRenderTextureUpdater, Assembly-CSharp");
        Assert.That(updaterType, Is.Not.Null,
            "Opening video needs a PolySpatial updater so Vision Pro receives every decoded frame.");

        var shouldTransfer = updaterType.GetMethod("ShouldTransfer");
        Assert.That(shouldTransfer, Is.Not.Null);
        Assert.That(shouldTransfer.Invoke(null, new object[] { null }), Is.False);

        var texture = new RenderTexture(16, 16, 0);
        try
        {
            Assert.That(shouldTransfer.Invoke(null, new object[] { texture }), Is.False);
            texture.Create();
            Assert.That(shouldTransfer.Invoke(null, new object[] { texture }), Is.True);
        }
        finally
        {
            texture.Release();
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }
}
