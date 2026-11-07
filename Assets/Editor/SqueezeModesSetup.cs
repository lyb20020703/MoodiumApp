using System.IO;
using Moodium.Squeeze;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

public static class SqueezeModesSetup
{
    const string Root="Assets/UI/Moodium/SqueezeModes/Resources/MoodiumSqueezeModes";
    [MenuItem("Moodium/Setup Squeeze Modes")]
    public static void Setup()
    {
        foreach(var path in Directory.GetFiles(Root,"*.png"))
        {
            var importer=AssetImporter.GetAtPath(path) as TextureImporter;
            if(importer==null)continue;
            importer.textureType=TextureImporterType.Sprite;
            importer.spriteImportMode=SpriteImportMode.Single;
            importer.alphaIsTransparency=true;
            importer.mipmapEnabled=false;
            importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.wrapMode=TextureWrapMode.Clamp;
            if(path.EndsWith("_band.png"))importer.wrapModeU=TextureWrapMode.Repeat;
            importer.SaveAndReimport();
        }
        var tuningPath=Root+"/SqueezeTuning.asset";
        if(AssetDatabase.LoadAssetAtPath<SqueezeTuning>(tuningPath)==null)
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<SqueezeTuning>(),tuningPath);
        AssetDatabase.SaveAssets();
        Debug.Log("[Squeeze Modes] Native tabletop assets and editable tuning are ready.");
    }
    [MenuItem("Moodium/Validate Squeeze Modes")]
    public static void Validate()
    {
        var api=ScriptableObject.CreateInstance<TestRunnerApi>();
        api.RegisterCallbacks(new Results());
        api.Execute(new ExecutionSettings(new Filter {testMode=TestMode.EditMode,
            groupNames=new[]{"^Squeeze","^CandyEnergy","^ChocolateCapsule","^PalmPress"}}));
    }
    [MenuItem("Moodium/Preview Squeeze Modes")]
    public static void Preview()
    {
        Setup();
        foreach(var nature in new[]{false,true})foreach(var selected in new[]{SqueezeMode.Rhythm,SqueezeMode.Breath})
        {
            var session=new GameObject("Squeeze Preview Session");var bag=new GameObject("Preview Bag Anchor");
            var cameraObject=new GameObject("Squeeze Preview Camera");
            var previous=RenderTexture.active;RenderTexture target=null;Texture2D image=null;
            try
            {
                var camera=cameraObject.AddComponent<Camera>();camera.transform.position=new Vector3(0,.65f,.62f);
                camera.transform.LookAt(new Vector3(0,-.05f,.04f));camera.orthographic=true;camera.orthographicSize=.37f;
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.055f,.065f,.075f);camera.cullingMask=1<<31;
                var controller=session.AddComponent<SqueezeModeController>();
                controller.BeginSession(session.AddComponent<Moodium.CandyWorld.CandyEnergyController>(),null,nature,_=>true);
                controller.RequestMode(selected);
                var capsule=bag.AddComponent<Moodium.Interaction.ChocolateCapsuleInteraction>();
                var view=session.GetComponent<SqueezeTableView>();view.Bind(capsule);
                view.TableRoot.GetChild(0).localRotation=Quaternion.identity;
                foreach(var child in view.TableRoot.GetComponentsInChildren<Transform>(true))child.gameObject.layer=31;
                // Render a static presentation after the one-second reveal, without changing Editor time.
                var tick=typeof(SqueezeTableView).GetMethod("Update",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
                tick.Invoke(view,null);
                var labels=(TableTextSlot)typeof(SqueezeTableView).GetField("text",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(view);
                labels.ShowHint(selected==SqueezeMode.Rhythm?"rhythm_hint":"ready");
                // The real bar is reveal-gated. Advance its birth time only in this isolated preview.
                var buttons=typeof(SqueezeTableView).GetField("buttons",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(view);
                typeof(ModeButtonBar).GetField("born",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(buttons,Time.unscaledTime-2);
                ((ModeButtonBar)buttons).Tick(1);
                foreach(var child in view.TableRoot.GetComponentsInChildren<Transform>(true))child.gameObject.layer=31;
                target=new RenderTexture(1200,1000,24);camera.targetTexture=target;camera.Render();RenderTexture.active=target;
                image=new Texture2D(1200,1000,TextureFormat.RGBA32,false);image.ReadPixels(new Rect(0,0,1200,1000),0,0);image.Apply();
                var path=Path.Combine(Path.GetTempPath(),"Squeeze-"+(nature?"nature":"candy")+"-"+selected+".png");File.WriteAllBytes(path,image.EncodeToPNG());
                Debug.Log("[Squeeze Preview] "+path);
            }
            finally
            {
                RenderTexture.active=previous;session.GetComponent<SqueezeModeController>()?.StopSession();
                Object.DestroyImmediate(session);Object.DestroyImmediate(bag);Object.DestroyImmediate(cameraObject);
                if(target!=null){target.Release();Object.DestroyImmediate(target);}if(image!=null)Object.DestroyImmediate(image);
            }
        }
    }
    sealed class Results:ICallbacks
    {
        public void RunStarted(ITestAdaptor test){}
        public void TestStarted(ITestAdaptor test){}
        public void TestFinished(ITestResultAdaptor result){}
        public void RunFinished(ITestResultAdaptor result)
        {
            var path=Path.Combine(Path.GetTempPath(),"Moodium-Squeeze-EditMode.xml");
            TestRunnerApi.SaveResultToFile(result,path);
            Debug.Log("[Squeeze Modes] Results: "+path);
        }
    }
}
