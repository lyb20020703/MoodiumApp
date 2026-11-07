using Moodium.UI;
using NUnit.Framework;
using UnityEngine;

public sealed class GlassSlideToEnterTests
{
    const float Width = 0.28f;
    const float Height = 0.068f;

    [Test]
    public void Knob_StartsAtTheLeftEndAndEndsAtTheRightEnd()
    {
        var knob = GlassSlideToEnter.KnobDiameter(Height);
        var margin = (Height - knob) * 0.5f;
        Assert.That(GlassSlideToEnter.KnobCenterX(0f, Width, Height), Is.EqualTo(-Width * 0.5f + margin + knob * 0.5f).Within(1e-5f));
        Assert.That(GlassSlideToEnter.KnobCenterX(1f, Width, Height), Is.EqualTo(Width * 0.5f - margin - knob * 0.5f).Within(1e-5f));
    }

    [Test]
    public void Progress_IsTheInverseOfKnobPosition()
    {
        for (var p = 0f; p <= 1f; p += 0.25f)
        {
            var x = GlassSlideToEnter.KnobCenterX(p, Width, Height);
            Assert.That(GlassSlideToEnter.ComputeProgress(x, Width, Height), Is.EqualTo(p).Within(1e-4f));
        }
    }

    [Test]
    public void Progress_IsClampedOutsideTheTrack()
    {
        Assert.That(GlassSlideToEnter.ComputeProgress(-1f, Width, Height), Is.Zero);
        Assert.That(GlassSlideToEnter.ComputeProgress(1f, Width, Height), Is.EqualTo(1f));
    }

    [Test]
    public void RoundedRect_FacesTheViewerAndCoversTheRequestedSize()
    {
        var mesh = new Mesh();
        GlassSlideToEnter.BuildRoundedRect(mesh, Width, Height, Height * 0.5f, 10);
        Assert.That(mesh.bounds.size.x, Is.EqualTo(Width).Within(1e-4f));
        Assert.That(mesh.bounds.size.y, Is.EqualTo(Height).Within(1e-4f));
        foreach (var normal in mesh.normals)
            Assert.That(normal, Is.EqualTo(Vector3.back));
        var t = mesh.triangles;
        var v = mesh.vertices;
        var faceNormal = Vector3.Cross(v[t[1]] - v[t[0]], v[t[2]] - v[t[0]]);
        Assert.That(faceNormal.z, Is.LessThan(0f), "Front faces must point to -Z (towards the viewer).");
        Object.DestroyImmediate(mesh);
    }

    [Test]
    public void KnobGlow_IsRenderedBehindTheGlassTrack()
    {
        var root = new GameObject("Glass Slider Test");
        var slider = root.AddComponent<GlassSlideToEnter>();
        slider.Configure("Enter", GlassSlideToEnter.Theme.Nature, null);

        var glow = root.transform.Find("Knob Glow");
        var tint = root.transform.Find("Tint");
        Assert.That(glow, Is.Not.Null);
        Assert.That(tint, Is.Not.Null);
        Assert.That(glow.localPosition.z, Is.GreaterThan(tint.localPosition.z),
            "The glow must stay behind the glass track instead of covering its capsule surface.");

        Object.DestroyImmediate(root);
    }
}
