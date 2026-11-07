using System.Collections;
using UnityEngine;

namespace Moodium
{
    public sealed class ChocolateCrackAutoLoopDebug : MonoBehaviour
    {
        [SerializeField] Animator m_Animator;
        [SerializeField] string m_StateName = "ChocolateCrack";
        [SerializeField, Min(0f)] float m_StartDelay = 1f;
        [SerializeField, Min(0f)] float m_EndPosePause = 0.5f;

        int m_StateHash;
        Coroutine m_PlaybackCoroutine;

        void OnEnable()
        {
            if (m_Animator == null)
                m_Animator = GetComponentInChildren<Animator>(true);

            m_StateHash = Animator.StringToHash(m_StateName);
            m_PlaybackCoroutine = StartCoroutine(PlayLoop());
        }

        void OnDisable()
        {
            if (m_PlaybackCoroutine != null)
                StopCoroutine(m_PlaybackCoroutine);
            m_PlaybackCoroutine = null;
        }

        IEnumerator PlayLoop()
        {
            if (m_Animator == null)
            {
                Debug.LogError("[Moodium Animation Debug] Animator is missing.");
                yield break;
            }

            var controller = m_Animator.runtimeAnimatorController;
            if (controller == null)
            {
                Debug.LogError("[Moodium Animation Debug] Animator Controller is missing.");
                yield break;
            }

            if (!m_Animator.HasState(0, m_StateHash))
            {
                Debug.LogError($"[Moodium Animation Debug] Animator state '{m_StateName}' was not found.");
                yield break;
            }

            var clipNames = string.Join(", ", System.Array.ConvertAll(
                controller.animationClips,
                clip => clip != null ? $"{clip.name} ({clip.length:F3}s)" : "null"));
            Debug.Log(
                $"[Moodium Animation Debug] Ready. controller={controller.name}, " +
                $"state={m_StateName}, clips=[{clipNames}], startDelay={m_StartDelay:F1}s");

            if (m_StartDelay > 0f)
                yield return new WaitForSeconds(m_StartDelay);

            while (enabled && gameObject.activeInHierarchy)
            {
                m_Animator.speed = 1f;
                m_Animator.Play(m_StateHash, 0, 0f);
                m_Animator.Update(0f);
                Debug.Log("[Moodium Animation Debug] ChocolateCrack loop started.");

                yield return null;
                while (enabled &&
                       gameObject.activeInHierarchy &&
                       m_Animator.GetCurrentAnimatorStateInfo(0).normalizedTime < 1f)
                {
                    yield return null;
                }

                Debug.Log("[Moodium Animation Debug] ChocolateCrack reached the end pose.");
                if (m_EndPosePause > 0f)
                    yield return new WaitForSeconds(m_EndPosePause);
            }
        }
    }
}
