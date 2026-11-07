using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class UltimateDissolveTimedPlayer : MonoBehaviour
{
    [Header("Timing (seconds)")]
    [Min(0f)] public float AppearDuration = 1.2f;
    [Min(0f)] public float HoldDuration = 1.0f;
    [Min(0f)] public float DisappearDuration = 1.2f;

    [Header("Playback")]
    public bool PlayOnEnable = true;
    public bool Loop = false;
    public AnimationCurve TransitionCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [Range(0f, 1f)] public float VisibleTransitionValue = 0f;
    [Range(0f, 1f)] public float HiddenTransitionValue = 1f;

    [Header("Shader Properties")]
    public string TransitionProperty = "_ManualTransition";
    public string ManualAutomaticProperty = "_ManualAutomatic";
    public string ManualAutomaticKeyword = "_MANUALAUTOMATIC_ON";

    private readonly List<Material> _materials = new List<Material>();
    private Coroutine _playRoutine;

    private void Awake()
    {
        CacheMaterials();
        SetManualMode();
        SetTransition(HiddenTransitionValue);
    }

    private void OnEnable()
    {
        if (PlayOnEnable) Play();
    }

    private void OnDisable()
    {
        if (_playRoutine != null)
        {
            StopCoroutine(_playRoutine);
            _playRoutine = null;
        }
    }

    [ContextMenu("Play")]
    public void Play()
    {
        if (_playRoutine != null) StopCoroutine(_playRoutine);
        _playRoutine = StartCoroutine(PlayRoutine());
    }

    [ContextMenu("Stop And Reset")]
    public void StopAndReset()
    {
        if (_playRoutine != null)
        {
            StopCoroutine(_playRoutine);
            _playRoutine = null;
        }

        SetTransition(HiddenTransitionValue);
    }

    private IEnumerator PlayRoutine()
    {
        do
        {
            yield return LerpTransition(HiddenTransitionValue, VisibleTransitionValue, AppearDuration);

            if (HoldDuration > 0f)
                yield return new WaitForSeconds(HoldDuration);

            yield return LerpTransition(VisibleTransitionValue, HiddenTransitionValue, DisappearDuration);
        }
        while (Loop);

        _playRoutine = null;
    }

    private IEnumerator LerpTransition(float from, float to, float duration)
    {
        if (duration <= 0f)
        {
            SetTransition(to);
            yield break;
        }

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float n = Mathf.Clamp01(t / duration);
            float c = TransitionCurve != null ? TransitionCurve.Evaluate(n) : n;
            SetTransition(Mathf.Lerp(from, to, c));
            yield return null;
        }

        SetTransition(to);
    }

    private void CacheMaterials()
    {
        _materials.Clear();

        var renderers = GetComponentsInChildren<Renderer>(true);
        var unique = new HashSet<Material>();

        foreach (var r in renderers)
        {
            var mats = r.materials;
            for (int i = 0; i < mats.Length; i++)
            {
                var mat = mats[i];
                if (mat != null && unique.Add(mat))
                    _materials.Add(mat);
            }
        }
    }

    private void SetManualMode()
    {
        for (int i = 0; i < _materials.Count; i++)
        {
            var mat = _materials[i];
            if (mat == null) continue;

            if (!string.IsNullOrEmpty(ManualAutomaticProperty))
                mat.SetFloat(ManualAutomaticProperty, 0f);

            if (!string.IsNullOrEmpty(ManualAutomaticKeyword))
                mat.DisableKeyword(ManualAutomaticKeyword);
        }
    }

    private void SetTransition(float value)
    {
        float v = Mathf.Clamp01(value);

        for (int i = 0; i < _materials.Count; i++)
        {
            var mat = _materials[i];
            if (mat == null) continue;
            mat.SetFloat(TransitionProperty, v);
        }
    }
}
