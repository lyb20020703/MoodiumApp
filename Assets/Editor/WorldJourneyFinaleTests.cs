using NUnit.Framework;
using UnityEngine;
using Moodium.Flow;
using Moodium.NatureWorld;
public class WorldJourneyFinaleTests
{
    [Test] public void RescueExploreBubbleReturnAndExitAreGuarded()
    {
        var go = new GameObject(); var journey = go.AddComponent<WorldJourneyDirector>();
        try {
            journey.Begin(); Assert.IsTrue(journey.CanGrowInRoom); Assert.IsFalse(journey.EnterBubble());
            journey.EnergyFull(true); Assert.AreEqual(WorldJourneyStage.Rescue, journey.Stage);
            journey.Rescued(); Assert.IsTrue(journey.EnterBubble()); Assert.IsFalse(journey.CanGrowInRoom);
            journey.Returned(); Assert.AreEqual(WorldJourneyStage.Explore, journey.Stage);
            Assert.IsTrue(journey.EnterBubble()); Assert.IsTrue(journey.RequestExit()); Assert.IsFalse(journey.RequestExit());
            Assert.AreEqual(WorldJourneyStage.Bubble, journey.Stage); journey.Returned();
            Assert.AreEqual(WorldJourneyStage.Ending, journey.Stage); Assert.IsFalse(journey.CanGrowInRoom);
        } finally { Object.DestroyImmediate(go); }
    }
    [TestCase(false,true,1.99f,5f,false)]
    [TestCase(false,true,2f,5f,true)]
    [TestCase(false,false,-1f,19.99f,false)]
    [TestCase(false,false,-1f,20f,true)]
    [TestCase(true,true,5f,30f,false)]
    public void WalkWaitsForPalmOrTimeout(bool hand,bool cycle,float free,float full,bool expected) =>
        Assert.AreEqual(expected, WalkTrailGuideController.ShouldOfferWalk(hand,cycle,free,full));
    [Test] public void NoRescueUsesFullEnergy()
    {
        var go = new GameObject(); try { var j = go.AddComponent<WorldJourneyDirector>(); j.Begin(); j.EnergyFull(false); Assert.AreEqual(WorldJourneyStage.Explore,j.Stage); } finally { Object.DestroyImmediate(go); }
    }
}

public class RoomVisibilityFinaleTests
{
    [UnityEngine.TestTools.UnityTest]
    public System.Collections.IEnumerator ReversingHidePreservesInactiveAndActiveObjects()
    {
        var owner = new GameObject("visibility"); var active = new GameObject("active"); var inactive = new GameObject("inactive");
        inactive.SetActive(false); active.transform.localScale = new Vector3(2,3,4);
        var visibility = owner.AddComponent<RoomContentVisibility>(); visibility.Register(active); visibility.Register(inactive);
        visibility.SetHidden(true); yield return null; visibility.SetHidden(false);
        var restore = (System.Collections.IEnumerator)typeof(RoomContentVisibility).GetMethod("Apply",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(visibility,new object[]{false});
        while (restore.MoveNext()) yield return null;
        Assert.IsTrue(active.activeSelf); Assert.IsFalse(inactive.activeSelf);
        Assert.AreEqual(new Vector3(2,3,4), active.transform.localScale);
        Object.DestroyImmediate(owner); Object.DestroyImmediate(active); Object.DestroyImmediate(inactive);
    }
    [UnityEngine.TestTools.UnityTest]
    public System.Collections.IEnumerator ClearDuringHideRestoresEveryCapturedObject()
    {
        var owner = new GameObject(); var visibility = owner.AddComponent<RoomContentVisibility>();
        var objects = new System.Collections.Generic.List<GameObject>();
        for (int i=0;i<45;i++) { var target = new GameObject(); target.SetActive(i%2==0); objects.Add(target); visibility.Register(target); }
        visibility.SetHidden(true); yield return null; visibility.Clear();
        for (int i=0;i<objects.Count;i++) { Assert.AreEqual(i%2==0,objects[i].activeSelf); Object.DestroyImmediate(objects[i]); }
        Object.DestroyImmediate(owner);
    }
    [Test]
    public void BraceletInspectionIsBlockedWhileRetainingCollectedState()
    {
        var owner = new GameObject(); var bracelet = owner.AddComponent<Moodium.Rewards.RewardBracelet>();
        bracelet.SetProgress(1); bracelet.SetInteractionEnabled(false);
        Assert.AreEqual(string.Empty,bracelet.InspectSlot(0,"en")); Assert.AreEqual(-1,bracelet.SelectedSlot);
        Assert.AreEqual(1,bracelet.NextSlotProgress); Object.DestroyImmediate(owner);
    }
}
