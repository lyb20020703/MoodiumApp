using System.Collections;
using System.Reflection;
using Moodium.NatureWorld;
using NUnit.Framework;
using UnityEngine;

public sealed class BubbleWorldNestedEntryTests
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
    static object Get(object target, string name) => target.GetType().GetField(name, Private).GetValue(target);

    [Test]
    public void OuterCloudFadesOnlyBetweenOneAndFourMetres()
    {
        Assert.That(BubbleWorldController.OuterAlpha(.08f), Is.EqualTo(1));
        Assert.That(BubbleWorldController.OuterAlpha(1), Is.EqualTo(1));
        float last = 1;
        for (float r = 1; r <= 4; r += .1f)
        {
            float alpha = BubbleWorldController.OuterAlpha(r);
            Assert.That(alpha, Is.LessThanOrEqualTo(last));
            last = alpha;
        }
        Assert.That(BubbleWorldController.OuterAlpha(4), Is.Zero);
        Assert.That(BubbleWorldController.OuterAlpha(10.5f), Is.Zero);
    }

    [Test]
    public void PortalApproachKeepsOriginalPoseRadiusAndOuterVisuals()
    {
        var host = new GameObject("Outer");
        var child = new GameObject("Inner"); child.transform.SetParent(host.transform);
        var cameraObject = new GameObject("Viewer");
        var outerVisual = new GameObject("Outer visual");
        var innerVisual = new GameObject("Inner visual");
        var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        try
        {
            var outer = host.AddComponent<BubbleWorldController>();
            var inner = child.AddComponent<BubbleWorldController>();
            var camera = cameraObject.AddComponent<Camera>();
            Set(outer, "m_Root", outerVisual);
            Set(outer, "m_OuterEntryFrom", new Vector3(1, .7f, 2));
            Set(inner, "m_Viewer", camera);
            Set(inner, "m_Root", innerVisual);
            Set(inner, "m_WorldRoot", innerVisual.transform);
            Set(inner, "m_Membrane", innerVisual.transform);
            Set(inner, "m_MembraneCollider", innerVisual.AddComponent<SphereCollider>());
            Set(inner, "m_MembraneMaterial", material);
            Set(inner, "m_Phase", BubbleWorldController.Phase.Portal);
            Set(inner, "m_Radius", .08f);
            innerVisual.transform.position = new Vector3(.2f, 1.1f, .6f);
            var start = innerVisual.transform.position;
            // Step the shared entry animation directly: t=0 must not jump.
            var routine = (IEnumerator)typeof(BubbleWorldController).GetMethod("ApproachAndReveal", Private).Invoke(inner, new object[] { camera });
            Assert.That(routine.MoveNext(), Is.True);
            Assert.That(Vector3.Distance(start, innerVisual.transform.position), Is.LessThan(.001f));
            Assert.That((float)Get(inner, "m_Radius"), Is.EqualTo(.08f).Within(.001f));
            Assert.That(outerVisual.activeSelf, Is.True);
            Assert.That((float)Get(inner, "m_NestedSkyScale") * BubbleWorldController.SkyMaxRadius, Is.LessThan(BubbleWorldController.SkyMaxRadius));
            inner.EnterFromPortal();
            Assert.That(inner.CurrentPhase, Is.EqualTo(BubbleWorldController.Phase.Approach));
            Assert.That((Vector3)Get(inner, "m_From"), Is.EqualTo(new Vector3(1, .7f, 2)));
            Assert.That(Vector3.Distance(start, innerVisual.transform.position), Is.LessThan(.001f));
            inner.StopAllCoroutines();
        }
        finally
        {
            // Cleanup is deferred by the production controller; prevent duplicate ownership in this synthetic fixture.
            var outer = host.GetComponent<BubbleWorldController>(); Set(outer, "m_Root", null);
            var inner = child.GetComponent<BubbleWorldController>(); Set(inner, "m_Root", null); Set(inner, "m_MembraneMaterial", null);
            Object.DestroyImmediate(host); Object.DestroyImmediate(cameraObject);
            Object.DestroyImmediate(outerVisual); Object.DestroyImmediate(innerVisual); Object.DestroyImmediate(material);
        }
    }
}
