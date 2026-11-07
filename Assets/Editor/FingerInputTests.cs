using System.IO;
using System.Reflection;
using Moodium.Flow;
using Moodium.NatureWorld;
using Moodium.UI;
using NUnit.Framework;
using UnityEngine;

/// <summary>One index finger, like a phone: drag the slide-to-enter knob; write on (and open) a wish chime. Pinch still works.</summary>
public sealed class FingerInputTests
{
    const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;

    // ---------- pen rules ----------

    [Test]
    public void TheFingerPenGoesDownOnlyWhenComingFromTheFront()
    {
        var mid = new Vector2(.5f, .5f);
        bool armed = false, down = false;
        down = WishFingerPen.Next(down, ref armed, .05f, mid);   // hovering in front
        Assert.That(down, Is.False); Assert.That(armed, Is.True);
        down = WishFingerPen.Next(down, ref armed, .004f, mid);  // touches
        Assert.That(down, Is.True);
        down = WishFingerPen.Next(down, ref armed, .012f, mid);  // slight wobble: still down (hysteresis)
        Assert.That(down, Is.True);
        down = WishFingerPen.Next(down, ref armed, -.025f, mid); // pushed through a bit: still down
        Assert.That(down, Is.True);
        down = WishFingerPen.Next(down, ref armed, .03f, mid);   // lifted
        Assert.That(down, Is.False);

        armed = false; down = false;
        down = WishFingerPen.Next(down, ref armed, -.05f, mid);  // a hand behind the paper
        down = WishFingerPen.Next(down, ref armed, -.01f, mid);  // ...moving forward through it
        Assert.That(down, Is.False, "never draws when it comes from behind");

        armed = true; down = true;
        down = WishFingerPen.Next(down, ref armed, -.05f, mid);  // went through
        Assert.That(down, Is.False);
        Assert.That(armed, Is.False);
        down = WishFingerPen.Next(down, ref armed, -.01f, mid);
        Assert.That(down, Is.False, "has to come back to the front first");
    }

    [Test]
    public void TheFingerPenStaysOnThePaper()
    {
        bool armed = true;
        Assert.That(WishFingerPen.Next(false, ref armed, .004f, new Vector2(1.02f, .5f)), Is.False, "a stroke never starts off the paper");
        armed = true;
        Assert.That(WishFingerPen.Next(true, ref armed, .004f, new Vector2(1.02f, .5f)), Is.True, "a stroke may run a little over the edge");
        Assert.That(WishFingerPen.Next(true, ref armed, .004f, new Vector2(1.1f, .5f)), Is.False);
        Assert.That(WishFingerPen.Touching(.01f, new Vector2(.5f, .5f)), Is.True);
        Assert.That(WishFingerPen.Touching(.05f, new Vector2(.5f, .5f)), Is.False);
        Assert.That(WishFingerPen.Touching(.0f, new Vector2(1.5f, .5f)), Is.False);
    }

    // ---------- the chime ----------

    sealed class Fixture : System.IDisposable
    {
        public readonly GameObject Root;
        public readonly WishChime Chime;
        public readonly BubblePlayHands Hands = new();
        public readonly WishPaperFrame Frame;
        readonly MethodInfo m_Update = typeof(WishChime).GetMethod("Update", Flags);
        public Fixture(bool blank)
        {
            Root = new GameObject("finger fixture");
            var viewer = Root.AddComponent<Camera>();
            var garden = Root.AddComponent<WishChimeGarden>();
            garden.Initialize(BubbleWorldData.NatureFolder, Root.transform, viewer, Hands, null);
            typeof(WishChimeGarden).GetField("taught", Flags).SetValue(garden, true);
            var go = new GameObject("wish"); go.transform.SetParent(Root.transform, false);
            go.transform.position = new Vector3(0f, -.2f, .55f);
            Chime = go.AddComponent<WishChime>();
            Chime.Initialize(garden, new WishChimeRecord { id = "fixture", position = new[] { 0f, -.2f, .55f } });
            typeof(WishChime).GetField("<State>k__BackingField", Flags).SetValue(Chime, WishChime.Stage.Blank);
            typeof(WishChime).GetField("putAwayAt", Flags).SetValue(Chime, -100f);
            if (!blank) Chime.BeginWrite();
            Frame = Resources.Load<WishPaperFrame>("MoodiumBubblePlay/PaperFrame_Nature");
        }
        public Vector3 Point(Vector2 uv, float infront) =>
            Chime.Strip.TransformPoint(Frame.Surface(uv)) + Chime.Strip.TransformDirection(Frame.Normal).normalized * infront;
        /// <summary>Right hand: index fingertip at <paramref name="index"/>, not pinching (thumb far from it).</summary>
        public void Finger(Vector3 index)
        {
            Hands.Hands[1] = new BubblePlayHand { Valid = true, Pinching = false, Open = true, Index = index, Tip = index + Vector3.down * .04f, Palm = index + Vector3.down * .1f, Up = Vector3.back };
            m_Update.Invoke(Chime, null);
        }
        public void NoHand() { Hands.Hands[1] = default; m_Update.Invoke(Chime, null); }
        public int CompletedVertices => Chime.GetComponentInChildren<WishInkMesh>().transform.Find("Completed Ink").GetComponent<MeshFilter>().sharedMesh.vertexCount;
        public int CurrentVertices => Chime.GetComponentInChildren<WishInkMesh>().transform.Find("Current Ink").GetComponent<MeshFilter>().sharedMesh.vertexCount;
        public void Dispose() => Object.DestroyImmediate(Root);
    }

    [Test]
    public void OneFingerWritesOnTheChime()
    {
        using var f = new Fixture(false);
        Assert.That(f.Chime.State, Is.EqualTo(WishChime.Stage.Writing));
        f.Finger(f.Point(new Vector2(.2f, .5f), .04f));   // approach from the front
        Assert.That(f.CurrentVertices, Is.Zero);
        for (var i = 0; i <= 20; i++) f.Finger(f.Point(new Vector2(.2f + i * .03f, .5f), .002f));   // draw across, touching
        Assert.That(f.CurrentVertices, Is.GreaterThan(0), "ink follows the fingertip without pinching");
        f.Finger(f.Point(new Vector2(.8f, .5f), .05f));   // lift
        Assert.That(f.CompletedVertices, Is.GreaterThan(0));
        Assert.That(f.Chime.HasInk, Is.True);
    }

    [Test]
    public void AFingerFromBehindThePaperDoesNotWrite()
    {
        using var f = new Fixture(false);
        f.Finger(f.Point(new Vector2(.5f, .5f), -.06f));
        for (var i = 0; i <= 10; i++) f.Finger(f.Point(new Vector2(.3f + i * .03f, .5f), -.01f));
        f.NoHand();
        Assert.That(f.Chime.HasInk, Is.False);
    }

    [Test]
    public void TouchingABlankChimesPaperOpensIt()
    {
        using var f = new Fixture(true);
        f.Finger(f.Point(new Vector2(.5f, .5f), .2f));
        Assert.That(f.Chime.State, Is.EqualTo(WishChime.Stage.Blank), "not yet");
        f.Finger(f.Point(new Vector2(.5f, .6f), .005f));
        Assert.That(f.Chime.State, Is.EqualTo(WishChime.Stage.Writing));
        var actions = f.Chime.transform.Find("Wish Actions");
        Assert.That(actions, Is.Not.Null);
        Assert.That(actions.GetComponent<SensoryBagDirectTouchInput>(), Is.Not.Null, "buttons take a fingertip tap too");
    }

    // ---------- the slider ----------

    [Test]
    public void OneFingerDragsTheSlideToEnterKnob()
    {
        var entry = new GameObject("entry");
        try
        {
            var holder = new GameObject("Enter World Slider"); holder.transform.SetParent(entry.transform, false);
            var slider = holder.AddComponent<GlassSlideToEnter>();
            slider.Configure("Enter", GlassSlideToEnter.Theme.Candy, null);
            var bridge = entry.AddComponent<SensoryBagDirectTouchInput>();
            Physics.SyncTransforms();
            // Start on the track (the knob's own collider only moves in play mode, where Update draws it).
            Vector3 OnTrack(float progress) => holder.transform.TransformPoint(new Vector3(GlassSlideToEnter.KnobCenterX(progress, .28f, .068f), 0f, -.012f));
            bridge.ProcessContact(-1002, OnTrack(.3f));   // fingertip touches the pill
            for (var p = .3f; p <= 1.001f; p += .05f) bridge.ProcessContact(-1002, OnTrack(p));
            Assert.That(slider.Progress, Is.GreaterThan(.9f), "the knob followed the finger");
            bridge.ProcessContact(-1002, OnTrack(1f) - holder.transform.forward * .1f);   // finger lifted away
            Assert.That(slider.IsCompleted, Is.True, "released past the threshold: enters the world");
        }
        finally { Object.DestroyImmediate(entry); }
    }

    [Test]
    public void TheStickerEntrySliderHasTheFingertipBridge()
    {
        var source = File.ReadAllText("Assets/Scripts/Moodium/Flow/StickerWorldSelectionController.cs");
        var create = source.Substring(source.IndexOf("void CreateEnterButton", System.StringComparison.Ordinal));
        create = create.Substring(0, create.IndexOf("string EntryButtonLabel", System.StringComparison.Ordinal));
        Assert.That(create, Does.Contain("AddComponent<SensoryBagDirectTouchInput>()"));
    }
}
