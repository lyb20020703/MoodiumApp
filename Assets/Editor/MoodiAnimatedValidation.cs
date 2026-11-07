using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using System.Linq;
using Moodium.Characters;

public static class MoodiAnimatedValidation
{
    [MenuItem("Moodium/Moodi/Preview Animated Moodi")]
    public static void Preview()
    {
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/prefab/Moodi meshy.prefab");
        if(prefab==null)return;
        foreach(var state in new[]{"Static","Idle","Walk","Touched","Happy","Wave"})
            Render(prefab,state);
    }
    static void Render(GameObject prefab,string state)
    {
        var root=(GameObject)PrefabUtility.InstantiatePrefab(prefab);
        var cameraObject=new GameObject("Isolated Moodi Camera");var lightObject=new GameObject("Isolated Moodi Light");
        var previous=RenderTexture.active;RenderTexture target=null;Texture2D image=null;
        try
        {
            root.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
            var staticModel=root.transform.Find("moodi");var rigged=root.transform.Find("moodi_rigged");
            staticModel.gameObject.SetActive(true);rigged.gameObject.SetActive(false);
            var bounds=staticModel.GetComponentInChildren<Renderer>().bounds;
            var camera=cameraObject.AddComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;
            camera.backgroundColor=new Color(.07f,.065f,.085f);camera.orthographic=true;camera.orthographicSize=bounds.size.y*.7f;
            camera.cullingMask=1<<31;camera.transform.position=bounds.center+Vector3.back*bounds.size.y*3+Vector3.up*bounds.size.y*.12f;
            camera.transform.LookAt(bounds.center);
            var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.4f;
            light.transform.rotation=Quaternion.Euler(30,-20,0);
            if(state!="Static")
            {
                staticModel.gameObject.SetActive(false);rigged.gameObject.SetActive(true);
                var animator=rigged.GetComponent<Animator>();animator.enabled=false;
                var clip=animator.runtimeAnimatorController.animationClips.First(c=>c.name==state);
                clip.SampleAnimation(rigged.gameObject,clip.length*(state=="Happy"?.5f:state=="Touched"?.2f:.35f));
            }
            foreach(var child in root.GetComponentsInChildren<Transform>(true))child.gameObject.layer=31;
            target=new RenderTexture(800,800,24);camera.targetTexture=target;camera.Render();RenderTexture.active=target;
            image=new Texture2D(800,800,TextureFormat.RGBA32,false);image.ReadPixels(new Rect(0,0,800,800),0,0);image.Apply();
            var path=Path.Combine(Path.GetTempPath(),"Moodi-Animated-"+state+".png");
            File.WriteAllBytes(path,image.EncodeToPNG());Debug.Log("[Moodi Animated] Preview: "+path);
        }
        finally
        {
            RenderTexture.active=previous;Object.DestroyImmediate(root);Object.DestroyImmediate(cameraObject);Object.DestroyImmediate(lightObject);
            if(target!=null){target.Release();Object.DestroyImmediate(target);}if(image!=null)Object.DestroyImmediate(image);
        }
    }
    [MenuItem("Moodium/Moodi/Validate Animated Moodi")]
    public static void Validate()
    {
        var api=ScriptableObject.CreateInstance<TestRunnerApi>();api.RegisterCallbacks(new Results());
        api.Execute(new ExecutionSettings(new Filter{testMode=TestMode.EditMode,
            groupNames=new[]{"^MoodiAnimatedTests","^MoodiBreathingFloatTests","^MoodiSelectionMascotControllerTests","^WalkTrailGuideControllerTests"}}));
    }
    sealed class Results:ICallbacks
    {
        public void RunStarted(ITestAdaptor test){}
        public void TestStarted(ITestAdaptor test){}
        public void TestFinished(ITestResultAdaptor result){}
        public void RunFinished(ITestResultAdaptor result)
        {
            var path=Path.Combine(Path.GetTempPath(),"Moodium-Animated-EditMode.xml");
            TestRunnerApi.SaveResultToFile(result,path);Debug.Log("[Moodi Animated] Results: "+path);
        }
    }
}
