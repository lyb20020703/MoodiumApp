namespace VFXViewer
{
    public enum ChapterPlacementStepStatus
    {
        Unknown = 0,
        RootMissing = 1,
        RootUnsaved = 2,
        RootSaved = 3,
        Unplaced = 4,
        PlacedUnsaved = 5,
        Saved = 6,
    }

    public partial class ChapterPlacementDirector
    {
        public bool TryGetCurrentStepPresentation(
            out string stepDisplayName,
            out string moduleId,
            out string chapterDisplayName,
            out int stepNumber,
            out int totalSteps,
            out ExhibitAppMode mode)
        {
            stepDisplayName = string.Empty;
            moduleId = string.Empty;
            chapterDisplayName = string.Empty;
            stepNumber = 0;
            totalSteps = HasConfiguredRootStep() ? GetExhibitStepCount() : m_FlatSteps.Count;
            mode = m_CurrentAppMode;

            if (IsLegacyRootNavigationActive)
            {
                stepDisplayName = "定位点";
                return true;
            }

            if (!IsGuidedModeReady || m_FlatSteps.Count == 0)
                return false;

            int flatIndex = m_CurrentFlatIndex;
            if (flatIndex < 0) flatIndex = 0;
            if (flatIndex >= m_FlatSteps.Count) flatIndex = m_FlatSteps.Count - 1;

            if (!TryResolveFlatStep(flatIndex, out var chapter, out var step))
                return false;

            stepDisplayName = step.DisplayName;
            moduleId = step.moduleId;
            chapterDisplayName = chapter.DisplayName;

            if (HasConfiguredRootStep() &&
                !IsRootStep(step) &&
                TryGetExhibitOrdinalForFlatIndex(flatIndex, out int exhibitStepNumber, out int exhibitTotalSteps))
            {
                stepNumber = exhibitStepNumber;
                totalSteps = exhibitTotalSteps;
            }
            else
            {
                stepNumber = flatIndex + 1;
            }

            return true;
        }

        public bool TryGetCurrentStepStatus(out ChapterPlacementStepStatus status)
        {
            status = ChapterPlacementStepStatus.Unknown;

            if (m_Spawner != null &&
                IsPlacementMode &&
                !m_Spawner.HasRootMarker)
            {
                status = ChapterPlacementStepStatus.RootMissing;
                return true;
            }

            if (IsLegacyRootNavigationActive)
            {
                return TryGetRootStepStatus(out status);
            }

            if (!IsGuidedModeReady || m_FlatSteps.Count == 0)
                return false;

            int flatIndex = m_CurrentFlatIndex;
            if (flatIndex < 0) flatIndex = 0;
            if (flatIndex >= m_FlatSteps.Count) flatIndex = m_FlatSteps.Count - 1;

            if (TryGetStepByFlatIndex(flatIndex, out var currentStep) &&
                IsRootStep(currentStep))
            {
                return TryGetRootStepStatus(out status);
            }

            if (m_SavedSteps.Contains(flatIndex))
                status = ChapterPlacementStepStatus.Saved;
            else if (m_PlacedSteps.Contains(flatIndex))
                status = ChapterPlacementStepStatus.PlacedUnsaved;
            else
                status = ChapterPlacementStepStatus.Unplaced;

            return true;
        }
    }
}
