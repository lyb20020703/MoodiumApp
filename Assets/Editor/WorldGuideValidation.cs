using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using Moodium.Guide;
using System.Reflection;

public static class WorldGuideValidation
{
    [MenuItem("Moodium/Preview World Guide Cards")]
    public static void Preview()
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        foreach(var nature in new[]{false,true})foreach(var english in new[]{false,true})
        for(int page=0;page<5;page++)
        {
            var owner=new GameObject("World Guide Preview");var cameraObject=new GameObject("World Guide Preview Camera");
            RenderTexture target=null;Texture2D image=null;var previous=RenderTexture.active;
            var guide=owner.AddComponent<WorldGuideCards>();
            try
            {
                var camera=cameraObject.AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=.32f;
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.09f,.085f,.11f);
                camera.cullingMask=1<<31;
                if(page<4)guide.ShowTutorial(nature,english,camera.transform);
                else guide.ShowExplore(nature,english,camera.transform,0);
                void Call(string name,params object[] args)=>typeof(WorldGuideCards).GetMethod(name,flags).Invoke(guide,args);
                Call("StopTransition");
                if(page<4){typeof(WorldGuideCards).GetField("m_Index",flags).SetValue(guide,page);Call("ApplyPage");}
                Call("SetAlpha",1f);
                var panel=(Transform)typeof(WorldGuideCards).GetField("m_Root",flags).GetValue(guide);
                var pivot=panel.Find("Card");pivot.localScale=Vector3.one;pivot.localPosition=Vector3.zero;
                foreach(var child in panel.GetComponentsInChildren<Transform>(true))child.gameObject.layer=31;
                camera.transform.position=new Vector3(0,panel.position.y,0);camera.transform.rotation=Quaternion.identity;
                target=new RenderTexture(900,1000,24);camera.targetTexture=target;camera.Render();RenderTexture.active=target;
                image=new Texture2D(900,1000,TextureFormat.RGBA32,false);image.ReadPixels(new Rect(0,0,900,1000),0,0);image.Apply();
                var path=Path.Combine(Path.GetTempPath(),$"WorldGuide-{(nature?"nature":"candy")}-{(english?"en":"zh")}-{page}.png");
                File.WriteAllBytes(path,image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active=previous;guide.Hide(true);Object.DestroyImmediate(owner);Object.DestroyImmediate(cameraObject);
                if(target!=null){target.Release();Object.DestroyImmediate(target);}if(image!=null)Object.DestroyImmediate(image);
            }
        }
        Debug.Log("[World Guide] Preview images saved in "+Path.GetTempPath());
    }
    [MenuItem("Moodium/Validate World Guide And Orientation")]
    public static void Validate()
    {
        var api=ScriptableObject.CreateInstance<TestRunnerApi>();api.RegisterCallbacks(new Results());
        api.Execute(new ExecutionSettings(new Filter{testMode=TestMode.EditMode,groupNames=new[]{
            "^WorldGuide","^TrackedSqueezeOrientationTests","^SqueezeProgressVideoControllerTests","^WalkTrailGuideControllerTests"}}));
    }
    sealed class Results:ICallbacks
    {
        public void RunStarted(ITestAdaptor test){}
        public void TestStarted(ITestAdaptor test){}
        public void TestFinished(ITestResultAdaptor result){}
        public void RunFinished(ITestResultAdaptor result)
        {
            var path=Path.Combine(Path.GetTempPath(),"Moodium-WorldGuide-Tests.xml");
            TestRunnerApi.SaveResultToFile(result,path);Debug.Log("[World Guide] Tests: "+path);
        }
    }
}
