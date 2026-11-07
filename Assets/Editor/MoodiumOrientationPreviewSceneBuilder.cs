using Moodium.Calibration;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Creates a deliberately isolated visual aid for checking the same calibrated-bag
/// orientation used at runtime. It is not included in build settings.
/// </summary>
public static class MoodiumOrientationPreviewSceneBuilder
{
    const string ScenePath = "Assets/Scenes/Moodium Orientation Preview.unity";
    const string CandyPath = "Assets/prefab/CandySoft.prefab";
    const string NaturePath = "Assets/prefab/NatureSoft.prefab";

    [MenuItem("Moodium/Build Orientation Preview Scene")]
    public static void Build()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var anchor = new GameObject("Image Anchor (Identity for orientation review)");
        var presentationRoot = new GameObject("Runtime Calibration Root (anchor × plane orientation)");
        presentationRoot.transform.SetParent(anchor.transform, false);
        presentationRoot.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        var visualRoot = new GameObject("Runtime VisualRoot (current world identity)");
        visualRoot.transform.SetParent(presentationRoot.transform, false);
        visualRoot.transform.rotation = Quaternion.identity;

        CreateAxes(anchor.transform, "Anchor Axes", 0.32f);
        CreateAxes(presentationRoot.transform, "Calibration Plane Axes", 0.25f);
        CreateAxes(visualRoot.transform, "VisualRoot Axes", 0.20f);

        CreateSample(AssetDatabase.LoadAssetAtPath<GameObject>(CandyPath), visualRoot.transform,
            "CandySoft — runtime orientation", new Vector3(-0.22f, 0f, 0f));
        CreateSample(AssetDatabase.LoadAssetAtPath<GameObject>(NaturePath), visualRoot.transform,
            "NatureSoft — runtime orientation", new Vector3(0.22f, 0f, 0f));

        CreateGround();
        CreateCamera();
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Selection.activeGameObject = visualRoot;
        Debug.Log("[Moodium Orientation Preview] Opened. Red=X, Green=Y (up), Blue=Z. " +
                  "VisualRoot currently uses world identity; prefab-authored rotation is preserved.");
    }

    static void CreateSample(GameObject prefab, Transform parent, string name, Vector3 localPosition)
    {
        if (prefab == null)
        {
            Debug.LogError($"[Moodium Orientation Preview] Missing prefab: {name}.");
            return;
        }
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        instance.name = name;
        instance.transform.SetParent(parent, false);
        instance.transform.localPosition = localPosition;
        instance.transform.localRotation = Quaternion.identity;
    }

    static void CreateAxes(Transform parent, string name, float length)
    {
        var root = new GameObject(name).transform;
        root.SetParent(parent, false);
        CreateAxis(root, Vector3.right, Color.red, "X", length);
        CreateAxis(root, Vector3.up, Color.green, "Y", length);
        CreateAxis(root, Vector3.forward, Color.blue, "Z", length);
    }

    static void CreateAxis(Transform parent, Vector3 direction, Color color, string label, float length)
    {
        var line = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        line.name = $"{label} axis ({(label == "X" ? "red" : label == "Y" ? "green" : "blue")})";
        line.transform.SetParent(parent, false);
        line.transform.localPosition = direction * (length * .5f);
        line.transform.localRotation = Quaternion.FromToRotation(Vector3.up, direction);
        line.transform.localScale = new Vector3(.008f, length * .5f, .008f);
        Object.DestroyImmediate(line.GetComponent<Collider>());
        line.GetComponent<Renderer>().sharedMaterial = CreateColorMaterial(color);

        var text = new GameObject($"{label} label").AddComponent<TextMesh>();
        text.transform.SetParent(parent, false);
        text.transform.localPosition = direction * (length + .025f);
        text.text = label;
        text.characterSize = .025f;
        text.fontSize = 64;
        text.color = color;
        text.anchor = TextAnchor.MiddleCenter;
    }

    static Material CreateColorMaterial(Color color)
    {
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.color = color;
        return material;
    }

    static void CreateGround()
    {
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground — X/Z plane (Y is up)";
        ground.transform.localScale = Vector3.one * .25f;
        ground.GetComponent<Renderer>().sharedMaterial = CreateColorMaterial(new Color(.14f, .15f, .19f));
    }

    static void CreateCamera()
    {
        var camera = new GameObject("Preview Camera").AddComponent<Camera>();
        camera.transform.position = new Vector3(.75f, .52f, -.75f);
        camera.transform.LookAt(new Vector3(0f, .06f, 0f));
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.045f, .05f, .075f);
        camera.nearClipPlane = .01f;
    }
}
