using System;
using System.Reflection;
using System.IO;
using Moodium.UI;
using Moodium.CandyWorld;
using Moodium.Calibration;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
public static class UIRefreshPreview
{
    const string Folder="docs/verification/ui-refresh-preview";
    static TMP_FontAsset Font(string guid)=>AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(guid));
    [MenuItem("Moodium/UI Refresh/Capture Verification Previews")]
    public static void Capture()
    {
        Directory.CreateDirectory(Folder);
        var chinese=Font("87c6dc9cf9eb7482f92d732e550a4b92");var english=Font("5d946757aa5b8410aba5b97ed2216bfa");
        foreach(var progress in new[]{.37f,1f})Render("energy-"+Mathf.RoundToInt(progress*100),.115f,(root,cam)=>{
            var soft=new GameObject("Preview Soft Anchor");soft.transform.SetParent(root.transform,false);
            var ring=root.AddComponent<FloatingSqueezeProgress>();ring.ConfigureFonts(chinese,english);ring.Bind(soft.transform,cam,progress>=1);ring.SetProgress(progress);
            typeof(FloatingSqueezeProgress).GetMethod("Refresh",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(ring,new object[]{Time.unscaledTime+10});
        });
        foreach(var en in new[]{false,true})Render(en?"settings-en":"settings-zh",.18f,(root,cam)=>root.AddComponent<SelectionSettingsVisual>().Initialize(en,!en,(_,__)=>{},()=>{}));
        Render("calibration-rotate",.42f,(root,cam)=>{
            cam.transform.position=new Vector3(0,.7f,-.85f);cam.transform.LookAt(new Vector3(0,0,.04f));
            var anchor=new GameObject("Preview Anchor");anchor.transform.SetParent(root.transform,false);
            var calibrator=root.AddComponent<SensoryBagCalibrator>();calibrator.ConfigureFonts(chinese,english);
            var settings=calibrator.CreateInspectorSettings();calibrator.Begin(anchor.transform,SensoryBagCalibrationData.CreateDefault("Preview",SensoryBagWorldKind.Candy,settings),settings);
            calibrator.RotateFrame(32);calibrator.SyncFrameNow();
            typeof(SensoryBagCalibrator).GetField("m_Viewer",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(calibrator,cam.transform);
            typeof(SensoryBagCalibrator).GetMethod("UpdateSpatialLayout",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(calibrator,null);
            var dial=calibrator.FrameRoot.GetComponent<CalibrationRotateDial>();dial.Refresh(calibrator.CurrentData.Width,calibrator.CurrentData.Height,32,true,false,cam.transform);
            calibrator.BeginPointerDrag(12,CalibrationHandleKind.Rotate,dial.Knob.position);
            typeof(SensoryBagCalibrator).GetMethod("UpdateStateVisuals",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(calibrator,null);
        });
        Debug.Log("[UI Refresh] Actual Unity component previews captured: "+Folder);
    }
    static void Render(string name,float size,Action<GameObject,Camera> build)
    {
        var scene=EditorSceneManager.NewPreviewScene();var root=new GameObject("UI Refresh Preview");var cameraObject=new GameObject("UI Preview Camera");
        SceneManager.MoveGameObjectToScene(root,scene);SceneManager.MoveGameObjectToScene(cameraObject,scene);
        var camera=cameraObject.AddComponent<Camera>();camera.scene=scene;camera.orthographic=true;camera.orthographicSize=size;
        camera.transform.position=new Vector3(0,0,-1);camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.13f,.115f,.16f);camera.nearClipPlane=.01f;camera.farClipPlane=10;
        var rt=new RenderTexture(1200,900,24);var previous=RenderTexture.active;
        try
        {
            build(root,camera);foreach(var text in root.GetComponentsInChildren<TMP_Text>())text.ForceMeshUpdate();
            camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;var image=new Texture2D(1200,900,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1200,900),0,0);image.Apply();File.WriteAllBytes(Folder+"/"+name+".png",image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);
        }
        finally{camera.targetTexture=null;RenderTexture.active=previous;rt.Release();UnityEngine.Object.DestroyImmediate(rt);EditorSceneManager.ClosePreviewScene(scene);}
    }
}
