using Moodium.Reality;
using NUnit.Framework;
using UnityEngine;

public sealed class WorldEnvironmentPromptLayoutTests
{
    [Test]
    public void CameraRelativePosition_StaysInFrontOfViewerAtSubtitleHeight()
    {
        var position = WorldEnvironmentPromptLayout.CameraRelativePosition(
            Vector3.zero, Vector3.forward, Vector3.up);

        Assert.That(position.x, Is.EqualTo(0f).Within(.0001f));
        Assert.That(position.y, Is.EqualTo(.03f).Within(.0001f));
        Assert.That(position.z, Is.EqualTo(.9f).Within(.0001f));
    }

    [Test]
    public void WorldAnchorPosition_StaysNearTheActiveWorldAndFacesViewer()
    {
        var position = WorldEnvironmentPromptLayout.WorldAnchorPosition(
            new Vector3(0f, 0f, 2f),
            new Vector3(0f, 0f, 1f),
            Vector3.up,
            new Vector3(0f, 0f, 0f));

        Assert.That(position.y, Is.EqualTo(.22f).Within(.0001f));
        Assert.That(position.z, Is.EqualTo(1.88f).Within(.0001f));
    }
}
