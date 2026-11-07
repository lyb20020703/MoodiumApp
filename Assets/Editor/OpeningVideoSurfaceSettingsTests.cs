using Moodium.Opening;
using NUnit.Framework;
using UnityEngine.Rendering;

public sealed class OpeningVideoSurfaceSettingsTests
{
    [Test]
    public void TransparentAlphaVideo_UsesSourceAlphaBlendingWithoutDepthWrites()
    {
        var settings = OpeningVideoSurfaceSettings.TransparentAlpha;

        Assert.That(settings.SurfaceType, Is.EqualTo(1f));
        Assert.That(settings.SourceBlend, Is.EqualTo((float)BlendMode.SrcAlpha));
        Assert.That(settings.DestinationBlend, Is.EqualTo((float)BlendMode.OneMinusSrcAlpha));
        Assert.That(settings.DepthWrite, Is.Zero);
    }
}
