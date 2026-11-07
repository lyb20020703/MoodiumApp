using System;
using UnityEngine;

public class DebugPosition : MonoBehaviour
{
    void Update()
    {
        // 如果物体位置异常（太远，或无效），打印出来
        if (transform.position.magnitude > 100f || float.IsNaN(transform.position.x))
        {
            Debug.LogError($"[消失追踪] 物体 {name} 飞走了！坐标: {transform.position}, 缩放: {transform.localScale}");
            // 暂停游戏以便查看
            // Debug.Break(); 
        }
    }
    
    // 监控抓取时的坐标变化
    void OnTransformParentChanged()
    {
        Debug.Log($"[父级改变] 新父级: {transform.parent?.name}, 当前坐标: {transform.position}");
    }

    private void OnDestroy()
    {
        Debug.Log($"物体 {name} 被销毁了");
    }
}