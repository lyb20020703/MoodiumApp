using Moodium.NatureWorld;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>Isolated editor sandbox. Does not change the open scene or resource materials.</summary>
public sealed class BubbleSplatMotionPreviewWindow : EditorWindow
{
    static readonly string[] Names = { "自然", "糖果", "自然第二层", "糖果第二层", "枫林" };
    static readonly string[] Folders = { BubbleWorldData.NatureFolder, BubbleWorldData.CandyFolder,
        BubbleWorldData.NestedNatureFolder, BubbleWorldData.NestedCandyFolder, BubbleWorldData.MapleNatureFolder };
    Scene scene;
    Camera camera;
    GameObject root, sky;
    Material material, skyMaterial;
    RenderTexture target;
    int selected;
    float strength = 1, boost = 1, yaw, pitch, handAmount;
    bool debug, skyVisible = true;
    Vector3 hand = new Vector3(0, 0, .6f);
    double lastRepaint;

    [MenuItem("Moodium/Bubble World/Open GPU Motion Sandbox")]
    public static void Open() => GetWindow<BubbleSplatMotionPreviewWindow>("点云运动预览");
    void OnEnable()
    {
        scene = EditorSceneManager.NewPreviewScene();
        var go = new GameObject("Sandbox Camera"); SceneManager.MoveGameObjectToScene(go, scene);
        camera = go.AddComponent<Camera>(); camera.scene = scene;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.025f, .035f, .055f);
        camera.fieldOfView = 60; camera.nearClipPlane = .05f; camera.farClipPlane = BubbleWorldController.SkyMaxRadius + 10;
        target = new RenderTexture(768, 768, 24); target.Create();
        LoadWorld(); EditorApplication.update += Tick;
    }
    void Tick()
    {
        // Preview refresh rate is capped; it must not be used as a device performance measurement.
        if (focusedWindow == this && EditorApplication.timeSinceStartup - lastRepaint > .1)
        { lastRepaint = EditorApplication.timeSinceStartup; Repaint(); }
    }
    void LoadWorld()
    {
        if (root != null) DestroyImmediate(root);
        if (material != null) DestroyImmediate(material);
        if (skyMaterial != null) DestroyImmediate(skyMaterial);
        root = new GameObject("GPU Motion Sandbox"); SceneManager.MoveGameObjectToScene(root, scene);
        var folder = Folders[selected];
        material = BubbleSplatMaterial.Create(Resources.Load<Texture2D>(BubbleWorldData.RoundColorsResource(folder)), 1.3f, BubbleWorldData.IsCandy(folder), true);
        for (var i = 0; i < BubbleWorldData.LayerCount; i++)
        {
            var layer = new GameObject("Layer " + i); layer.transform.SetParent(root.transform, false);
            layer.AddComponent<MeshFilter>().sharedMesh = Resources.Load<Mesh>(BubbleWorldData.MeshResource(folder, BubbleWorldData.LayerMeshName(i)));
            layer.AddComponent<MeshRenderer>().sharedMaterial = material;
        }
        sky = new GameObject("Sky"); sky.transform.SetParent(root.transform, false);
        sky.transform.localScale = Vector3.one * BubbleWorldController.SkyMaxRadius;
        sky.AddComponent<MeshFilter>().sharedMesh = Resources.Load<Mesh>(BubbleWorldData.MeshResource(folder, BubbleWorldData.PanoramaMeshName));
        skyMaterial = BubbleWorldData.CreateUnlit("Sandbox sky", Resources.Load<Texture2D>(BubbleWorldData.PanoramaResource(folder)), false, CullMode.Back, Color.white);
        skyMaterial.renderQueue = 1000;
        sky.AddComponent<MeshRenderer>().sharedMaterial = skyMaterial;
    }
    void OnGUI()
    {
        if (camera == null || material == null || target == null) return;
        EditorGUI.BeginChangeCheck();
        var next = EditorGUILayout.Popup("世界", selected, Names);
        if (EditorGUI.EndChangeCheck()) { selected = next; LoadWorld(); }
        strength = EditorGUILayout.Slider("风 / 漂浮强度", strength, 0, 1);
        boost = EditorGUILayout.Slider("调试倍数", boost, 0, 8);
        debug = EditorGUILayout.Toggle("显示手部和摆动权重", debug);
        handAmount = EditorGUILayout.Slider("模拟手部强度", handAmount, 0, 1);
        hand = EditorGUILayout.Vector3Field("模拟掌心位置（米）", hand);
        yaw = EditorGUILayout.Slider("视角左右", yaw, -180, 180);
        pitch = EditorGUILayout.Slider("视角上下", pitch, -80, 80);
        skyVisible = EditorGUILayout.Toggle("显示全景背景", skyVisible);
        EditorGUILayout.HelpBox("这是 Unity GPU 预览：手部参数为模拟输入，窗口最多刷新 10 次/秒，不代表真机追踪或帧率。强度设为 0、模拟手部设为 0 可检查静态画面。", MessageType.Info);
        var rect = GUILayoutUtility.GetAspectRect(1, GUILayout.ExpandWidth(true));
        if (Event.current.type != EventType.Repaint) return;
        material.SetFloat("_WindStrength", strength); material.SetFloat("_Boost", boost); material.SetFloat("_DebugTint", debug ? 1 : 0);
        material.SetVector("_HandA", new Vector4(hand.x, hand.y, hand.z, .3f));
        material.SetVector("_HandAmount", new Vector4(handAmount, 0, 0, 0));
        sky.SetActive(skyVisible); camera.transform.rotation = Quaternion.Euler(pitch, yaw, 0);
        RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
        GUI.DrawTexture(rect, target, ScaleMode.ScaleToFit, false);
    }
    void OnDisable()
    {
        EditorApplication.update -= Tick;
        if (material != null) DestroyImmediate(material);
        if (skyMaterial != null) DestroyImmediate(skyMaterial);
        if (target != null) { target.Release(); DestroyImmediate(target); }
        if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
    }
}
