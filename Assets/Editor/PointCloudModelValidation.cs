using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

public static class PointCloudModelValidation
{
    [MenuItem("Moodium/Point Cloud Models/Bake All Round Point Clouds")]
    public static void BakeAllRoundPointClouds()
    {
        PointCloudModelBaker.BakeProjectModels();
        BubbleWorldBaker.Bake();
    }
    [MenuItem("Moodium/Point Cloud Models/Validate Integration")]
    public static void Run()
    {
        var api = ScriptableObject.CreateInstance<TestRunnerApi>();
        api.RegisterCallbacks(new Results());
        api.Execute(new ExecutionSettings(new Filter
        {
            testMode = TestMode.EditMode,
            groupNames = new[] { "^PointCloudFxTests", "^CandyCompletionHouseControllerTests", "^CompletionHouseSoftInteractionTests", "^NatureForestGrowthControllerTests", "^BubbleWorldTests", "^MoodiRescueTests" }
        }));
    }

    sealed class Results : ICallbacks
    {
        public void RunStarted(ITestAdaptor testsToRun) { }
        public void TestStarted(ITestAdaptor test) { }
        public void TestFinished(ITestResultAdaptor result) { }
        public void RunFinished(ITestResultAdaptor result)
        {
            var path = Path.Combine(Path.GetTempPath(), "Moodium-PointCloudModels-Tests.xml");
            TestRunnerApi.SaveResultToFile(result, path);
            Debug.Log($"[Point Clouds] Integration tests: {result.TestStatus}; {result.PassCount} passed, {result.FailCount} failed. Results: {path}");
        }
    }
}
