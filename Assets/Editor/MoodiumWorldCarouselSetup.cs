using System.IO;
using Moodium.Flow;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class MoodiumWorldCarouselSetup
{
    const string ScenePath = "Assets/Samples/PolySpatial/Scenes/Meshing 1.unity";
    const string GlassPrefabPath = "Assets/Prefab/UI/Moodium_GlassPanel.prefab";
    const string CardPrefabPath = "Assets/Prefab/UI/WorldCard.prefab";
    const string FontPath = "Assets/UI/Moodium/Fonts/Inter-Regular SDF.asset";
    const string AccentMaterialPath = "Assets/Materials/Moodium_Glass_UI_Edge.mat";
    const string WorldFolder = "Assets/Data/Moodium/Worlds";
    const string DatabasePath = WorldFolder + "/MoodiumWorldDatabase.asset";
    const string CandyPath = WorldFolder + "/CandyWorld.asset";

    [MenuItem("Moodium/Setup World Carousel")]
    public static void Setup()
    {
        EnsureFolder("Assets/Prefab/UI");
        EnsureFolder(WorldFolder);

        var glassPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(GlassPrefabPath);
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        var accentMaterial = AssetDatabase.LoadAssetAtPath<Material>(AccentMaterialPath);
        if (glassPrefab == null || font == null || accentMaterial == null)
            throw new FileNotFoundException("Glass UI assets are incomplete. Run Moodium/Setup Glass Panel Test first.");

        var cardPrefab = CreateWorldCardPrefab(glassPrefab, font, accentMaterial);
        var worlds = CreateWorldAssets();
        var database = CreateDatabase(worlds);
        BindScene(database, cardPrefab);
        AssetDatabase.SaveAssets();
        Debug.Log($"MOODIUM_WORLD_CAROUSEL_READY={CardPrefabPath}; worlds={worlds.Length}; database={DatabasePath}");
    }

    static GameObject CreateWorldCardPrefab(GameObject glassPrefab, TMP_FontAsset font, Material accentMaterial)
    {
        var root = new GameObject("WorldCard");

        var glass = (GameObject)PrefabUtility.InstantiatePrefab(glassPrefab);
        glass.name = "Card Glass Background";
        glass.transform.SetParent(root.transform, false);
        glass.transform.localScale = new Vector3(0.42f / 0.72f, 0.52f / 0.43f, 1f);
        foreach (var oldText in glass.GetComponentsInChildren<TMP_Text>(true))
            oldText.gameObject.SetActive(false);

        var accent = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        accent.name = "World Accent Orb";
        accent.transform.SetParent(root.transform, false);
        accent.transform.localPosition = new Vector3(0f, 0.09f, -0.012f);
        accent.transform.localScale = new Vector3(0.15f, 0.15f, 0.026f);
        Object.DestroyImmediate(accent.GetComponent<Collider>());
        var accentRenderer = accent.GetComponent<MeshRenderer>();
        accentRenderer.sharedMaterial = accentMaterial;
        accentRenderer.shadowCastingMode = ShadowCastingMode.Off;
        accentRenderer.receiveShadows = false;
        accentRenderer.sortingOrder = 8;

        var artworkObject = new GameObject("World Artwork");
        artworkObject.transform.SetParent(root.transform, false);
        artworkObject.transform.localPosition = new Vector3(0f, 0.09f, -0.016f);
        artworkObject.transform.localScale = new Vector3(0.28f, 0.2f, 1f);
        var artwork = artworkObject.AddComponent<SpriteRenderer>();
        artwork.sortingOrder = 9;
        artworkObject.SetActive(false);

        var iconObject = new GameObject("World Icon");
        iconObject.transform.SetParent(root.transform, false);
        iconObject.transform.localPosition = new Vector3(0f, 0.09f, -0.02f);
        iconObject.transform.localScale = Vector3.one * 0.07f;
        var icon = iconObject.AddComponent<SpriteRenderer>();
        icon.sortingOrder = 10;
        iconObject.SetActive(false);

        var worldName = CreateText(root.transform, "World Name", new Vector3(0f, -0.055f, -0.021f), 2.85f, new Vector2(3.7f, 0.62f), font, FontStyles.Bold);
        var localizedName = CreateText(root.transform, "Localized Name", new Vector3(0f, -0.105f, -0.021f), 1.75f, new Vector2(3.7f, 0.52f), font, FontStyles.Normal);
        var description = CreateText(root.transform, "World description", new Vector3(0f, -0.158f, -0.021f), 1.45f, new Vector2(3.6f, 0.62f), font, FontStyles.Normal);
        var availability = CreateText(root.transform, "Coming Soon", new Vector3(0f, -0.22f, -0.021f), 1.45f, new Vector2(3.4f, 0.48f), font, FontStyles.Normal);
        availability.color = new Color(0.72f, 0.82f, 1f, 1f);

        var collider = root.AddComponent<BoxCollider>();
        collider.size = new Vector3(0.42f, 0.52f, 0.025f);

        var card = root.AddComponent<WorldCard>();
        var serializedCard = new SerializedObject(card);
        serializedCard.FindProperty("m_WorldName").objectReferenceValue = worldName;
        serializedCard.FindProperty("m_LocalizedName").objectReferenceValue = localizedName;
        serializedCard.FindProperty("m_Description").objectReferenceValue = description;
        serializedCard.FindProperty("m_Availability").objectReferenceValue = availability;
        serializedCard.FindProperty("m_Artwork").objectReferenceValue = artwork;
        serializedCard.FindProperty("m_Icon").objectReferenceValue = icon;
        serializedCard.FindProperty("m_AccentRenderer").objectReferenceValue = accentRenderer;
        serializedCard.ApplyModifiedPropertiesWithoutUndo();

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, CardPrefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    static TMP_Text CreateText(
        Transform parent,
        string value,
        Vector3 position,
        float fontSize,
        Vector2 rectSize,
        TMP_FontAsset font,
        FontStyles style)
    {
        var textObject = new GameObject("Text - " + value);
        textObject.transform.SetParent(parent, false);
        textObject.transform.localPosition = position;
        textObject.transform.localScale = Vector3.one * 0.1f;
        var text = textObject.AddComponent<TextMeshPro>();
        text.font = font;
        text.text = value;
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.rectTransform.sizeDelta = rectSize;
        text.renderer.sortingOrder = 20;
        return text;
    }

    static MoodiumWorldDefinition[] CreateWorldAssets()
    {
        var candy = AssetDatabase.LoadAssetAtPath<MoodiumWorldDefinition>(CandyPath);
        if (candy == null)
            throw new FileNotFoundException("CandyWorld asset was not found.", CandyPath);
        ConfigureWorld(candy, "candy", "Candy World", string.Empty, "Sweet sensory experience", new Color(1f, 0.48f, 0.72f, 1f), true);

        return new[]
        {
            CreatePlaceholder("NatureWorld", "nature", "Nature World", "Soft organic calm", new Color(0.42f, 0.82f, 0.58f, 1f)),
            CreatePlaceholder("WaterWorld", "water", "Water World", "Flowing and weightless", new Color(0.32f, 0.7f, 1f, 1f)),
            candy,
            CreatePlaceholder("CloudWorld", "cloud", "Cloud World", "Light, quiet and airy", new Color(0.72f, 0.76f, 1f, 1f)),
            CreatePlaceholder("DreamWorld", "dream", "Dream World", "A soft surreal escape", new Color(0.74f, 0.48f, 1f, 1f))
        };
    }

    static MoodiumWorldDefinition CreatePlaceholder(
        string assetName,
        string id,
        string displayName,
        string description,
        Color accent)
    {
        var path = $"{WorldFolder}/{assetName}.asset";
        var world = AssetDatabase.LoadAssetAtPath<MoodiumWorldDefinition>(path);
        if (world == null)
        {
            world = ScriptableObject.CreateInstance<MoodiumWorldDefinition>();
            world.name = assetName;
            AssetDatabase.CreateAsset(world, path);
        }
        ConfigureWorld(world, id, displayName, string.Empty, description, accent, false);
        return world;
    }

    static void ConfigureWorld(
        MoodiumWorldDefinition world,
        string id,
        string displayName,
        string localizedName,
        string description,
        Color accent,
        bool available)
    {
        var serializedWorld = new SerializedObject(world);
        serializedWorld.FindProperty("m_WorldId").stringValue = id;
        serializedWorld.FindProperty("m_DisplayName").stringValue = displayName;
        serializedWorld.FindProperty("m_LocalizedName").stringValue = localizedName;
        serializedWorld.FindProperty("m_Description").stringValue = description;
        serializedWorld.FindProperty("m_AccentColor").colorValue = accent;
        serializedWorld.FindProperty("m_IsAvailable").boolValue = available;
        serializedWorld.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(world);
    }

    static MoodiumWorldDatabase CreateDatabase(MoodiumWorldDefinition[] worlds)
    {
        var database = AssetDatabase.LoadAssetAtPath<MoodiumWorldDatabase>(DatabasePath);
        if (database == null)
        {
            database = ScriptableObject.CreateInstance<MoodiumWorldDatabase>();
            database.name = "MoodiumWorldDatabase";
            AssetDatabase.CreateAsset(database, DatabasePath);
        }
        var serializedDatabase = new SerializedObject(database);
        var worldsProperty = serializedDatabase.FindProperty("m_Worlds");
        worldsProperty.arraySize = worlds.Length;
        for (var i = 0; i < worlds.Length; i++)
            worldsProperty.GetArrayElementAtIndex(i).objectReferenceValue = worlds[i];
        serializedDatabase.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(database);
        return database;
    }

    static void BindScene(MoodiumWorldDatabase database, GameObject cardPrefab)
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var flow = Object.FindFirstObjectByType<MoodiumAppFlowController>();
        if (flow == null)
            throw new System.InvalidOperationException("MoodiumAppFlowController was not found in Meshing 1.");

        var serializedFlow = new SerializedObject(flow);
        serializedFlow.FindProperty("m_WorldDatabase").objectReferenceValue = database;
        serializedFlow.FindProperty("m_WorldCardPrefab").objectReferenceValue = cardPrefab;
        serializedFlow.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(flow);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;
        var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        if (!string.IsNullOrEmpty(parent))
            EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
