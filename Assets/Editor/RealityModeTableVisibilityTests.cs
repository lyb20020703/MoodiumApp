using System.Reflection;
using Moodium.CandyWorld;
using Moodium.Flow;
using Moodium.Reality;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class RealityModeTableVisibilityTests
{
    GameObject m_Root;
    GameObject m_CameraObject;
    GameObject m_TablePrefab;
    MoodiumWorldDefinition m_World;

    [TearDown]
    public void TearDown()
    {
        if (m_Root != null)
        {
            var tableManager = m_Root.GetComponent<RealitySpatialTableManager>();
            if (tableManager != null && tableManager.TableInstance != null)
                Object.DestroyImmediate(tableManager.TableInstance);
        }
        if (m_Root != null)
            Object.DestroyImmediate(m_Root);
        if (m_CameraObject != null)
            Object.DestroyImmediate(m_CameraObject);
        if (m_TablePrefab != null)
            Object.DestroyImmediate(m_TablePrefab);
        if (m_World != null)
            Object.DestroyImmediate(m_World);
    }

    [Test]
    public void SelectingRealityCandyWorldDoesNotCreateSpatialTable()
    {
        m_CameraObject = new GameObject("Reality Test Camera", typeof(Camera));
        m_CameraObject.tag = "MainCamera";
        var camera = m_CameraObject.GetComponent<Camera>();

        m_World = ScriptableObject.CreateInstance<MoodiumWorldDefinition>();
        m_World.name = "Candy World";
        var worldIdField = typeof(MoodiumWorldDefinition).GetField(
            "m_WorldId",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(worldIdField, Is.Not.Null);
        worldIdField.SetValue(m_World, "candy");
        m_TablePrefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
        m_TablePrefab.name = "Test Spatial Table Prefab";

        m_Root = new GameObject("Reality Flow Test Root");
        var flow = m_Root.AddComponent<MoodiumAppFlowController>();
        var objectPanel = new GameObject("Reality Object Panel");
        objectPanel.transform.SetParent(m_Root.transform);
        var candyManagerObject = new GameObject("Test Candy World Manager");
        candyManagerObject.transform.SetParent(m_Root.transform);
        var candyManager = candyManagerObject.AddComponent<CandyWorldManager>();
        candyManager.Configure(camera, null, null, m_World, null, null, null, null, null);

        SetField(flow, "m_MainCamera", camera);
        SetField(flow, "m_ObjectPanel", objectPanel);
        SetField(flow, "m_SpatialTablePrefab", m_TablePrefab);
        SetField(flow, "m_CandyWorldManager", candyManager);

        var selectMethod = typeof(MoodiumAppFlowController).GetMethod(
            "SelectObjectWorld",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(selectMethod, Is.Not.Null);
        LogAssert.Expect(LogType.Error, "[Reality Flow] No tracked-source Capsule spawner was found.");
        selectMethod.Invoke(flow, new object[] { m_World });

        var tableManager = GetField<RealitySpatialTableManager>(flow, "m_RealitySpatialTableManager");
        Assert.That(tableManager == null || tableManager.TableInstance == null, Is.True);
    }

    static void SetField<T>(MoodiumAppFlowController target, string name, T value)
    {
        var field = typeof(MoodiumAppFlowController).GetField(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null);
        field.SetValue(target, value);
    }

    static T GetField<T>(MoodiumAppFlowController target, string name) where T : class
    {
        var field = typeof(MoodiumAppFlowController).GetField(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null);
        return field.GetValue(target) as T;
    }
}
