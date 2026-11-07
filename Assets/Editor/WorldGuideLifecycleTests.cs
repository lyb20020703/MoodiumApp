using System.Collections;
using System.Reflection;
using System.Linq;
using Moodium.Guide;
using NUnit.Framework;
using UnityEngine;

public sealed class WorldGuideLifecycleTests
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    GameObject root,viewer;WorldGuideCards guide;
    object Field(string name)=>typeof(WorldGuideCards).GetField(name,Private).GetValue(guide);
    object Call(string name,params object[] args)=>typeof(WorldGuideCards).GetMethod(name,Private).Invoke(guide,args);
    [SetUp] public void SetUp()
    {
        root=new GameObject("Guide lifecycle");viewer=new GameObject("Viewer");
        viewer.transform.position=new Vector3(1,1.6f,2);viewer.transform.rotation=Quaternion.Euler(20,70,15);
        guide=root.AddComponent<WorldGuideCards>();
    }
    [TearDown] public void TearDown(){guide.Hide(true);Object.DestroyImmediate(root);Object.DestroyImmediate(viewer);}
    [TestCase(false,false)] [TestCase(true,true)]
    public void EnterBuildsCorrectCardInFrontOfViewerAndExitRemovesIt(bool nature,bool english)
    {
        guide.ShowTutorial(nature,english,viewer.transform);
        Assert.That(guide.IsTutorialShowing,Is.True);
        var face=(SpriteRenderer)Field("m_Face");
        Assert.That(face.sprite.name,Is.EqualTo(nature?"guide_card_nature_press_en":"guide_card_candy_press_zh"));
        Assert.That(face.bounds.size.magnitude,Is.GreaterThan(.4f));
        var panel=(Transform)Field("m_Root");
        Assert.That(panel.position.y,Is.EqualTo(1.48f).Within(.001f));
        Assert.That(Vector3.Angle(panel.up,Vector3.up),Is.LessThan(.01f));
        guide.Hide(true);Assert.That(guide.IsShowing,Is.False);
    }
    [Test] public void SkipRaisesOnlyOnceAndFadingControlsRejectFurtherInput()
    {
        guide.ShowTutorial(false,false,viewer.transform);int finished=0;guide.TutorialFinished+=_=>finished++;
        guide.Skip();guide.Skip();guide.Next();
        Assert.That(finished,Is.EqualTo(1));
        foreach(var collider in ((Transform)Field("m_Root")).GetComponentsInChildren<Collider>())
            Assert.That(collider.enabled,Is.False,collider.name);
    }
    [Test] public void ExploreWaitsForUnfinishedTutorialInsteadOfReplacingIt()
    {
        guide.ShowTutorial(false,false,viewer.transform);
        var routine=(IEnumerator)Call("ShowExploreAfter",0f,GuideWorld.Candy,false,viewer.transform);
        Assert.That(routine.MoveNext(),Is.True,"Explore must wait for tutorial dismissal.");
        Assert.That(guide.IsTutorialShowing,Is.True);
        guide.Hide(true);
    }
    [Test] public void CancelledSwipeDoesNotFlipPageOrRemainCaptured()
    {
        guide.ShowTutorial(false,false,viewer.transform);
        Call("StopTransition");Call("SetAlpha",1f);
        var handle=((Transform)Field("m_Root")).GetComponentInChildren<WorldGuideSwipeHandle>();
        handle.PointerStarted(1,Vector3.zero);handle.PointerMoved(1,-((Transform)Field("m_Root")).right*.09f);
        handle.CancelPointer(1);
        Assert.That(guide.CardIndex,Is.Zero);
        Assert.That((bool)Field("m_Swiping"),Is.False);
        Assert.That(handle.CanInteract,Is.False,"Canceled fade/swipe should not admit another pointer before settling.");
    }
    [Test] public void ClosingOneInstanceDoesNotInvalidateOtherInstancesSprites()
    {
        var otherRoot=new GameObject("Other guide");var other=otherRoot.AddComponent<WorldGuideCards>();
        try
        {
            guide.ShowTutorial(false,false,viewer.transform);other.ShowTutorial(false,false,viewer.transform);
            var panel=(Transform)typeof(WorldGuideCards).GetField("m_Root",Private).GetValue(other);
            var face=panel.Find("Card/Face").GetComponent<SpriteRenderer>();
            var texture=face.sprite.texture;
            guide.Hide(true);
            Assert.That(texture!=null,Is.True);
            Assert.That(face.sprite.texture,Is.SameAs(texture));
        }
        finally{other.Hide(true);Object.DestroyImmediate(otherRoot);}
    }
    [Test] public void ButtonsAreHitBeforeTheSwipeSurface()
    {
        guide.ShowTutorial(false,false,viewer.transform);Call("StopTransition");Call("SetAlpha",1f);
        var button=(SpriteRenderer)Field("m_ActionButton");var panel=(Transform)Field("m_Root");
        Physics.SyncTransforms();
        var ray=new Ray(button.transform.position-panel.forward*.2f,panel.forward);
        Assert.That(Physics.Raycast(ray,out var hit,1f),Is.True);
        Assert.That(hit.collider.gameObject,Is.SameAs(button.gameObject));
    }
    [Test] public void LastOwnerReleasesTheSharedCardLease()
    {
        var shared=(System.Collections.IDictionary)typeof(WorldGuideCards).GetField("SharedSprites",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
        // Other legacy full-suite tests can leave live world managers behind after failing.
        // Use an unleased card so this really is its last owner, without touching those managers.
        var name=(from world in new[]{GuideWorld.Candy,GuideWorld.Nature}
            from card in new[]{GuideCard.Press,GuideCard.Free,GuideCard.Rhythm,GuideCard.Breath}
            from english in new[]{false,true}
            select WorldGuideCards.CardSpriteName(world,card,english)).First(n=>!shared.Contains(n));
        Assert.That(Call("Load",name),Is.Not.Null);
        guide.Hide(true);
        // Editor import machinery may retain the native Texture object after UnloadAsset.
        // Check our shared ownership contract; native memory reclamation needs device profiling.
        Assert.That(shared.Contains(name),Is.False);
        Assert.That(((System.Collections.IDictionary)Field("m_Loaded")).Count,Is.Zero);
    }
    [TestCase(false)] [TestCase(true)]
    public void ExplorePageIsTheCardOnlyWithoutTheOldRingHint(bool english)
    {
        // The light-ring step is gone, so the explore page must not draw the old hint line on top of the card.
        var dock=new GameObject("Dock");
        try
        {
            guide.ShowDocked(dock.transform,true,english,true);
            Assert.That(guide.CardIndex,Is.Zero);
            var panel=(Transform)Field("m_Root");
            Assert.That(panel.GetComponentsInChildren<TMPro.TMP_Text>(true),Is.Empty);
        }
        finally{guide.Hide(true);Object.DestroyImmediate(dock);}
    }
}
