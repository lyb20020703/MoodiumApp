using UnityEngine;
using UnityEngine.VFX; // 必须引入这个命名空间

public class VFXTrigger : MonoBehaviour
{
    public VisualEffect vfx; // 拖入你的 VFX 组件
    public string parameterName = "ManualTime"; // 刚才在 Graph 里起的名字
    public float playSpeed = 1.0f; // 播放速度

    private bool isPlaying = false;
    private float timer = 0.0f;

    void Start()
    {
        // 游戏开始时，强制把时间设为 0，确保静止
        if(vfx != null)
        {
             vfx.SetFloat(parameterName, 0.0f);
        }
    }

    void Update()
    {
        // 示例：按下空格键触发
        if (Input.GetKeyDown(KeyCode.Space) && !isPlaying)
        {
            PlayAnimation();
        }

        // 如果处于播放状态，就开始计时
        if (isPlaying)
        {
            timer += Time.deltaTime * playSpeed;
            vfx.SetFloat(parameterName, timer);
        }
    }

    // 公共方法，也可以给 UI 按钮调用
    public void PlayAnimation()
    {
        isPlaying = true;
    }
}