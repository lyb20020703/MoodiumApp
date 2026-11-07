using System.Reflection;
using Moodium.NatureWorld;
using Moodium.Reality;
using NUnit.Framework;
using UnityEngine;

public sealed class WalkCompanionPresentationTests
{
    [Test]
    public void GuideNormalizesDifferentModelsToTwentyCentimetres()
    {
        var model = GameObject.CreatePrimitive(PrimitiveType.Cube);
        try
        {
            model.transform.localScale = new Vector3(2f, 1f, .5f);
            typeof(WalkTrailGuideController).GetMethod("PrepareGuide", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { model });
            Assert.That(model.GetComponent<Renderer>().bounds.size.x, Is.EqualTo(.2f).Within(.0001f));
        }
        finally { Object.DestroyImmediate(model); }
    }

    [Test]
    public void WrapperBetweenViewerAndUIIsHiddenButSurfaceBehindUIIsNot()
    {
        var surface = GameObject.CreatePrimitive(PrimitiveType.Cube);
        try
        {
            surface.transform.position = new Vector3(0, 0, .5f);
            surface.transform.localScale = new Vector3(1, 1, .05f);
            Physics.SyncTransforms();
            var collider = surface.GetComponent<Collider>();
            Assert.That(CandySpatialMeshTransformationController.BlocksUIPoint(collider, Vector3.zero, Vector3.forward), Is.True);
            surface.transform.position = new Vector3(0, 0, 2f);
            Physics.SyncTransforms();
            Assert.That(CandySpatialMeshTransformationController.BlocksUIPoint(collider, Vector3.zero, Vector3.forward), Is.False);
        }
        finally { Object.DestroyImmediate(surface); }
    }
}
