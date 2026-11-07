using Interaction;
using UnityEngine;

namespace VFXViewer
{
    public partial class ExhibitPlacementManager
    {
        public bool TryGetRootMarkerTransform(out Transform rootTransform)
        {
            rootTransform = GetRootTransform();
            return rootTransform != null;
        }

        public bool TryGetRootMarkerYaw(out float yawDegrees)
        {
            yawDegrees = 0f;
            if (!TryGetRootMarkerTransform(out var rootTransform) || rootTransform == null)
                return false;

            yawDegrees = Mathf.DeltaAngle(0f, rootTransform.eulerAngles.y);
            return true;
        }

        public bool TrySetRootMarkerYaw(float yawDegrees)
        {
            if (!TryGetRootMarkerTransform(out var rootTransform) || rootTransform == null)
            {
                onStatusMessage?.Invoke("当前没有定位点，无法修改旋转。");
                return false;
            }

            TryDetachRootFromCurrentAnchor(rootTransform);
            ParentLooseRootToModuleContainer(rootTransform);

            rootTransform.rotation = Quaternion.Euler(0f, yawDegrees, 0f);
            m_RuntimeRootTrans = rootTransform;
            SetManualSelection(rootTransform);
            RefreshPlacementProgressInDirector();

            if (!EnsureAnchorForRoot(rootTransform))
            {
                onStatusMessage?.Invoke("已修改 Root 旋转，但重新挂锚失败。");
                return false;
            }

            onStatusMessage?.Invoke($"已将定位点旋转设置为 {Mathf.RoundToInt(Mathf.DeltaAngle(0f, yawDegrees))}°");
            return true;
        }

        public bool TryGetStageModuleYaw(string moduleId, out float yawDegrees)
        {
            yawDegrees = 0f;
            if (!TryGetStageModulePlaced(moduleId, out var stageRoot) || stageRoot == null)
                return false;

            yawDegrees = Mathf.DeltaAngle(0f, stageRoot.eulerAngles.y);
            return true;
        }

        public bool TrySetStageModuleYaw(string moduleId, float yawDegrees)
        {
            moduleId = NormalizeModuleId(moduleId);
            if (!TryGetStageModulePlaced(moduleId, out var stageRoot) || stageRoot == null)
            {
                onStatusMessage?.Invoke("当前展台尚未放置，无法修改旋转。");
                return false;
            }

            stageRoot.rotation = Quaternion.Euler(0f, yawDegrees, 0f);
            SetManualSelection(stageRoot);

            bool saved = TryAutoSaveExhibit(stageRoot);
            InteractionModule.NotifyPlacementGuidePreviewExhibitPlaced(stageRoot);

            if (!saved)
            {
                onStatusMessage?.Invoke("已修改展台旋转，但保存失败。请检查定位点。");
                return false;
            }

            onStatusMessage?.Invoke($"已将当前展台旋转设置为 {Mathf.DeltaAngle(0f, yawDegrees):0.###}°");
            return true;
        }

        public bool TryGetCurrentStepPlacedExhibitTransform(out Transform exhibitRoot)
        {
            exhibitRoot = null;
            string moduleId = GetCurrentSelectedModuleId();
            LogPlacementStateSnapshot("detail resolve begin", moduleId, null, "action=resolveDetailTarget");
            if (string.IsNullOrEmpty(moduleId))
            {
                LogDetailedPlacementResolution("detail resolve skipped: current moduleId empty");
                return false;
            }

            exhibitRoot = ResolveDeleteTargetForModule(moduleId);
            if (exhibitRoot == null && TryEnsureSessionPlacementRestoredByModuleId(moduleId))
                exhibitRoot = ResolveDeleteTargetForModule(moduleId);

            LogPlacementStateSnapshot("detail resolve result", moduleId, exhibitRoot, $"resolved={(exhibitRoot != null)}");
            LogDetailedPlacementResolution(
                $"detail resolve result: moduleId={moduleId}, target={DescribePlacementResolveTarget(exhibitRoot)}");
            return exhibitRoot != null;
        }

        public bool TryGetCurrentStepPlacedExhibitYaw(out float yawDegrees)
        {
            yawDegrees = 0f;
            if (!TryGetCurrentStepPlacedExhibitTransform(out var exhibitRoot) || exhibitRoot == null)
                return false;

            yawDegrees = Mathf.DeltaAngle(0f, exhibitRoot.eulerAngles.y);
            return true;
        }

        public bool TrySetCurrentStepPlacedExhibitYaw(float yawDegrees)
        {
            if (!TryGetCurrentStepPlacedExhibitTransform(out var exhibitRoot) || exhibitRoot == null)
            {
                onStatusMessage?.Invoke("当前步骤尚未放置，无法修改旋转。");
                return false;
            }

            exhibitRoot.rotation = Quaternion.Euler(0f, yawDegrees, 0f);
            SetManualSelection(exhibitRoot);

            bool saved = TryAutoSaveExhibit(exhibitRoot);
            InteractionModule.NotifyPlacementGuidePreviewExhibitPlaced(exhibitRoot);

            if (!saved)
            {
                onStatusMessage?.Invoke("已修改旋转，但保存失败。请检查定位点。");
                return false;
            }

            onStatusMessage?.Invoke($"已将当前展品旋转设置为 {Mathf.DeltaAngle(0f, yawDegrees):0.###}°");
            return true;
        }
    }
}
