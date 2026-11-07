using System.IO;
using Moodium.Flow;
using NUnit.Framework;
using Unity.PolySpatial;
using UnityEditor;
using UnityEngine;

public sealed class MoodCheckUISetupTests
{
    const string Root = "Assets/UI/Moodium/MoodCheck";
    const string PrefabPath = Root + "/Generated/MoodCheckPanel.prefab";

    [OneTimeSetUp]
    public void BuildAssets() => MoodCheckUISetup.BuildOrUpdate();

    [Test]
    public void ColorLakeHasLensHaloHiddenAxesAndReadableTypographyHierarchy()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var plate = prefab.transform.Find("Plate");
        Assert.That(plate.localPosition.y, Is.EqualTo(.005f).Within(.0001f));
        Assert.That(plate.Find("PlateLens").GetComponent<SpriteRenderer>().sprite.name, Is.EqualTo("mood_plate_lens"));
        Assert.That(plate.Find("PlateTintBase").GetComponent<SpriteRenderer>().sprite.name, Is.EqualTo("mood_plate_colorfield"));
        Assert.That(plate.Find("Token/Halo"), Is.Not.Null);
        Assert.That(plate.Find("Axes").gameObject.activeSelf, Is.False);
        Assert.That(plate.Find("NeutralZone").gameObject.activeSelf, Is.False);
        Assert.That(plate.Find("Trail").gameObject.activeSelf, Is.False);
        var title = prefab.transform.Find("Header/Title").GetComponent<TMPro.TMP_Text>();
        var axis = prefab.transform.Find("AxisLabels/Up").GetComponent<TMPro.TMP_Text>();
        Assert.That(title.fontSize, Is.GreaterThan(axis.fontSize * 1.5f));
        Assert.That(prefab.transform.Find("Readout/MoodDot"), Is.Not.Null);
        foreach (Transform chip in prefab.transform.Find("QuickChips"))
            Assert.That(chip.Find("Dot"), Is.Not.Null);
        Assert.That(prefab.transform.Find("Footer/ConfirmButton").localPosition.x, Is.Zero);
    }

    [Test]
    public void PngAssets_UseSpatialSpriteImportSettings()
    {
        foreach (var path in Directory.GetFiles(Root + "/png", "*.png"))
        {
            var assetPath = path.Replace('\\', '/');
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            Assert.That(importer, Is.Not.Null, assetPath);
            Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Sprite), assetPath);
            Assert.That(importer.sRGBTexture, Is.True, assetPath);
            Assert.That(importer.alphaIsTransparency, Is.True, assetPath);
            Assert.That(importer.mipmapEnabled, Is.True, assetPath);
            Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Clamp), assetPath);
            Assert.That(importer.maxTextureSize, Is.EqualTo(256).Or.EqualTo(512), assetPath);
        }
    }

    [Test]
    public void Prefab_HasRequiredHierarchyDimensionsAndSorting()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Assert.That(prefab, Is.Not.Null);
        var panelGlass = prefab.transform.Find("PanelGlass");
        Assert.That(panelGlass, Is.Not.Null);
        var bounds = panelGlass.Find("Glass Surface").GetComponent<MeshFilter>().sharedMesh.bounds.size;
        Assert.That(bounds.x, Is.EqualTo(.462f).Within(.001f));
        Assert.That(bounds.y, Is.EqualTo(.566f).Within(.001f));
        Assert.That(prefab.transform.Find("Header"), Is.Not.Null);
        Assert.That(prefab.transform.Find("Readout"), Is.Not.Null);
        Assert.That(prefab.transform.Find("AxisLabels"), Is.Not.Null);
        Assert.That(prefab.transform.Find("Footer"), Is.Not.Null);

        var plate = prefab.transform.Find("Plate");
        Assert.That(plate.Find("PlateWell"), Is.Not.Null);
        Assert.That(plate.Find("PlateTintBase"), Is.Not.Null);
        Assert.That(plate.Find("PlateGlow"), Is.Not.Null);
        Assert.That(plate.Find("Axes"), Is.Not.Null);
        Assert.That(plate.Find("NeutralZone"), Is.Not.Null);
        Assert.That(plate.Find("QuadrantIcons"), Is.Not.Null);
        Assert.That(plate.Find("Trail"), Is.Not.Null);
        Assert.That(plate.Find("PlateCollider").GetComponent<BoxCollider>().size.x,
            Is.EqualTo(.28f).Within(.001f));
        Assert.That(plate.Find("Token/TokenCollider").GetComponent<SphereCollider>().radius,
            Is.EqualTo(.035f).Within(.001f));
        Assert.That(plate.Find("Token/TokenCollider").GetComponent<VisionOSHoverEffect>(), Is.Not.Null);
        Assert.That(plate.Find("Token/MoodiSprite").GetComponent<SpriteRenderer>().sortingOrder, Is.EqualTo(22));
        Assert.That(prefab.transform.Find("Readout/MoodName").localScale.x, Is.EqualTo(.1f).Within(.0001f));
        Assert.That(prefab.transform.Find("AxisLabels/Up").localScale.x, Is.EqualTo(.1f).Within(.0001f));
        foreach (var text in prefab.GetComponentsInChildren<TMPro.TMP_Text>(true))
            Assert.That(text.rectTransform.localScale.x, Is.EqualTo(.1f).Within(.0001f), text.name);
        Assert.That(plate.Find("Axes/Horizontal").GetComponent<Renderer>().sharedMaterial.color.r,
            Is.EqualTo(1f).Within(.0001f));
        Assert.That(plate.Find("Trail").GetComponent<Renderer>().sharedMaterial.color.r,
            Is.EqualTo(1f).Within(.0001f));
    }

    [Test]
    public void Prefab_HasFiveQuickChipsAndAllActionButtonsAreSpatial()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var chips = prefab.transform.Find("QuickChips");
        Assert.That(chips.childCount, Is.EqualTo(5));
        foreach (Transform chip in chips)
        {
            Assert.That(chip.GetComponent<MoodiumSpatialButton>(), Is.Not.Null, chip.name);
            Assert.That(chip.GetComponent<BoxCollider>().size.y, Is.GreaterThanOrEqualTo(.045f), chip.name);
        }

        var footer = prefab.transform.Find("Footer");
        foreach (var name in new[] { "SkipButton", "AdjustButton", "ConfirmButton", "RetryButton", "ContinueButton" })
            Assert.That(footer.Find(name).GetComponent<MoodiumSpatialButton>(), Is.Not.Null, name);
    }

    [Test]
    public void BuildOrUpdate_IsIdempotent()
    {
        var before = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath).transform.GetComponentsInChildren<Transform>(true).Length;
        MoodCheckUISetup.BuildOrUpdate();
        var after = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath).transform.GetComponentsInChildren<Transform>(true).Length;
        Assert.That(after, Is.EqualTo(before));
    }

    [Test]
    public void RuntimePrefab_IsAvailableThroughResourcesFallback()
    {
        Assert.That(Resources.Load<GameObject>("Moodium/MoodCheck/MoodCheckPanel"), Is.Not.Null);
    }
}
