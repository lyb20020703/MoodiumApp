using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

/// <summary>Imports the tabletop sprites and runs the on-demand tutorial regression suite.</summary>
public static class TutorialOnDemandValidation
{
    static TestRunnerApi runner;

    [MenuItem("Moodium/Validate Tutorial On Demand")]
    public static void Run()
    {
        SqueezeModesSetup.Setup();
        runner = ScriptableObject.CreateInstance<TestRunnerApi>();
        runner.RegisterCallbacks(new Results());
        runner.Execute(new ExecutionSettings(new Filter
        {
            testMode = TestMode.EditMode,
            groupNames = new[] { "^GuideHelpTests$", "^WorldGuideCardsTests$", "^WorldGuideLifecycleTests$",
                "^SqueezeTableViewTests$", "^SqueezeModeControllerTests$" }
        }));
    }

    sealed class Results : ICallbacks
    {
        public void RunStarted(ITestAdaptor test) { }
        public void TestStarted(ITestAdaptor test) { }
        public void TestFinished(ITestResultAdaptor result) { }
        public void RunFinished(ITestResultAdaptor result)
        {
            var path = Path.Combine(Path.GetTempPath(), "Moodium-TutorialOnDemand-EditMode.xml");
            TestRunnerApi.SaveResultToFile(result, path);
            Debug.Log($"[Tutorial On Demand] {result.TestStatus}: {result.PassCount} passed, {result.FailCount} failed. Results: {path}");
        }
    }
}
