using System.Collections;
using UnityEngine;
using UnityEngine.VFX;

[DisallowMultipleComponent]
[RequireComponent(typeof(VisualEffect))]
public class PointCloudQianlao2RevealController : MonoBehaviour
{
    [SerializeField] private VisualEffect vfx;
    [SerializeField] private float revealDuration = 2f;
    [SerializeField] private AnimationCurve revealCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField] private bool playOnEnable;
    [SerializeField] private bool reinitOnReveal = true;

    private Coroutine _revealCoroutine;

    public float RevealDuration
    {
        get => revealDuration;
        set => revealDuration = Mathf.Max(0.01f, value);
    }

    private void Reset()
    {
        vfx = GetComponent<VisualEffect>();
    }

    private void Awake()
    {
        if (vfx == null)
            vfx = GetComponent<VisualEffect>();
    }

    private void OnEnable()
    {
        if (playOnEnable)
            PlayReveal();
    }

    public void PlayReveal()
    {
        if (vfx == null)
            vfx = GetComponent<VisualEffect>();
        if (vfx == null)
            return;

        if (_revealCoroutine != null)
            StopCoroutine(_revealCoroutine);

        _revealCoroutine = StartCoroutine(PlayRevealCoroutine());
    }

    public void SetVisibleImmediate()
    {
        if (vfx == null)
            vfx = GetComponent<VisualEffect>();
        if (vfx == null)
            return;

        vfx.SetFloat("AssembleWeight", 1f);
        vfx.SetFloat("ScatterForce", 0f);
        vfx.SetBool("EnableRising", false);
        vfx.SetFloat("FadeAlpha", 1f);
    }

    public void SetHiddenImmediate()
    {
        if (vfx == null)
            vfx = GetComponent<VisualEffect>();
        if (vfx == null)
            return;

        vfx.SetFloat("AssembleWeight", 1f);
        vfx.SetFloat("ScatterForce", 0f);
        vfx.SetBool("EnableRising", false);
        vfx.SetFloat("FadeAlpha", 0f);
    }

    private IEnumerator PlayRevealCoroutine()
    {
        vfx.SetFloat("AssembleWeight", 1f);
        vfx.SetFloat("ScatterForce", 0f);
        vfx.SetBool("EnableRising", false);
        vfx.SetFloat("FadeAlpha", 0f);

        if (reinitOnReveal)
            vfx.Reinit();

        vfx.Play();

        float duration = Mathf.Max(0.01f, revealDuration);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float alpha = Mathf.Clamp01(revealCurve.Evaluate(t));
            vfx.SetFloat("FadeAlpha", alpha);
            yield return null;
        }

        vfx.SetFloat("FadeAlpha", 1f);
        _revealCoroutine = null;
    }
}
