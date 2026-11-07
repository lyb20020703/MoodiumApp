using System;
using System.IO;
using Moodium.MoodCheck;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class MoodCheckLogTests
{
    string m_TempDirectory;

    [SetUp]
    public void SetUp()
    {
        m_TempDirectory = Path.Combine(Path.GetTempPath(), "moodium-mood-log-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(m_TempDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(m_TempDirectory))
            Directory.Delete(m_TempDirectory, true);
    }

    [Test]
    public void BuildJsonLine_UsesRequiredNamesAndLowercaseValues()
    {
        var json = MoodCheckLog.BuildJsonLine(Sample(MoodCheckPhase.Post));

        StringAssert.Contains("\"sessionId\":\"session-1\"", json);
        StringAssert.Contains("\"visitId\":\"visit-1\"", json);
        StringAssert.Contains("\"phase\":\"post\"", json);
        StringAssert.Contains("\"mood\":\"tense\"", json);
        StringAssert.Contains("\"inputMethod\":\"quick\"", json);
        StringAssert.Contains("\"timestampUtc\":\"2026-10-22T10:00:00.0000000Z\"", json);
        StringAssert.Contains("\"durationInWorldSeconds\":42.5", json);
        StringAssert.Contains("\"pinchCount\":3", json);
    }

    [Test]
    public void TryAppend_AppendsOneJsonObjectPerLine()
    {
        var path = Path.Combine(m_TempDirectory, "mood.json");
        Assert.That(MoodCheckLog.TryAppend(Sample(MoodCheckPhase.Pre), path), Is.True);
        Assert.That(MoodCheckLog.TryAppend(Sample(MoodCheckPhase.Post), path), Is.True);

        var lines = File.ReadAllLines(path);
        Assert.That(lines, Has.Length.EqualTo(2));
        Assert.That(lines[0].TrimStart(), Does.StartWith("{"));
        Assert.That(lines[1], Does.Contain("\"phase\":\"post\""));
    }

    [Test]
    public void TryAppend_UnwritableTargetWarnsAndReturnsFalse()
    {
        var malformedPath = Path.Combine(m_TempDirectory, "existing-directory");
        Directory.CreateDirectory(malformedPath);
        File.WriteAllText(Path.Combine(malformedPath, "malformed.json"), "not-json");
        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("^\\[Mood Check\\].*write"));

        Assert.That(MoodCheckLog.TryAppend(Sample(MoodCheckPhase.Pre), malformedPath), Is.False);
    }

    static MoodCheckSample Sample(MoodCheckPhase phase) => new()
    {
        sessionId = "session-1",
        visitId = "visit-1",
        phase = phase,
        valence = -.6f,
        arousal = .6f,
        mood = MoodKind.Tense,
        intensity = .8485f,
        inputMethod = MoodInputMethod.Quick,
        recommendedWorldId = "nature",
        enteredWorldId = phase == MoodCheckPhase.Pre ? string.Empty : "candy",
        timestampUtc = "2026-10-22T10:00:00.0000000Z",
        language = "zh-CN",
        durationInWorldSeconds = 42.5f,
        pinchCount = 3
    };
}
