using UnityEngine;
using System.Collections.Generic;

public class SceneInternalController : MonoBehaviour
{
    [Header("设置")]
    [Tooltip("请把包含所有特效的【父物体】拖到这里")]
    public Transform effectsRoot; 

    // 这个列表现在由代码自动填充，不需要你在 Inspector 里填了
    private List<GameObject> effectsList = new List<GameObject>();
    private int currentIndex = 0;

    void Start()
    {
        if (effectsRoot == null)
        {
            Debug.LogError($"[SceneInternalController] {gameObject.name} 没有绑定 Effects Root！无法切换特效。");
            return;
        }
        
        effectsRoot.localScale = new Vector3(0.33f, 0.33f, 0.33f);

        // 1. 遍历父物体下的所有子物体，加入列表
        foreach (Transform child in effectsRoot)
        {
            effectsList.Add(child.gameObject);
        }

        // 2. 初始化显示第 0 个
        if (effectsList.Count > 0)
        {
            ShowEffect(0);
        }
    }

    // --- 下面的逻辑保持不变 ---

    public void NextEffect()
    {
        if (effectsList.Count == 0) return;
        
        currentIndex = (currentIndex + 1) % effectsList.Count;
        ShowEffect(currentIndex);
    }

    public void PrevEffect()
    {
        if (effectsList.Count == 0) return;

        currentIndex--;
        if (currentIndex < 0) currentIndex = effectsList.Count - 1;
        ShowEffect(currentIndex);
    }

    private void ShowEffect(int index)
    {
        for (int i = 0; i < effectsList.Count; i++)
        {
            if (effectsList[i] != null)
                effectsList[i].SetActive(i == index);
        }
    }
}