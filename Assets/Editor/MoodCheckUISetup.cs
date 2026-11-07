using System.Collections.Generic;
using System.IO;
using Moodium.Flow;
using Moodium.MoodCheck;
using TMPro;
using Unity.PolySpatial;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class MoodCheckUISetup
{
    [MenuItem("Moodium/Validate Mood Check UI")]
    public static void ValidateMoodCheckUI()
    {
        var api = ScriptableObject.CreateInstance<UnityEditor.TestTools.TestRunner.Api.TestRunnerApi>();
        api.RegisterCallbacks(new MoodUITestResults());
        api.Execute(new UnityEditor.TestTools.TestRunner.Api.ExecutionSettings(
            new UnityEditor.TestTools.TestRunner.Api.Filter
            {
                testMode = UnityEditor.TestTools.TestRunner.Api.TestMode.EditMode,
                groupNames = new[] { "^MoodCheck", "^MoodClassifier", "^WalkTrailGuideControllerTests" }
            }));
    }

    sealed class MoodUITestResults : UnityEditor.TestTools.TestRunner.Api.ICallbacks
    {
        public void RunStarted(UnityEditor.TestTools.TestRunner.Api.ITestAdaptor testsToRun) { }
        public void TestStarted(UnityEditor.TestTools.TestRunner.Api.ITestAdaptor test) { }
        public void TestFinished(UnityEditor.TestTools.TestRunner.Api.ITestResultAdaptor result) { }
        public void RunFinished(UnityEditor.TestTools.TestRunner.Api.ITestResultAdaptor result)
        {
            var report = Path.Combine(Path.GetTempPath(), "Moodium-MoodPlate-EditMode.xml");
            UnityEditor.TestTools.TestRunner.Api.TestRunnerApi.SaveResultToFile(result, report);
            Debug.Log("[Mood Plate UI] EditMode results: " + report);
        }
    }

    [MenuItem("Moodium/Preview Mood Check UI")]
    public static void PreviewMoodCheckUI()
    {
        BuildOrUpdate();
        foreach (var english in new[] { false, true })
        {
            CapturePlatePreview(english, false);
            CapturePlatePreview(english, true);
        }
    }

    static void CapturePlatePreview(bool english, bool confirming)
    {
        var owner = new GameObject("Mood Plate Preview");
        var cameraRoot = new GameObject("Mood Plate Preview Camera");
        var camera = cameraRoot.AddComponent<Camera>();
        camera.transform.position = new Vector3(0f, 0f, -1f);
        camera.orthographic = true; camera.orthographicSize = .315f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.055f, .045f, .09f);
        camera.cullingMask = 1 << 31;
        var target = new RenderTexture(924, 1132, 24);
        var previous = RenderTexture.active;
        Texture2D image = null;
        try
        {
            var controller = owner.AddComponent<MoodCheckController>();
            controller.Configure(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), camera);
            controller.BeginPreCheck(new MoodCheckAvailability(true, true), english);
            controller.PanelTransform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            controller.Plate.SetNormalizedPosition(MoodClassifier.Preset(MoodKind.Calm), true);
            if (confirming) controller.QuickSelect(MoodKind.Calm);
            foreach (var node in owner.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = 31;
            foreach (var text in owner.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate(true);
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            image = new Texture2D(924, 1132, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 924, 1132), 0, 0); image.Apply();
            var path = Path.Combine(Path.GetTempPath(), "Moodium-MoodPlate-" + (english ? "en" : "cn") +
                (confirming ? "-confirm" : "") + ".png");
            File.WriteAllBytes(path, image.EncodeToPNG());
            Debug.Log("[Mood Plate UI] Preview: " + path);
        }
        finally
        {
            RenderTexture.active = previous;
            camera.targetTexture = null;
            if (image != null) Object.DestroyImmediate(image);
            target.Release(); Object.DestroyImmediate(target);
            Object.DestroyImmediate(owner); Object.DestroyImmediate(cameraRoot);
        }
    }
    const string Root = "Assets/UI/Moodium/MoodCheck";
    const string Generated = Root + "/Generated";
    const string PrefabPath = Generated + "/MoodCheckPanel.prefab";
    const string ResourceFolder = "Assets/Resources/Moodium/MoodCheck";
    const string ResourcePrefabPath = ResourceFolder + "/MoodCheckPanel.prefab";
    const string SurfacePath = Generated + "/MoodCheckPanel_Surface.asset";
    const string BorderPath = Generated + "/MoodCheckPanel_Border.asset";
    const string ButtonPath = Generated + "/MoodCheckButton_Surface.asset";
    const string LineMaterialPath = Generated + "/MoodCheckLine_White.mat";
    const string GlassPath = "Assets/Materials/Moodium_Glass_UI.mat";
    const string EdgePath = "Assets/Materials/Moodium_Glass_UI_Edge.mat";
    const string ChineseFontPath = "Assets/UI/Moodium/Fonts/AlibabaPuHuiTi Moodium SDF.asset";
    const string EnglishFontPath = "Assets/UI/Moodium/Fonts/Inter-Regular SDF.asset";
    const float PanelTextScale = .1f;

    [MenuItem("Moodium/Build Mood Check UI")]
    public static void BuildOrUpdate()
    {
        EnsureFolder(Generated);
        EnsureFolder(ResourceFolder);
        ConfigureTextures();
        var surface = ReplaceMesh(SurfacePath, CreateRoundedRectMesh(.462f, .566f, .034f, 12));
        var border = ReplaceMesh(BorderPath, CreateRoundedBorderMesh(.462f, .566f, .034f, .0014f, 12));
        var button = ReplaceMesh(ButtonPath, CreateRoundedRectMesh(.11f, .036f, .018f, 8));
        var root = BuildPrefab(surface, border, button);
        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        PrefabUtility.SaveAsPrefabAsset(root, ResourcePrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    static void ConfigureTextures()
    {
        var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { Root + "/png" });
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer)
                continue;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.sRGBTexture = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = true;
            var name = Path.GetFileNameWithoutExtension(path);
            importer.maxTextureSize = name.StartsWith("moodi_") || name.StartsWith("mood_plate_") ||
                name is "disc_mask" or "zone_ring_dashed" or "mood_token_halo" or "mood_panel_rim" ? 512 : 256;
            importer.SaveAndReimport();
        }
    }

    static GameObject BuildPrefab(Mesh surface, Mesh border, Mesh buttonMesh)
    {
        var root = new GameObject("MoodCheckPanel");
        var glass = Child(root.transform, "PanelGlass");
        CreateMeshVisual(glass.transform, "Glass Surface", surface,
            AssetDatabase.LoadAssetAtPath<Material>(GlassPath), 0);
        CreateMeshVisual(glass.transform, "Soft Edge Highlight", border,
            AssetDatabase.LoadAssetAtPath<Material>(EdgePath), 1);
        var rim = CreateSprite(glass.transform, "Rim", "mood_panel_rim", .462f, Color.white, 1);
        rim.transform.localScale = Vector3.Scale(rim.transform.localScale, new Vector3(1f, .566f / .462f, 1f));
        rim.transform.localPosition = new Vector3(0f, 0f, -.002f);

        var chinese = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(ChineseFontPath);
        var english = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(EnglishFontPath);
        var header = Child(root.transform, "Header");
        var kicker = CreateText(header.transform, "Kicker", "Moodi 想知道", new Vector3(0f, .246f, -.004f), .011f, chinese).GetComponent<TMP_Text>();
        kicker.color = new Color(1f, 1f, 1f, .6f); kicker.characterSpacing = 8f;
        CreateText(header.transform, "Title", "你现在的心情怎么样？", new Vector3(0f, .2175f, -.004f), .019f, chinese)
            .GetComponent<TMP_Text>().fontStyle = FontStyles.Bold;
        var readout = Child(root.transform, "Readout");
        CreateText(readout.transform, "MoodName", "一般", new Vector3(-.025f, .1815f, -.004f), .023f, chinese)
            .GetComponent<TMP_Text>().fontStyle = FontStyles.Bold;
        CreateText(readout.transform, "MoodDescription", "说不上好坏", new Vector3(.045f, .1815f, -.004f), .012f, chinese)
            .GetComponent<TMP_Text>().color = new Color(1f, 1f, 1f, .66f);
        CreateSprite(readout.transform, "MoodDot", "mood_chip_dot", .010f, Color.white, 21);

        var plate = Child(root.transform, "Plate");
        plate.transform.localPosition = new Vector3(0f, .005f, -.003f);
        CreateSprite(plate.transform, "PlateWell", "mood_plate_well", .28f, Color.white, 10)
            .transform.localPosition = new Vector3(0f, -.007f, 0f);
        CreateSprite(plate.transform, "PlateTintBase", "mood_plate_colorfield", .28f, new Color(1f, 1f, 1f, .92f), 11);
        CreateSprite(plate.transform, "PlateGlow", "glow_radial", .17f, new Color(1f, 1f, 1f, .30f), 12);
        CreateSprite(plate.transform, "PlateLens", "mood_plate_lens", .28f, Color.white, 13)
            .transform.localPosition = new Vector3(0f, 0f, -.003f);
        var axes = Child(plate.transform, "Axes");
        CreateLine(axes.transform, "Horizontal", new Vector2(.252f, .0007f), 13);
        CreateLine(axes.transform, "Vertical", new Vector2(.0007f, .252f), 13);
        axes.SetActive(false);
        CreateSprite(plate.transform, "NeutralZone", "zone_ring_dashed", .123f, new Color(1f, 1f, 1f, .26f), 13).SetActive(false);
        var icons = Child(plate.transform, "QuadrantIcons");
        CreateIcon(icons.transform, "HappyIcon", "mood_glyph_happy", new Vector2(.075f, .075f));
        CreateIcon(icons.transform, "CalmIcon", "mood_glyph_calm", new Vector2(.075f, -.075f));
        CreateIcon(icons.transform, "LowIcon", "mood_glyph_low", new Vector2(-.075f, -.075f));
        CreateIcon(icons.transform, "TenseIcon", "mood_glyph_tense", new Vector2(-.075f, .075f));
        CreateLine(plate.transform, "Trail", new Vector2(.0005f, .112f), 15).SetActive(false);
        var plateCollider = Child(plate.transform, "PlateCollider");
        plateCollider.AddComponent<BoxCollider>().size = new Vector3(.28f, .28f, .012f);
        var shadow = CreateSprite(plate.transform, "TokenShadow", "glow_radial", .043f,
            new Color(24f / 255f, 14f / 255f, 48f / 255f, .34f), 18);
        shadow.transform.localScale = new Vector3(shadow.transform.localScale.x, shadow.transform.localScale.y * .23f, 1f);
        shadow.transform.localPosition = new Vector3(0f, -.027f, -.001f);
        var token = Child(plate.transform, "Token");
        token.transform.localPosition = new Vector3(0f, 0f, -.012f);
        CreateSprite(token.transform, "Halo", "mood_token_halo", .10f, Color.white, 21)
            .transform.localPosition = new Vector3(0f, 0f, .001f);
        CreateSprite(token.transform, "MoodiSprite", "moodi_neutral", .073f, Color.white, 22);
        var tokenCollider = Child(token.transform, "TokenCollider");
        tokenCollider.AddComponent<SphereCollider>().radius = .035f;
        var tokenHover = tokenCollider.AddComponent<VisionOSHoverEffect>();
        tokenHover.Type = VisionOSHoverEffect.EffectType.Highlight;
        var moodPlate = plate.AddComponent<MoodCheckPlate>();
        moodPlate.Configure(.112f);
        moodPlate.ConfigureVisuals(new[]
        {
            LoadSprite("moodi_happy"), LoadSprite("moodi_calm"), LoadSprite("moodi_neutral"),
            LoadSprite("moodi_low"), LoadSprite("moodi_tense")
        });
        var plateHandle = plate.AddComponent<MoodCheckPlateHandle>();
        plateHandle.Configure(moodPlate);

        var labels = Child(root.transform, "AxisLabels");
        CreateText(labels.transform, "Up", "精力充沛", new Vector3(0f, .120f, -.010f), .0105f, chinese);
        CreateText(labels.transform, "Down", "有点累", new Vector3(0f, -.110f, -.010f), .0105f, chinese);
        var left = CreateText(labels.transform, "Left", "不太好", new Vector3(-.121f, .005f, -.010f), .0105f, chinese).GetComponent<TMP_Text>();
        left.alignment = TextAlignmentOptions.Left; left.rectTransform.pivot = new Vector2(0f, .5f);
        var right = CreateText(labels.transform, "Right", "还不错", new Vector3(.121f, .005f, -.010f), .0105f, chinese).GetComponent<TMP_Text>();
        right.alignment = TextAlignmentOptions.Right; right.rectTransform.pivot = new Vector2(1f, .5f);
        foreach (var label in labels.GetComponentsInChildren<TMP_Text>())
        {
            label.color = new Color(1f, 1f, 1f, .88f);
            label.fontSharedMaterial = GetOrCreateAxisMaterial(chinese);
        }

        var chips = Child(root.transform, "QuickChips");
        var moods = new[] { "Happy", "Calm", "Neutral", "Low", "Tense" };
        var words = new[] { "开心", "平静", "一般", "低落", "烦躁" };
        for (var i = 0; i < moods.Length; i++)
        {
            var chip = CreateButton(chips.transform, moods[i] + "Chip", words[i], new Vector3((i - 2) * .082f, -.17f, -.004f),
                new Vector2(.074f, .032f), buttonMesh, chinese);
            var label = chip.transform.Find("Label").GetComponent<TMP_Text>();
            label.fontSize = .012f / (.0967f * PanelTextScale);
            CreateSprite(chip.transform, "Dot", "mood_chip_dot", .007f, MoodCheckPlate.Tint((MoodKind)i), 23)
                .transform.localPosition = new Vector3(-.022f, 0f, -.005f);
            var selected = Child(chip.transform, "Selected Background");
            var selectedSprite = selected.AddComponent<SpriteRenderer>();
            selectedSprite.sprite = LoadSprite("mood_btn_primary_9slice");
            selectedSprite.drawMode = SpriteDrawMode.Sliced;
            selectedSprite.size = new Vector2(.074f, .032f);
            selectedSprite.sortingOrder = 21;
            selected.transform.localPosition = new Vector3(0f, 0f, -.001f);
            selected.SetActive(false);
        }

        var footer = Child(root.transform, "Footer");
        CreateText(footer.transform, "Hint", "拖动 Moodi，或直接点一个心情", new Vector3(0f, -.2375f, -.004f), .0115f, chinese)
            .GetComponent<TMP_Text>().color = new Color(1f, 1f, 1f, .58f);
        var confirmTitle = CreateText(footer.transform, "ConfirmTitle", "你现在感觉一般，对吗？", new Vector3(0f, -.2105f, -.004f), .015f, chinese);
        confirmTitle.GetComponent<TMP_Text>().fontStyle = FontStyles.Bold; confirmTitle.SetActive(false);
        CreateText(footer.transform, "RecommendationSummary", "Moodi 推荐", new Vector3(-.065f, -.245f, -.004f), .011f, chinese).SetActive(false);
        CreateButton(footer.transform, "SkipButton", "跳过", new Vector3(.15f, -.245f, -.004f), new Vector2(.08f, .036f), buttonMesh, chinese);
        CreateButton(footer.transform, "AdjustButton", "再调调", new Vector3(.095f, -.245f, -.004f), new Vector2(.09f, .036f), buttonMesh, chinese).SetActive(false);
        var confirmButton = CreateButton(footer.transform, "ConfirmButton", "对", new Vector3(0f, -.247f, -.004f), new Vector2(.12f, .036f), buttonMesh, chinese);
        var confirmLabel = confirmButton.transform.Find("Label").GetComponent<TMP_Text>();
        confirmLabel.fontSize = .014f / (.0967f * PanelTextScale); confirmLabel.fontStyle = FontStyles.Bold;
        confirmButton.SetActive(false);
        CreateButton(footer.transform, "RetryButton", "再选一次", new Vector3(.145f, -.245f, -.004f), new Vector2(.10f, .036f), buttonMesh, chinese).SetActive(false);
        CreateButton(footer.transform, "ContinueButton", "继续", new Vector3(.15f, -.245f, -.004f), new Vector2(.08f, .036f), buttonMesh, chinese).SetActive(false);

        var recommendation = Child(root.transform, "RecommendationCard");
        recommendation.AddComponent<MoodRecommendationView>();
        var recommendationGlass = CreateMeshVisual(recommendation.transform, "Glass", surface,
            AssetDatabase.LoadAssetAtPath<Material>(GlassPath), 2);
        recommendationGlass.transform.localScale = new Vector3(.392f / .462f, .78f, 1f);
        CreateText(recommendation.transform, "Kicker", "Moodi 推荐", new Vector3(-.12f, .18f, -.006f), .010f, chinese);
        CreateText(recommendation.transform, "Badge", "现在：一般", new Vector3(.105f, .18f, -.006f), .010f, chinese);
        CreateSprite(recommendation.transform, "Portrait", "moodi_neutral", .053f, Color.white, 22)
            .transform.localPosition = new Vector3(-.12f, .11f, -.008f);
        CreateText(recommendation.transform, "WorldName", "自然世界", new Vector3(.025f, .11f, -.008f), .031f, chinese);
        CreateText(recommendation.transform, "Recommendation", "都可以哦，要不先去森林里走走？",
            new Vector3(0f, .045f, -.008f), .014f, chinese);
        CreateText(recommendation.transform, "StickerText", "找到这张贴纸，贴在袋子上",
            new Vector3(0f, -.055f, -.008f), .011f, chinese);
        CreateText(recommendation.transform, "Other", "也想去另一个？完全可以",
            new Vector3(-.08f, -.145f, -.008f), .010f, chinese);
        recommendation.SetActive(false);
        var comparison = Child(root.transform, "ComparisonCard");
        var comparisonGlass = CreateMeshVisual(comparison.transform, "Glass", surface,
            AssetDatabase.LoadAssetAtPath<Material>(GlassPath), 2);
        comparisonGlass.transform.localScale = new Vector3(.56f / .462f, .49f / .566f, 1f);
        CreateText(comparison.transform, "Kicker", "在世界待了一会儿之后",
            new Vector3(0f, .19f, -.008f), .010f, chinese);
        CreateText(comparison.transform, "MoodTransition", "进入前 → 现在",
            new Vector3(.08f, .09f, -.008f), .018f, chinese);
        CreateText(comparison.transform, "Result", "谢谢你陪自己待了一会儿",
            new Vector3(.08f, .035f, -.008f), .014f, chinese);
        CreateText(comparison.transform, "Metrics", "停留 0 分钟 · 慢慢捏了 0 次",
            new Vector3(.08f, -.035f, -.008f), .011f, chinese);
        CreateText(comparison.transform, "Privacy", "这条记录只保存在这台设备上",
            new Vector3(-.08f, -.17f, -.008f), .010f, chinese);
        comparison.SetActive(false);
        var fonts = Child(root.transform, "FontReferences");
        var fontRefs = fonts.AddComponent<MoodCheckFontReferences>();
        fontRefs.Configure(chinese, english);
        fonts.SetActive(false);
        return root;
    }

    static GameObject CreateButton(Transform parent, string name, string text, Vector3 position,
        Vector2 size, Mesh mesh, TMP_FontAsset font)
    {
        var root = Child(parent, name);
        root.transform.localPosition = position;
        var collider = root.AddComponent<BoxCollider>();
        collider.size = new Vector3(size.x, Mathf.Max(.045f, size.y), .02f);
        var visual = CreateMeshVisual(root.transform, "Background", mesh,
            AssetDatabase.LoadAssetAtPath<Material>(EdgePath), 20);
        if (name == "ConfirmButton" || name.EndsWith("Chip"))
        {
            Object.DestroyImmediate(visual);
            visual = Child(root.transform, "Background");
            var sprite = visual.AddComponent<SpriteRenderer>();
            sprite.sprite = LoadSprite(name == "ConfirmButton" ? "mood_btn_primary_9slice" : "mood_btn_glass_9slice");
            sprite.drawMode = SpriteDrawMode.Sliced;
            sprite.size = new Vector2(.11f, .036f);
            sprite.sortingOrder = 20;
        }
        visual.transform.localScale = new Vector3(size.x / .11f, size.y / .036f, 1f);
        var label = CreateText(root.transform, "Label", text, new Vector3(0f, 0f, -.003f), .011f, font)
            .GetComponent<TextMeshPro>();
        var spatial = root.AddComponent<MoodiumSpatialButton>();
        spatial.Configure(label, visual.GetComponent<Renderer>(), null);
        var hover = visual.AddComponent<VisionOSHoverEffect>();
        hover.Type = VisionOSHoverEffect.EffectType.Highlight;
        return root;
    }

    static GameObject CreateIcon(Transform parent, string name, string spriteName, Vector2 position)
    {
        var icon = CreateSprite(parent, name, spriteName, .024f, new Color(1f, 1f, 1f, .45f), 14);
        icon.transform.localPosition = new Vector3(position.x, position.y, -.004f);
        return icon;
    }

    static GameObject CreateSprite(Transform parent, string name, string spriteName, float size, Color color, int order)
    {
        var root = Child(parent, name);
        var renderer = root.AddComponent<SpriteRenderer>();
        renderer.sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{Root}/png/{spriteName}.png");
        renderer.color = color;
        renderer.sortingOrder = order;
        if (renderer.sprite != null)
        {
            var spriteSize = renderer.sprite.bounds.size;
            root.transform.localScale = new Vector3(size / spriteSize.x, size / spriteSize.y, 1f);
        }
        return root;
    }

    static Sprite LoadSprite(string spriteName) =>
        AssetDatabase.LoadAssetAtPath<Sprite>($"{Root}/png/{spriteName}.png");

    static GameObject CreateLine(Transform parent, string name, Vector2 size, int order)
    {
        var line = GameObject.CreatePrimitive(PrimitiveType.Quad);
        line.name = name;
        line.transform.SetParent(parent, false);
        Object.DestroyImmediate(line.GetComponent<Collider>());
        line.transform.localScale = new Vector3(size.x, size.y, 1f);
        line.transform.localPosition = new Vector3(0f, 0f, -.004f);
        var renderer = line.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = GetOrCreateWhiteLineMaterial();
        renderer.sortingOrder = order;
        return line;
    }

    static GameObject CreateText(Transform parent, string name, string value, Vector3 position,
        float worldHeight, TMP_FontAsset font)
    {
        var root = Child(parent, name);
        root.transform.localPosition = position;
        var text = root.AddComponent<TextMeshPro>();
        text.font = font;
        text.text = value;
        text.fontSize = worldHeight / (.0967f * PanelTextScale);
        text.alignment = TextAlignmentOptions.Center;
        text.color = new Color(1f, 1f, 1f, .96f);
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.rectTransform.sizeDelta = new Vector2(.4f / PanelTextScale, worldHeight * 1.5f / PanelTextScale);
        text.rectTransform.localScale = Vector3.one * PanelTextScale;
        text.renderer.sortingOrder = 30;
        return root;
    }

    static Material GetOrCreateWhiteLineMaterial()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(LineMaterialPath);
        if (material != null)
            return material;
        var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
        material = new Material(shader) { name = "Mood Check White Line" };
        material.SetColor("_BaseColor", new Color(1f, 1f, 1f, .42f));
        material.SetColor("_Color", new Color(1f, 1f, 1f, .42f));
        AssetDatabase.CreateAsset(material, LineMaterialPath);
        return material;
    }

    static Material GetOrCreateAxisMaterial(TMP_FontAsset font)
    {
        var path = Generated + "/MoodCheckAxis_Outline.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(font.material) { name = "Mood Check Axis Outline" };
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetFloat(ShaderUtilities.ID_OutlineWidth, .15f);
        material.SetColor(ShaderUtilities.ID_OutlineColor, new Color(20f / 255f, 10f / 255f, 40f / 255f, .5f));
        EditorUtility.SetDirty(material);
        return material;
    }

    static GameObject CreateMeshVisual(Transform parent, string name, Mesh mesh, Material material, int order)
    {
        var root = Child(parent, name);
        root.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = root.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.sortingOrder = order;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return root;
    }

    static GameObject Child(Transform parent, string name)
    {
        var child = new GameObject(name);
        child.transform.SetParent(parent, false);
        return child;
    }

    static Mesh ReplaceMesh(string path, Mesh replacement)
    {
        replacement.name = Path.GetFileNameWithoutExtension(path);
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(replacement, path);
            return replacement;
        }
        EditorUtility.CopySerialized(replacement, existing);
        Object.DestroyImmediate(replacement);
        EditorUtility.SetDirty(existing);
        return existing;
    }

    static Mesh CreateRoundedRectMesh(float width, float height, float radius, int segments)
    {
        var perimeter = BuildPerimeter(width, height, radius, segments);
        var vertices = new List<Vector3> { Vector3.zero };
        vertices.AddRange(perimeter);
        var triangles = new List<int>();
        for (var i = 0; i < perimeter.Count; i++)
        {
            triangles.Add(0);
            triangles.Add(1 + (i + 1) % perimeter.Count);
            triangles.Add(1 + i);
        }
        return CreateMesh(vertices, triangles);
    }

    static Mesh CreateRoundedBorderMesh(float width, float height, float radius, float thickness, int segments)
    {
        var outer = BuildPerimeter(width, height, radius, segments);
        var inner = BuildPerimeter(width - thickness * 2f, height - thickness * 2f,
            Mathf.Max(.001f, radius - thickness), segments);
        var vertices = new List<Vector3>(outer);
        vertices.AddRange(inner);
        var triangles = new List<int>();
        for (var i = 0; i < outer.Count; i++)
        {
            var next = (i + 1) % outer.Count;
            triangles.Add(i); triangles.Add(next); triangles.Add(outer.Count + i);
            triangles.Add(next); triangles.Add(outer.Count + next); triangles.Add(outer.Count + i);
        }
        return CreateMesh(vertices, triangles);
    }

    static List<Vector3> BuildPerimeter(float width, float height, float radius, int segments)
    {
        var points = new List<Vector3>(segments * 4);
        var centers = new[]
        {
            new Vector2(width * .5f - radius, height * .5f - radius),
            new Vector2(-width * .5f + radius, height * .5f - radius),
            new Vector2(-width * .5f + radius, -height * .5f + radius),
            new Vector2(width * .5f - radius, -height * .5f + radius)
        };
        var starts = new[] { 0f, 90f, 180f, 270f };
        for (var corner = 0; corner < 4; corner++)
        for (var step = 0; step < segments; step++)
        {
            var angle = (starts[corner] + step * 90f / segments) * Mathf.Deg2Rad;
            var point = centers[corner] + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            points.Add(new Vector3(point.x, point.y, 0f));
        }
        return points;
    }

    static Mesh CreateMesh(List<Vector3> vertices, List<int> triangles)
    {
        var mesh = new Mesh();
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        var normals = new Vector3[vertices.Count];
        for (var i = 0; i < normals.Length; i++) normals[i] = Vector3.back;
        mesh.normals = normals;
        mesh.RecalculateBounds();
        return mesh;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
