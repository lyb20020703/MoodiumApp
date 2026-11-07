using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Moodium.NatureWorld;
using NUnit.Framework;
using Unity.PolySpatial;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class WishInkVisibilityTests
{
    [TestCase("Candy")]
    [TestCase("Nature")]
    public void FirstContactAndLiveTipAreVisibleBeforePenUp(string world)
    {
        var root=new GameObject("paper");
        try
        {
            var go=new GameObject("ink");var ink=go.AddComponent<WishInkMesh>();
            var frame=Resources.Load<WishPaperFrame>("MoodiumBubblePlay/PaperFrame_"+world);
            ink.Initialize(root.transform,frame,world=="Candy");
            var points=new List<float[]>{new[]{.2f,.5f}};ink.Active(points);
            var current=go.transform.Find("Current Ink").GetComponent<MeshFilter>().sharedMesh;
            Assert.That(current.vertexCount,Is.GreaterThan(0));
            Assert.That(go.transform.Find("Completed Ink").GetComponent<MeshFilter>().sharedMesh.vertexCount,Is.Zero);
            var dot=current.bounds;ink.Active(points,new Vector2(.8f,.5f));
            Assert.That(current.bounds.size.x,Is.GreaterThan(dot.size.x*3));
            Assert.That(points.Count,Is.EqualTo(1),"Transient tip must not mutate persisted points");
            var live=current.vertices;var endpoint=(live[live.Length-2]+live[live.Length-1])*.5f;
            Assert.That(Vector3.Distance(endpoint,frame.Surface(new Vector2(.8f,.5f))),Is.LessThan(.0015f));
            ink.Completed(new(){new(){new[]{.2f,.5f},new[]{.8f,.5f}}});ink.Active(null);
            Assert.That(current.vertexCount,Is.Zero);
            Assert.That(go.transform.Find("Completed Ink").GetComponent<MeshFilter>().sharedMesh.vertexCount,Is.GreaterThan(0));
        }
        finally{Object.DestroyImmediate(root);}
    }

    [TestCase("Candy",1f)] [TestCase("Candy",2.5f)]
    [TestCase("Nature",1f)] [TestCase("Nature",2.5f)]
    public void BothInkLayersHaveMetricClearanceUnderRotatedScaledParents(string world,float scale)
    {
        var root=new GameObject("world");
        try
        {
            root.transform.localScale=new Vector3(.8f,1.3f,.7f);
            root.transform.rotation=Quaternion.Euler(17,62,11);
            var paper=new GameObject("paper");paper.transform.SetParent(root.transform,false);paper.transform.localScale=Vector3.one*scale;
            var go=new GameObject("ink");var ink=go.AddComponent<WishInkMesh>();
            var frame=Resources.Load<WishPaperFrame>("MoodiumBubblePlay/PaperFrame_"+world);
            ink.Initialize(paper.transform,frame,world=="Candy");
            var points=new List<float[]>();for(int i=0;i<40;i++)points.Add(new[]{.1f+i*.8f/39,.5f});ink.Active(points);
            foreach(var name in new[]{"Current Ink","Current Ink Glow"})
            {
                var filter=go.transform.Find(name).GetComponent<MeshFilter>();
                var expected=name.EndsWith("Glow")?WishInkMesh.GlowLiftMetres:WishInkMesh.InkLiftMetres;
                foreach(var v in filter.sharedMesh.vertices)
                {
                    // Clearance is physical, independent of parent import/world scale.
                    var uv=frame.Project(v);
                    var n=Vector3.Cross(paper.transform.TransformVector(frame.UpAt(uv)),paper.transform.TransformVector(frame.RightAt(uv))).normalized;
                    var clearance=Vector3.Dot(filter.transform.TransformPoint(v)-paper.transform.TransformPoint(frame.Surface(uv)),n);
                    Assert.That(clearance,Is.GreaterThan(expected*.9f));
                    Assert.That(clearance,Is.LessThan(expected*1.1f));
                }
            }
        }
        finally{Object.DestroyImmediate(root);}
    }

    [Test]
    public void NativeSortingSeparatesPaperGlowAndLiveInkAndBudgetRemainsBounded()
    {
        var root=new GameObject("scan");
        try
        {
            var group=root.AddComponent<VisionOSSortingGroup>();
            var paper=new GameObject("paper");paper.transform.SetParent(root.transform,false);
            Moodium.UI.UIRefreshVisuals.Register(paper.transform,paper,WishInkMesh.PaperOrder);
            var go=new GameObject("ink");var ink=go.AddComponent<WishInkMesh>();
            ink.Initialize(paper.transform,Resources.Load<WishPaperFrame>("MoodiumBubblePlay/PaperFrame_Candy"),true);
            var points=new List<float[]>();for(int i=0;i<200;i++)points.Add(new[]{i/200f,.5f});ink.Active(points,Vector2.one*.8f);
            int activeVertices=0;
            foreach(var renderer in go.GetComponentsInChildren<MeshRenderer>())
            {
                var entry=group.Renderers.Single(e=>e.Renderer==renderer.gameObject);
                Assert.That(entry.ApplyToDescendants,Is.False);
                Assert.That(entry.Order,Is.GreaterThan(WishInkMesh.PaperOrder));
                Assert.That(renderer.sharedMaterials.Length,Is.EqualTo(1));
                Assert.That(renderer.sharedMaterial.GetFloat("_Cull"),Is.EqualTo((float)CullMode.Off));
                if(renderer.name.StartsWith("Current"))activeVertices+=renderer.GetComponent<MeshFilter>().sharedMesh.vertexCount;
            }
            Assert.That(activeVertices,Is.LessThanOrEqualTo(300));
            ink.Alpha(.25f);
            Assert.That(go.transform.Find("Current Ink").GetComponent<MeshRenderer>().sharedMaterial.GetColor("_BaseColor").a,Is.EqualTo(.25f).Within(.001f));
            ink.Show(false);Assert.That(go.GetComponentsInChildren<MeshRenderer>().All(r=>!r.enabled),Is.True);
        }
        finally{Object.DestroyImmediate(root);}
    }

    [TestCase("Candy")] [TestCase("Nature")]
    public void HandInputUpdatesInkWhileStillPinchingAndCommitsTheVisibleEndpoint(string world)
    {
        const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;
        var root=new GameObject("writing fixture");
        try
        {
            var viewer=root.AddComponent<Camera>();var hands=new BubblePlayHands();
            var garden=root.AddComponent<WishChimeGarden>();
            garden.Initialize(world=="Candy"?BubbleWorldData.CandyFolder:BubbleWorldData.NatureFolder,root.transform,viewer,hands,null);
            typeof(WishChimeGarden).GetField("taught",flags).SetValue(garden,true);
            var go=new GameObject("wish");go.transform.SetParent(root.transform,false);var chime=go.AddComponent<WishChime>();
            chime.Initialize(garden,new WishChimeRecord{id="fixture",position=new[]{0f,0f,0f}});
            typeof(WishChime).GetField("<State>k__BackingField",flags).SetValue(chime,WishChime.Stage.Blank);chime.BeginWrite();
            var frame=Resources.Load<WishPaperFrame>("MoodiumBubblePlay/PaperFrame_"+world);
            var update=typeof(WishChime).GetMethod("Update",flags);
            for(int i=0;i<20;i++)
            {
                var uv=new Vector2(.2f+i*.6f/19,.5f);
                hands.Hands[1]=new BubblePlayHand{Valid=true,Pinching=true,Tip=chime.Strip.TransformPoint(frame.Surface(uv))};
                update.Invoke(chime,null);
                Assert.That(chime.State,Is.EqualTo(WishChime.Stage.Writing));
                var ink=go.GetComponentInChildren<WishInkMesh>();
                Assert.That(ink.transform.Find("Current Ink").GetComponent<MeshFilter>().sharedMesh.vertexCount,Is.GreaterThan(0));
                Assert.That(ink.transform.Find("Completed Ink").GetComponent<MeshFilter>().sharedMesh.vertexCount,Is.Zero);
            }
            hands.Hands[1]=default;update.Invoke(chime,null);
            var completed=go.GetComponentInChildren<WishInkMesh>().transform.Find("Completed Ink").GetComponent<MeshFilter>().sharedMesh;
            Assert.That(completed.vertexCount,Is.GreaterThan(0));
            Assert.That(completed.vertices.Max(v=>frame.Project(v).x),Is.GreaterThan(.79f));
        }
        finally{Object.DestroyImmediate(root);}
    }
}
