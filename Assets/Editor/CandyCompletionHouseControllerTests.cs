using Moodium.CandyWorld;
using NUnit.Framework;
using UnityEngine;

public sealed class CandyCompletionHouseControllerTests
{
    [Test]
    public void CompletionHouseUsesAForwardRightOffset()
    {
        Assert.That(CandyCompletionHouseController.GetViewerRelativeSpawnOffset(),
            Is.EqualTo(new Vector2(2.8f, 2.9f)));
    }

    [Test]
    public void MainHouseSpawnsAtFiftyFivePercentOnlyOnce()
    {
        Assert.That(CandyCompletionHouseController.ShouldSpawnCandyHouse(.55f, false), Is.True);
        Assert.That(CandyCompletionHouseController.ShouldSpawnCandyHouse(.54f, false), Is.False);
        Assert.That(CandyCompletionHouseController.ShouldSpawnCandyHouse(1f, true), Is.False);
    }

    [Test]
    public void LittleHouseSpawnsAtNinetyPercentOnlyOnce()
    {
        Assert.That(CandyCompletionHouseController.ShouldSpawnCandyLittleHouse(.9f, false), Is.True);
        Assert.That(CandyCompletionHouseController.ShouldSpawnCandyLittleHouse(.89f, false), Is.False);
        Assert.That(CandyCompletionHouseController.ShouldSpawnCandyLittleHouse(1f, true), Is.False);
    }

    [Test]
    public void CompletionHouseUsesThreeTimesTheSourcePrefabScale()
    {
        Assert.That(CandyCompletionHouseController.GetHouseScaleMultiplier(), Is.EqualTo(3f));
    }
}
