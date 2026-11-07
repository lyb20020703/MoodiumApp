using System;
using UnityEngine;

namespace Interaction
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Animator))]
    public sealed class AnimatorActivationController : MonoBehaviour
    {
        private const bool DebugLogging = false;

        private Animator _animator;
        private bool _playbackActive;
        private float _remainingTime;
        private Action _pendingCompletion;
        private string _activeStateName;

        private void OnDisable()
        {
            Log("OnDisable:beforeCancel");
            CancelPendingPlayback();
        }

        private void Update()
        {
            if (!_playbackActive)
                return;

            _remainingTime -= Time.deltaTime;
            if (_remainingTime <= 0.05f && _remainingTime + Time.deltaTime > 0.05f)
                Log("Update:aboutToComplete");

            if (_remainingTime > 0f)
                return;

            CompletePlayback();
        }

        public bool TryPlay(string stateName, Action onPlaybackComplete = null)
        {
            ResolveAnimator();
            if (_animator == null || string.IsNullOrEmpty(stateName))
            {
                Log("TryPlay:invalid", stateName);
                onPlaybackComplete?.Invoke();
                return false;
            }

            Log("TryPlay:enter", stateName);
            CancelPendingPlayback();

            _playbackActive = true;
            _pendingCompletion = onPlaybackComplete;
            _activeStateName = stateName;
            SetAnimatorEnabled(true);
            _animator.Play(stateName, 0, 0f);
            _animator.Update(0f);

            _remainingTime = ResolveClipLength(stateName);
            Log("TryPlay:scheduled", stateName);
            if (_remainingTime <= 0f)
            {
                CompletePlayback();
                return true;
            }

            return true;
        }

        private void CancelPendingPlayback()
        {
            Log("CancelPendingPlayback");
            _playbackActive = false;
            _remainingTime = 0f;
            _pendingCompletion = null;
            _activeStateName = null;
        }

        private void CompletePlayback()
        {
            Log("CompletePlayback:enter", _activeStateName);
            Action onPlaybackComplete = _pendingCompletion;
            _playbackActive = false;
            _remainingTime = 0f;
            _pendingCompletion = null;
            _activeStateName = null;

            // Playback should always release Animator ownership after the clip ends.
            SetAnimatorEnabled(false);

            Log("CompletePlayback:exit");
            onPlaybackComplete?.Invoke();
        }

        private float ResolveClipLength(string stateName)
        {
            if (_animator == null || _animator.runtimeAnimatorController == null)
                return 0f;

            AnimationClip[] clips = _animator.runtimeAnimatorController.animationClips;
            if (clips == null)
                return 0f;

            for (int i = 0; i < clips.Length; i++)
            {
                AnimationClip clip = clips[i];
                if (clip != null && clip.name == stateName)
                    return clip.length;
            }

            return 0f;
        }

        private void ResolveAnimator()
        {
            if (_animator == null)
                TryGetComponent(out _animator);
        }

        private void SetAnimatorEnabled(bool enabled)
        {
            if (_animator == null)
                return;

            if (_animator.enabled == enabled)
            {
                Log(enabled ? "SetAnimatorEnabled:alreadyTrue" : "SetAnimatorEnabled:alreadyFalse");
                return;
            }

            _animator.enabled = enabled;
            Log(enabled ? "SetAnimatorEnabled:true" : "SetAnimatorEnabled:false");
        }

        private void Log(string eventName, string stateName = null)
        {
            if (!DebugLogging)
                return;

            ResolveAnimator();
            Debug.Log(
                $"[AnimatorActivationController] name={name} frame={Time.frameCount} event={eventName} " +
                $"state={(string.IsNullOrEmpty(stateName) ? "-" : stateName)} " +
                $"activeState={(string.IsNullOrEmpty(_activeStateName) ? "-" : _activeStateName)} " +
                $"playbackActive={_playbackActive} remaining={_remainingTime:F3} " +
                $"animatorEnabled={(_animator != null && _animator.enabled)}",
                this);
        }
    }
}
