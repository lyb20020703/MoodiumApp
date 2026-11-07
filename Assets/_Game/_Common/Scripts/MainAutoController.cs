using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Collections.Generic;

public class MainAutoController : MonoBehaviour
{
    [Header("UI 设置")]
    [Tooltip("把包含两个特效按钮的父物体拖进来，没有特效时会自动隐藏它")]
    public GameObject effectButtonsGroup; 

    // --- 内部变量 ---
    private List<int> contentSceneIndices = new List<int>();
    private int currentListIndex = 0;
    private int loadedSceneBuildIndex = -1;
    
    // 当前场景的分控脚本引用
    private SceneInternalController currentSubController;
    private bool isSwitching = false;

    void Start()
    {
        // 1. 自动扫描 Build Settings
        int totalScenes = SceneManager.sceneCountInBuildSettings;
        for (int i = 1; i < totalScenes; i++)
        {
            contentSceneIndices.Add(i);
        }

        if (contentSceneIndices.Count > 0)
        {
            LoadContentScene(0);
        }
    }

    // ==========================================
    //              按钮绑定区
    // ==========================================

    // --- 场景切换按钮 ---
    public void OnSceneNext()
    {
        if (isSwitching || contentSceneIndices.Count == 0) return;
        int nextIndex = (currentListIndex + 1) % contentSceneIndices.Count;
        LoadContentScene(nextIndex);
    }

    public void OnScenePrev()
    {
        if (isSwitching || contentSceneIndices.Count == 0) return;
        int prevIndex = currentListIndex - 1;
        if (prevIndex < 0) prevIndex = contentSceneIndices.Count - 1;
        LoadContentScene(prevIndex);
    }

    // --- 特效切换按钮 ---
    public void OnEffectNext()
    {
        // 直接转发给子控制器
        if (currentSubController != null)
        {
            currentSubController.NextEffect();
        }
    }

    public void OnEffectPrev()
    {
        if (currentSubController != null)
        {
            currentSubController.PrevEffect();
        }
    }

    // ==========================================
    //              核心逻辑
    // ==========================================

    private void LoadContentScene(int listIndex)
    {
        StartCoroutine(SwitchRoutine(contentSceneIndices[listIndex]));
        currentListIndex = listIndex;
    }

    IEnumerator SwitchRoutine(int nextSceneBuildIndex)
    {
        isSwitching = true;
        currentSubController = null; 

        // 1. 隐藏特效按钮 (防止在加载过程中误触)
        if(effectButtonsGroup != null) effectButtonsGroup.SetActive(false);

        // 2. 卸载旧场景
        if (loadedSceneBuildIndex != -1)
        {
            yield return SceneManager.UnloadSceneAsync(loadedSceneBuildIndex);
        }

        // 3. 加载新场景
        yield return SceneManager.LoadSceneAsync(nextSceneBuildIndex, LoadSceneMode.Additive);
        loadedSceneBuildIndex = nextSceneBuildIndex;

        // 4. 寻找分控脚本
        Scene newScene = SceneManager.GetSceneByBuildIndex(nextSceneBuildIndex);
        if (newScene.IsValid())
        {
            SceneManager.SetActiveScene(newScene);
            currentSubController = FindObjectOfType<SceneInternalController>();

            // ★ 智能控制：如果找到了分控脚本，就显示特效按钮；否则保持隐藏
            if (effectButtonsGroup != null)
            {
                effectButtonsGroup.SetActive(currentSubController != null);
            }
        }

        isSwitching = false;
    }
}