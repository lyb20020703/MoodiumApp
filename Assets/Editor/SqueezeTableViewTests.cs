using Moodium.Squeeze;
using NUnit.Framework;
using UnityEngine;
using UnityEditor;

public sealed class SqueezeTableViewTests
{
    [Test] public void RhythmHitUsesMiniMagicAtRaisedVolume()
    {
        var tuning=Resources.Load<SqueezeTuning>("MoodiumSqueezeModes/SqueezeTuning");
        Assert.That(tuning,Is.Not.Null);
        Assert.That(tuning.RhythmHitClip,Is.SameAs(AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Epic Toon FX/Sound/etfx_explosion_minimagic.wav")));
        Assert.That(tuning.RhythmHitVolume,Is.EqualTo(.22f).Within(.001f));
    }
    [Test] public void ModeButtonsSitBeyondTheFarBagEdgeAndKeepFacingTheUser()
    {
        var root=new GameObject("Far-side buttons");var session=new GameObject("Mode session");
        TableVisuals visuals=null;
        try
        {
            var half=new Vector2(.125f,.085f);
            visuals=new TableVisuals(root.transform,half,false);
            new ModeButtonBar(visuals,root.transform,session.AddComponent<SqueezeModeController>());
            Assert.That(root.transform.childCount,Is.EqualTo(3));
            for(int i=0;i<3;i++)
            {
                var button=root.transform.GetChild(i);
                Assert.That(button.localPosition.z,Is.LessThan(-half.y-.08f));
                Assert.That(button.localPosition.x,Is.EqualTo((1-i)*.085f).Within(.0001f));
                Assert.That(Quaternion.Angle(button.localRotation,Quaternion.identity),Is.LessThan(.001f));
            }
        }
        finally{visuals?.Dispose();Object.DestroyImmediate(root);Object.DestroyImmediate(session);}
    }
    [TestCase(35f,1f)]
    [TestCase(-50f,-1f)]
    public void ModeBarStaysParallelToBagWhenViewedFromAnAngle(float sideAngle,float side)
    {
        var bagRotation=Quaternion.Euler(8,23,-4);
        var viewerDirection=bagRotation*(Quaternion.Euler(0,sideAngle,0)*Vector3.forward*side);
        var local=SqueezeTableView.BuildAlignedUIRotation(bagRotation,viewerDirection);
        Assert.That(Mathf.Abs(Vector3.Dot(local*Vector3.right,Vector3.right)),Is.EqualTo(1).Within(.0001f));
        Assert.That(Vector3.Dot(local*Vector3.up,Vector3.up),Is.EqualTo(1).Within(.0001f));
        Assert.That(Vector3.Dot(local*Vector3.forward,Vector3.forward)*side,Is.GreaterThan(.999f));
    }
    [Test] public void FingertipContactConvertsTrackingCoordinatesToWorld()
    {
        var origin=new GameObject("Origin");var button=new GameObject("Mode button");
        try
        {
            origin.transform.SetPositionAndRotation(new Vector3(3,1,2),Quaternion.Euler(0,60,0));
            var local=new Vector3(.1f,.2f,.3f);button.transform.position=origin.transform.TransformPoint(local);
            var collider=button.AddComponent<BoxCollider>();collider.size=Vector3.one*.07f;
            Physics.SyncTransforms();
            Assert.That(ModeButtonBar.TouchesPoint(local,collider,origin.transform),Is.True);
            Assert.That(ModeButtonBar.TouchesPoint(local,collider,null),Is.False);
        }
        finally{Object.DestroyImmediate(origin);Object.DestroyImmediate(button);}
    }
    [Test] public void SuppliedAssetsLoadWithAlphaAndSingleSprites()
    {
        foreach(var guid in AssetDatabase.FindAssets("t:Texture2D",new[]{"Assets/UI/Moodium/SqueezeModes"}))
        {
            var path=AssetDatabase.GUIDToAssetPath(guid);
            if(!path.EndsWith(".png"))continue;
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            Assert.That(importer.spriteImportMode,Is.EqualTo(SpriteImportMode.Single),path);
            Assert.That(importer.alphaIsTransparency,Is.True,path);
            Assert.That(Resources.Load<Sprite>("MoodiumSqueezeModes/"+System.IO.Path.GetFileNameWithoutExtension(path)),Is.Not.Null,path);
        }
    }
    [Test] public void BandFillRestoresMeshAfterEmptyAndStaysOnTable()
    {
        var go=new GameObject("Band test");
        try
        {
            var band=go.AddComponent<RoundedRectBandMesh>();
            band.Set(new Vector2(.125f,.085f),.004f,.006f,0);
            band.Set(new Vector2(.125f,.085f),.004f,.006f,.5f);
            var mesh=go.GetComponent<MeshFilter>().sharedMesh;
            Assert.That(mesh.vertexCount,Is.EqualTo(514));
            foreach(var vertex in mesh.vertices)Assert.That(vertex.y,Is.Zero);
            Assert.That(mesh.bounds.size.x,Is.GreaterThan(.2f));
        }
        finally{Object.DestroyImmediate(go);}
    }
}
