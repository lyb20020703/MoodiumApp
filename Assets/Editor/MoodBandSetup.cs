using System.IO;
using System.Reflection;
using Moodium.CandyWorld;
using TMPro;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

public static class MoodBandSetup
{
    [MenuItem("Moodium/Validate Wrist UI")]
    public static void Validate()=>Run(false);
    [MenuItem("Moodium/Validate All EditMode Tests")]
    public static void ValidateAll()=>Run(true);
    static void Run(bool all)
    {
        var api=ScriptableObject.CreateInstance<TestRunnerApi>();api.RegisterCallbacks(new Results(all));
        var filter=new Filter{testMode=TestMode.EditMode};
        if(!all)filter.groupNames=new[]{"^MoodBand","^CandyEnergyWristRing","^WristExitConfirmation"};
        api.Execute(new ExecutionSettings(filter));
    }
    sealed class Results:ICallbacks
    {
        readonly bool all;public Results(bool allTests){all=allTests;}
        public void RunStarted(ITestAdaptor test){}
        public void TestStarted(ITestAdaptor test){}
        public void TestFinished(ITestResultAdaptor result){}
        public void RunFinished(ITestResultAdaptor result)
        {
            var path=Path.Combine(Path.GetTempPath(),all?"Moodium-Wrist-AllTests.xml":"Moodium-Wrist-EditMode.xml");
            TestRunnerApi.SaveResultToFile(result,path);Debug.Log("[Mood Band] Test results: "+path);
        }
    }
    [MenuItem("Moodium/Preview Wrist UI")]
    public static void Preview()
    {
        foreach(var english in new[]{false,true})foreach(var state in new[]{"ambient","glance","full","confirm","nature"})
            Render(english,state);
    }
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static void Set(object ui,string field,object value)=>ui.GetType().GetField(field,Private).SetValue(ui,value);
    static void Call(object ui,string method,params object[] args)=>ui.GetType().GetMethod(method,Private).Invoke(ui,args);
    static void Render(bool english,string state)
    {
        var root=new GameObject("Isolated Wrist Preview");var cameraObject=new GameObject("Wrist Preview Camera");
        var lightObject=new GameObject("Wrist Preview Light");var prior=RenderTexture.active;
        var hadLanguage=PlayerPrefs.HasKey("Moodium.Language");var language=PlayerPrefs.GetString("Moodium.Language");
        RenderTexture target=null;Texture2D image=null;Material skin=null;
        try
        {
            PlayerPrefs.SetString("Moodium.Language",english?"English":"Chinese");
            var camera=cameraObject.AddComponent<Camera>();camera.transform.position=new Vector3(.015f,.28f,-.32f);
            camera.transform.LookAt(new Vector3(0,.015f,0));camera.orthographic=true;camera.orthographicSize=.115f;
            camera.cullingMask=1<<31;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.07f,.065f,.085f);
            var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.2f;light.transform.rotation=Quaternion.Euler(35,-30,0);
            var font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/UI/Moodium/Fonts/"+(english?"Inter-Regular SDF.asset":"AlibabaPuHuiTi Moodium SDF.asset"));
            var ui=root.AddComponent<CandyEnergyWristUI>();ui.Build(camera,font,null,null);
            root.transform.rotation=Quaternion.LookRotation(Vector3.back,Vector3.up);
            ui.SetWorldTheme(state=="nature");ui.SetExitAvailable(true);
            var progress=state=="full"?1f:state=="confirm"?.62f:.37f;
            ui.SetProgress(progress*100,progress);ui.SetReleaseAvailable(state=="full");
            Set(ui,"m_DisplayedProgress",progress);Set(ui,"m_DisplayedEnergy",progress*100);
            Set(ui,"m_VisualAlpha",1f);Set(ui,"m_Glance",state=="ambient"?0f:1f);
            Set(ui,"m_Glancing",state!="ambient");Set(ui,"m_InputReady",state!="ambient");
            typeof(CandyEnergyWristUI).GetProperty("HandTracked").SetValue(ui,true);
            Call(ui,"UpdateProgressAnimation",0f);Call(ui,"UpdateCardAndToast",0f);
            if(state=="confirm")Call(ui,"PressExit");
            Call(ui,"UpdateExitConfirmation");Call(ui,"ApplyColors");
            var arm=GameObject.CreatePrimitive(PrimitiveType.Cylinder);arm.name="Preview Forearm";arm.transform.SetParent(root.transform,false);
            arm.transform.localPosition=new Vector3(0,-.105f,0);arm.transform.localScale=new Vector3(.06f,.11f,.047f);
            skin=new Material(Shader.Find("Universal Render Pipeline/Lit"));skin.SetColor("_BaseColor",new Color(.7f,.43f,.30f));arm.GetComponent<Renderer>().sharedMaterial=skin;
            foreach(var child in root.GetComponentsInChildren<Transform>(true))child.gameObject.layer=31;
            Canvas.ForceUpdateCanvases();target=new RenderTexture(1200,1000,24);camera.targetTexture=target;camera.Render();
            RenderTexture.active=target;image=new Texture2D(1200,1000,TextureFormat.RGBA32,false);
            image.ReadPixels(new Rect(0,0,1200,1000),0,0);image.Apply();
            var path=Path.Combine(Path.GetTempPath(),"MoodBand-"+state+"-"+(english?"en":"cn")+".png");
            File.WriteAllBytes(path,image.EncodeToPNG());Debug.Log("[Mood Band] Preview: "+path);
        }
        finally
        {
            if(hadLanguage)PlayerPrefs.SetString("Moodium.Language",language);else PlayerPrefs.DeleteKey("Moodium.Language");
            RenderTexture.active=prior;Object.DestroyImmediate(root);Object.DestroyImmediate(cameraObject);Object.DestroyImmediate(lightObject);
            if(skin!=null)Object.DestroyImmediate(skin);if(image!=null)Object.DestroyImmediate(image);
            if(target!=null){target.Release();Object.DestroyImmediate(target);}
        }
    }
}
