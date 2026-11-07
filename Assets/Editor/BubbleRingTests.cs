using Moodium.NatureWorld;
using NUnit.Framework;
using UnityEngine;

public sealed class BubbleRingTests
{
    [Test] public void FloorUsesLowestUpwardSurfaceAndFallsBackAlongWalk()
    {
        var hits=new[]{new BubbleRingPlacement.GroundHit(Vector3.zero,Vector3.up),new BubbleRingPlacement.GroundHit(Vector3.up*.5f,Vector3.up),new BubbleRingPlacement.GroundHit(Vector3.down,Vector3.right)};
        Assert.That(BubbleRingPlacement.FloorRing(Vector3.up*1.8f,hits,new Vector3[0],new Vector3[0],out var p));Assert.That(p,Is.EqualTo(Vector3.zero));
        Assert.That(BubbleRingPlacement.FloorRing(Vector3.up*1.8f,hits,new[]{Vector3.right},new[]{Vector3.zero},out p));Assert.That(p,Is.EqualTo(Vector3.right));
        Assert.That(BubbleRingPlacement.FloorRing(Vector3.up*1.8f,hits,new[]{Vector3.right*2},new[]{Vector3.zero},out p),Is.False);
    }
    [Test] public void FloorHysteresisAndPalmCylinderHaveDistinctLimits()
    {
        Assert.That(BubbleRingPlacement.FloorInside(Vector3.right*.6f,Vector3.zero,false),Is.False);
        Assert.That(BubbleRingPlacement.FloorInside(Vector3.right*.6f,Vector3.zero,true),Is.True);
        Assert.That(BubbleRingPlacement.PalmInside(Vector3.up*.1f,Vector3.zero));
        Assert.That(BubbleRingPlacement.PalmInside(Vector3.up*.16f,Vector3.zero),Is.False);
        Assert.That(BubbleRingPlacement.PalmInside(Vector3.down*.01f,Vector3.zero),Is.False);
    }
    [Test] public void TableTriesRightLeftThenBackAndRequiresBag()
    {
        var bag=new Bounds(Vector3.zero,new Vector3(.2f,.1f,.2f));
        Assert.That(BubbleRingPlacement.TableRing(bag,0,Vector3.forward,new Bounds[0],out var p));Assert.That(p.x,Is.GreaterThan(0));
        var right=new Bounds(p,Vector3.one*.15f);
        Assert.That(BubbleRingPlacement.TableRing(bag,0,Vector3.forward,new[]{right},out p));Assert.That(p.x,Is.LessThan(0));
        var left=new Bounds(p,Vector3.one*.15f);
        Assert.That(BubbleRingPlacement.TableRing(bag,0,Vector3.forward,new[]{right,left},out p));Assert.That(p.z,Is.GreaterThan(0));
        Assert.That(BubbleRingPlacement.TableRing(null,0,Vector3.forward,new Bounds[0],out p),Is.False);
    }
    [Test] public void EachBlockerPreventsTrigger()
    {
        Assert.That(BubbleRingPlacement.CanTrigger(true,false,false,false));
        Assert.That(BubbleRingPlacement.CanTrigger(false,false,false,false),Is.False);
        Assert.That(BubbleRingPlacement.CanTrigger(true,true,false,false),Is.False);
        Assert.That(BubbleRingPlacement.CanTrigger(true,false,true,false),Is.False);
        Assert.That(BubbleRingPlacement.CanTrigger(true,false,false,true),Is.False);
    }
    [Test] public void FeedbackUsesOneDrawMeshAndReleasesGeneratedAssets()
    {
        var go=new GameObject("Ring feedback test");
        var controller=go.AddComponent<BubbleRingController>();
        try {
            controller.Configure(null,null,false);
            controller.OnWalkInvited(new Bounds(Vector3.zero,Vector3.one*.2f),0,new Bounds[0]);
            controller.OnWalkInvited(new Bounds(Vector3.zero,Vector3.one*.2f),0,new Bounds[0]);
            Assert.That(controller.RingCount,Is.EqualTo(1));
            Assert.That(go.GetComponentsInChildren<SpriteRenderer>(true).Length,Is.EqualTo(1));
            var mesh=go.GetComponentInChildren<MeshFilter>(true).sharedMesh;
            var material=go.GetComponentInChildren<MeshRenderer>(true).sharedMaterial;
            Assert.That(mesh.vertexCount,Is.EqualTo(212));
            Assert.That(go.GetComponentsInChildren<MeshRenderer>(true).Length,Is.EqualTo(1));
            controller.SetVisible(false);
            Assert.That(go.GetComponentsInChildren<SpriteRenderer>().Length,Is.Zero);
            controller.StopAndClear();
            Assert.That(controller.RingCount,Is.Zero);
            Assert.That(mesh==null,Is.True);
            Assert.That(material==null,Is.True);
        } finally { Object.DestroyImmediate(go); }
    }
}
