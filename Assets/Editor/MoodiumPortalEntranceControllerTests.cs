using System.Reflection;
using Moodium.Opening;
using NUnit.Framework;
using UnityEngine;

public sealed class MoodiumPortalEntranceControllerTests
{
    GameObject m_Root;

    [TearDown]
    public void TearDown()
    {
        if (m_Root != null)
            Object.DestroyImmediate(m_Root);
    }

    [Test]
    public void ContactIsAcceptedOnlyOnceAfterOpening()
    {
        m_Root = new GameObject("Portal Test");
        var model = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        model.transform.SetParent(m_Root.transform, false);
        var controller = m_Root.AddComponent<MoodiumPortalEntranceController>();
        SetField(controller, "m_EntryModelRoot", model.transform);

        controller.BeginOpeningForTests();
        Assert.That(controller.TryAcceptContact(model.transform.position), Is.False);
        controller.NotifyOpeningCompleteForTests();
        Assert.That(controller.TryAcceptContact(model.transform.position), Is.True);
        Assert.That(controller.TryAcceptContact(model.transform.position), Is.False);
    }

    [Test]
    public void RendererBoundsProvideContactVolume()
    {
        m_Root = new GameObject("Portal Bounds Test");
        var model = GameObject.CreatePrimitive(PrimitiveType.Cube);
        model.transform.SetParent(m_Root.transform, false);
        model.transform.localScale = new Vector3(0.3f, 0.4f, 0.2f);
        var controller = m_Root.AddComponent<MoodiumPortalEntranceController>();
        SetField(controller, "m_EntryModelRoot", model.transform);

        Assert.That(controller.TryGetEntryBounds(out var bounds), Is.True);
        Assert.That(bounds.Contains(model.transform.position), Is.True);
        Assert.That(bounds.size.x, Is.GreaterThan(0f));
    }

    [Test]
    public void EntryTrailUsesLooseParticlesWithoutARibbonRenderer()
    {
        m_Root = new GameObject("Portal Trail Test");
        var model = new GameObject("Moodi");
        model.transform.SetParent(m_Root.transform, false);
        var controller = m_Root.AddComponent<MoodiumPortalEntranceController>();
        SetField(controller, "m_EntryModelRoot", model.transform);

        var configure = typeof(MoodiumPortalEntranceController).GetMethod(
            "ConfigureEntryTrail", BindingFlags.Instance | BindingFlags.NonPublic);
        configure.Invoke(controller, null);

        Assert.That(model.GetComponent<TrailRenderer>(), Is.Null);
        Assert.That(model.transform.Find("Moodi Loose Trail Particles"), Is.Not.Null);
    }

    static void SetField<T>(MoodiumPortalEntranceController target, string name, T value)
    {
        var field = typeof(MoodiumPortalEntranceController).GetField(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null);
        field.SetValue(target, value);
    }
}
