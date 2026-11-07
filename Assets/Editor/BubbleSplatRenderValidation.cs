using System;
using System.IO;
using Moodium.NatureWorld;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Real GPU render comparison, run without -nographics. No device results are inferred.</summary>
public static class BubbleSplatRenderValidation
{
    const string Output = "docs/validation/bubble-motion";
    [MenuItem("Moodium/Bubble World/Render Motion Validation Images")]
    public static void Run()
    {
        Directory.CreateDirectory(Output);
        var scene = EditorSceneManager.NewPreviewScene();
        var cameraObject = new GameObject("Motion Validation Camera");
        SceneManager.MoveGameObjectToScene(cameraObject, scene);
        var camera = cameraObject.AddComponent<Camera>();
        camera.scene = scene; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.025f, .035f, .055f);
        camera.fieldOfView = 48; camera.nearClipPlane = .05f; camera.farClipPlane = 100;
        var rt = new RenderTexture(768, 768, 24, RenderTextureFormat.ARGB32);
        var cloud = new BubbleWorldData.Cloud { Count = 31 * 31, EyeHeight = 1.3f,
            Positions = new Vector3[31 * 31], Sizes = new float[31 * 31], Colors = new Color32[31 * 31] };
        for (var y = 0; y < 31; y++)
        for (var x = 0; x < 31; x++)
        {
            var i = y * 31 + x;
            cloud.Positions[i] = new Vector3((x - 15) * .14f, (y - 15) * .14f, 6 + .2f * Mathf.Sin(x * .3f));
            cloud.Sizes[i] = .095f;
            cloud.Colors[i] = new Color(.2f + x / 50f, .5f + y / 90f, .7f - x / 100f, 1);
        }
        var texture = BubbleWorldData.BuildRoundAtlas(cloud);
        var mesh = BubbleWorldData.BuildLayerMesh(cloud, 0, cloud.Count, "GPU validation splats");
        var motion = BubbleSplatMaterial.Create(texture, 1.3f, false, true);
        var legacy = BubbleWorldData.CreateUnlit("static baseline", texture, true, CullMode.Off, Color.white);
        BubbleWorldData.ConfigureRoundPoints(legacy); BubbleWorldController.ConfigureTransparentPoints(legacy);
        var points = new GameObject("Validation Splats"); SceneManager.MoveGameObjectToScene(points, scene);
        points.AddComponent<MeshFilter>().sharedMesh = mesh; var renderer = points.AddComponent<MeshRenderer>();
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        Color32[] Capture(string name, Material mat)
        {
            renderer.sharedMaterial = mat;
            RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = rt });
            var old = RenderTexture.active; RenderTexture.active = rt;
            var image = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); image.Apply();
            RenderTexture.active = old;
            File.WriteAllBytes(Output + "/" + name + ".png", image.EncodeToPNG());
            var pixels = image.GetPixels32(); Object.DestroyImmediate(image); return pixels;
        }
        try
        {
            rt.Create();
            var baseline = Capture("01-legacy-static", legacy);
            var stationary = Capture("02-motion-static", motion);
            motion.SetFloat("_WindStrength", 1); motion.SetFloat("_Boost", 8);
            var moving = Capture("03-wind-boost8", motion);
            System.Threading.Thread.Sleep(1200); // Editor-only capture: URP uses realtimeSinceStartup here.
            var later = Capture("03b-wind-later", motion);
            motion.SetFloat("_Style", 1);
            Capture("04-candy-boost8", motion);
            motion.SetFloat("_Style", 0); motion.SetFloat("_WindStrength", 0); motion.SetFloat("_Boost", 3);
            motion.SetVector("_HandA", new Vector4(0, 0, .6f, .3f)); motion.SetVector("_HandAmount", new Vector4(1, 0, 0, 0));
            var hand = Capture("05-hand-ray", motion);
            motion.SetFloat("_DebugTint", 1); Capture("06-hand-debug", motion);
            motion.SetFloat("_DebugTint", 0); motion.SetVector("_HandA", new Vector4(0, 0, 6, .35f));
            var nearHand = Capture("07-hand-near", motion);
            var staticError = Difference(baseline, stationary); var windChange = Difference(stationary, moving); var handChange = Difference(stationary, hand);
            var temporalChange = Difference(moving, later); var nearChange = Difference(stationary, nearHand);
            var text = $"GPU: {SystemInfo.graphicsDeviceName}\nStatic baseline mean RGB error: {staticError:F6}\nBoost 8 wind RGB change: {windChange:F6}\nTemporal wind RGB change (>=1.2 s): {temporalChange:F6}\nHand ray RGB change: {handChange:F6}\nNear hand RGB change: {nearChange:F6}\n";
            File.WriteAllText(Output + "/results.txt", text); Debug.Log("[Bubble Motion] " + text);
            if (staticError > .015f || windChange < .002f || handChange < .0005f || temporalChange < .002f || nearChange < .0001f)
                throw new InvalidOperationException("GPU image comparison failed. Inspect " + Output);
            points.SetActive(false);
            foreach (var folder in new[] { BubbleWorldData.NatureFolder, BubbleWorldData.CandyFolder,
                         BubbleWorldData.NestedNatureFolder, BubbleWorldData.NestedCandyFolder, BubbleWorldData.MapleNatureFolder })
            {
                var root = new GameObject("World capture"); SceneManager.MoveGameObjectToScene(root, scene);
                var worldMaterial = BubbleSplatMaterial.Create(Resources.Load<Texture2D>(BubbleWorldData.RoundColorsResource(folder)), 1.3f, BubbleWorldData.IsCandy(folder), true);
                try
                {
                    for (var layer = 0; layer < BubbleWorldData.LayerCount; layer++)
                    {
                        var go = new GameObject("Layer " + layer); go.transform.SetParent(root.transform, false);
                        go.AddComponent<MeshFilter>().sharedMesh = Resources.Load<Mesh>(BubbleWorldData.MeshResource(folder, BubbleWorldData.LayerMeshName(layer)));
                        go.AddComponent<MeshRenderer>().sharedMaterial = worldMaterial;
                    }
                    var label = folder == BubbleWorldData.NatureFolder ? "nature" : folder == BubbleWorldData.CandyFolder ? "candy" :
                        folder == BubbleWorldData.NestedNatureFolder ? "nested-nature" : folder == BubbleWorldData.MapleNatureFolder ? "maple-nature" : "nested-candy";
                    var worldStatic = Capture(label + "-static", worldMaterial);
                    worldMaterial.SetFloat("_WindStrength", 1);
                    var worldMoving = Capture(label + "-normal-motion", worldMaterial);
                    var difference = Difference(worldStatic, worldMoving);
                    File.AppendAllText(Output + "/results.txt", $"{label} normal motion RGB change: {difference:F6}\n");
                    if (difference < .0001f) throw new InvalidOperationException("No visible normal-amplitude motion in " + label);
                    worldMaterial.SetVector("_HandA", new Vector4(0, 0, .6f, .3f));
                    worldMaterial.SetVector("_HandAmount", new Vector4(1, 0, 0, 0));
                    worldMaterial.SetFloat("_DebugTint", 1);
                    Capture(label + "-hand-debug", worldMaterial);
                }
                finally { Object.DestroyImmediate(root); Object.DestroyImmediate(worldMaterial); }
            }
        }
        finally
        {
            Object.DestroyImmediate(motion); Object.DestroyImmediate(legacy); Object.DestroyImmediate(texture); Object.DestroyImmediate(mesh);
            rt.Release(); Object.DestroyImmediate(rt); EditorSceneManager.ClosePreviewScene(scene);
        }
    }
    static float Difference(Color32[] a, Color32[] b)
    {
        double sum = 0;
        for (var i = 0; i < a.Length; i++) sum += Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b);
        return (float)(sum / (a.Length * 3 * 255));
    }
}
