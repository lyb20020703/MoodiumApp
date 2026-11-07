using Moodium.NatureWorld;
using NUnit.Framework;
using UnityEngine;

public sealed class NatureCompletionPanoramaControllerTests
{
    [Test]
    public void PanoramaOnlyDisplaysOnceNatureProgressIsComplete()
    {
        Assert.That(NatureCompletionPanoramaController.ShouldDisplay(.998f), Is.False);
        Assert.That(NatureCompletionPanoramaController.ShouldDisplay(.999f), Is.True);
        Assert.That(NatureCompletionPanoramaController.ShouldDisplay(1f), Is.True);
    }

    [Test]
    public void PanoramaUsesTheBackgroundRenderQueueSoForegroundEffectsRemainVisible()
    {
        Assert.That(NatureCompletionPanoramaController.BackgroundRenderQueue, Is.EqualTo(1000));
    }

    [Test]
    public void PanoramaUsesThePolySpatialSupportedUnlitShader()
    {
        Assert.That(NatureCompletionPanoramaController.PanoramaFeatherShaderName,
            Is.EqualTo("Universal Render Pipeline/Unlit"));
    }

    [Test]
    public void PanoramaEdgeAlphaFadesFromTheCutEdgeIntoTheDome()
    {
        Assert.That(NatureCompletionPanoramaController.GetEdgeFeatherAlpha(0f, 0.8f), Is.EqualTo(0f));
        Assert.That(NatureCompletionPanoramaController.GetEdgeFeatherAlpha(0.4f, 0.8f), Is.EqualTo(.5f).Within(.001f));
        Assert.That(NatureCompletionPanoramaController.GetEdgeFeatherAlpha(1.2f, 0.8f), Is.EqualTo(1f));
    }

    [Test]
    public void PanoramaUsesTheStaticBlenderDomeInsteadOfAGeneratedRuntimeMesh()
    {
        Assert.That(NatureCompletionPanoramaController.DomeMeshResourcePath,
            Is.EqualTo("Moodium/NaturePanoramaDome"));
    }

    [Test]
    public void ImportedDomeRotatesItsCutAxisToWorldUp()
    {
        var importedBounds = new Bounds(new Vector3(0f, 0f, .5f), new Vector3(2f, 2f, 1f));
        var rotation = NatureCompletionPanoramaController.GetDomeRotation(importedBounds);

        Assert.That(Vector3.Angle(rotation * Vector3.forward, Vector3.up), Is.LessThan(.01f));
    }

    [Test]
    public void ImportedDomeScaleProducesTheConfiguredWorldRadius()
    {
        var importedBounds = new Bounds(new Vector3(0f, 0f, .05f), new Vector3(.2f, .2f, .1f));

        Assert.That(NatureCompletionPanoramaController.GetDomeUniformScale(importedBounds, 12f),
            Is.EqualTo(120f).Within(.001f));
    }

    [Test]
    public void PanoramaStaysAtItsCreationPositionWhenViewerMoves()
    {
        var host = new GameObject("Nature Panorama Test Host");
        var cameraObject = new GameObject("Nature Panorama Test Camera");
        var camera = cameraObject.AddComponent<Camera>();
        var texture = new Texture2D(2, 2);
        var controller = host.AddComponent<NatureCompletionPanoramaController>();

        try
        {
            cameraObject.transform.position = new Vector3(1f, 2f, 3f);
            controller.Configure(camera, texture);
            controller.StartDisplay();
            controller.SetProgress(1f);

            var panorama = GameObject.Find("Nature Completion Panorama Background");
            Assert.That(panorama, Is.Not.Null);
            var creationPosition = panorama.transform.position;

            cameraObject.transform.position = new Vector3(4f, 5f, 6f);
            controller.SendMessage("LateUpdate", SendMessageOptions.DontRequireReceiver);

            Assert.That(Vector3.Distance(panorama.transform.position, creationPosition), Is.LessThan(.0001f));
        }
        finally
        {
            Object.DestroyImmediate(host);
            Object.DestroyImmediate(cameraObject);
            Object.DestroyImmediate(texture);
            var panorama = GameObject.Find("Nature Completion Panorama Background");
            if (panorama != null)
                Object.DestroyImmediate(panorama);
        }
    }
}
