using UnityEngine;
using UnityEngine.VFX;
using VFXViewer;

[DisallowMultipleComponent]
[RequireComponent(typeof(PointCloudController))]
[RequireComponent(typeof(VisualEffect))]
public class PointCloudAssembleFromOrigin : MonoBehaviour
{
    [Header("Origin")]
    [Tooltip("点云出现时的出发点。通常拖入触发球/触发物体中心的 Transform。")]
    [SerializeField] private Transform originTransform;
    [Tooltip("VFX Graph 中用于接收起点的 Vector3 暴露属性名。")]
    [SerializeField] private string spawnOriginProperty = "SpawnOrigin";
    [Tooltip("在 origin 基础上额外叠加的局部偏移。")]
    [SerializeField] private Vector3 localOriginOffset = Vector3.zero;
    [Tooltip("出现时是否重新初始化 VFX，让粒子重新从起点生成。")]
    [SerializeField] private bool reinitializeOnAppear = true;

    private VisualEffect _vfx;

    public Transform OriginTransform
    {
        get => originTransform;
        set => originTransform = value;
    }

    public bool IsConfigured => originTransform != null;

    private void Awake()
    {
        CacheComponents();
        PlacementDebugFileLogger.Log(
            $"[PlacementVFX] originHelper={name} stage=Awake configured={IsConfigured} " +
            $"spawnOriginProperty={spawnOriginProperty} reinitializeOnAppear={reinitializeOnAppear}");
    }

    private void OnEnable()
    {
        SyncSpawnOriginForEditorPreviewIfNeeded();
    }

    private void OnValidate()
    {
        SyncSpawnOriginForEditorPreviewIfNeeded();
    }

    public void CaptureHomePoseIfNeeded()
    {
        CacheComponents();
        PlacementDebugFileLogger.Log(
            $"[PlacementVFX] originHelper={name} stage=CaptureHomePoseIfNeeded configured={IsConfigured} " +
            $"origin={(originTransform != null ? originTransform.position.ToString() : "null")}");
    }

    public void BeginAppear()
    {
        CacheComponents();
        PlacementDebugFileLogger.Log(
            $"[PlacementVFX] originHelper={name} stage=BeginAppear:enter configured={IsConfigured} " +
            $"hasVfx={_vfx != null} spawnOriginProperty={spawnOriginProperty} reinitializeOnAppear={reinitializeOnAppear}");

        if (_vfx == null || !IsConfigured || string.IsNullOrWhiteSpace(spawnOriginProperty))
        {
            PlacementDebugFileLogger.Log(
                $"[PlacementVFX] originHelper={name} stage=BeginAppear:skip reason=" +
                $"{(_vfx == null ? "missingVfx" : !IsConfigured ? "notConfigured" : "emptyProperty")}");
            return;
        }

        if (!_vfx.HasVector3(spawnOriginProperty))
        {
            PlacementDebugFileLogger.Log(
                $"[PlacementVFX] originHelper={name} stage=BeginAppear:skip reason=missingVectorProperty");
            return;
        }

        // Point cloud graphs in this project run in the effect's local space, so
        // convert the source origin into the VFX root's local coordinates first.
        Vector3 spawnOrigin = transform.InverseTransformPoint(originTransform.position) + localOriginOffset;
        _vfx.SetVector3(spawnOriginProperty, spawnOrigin);
        PlacementDebugFileLogger.Log(
            $"[PlacementVFX] originHelper={name} stage=BeginAppear:setSpawnOrigin worldOrigin={originTransform.position} " +
            $"localOriginOffset={localOriginOffset} spawnOrigin={spawnOrigin}");

        if (reinitializeOnAppear)
        {
            _vfx.Reinit();
            PlacementDebugFileLogger.Log(
                $"[PlacementVFX] originHelper={name} stage=BeginAppear:reinit aliveParticles={_vfx.aliveParticleCount}");
        }
    }

    public void BeginDisappear()
    {
    }

    public void ApplyProgress(float visibleProgress, bool isDispersing)
    {
    }

    private void CacheComponents()
    {
        if (_vfx == null)
            _vfx = GetComponent<VisualEffect>();
    }

    private void SyncSpawnOriginForEditorPreviewIfNeeded()
    {
        if (Application.isPlaying)
            return;

        CacheComponents();
        if (_vfx == null || !IsConfigured || string.IsNullOrWhiteSpace(spawnOriginProperty))
            return;

        if (!_vfx.HasVector3(spawnOriginProperty))
            return;

        Vector3 spawnOrigin = transform.InverseTransformPoint(originTransform.position) + localOriginOffset;
        _vfx.SetVector3(spawnOriginProperty, spawnOrigin);
    }
}
