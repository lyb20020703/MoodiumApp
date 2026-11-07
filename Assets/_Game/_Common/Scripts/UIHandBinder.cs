using UnityEngine;
using UnityEngine.SceneManagement;

public class UIHandBinder : MonoBehaviour
{
    public enum HandType { Left, Right }

    [Header("绑定设置")]
    public HandType targetHand = HandType.Left; // 你想绑在哪只手上？
    
    [Header("位置微调 (单位: 米)")]
    // 建议位置：手掌上方一点点，向前一点点
    public Vector3 positionOffset = new Vector3(0, 0.15f, 0.1f); 
    
    // 建议旋转：X=45度可以让它稍微朝向脸部
    public Vector3 rotationOffset = new Vector3(45f, 0f, 0f); 

    [Header("缩放")]
    [Tooltip("世界坐标下的UI通常需要非常小的缩放，例如 0.001")]
    public float uiScale = 0.002f;

    private void Awake()
    {
        // 保证切换场景时脚本还在
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    // 每次场景加载完，执行绑定
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        BindToHand();
    }

    private void Start()
    {
        // 第一次启动也要绑定
        BindToHand();
    }

    public void BindToHand()
    {
        // 1. 确定我们要找的物体名字 (基于 XR Origin 默认命名)
        // 如果你的手部物体名字不一样，请在这里修改
        string handName = (targetHand == HandType.Left) ? "Left Controller" : "Right Controller";
        
        // 也可以尝试找 "LeftHand Controller" 或 "Left Hand Interaction Visual"
        // 这是一个模糊搜索，为了兼容性，我们尝试几种常见的名字
        GameObject handObj = FindObjectInScene(handName);
        if (handObj == null) handObj = FindObjectInScene(targetHand + "Hand Controller"); // 尝试找 "LeftHand Controller"
        if (handObj == null) handObj = FindObjectInScene(targetHand + " Hand"); 

        if (handObj != null)
        {
            // 2. 核心：把 Canvas 变成手的子物体
            transform.SetParent(handObj.transform);

            // 3. 重置坐标和旋转
            transform.localPosition = positionOffset;
            transform.localEulerAngles = rotationOffset;
            transform.localScale = Vector3.one * uiScale;

            // 4. 确保 Canvas 模式正确
            Canvas c = GetComponent<Canvas>();
            if (c != null)
            {
                c.renderMode = RenderMode.WorldSpace;
                // 关键：重新绑定事件相机（虽然 SceneSwitcher 做过，这里再做一次双重保险）
                c.worldCamera = Camera.main;
            }

            Debug.Log($"UI 已成功绑定到: {handObj.name}");
        }
        else
        {
            Debug.LogWarning($"在新场景中找不到 {targetHand} 手部控制器，UI 将悬浮在原处。");
        }
    }

    // 辅助函数：在当前场景查找物体
    GameObject FindObjectInScene(string nameContains)
    {
        // 这是一个低效但简单的查找，只在场景加载时跑一次，没问题
        foreach (GameObject go in FindObjectsByType<GameObject>(FindObjectsSortMode.None))
        {
            if (go.name.Contains(nameContains) && go.activeInHierarchy)
            {
                return go;
            }
        }
        return null;
    }
}