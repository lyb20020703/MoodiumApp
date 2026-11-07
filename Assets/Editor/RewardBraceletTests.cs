using Moodium.Rewards;
using NUnit.Framework;
using UnityEngine;

public sealed class RewardBraceletTests
{
    [Test]
    public void Bracelet_TracksFourSlotsProgressVisibilityAndInspection()
    {
        var root = new GameObject("Bracelet");
        var orbRoot = new GameObject("Orb");
        var bracelet = root.AddComponent<RewardBracelet>();
        var orb = orbRoot.AddComponent<RewardOrb>();

        try
        {
            bracelet.SetProgress(.5f);
            bracelet.SetTrackedWrist(false, false);
            Assert.That(bracelet.SlotCount, Is.EqualTo(4));
            Assert.That(bracelet.CollectedCount, Is.EqualTo(0));
            Assert.That(bracelet.NextSlotProgress, Is.EqualTo(.5f));
            Assert.That(bracelet.VisualAlpha, Is.EqualTo(0f));
            Assert.That(bracelet.GetSlotScale(0), Is.EqualTo(.9f));
            Assert.That(bracelet.GetSlotScale(1), Is.EqualTo(1f));

            bracelet.SetTrackedWrist(true, false);
            Assert.That(bracelet.VisualAlpha, Is.EqualTo(.35f));
            Assert.That(bracelet.VisualScale, Is.EqualTo(1f));

            bracelet.SetTrackedWrist(true, true);
            Assert.That(bracelet.VisualAlpha, Is.EqualTo(1f));
            Assert.That(bracelet.VisualScale, Is.EqualTo(1.15f));
            Assert.That(bracelet.TryCollect(orb, new RewardEntry { displayName = "Candy House" }), Is.True);
            Assert.That(bracelet.CollectedCount, Is.EqualTo(1));
            Assert.That(bracelet.InspectSlot(0, "English"), Is.EqualTo("Gift 1 · Candy House"));
        }
        finally
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(orbRoot);
        }
    }
}
