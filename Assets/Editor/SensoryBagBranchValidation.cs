using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

public static class SensoryBagBranchValidation
{
    static TestRunnerApi runner;
    [MenuItem("Moodium/Sensory Bag Branch/Validate Branch")]
    public static void Run()
    {
        runner = ScriptableObject.CreateInstance<TestRunnerApi>();
        runner.RegisterCallbacks(new Results());
        runner.Execute(new ExecutionSettings(new Filter { testMode = TestMode.EditMode,
            groupNames = new[] { "^SensoryBagBranch", "^TablePlaneSelector", "^TableAnchorFinder", "^WorldCarouselSelection", "^WorldCarouselLayout" } }));
    }
    sealed class Results : ICallbacks
    {
        public void RunStarted(ITestAdaptor _) { }
        public void TestStarted(ITestAdaptor _) { }
        public void TestFinished(ITestResultAdaptor _) { }
        public void RunFinished(ITestResultAdaptor result)
        {
            var path = Path.Combine(Path.GetTempPath(), "Moodium-SensoryBagBranch-Tests.xml");
            TestRunnerApi.SaveResultToFile(result, path);
            Debug.Log($"[Sensory Bag Branch] tests={result.TestStatus} passed={result.PassCount} failed={result.FailCount} path={path}");
        }
    }
}
