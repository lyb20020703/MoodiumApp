using System.Collections.Generic;
using Moodium.NatureWorld;
using NUnit.Framework;
using UnityEngine;

public sealed class NatureIvySurfaceResolverTests
{
    GameObject m_Root;

    [SetUp]
    public void SetUp() => m_Root = new GameObject("Nature Ivy Surface Resolver Tests");

    [TearDown]
    public void TearDown()
    {
        if (m_Root != null)
            Object.DestroyImmediate(m_Root);
    }

    [Test]
    public void FindsUpwardSpatialSurfaceAndIgnoresNearerVirtualCollider()
    {
        var table = GameObject.CreatePrimitive(PrimitiveType.Cube);
        table.transform.SetParent(m_Root.transform);
        table.transform.position = Vector3.zero;
        table.transform.localScale = new Vector3(2f, .1f, 2f);

        var virtualBlocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        virtualBlocker.transform.SetParent(m_Root.transform);
        virtualBlocker.transform.position = new Vector3(0f, .5f, 0f);
        virtualBlocker.transform.localScale = new Vector3(.5f, .1f, .5f);
        Physics.SyncTransforms();

        var resolver = new NatureIvySurfaceResolver(
            new[] { table.GetComponent<Collider>() }, .65f, 2f, .08f, 8);

        Assert.That(resolver.TryFindSurfaceBelow(new Vector3(0f, 1f, 0f), out var hit), Is.True);
        Assert.That(hit.collider, Is.EqualTo(table.GetComponent<Collider>()));
        Assert.That(Vector3.Dot(hit.normal, Vector3.up), Is.GreaterThanOrEqualTo(.65f));
    }

    [Test]
    public void GazeRayFindsWallBehindVirtualSoftAndRejectsEmptySpace()
    {
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.transform.SetParent(m_Root.transform);
        wall.transform.position = new Vector3(0, 0, 2);
        wall.transform.localScale = new Vector3(3, 3, .1f);
        var soft = GameObject.CreatePrimitive(PrimitiveType.Cube);
        soft.transform.SetParent(m_Root.transform);
        soft.transform.position = Vector3.forward;
        Physics.SyncTransforms();
        var owned = new[] { wall.GetComponent<Collider>() };
        Assert.That(NatureIvySurfaceResolver.TryRaycast(owned, new Ray(Vector3.zero, Vector3.forward), 8, out var hit), Is.True);
        Assert.That(hit.collider, Is.EqualTo(owned[0]));
        Assert.That(Vector3.Dot(hit.normal, Vector3.back), Is.GreaterThan(.99f));
        Assert.That(NatureIvySurfaceResolver.TryRaycast(owned, new Ray(Vector3.zero, Vector3.up), 8, out _), Is.False);
    }

    [Test]
    public void RejectsWallWhenNoUpwardSpatialSurfaceExists()
    {
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.transform.SetParent(m_Root.transform);
        wall.transform.position = new Vector3(0f, .5f, .1f);
        wall.transform.localScale = new Vector3(2f, 2f, .1f);
        Physics.SyncTransforms();

        var resolver = new NatureIvySurfaceResolver(
            new List<Collider> { wall.GetComponent<Collider>() }, .65f, 2f, .08f, 8);

        Assert.That(resolver.TryFindSurfaceBelow(new Vector3(0f, 1f, 0f), out _), Is.False);
    }
}
