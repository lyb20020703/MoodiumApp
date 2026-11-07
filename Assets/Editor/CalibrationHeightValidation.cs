using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

public static class CalibrationHeightValidation
{
    [MenuItem("Moodium/Preview Calibration Height")]
    public static void Preview()
    {
        var previousLanguage=PlayerPrefs.GetString("Moodium.Language","Chinese");
        foreach(var english in new[]{false,true})
        {
            var host=new GameObject("Calibration Preview");var anchor=new GameObject("Top sticker anchor");var cameraObject=new GameObject("Calibration Camera");
            var table=GameObject.CreatePrimitive(PrimitiveType.Cube);var sticker=GameObject.CreatePrimitive(PrimitiveType.Cube);
            RenderTexture target=null;Texture2D image=null;var previous=RenderTexture.active;
            var calibrator=host.AddComponent<Moodium.Calibration.SensoryBagCalibrator>();
            try
            {
                PlayerPrefs.SetString("Moodium.Language",english?"English":"Chinese");
                calibrator.ConfigureFonts(
                    AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>("Assets/UI/Moodium/Fonts/AlibabaPuHuiTi Moodium SDF.asset"),
                    AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>("Assets/UI/Moodium/Fonts/Inter-Regular SDF.asset"));
                var settings=calibrator.CreateInspectorSettings();
                calibrator.Begin(anchor.transform,Moodium.Calibration.SensoryBagCalibrationData.CreateDefault("Preview",Moodium.Calibration.SensoryBagWorldKind.Candy,settings),settings);
                table.transform.position=new Vector3(.08f,-.054f,0);table.transform.localScale=new Vector3(.9f,.005f,.6f);
                sticker.transform.position=Vector3.zero;sticker.transform.localScale=new Vector3(.025f,.001f,.025f);
                var camera=cameraObject.AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=.35f;
                camera.transform.position=new Vector3(.12f,.7f,-.7f);camera.transform.LookAt(new Vector3(.1f,-.02f,-.025f));
                camera.cullingMask=1<<31;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.10f,.10f,.13f);
                foreach(var child in host.GetComponentsInChildren<Transform>(true))child.gameObject.layer=31;
                table.layer=sticker.layer=31;
                target=new RenderTexture(1100,850,24);camera.targetTexture=target;camera.Render();RenderTexture.active=target;
                image=new Texture2D(1100,850,TextureFormat.RGBA32,false);image.ReadPixels(new Rect(0,0,1100,850),0,0);image.Apply();
                File.WriteAllBytes(Path.Combine(Path.GetTempPath(),$"CalibrationHeight-{(english?"en":"zh")}.png"),image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active=previous;
                Object.DestroyImmediate(host);Object.DestroyImmediate(anchor);Object.DestroyImmediate(cameraObject);Object.DestroyImmediate(table);Object.DestroyImmediate(sticker);
                if(target!=null){target.Release();Object.DestroyImmediate(target);}if(image!=null)Object.DestroyImmediate(image);
                PlayerPrefs.SetString("Moodium.Language",previousLanguage);
            }
        }
        Debug.Log("[Calibration height] Preview: "+Path.GetTempPath());
    }
    [MenuItem("Moodium/Validate Calibration Height")]
    public static void Validate()
    {
        var api=ScriptableObject.CreateInstance<TestRunnerApi>();api.RegisterCallbacks(new Results());
        api.Execute(new ExecutionSettings(new Filter{testMode=TestMode.EditMode,groupNames=new[]{"^CalibrationHeightTests","^SensoryBagCalib","^CalibratedSensoryBagPresentationTests","^TrackedSqueezeOrientationTests","^SqueezeProgressVideoControllerTests"}}));
    }
    sealed class Results:ICallbacks
    {
        public void RunStarted(ITestAdaptor test){}public void TestStarted(ITestAdaptor test){}public void TestFinished(ITestResultAdaptor result){}
        public void RunFinished(ITestResultAdaptor result)
        {
            var path=Path.Combine(Path.GetTempPath(),"Moodium-CalibrationHeight-Tests.xml");TestRunnerApi.SaveResultToFile(result,path);Debug.Log("[Calibration height] Tests: "+path);
        }
    }
}
