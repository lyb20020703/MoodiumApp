using Moodium.NatureWorld;
using NUnit.Framework;
using UnityEngine;

public sealed class BubbleWorldSkyTests
{
    static readonly string[] Folders =
    {
        BubbleWorldData.NatureFolder, BubbleWorldData.NestedNatureFolder,
        BubbleWorldData.CandyFolder, BubbleWorldData.NestedCandyFolder, BubbleWorldData.MapleNatureFolder
    };

    [Test]
    public void EveryWorldHasASoftSkyThatIsOnlyColour()
    {
        foreach (var folder in Folders)
        {
            var soft = Resources.Load<Texture2D>(BubbleWorldData.SoftPanoramaResource(folder));
            Assert.That(soft, Is.Not.Null, folder);
            Assert.That(soft.width, Is.LessThanOrEqualTo(64), folder + " no photo detail");
            Assert.That(soft.width, Is.EqualTo(soft.height * 2), folder + " equirectangular");
            Assert.That(soft.mipmapCount, Is.EqualTo(1), folder);
            Assert.That(soft.wrapModeU, Is.EqualTo(TextureWrapMode.Repeat), folder + " wraps around");
        }
    }

    [Test]
    public void SkyTextureFollowsTheStyle()
    {
        foreach (var folder in Folders)
        {
            var photo = Resources.Load<Texture2D>(BubbleWorldData.PanoramaResource(folder));
            var soft = Resources.Load<Texture2D>(BubbleWorldData.SoftPanoramaResource(folder));
            Assert.That(BubbleWorldController.SkyTexture(folder, BubbleWorldController.SkyStyle.Soft, photo), Is.SameAs(soft), folder);
            Assert.That(BubbleWorldController.SkyTexture(folder, BubbleWorldController.SkyStyle.Photo, photo), Is.SameAs(photo), folder);
        }
    }

    [Test]
    public void SoftSkyIsTheDefaultAndTheSkyDrawsBeforeThePoints()
    {
        var go = new GameObject("Sky style");
        try { Assert.That(go.AddComponent<BubbleWorldController>().Sky, Is.EqualTo(BubbleWorldController.SkyStyle.Soft)); }
        finally { Object.DestroyImmediate(go); }
        Assert.That(BubbleWorldController.SkySortOrder, Is.LessThan(BubbleWorldController.PointLayerSortOrder));
    }
}
