using System.IO;
using Moodium.NatureWorld;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Portal probe (device test): creates the occlusion material in Resources so its shader ships in the build
/// (a shader only found by name at runtime is stripped).
/// </summary>
public static class PortalProbeSetup
{
    public const string MaterialPath = "Assets/Resources/" + BubblePortalProbe.OccluderResource + ".mat";

    [MenuItem("Moodium/Portal Probe/Create Occluder Material")]
    public static void CreateOccluderMaterial()
    {
        Shader shader = null;
        foreach (var name in BubblePortalProbe.OccluderShaders)
            if ((shader = Shader.Find(name)) != null) break;
        if (shader == null)
        {
            Debug.LogError("[Portal Probe] Neither \"AR/Basic Occlusion\" nor \"AR/Occlusion\" is in the project. " +
                           "Look for it in the installed packages (AR Foundation / PolySpatial) before testing.");
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(MaterialPath));
        var existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (existing != null) existing.shader = shader;
        else AssetDatabase.CreateAsset(new Material(shader) { name = "PortalOccluder" }, MaterialPath);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Portal Probe] {MaterialPath} uses {shader.name}");
    }
}
