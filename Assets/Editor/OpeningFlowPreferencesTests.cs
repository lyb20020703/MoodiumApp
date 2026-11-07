using Moodium.Flow;
using Moodium.Opening;
using NUnit.Framework;
using UnityEngine;

public sealed class OpeningFlowPreferencesTests
{
    static readonly string[] Keys =
    {
        OpeningFlowPreferences.AskLanguageKey, OpeningFlowPreferences.AskBagKey, OpeningFlowPreferences.CompletedKey,
        OpeningFlowPreferences.LanguageKey, SensoryBagOwnership.Key,
    };
    readonly string[] m_Saved = new string[Keys.Length];
    readonly bool[] m_Had = new bool[Keys.Length];

    // These keys are the user's real editor prefs: save and restore them around every test.
    [SetUp]
    public void SaveKeys()
    {
        for (int i = 0; i < Keys.Length; i++)
        {
            m_Had[i] = PlayerPrefs.HasKey(Keys[i]);
            m_Saved[i] = Keys[i] == OpeningFlowPreferences.LanguageKey ? PlayerPrefs.GetString(Keys[i]) : PlayerPrefs.GetInt(Keys[i]).ToString();
            PlayerPrefs.DeleteKey(Keys[i]);
        }
    }

    [TearDown]
    public void RestoreKeys()
    {
        for (int i = 0; i < Keys.Length; i++)
        {
            if (!m_Had[i]) PlayerPrefs.DeleteKey(Keys[i]);
            else if (Keys[i] == OpeningFlowPreferences.LanguageKey) PlayerPrefs.SetString(Keys[i], m_Saved[i]);
            else PlayerPrefs.SetInt(Keys[i], int.Parse(m_Saved[i]));
        }
        PlayerPrefs.Save();
    }

    [TestCase(true, true, true, true)]
    [TestCase(false, true, true, false)]   // the only case that skips: turned off, seen before, answer saved
    [TestCase(false, false, true, true)]   // first run always asks
    [TestCase(false, true, false, true)]   // nothing saved to fall back on
    public void AsksUnlessTurnedOffAfterAFirstFullRun(bool preference, bool completed, bool saved, bool expected) =>
        Assert.That(OpeningFlowPreferences.ShouldAsk(preference, completed, saved), Is.EqualTo(expected));

    [Test]
    public void BothQuestionsAreAskedByDefault()
    {
        Assert.That(OpeningFlowPreferences.AskLanguage, Is.True);
        Assert.That(OpeningFlowPreferences.AskBag, Is.True);
        Assert.That(OpeningFlowPreferences.ShouldAskLanguage(), Is.True);
        Assert.That(OpeningFlowPreferences.ShouldAskBag(), Is.True);
    }

    [Test]
    public void TurnedOffQuestionsAreSkippedOnlyAfterTheFirstRun()
    {
        PlayerPrefs.SetString(OpeningFlowPreferences.LanguageKey, "English");
        SensoryBagOwnership.SetHasBag(false);
        OpeningFlowPreferences.AskLanguage = false;
        OpeningFlowPreferences.AskBag = false;
        Assert.That(OpeningFlowPreferences.ShouldAskLanguage(), Is.True, "not completed yet");
        Assert.That(OpeningFlowPreferences.ShouldAskBag(), Is.True, "not completed yet");

        PlayerPrefs.SetInt(OpeningFlowPreferences.CompletedKey, 1);
        Assert.That(OpeningFlowPreferences.ShouldAskLanguage(), Is.False);
        Assert.That(OpeningFlowPreferences.ShouldAskBag(), Is.False);
        Assert.That(OpeningFlowPreferences.SavedEnglish, Is.True);
        Assert.That(SensoryBagOwnership.HasBag, Is.False);

        OpeningFlowPreferences.AskBag = true;
        Assert.That(OpeningFlowPreferences.ShouldAskBag(), Is.True);
        Assert.That(OpeningFlowPreferences.ShouldAskLanguage(), Is.False, "the two switches are independent");
    }

    [Test]
    public void OpeningAndSettingsUseTheSwitches()
    {
        var opening = System.IO.File.ReadAllText("Assets/Scripts/Moodium/Opening/OpeningManager.cs");
        Assert.That(opening, Does.Contain("OpeningFlowPreferences.ShouldAskLanguage()"));
        Assert.That(opening, Does.Contain("OpeningFlowPreferences.ShouldAskBag()"));
        Assert.That(opening, Does.Contain("OpeningFlowPreferences.CompletedKey"), "one key for 'opening completed'");
        var flow = System.IO.File.ReadAllText("Assets/Scripts/Moodium/MoodiumAppFlowController.cs");
        Assert.That(flow, Does.Contain("ApplyOpeningQuestions"));
    }
}
