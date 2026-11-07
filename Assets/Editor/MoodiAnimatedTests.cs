using Moodium.Characters;
using NUnit.Framework;
using UnityEngine;
using UnityEditor;
using Moodium.Flow;
using System.Linq;
using System.Reflection;

public sealed class MoodiAnimatedTests
{
    const string PrefabPath="Assets/prefab/Moodi meshy.prefab";
    [TestCase(1f)]
    [TestCase(0f)]
    public void WalkingGuideFacesAssignedViewerWhileMovingAndWaiting(float speed)
    {
        var moodi=Create(out var root);var viewer=new GameObject("Walking viewer");
        try
        {
            var configure=typeof(MoodiCompanionAnimator).GetMethod("SetFacingViewer");
            Assert.That(configure,Is.Not.Null,"Walking guides need a viewer-facing override, not travel-direction facing.");
            configure.Invoke(moodi,new object[]{viewer.transform});
            root.transform.position=new Vector3(2,1,3);viewer.transform.position=new Vector3(-2,1.8f,3);
            typeof(MoodiCompanionAnimator).GetField("m_Velocity",Private).SetValue(moodi,Vector3.right);
            var position=root.transform.position;var rotation=root.transform.rotation;
            for(var i=0;i<10;i++)Call(moodi,"UpdateFacing",speed,.1f);
            var face=(Vector3)typeof(MoodiCompanionAnimator).GetField("m_FaceInModel",Private).GetValue(moodi);
            var actual=Vector3.ProjectOnPlane(moodi.Animator.transform.TransformDirection(face),Vector3.up).normalized;
            Assert.That(Vector3.Dot(actual,Vector3.left),Is.GreaterThan(.99f));
            Assert.That(root.transform.position,Is.EqualTo(position));
            Assert.That(Quaternion.Angle(root.transform.rotation,rotation),Is.LessThan(.001f));
            // The same companion keeps following the viewer after they walk to the other side.
            viewer.transform.position=new Vector3(6,1.8f,3);
            for(var i=0;i<10;i++)Call(moodi,"UpdateFacing",speed,.1f);
            actual=Vector3.ProjectOnPlane(moodi.Animator.transform.TransformDirection(face),Vector3.up).normalized;
            Assert.That(Vector3.Dot(actual,Vector3.right),Is.GreaterThan(.99f));
        }
        finally{Object.DestroyImmediate(root);Object.DestroyImmediate(viewer);}
    }
    [Test] public void UpgradedPrefabKeepsOriginalIdentityAndStaticBackup()
    {
        Assert.That(AssetDatabase.AssetPathToGUID(PrefabPath),Is.EqualTo("1c1c0c337d80f495e8342b47fb803074"));
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Assert.That(prefab.GetComponent<MoodiCompanionAnimator>(),Is.Not.Null);
        Assert.That(prefab.transform.Find("moodi").gameObject.activeSelf,Is.False);
        Assert.That(prefab.transform.Find("moodi_rigged").gameObject.activeSelf,Is.True);
        var animator=prefab.GetComponentInChildren<Animator>();
        Assert.That(animator.applyRootMotion,Is.False);
        Assert.That(animator.runtimeAnimatorController.animationClips.Select(c=>c.name),Is.EquivalentTo(new[]{"Idle","Walk","Touched","Happy","Wave","Emerge","FallIn","Peek","Strain"}));
        foreach(var clip in animator.runtimeAnimatorController.animationClips)
            Assert.That(clip.isLooping,Is.EqualTo(clip.name=="Idle"||clip.name=="Walk"||clip.name=="Peek"),clip.name);
        var floating=new SerializedObject(prefab.GetComponent<MoodiBreathingFloat>());
        Assert.That(floating.FindProperty("m_VerticalAmplitude").floatValue,Is.GreaterThan(0));
        Assert.That(floating.FindProperty("m_ScaleAmplitude").floatValue,Is.Zero);
    }
    [Test] public void LegacySelectionDoesNotWrapOrHijackRiggedCompanion()
    {
        var root=new GameObject("New companion entry");var model=new GameObject("moodi_rigged");model.transform.SetParent(root.transform,false);
        root.AddComponent<MoodiCompanionAnimator>();model.AddComponent<Animator>();
        try
        {
            Assert.That(MoodiSelectionMascotController.AttachTo(root),Is.Null);
            Assert.That(model.transform.parent,Is.SameAs(root.transform));
            Assert.That(root.GetComponentInChildren<MoodiSelectionMascotController>(),Is.Null);
        }
        finally{Object.DestroyImmediate(root);}
    }
    [Test] public void MenuCommandsHaveVoidReturnTypes()
    {
        foreach(var method in typeof(MoodiAnimatedSetup).GetMethods(BindingFlags.Public|BindingFlags.Static))
            foreach(MenuItem item in method.GetCustomAttributes(typeof(MenuItem),false))
                if(!item.validate)Assert.That(method.ReturnType,Is.EqualTo(typeof(void)),method.Name);
    }
    [Test] public void RebuildingControllerPreservesAssetGuid()
    {
        var path="Assets/__MoodiAnimatedTest_"+System.Guid.NewGuid().ToString("N")+".controller";
        try
        {
            Assert.That(AssetDatabase.CopyAsset("Assets/Animations/Moodi/MoodiCompanion.controller",path),Is.True);
            var guid=AssetDatabase.AssetPathToGUID(path);
            MoodiAnimatedSetup.BuildController(path);
            Assert.That(AssetDatabase.AssetPathToGUID(path),Is.EqualTo(guid));
            var controller=AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(path);
            Assert.That(controller.layers[0].stateMachine.states.First(c=>c.state.name=="Touched").state.transitions.Length,Is.EqualTo(1));
        }
        finally{AssetDatabase.DeleteAsset(path);}
    }
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static void Call(object target,string method,params object[] args)=>target.GetType().GetMethod(method,Private).Invoke(target,args);
    static MoodiCompanionAnimator Create(out GameObject root)
    {
        root=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
        var moodi=root.GetComponent<MoodiCompanionAnimator>();Call(moodi,"Awake");
        // Rebuild the binding after setup tests edit this controller in the same Editor session.
        var controller=moodi.Animator.runtimeAnimatorController;
        moodi.Animator.runtimeAnimatorController=null;moodi.Animator.runtimeAnimatorController=controller;
        moodi.Animator.Rebind();moodi.Animator.Update(0);Call(moodi,"OnEnable");return moodi;
    }
    [Test] public void ColdPlacementIsNotInterpretedAsWalking()
    {
        var moodi=Create(out var root);
        try
        {
            root.transform.position=new Vector3(10,1,3);Call(moodi,"Update");
            Assert.That(moodi.CurrentSpeed,Is.LessThan(.001f));
        }
        finally{Object.DestroyImmediate(root);}
    }
    [Test] public void LatestTouchedReactionDoesNotLeaveAPendingWave()
    {
        var moodi=Create(out var root);
        try
        {
            moodi.PlayWave();moodi.PlayTouched();Call(moodi,"Update");
            Assert.That(moodi.Animator.GetBool("Wave"),Is.False,"Touched must cancel an unconsumed greeting.");
            moodi.Animator.Update(.1f);moodi.Animator.Update(.15f);
            Assert.That(moodi.Animator.GetCurrentAnimatorStateInfo(0).IsName("Touched")||moodi.Animator.GetNextAnimatorStateInfo(0).IsName("Touched"),Is.True);
            Assert.That(moodi.Animator.GetBool("Wave"),Is.False);
            // Inspect both the authored exit and its evaluated result after rebuilding.
            var controller=(UnityEditor.Animations.AnimatorController)moodi.Animator.runtimeAnimatorController;
            var state=controller.layers[0].stateMachine.states.First(c=>c.state.name=="Touched").state;
            Assert.That(state.transitions.Length,Is.EqualTo(1));
            Assert.That(state.transitions[0].hasExitTime,Is.True);
            Assert.That(state.transitions[0].destinationState.name,Is.EqualTo("Idle"));
            for(int i=0;i<40;i++)moodi.Animator.Update(.05f);
            Assert.That(moodi.Animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"),Is.True);
        }
        finally{Object.DestroyImmediate(root);}
    }
    [Test] public void TouchCooldownAndVisualTurningNeverMoveTheGuideRoot()
    {
        var moodi=Create(out var root);
        try
        {
            int touched=0;moodi.Touched+=()=>touched++;moodi.PlayTouched();moodi.PlayTouched();
            Assert.That(touched,Is.EqualTo(1));
            root.transform.SetPositionAndRotation(new Vector3(2,1.2f,3),Quaternion.Euler(0,30,0));
            var pose=new Pose(root.transform.position,root.transform.rotation);
            typeof(MoodiCompanionAnimator).GetField("m_Velocity",Private).SetValue(moodi,Vector3.right);
            Call(moodi,"UpdateFacing",1f,.1f);
            Assert.That(root.transform.position,Is.EqualTo(pose.position));
            Assert.That(Quaternion.Angle(root.transform.rotation,pose.rotation),Is.LessThan(.001f));
        }
        finally{Object.DestroyImmediate(root);}
    }
    [TestCase("Moodi_Rig|Idle", "Idle")]
    [TestCase("Moodi_Rig|Moodi_Rig|Touched", "Touched")]
    [TestCase("Wave", "Wave")]
    public void ClipNames_DropTheBlenderPrefix(string take, string expected)
    {
        Assert.That(MoodiAnimatedSetup.ShortClipName(take), Is.EqualTo(expected));
    }

    [Test]
    public void YawStep_TurnsTheShortWayAndIsLimited()
    {
        // Facing +Z, target +X: a quarter turn to the right (positive yaw in Unity), capped at 30°.
        Assert.That(MoodiCompanionAnimator.YawStep(Vector3.forward, Vector3.right, 30f), Is.EqualTo(30f).Within(1e-3f));
        Assert.That(MoodiCompanionAnimator.YawStep(Vector3.forward, Vector3.left, 30f), Is.EqualTo(-30f).Within(1e-3f));
        Assert.That(MoodiCompanionAnimator.YawStep(Vector3.forward, Vector3.right, 360f), Is.EqualTo(90f).Within(1e-3f));
    }

    [Test]
    public void YawStep_IgnoresTinyAndVerticalDifferences()
    {
        Assert.That(MoodiCompanionAnimator.YawStep(Vector3.forward, Quaternion.Euler(0, 1, 0) * Vector3.forward, 30f), Is.Zero);
        Assert.That(MoodiCompanionAnimator.YawStep(Vector3.forward, Vector3.up, 30f), Is.Zero);
        Assert.That(MoodiCompanionAnimator.YawStep(Vector3.forward, new Vector3(0, 5, 1), 30f), Is.Zero);
    }
}
