using Moodium.Flow;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;

public sealed class StickerWorldEntryTests
{
    GameObject m_Root;

    [SetUp]
    public void SetUp()
    {
        m_Root = new GameObject("Sticker World Entry Test Root");
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(m_Root);
    }

    [Test]
    public void Constructor_RetainsSelectedTrackedAnchorAndWorld()
    {
        var anchor = new GameObject("Candy Anchor").transform;
        anchor.SetParent(m_Root.transform);
        var world = ScriptableObject.CreateInstance<MoodiumWorldDefinition>();
        var entry = new StickerWorldEntry(
            new TrackableId(3, 7), "CandyWorldPhoneTracking", world, anchor, null);

        Assert.That(entry.ReferenceImageName, Is.EqualTo("CandyWorldPhoneTracking"));
        Assert.That(entry.Anchor, Is.SameAs(anchor));
        Assert.That(entry.World, Is.SameAs(world));

        Object.DestroyImmediate(world);
    }
}
