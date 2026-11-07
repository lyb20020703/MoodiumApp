using UnityEngine;

public class WaterBallInputTest : MonoBehaviour
{
    [SerializeField] private Animator animator;

    private bool isVisible = false;
    private bool isTalking = false;
    private bool isTransitioning = false;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            ToggleVisible();
        }

        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            PlayTouch();
        }

        if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            StartTalk();
        }

        if (Input.GetKeyDown(KeyCode.Alpha3))
        {
            StopTalk();
        }
    }

    void ToggleVisible()
    {
        if (isTransitioning) return;

        if (!isVisible)
        {
            isTransitioning = true;
            animator.SetTrigger("Appear");
        }
        else
        {
            isTransitioning = true;
            isTalking = false;
            animator.SetBool("IsTalking", false);
            animator.SetTrigger("Disappear");
        }
    }

    void PlayTouch()
    {
        if (!isVisible || isTransitioning) return;
        if (isTalking) return;

        animator.SetTrigger("Touch");
    }

    void StartTalk()
    {
        if (!isVisible || isTransitioning) return;

        isTalking = true;
        animator.SetBool("IsTalking", true);
    }

    void StopTalk()
    {
        if (!isVisible || isTransitioning) return;

        isTalking = false;
        animator.SetBool("IsTalking", false);
    }

    // ===== 下面两个函数给 Animation Event 调用 =====

    public void OnAppearFinished()
    {
        isVisible = true;
        isTransitioning = false;
    }

    public void OnDisappearFinished()
    {
        isVisible = false;
        isTransitioning = false;
    }
}