using Moodium.MoodCheck;
using NUnit.Framework;
using UnityEngine;

public sealed class MoodCheckPlateTests
{
    GameObject m_Parent;
    GameObject m_PlateObject;
    MoodCheckPlate m_Plate;
    MoodCheckPlateHandle m_Handle;

    [SetUp]
    public void SetUp()
    {
        m_Parent = new GameObject("Scaled Parent");
        m_Parent.transform.position = new Vector3(1f, 2f, 3f);
        m_Parent.transform.rotation = Quaternion.Euler(10f, 35f, 20f);
        m_Parent.transform.localScale = new Vector3(2f, .5f, 3f);
        m_PlateObject = new GameObject("Plate");
        m_PlateObject.transform.SetParent(m_Parent.transform, false);
        m_Plate = m_PlateObject.AddComponent<MoodCheckPlate>();
        m_Plate.Configure(.112f);
        m_Handle = m_PlateObject.AddComponent<MoodCheckPlateHandle>();
        m_Handle.Configure(m_Plate);
    }

    [TearDown]
    public void TearDown() => Object.DestroyImmediate(m_Parent);

    [Test]
    public void WorldPoint_IsConvertedThroughPlateTransformAndDropsLocalZ()
    {
        var world = m_PlateObject.transform.TransformPoint(new Vector3(.056f, -.112f, .8f));
        m_Handle.PointerStarted(7, world);

        Assert.That(m_Plate.NormalizedPosition.x, Is.EqualTo(.4472136f).Within(.0001f));
        Assert.That(m_Plate.NormalizedPosition.y, Is.EqualTo(-.8944272f).Within(.0001f));
    }

    [Test]
    public void OutsidePoint_ClampsToUnitCircleAndCenterRemainsZero()
    {
        m_Handle.PointerStarted(1, m_PlateObject.transform.TransformPoint(new Vector3(.224f, 0f, 0f)));
        Assert.That(m_Plate.NormalizedPosition.x, Is.EqualTo(1f).Within(.0001f));
        Assert.That(m_Plate.NormalizedPosition.y, Is.Zero.Within(.0001f));
        m_Handle.PointerMoved(1, m_PlateObject.transform.position);
        Assert.That(m_Plate.NormalizedPosition, Is.EqualTo(Vector2.zero));
        Assert.That(m_Plate.CurrentMood, Is.EqualTo(MoodKind.Neutral));
    }

    [Test]
    public void CapturedPointer_CannotBeStolenOrEndedByAnotherPointer()
    {
        m_Handle.PointerStarted(1, m_PlateObject.transform.TransformPoint(new Vector3(.056f, 0f, 0f)));
        m_Handle.PointerStarted(2, m_PlateObject.transform.TransformPoint(new Vector3(-.056f, 0f, 0f)));
        m_Handle.PointerMoved(2, m_PlateObject.transform.TransformPoint(new Vector3(-.112f, 0f, 0f)));
        m_Handle.PointerEnded(2, Vector3.zero);

        Assert.That(m_Handle.ActivePointerId, Is.EqualTo(1));
        Assert.That(m_Plate.NormalizedPosition.x, Is.EqualTo(.5f).Within(.0001f));
        m_Handle.CancelPointer(1);
        Assert.That(m_Handle.ActivePointerId, Is.EqualTo(-1));
        Assert.That(m_Plate.IsDragging, Is.False);
    }

    [Test]
    public void ValidRelease_FiresOnceWithFinalPosition()
    {
        var releases = 0;
        var releasedPosition = Vector2.zero;
        m_Handle.Released += position => { releases++; releasedPosition = position; };
        m_Handle.PointerStarted(3, m_PlateObject.transform.position);
        m_Handle.PointerEnded(3, m_PlateObject.transform.TransformPoint(new Vector3(0f, .112f, 0f)));
        m_Handle.PointerEnded(3, m_PlateObject.transform.position);

        Assert.That(releases, Is.EqualTo(1));
        Assert.That(releasedPosition.x, Is.Zero.Within(.0001f));
        Assert.That(releasedPosition.y, Is.EqualTo(1f).Within(.0001f));
    }
}
