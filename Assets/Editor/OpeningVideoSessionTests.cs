using Moodium.Opening;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Video;

public sealed class OpeningVideoSessionTests
{
    GameObject m_Root;

    [SetUp]
    public void SetUp()
    {
        m_Root = new GameObject("Opening Video Session Test Root");
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(m_Root);
    }

    [Test]
    public void SetVisible_HidesAndShowsDisplayWithoutDisposingSession()
    {
        var player = m_Root.AddComponent<VideoPlayer>();
        var display = new GameObject("Display");
        display.transform.SetParent(m_Root.transform);
        var session = new OpeningVideoSession(player, display.transform, null, english: true, () => { });

        session.SetVisible(false);

        Assert.That(display.activeSelf, Is.False);
        Assert.That(session.IsDisposed, Is.False);

        session.SetVisible(true);

        Assert.That(display.activeSelf, Is.True);
        Assert.That(session.IsDisposed, Is.False);
    }

    [Test]
    public void DisposeOnce_InvokesTeardownOnlyOnce()
    {
        var calls = 0;
        var session = new OpeningVideoSession(m_Root.AddComponent<VideoPlayer>(), m_Root.transform, null, false, () => calls++);

        session.DisposeOnce();
        session.DisposeOnce();

        Assert.That(session.IsDisposed, Is.True);
        Assert.That(calls, Is.EqualTo(1));
    }
}
