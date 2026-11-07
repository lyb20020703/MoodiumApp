using System.IO;
using Moodium.Reality;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>Touching Moodi while it sits on the soft model plays its Touched clip, not the press squash.</summary>
public sealed class MoodiSoftTouchTests
{
    [Test]
    public void PattingMoodiDoesNotSquashIt()
    {
        Assert.That(MoodiRiseDisplay.PressesMoodi(true, false, false), Is.True, "squeezing the bag still strains Moodi");
        Assert.That(MoodiRiseDisplay.PressesMoodi(true, true, false), Is.False, "hand on Moodi: no squash");
        Assert.That(MoodiRiseDisplay.PressesMoodi(true, false, true), Is.False, "Touched clip playing: no squash");
        Assert.That(MoodiRiseDisplay.PressesMoodi(false, false, false), Is.False);
    }

    [Test]
    public void CompanionControllerHasTheTouchedState()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Animations/Moodi/MoodiCompanion.controller");
        Assert.That(controller, Is.Not.Null);
        var found = false;
        foreach (var state in controller.layers[0].stateMachine.states) found |= state.state.name == "Touched" && state.state.motion != null;
        Assert.That(found, Is.True, "MoodiRiseDisplay cross-fades to the Touched state on layer 0");
    }

    [Test]
    public void TouchedLengthFallsBackWithoutAController()
    {
        Assert.That(MoodiRiseDisplay.TouchedSeconds(null), Is.EqualTo(1.2f));
    }

    [Test]
    public void RiseDisplayUsesTheCompanionsHandTouch()
    {
        var rise = File.ReadAllText("Assets/Scripts/Moodium/Reality/Emergence/MoodiRiseDisplay.cs");
        Assert.That(rise, Does.Contain("companion.PollHandTouch(out handOnMoodi"));
        Assert.That(rise, Does.Contain("PressesMoodi(capsule!=null&&capsule.IsPinched,handOnMoodi,touching)"));
        var companion = File.ReadAllText("Assets/Scripts/Moodium/Characters/MoodiCompanionAnimator.cs");
        Assert.That(companion, Does.Contain("if (DetectHandTouch(out _)) PlayTouched();"), "free Moodi keeps its own touch reaction");
    }
}
