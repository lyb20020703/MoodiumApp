using System.Collections;
using Autohand;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace VFXViewer
{
    /// <summary>
    /// Makes an object grabbable and can animate it back to its recorded home pose on release.
    /// The home pose is stored relative to the original parent when possible, so the behavior
    /// keeps working after the whole module is turned into a prefab and instantiated elsewhere.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("VFXViewer/Interaction/Grab Return To Origin")]
    [RequireComponent(typeof(GrabLifecycleRelay))]
    public class GrabReturnToOrigin : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private bool autoConfigureGrabInteractable = true;
        [SerializeField] private bool addBoxColliderIfMissing = true;
        [SerializeField] private bool addRigidbodyIfMissing = true;

        [Header("Return")]
        [SerializeField] private bool recordHomePoseOnAwake = true;
        [SerializeField] private bool recordReleasedPoseAsHome = true;
        [SerializeField] private bool restoreOriginalParent = true;
        [SerializeField, Min(0.01f)] private float returnDuration = 0.45f;
        [SerializeField, Range(0f, 3f)] private float positionOvershoot = 1.1f;
        [SerializeField] private bool restoreRotation = true;

        [Header("Grab")]
        [SerializeField] private bool useDynamicAttach = true;
        [SerializeField] private XRBaseInteractable.MovementType movementType = XRBaseInteractable.MovementType.Instantaneous;
        [SerializeField] private bool throwOnDetach = false;
        [SerializeField] private bool disableGravity = true;
        [SerializeField] private bool keepKinematic = true;

        [Header("Debug")]
        [SerializeField] private bool debugLogging = false;

        private GrabLifecycleRelay _grabRelay;
        private XRGrabInteractable _grabInteractable;
        private Grabbable _grabbable;
        private Rigidbody _rigidbody;
        private Coroutine _returnRoutine;
        private bool _listenersRegistered;
        private bool _awaitingAutoHandReleaseFallback;

        private Transform _homeParent;
        private Vector3 _homeLocalPosition;
        private Quaternion _homeLocalRotation;
        private Vector3 _homeLocalScale;
        private Vector3 _homeWorldPosition;
        private Quaternion _homeWorldRotation;
        private bool _hasHomePose;

        private void Reset()
        {
            EnsureDependencies();
            ConfigureDependencies();
            CaptureHomePose();
        }

        private void Awake()
        {
            EnsureDependencies();
            ConfigureDependencies();

            if (recordHomePoseOnAwake || !_hasHomePose)
                CaptureHomePose();

            SnapToHomePose();
            LogDebug("event=Awake");
        }

        private void OnEnable()
        {
            EnsureDependencies();
            RegisterListeners();
            LogDebug("event=OnEnable");
        }

        private void OnDisable()
        {
            LogDebug("event=OnDisable:enter");
            UnregisterListeners();
            StopReturnRoutine();
            _awaitingAutoHandReleaseFallback = false;

            if (recordReleasedPoseAsHome)
            {
                // Placement mode and module state changes can temporarily disable this component.
                // In the "stay where released" mode, keep the current pose instead of snapping back.
                PrepareReleasedPose();
                CaptureHomePose();
                LogDebug("event=OnDisable:captureCurrentAsHome");
                return;
            }

            SnapToHomePose();
            LogDebug("event=OnDisable:snapToHome");
        }

        private void OnDestroy()
        {
            UnregisterListeners();
        }

        private void Update()
        {
            if (HandleHiddenHomeParent())
                return;

            if (HandleDetachedWithoutActiveGrab())
                return;

            if (!_awaitingAutoHandReleaseFallback || _grabbable == null || !_grabbable.enabled)
                return;

            if (_grabbable.HeldCount() > 0 || _grabbable.beingGrabbed)
                return;

            _awaitingAutoHandReleaseFallback = false;
            OnGrabEnded();
        }

        private bool HandleDetachedWithoutActiveGrab()
        {
            if (!_hasHomePose || _returnRoutine != null)
                return false;

            bool hasAutoHand = _grabbable != null && _grabbable.enabled;
            bool isHeldByAutoHand = hasAutoHand && (_grabbable.HeldCount() > 0 || _grabbable.beingGrabbed);
            bool isHeldByRelay = _grabRelay != null && _grabRelay.IsGrabbed;
            if (isHeldByAutoHand || isHeldByRelay)
                return false;

            if (!IsAwayFromHomePose())
                return false;

            _awaitingAutoHandReleaseFallback = false;
            PrepareReleasedPose();
            LogDebug("event=HandleDetachedWithoutActiveGrab");

            if (recordReleasedPoseAsHome)
            {
                CaptureHomePose();
                LogDebug("event=HandleDetachedWithoutActiveGrab:captureCurrentAsHome");
                return true;
            }

            StopReturnRoutine();
            _returnRoutine = StartCoroutine(ReturnHomeRoutine());
            LogDebug("event=HandleDetachedWithoutActiveGrab:returnHome");
            return true;
        }

        private bool HandleHiddenHomeParent()
        {
            if (!_hasHomePose || _homeParent == null || _homeParent.gameObject.activeInHierarchy)
                return false;

            bool isDetachedFromHomeParent = transform.parent != _homeParent;
            bool isHeldByAutoHand = _grabbable != null && (_grabbable.HeldCount() > 0 || _grabbable.beingGrabbed);
            if (!isDetachedFromHomeParent && !isHeldByAutoHand)
                return false;

            StopReturnRoutine();

            if (isHeldByAutoHand)
                _grabbable.ForceHandsRelease();

            RestoreParentIfNeeded();
            SnapToHomePose();
            LogDebug("event=HandleHiddenHomeParent:snapToHome");
            return true;
        }

        [ContextMenu("Capture Current Pose As Home")]
        public void CaptureHomePose()
        {
            _homeParent = transform.parent;
            _homeLocalPosition = transform.localPosition;
            _homeLocalRotation = transform.localRotation;
            _homeLocalScale = transform.localScale;
            _homeWorldPosition = transform.position;
            _homeWorldRotation = transform.rotation;
            _hasHomePose = true;
            LogDebug("event=CaptureHomePose");
        }

        [ContextMenu("Snap To Home Pose")]
        public void SnapToHomePose()
        {
            if (!_hasHomePose)
                return;

            RestoreParentIfNeeded();
            transform.localScale = _homeLocalScale;

            if (UseLocalSpace())
            {
                transform.localPosition = _homeLocalPosition;
                if (restoreRotation)
                    transform.localRotation = _homeLocalRotation;
            }
            else
            {
                transform.position = _homeWorldPosition;
                if (restoreRotation)
                    transform.rotation = _homeWorldRotation;
            }

            ZeroVelocities();
            if (_rigidbody != null)
            {
                if (disableGravity)
                    _rigidbody.useGravity = false;
                if (keepKinematic)
                    _rigidbody.isKinematic = true;
            }

            LogDebug("event=SnapToHomePose");
        }

        private void EnsureDependencies()
        {
            if (addBoxColliderIfMissing && GetComponent<Collider>() == null)
                gameObject.AddComponent<BoxCollider>();

            if (!TryGetComponent(out _grabRelay))
                _grabRelay = gameObject.AddComponent<GrabLifecycleRelay>();

            if (!TryGetComponent(out _rigidbody) && addRigidbodyIfMissing)
                _rigidbody = gameObject.AddComponent<Rigidbody>();

            TryGetComponent(out _grabInteractable);
            TryGetComponent(out _grabbable);
        }

        private void ConfigureDependencies()
        {
            if (_rigidbody != null)
            {
                if (disableGravity)
                    _rigidbody.useGravity = false;
                if (keepKinematic)
                    _rigidbody.isKinematic = true;
            }

            if (!autoConfigureGrabInteractable || _grabInteractable == null)
                return;

            _grabInteractable.useDynamicAttach = useDynamicAttach;
            _grabInteractable.movementType = movementType;
            _grabInteractable.throwOnDetach = throwOnDetach;
        }

        private void RegisterListeners()
        {
            if (_listenersRegistered || _grabRelay == null)
                return;

            _grabRelay.GrabStarted += OnGrabStarted;
            _grabRelay.GrabEnded += OnGrabEnded;
            _listenersRegistered = true;
        }

        private void UnregisterListeners()
        {
            if (!_listenersRegistered || _grabRelay == null)
                return;

            _grabRelay.GrabStarted -= OnGrabStarted;
            _grabRelay.GrabEnded -= OnGrabEnded;
            _listenersRegistered = false;
        }

        private void OnGrabStarted()
        {
            StopReturnRoutine();
            ZeroVelocities();
            LogDebug("event=OnGrabStarted");

            if (_grabbable != null && _grabbable.enabled)
                _awaitingAutoHandReleaseFallback = true;

            if (_rigidbody != null)
            {
                if (disableGravity)
                    _rigidbody.useGravity = false;
                // AutoHand held objects need a dynamic body while the grab joint is active.
                if (_grabbable != null && _grabbable.enabled)
                    _rigidbody.isKinematic = false;
                else if (keepKinematic)
                    _rigidbody.isKinematic = true;
            }
        }

        private void OnGrabEnded()
        {
            _awaitingAutoHandReleaseFallback = false;
            StopReturnRoutine();
            PrepareReleasedPose();
            LogDebug("event=OnGrabEnded");

            if (recordReleasedPoseAsHome)
            {
                CaptureHomePose();
                LogDebug("event=OnGrabEnded:captureCurrentAsHome");
                return;
            }

            if (!_hasHomePose)
                CaptureHomePose();

            _returnRoutine = StartCoroutine(ReturnHomeRoutine());
            LogDebug("event=OnGrabEnded:returnHome");
        }

        private IEnumerator ReturnHomeRoutine()
        {
            RestoreParentIfNeeded();

            Vector3 startPosition;
            Quaternion startRotation;
            Vector3 startScale = transform.localScale;

            if (UseLocalSpace())
            {
                startPosition = transform.localPosition;
                startRotation = transform.localRotation;
            }
            else
            {
                startPosition = transform.position;
                startRotation = transform.rotation;
            }

            float elapsed = 0f;
            while (elapsed < returnDuration)
            {
                float normalized = Mathf.Clamp01(elapsed / returnDuration);
                float positionT = EaseOutBack(normalized, positionOvershoot);
                float rotationT = Mathf.SmoothStep(0f, 1f, normalized);

                if (UseLocalSpace())
                {
                    transform.localPosition = Vector3.LerpUnclamped(startPosition, _homeLocalPosition, positionT);
                    if (restoreRotation)
                        transform.localRotation = Quaternion.Slerp(startRotation, _homeLocalRotation, rotationT);
                    transform.localScale = Vector3.LerpUnclamped(startScale, _homeLocalScale, rotationT);
                }
                else
                {
                    transform.position = Vector3.LerpUnclamped(startPosition, _homeWorldPosition, positionT);
                    if (restoreRotation)
                        transform.rotation = Quaternion.Slerp(startRotation, _homeWorldRotation, rotationT);
                    transform.localScale = Vector3.LerpUnclamped(startScale, _homeLocalScale, rotationT);
                }

                elapsed += Time.deltaTime;
                yield return null;
            }

            _returnRoutine = null;
            SnapToHomePose();
        }

        private void StopReturnRoutine()
        {
            if (_returnRoutine == null)
                return;

            StopCoroutine(_returnRoutine);
            _returnRoutine = null;
            LogDebug("event=StopReturnRoutine");
        }

        private void PrepareReleasedPose()
        {
            RestoreParentIfNeeded();

            if (_rigidbody == null)
            {
                LogDebug("event=PrepareReleasedPose:noRigidbody");
                return;
            }

            ZeroVelocities();
            if (disableGravity)
                _rigidbody.useGravity = false;
            if (keepKinematic)
                _rigidbody.isKinematic = true;
            LogDebug("event=PrepareReleasedPose");
        }

        private void RestoreParentIfNeeded()
        {
            if (!restoreOriginalParent || _homeParent == null || transform.parent == _homeParent)
                return;

            transform.SetParent(_homeParent, true);
            LogDebug("event=RestoreParentIfNeeded");
        }

        private void LogDebug(string message)
        {
            if (!debugLogging)
                return;

            string currentParent = transform.parent != null ? transform.parent.name : "null";
            string homeParent = _homeParent != null ? _homeParent.name : "null";
            string rigidbodyState = _rigidbody == null
                ? "none"
                : $"kinematic={_rigidbody.isKinematic} gravity={_rigidbody.useGravity}";
            bool relayGrabbed = _grabRelay != null && _grabRelay.IsGrabbed;
            bool autoHandGrabbed = _grabbable != null && (_grabbable.HeldCount() > 0 || _grabbable.beingGrabbed);
            bool awayFromHome = _hasHomePose && IsAwayFromHomePose();

            Debug.Log(
                $"[GrabReturnToOrigin] name={name} frame={Time.frameCount} {message} " +
                $"recordReleasedPoseAsHome={recordReleasedPoseAsHome} hasHome={_hasHomePose} " +
                $"relayGrabbed={relayGrabbed} autoHandGrabbed={autoHandGrabbed} awaitingFallback={_awaitingAutoHandReleaseFallback} " +
                $"currentParent={currentParent} homeParent={homeParent} awayFromHome={awayFromHome} " +
                $"worldPos={transform.position} homeWorldPos={_homeWorldPosition} rb={rigidbodyState}",
                this);
        }

        private bool UseLocalSpace()
        {
            return _homeParent != null && transform.parent == _homeParent;
        }

        private bool IsAwayFromHomePose()
        {
            const float positionEpsilon = 0.000001f;
            const float scaleEpsilon = 0.000001f;
            const float rotationEpsilonDegrees = 0.25f;

            if (transform.parent != _homeParent)
                return true;

            if (UseLocalSpace())
            {
                if ((transform.localPosition - _homeLocalPosition).sqrMagnitude > positionEpsilon)
                    return true;

                if ((transform.localScale - _homeLocalScale).sqrMagnitude > scaleEpsilon)
                    return true;

                if (restoreRotation &&
                    Quaternion.Angle(transform.localRotation, _homeLocalRotation) > rotationEpsilonDegrees)
                    return true;

                return false;
            }

            if ((transform.position - _homeWorldPosition).sqrMagnitude > positionEpsilon)
                return true;

            if ((transform.localScale - _homeLocalScale).sqrMagnitude > scaleEpsilon)
                return true;

            return restoreRotation &&
                   Quaternion.Angle(transform.rotation, _homeWorldRotation) > rotationEpsilonDegrees;
        }

        private void ZeroVelocities()
        {
            if (_rigidbody == null)
                return;

#if UNITY_2023_3_OR_NEWER
            _rigidbody.linearVelocity = Vector3.zero;
#else
            _rigidbody.velocity = Vector3.zero;
#endif
            _rigidbody.angularVelocity = Vector3.zero;
        }

        private static float EaseOutBack(float t, float overshoot)
        {
            float c1 = overshoot;
            float c3 = c1 + 1f;
            float x = t - 1f;
            return 1f + c3 * x * x * x + c1 * x * x;
        }
    }
}
