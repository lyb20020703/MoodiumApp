using Moodium.MoodCheck;
using NUnit.Framework;
using UnityEngine;

public sealed class MoodClassifierTests
{
    [TestCase(0f, 0f, MoodKind.Neutral)]
    [TestCase(0.219f, 0f, MoodKind.Neutral)]
    [TestCase(0.22f, 0f, MoodKind.Happy)]
    [TestCase(0.6f, 0.6f, MoodKind.Happy)]
    [TestCase(0.6f, -0.6f, MoodKind.Calm)]
    [TestCase(-0.6f, -0.6f, MoodKind.Low)]
    [TestCase(-0.6f, 0.6f, MoodKind.Tense)]
    public void Classify_UsesNeutralRadiusThenSignedQuadrants(float valence, float arousal, MoodKind expected)
    {
        Assert.That(MoodClassifier.Classify(valence, arousal), Is.EqualTo(expected));
    }

    [Test]
    public void Intensity_ClampsInputsAndRadiusToOne()
    {
        Assert.That(MoodClassifier.Intensity(2f, 0f), Is.EqualTo(1f));
        Assert.That(MoodClassifier.Intensity(0.6f, 0.8f), Is.EqualTo(1f).Within(0.0001f));
    }

    [TestCase(MoodKind.Happy, 0.6f, 0.6f)]
    [TestCase(MoodKind.Calm, 0.6f, -0.6f)]
    [TestCase(MoodKind.Neutral, 0f, 0f)]
    [TestCase(MoodKind.Low, -0.6f, -0.6f)]
    [TestCase(MoodKind.Tense, -0.6f, 0.6f)]
    public void Preset_ReturnsSpecifiedCoordinates(MoodKind mood, float x, float y)
    {
        Assert.That(MoodClassifier.Preset(mood), Is.EqualTo(new Vector2(x, y)));
    }

    [TestCase(MoodKind.Happy, true, true, "candy")]
    [TestCase(MoodKind.Low, true, true, "candy")]
    [TestCase(MoodKind.Calm, true, true, "nature")]
    [TestCase(MoodKind.Neutral, true, true, "nature")]
    [TestCase(MoodKind.Tense, true, true, "nature")]
    [TestCase(MoodKind.Happy, false, true, "nature")]
    [TestCase(MoodKind.Tense, true, false, "candy")]
    [TestCase(MoodKind.Happy, false, false, null)]
    public void Recommend_UsesPreferredWorldThenAvailabilityFallback(
        MoodKind mood, bool candyAvailable, bool natureAvailable, string expected)
    {
        Assert.That(MoodClassifier.Recommend(mood, candyAvailable, natureAvailable), Is.EqualTo(expected));
    }

    [Test]
    public void Compare_UsesInclusivePointOneFiveThreshold()
    {
        var target = Vector2.zero;
        Assert.That(MoodClassifier.Compare(new Vector2(.5f, 0f), new Vector2(.35f, 0f), target),
            Is.EqualTo(MoodComparison.Better));
        Assert.That(MoodClassifier.Compare(new Vector2(.35f, 0f), new Vector2(.5f, 0f), target),
            Is.EqualTo(MoodComparison.Worse));
        Assert.That(MoodClassifier.Compare(new Vector2(.5f, 0f), new Vector2(.36f, 0f), target),
            Is.EqualTo(MoodComparison.Same));
    }

    [Test]
    public void CopyTable_ContainsEveryRequiredKeyInBothLanguages()
    {
        var keys = new[]
        {
            "prompt.entry", "plate.kicker", "plate.title.pre", "plate.title.post", "plate.hint",
            "axis.up", "axis.down", "axis.left", "axis.right",
            "mood.happy", "mood.calm", "mood.neutral", "mood.low", "mood.tense",
            "confirm.title", "confirm.yes", "confirm.adjust", "skip",
            "rec.happy", "rec.calm", "rec.neutral", "rec.low", "rec.tense", "rec.today",
            "rec.sticker", "rec.sticker.sub", "rec.other", "rec.cta", "rec.retry", "tag.pick",
            "compare.kicker", "compare.better", "compare.same", "compare.worse", "compare.before",
            "compare.now", "compare.time", "compare.squeezes", "compare.privacy", "compare.continue"
        };

        foreach (var key in keys)
        {
            Assert.That(MoodCheckCopy.Get(key, false), Is.Not.Empty, $"Missing Chinese copy for {key}");
            Assert.That(MoodCheckCopy.Get(key, true), Is.Not.Empty, $"Missing English copy for {key}");
        }
    }

    [Test]
    public void Format_ReplacesNamedMoodPlaceholder()
    {
        Assert.That(MoodCheckCopy.Format("confirm.title", false, ("mood", "平静")),
            Is.EqualTo("你现在感觉平静，对吗？"));
        Assert.That(MoodCheckCopy.Format("rec.today", true, ("mood", "Calm")),
            Is.EqualTo("Now: Calm"));
    }
}
