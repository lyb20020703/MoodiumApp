using System.IO;
using NUnit.Framework;

public sealed class MoodiumPortalFlowTests
{
    const string FlowPath = "Assets/Scripts/Moodium/MoodiumAppFlowController.cs";
    const string OpeningPath = "Assets/Scripts/Moodium/Opening/OpeningManager.cs";

    [Test]
    public void RuntimeStartupRoutesDirectlyToObjectWorldSelection()
    {
        var source = File.ReadAllText(FlowPath);

        StringAssert.Contains("ShowObjectWorldSelection();", source);
        StringAssert.DoesNotContain("BuildModeSelection();", source);
        StringAssert.DoesNotContain("BuildWorldSelectionPanel();", source);
        StringAssert.DoesNotContain("BuildNoObjectPanel();", source);
    }

    [Test]
    public void OpeningHandsCompletedTutorialVideoToStickerWorldSelection()
    {
        var source = File.ReadAllText(OpeningPath);

        StringAssert.Contains("BeginStickerWorldSelection(session)", source);
    }

    [Test]
    public void RealityBackReturnsToStickerWorldSelectionWhenOpeningVideoExists()
    {
        var source = File.ReadAllText(FlowPath);

        StringAssert.Contains("ReturnToStickerWorldSelection", source);
    }
}
