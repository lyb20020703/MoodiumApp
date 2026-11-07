using System;
using System.Linq;
using System.Reflection;
using Moodium.Flow;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;

public sealed class RealityWorldModelCarouselTests
{
    GameObject m_CreatedItem;
    GameObject m_ModelPrefab;
    MoodiumWorldDefinition m_World;

    [TearDown]
    public void TearDown()
    {
        if (m_CreatedItem != null)
            UnityEngine.Object.DestroyImmediate(m_CreatedItem);
        if (m_ModelPrefab != null)
            UnityEngine.Object.DestroyImmediate(m_ModelPrefab);
        if (m_World != null)
            UnityEngine.Object.DestroyImmediate(m_World);
    }

    [Test]
    public void ResolverUsesCandyNatureAndFallbackModels()
    {
        var resolverType = RuntimeType("Moodium.Flow.WorldModelPrefabResolver");
        Assert.That(resolverType, Is.Not.Null);
        var resolve = resolverType.GetMethod("Resolve", BindingFlags.Public | BindingFlags.Static);
        Assert.That(resolve, Is.Not.Null);

        var candy = new GameObject("Candy Model");
        var nature = new GameObject("Nature Model");
        var fallback = new GameObject("Fallback Model");
        try
        {
            Assert.That(resolve.Invoke(null, new object[] { "candy", candy, nature, fallback }), Is.SameAs(candy));
            Assert.That(resolve.Invoke(null, new object[] { "nature", candy, nature, fallback }), Is.SameAs(nature));
            Assert.That(resolve.Invoke(null, new object[] { "water", candy, nature, fallback }), Is.SameAs(fallback));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(candy);
            UnityEngine.Object.DestroyImmediate(nature);
            UnityEngine.Object.DestroyImmediate(fallback);
        }
    }

    [Test]
    public void ModelItemBuildsHitTargetAndShowsStatusOnlyWhenSelected()
    {
        var itemType = RuntimeType("Moodium.Flow.WorldModelCarouselItem");
        Assert.That(itemType, Is.Not.Null);
        var create = itemType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static);
        Assert.That(create, Is.Not.Null);

        m_World = ScriptableObject.CreateInstance<MoodiumWorldDefinition>();
        var serializedWorld = new SerializedObject(m_World);
        serializedWorld.FindProperty("m_WorldId").stringValue = "nature";
        serializedWorld.FindProperty("m_DisplayName").stringValue = "Nature World";
        serializedWorld.FindProperty("m_IsAvailable").boolValue = false;
        serializedWorld.ApplyModifiedPropertiesWithoutUndo();

        m_ModelPrefab = new GameObject("Nature Model Source");
        var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
        visual.transform.SetParent(m_ModelPrefab.transform, false);
        UnityEngine.Object.DestroyImmediate(visual.GetComponent<Collider>());

        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
            "Assets/UI/Moodium/Fonts/Inter-Regular SDF.asset");
        m_CreatedItem = (GameObject)create.Invoke(
            null,
            new object[] { null, m_World, 0, m_ModelPrefab, font });

        var hitTarget = m_CreatedItem.GetComponent<BoxCollider>();
        var status = m_CreatedItem.GetComponentsInChildren<TMP_Text>(true).Single();
        Assert.That(hitTarget, Is.Not.Null);
        Assert.That(hitTarget.size.x, Is.GreaterThan(0f));
        Assert.That(hitTarget.size.y, Is.GreaterThan(0f));
        Assert.That(hitTarget.size.z, Is.GreaterThan(0f));
        Assert.That(status.text, Is.EqualTo("Nature World\nComing Soon"));
        Assert.That(status.gameObject.activeSelf, Is.False);

        itemType.GetMethod("SetSelected", BindingFlags.Instance | BindingFlags.Public)
            ?.Invoke(m_CreatedItem.GetComponent(itemType), new object[] { true });

        Assert.That(status.gameObject.activeSelf, Is.True);
    }

    [Test]
    public void CarouselOffersDedicatedModelConfigurationWithoutReplacingCardConfiguration()
    {
        var methods = typeof(WorldCarouselController).GetMethods(BindingFlags.Instance | BindingFlags.Public);

        Assert.That(methods.Any(method => method.Name == "Configure"), Is.True);
        Assert.That(methods.Any(method => method.Name == "ConfigureModels"), Is.True);
    }

    [Test]
    public void NatureWorldIsAvailableForRealityEnhancement()
    {
        var nature = AssetDatabase.LoadAssetAtPath<MoodiumWorldDefinition>(
            "Assets/Data/Moodium/Worlds/NatureWorld.asset");

        Assert.That(nature, Is.Not.Null);
        Assert.That(nature.IsAvailable, Is.True,
            "Nature World must be selectable from the Reality Enhancement carousel.");
    }

    [Test]
    public void RealityEnhancementSupportsNatureWorld()
    {
        var nature = ScriptableObject.CreateInstance<MoodiumWorldDefinition>();
        try
        {
            var serializedNature = new SerializedObject(nature);
            serializedNature.FindProperty("m_WorldId").stringValue = "nature";
            serializedNature.ApplyModifiedPropertiesWithoutUndo();

            Assert.That(MoodiumAppFlowController.SupportsRealityWorld(nature), Is.True);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(nature);
        }
    }

    [Test]
    public void ExistingWorldCardUsesSharedCarouselItemContract()
    {
        var itemType = RuntimeType("Moodium.Flow.WorldCarouselItem");

        Assert.That(itemType, Is.Not.Null);
        Assert.That(itemType.IsAssignableFrom(typeof(WorldCard)), Is.True);
    }

    [Test]
    public void RealityCarouselBuildsOnlyCandyAndNatureWithCandyInitiallySelected()
    {
        var database = AssetDatabase.LoadAssetAtPath<MoodiumWorldDatabase>(
            "Assets/Data/Moodium/Worlds/MoodiumWorldDatabase.asset");
        var candy = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/prefab/CandyWorldUI.prefab");
        var nature = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/prefab/NatureWorldUI.prefab");
        var fallback = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/prefab/CandyWorlLevelSelection.prefab");
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
            "Assets/UI/Moodium/Fonts/Inter-Regular SDF.asset");
        var carouselObject = new GameObject("Reality Carousel Integration Test");
        try
        {
            var carousel = carouselObject.AddComponent<WorldCarouselController>();
            carousel.ConfigureModels(database, candy, nature, fallback, font, _ => { });

            var items = carouselObject.GetComponentsInChildren<WorldModelCarouselItem>(true);
            Assert.That(items.Length, Is.EqualTo(2));
            Assert.That(items.Single(item => item.World.WorldId == "candy").transform.Find("CandyWorldUI"), Is.Not.Null);
            Assert.That(items.Single(item => item.World.WorldId == "nature").transform.Find("NatureWorldUI"), Is.Not.Null);
            Assert.That(items.Count(item => item.GetComponentsInChildren<TMP_Text>(true)
                .Single().gameObject.activeSelf), Is.EqualTo(1));
            Assert.That(items.Single(item => item.GetComponentsInChildren<TMP_Text>(true)
                .Single().gameObject.activeSelf).World.WorldId, Is.EqualTo("candy"));
            Assert.That(items.All(item => item.GetComponent<BoxCollider>() != null), Is.True);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(carouselObject);
        }
    }

    [Test]
    public void RealityCarouselNotifiesCandyAsInitialPreview()
    {
        var database = AssetDatabase.LoadAssetAtPath<MoodiumWorldDatabase>(
            "Assets/Data/Moodium/Worlds/MoodiumWorldDatabase.asset");
        var candy = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/prefab/CandyWorldUI.prefab");
        var nature = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/prefab/NatureWorldUI.prefab");
        var fallback = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/prefab/CandyWorlLevelSelection.prefab");
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
            "Assets/UI/Moodium/Fonts/Inter-Regular SDF.asset");
        var carouselObject = new GameObject("Reality Carousel Preview Test");
        var previewedWorldIds = new System.Collections.Generic.List<string>();
        try
        {
            var carousel = carouselObject.AddComponent<WorldCarouselController>();
            carousel.ConfigureModels(
                database,
                candy,
                nature,
                fallback,
                font,
                _ => { },
                world => previewedWorldIds.Add(world.WorldId));

            Assert.That(previewedWorldIds, Is.EqualTo(new[] { "candy" }));

            var candyItem = carouselObject.GetComponentsInChildren<WorldModelCarouselItem>(true)
                .Single(item => item.World.WorldId == "candy");
            carousel.PointerStarted(1, candyItem, Vector3.zero);
            carousel.PointerMoved(1, new Vector3(0.2f, 0f, 0f));
            typeof(WorldCarouselController).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(carousel, null);

            Assert.That(previewedWorldIds, Is.EqualTo(new[] { "candy", "nature" }));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(carouselObject);
        }
    }

    [Test]
    public void RealityCarouselLeavesVisibleGapBetweenWorldModels()
    {
        var database = AssetDatabase.LoadAssetAtPath<MoodiumWorldDatabase>(
            "Assets/Data/Moodium/Worlds/MoodiumWorldDatabase.asset");
        var candy = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/prefab/CandyWorldUI.prefab");
        var nature = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/prefab/NatureWorldUI.prefab");
        var fallback = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/prefab/CandyWorlLevelSelection.prefab");
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
            "Assets/UI/Moodium/Fonts/Inter-Regular SDF.asset");
        var carouselObject = new GameObject("Reality Carousel Spacing Test");
        try
        {
            var carousel = carouselObject.AddComponent<WorldCarouselController>();
            carousel.ConfigureModels(database, candy, nature, fallback, font, _ => { });

            var items = carouselObject.GetComponentsInChildren<WorldModelCarouselItem>(true);
            Assert.That(items.Length, Is.EqualTo(2));
            Assert.That(Mathf.Abs(items[0].transform.localPosition.x - items[1].transform.localPosition.x),
                Is.GreaterThanOrEqualTo(0.6f));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(carouselObject);
        }
    }

    static Type RuntimeType(string fullName)
    {
        return AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(fullName))
            .FirstOrDefault(type => type != null);
    }
}
