using Moodium.CandyWorld;
using NUnit.Framework;
using UnityEngine;
using Moodium.Flow;
using System.Reflection;

/// <summary>Tests for the "Mood Band" wrist UI (v3: slim energy capsule, round buttons, one-press exit).
/// The existing CandyEnergyWristRingTests must keep passing too.</summary>
public sealed class MoodBandWristUITests
{
    [Test]
    public void RaisedWristRequiresPresentationNotJustHeadDirection()
    {
        Assert.That(CandyEnergyWristUI.EvaluateWristPresentation(false,40,.5f,-.2f,.8f),Is.True);
        Assert.That(CandyEnergyWristUI.EvaluateWristPresentation(false,10,.5f,-.6f,.8f),Is.False,"Low hands must not open the card.");
        Assert.That(CandyEnergyWristUI.EvaluateWristPresentation(false,10,.5f,-.2f,-.3f),Is.False,"Palm facing viewer must not open dorsal card.");
        Assert.That(CandyEnergyWristUI.EvaluateWristPresentation(false,10,.8f,-.2f,.8f),Is.False);
        Assert.That(CandyEnergyWristUI.EvaluateWristPresentation(true,50,.5f,-.5f,.2f),Is.True,"Keep comfort hysteresis after opening.");
    }

    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static void Set(object ui,string field,object value)=>ui.GetType().GetField(field,Private).SetValue(ui,value);
    static T Get<T>(object ui,string field)=>(T)ui.GetType().GetField(field,Private).GetValue(ui);
    static void Call(object ui,string method,params object[] args)=>ui.GetType().GetMethod(method,Private).Invoke(ui,args);

    static CandyEnergyWristUI Create(out GameObject root,out Camera camera)
    {
        root=new GameObject("Wrist test");camera=new GameObject("Wrist camera").AddComponent<Camera>();
        camera.transform.position=new Vector3(0,1.6f,0);
        var font=UnityEditor.AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>("Assets/UI/Moodium/Fonts/AlibabaPuHuiTi Moodium SDF.asset");
        var ui=root.AddComponent<CandyEnergyWristUI>();ui.Build(camera,font,null,null);
        root.transform.SetPositionAndRotation(new Vector3(0,1.4f,.45f),Quaternion.LookRotation(Vector3.back,Vector3.up));
        return ui;
    }
    static void Present(CandyEnergyWristUI ui,bool tracked)
    {
        typeof(CandyEnergyWristUI).GetProperty("HandTracked").SetValue(ui,tracked);
        Call(ui,"UpdateGlance",.3f);Call(ui,"UpdateCardAndToast",.3f);
    }
    // The row has been on screen long enough for presses to count.
    static void Settle(CandyEnergyWristUI ui)=>Set(ui,"m_GlanceSince",Time.time-1f);

    [Test] public void CardInputIsDisabledInitiallyAndImmediatelyOnTrackingLoss()
    {
        var ui=Create(out var root,out var camera);
        try
        {
            ui.SetExitAvailable(true);
            var exit=Get<GameObject>(ui,"m_ExitButtonRoot").GetComponent<BoxCollider>();
            Assert.That(exit.enabled,Is.False,"Hidden card must never have live colliders.");
            Present(ui,true);Assert.That(exit.enabled,Is.True);
            Present(ui,false);Assert.That(exit.enabled,Is.False);
        }
        finally{Object.DestroyImmediate(root);Object.DestroyImmediate(camera.gameObject);}
    }

    [Test] public void ExitLeavesOnOnePressOnceTheRowHasSettled()
    {
        var ui=Create(out var root,out var camera);
        try
        {
            ui.SetExitAvailable(true);Present(ui,true);int exits=0;ui.ExitPressed+=()=>exits++;
            Call(ui,"PressExit");
            Assert.That(exits,Is.Zero,"A touch while the wrist is still coming up must not exit.");
            Settle(ui);
            Call(ui,"PressExit");
            Assert.That(exits,Is.EqualTo(1),"One press exits; there is no confirmation step.");
        }
        finally{Object.DestroyImmediate(root);Object.DestroyImmediate(camera.gameObject);}
    }

    [Test] public void ExitIsIgnoredWhenUnavailableOrHidden()
    {
        var ui=Create(out var root,out var camera);
        try
        {
            int exits=0;ui.ExitPressed+=()=>exits++;
            ui.SetExitAvailable(false);Present(ui,true);Settle(ui);
            Call(ui,"PressExit");Assert.That(exits,Is.Zero);
            ui.SetExitAvailable(true);Present(ui,false);Settle(ui);
            Call(ui,"PressExit");Assert.That(exits,Is.Zero);
        }
        finally{Object.DestroyImmediate(root);Object.DestroyImmediate(camera.gameObject);}
    }

    [Test]
    public void ExitPressRules()
    {
        Assert.That(CandyEnergyWristUI.CanPressExit(true,true,true,1f,.5f,.35f),Is.True);
        Assert.That(CandyEnergyWristUI.CanPressExit(true,true,true,1f,.2f,.35f),Is.False,"too soon after the row appeared");
        Assert.That(CandyEnergyWristUI.CanPressExit(true,true,true,.3f,.5f,.35f),Is.False,"row still fading in");
        Assert.That(CandyEnergyWristUI.CanPressExit(true,false,true,1f,.5f,.35f),Is.False,"hand not tracked");
        Assert.That(CandyEnergyWristUI.CanPressExit(false,true,true,1f,.5f,.35f),Is.False,"exit not available");
        Assert.That(CandyEnergyWristUI.CanPressExit(true,true,false,1f,.5f,.35f),Is.False,"wrist no longer presented");
    }

    [Test] public void ProgressResetClearsSocketsAndTheCapsuleShowsZero()
    {
        var ui=Create(out var root,out var camera);
        try
        {
            ui.SetProgress(62,.62f);
            ui.SetCollectedGems(2);ui.SetProgress(0,0);
            Assert.That(ui.DisplayedProgress,Is.Zero);
            Assert.That(Get<int>(ui,"m_CollectedGems"),Is.Zero);
            Call(ui,"UpdateProgressAnimation",.016f);
            Assert.That(Get<TMPro.TMP_Text>(ui,"m_Percentage").text,Is.EqualTo("0%"));
        }
        finally{Object.DestroyImmediate(root);Object.DestroyImmediate(camera.gameObject);}
    }

    [Test] public void RowIsReleaseThenCapsuleThenExitWithoutOverlap()
    {
        var ui=Create(out var root,out var camera);
        try
        {
            ui.SetReleaseAvailable(true);ui.SetExitAvailable(true);
            var card=Get<RectTransform>(ui,"m_CardRect");
            var release=RectTransformUtility.CalculateRelativeRectTransformBounds(card,Get<GameObject>(ui,"m_ReleaseButtonRoot").transform);
            var capsule=RectTransformUtility.CalculateRelativeRectTransformBounds(card,Get<RectTransform>(ui,"m_Capsule"));
            var exit=RectTransformUtility.CalculateRelativeRectTransformBounds(card,Get<GameObject>(ui,"m_ExitButtonRoot").transform);
            Assert.That(release.max.x,Is.LessThan(capsule.min.x));
            Assert.That(capsule.max.x,Is.LessThan(exit.min.x));
            Assert.That(card.sizeDelta.x,Is.EqualTo(CandyEnergyWristUI.RowWidth(true,true)));
            Assert.That(card.sizeDelta.y,Is.EqualTo(CandyEnergyWristUI.RowHeight));
            Assert.That(Mathf.Abs(capsule.center.x-(release.center.x+exit.center.x)*.5f),Is.LessThan(.01f),"capsule stays centred between the buttons");

            ui.SetReleaseAvailable(false);
            Assert.That(card.sizeDelta.x,Is.EqualTo(CandyEnergyWristUI.RowWidth(false,true)));
        }
        finally{Object.DestroyImmediate(root);Object.DestroyImmediate(camera.gameObject);}
    }

    [Test] public void PercentageAndGiftDotsFitInsideTheCapsule()
    {
        var ui=Create(out var root,out var camera);
        try
        {
            var capsule=Get<RectTransform>(ui,"m_Capsule");
            var percentage=Get<TMPro.TMP_Text>(ui,"m_Percentage");
            percentage.text="100%";percentage.ForceMeshUpdate(true,true);
            Assert.That(percentage.textInfo.lineCount,Is.EqualTo(1));
            var text=RectTransformUtility.CalculateRelativeRectTransformBounds(capsule,percentage.transform);
            var dots=Get<UnityEngine.UI.Image[]>(ui,"m_GemDots");
            var firstDot=RectTransformUtility.CalculateRelativeRectTransformBounds(capsule,dots[0].transform);
            var lastDot=RectTransformUtility.CalculateRelativeRectTransformBounds(capsule,dots[3].transform);
            Assert.That(text.max.x,Is.LessThanOrEqualTo(firstDot.min.x));
            Assert.That(text.min.x,Is.GreaterThanOrEqualTo(-CandyEnergyWristUI.CapsuleWidth*.5f));
            Assert.That(lastDot.max.x,Is.LessThanOrEqualTo(CandyEnergyWristUI.CapsuleWidth*.5f));
        }
        finally{Object.DestroyImmediate(root);Object.DestroyImmediate(camera.gameObject);}
    }

    [Test] public void RowIsSmallAndSitsLowAboveTheBand()
    {
        var ui=Create(out var root,out var camera);
        try
        {
            const float canvasScale=.00025f;
            Assert.That(CandyEnergyWristUI.RowHeight*canvasScale,Is.EqualTo(.018f).Within(.0001f),"18 mm tall");
            Assert.That(CandyEnergyWristUI.RowWidth(true,true)*canvasScale,Is.EqualTo(.087f).Within(.0001f),"87 mm with every button");
            Assert.That(Get<float>(ui,"m_CardLift"),Is.EqualTo(.028f).Within(.0001f),"28 mm above the band");
        }
        finally{Object.DestroyImmediate(root);Object.DestroyImmediate(camera.gameObject);}
    }

    [Test] public void ReleaseButtonTurnsWorldColourWhenOn()
    {
        var ui=Create(out var root,out var camera);
        try
        {
            ui.SetWorldTheme(true);ui.SetReleaseAvailable(true);
            var chip=Get<UnityEngine.UI.Image>(ui,"m_ReleaseChip");
            ui.SetReleaseToggleState(true);
            Assert.That(chip.color,Is.EqualTo(CandyEnergyWristUI.GetProgressRingColor(true)));
            ui.SetReleaseToggleState(false);
            Assert.That(chip.color,Is.EqualTo(Color.white),"off = dark circle sprite, untinted");
        }
        finally{Object.DestroyImmediate(root);Object.DestroyImmediate(camera.gameObject);}
    }

    [Test]
    public void Sockets_SitAtTheFourRewardStages()
    {
        Assert.That(CandyEnergyWristUI.GetSocketProgress(0), Is.EqualTo(.2f));
        Assert.That(CandyEnergyWristUI.GetSocketProgress(1), Is.EqualTo(.5f));
        Assert.That(CandyEnergyWristUI.GetSocketProgress(2), Is.EqualTo(.8f));
        Assert.That(CandyEnergyWristUI.GetSocketProgress(3), Is.EqualTo(1f));
    }

    [Test]
    public void Thread_RunsAcrossTheBackOfTheWrist()
    {
        Assert.That(CandyEnergyWristUI.ThreadAngleForProgress(0f, 150f, 30f), Is.EqualTo(150f));
        Assert.That(CandyEnergyWristUI.ThreadAngleForProgress(.5f, 150f, 30f), Is.EqualTo(90f), "Halfway must be the centre of the back of the wrist.");
        Assert.That(CandyEnergyWristUI.ThreadAngleForProgress(1f, 150f, 30f), Is.EqualTo(30f));
        Assert.That(CandyEnergyWristUI.ThreadAngleForProgress(2f, 150f, 30f), Is.EqualTo(30f));
    }

    [Test]
    public void Glance_UsesHysteresisAndADistanceLimit()
    {
        Assert.That(CandyEnergyWristUI.EvaluateGlance(false, 30f, .4f, 26f, 34f, .65f), Is.False, "30° is not enough to open the card.");
        Assert.That(CandyEnergyWristUI.EvaluateGlance(false, 20f, .4f, 26f, 34f, .65f), Is.True);
        Assert.That(CandyEnergyWristUI.EvaluateGlance(true, 30f, .4f, 26f, 34f, .65f), Is.True, "An open card stays open until 34°.");
        Assert.That(CandyEnergyWristUI.EvaluateGlance(true, 40f, .4f, 26f, 34f, .65f), Is.False);
        Assert.That(CandyEnergyWristUI.EvaluateGlance(true, 10f, .9f, 26f, 34f, .65f), Is.False, "A wrist held far away never opens the card.");
    }

    [Test]
    public void Texts_AreLocalized()
    {
        Assert.That(CandyEnergyWristUI.GetExitConfirmLabel(false), Is.EqualTo("再点一次退出"));
        Assert.That(CandyEnergyWristUI.GetExitConfirmLabel(true), Is.EqualTo("Tap again to exit"));
        Assert.That(CandyEnergyWristUI.GetNextGiftLabel(1, false), Is.EqualTo("下一件礼物 · 第 2 件"));
        Assert.That(CandyEnergyWristUI.GetNextGiftLabel(4, true), Is.EqualTo("All 4 gifts collected"));
        Assert.That(CandyEnergyWristUI.GetWorldEnergyTitle(true, true), Is.EqualTo("Nature Energy"));
    }
}
