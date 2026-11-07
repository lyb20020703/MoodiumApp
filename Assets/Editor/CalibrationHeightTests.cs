using System;
using System.Linq;
using System.Reflection;
using Moodium.Calibration;
using NUnit.Framework;
using UnityEngine;
using Object=UnityEngine.Object;

public sealed class CalibrationHeightTests
{
    GameObject host,anchor,prefab;
    SensoryBagCalibrator calibrator;
    static readonly SensoryBagCalibrationSettings Settings=new(.4f,.25f,.05f,Vector2.zero,.08f,.6f,.06f,.45f);
    [TearDown] public void Cleanup()
    {
        if(host!=null)Object.DestroyImmediate(host);if(anchor!=null)Object.DestroyImmediate(anchor);if(prefab!=null)Object.DestroyImmediate(prefab);
    }
    void Begin()
    {
        anchor=new GameObject("Tilted sticker");anchor.transform.SetPositionAndRotation(new Vector3(1,1.2f,3),Quaternion.Euler(20,65,12));
        host=new GameObject("Calibrator");calibrator=host.AddComponent<SensoryBagCalibrator>();
        calibrator.Begin(anchor.transform,SensoryBagCalibrationData.CreateDefault("CandyWorldPhoneTracking",SensoryBagWorldKind.Candy,Settings),Settings);
    }
    static void SetHeight(SensoryBagCalibrator c,float value)
    {
        var method=typeof(SensoryBagCalibrator).GetMethod("SetPlaneHeight");Assert.That(method,Is.Not.Null,"Height adjustment API is missing.");
        method.Invoke(c,new object[]{value});
    }
    [Test] public void NewCoordinatorAndCalibratorUseFortyByTwentyFiveCentimetres()
    {
        host=new GameObject("Default size");var coordinator=host.AddComponent<SensoryBagCalibrationCoordinator>();
        var source=host.AddComponent<SensoryBagCalibrator>();
        Assert.That(coordinator.CreateSettings().DefaultWidth,Is.EqualTo(.4f));
        Assert.That(coordinator.CreateSettings().DefaultHeight,Is.EqualTo(.25f));
        Assert.That(source.CreateInspectorSettings().DefaultWidth,Is.EqualTo(.4f));
        Assert.That(source.CreateInspectorSettings().DefaultHeight,Is.EqualTo(.25f));
    }
    [Test] public void DefaultBasePlaneIsFiveCentimetresBelowTopSticker()
    {
        Begin();Assert.That(calibrator.CurrentData.PlaneLocalOffset.y,Is.EqualTo(-.05f).Within(.0001f));
        Assert.That(Vector3.Distance(calibrator.FrameRoot.position,anchor.transform.TransformPoint(new Vector3(0,-.05f,0))),Is.LessThan(.0001f));
    }
    [Test] public void HeightChangePreservesFootprintAllowsZeroAndClampsUnsafeOffsets()
    {
        Begin();calibrator.MoveFrameToAnchorPlane(new Vector2(.02f,-.03f));
        SetHeight(calibrator,0f);
        Assert.That(calibrator.CurrentData.PlaneLocalOffset.y,Is.Zero);
        Assert.That(calibrator.CurrentData.Width,Is.EqualTo(.4f));Assert.That(calibrator.CurrentData.Height,Is.EqualTo(.25f));
        Assert.That(calibrator.CurrentData.CenterOffset,Is.EqualTo(new Vector2(.02f,-.03f)));
        Assert.That(Vector3.Distance(calibrator.FrameRoot.position,anchor.transform.TransformPoint(new Vector3(.02f,0,-.03f))),Is.LessThan(.0001f));
        SetHeight(calibrator,-1f);Assert.That(calibrator.CurrentData.PlaneLocalOffset.y,Is.EqualTo(-.25f));
        SetHeight(calibrator,1f);Assert.That(calibrator.CurrentData.PlaneLocalOffset.y,Is.EqualTo(.1f));
        SetHeight(calibrator,float.NaN);Assert.That(calibrator.CurrentData.PlaneLocalOffset.y,Is.EqualTo(.1f));
        SetHeight(calibrator,float.PositiveInfinity);Assert.That(calibrator.CurrentData.PlaneLocalOffset.y,Is.EqualTo(.1f));
    }
    [Test] public void NormalDragChangesOnlyPlaneHeightAndTrackingLossEndsIt()
    {
        Begin();
        Assert.That(Enum.TryParse("Height",out CalibrationHandleKind height),Is.True,"Independent height handle is missing.");
        var start=anchor.transform.TransformPoint(new Vector3(.1f,.12f,.03f));
        calibrator.BeginPointerDrag(1,height,start);
        calibrator.MovePointerDrag(1,start+anchor.transform.up*.02f+anchor.transform.right*.07f);
        Assert.That(calibrator.CurrentData.PlaneLocalOffset.y,Is.EqualTo(-.03f).Within(.0001f));
        Assert.That(calibrator.CurrentData.CenterOffset,Is.EqualTo(Vector2.zero));
        Assert.That(calibrator.CurrentData.Width,Is.EqualTo(.4f));Assert.That(calibrator.CurrentData.Height,Is.EqualTo(.25f));
        calibrator.SetTrackingVisible(false);calibrator.SetTrackingVisible(true);
        calibrator.MovePointerDrag(1,start+anchor.transform.up*.1f);
        Assert.That(calibrator.CurrentData.PlaneLocalOffset.y,Is.EqualTo(-.03f).Within(.0001f));
    }
    [Test] public void FineButtonsStepHalfACentimetreAndResetAndConfirmPreserveTheirContracts()
    {
        Begin();
        var raise=calibrator.FrameRoot.Find("Calibration Raise Table Button");
        var lower=calibrator.FrameRoot.Find("Calibration Lower Table Button");
        Assert.That(raise,Is.Not.Null);Assert.That(lower,Is.Not.Null);
        raise.GetComponent<SensoryBagCalibrationHandle>().PointerStarted(2,raise.position);
        Assert.That(calibrator.CurrentData.PlaneLocalOffset.y,Is.EqualTo(-.045f).Within(.0001f));
        lower.GetComponent<SensoryBagCalibrationHandle>().PointerStarted(3,lower.position);
        Assert.That(calibrator.CurrentData.PlaneLocalOffset.y,Is.EqualTo(-.05f).Within(.0001f));
        SetHeight(calibrator,-.09f);calibrator.ResetCalibration();
        Assert.That(calibrator.CurrentData.PlaneLocalOffset.y,Is.EqualTo(-.05f).Within(.0001f));
        SetHeight(calibrator,-.075f);SensoryBagCalibrationData received=default;calibrator.CalibrationConfirmed+=d=>received=d;
        calibrator.ConfirmCalibration();SetHeight(calibrator,-.01f);
        Assert.That(received.PlaneLocalOffset.y,Is.EqualTo(-.075f).Within(.0001f));
        Assert.That(calibrator.CurrentData.PlaneLocalOffset.y,Is.EqualTo(-.075f).Within(.0001f));
    }
    [Test] public void PlanarResizeAndFrameMoveKeepTheAdjustedHeight()
    {
        Begin();SetHeight(calibrator,-.08f);
        calibrator.ResizeFromHandle(CalibrationHandleKind.Right,new Vector2(.3f,0));
        var center=calibrator.CurrentData.CenterOffset;
        var start=anchor.transform.TransformPoint(Vector3.zero);
        calibrator.BeginPointerDrag(8,CalibrationHandleKind.Frame,start);
        calibrator.MovePointerDrag(8,start+anchor.transform.TransformVector(new Vector3(.04f,.07f,.03f)));
        Assert.That(calibrator.CurrentData.PlaneLocalOffset.y,Is.EqualTo(-.08f).Within(.0001f));
        Assert.That(calibrator.CurrentData.Width,Is.EqualTo(.5f).Within(.0001f));
        Assert.That(Vector2.Distance(calibrator.CurrentData.CenterOffset,center+new Vector2(.04f,.03f)),Is.LessThan(.0001f));
    }
    [Test] public void TiltedOffCenterHalfScaleModelBottomAndInteractionVolumeMatchBasePlane()
    {
        anchor=new GameObject("Sticker");anchor.transform.rotation=Quaternion.Euler(20,65,12);
        prefab=new GameObject("Off center model");prefab.transform.localScale=Vector3.one*.5f;
        var mesh=GameObject.CreatePrimitive(PrimitiveType.Cube);mesh.transform.SetParent(prefab.transform,false);
        mesh.transform.localPosition=new Vector3(.7f,.25f,-.4f);mesh.transform.localScale=new Vector3(2,.4f,1);
        host=new GameObject("Presentation");var p=host.AddComponent<CalibratedSensoryBagPresentation>();
        var data=SensoryBagCalibrationData.CreateDefault("Candy",SensoryBagWorldKind.Candy,Settings);data.CenterOffset=new Vector2(.04f,-.03f);
        p.Configure(anchor.transform,data,prefab);
        var renderer=p.VisualRoot.GetComponentInChildren<Renderer>();
        var points=(from x in new[]{-.5f,.5f} from y in new[]{-.5f,.5f} from z in new[]{-.5f,.5f}
            select anchor.transform.InverseTransformPoint(renderer.transform.TransformPoint(new Vector3(x,y,z)))).ToArray();
        Assert.That(points.Min(v=>v.y),Is.EqualTo(-.05f).Within(.0001f));
        Assert.That(points.Max(v=>v.x)-points.Min(v=>v.x),Is.EqualTo(.4f).Within(.0001f));
        Assert.That(points.Max(v=>v.z)-points.Min(v=>v.z),Is.EqualTo(.25f).Within(.0001f));
        Assert.That((points.Max(v=>v.x)+points.Min(v=>v.x))*.5f,Is.EqualTo(.04f).Within(.0001f));
        var volume=p.InteractionVolume.Collider;
        var worldCenter=volume.transform.TransformPoint(volume.center);
        Assert.That(Vector3.Dot(worldCenter-p.transform.position,anchor.transform.up),Is.EqualTo(.025f).Within(.0001f));
    }
    [Test] public void TabletopModesDoNotSubtractBagThicknessAgainFromTheCalibratedBase()
    {
        anchor=new GameObject("Sticker");anchor.transform.rotation=Quaternion.Euler(20,65,12);
        host=new GameObject("Base and modes");var p=host.AddComponent<CalibratedSensoryBagPresentation>();
        p.Configure(anchor.transform,SensoryBagCalibrationData.CreateDefault("Candy",SensoryBagWorldKind.Candy,Settings),null);
        var table=host.AddComponent<Moodium.Squeeze.SqueezeTableView>();
        var surface=new GameObject("Table surface");surface.transform.SetParent(host.transform,false);
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        foreach(var item in new[]{("anchor",(object)host.transform),("root",surface.transform),("presentation",p)})
            typeof(Moodium.Squeeze.SqueezeTableView).GetField(item.Item1,flags).SetValue(table,item.Item2);
        typeof(Moodium.Squeeze.SqueezeTableView).GetMethod("UpdatePose",flags).Invoke(table,null);
        Assert.That(Vector3.Dot(surface.transform.position-p.transform.position,anchor.transform.up),Is.EqualTo(.002f).Within(.0001f));
        typeof(Moodium.Squeeze.SqueezeTableView).GetField("root",flags).SetValue(table,null);
    }
    [TestCase("Assets/prefab/CandySoft.prefab")]
    [TestCase("Assets/prefab/NatureSoft.prefab")]
    public void RealSoftAssetsFitTheFortyByTwentyFiveFrameAndRestOnItsBase(string path)
    {
        anchor=new GameObject("Rotated real-bag sticker");anchor.transform.rotation=Quaternion.Euler(15,75,10);
        host=new GameObject("Real bag presentation");var p=host.AddComponent<CalibratedSensoryBagPresentation>();
        var asset=UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);Assert.That(asset,Is.Not.Null);
        p.Configure(anchor.transform,SensoryBagCalibrationData.CreateDefault("Real bag",SensoryBagWorldKind.Candy,Settings),asset);
        var bounds=new Bounds();bool hasBounds=false;
        foreach(var renderer in p.VisualRoot.GetComponentsInChildren<Renderer>(true))
        {
            var local=renderer.localBounds;
            foreach(var x in new[]{-1,1})foreach(var y in new[]{-1,1})foreach(var z in new[]{-1,1})
            {
                var world=renderer.transform.TransformPoint(local.center+Vector3.Scale(local.extents,new Vector3(x,y,z)));
                var point=anchor.transform.InverseTransformPoint(world);
                if(hasBounds)bounds.Encapsulate(point);else{bounds=new Bounds(point,Vector3.zero);hasBounds=true;}
            }
        }
        Assert.That(hasBounds,Is.True);
        Assert.That(bounds.min.y,Is.EqualTo(-.05f).Within(.002f),path);
        Assert.That(bounds.size.x,Is.EqualTo(.4f).Within(.002f),path);
        Assert.That(bounds.size.z,Is.EqualTo(.25f).Within(.002f),path);
    }
}
