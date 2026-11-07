using Moodium.Reality;
using NUnit.Framework;
using UnityEngine;

public sealed class CandyWrapperContactFeedbackTests
{
    [Test]
    public void FoilContactMeshIsSmallAndHasActualRaisedCreases()
    {
        var mesh = CandyWrapperContactFeedback.BuildFoldMesh();
        try
        {
            Assert.That(mesh.vertexCount, Is.EqualTo(108));
            Assert.That(mesh.triangles.Length / 3, Is.EqualTo(36));
            Assert.That(mesh.bounds.size.z, Is.GreaterThan(0f));
            foreach (var vertex in mesh.vertices)
            {
                Assert.That(float.IsNaN(vertex.x) || float.IsNaN(vertex.y) || float.IsNaN(vertex.z), Is.False);
                Assert.That(vertex.magnitude, Is.LessThan(1.01f));
            }
        }
        finally { Object.DestroyImmediate(mesh); }
    }
}
