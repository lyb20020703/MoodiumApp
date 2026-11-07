using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
public static class UIRefreshVerification
{
    [MenuItem("Moodium/UI Refresh/Run EditMode Tests")]
    public static void Run()
    {
        if(EditorApplication.isCompiling)return;
        foreach(var dir in new[]{"Assets/Resources/MoodiumUIRefresh","Assets/Resources/MoodiumOpening"})
            foreach(var p in System.IO.Directory.GetFiles(dir,"*.png"))AssetDatabase.ImportAsset(p,ImportAssetOptions.ForceUpdate);
        var api=ScriptableObject.CreateInstance<TestRunnerApi>();api.RegisterCallbacks(new Result());
        api.Execute(new ExecutionSettings(new Filter{testMode=TestMode.EditMode}));
    }
    sealed class Result:ICallbacks
    {
        public void RunStarted(ITestAdaptor t){} public void TestStarted(ITestAdaptor t){} public void TestFinished(ITestResultAdaptor t){}
        public void RunFinished(ITestResultAdaptor r){TestRunnerApi.SaveResultToFile(r,"docs/verification/ui-refresh-latest-tests.xml");EditorApplication.delayCall+=UIRefreshPreview.Capture;}
    }
}
