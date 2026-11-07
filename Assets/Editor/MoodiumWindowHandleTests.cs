using Moodium.Flow;
using NUnit.Framework;
using UnityEngine;

public sealed class MoodiumWindowHandleTests
{
    GameObject m_Camera, m_Window, m_Panel;
    MoodiumWindowHandle m_Handle;

    [SetUp]
    public void SetUp()
    {
        m_Camera = new GameObject("Viewer", typeof(Camera)) { tag = "MainCamera" };
        m_Camera.transform.position = new Vector3(0f, 1.4f, 0f);
        // Same set-up as the opening video: 1.35 m ahead, non-uniform scale, handle as a child.
        m_Window = new GameObject("Opening Video");
        m_Window.transform.SetPositionAndRotation(new Vector3(0f, 1.15f, 1.35f), Quaternion.identity);
        m_Window.transform.localScale = new Vector3(1.28f, .72f, 1f);
        var handle = new GameObject("Handle");
        handle.transform.SetParent(m_Window.transform, false);
        m_Handle = handle.AddComponent<MoodiumWindowHandle>();
        m_Handle.Configure(m_Window.transform);
        m_Handle.MinDistanceFromViewer = MoodiumAppFlowController.OpeningWindowMinDistance;
        m_Panel = new GameObject("Mood Panel");
        var pose = MoodiumAppFlowController.PanelPoseInFrontOfVideo(m_Window.transform, m_Camera.transform.position,
            MoodiumAppFlowController.OpeningPanelClearance);
        m_Panel.transform.SetPositionAndRotation(pose.position, pose.rotation);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(m_Panel);
        Object.DestroyImmediate(m_Window);
        Object.DestroyImmediate(m_Camera);
    }

    [Test]
    public void PanelStandsInFrontOfTheVideoFacingTheSameWay()
    {
        var local = MoodiumWindowHandle.RelativePose(m_Window.transform, m_Panel.transform);
        Assert.That(-local.position.z, Is.EqualTo(MoodiumAppFlowController.OpeningPanelClearance).Within(1e-4f));
        Assert.That(Quaternion.Angle(m_Panel.transform.rotation, m_Window.transform.rotation), Is.LessThan(.01f));
        Assert.That(m_Panel.transform.position.y, Is.EqualTo(1.4f - .12f).Within(1e-4f));
    }

    [Test]
    public void DraggingTheVideoCarriesTheFollowersWithoutDistortion()
    {
        m_Handle.AddFollower(m_Panel.transform);
        var before = MoodiumWindowHandle.RelativePose(m_Window.transform, m_Panel.transform);
        var grab = m_Handle.transform.position;
        m_Handle.PointerStarted(1, grab);
        m_Handle.PointerMoved(1, grab + new Vector3(.6f, .1f, .2f));   // to the right: the window also turns
        m_Handle.PointerEnded(1);
        Assert.That(Quaternion.Angle(m_Window.transform.rotation, Quaternion.identity), Is.GreaterThan(5f), "window turned");
        var after = MoodiumWindowHandle.RelativePose(m_Window.transform, m_Panel.transform);
        Assert.That((after.position - before.position).magnitude, Is.LessThan(1e-4f));
        Assert.That(Quaternion.Angle(after.rotation, before.rotation), Is.LessThan(.01f));
    }

    [Test]
    public void TheVideoCannotBePulledIntoTheViewersFace()
    {
        var grab = m_Handle.transform.position;
        m_Handle.PointerStarted(1, grab);
        m_Handle.PointerMoved(1, grab + new Vector3(0f, 0f, -1.2f));
        var flat = Vector3.ProjectOnPlane(m_Window.transform.position - m_Camera.transform.position, Vector3.up);
        Assert.That(flat.magnitude, Is.GreaterThanOrEqualTo(MoodiumAppFlowController.OpeningWindowMinDistance - 1e-4f));
    }

    [Test]
    public void DestroyedFollowersAreDropped()
    {
        m_Handle.AddFollower(m_Panel.transform);
        Object.DestroyImmediate(m_Panel);
        m_Handle.PointerStarted(1, m_Handle.transform.position);
        Assert.That(m_Handle.Followers.Count, Is.Zero);
    }
}
