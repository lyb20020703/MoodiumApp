using System.Collections.Generic;
using System.Reflection;
using Moodium.Rewards;
using NUnit.Framework;
using UnityEngine;

public sealed class WorldRewardTableTests
{
    [Test]
    public void TryGetEntry_ReturnsOnlySupportedStagesWithAUsableReward()
    {
        var prefab = new GameObject("Reward Prefab");
        var table = ScriptableObject.CreateInstance<WorldRewardTable>();

        try
        {
            SetEntries(table, new List<RewardEntry>
            {
                new() { stage = 20, prefabs = new[] { prefab }, displayName = "Macarons", sizeClass = RewardSizeClass.Small, targetHeight = .25f },
                new() { stage = 50, prefabs = new GameObject[] { null }, displayName = "Empty", sizeClass = RewardSizeClass.Medium, targetHeight = .5f },
                new() { stage = 35, prefabs = new[] { prefab }, displayName = "Invalid stage", sizeClass = RewardSizeClass.Small, targetHeight = .25f }
            });

            Assert.That(table.TryGetEntry(20, out var valid), Is.True);
            Assert.That(valid.displayName, Is.EqualTo("Macarons"));
            Assert.That(table.TryGetEntry(50, out _), Is.False, "An entry with no usable prefab must not be delivered.");
            Assert.That(table.TryGetEntry(35, out _), Is.False, "Only the four energy stages are reward stages.");
        }
        finally
        {
            Object.DestroyImmediate(prefab);
            Object.DestroyImmediate(table);
        }
    }

    [Test]
    public void Get_GemLabelUsesRequestedLanguageGiftNumberAndName()
    {
        Assert.That(RewardTextTable.Get("gem.label", "Chinese", 3, "糖果屋"), Is.EqualTo("第 3 件 · 糖果屋"));
        Assert.That(RewardTextTable.Get("gem.label", "English", 3, "Candy House"), Is.EqualTo("Gift 3 · Candy House"));
    }

    static void SetEntries(WorldRewardTable table, List<RewardEntry> entries)
    {
        var field = typeof(WorldRewardTable).GetField("m_Entries", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null);
        field.SetValue(table, entries);
    }
}
