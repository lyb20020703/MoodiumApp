using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

public static class MoodiStickerVoiceValidation
{
    [MenuItem("Moodium/Validate House Contact Candy")]
    public static void RunHouseContactCandy()
    {
        var api=ScriptableObject.CreateInstance<TestRunnerApi>();api.RegisterCallbacks(new HouseResults());
        api.Execute(new ExecutionSettings(new Filter{testMode=TestMode.EditMode,
            groupNames=new[]{"^CompletionHouseSoftInteractionTests","^CandyCompletionHouseControllerTests","^CandyBurstControllerTests"}}));
    }
    sealed class HouseResults:ICallbacks
    {
        public void RunStarted(ITestAdaptor test){}
        public void TestStarted(ITestAdaptor test){}
        public void TestFinished(ITestResultAdaptor result){}
        public void RunFinished(ITestResultAdaptor result)=>TestRunnerApi.SaveResultToFile(result,Path.Combine(Path.GetTempPath(),"Moodium-HouseContactCandy-Tests.xml"));
    }
    [MenuItem("Moodium/Validate Sticker Voice")]
    public static void Run()
    {
        var api=ScriptableObject.CreateInstance<TestRunnerApi>();
        api.RegisterCallbacks(new Results());
        api.Execute(new ExecutionSettings(new Filter{testMode=TestMode.EditMode,
            groupNames=new[]{"^MoodiVoiceTests","^MoodiStickerInviteTests","^StickerWorldSelectionControllerTests"}}));
    }
    sealed class Results:ICallbacks
    {
        public void RunStarted(ITestAdaptor test){}
        public void TestStarted(ITestAdaptor test){}
        public void TestFinished(ITestResultAdaptor result){}
        public void RunFinished(ITestResultAdaptor result)
        {
            var path=Path.Combine(Path.GetTempPath(),"Moodium-StickerVoice-Tests.xml");
            TestRunnerApi.SaveResultToFile(result,path);
            Debug.Log("[Moodi Sticker Voice] Results: "+path);
        }
    }
}
