#if MOODIUM_IVY_STUDIO
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Artngame.TreeGEN.ProceduralIvy;
using Moodium.NatureWorld;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Nature world vines on Vision Pro: Ivy Studio no longer hides its own branches, vines wrap over edges,
/// a fingertip touch plants them, leaves unfold along the stem, and a one-line hint explains it.
/// </summary>
public sealed class NatureVineTouchTests
{
    const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
    GameObject m_Root, m_GeneratorObject, m_Table;

    [SetUp]
    public void SetUp()
    {
        m_Root = new GameObject("Nature Vine Test Root");
        m_GeneratorObject = new GameObject("Ivy Generator");
        m_GeneratorObject.transform.SetParent(m_Root.transform);
        m_GeneratorObject.AddComponent<MeshFilter>();
        m_GeneratorObject.AddComponent<MeshRenderer>();
        var generator = m_GeneratorObject.AddComponent<IvyGeneratorTREANT>();
        generator.addLeaves = false;
        generator.addColliderPerIvy = false;
        generator.growIvy = false;
        generator.singleMeshBranch = false;
        generator.use3dbranch = true;
        generator.branch3dDivisions = NatureIvySpatialGrowthController.VineDivisions;
        generator.branch3dSize = NatureIvySpatialGrowthController.VineRadius;
        generator.narrowFactor = NatureIvySpatialGrowthController.VineTipNarrow;
        generator.branchPartLength = NatureIvySpatialGrowthController.VineStep;
        generator.branchCount = 1;
        generator.maxBranchPositions = 20;
        generator.zigZagPower = 0f;
        generator.directionality = new Vector4(1f, 0f, 0f, 0f);   // straight toward the +x edge
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit == null) lit = Shader.Find("Standard");
        generator.branchMaterial = new Material(lit);

        // A 60 x 30 x 60 cm "table" whose top is at y = 0, standing in for the scanned room mesh.
        m_Table = GameObject.CreatePrimitive(PrimitiveType.Cube);
        m_Table.name = "Room Mesh Table";
        m_Table.transform.SetParent(m_Root.transform);
        m_Table.transform.position = new Vector3(0f, -.15f, 0f);
        m_Table.transform.localScale = new Vector3(.6f, .3f, .6f);
        Physics.SyncTransforms();
    }

    [TearDown]
    public void TearDown()
    {
        if (m_GeneratorObject != null)
        {
            var material = m_GeneratorObject.GetComponent<IvyGeneratorTREANT>()?.branchMaterial;
            if (material != null) Object.DestroyImmediate(material);
        }
        if (m_Root != null) Object.DestroyImmediate(m_Root);
    }

    NatureIvySpatialGrowthController Planter(int unattached = NatureIvySpatialGrowthController.VineUnattachedSteps)
    {
        var controller = m_Root.AddComponent<NatureIvySpatialGrowthController>();
        var generator = m_GeneratorObject.GetComponent<IvyGeneratorTREANT>();
        controller.Configure(generator, 10);
        generator.maxUnattachedSteps = unattached;
        controller.StartGrowth();
        m_Root.AddComponent<Moodium.Flow.WorldJourneyDirector>().Rescued();
        controller.SetProgress(1f);
        var colliders = (HashSet<Collider>)typeof(NatureIvySpatialGrowthController).GetField("m_SpatialColliders", Flags).GetValue(controller);
        colliders.Add(m_Table.GetComponent<Collider>());
        return controller;
    }

    RaycastHit TableHit(Vector3 above)
    {
        Assert.That(Physics.Raycast(above, Vector3.down, out var hit, 1f), Is.True);
        return hit;
    }

    List<MeshFilter> Branches() => m_GeneratorObject.GetComponentsInChildren<MeshFilter>(true)
        .Where(f => f.gameObject != m_GeneratorObject && f.sharedMesh != null && f.sharedMesh.triangles.Length > 0).ToList();

    [Test]
    public void IvyStudioNoLongerBatchesAndHidesTheBranches()
    {
        var generator = m_GeneratorObject.GetComponent<IvyGeneratorTREANT>();
        generator.maxFlowerPerHolder = 1;   // the device prefab's value: the batcher fired after every stroke
        var controller = Planter();
        Assert.That(generator.disableRuntimeBatching, Is.True);
        Assert.That(controller.TryGrowAtSpatialHit(TableHit(new Vector3(0f, .2f, 0f))), Is.True);
        Assert.That(m_GeneratorObject.GetComponentsInChildren<Artngame.INfiniDy.ControlCombineChildrenINfiniDyGrassLAND>(true), Is.Empty);
        var renderers = Branches().Select(f => f.GetComponent<MeshRenderer>()).ToList();
        Assert.That(renderers, Is.Not.Empty);
        Assert.That(renderers.All(r => r.enabled && r.gameObject.activeInHierarchy), Is.True);
    }

    [Test]
    public void VinesWrapOverATableEdgeOnlyWhenAllowed()
    {
        // Planted 15 cm from the +x edge, heading straight for it.
        var controller = Planter(0);
        Assert.That(controller.TryGrowAtSpatialHit(TableHit(new Vector3(.15f, .2f, 0f))), Is.True);
        var stopped = Branches().SelectMany(f => f.sharedMesh.vertices).Min(v => v.y);
        Assert.That(stopped, Is.GreaterThan(-.012f), "without free steps the vine stops at the edge");
        foreach (var f in Branches()) Object.DestroyImmediate(f.gameObject);

        m_GeneratorObject.GetComponent<IvyGeneratorTREANT>().maxUnattachedSteps = NatureIvySpatialGrowthController.VineUnattachedSteps;
        Assert.That(controller.TryGrowAtSpatialHit(TableHit(new Vector3(.15f, .2f, 0f))), Is.True);
        var wrapped = Branches().SelectMany(f => f.sharedMesh.vertices).Min(v => v.y);
        Assert.That(wrapped, Is.LessThan(-.03f), "with two free steps it turns down the side of the table");
    }

    [Test]
    public void ConfigureMakesThinVinesThatTheLeafCodeCanRead()
    {
        var controller = m_Root.AddComponent<NatureIvySpatialGrowthController>();
        var camera = new GameObject("Viewer").AddComponent<Camera>();
        camera.transform.SetParent(m_Root.transform);
        controller.Configure(camera, null, m_GeneratorObject, null, 48);
        var generator = m_Root.GetComponentsInChildren<IvyGeneratorTREANT>(true).First(g => g.name == "Nature Runtime Ivy Generator");
        Assert.That(generator.disableRuntimeBatching, Is.True);
        Assert.That(generator.maxUnattachedSteps, Is.EqualTo(NatureIvySpatialGrowthController.VineUnattachedSteps));
        Assert.That(generator.branch3dSize, Is.LessThanOrEqualTo(.006f), "a thin stem, not a 6 cm pipe");
        Assert.That(generator.narrowFactor, Is.LessThan(generator.branch3dSize), "the tube must not turn inside out at the tip");
        Assert.That(generator.branch3dDivisions % 4, Is.Zero);
        Assert.That(generator.use3dbranch, Is.True);
        Assert.That(generator.growIvy, Is.False, "the custom grow shader does not run on visionOS");
        Assert.That(generator.addLeaves, Is.True);
        Assert.That(generator.maxBranchPositions * generator.branchPartLength, Is.InRange(1f, 1.6f));
    }

    [Test]
    public void TubeRingsGiveThePathItsSurfaceNormalAndSide()
    {
        var controller = Planter();
        Assert.That(controller.TryGrowAtSpatialHit(TableHit(new Vector3(-.2f, .2f, 0f))), Is.True);
        var branch = Branches()[0];
        var mesh = branch.sharedMesh;
        var centres = new List<Vector3>(); var normals = new List<Vector3>(); var sides = new List<Vector3>();
        var rings = NatureVineRings.Extract(mesh.vertices, mesh.triangles.Length, NatureIvySpatialGrowthController.VineDivisions, centres, normals, sides);
        Assert.That(rings, Is.GreaterThanOrEqualTo(4));
        Assert.That(centres[0].y, Is.EqualTo(0f).Within(.003f), "the root sits on the table top");
        for (var i = 0; i < rings; i++)
        {
            if (centres[i].x > .28f) break;   // on the top only
            Assert.That(Vector3.Dot(normals[i], Vector3.up), Is.GreaterThan(.9f), $"ring {i} normal {normals[i]}");
            Assert.That(Mathf.Abs(Vector3.Dot(sides[i], normals[i])), Is.LessThan(.1f));
        }
        Assert.That(NatureVineRings.Extract(mesh.vertices, mesh.triangles.Length, 6, centres, normals, sides), Is.Zero, "divisions must be a multiple of 4");
    }

    [Test]
    public void LeavesSitAlongTheVineAlternatingAndUnfold()
    {
        var centres = new List<Vector3>(); var normals = new List<Vector3>(); var sides = new List<Vector3>();
        for (var i = 0; i <= 20; i++) { centres.Add(new Vector3(i * .045f, 0f, 0f)); normals.Add(Vector3.up); sides.Add(Vector3.forward); }
        var leaves = new List<NatureVineLeaf>();
        NatureVineLeaves.Plan(centres, normals, sides, new System.Random(7), leaves);
        Assert.That(leaves.Count, Is.InRange(16, 28), "about one leaf every 4 cm over 90 cm");
        for (var i = 0; i < leaves.Count; i++)
        {
            var leaf = leaves[i];
            Assert.That(leaf.Size, Is.InRange(NatureVineLeaves.MinSize * .64f, NatureVineLeaves.MaxSize));
            Assert.That(Vector3.Dot(leaf.Tip, Vector3.up), Is.GreaterThan(.2f), "leaves lift off the surface");
            Assert.That(Mathf.Abs(Vector3.Dot(leaf.Tip, leaf.Width)), Is.LessThan(.01f));
            Assert.That(leaf.Stem.y, Is.EqualTo(NatureVineLeaves.Lift).Within(1e-4f));
            if (i > 0) Assert.That(Mathf.Sign(leaf.Tip.z), Is.Not.EqualTo(Mathf.Sign(leaves[i - 1].Tip.z)), "alternating sides");
            if (i > 0) Assert.That(leaf.At, Is.GreaterThan(leaves[i - 1].At));
        }
        Assert.That(NatureVineLeaves.Unfold(0f), Is.Zero);
        Assert.That(NatureVineLeaves.Unfold(NatureVineLeaves.UnfoldSeconds), Is.EqualTo(1f).Within(1e-4f));
        Assert.That(NatureVineLeaves.Unfold(NatureVineLeaves.UnfoldSeconds * .7f), Is.GreaterThan(1f), "a little overshoot");

        var space = new GameObject("space").transform;
        var mesh = new Mesh();
        try
        {
            var scales = new float[leaves.Count];
            var lists = (new List<Vector3>(), new List<Vector3>(), new List<Vector2>(), new List<int>());
            NatureVineLeaves.Build(mesh, leaves, scales, 0, space, lists.Item1, lists.Item2, lists.Item3, lists.Item4);
            Assert.That(mesh.vertexCount, Is.Zero, "nothing before the reveal reaches a leaf");
            for (var i = 0; i < scales.Length; i++) scales[i] = 1f;
            NatureVineLeaves.Build(mesh, leaves, scales, 0, space, lists.Item1, lists.Item2, lists.Item3, lists.Item4);
            Assert.That(mesh.vertexCount, Is.EqualTo(4 * leaves.Count(l => l.Texture == 0)));
            Assert.That(mesh.triangles.Length, Is.EqualTo(6 * leaves.Count(l => l.Texture == 0)));
        }
        finally { Object.DestroyImmediate(mesh); Object.DestroyImmediate(space.gameObject); }
    }

    [Test]
    public void AFingertipOnTheRoomMeshIsATouch()
    {
        var colliders = new[] { m_Table.GetComponent<Collider>() };
        Assert.That(NatureVineTouch.TryFindSurface(new Vector3(0f, .012f, 0f), colliders, NatureVineTouch.TouchDistance, out var hit), Is.True);
        Assert.That(hit.normal.y, Is.GreaterThan(.99f));
        Assert.That(hit.point.y, Is.EqualTo(0f).Within(.001f));
        Assert.That(NatureVineTouch.TryFindSurface(new Vector3(0f, -.006f, 0f), colliders, NatureVineTouch.TouchDistance, out hit), Is.True, "pressed slightly into the coarse mesh");
        Assert.That(NatureVineTouch.TryFindSurface(new Vector3(.31f, -.1f, 0f), colliders, NatureVineTouch.TouchDistance, out hit), Is.True, "the table's side");
        Assert.That(hit.normal.x, Is.GreaterThan(.99f));
        Assert.That(NatureVineTouch.TryFindSurface(new Vector3(0f, .05f, 0f), colliders, NatureVineTouch.TouchDistance, out _), Is.False, "5 cm above is not a touch");
        Assert.That(NatureVineTouch.TryFindSurface(new Vector3(0f, .05f, 0f), new Collider[0], .2f, out _), Is.False, "only room colliders count");
    }

    [Test]
    public void OneVinePerTouchAwayFromTheBag()
    {
        var p = new Vector3(1f, 0f, 0f);
        Assert.That(NatureVineTouch.CanPlant(true, 10f, -100f, p, null, null, 0), Is.True);
        Assert.That(NatureVineTouch.CanPlant(false, 10f, -100f, p, null, null, 0), Is.False, "only when the touch begins");
        Assert.That(NatureVineTouch.CanPlant(true, 10f, 9.5f, p, null, null, 0), Is.False, "cooldown");
        Assert.That(NatureVineTouch.CanPlant(true, 10f, -100f, p, p + Vector3.right * .1f, null, 0), Is.False, "too close to that hand's last vine");
        Assert.That(NatureVineTouch.CanPlant(true, 10f, -100f, p, p + Vector3.right * .3f, null, 0), Is.True);
        Assert.That(NatureVineTouch.CanPlant(true, 10f, -100f, p, null, p + Vector3.up * .1f, 0), Is.False, "squeezing the bag on the table");
        Assert.That(NatureVineTouch.CanPlant(true, 10f, -100f, p, null, null, NatureVineTouch.MaxGrowing), Is.False, "too many growing");
    }

    [Test]
    public void HintShowsOnceThenOnceMoreAndStopsAfterTheFirstVine()
    {
        var controller = m_Root.AddComponent<NatureIvySpatialGrowthController>();
        var camera = new GameObject("Viewer").AddComponent<Camera>();
        camera.transform.SetParent(m_Root.transform);
        camera.transform.position = new Vector3(0f, 1.5f, -1f);
        typeof(NatureIvySpatialGrowthController).GetField("m_Camera", Flags).SetValue(controller, camera);

        Assert.That(NatureIvySpatialGrowthController.HintAlpha(0f, -1f), Is.Zero);
        Assert.That(NatureIvySpatialGrowthController.HintAlpha(2f, -1f), Is.EqualTo(NatureIvySpatialGrowthController.HintAlphaMax));
        Assert.That(NatureIvySpatialGrowthController.HintAlpha(7f, -1f), Is.Zero);
        Assert.That(NatureIvySpatialGrowthController.HintAlpha(2f, .4f), Is.Zero, "cancelled hints fade fast");

        controller.UpdateHint(true, 0f);
        controller.UpdateHint(true, 2.9f);
        Assert.That(controller.Hint, Is.Null);
        controller.UpdateHint(true, 3.1f); controller.UpdateHint(true, 5f);
        Assert.That(controller.Hint, Is.Not.Null);
        Assert.That(controller.Hint.gameObject.activeSelf, Is.True);
        Assert.That(controller.Hint.sprite, Is.Not.Null);
        var toHint = controller.Hint.transform.position - camera.transform.position;
        Assert.That(toHint.y, Is.LessThan(0f), "below eye level");
        Assert.That(Vector3.Dot(controller.Hint.transform.forward, toHint.normalized), Is.GreaterThan(.99f), "faces the user");
        controller.UpdateHint(true, 10f);
        Assert.That(controller.Hint.gameObject.activeSelf, Is.False);
        controller.UpdateHint(true, 40f);
        Assert.That(controller.Hint.gameObject.activeSelf, Is.False, "the second time waits 45 s");
        controller.UpdateHint(true, 48.2f); controller.UpdateHint(true, 50f);
        Assert.That(controller.Hint.gameObject.activeSelf, Is.True);
        controller.UpdateHint(true, 60f); controller.UpdateHint(true, 200f);
        Assert.That(controller.Hint.gameObject.activeSelf, Is.False, "at most twice");

        var fresh = new GameObject("fresh").AddComponent<NatureIvySpatialGrowthController>();
        fresh.transform.SetParent(m_Root.transform);
        typeof(NatureIvySpatialGrowthController).GetField("m_Camera", Flags).SetValue(fresh, camera);
        typeof(NatureIvySpatialGrowthController).GetField("m_TouchVines", Flags).SetValue(fresh, 1);
        fresh.UpdateHint(true, 0f); fresh.UpdateHint(true, 10f);
        Assert.That(fresh.Hint, Is.Null, "no hint once a vine has been planted by touch");
    }

    [Test]
    public void LeafTexturesAndHintSpritesAreInResources()
    {
        foreach (var maple in new[] { false, true })
            foreach (var name in NatureVineLook.Textures(maple))
                Assert.That(Resources.Load<Texture2D>(name), Is.Not.Null, name);
        foreach (var english in new[] { false, true })
        {
            var sprite = Resources.Load<Sprite>(NatureVineLook.HintSprite(english));
            Assert.That(sprite, Is.Not.Null);
            Assert.That(sprite.pixelsPerUnit, Is.EqualTo(4000f).Within(.01f));
        }
        Assert.That(Resources.Load<Material>("MoodiumNature/NatureIvyLeaf"), Is.Not.Null, "base leaf material (URP Lit, alpha clip, double-sided)");
    }
}

#endif
