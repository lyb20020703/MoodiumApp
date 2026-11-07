using System.Collections;
using Autohand;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables; // XRI 3.x 命名空间

namespace VFXViewer
{
    // 定义旋转模式枚举
    public enum RotationMode
    {
        Free,           // 自由旋转 (默认)
        KeepUpright,    // 保持直立 (只允许 Y 轴旋转，适合家具/摆件)
        Fixed           // 完全固定 (不随手腕旋转)
    }

    [RequireComponent(typeof(GrabLifecycleRelay))]
    [RequireComponent(typeof(Rigidbody))]
    public class MovableExhibit : MonoBehaviour
    {
        private const string PlacementGrabColliderName = "__PlacementGrabCollider";
        private static int s_ActiveGrabCount;

        [Header("交互设置")]
        [Tooltip("控制物体被抓取时的旋转行为")]
        [SerializeField] private RotationMode m_RotationMode = RotationMode.KeepUpright;

        private GrabLifecycleRelay m_GrabRelay;
        private XRGrabInteractable m_Interactable;
        private Grabbable m_Grabbable;
        private Rigidbody m_Rb;
        private ExhibitPlacementManager m_Spawner; // 引用主控制器
        private bool m_ManipulationEnabled = true;
        private Coroutine m_PendingSaveCoroutine;
        private Coroutine m_GuidePreviewDragCoroutine;
        private bool m_GuidePreviewPoseValid;
        private Vector3 m_GuidePreviewLastPosition;
        private Quaternion m_GuidePreviewLastRotation;
        private bool m_IsGrabbed;

        public static bool IsAnyGrabInProgress => s_ActiveGrabCount > 0;

        void Awake()
        {
            m_GrabRelay = GetComponent<GrabLifecycleRelay>();
            m_Interactable = GetComponent<XRGrabInteractable>();
            m_Grabbable = GetComponent<Grabbable>();
            m_Rb = GetComponent<Rigidbody>();
            
            // 查找主控制器 (用于通知 UI 选中状态)
            m_Spawner = FindAnyObjectByType<ExhibitPlacementManager>();

            if (m_Interactable != null)
            {
                // 允许动态挂载，这样抓取时物体会吸附在手上，而不是瞬移到手心
                m_Interactable.useDynamicAttach = true;
                m_Interactable.movementType = XRBaseInteractable.MovementType.Instantaneous;
                m_Interactable.throwOnDetach = false;
            }

            UpdateRotationSettings();
        }

        void OnValidate()
        {
            if (m_Interactable == null)
                m_Interactable = GetComponent<XRGrabInteractable>();
            UpdateRotationSettings();
        }

        private void UpdateRotationSettings()
        {
            if (m_Interactable == null) return;

            switch (m_RotationMode)
            {
                case RotationMode.Fixed:
                    m_Interactable.trackRotation = false;
                    break;
                case RotationMode.Free:
                case RotationMode.KeepUpright:
                    m_Interactable.trackRotation = true;
                    break;
            }
        }

        public void SetManipulationEnabled(bool enabled)
        {
            if (m_Interactable == null)
                m_Interactable = GetComponent<XRGrabInteractable>();
            if (m_Grabbable == null)
                m_Grabbable = GetComponent<Grabbable>();
            if (m_Rb == null)
                m_Rb = GetComponent<Rigidbody>();

            bool placementMode = IsInPlacementMode();
            bool hasSpawnerMode = m_Spawner != null;
            bool xriEnabled = enabled && m_Interactable != null && (!hasSpawnerMode || placementMode);
            bool autoHandEnabled = enabled && m_Grabbable != null && (!hasSpawnerMode || !placementMode);

            m_ManipulationEnabled = xriEnabled || autoHandEnabled;

            if (m_Interactable != null)
                m_Interactable.enabled = xriEnabled;

            if (m_Grabbable != null)
                m_Grabbable.enabled = autoHandEnabled;

            if (m_Rb != null && !m_ManipulationEnabled)
                m_Rb.isKinematic = true;
        }

        void OnEnable()
        {
            if (m_GrabRelay == null)
                m_GrabRelay = GetComponent<GrabLifecycleRelay>();

            if (m_GrabRelay != null)
            {
                m_GrabRelay.GrabStarted += OnGrabStart;
                m_GrabRelay.GrabEnded += OnGrabEnd;
            }

            Application.onBeforeRender += HandleBeforeRender;
        }

        void OnDisable()
        {
            if (m_GrabRelay != null)
            {
                m_GrabRelay.GrabStarted -= OnGrabStart;
                m_GrabRelay.GrabEnded -= OnGrabEnd;
            }

            Application.onBeforeRender -= HandleBeforeRender;
            SetGrabbedState(false);
            StopGuidePreviewDragCoroutine();
        }

        // === 核心：每帧修正旋转 ===
        void LateUpdate()
        {
            ApplyKeepUprightRotationIfNeeded();
        }

        private void HandleBeforeRender()
        {
            ApplyKeepUprightRotationIfNeeded();
        }

        private void ApplyKeepUprightRotationIfNeeded()
        {
            if (!m_ManipulationEnabled || m_GrabRelay == null)
                return;

            if (!m_GrabRelay.IsGrabbed || m_RotationMode != RotationMode.KeepUpright)
                return;

            Vector3 currentEuler = transform.eulerAngles;
            transform.rotation = Quaternion.Euler(0f, currentEuler.y, 0f);
        }

        // === 抓取开始 ===
        private void OnGrabStart()
        {
            if (!m_ManipulationEnabled)
                return;

            SetGrabbedState(true);

            if (IsInPlacementMode())
            {
                ResetGuidePreviewPoseTracking();
                StartGuidePreviewDragCoroutine();
            }

            // 1. 通知主控制器：我被选中了！(更新 UI)
            if (m_Spawner != null)
            {
                m_Spawner.SetManualSelection(this.transform);
            }

            // 2. 父子分离逻辑 (脱离旧锚点)
            // 必须脱离，否则移动物体时，旧的 ARAnchor 会试图把物体拉回原位
            if (m_Spawner != null && m_Spawner.TryDetachExhibitFromCurrentAnchor(transform))
            {
                Debug.Log($"[交互] {name} 已脱离锚点，开始移动");
            }

            // 3. 物理设置
            if (m_Rb != null) m_Rb.isKinematic = true; // 抓取时关闭物理模拟
        }

        // === 抓取结束 ===
        private void OnGrabEnd()
        {
            if (!m_ManipulationEnabled)
                return;

            SetGrabbedState(false);

            if (IsInPlacementMode())
            {
                StopGuidePreviewDragCoroutine();
                ResetGuidePreviewPoseTracking();
            }

            if (m_PendingSaveCoroutine != null)
                StopCoroutine(m_PendingSaveCoroutine);

            m_PendingSaveCoroutine = StartCoroutine(SaveAfterReleaseSettles());
        }

        private IEnumerator SaveAfterReleaseSettles()
        {
            yield return null;
            yield return new WaitForEndOfFrame();

            if (m_RotationMode == RotationMode.KeepUpright)
            {
                Vector3 e = transform.eulerAngles;
                transform.rotation = Quaternion.Euler(0, e.y, 0);
            }

            if (m_Spawner != null)
            {
                m_Spawner.SetManualSelection(this.transform);
                if (!m_Spawner.TryAutoSaveExhibit(this.transform))
                {
                    m_Spawner.onStatusMessage?.Invoke($"已移动: {name}，但保存失败。请检查定位点。");
                }
                else
                {
                    m_Spawner.onStatusMessage?.Invoke($"已移动: {name}，正在更新空间锚点。");
                }
            }

            Debug.Log($"[交互] {name} 移动结束，已尝试保存锚点");
            if (IsInPlacementMode())
            {
                Interaction.InteractionModule.NotifyPlacementGuidePreviewExhibitPlaced(transform);
                ResetGuidePreviewPoseTracking();
            }
            m_PendingSaveCoroutine = null;
        }

        private void NotifyGuidePreviewDragIfNeeded(bool force)
        {
            Vector3 currentPosition = transform.position;
            Quaternion currentRotation = transform.rotation;
            if (!force &&
                m_GuidePreviewPoseValid &&
                (currentPosition - m_GuidePreviewLastPosition).sqrMagnitude <= 0.000001f &&
                Quaternion.Angle(currentRotation, m_GuidePreviewLastRotation) <= 0.01f)
            {
                return;
            }

            m_GuidePreviewLastPosition = currentPosition;
            m_GuidePreviewLastRotation = currentRotation;
            m_GuidePreviewPoseValid = true;
            Interaction.InteractionModule.NotifyPlacementGuidePreviewExhibitMoved(transform);
        }

        private void ResetGuidePreviewPoseTracking()
        {
            m_GuidePreviewPoseValid = false;
            m_GuidePreviewLastPosition = Vector3.zero;
            m_GuidePreviewLastRotation = Quaternion.identity;
        }

        private void StartGuidePreviewDragCoroutine()
        {
            StopGuidePreviewDragCoroutine();
            if (!isActiveAndEnabled || !IsInPlacementMode())
                return;

            m_GuidePreviewDragCoroutine = StartCoroutine(GuidePreviewDragCoroutine());
        }

        private void StopGuidePreviewDragCoroutine()
        {
            if (m_GuidePreviewDragCoroutine == null)
                return;

            StopCoroutine(m_GuidePreviewDragCoroutine);
            m_GuidePreviewDragCoroutine = null;
        }

        private IEnumerator GuidePreviewDragCoroutine()
        {
            while (m_GrabRelay != null && m_GrabRelay.IsGrabbed && IsInPlacementMode())
            {
                yield return new WaitForEndOfFrame();

                if (m_GrabRelay == null || !m_GrabRelay.IsGrabbed || !IsInPlacementMode())
                {
                    m_GuidePreviewDragCoroutine = null;
                    yield break;
                }

                NotifyGuidePreviewDragIfNeeded(force: false);
            }

            m_GuidePreviewDragCoroutine = null;
        }

        private bool IsInPlacementMode()
        {
            return m_Spawner != null && m_Spawner.CurrentAppMode == ExhibitAppMode.Placement;
        }

        private void OnDrawGizmosSelected()
        {
            Transform colliderTransform = transform.Find(PlacementGrabColliderName);
            if (colliderTransform == null)
                return;

            var boxCollider = colliderTransform.GetComponent<BoxCollider>();
            if (boxCollider == null || !boxCollider.enabled)
                return;

            Matrix4x4 previousMatrix = Gizmos.matrix;
            Color previousColor = Gizmos.color;

            Gizmos.matrix = colliderTransform.localToWorldMatrix;
            Gizmos.color = m_ManipulationEnabled
                ? new Color(0.18f, 0.84f, 0.36f, 0.18f)
                : new Color(0.65f, 0.65f, 0.65f, 0.12f);
            Gizmos.DrawCube(boxCollider.center, boxCollider.size);

            Gizmos.color = m_ManipulationEnabled
                ? new Color(0.12f, 0.95f, 0.32f, 0.95f)
                : new Color(0.7f, 0.7f, 0.7f, 0.85f);
            Gizmos.DrawWireCube(boxCollider.center, boxCollider.size);

            Gizmos.matrix = previousMatrix;
            Gizmos.color = previousColor;
        }

        private void OnDestroy()
        {
            SetGrabbedState(false);
            StopGuidePreviewDragCoroutine();
        }

        private void SetGrabbedState(bool grabbed)
        {
            if (m_IsGrabbed == grabbed)
                return;

            m_IsGrabbed = grabbed;
            if (grabbed)
                s_ActiveGrabCount++;
            else if (s_ActiveGrabCount > 0)
                s_ActiveGrabCount--;
        }
    }
}
