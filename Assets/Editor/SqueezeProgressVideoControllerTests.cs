using Moodium.Opening;
using Moodium.Reality;
using NUnit.Framework;
using UnityEngine;

public sealed class SqueezeProgressVideoControllerTests
{
    [TestCase("Assets/Video/CandySqueezeAlpha.mp4")]
    [TestCase("Assets/Video/newnaturesqueeze.mp4")]
    public void SqueezeVideoDisablesEmbeddedAudio(string path)
    {
        var root=new GameObject("Muted squeeze video test");
        var controller=root.AddComponent<SqueezeProgressVideoController>();
        var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        try
        {
            var clip=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Video.VideoClip>(path);
            Assert.That(clip,Is.Not.Null,path);
            typeof(SqueezeProgressVideoController).GetMethod("CreateDisplay",flags).Invoke(controller,new object[]{clip});
            var player=(UnityEngine.Video.VideoPlayer)typeof(SqueezeProgressVideoController).GetField("m_Player",flags).GetValue(controller);
            Assert.That(player.audioOutputMode,Is.EqualTo(UnityEngine.Video.VideoAudioOutputMode.None));
            // VideoClip sources report the clip's own track count even with no output.
            // None is the output guarantee; track count is not a mute state.
        }
        finally
        {
            // The runtime controller uses deferred Destroy; explicitly dispose its owned objects in EditMode.
            foreach(var name in new[]{"m_Display","m_RenderTexture","m_Material","m_DomeMaterial","m_DiscMesh","m_DomeMesh"})
            {
                var field=typeof(SqueezeProgressVideoController).GetField(name,flags);
                var owned=field.GetValue(controller) as Object;
                if(owned!=null)Object.DestroyImmediate(owned);
                field.SetValue(controller,null);
            }
            Object.DestroyImmediate(root);
        }
    }
    [TestCase(0f, 12d, 0d)]
    [TestCase(0.25f, 12d, 3d)]
    [TestCase(0.5f, 12d, 6d)]
    [TestCase(1f, 12d, 12d)]
    [TestCase(1.5f, 12d, 12d)]
    public void ProgressToVideoTime_ClampsAndMapsNormalizedProgress(float progress, double duration, double expected)
    {
        Assert.That(SqueezeProgressVideoController.ProgressToVideoTime(progress, duration),
            Is.EqualTo(expected).Within(0.0001d));
    }

    [Test]
    public void HasReachedTarget_UsesSmallToleranceBeforePausing()
    {
        Assert.That(SqueezeProgressVideoController.HasReachedTarget(4.995d, 5d, 0.01d), Is.True);
        Assert.That(SqueezeProgressVideoController.HasReachedTarget(4.8d, 5d, 0.01d), Is.False);
    }

    [Test]
    public void UprightFacingRotation_IgnoresCameraPitchAndKeepsVideoVertical()
    {
        var rotation = OpeningVideoDisplayPlacement.BuildUprightFacingRotation(new Vector3(0.2f, -0.65f, 0.73f));

        Assert.That(Vector3.Angle(rotation * Vector3.up, Vector3.up), Is.LessThan(0.01f));
        Assert.That(Vector3.Dot((rotation * Vector3.forward).normalized,
            new Vector3(0.2f, 0f, 0.73f).normalized), Is.GreaterThan(0.999f));
    }

    [Test]
    public void SqueezeVideoUsesAForegroundRenderQueue()
    {
        Assert.That(SqueezeProgressVideoController.ForegroundRenderQueue, Is.GreaterThan(4000));
    }

    [Test]
    public void SqueezeVideoPlacementPrefersTheLiveMainCameraOverTheCachedCamera()
    {
        var cached = new GameObject("Cached squeeze camera").AddComponent<Camera>();
        var live = new GameObject("Live squeeze camera").AddComponent<Camera>();

        Assert.That(SqueezeProgressVideoController.SelectPlacementCamera(live, cached), Is.SameAs(live));

        Object.DestroyImmediate(cached.gameObject);
        Object.DestroyImmediate(live.gameObject);
    }

    [Test]
    public void CircularVideoUvCenterCropsAWideClipWithoutStretchingIt()
    {
        var center = SqueezeProgressVideoController.MapDiscPointToVideoUv(Vector2.zero, 1920, 1080);
        Assert.That(center.x, Is.EqualTo(.5f).Within(.0001f));
        Assert.That(center.y, Is.EqualTo(.5f).Within(.0001f));
        Assert.That(SqueezeProgressVideoController.MapDiscPointToVideoUv(Vector2.right, 1920, 1080).x,
            Is.EqualTo(.78125f).Within(.0001f));
        Assert.That(SqueezeProgressVideoController.MapDiscPointToVideoUv(Vector2.up, 1920, 1080).y,
            Is.EqualTo(1f).Within(.0001f));
    }

    [Test]
    public void SurfaceRotationPlacesTheCircularVideoPlaneAgainstTheTrackedSurface()
    {
        var normal = new Vector3(.1f, .98f, .16f).normalized;
        var rotation = SqueezeProgressVideoController.BuildSurfaceRotation(normal);

        Assert.That(Vector3.Angle(rotation * Vector3.up, normal), Is.LessThan(.01f));
    }

    [Test]
    public void SurfacePositionUsesTrackingCenterAndPlacesVideoAboveTheSoftModel()
    {
        var position = SqueezeProgressVideoController.BuildSurfacePosition(
            new Vector3(.2f, .3f, -.4f), .37f, Vector3.up, .005f);

        Assert.That(position.x, Is.EqualTo(.2f).Within(.0001f));
        Assert.That(position.y, Is.EqualTo(.375f).Within(.0001f));
        Assert.That(position.z, Is.EqualTo(-.4f).Within(.0001f));
    }

    [Test]
    public void SurfacePositionAllowsANegativeOffsetToLowerTheVideoAndDomeIntoTheSoft()
    {
        var position = SqueezeProgressVideoController.BuildSurfacePosition(
            new Vector3(.2f, .3f, -.4f), .37f, Vector3.up, -.005f);

        Assert.That(position.y, Is.EqualTo(.365f).Within(.0001f));
    }
}
