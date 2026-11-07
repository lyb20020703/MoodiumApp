using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VFXViewer;

namespace Interaction
{
    /// <summary>
    /// Starts sibling InteractionModules when this module begins appearing.
    /// Used for grouped exhibits that should share a single user trigger.
    /// </summary>
    [DisallowMultipleComponent]
    public class InteractionModuleGroupStartRelay : MonoBehaviour
    {
        [SerializeField] private InteractionModule sourceModule;
        [SerializeField] private List<string> targetModuleIds = new List<string>();
        [SerializeField] private List<float> targetStartDelays = new List<float>();
        [SerializeField] private bool triggerOnAppearing = true;
        [SerializeField] private bool triggerOnce = true;

        private bool hasTriggered;
        private Coroutine triggerRoutine;

        private void Awake()
        {
            if (sourceModule == null)
                sourceModule = GetComponent<InteractionModule>();
        }

        private void OnEnable()
        {
            if (sourceModule == null)
                sourceModule = GetComponent<InteractionModule>();

            if (sourceModule != null)
            {
                sourceModule.PhaseChanged -= OnSourcePhaseChanged;
                sourceModule.PhaseChanged += OnSourcePhaseChanged;
            }
        }

        private void OnDisable()
        {
            if (sourceModule != null)
                sourceModule.PhaseChanged -= OnSourcePhaseChanged;

            if (triggerRoutine != null)
            {
                StopCoroutine(triggerRoutine);
                triggerRoutine = null;
            }
        }

        private void OnSourcePhaseChanged(InteractionModule module, InteractionPhase previous, InteractionPhase current)
        {
            if (triggerOnce && hasTriggered)
                return;

            bool shouldTrigger = triggerOnAppearing
                ? current == InteractionPhase.Appearing
                : current == InteractionPhase.InProgress;

            if (!shouldTrigger)
                return;

            hasTriggered = true;
            if (triggerRoutine != null)
                StopCoroutine(triggerRoutine);
            triggerRoutine = StartCoroutine(TriggerTargets());
        }

        private IEnumerator TriggerTargets()
        {
            float elapsed = 0f;

            for (int i = 0; i < targetModuleIds.Count; i++)
            {
                string moduleId = targetModuleIds[i];
                if (string.IsNullOrWhiteSpace(moduleId))
                    continue;

                float targetDelay = i < targetStartDelays.Count ? Mathf.Max(0f, targetStartDelays[i]) : 0f;
                float wait = targetDelay - elapsed;
                if (wait > 0f)
                {
                    elapsed += wait;
                    yield return new WaitForSeconds(wait);
                }

                var manager = ResolveModuleManager();
                if (manager == null)
                    continue;

                if (manager.TryGetModuleById(moduleId, out var targetModule) && targetModule != null)
                    targetModule.TryTriggerStart();
            }

            triggerRoutine = null;
        }

        private static InteractionModuleManager ResolveModuleManager()
        {
            var manager = Object.FindFirstObjectByType<InteractionModuleManager>(FindObjectsInactive.Include);
            if (manager != null)
                manager.RefreshRegistry();
            return manager;
        }
    }
}
