using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
public static class MoodiEmergenceSetup
{
    const string Output="Assets/Animations/Moodi/";
    public static void PreserveOriginalClips()
    {
        const string source="Assets/Editor/MoodiOriginalVerification.fbx";
        if(!File.Exists(source))return;
        Directory.CreateDirectory(Output+"OriginalClips");
        foreach(var clip in AssetDatabase.LoadAllAssetsAtPath(source).OfType<AnimationClip>())
        {
            var name=MoodiAnimatedSetup.ShortClipName(clip.name);
            if(!new[]{"Idle","Walk","Touched","Happy","Wave"}.Contains(name))continue;
            var path=Output+"OriginalClips/"+name+".anim";
            if(AssetDatabase.LoadAssetAtPath<AnimationClip>(path)!=null)continue;
            var copy=UnityEngine.Object.Instantiate(clip);copy.name=name;
            var settings=AnimationUtility.GetAnimationClipSettings(copy);settings.loopTime=name=="Idle"||name=="Walk";
            AnimationUtility.SetAnimationClipSettings(copy,settings);AssetDatabase.CreateAsset(copy,path);
        }
        AssetDatabase.SaveAssets();
    }
    public static void UseOriginalClips(Dictionary<string,AnimationClip> clips)
    {
        foreach(var name in new[]{"Idle","Walk","Touched","Happy","Wave"})
        {
            var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(Output+"OriginalClips/"+name+".anim");
            if(clip!=null)clips[name]=clip;
        }
    }
    public static void AugmentMoodi(AnimatorController c,Dictionary<string,AnimationClip> clips)
    {
        if(!clips.ContainsKey("Emerge"))return;
        Param(c,"EmergeProgress",AnimatorControllerParameterType.Float);Param(c,"Press",AnimatorControllerParameterType.Float);
        Param(c,"Emerging",AnimatorControllerParameterType.Bool);Param(c,"Peeking",AnimatorControllerParameterType.Bool);
        Param(c,"FallIn",AnimatorControllerParameterType.Trigger);
        var sm=c.layers[0].stateMachine;
        var emerge=State(sm,"Emerge",clips["Emerge"],"EmergeProgress");
        var peek=State(sm,"Peek",clips["Peek"]);
        var fall=State(sm,"FallIn",clips["FallIn"]);
        // Exported clips report 24 fps, while the supplied animation is authored at 30.
        fall.speed=clips["FallIn"].length/.5f;
        peek.speed=clips["Peek"].length/1.2f;
        foreach(var pair in new[]{(emerge,"Emerging"),(peek,"Peeking"),(fall,"FallIn")})
        {var t=sm.AddAnyStateTransition(pair.Item1);t.hasExitTime=false;t.duration=.06f;t.canTransitionToSelf=false;t.AddCondition(AnimatorConditionMode.If,0,pair.Item2);}
        var idle=sm.states.First(s=>s.state.name=="Idle").state;
        foreach(var s in new[]{emerge,peek}){var t=s.AddTransition(idle);t.hasExitTime=false;t.duration=.1f;t.AddCondition(AnimatorConditionMode.IfNot,0,s==emerge?"Emerging":"Peeking");}
        var back=fall.AddTransition(peek);back.hasExitTime=true;back.exitTime=1;back.duration=0;
        AddLayer(c,"Strain",clips["Strain"],"Press");
    }
    static void Param(AnimatorController c,string name,AnimatorControllerParameterType type)
    {if(!c.parameters.Any(p=>p.name==name))c.AddParameter(name,type);}
    static AnimatorState State(AnimatorStateMachine sm,string name,AnimationClip clip,string time=null)
    {
        var s=sm.states.FirstOrDefault(x=>x.state.name==name).state??sm.AddState(name);
        s.transitions=Array.Empty<AnimatorStateTransition>();s.motion=clip;
        s.timeParameterActive=time!=null;if(time!=null){s.timeParameter=time;s.speed=0;}
        EditorUtility.SetDirty(s);return s;
    }
    static void AddLayer(AnimatorController c,string name,AnimationClip clip,string time)
    {
        var layers=c.layers.ToList();var layer=layers.FirstOrDefault(l=>l.name==name);
        if(layer==null){var sm=new AnimatorStateMachine{name=name};AssetDatabase.AddObjectToAsset(sm,c);layer=new AnimatorControllerLayer{name=name,stateMachine=sm};layers.Add(layer);}
        layer.blendingMode=AnimatorLayerBlendingMode.Additive;layer.defaultWeight=1;
        layer.stateMachine.defaultState=State(layer.stateMachine,name,clip,time);c.layers=layers.ToArray();
    }
    [MenuItem("Moodium/Moodi/Setup Rise Display")]
    public static void Setup()
    {
        MoodiAnimatedSetup.SetupAll();
        var fallback=PrefabUtility.LoadPrefabContents("Assets/prefab/Moodi meshy.prefab");
        try{PrefabUtility.SaveAsPrefabAsset(fallback,"Assets/Resources/MoodiEmergence/Moodi.prefab");}finally{PrefabUtility.UnloadPrefabContents(fallback);}
        foreach(var world in new[]{"Nature","Candy"})
        {
            var importer=(ModelImporter)AssetImporter.GetAtPath($"Assets/Model/{world}Soft.fbx");
            var readable=importer.isReadable;
            try
            {
                importer.isReadable=true;importer.SaveAndReimport();
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/prefab/{world}Soft.prefab");
                var points=new Dictionary<Vector2Int,Vector3>();
                foreach(var filter in prefab.GetComponentsInChildren<MeshFilter>())
                {
                    var matrix=prefab.transform.worldToLocalMatrix*filter.transform.localToWorldMatrix;
                    foreach(var vertex in filter.sharedMesh.vertices)
                    {
                        var v=matrix.MultiplyPoint3x4(vertex);var key=new Vector2Int(Mathf.RoundToInt(v.x/.001f),Mathf.RoundToInt(v.z/.001f));
                        if(!points.TryGetValue(key,out var previous)||v.y>previous.y)points[key]=v;
                    }
                }
                var path=$"Assets/Resources/MoodiEmergence/{world}Surface.asset";
                var asset=AssetDatabase.LoadAssetAtPath<Moodium.Reality.SoftSurfaceSamples>(path);
                if(asset==null){asset=ScriptableObject.CreateInstance<Moodium.Reality.SoftSurfaceSamples>();AssetDatabase.CreateAsset(asset,path);}
                asset.Points=points.Values.ToArray();EditorUtility.SetDirty(asset);
            }finally{importer.isReadable=readable;importer.SaveAndReimport();}
        }
        AssetDatabase.SaveAssets();
    }
}
