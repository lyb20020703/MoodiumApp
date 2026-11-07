using System.Collections.Generic;
using System.Reflection;
using Moodium.CandyWorld;
using Moodium.Rewards;
using NUnit.Framework;
using UnityEngine;

public sealed class RewardSequenceControllerTests
{
    [Test]
    public void Session_DeliversEachStageOnlyOnceAndClearsOnStop()
    {
        var root = new GameObject("Reward sequence");
        var table = ScriptableObject.CreateInstance<WorldRewardTable>();
        var prefab = new GameObject("Reward");
        var entries = new List<RewardEntry> { new() { stage = 20, prefabs = new[] { prefab }, displayName = "Gift", targetHeight = .25f } };
        typeof(WorldRewardTable).GetField("m_Entries", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(table, entries);
        var energy = root.AddComponent<CandyEnergyController>();
        var sequence = root.AddComponent<RewardSequenceController>();
        try
        {
            sequence.Configure(energy, table, null, null, false, null);
            sequence.BeginSession();
            for (var i = 0; i < 8; i++) energy.RegisterPinch();
            Assert.That(sequence.DeliveredStageCount, Is.EqualTo(1));
            sequence.StopAndClear();
            Assert.That(sequence.DeliveredStageCount, Is.EqualTo(0));
        }
        finally { Object.DestroyImmediate(prefab); Object.DestroyImmediate(table); Object.DestroyImmediate(root); }
    }
}
