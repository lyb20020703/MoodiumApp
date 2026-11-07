using System.Reflection;
using Moodium.Interaction;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class CompletionHouseSoftInteractionTests
{
    [TestCase(0f)]
    [TestCase(20f)]
    public void CandyBurstUsesRealSurfaceContactAndIsThrottledWhileHeld(float tilt)
    {
        CreatePatch("Roof",0f);
        m_House.transform.SetPositionAndRotation(new Vector3(2,0,3),Quaternion.Euler(tilt,35,0));
        m_House.transform.localScale=new Vector3(2,1,3);
        var soft=m_House.AddComponent<CompletionHouseSoftInteraction>();soft.Configure();Physics.SyncTransforms();
        var burstEvent=typeof(CompletionHouseSoftInteraction).GetEvent("ContactBurstRequested");
        Assert.That(burstEvent,Is.Not.Null,"The existing actual surface contact must drive candy emission.");
        var emitted=new System.Collections.Generic.List<Vector3>();
        System.Action<Vector3> handler=point=>emitted.Add(point);burstEvent.AddEventHandler(soft,handler);
        Press(soft,new[]{new Vector3(20,20,20)},1);
        Assert.That(emitted,Is.Empty);
        var fingertips=new[]{m_House.transform.TransformPoint(new Vector3(0,.49f,0)),m_House.transform.TransformPoint(new Vector3(.03f,.49f,0))};
        Press(soft,fingertips,1);
        Assert.That(emitted.Count,Is.EqualTo(1));
        var expected=m_House.transform.TransformPoint(new Vector3(0,.5f,0))+m_House.transform.up*.015f;
        // The existing contact solver casts along six world axes, so a sloped roof
        // can land a few millimetres sideways from the perpendicular projection.
        Assert.That(Vector3.Distance(emitted[0],expected),Is.LessThan(.01f),"Surface plus a small outward offset, not root center or finger depth.");
        Assert.That(Vector3.Dot(emitted[0]-m_House.transform.TransformPoint(new Vector3(0,.5f,0)),m_House.transform.up),
            Is.EqualTo(.015f).Within(.0002f),"Emission offset follows the tilted surface normal.");
        Press(soft,fingertips,20);
        Assert.That(emitted.Count,Is.EqualTo(1),"Multiple fingertips must not create a burst every frame.");
        Press(soft,fingertips,60);
        Assert.That(emitted.Count,Is.InRange(2,3));
        var before=emitted.Count;Press(soft,new Vector3[0],120);
        Assert.That(emitted.Count,Is.EqualTo(before),"No candy after release.");
    }
    GameObject m_House;
    Mesh m_Source;

    [TearDown]
    public void Cleanup()
    {
        if (m_House != null) Object.DestroyImmediate(m_House);
        if (m_Source != null) Object.DestroyImmediate(m_Source);
    }

    MeshFilter CreatePatch(string name, float x)
    {
        if (m_House == null) m_House = new GameObject("House");
        var child = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        child.transform.SetParent(m_House.transform, false);
        child.transform.localPosition = new Vector3(x, 0f, 0f);
        if (m_Source == null)
        {
            m_Source = new Mesh();
            var vertices = new Vector3[122];
            var triangles = new int[600];
            var t = 0;
            for (var z = 0; z <= 10; z++)
            for (var i = 0; i <= 10; i++)
            {
                vertices[z * 11 + i] = new Vector3((i - 5) * .02f, .5f, (z - 5) * .02f);
                if (i == 10 || z == 10) continue;
                var a = z * 11 + i;
                triangles[t++] = a; triangles[t++] = a + 11; triangles[t++] = a + 1;
                triangles[t++] = a + 1; triangles[t++] = a + 11; triangles[t++] = a + 12;
            }
            vertices[121] = Vector3.zero; // A grounded base below the roof, anchored throughout a press.
            m_Source.vertices = vertices;
            m_Source.triangles = triangles;
            m_Source.RecalculateNormals();
        }
        var filter = child.GetComponent<MeshFilter>();
        filter.sharedMesh = m_Source;
        return filter;
    }

    void Press(CompletionHouseSoftInteraction soft, Vector3[] fingers, int frames = 30)
    {
        for (var i = 0; i < frames; i++)
        {
            soft.SetFingerPositions(fingers, fingers.Length);
            soft.StepDeformation(1f / 60f);
        }
    }

    [Test]
    public void PokeIndentsOnlyTheContactNeighborhoodWithoutMovingTheHouse()
    {
        var filter = CreatePatch("Roof", 0f);
        var soft = m_House.AddComponent<CompletionHouseSoftInteraction>();
        soft.Configure();
        Physics.SyncTransforms();
        Press(soft, new[] { new Vector3(0f, .49f, 0f) });
        Assert.That(filter.sharedMesh.vertices[60].y, Is.LessThan(.49f));
        Assert.That(filter.sharedMesh.vertices[0], Is.EqualTo(m_Source.vertices[0]));
        Assert.That(m_House.transform.position, Is.EqualTo(Vector3.zero));
        Assert.That(m_House.transform.localScale, Is.EqualTo(Vector3.one));
        Assert.That(m_Source.vertices[60].y, Is.EqualTo(.5f), "Never edit the asset mesh.");
        Assert.That(filter.sharedMesh.vertices[121], Is.EqualTo(Vector3.zero), "The base stays planted.");
    }

    [Test]
    public void DeeperFingerPenetrationProducesADeeperDent()
    {
        var filter = CreatePatch("Roof", 0f);
        var soft = m_House.AddComponent<CompletionHouseSoftInteraction>();
        soft.Configure();
        Physics.SyncTransforms();
        Press(soft, new[] { new Vector3(0f, .505f, 0f) });
        var shallow = filter.sharedMesh.vertices[60].y;
        Press(soft, new[] { new Vector3(0f, .48f, 0f) });
        Assert.That(filter.sharedMesh.vertices[60].y, Is.LessThan(shallow - .01f));
    }

    [Test]
    public void WorldSpaceContactHandlesRotatedScaledHierarchyAndDisableRestoresMesh()
    {
        var filter = CreatePatch("Roof", 0f);
        m_House.transform.SetPositionAndRotation(new Vector3(2f, 0f, 3f), Quaternion.Euler(0f, 35f, 0f));
        m_House.transform.localScale = new Vector3(2f, 1f, 3f);
        var soft = m_House.AddComponent<CompletionHouseSoftInteraction>();
        soft.Configure();
        Physics.SyncTransforms();
        Press(soft, new[] { m_House.transform.TransformPoint(new Vector3(0f, .49f, 0f)) });
        Assert.That(filter.sharedMesh.vertices[60].y, Is.LessThan(.49f));
        Assert.That(m_House.transform.position, Is.EqualTo(new Vector3(2f, 0f, 3f)));
        Assert.That(m_House.transform.localScale, Is.EqualTo(new Vector3(2f, 1f, 3f)));
        // Invoke the actual cleanup hook: EditMode does not run the play-mode lifecycle automatically.
        typeof(CompletionHouseSoftInteraction).GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(soft, null);
        Assert.That(filter.sharedMesh.vertices[60].y, Is.EqualTo(.5f));
    }

    [TestCase("Assets/prefab/CandyHouse.prefab")]
    [TestCase("Assets/prefab/CandyLittleHouse.prefab")]
    [TestCase("Assets/prefab/Honey Jar.prefab")]
    public void ActualHousePrefabDeformsItsVisibleMeshAtItsAuthoredScale(string path)
    {
        m_House = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
        m_House.transform.localScale *= 3f;
        var soft = m_House.GetComponent<CompletionHouseSoftInteraction>();
        // Honey Jar is upgraded by CandyCompletionHouseController.SpawnHouse at runtime.
        if (soft == null && path == "Assets/prefab/Honey Jar.prefab")
            soft = m_House.AddComponent<CompletionHouseSoftInteraction>();
        Assert.That(soft, Is.Not.Null, "Prefab settings must be editable before entering Play mode.");
        soft.Configure();
        var bursts=new System.Collections.Generic.List<Vector3>();
        soft.ContactBurstRequested+=point=>bursts.Add(point);
        Physics.SyncTransforms();
        var renderer = m_House.GetComponentInChildren<MeshRenderer>();
        var surface = m_House.GetComponentInChildren<MeshCollider>();
        var ray = new Ray(renderer.bounds.center + Vector3.up * 10f, Vector3.down);
        Assert.That(surface.Raycast(ray, out var hit, 20f), Is.True);
        var filter = m_House.GetComponentInChildren<MeshFilter>();
        var original = filter.sharedMesh.vertices;
        var position = m_House.transform.position;
        var scale = m_House.transform.localScale;
        var timer = System.Diagnostics.Stopwatch.StartNew();
        Press(soft, new[] { hit.point - hit.normal * .015f });
        Assert.That(bursts.Count,Is.EqualTo(1));
        Assert.That(Vector3.Distance(bursts[0],hit.point),Is.LessThan(.04f),"Real assets emit at the touched surface, not their pivot.");
        timer.Stop();
        var deformed = filter.sharedMesh.vertices;
        var displaced = 0;
        for (var i = 0; i < deformed.Length; i++)
            if (filter.transform.TransformVector(deformed[i] - original[i]).magnitude > .003f) displaced++;
        Assert.That(displaced, Is.GreaterThan(0), "The actual render mesh must respond, not just its Transform.");
        Assert.That(displaced, Is.LessThan(original.Length / 4), "Deformation must stay local.");
        Assert.That(m_House.transform.position, Is.EqualTo(position));
        Assert.That(m_House.transform.localScale, Is.EqualTo(scale));
        Debug.Log($"[House mesh audit] {path}: affected={displaced}/{original.Length}; 30 deformation steps={timer.ElapsedMilliseconds}ms (Editor CPU, not device rendering)");
    }

    [Test]
    public void TwoFingerContactsDeformBothChildMeshesAndRecoverAfterRelease()
    {
        var left = CreatePatch("Left", -.3f);
        var right = CreatePatch("Right", .3f);
        var soft = m_House.AddComponent<CompletionHouseSoftInteraction>();
        soft.Configure();
        Physics.SyncTransforms();
        Press(soft, new[] { new Vector3(-.3f, .49f, 0f), new Vector3(.3f, .49f, 0f) });
        Assert.That(left.sharedMesh.vertices[60].y, Is.LessThan(.49f));
        Assert.That(right.sharedMesh.vertices[60].y, Is.LessThan(.49f));
        Press(soft, new Vector3[0], 180);
        Assert.That(left.sharedMesh.vertices[60].y, Is.EqualTo(.5f).Within(.0001f));
        Assert.That(right.sharedMesh.vertices[60].y, Is.EqualTo(.5f).Within(.0001f));
    }

    [TestCase("Assets/prefab/CandyHouse.prefab")]
    [TestCase("Assets/prefab/CandyLittleHouse.prefab")]
    public void HouseAssetsProvideReadableMeshesForDeviceDeformation(string path)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Assert.That(prefab, Is.Not.Null);
        var filters = prefab.GetComponentsInChildren<MeshFilter>(true);
        Assert.That(filters.Length, Is.GreaterThan(0));
        foreach (var filter in filters)
        {
            Assert.That(filter.sharedMesh, Is.Not.Null, filter.name);
            Assert.That(filter.sharedMesh.isReadable, Is.True, filter.name);
            Debug.Log($"[House mesh audit] {path}/{filter.name}: vertices={filter.sharedMesh.vertexCount}, bounds={filter.sharedMesh.bounds.size}, scale={filter.transform.lossyScale}");
        }
    }
}
