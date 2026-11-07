using UnityEngine;
using UnityEngine.UI;

namespace VFXViewer
{
    public partial class ExhibitControlPanel
    {
        private enum PlacementTabType
        {
            Base = 0,
            Stage = 1,
            Exhibit = 2,
        }

        private const string RuntimePlacementTabsObjectName = "__PlacementTabs";
        private const string BaseTabButtonObjectName = "BaseTabButton";
        private const string StageTabButtonObjectName = "StageTabButton";
        private const string ExhibitTabButtonObjectName = "ExhibitTabButton";

        private PlacementTabType m_ActivePlacementTab = PlacementTabType.Base;
        private GameObject m_RuntimePlacementTabsRoot;
        private Button m_RuntimeBaseTabButton;
        private Button m_RuntimeStageTabButton;
        private Button m_RuntimeExhibitTabButton;
        private int m_SelectedStageIndex;
        private bool m_RuntimePlacementModeWasActive;

        private bool IsBasePlacementTabActive()
        {
            return m_ActivePlacementTab == PlacementTabType.Base;
        }

        private bool IsStagePlacementTabActive()
        {
            return m_ActivePlacementTab == PlacementTabType.Stage;
        }

        private bool IsExhibitPlacementTabActive()
        {
            return m_ActivePlacementTab == PlacementTabType.Exhibit;
        }

        private void EnsureStagePlacementUiCreated()
        {
            if (!Application.isPlaying)
                return;

            Transform host = ResolveStagePlacementHost();
            if (host == null)
                return;

            EnsureRuntimePlacementTabs(host);
            ClampSelectedStageIndex();
        }

        private Transform ResolveStagePlacementHost()
        {
            if (m_MainPanelToHideWhenDetailOpen != null)
                return m_MainPanelToHideWhenDetailOpen.transform;

            if (m_PanelRoot != null)
                return m_PanelRoot.transform;

            return null;
        }

        private void EnsureRuntimePlacementTabs(Transform host)
        {
            if (host == null)
                return;

            if (m_RuntimePlacementTabsRoot == null)
            {
                Transform existingTabsRoot = FindNamedChildRecursive(host, RuntimePlacementTabsObjectName);
                if (existingTabsRoot != null)
                {
                    m_RuntimePlacementTabsRoot = existingTabsRoot.gameObject;
                    m_RuntimeBaseTabButton = ResolveButtonInRoot(m_RuntimePlacementTabsRoot, null, BaseTabButtonObjectName);
                    m_RuntimeStageTabButton = ResolveButtonInRoot(m_RuntimePlacementTabsRoot, null, StageTabButtonObjectName);
                    m_RuntimeExhibitTabButton = ResolveButtonInRoot(m_RuntimePlacementTabsRoot, null, ExhibitTabButtonObjectName);
                }
            }

            if (m_RuntimePlacementTabsRoot == null)
                CreateRuntimePlacementTabs(host);

            BindPlacementTabButtons();
        }

        private void CreateRuntimePlacementTabs(Transform host)
        {
            var tabsRoot = new GameObject(RuntimePlacementTabsObjectName, typeof(RectTransform), typeof(HorizontalLayoutGroup));
            var tabsRect = tabsRoot.GetComponent<RectTransform>();
            tabsRect.SetParent(host, false);
            tabsRect.anchorMin = new Vector2(0f, 1f);
            tabsRect.anchorMax = new Vector2(1f, 1f);
            tabsRect.pivot = new Vector2(0.5f, 1f);
            tabsRect.offsetMin = new Vector2(16f, -48f);
            tabsRect.offsetMax = new Vector2(-16f, -8f);

            var layout = tabsRoot.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;

            m_RuntimePlacementTabsRoot = tabsRoot;
            m_RuntimeBaseTabButton = CreateRuntimeButton(tabsRoot.transform, BaseTabButtonObjectName, m_SpawnButton, "定位");
            m_RuntimeStageTabButton = CreateRuntimeButton(tabsRoot.transform, StageTabButtonObjectName, m_DeleteCurrentPlacementButton, "展台");
            m_RuntimeExhibitTabButton = CreateRuntimeButton(tabsRoot.transform, ExhibitTabButtonObjectName, m_SaveClosestButton, "展品");
        }

        private void BindPlacementTabButtons()
        {
            if (m_RuntimeBaseTabButton != null)
            {
                m_RuntimeBaseTabButton.onClick.RemoveAllListeners();
                m_RuntimeBaseTabButton.onClick.AddListener(() => SetPlacementTab(PlacementTabType.Base));
            }

            if (m_RuntimeStageTabButton != null)
            {
                m_RuntimeStageTabButton.onClick.RemoveAllListeners();
                m_RuntimeStageTabButton.onClick.AddListener(() => SetPlacementTab(PlacementTabType.Stage));
            }

            if (m_RuntimeExhibitTabButton != null)
            {
                m_RuntimeExhibitTabButton.onClick.RemoveAllListeners();
                m_RuntimeExhibitTabButton.onClick.AddListener(() => SetPlacementTab(PlacementTabType.Exhibit));
            }
        }

        private Button CreateRuntimeButton(Transform parent, string name, Button template, string label)
        {
            if (parent == null || template == null)
                return null;

            var clone = Instantiate(template.gameObject, parent);
            clone.name = name;
            var button = clone.GetComponent<Button>();
            if (button == null)
                return null;

            InvalidateButtonLabelCache(button);
            SetButtonLabel(button, label);
            return button;
        }

        private void SetPlacementTab(PlacementTabType tab)
        {
            ClampSelectedStageIndex();

            if (m_ActivePlacementTab == tab)
            {
                SyncSelectionForActivePlacementTab();
                HideBatchActionDialog(cancelPendingAction: true);
                RequestUiRefresh(force: true);
                return;
            }

            m_ActivePlacementTab = tab;
            if (!IsBasePlacementTabActive())
                ExitDirectorRootNavigationIfNeeded();

            if (m_ActivePlacementTab == PlacementTabType.Exhibit)
                m_PlacementDirector?.EnsureExhibitStepSelectedForPlacementUi();

            HideBatchActionDialog(cancelPendingAction: true);
            SyncSelectionForActivePlacementTab();
            RequestUiRefresh(force: true);
        }

        private void RefreshStagePlacementUi(bool force)
        {
            if (!Application.isPlaying)
                return;

            EnsureStagePlacementUiCreated();
            if (m_RuntimePlacementTabsRoot == null)
                return;

            bool placementModeActive = m_PlacementDirector != null && m_PlacementDirector.IsPlacementMode;
            if (m_RuntimePlacementTabsRoot.activeSelf != placementModeActive)
                m_RuntimePlacementTabsRoot.SetActive(placementModeActive);

            if (placementModeActive && !m_RuntimePlacementModeWasActive)
            {
                m_ActivePlacementTab = PlacementTabType.Base;
                m_SelectedStageIndex = 0;
                SyncSelectionForActivePlacementTab();
                force = true;
            }

            m_RuntimePlacementModeWasActive = placementModeActive;
            if (!placementModeActive || m_DetailPanelOpen || m_DetailPanelTransitionActive)
                return;

            ClampSelectedStageIndex();
            RefreshPlacementTabButtons();
            SyncSelectionForActivePlacementTab();
        }

        private void RefreshPlacementTabButtons()
        {
            SetButtonLabel(m_RuntimeBaseTabButton, "定位");
            SetButtonLabel(m_RuntimeStageTabButton, "展台");
            SetButtonLabel(m_RuntimeExhibitTabButton, "展品");

            if (m_RuntimeBaseTabButton != null)
                m_RuntimeBaseTabButton.interactable = !IsBasePlacementTabActive();

            if (m_RuntimeStageTabButton != null)
                m_RuntimeStageTabButton.interactable = !IsStagePlacementTabActive();

            if (m_RuntimeExhibitTabButton != null)
                m_RuntimeExhibitTabButton.interactable = !IsExhibitPlacementTabActive();
        }

        private void ExitDirectorRootNavigationIfNeeded()
        {
            if (m_PlacementDirector == null ||
                !m_PlacementDirector.IsPlacementMode ||
                !m_PlacementDirector.IsRootNavigationActive)
            {
                return;
            }

            m_PlacementDirector.MoveToNextStep();
        }

        private void SyncSelectionForActivePlacementTab()
        {
            if (m_Spawner == null || m_PlacementDirector == null || !m_PlacementDirector.IsPlacementMode)
                return;

            if (IsExhibitPlacementTabActive())
                m_PlacementDirector.EnsureExhibitStepSelectedForPlacementUi();

            bool useRootSelection = IsBasePlacementTabActive();
            m_Spawner.SetRootManipulationEnabled(useRootSelection);

            if (useRootSelection)
            {
                if (!m_Spawner.SelectRootMarker(notifyUI: false))
                    m_Spawner.ClearManualSelectionOverride(notifyUI: false);
                return;
            }

            ExitDirectorRootNavigationIfNeeded();

            if (IsStagePlacementTabActive())
            {
                if (TryGetSelectedStagePlacementTarget(out var stageTarget))
                    m_Spawner.SetManualSelection(stageTarget);
                else
                    m_Spawner.ClearManualSelectionOverride(notifyUI: false);
                return;
            }

            if (m_Spawner.TryGetCurrentStepPlacedExhibitTransform(out var exhibitTarget))
                m_Spawner.SetManualSelection(exhibitTarget);
            else
                m_Spawner.ClearManualSelectionOverride(notifyUI: false);
        }

        private int GetConfiguredStageModuleCount()
        {
            if (m_PlacementDirector?.Plan?.stageModules == null)
                return 0;

            int count = 0;
            for (int i = 0; i < m_PlacementDirector.Plan.stageModules.Count; i++)
            {
                var stageModule = m_PlacementDirector.Plan.stageModules[i];
                if (!IsValidStageModule(stageModule))
                    continue;

                count++;
            }

            return count;
        }

        private bool HasConfiguredStageModules()
        {
            return GetConfiguredStageModuleCount() > 0;
        }

        private void ClampSelectedStageIndex()
        {
            int count = GetConfiguredStageModuleCount();
            if (count <= 0)
            {
                m_SelectedStageIndex = 0;
                return;
            }

            if (m_SelectedStageIndex < 0)
                m_SelectedStageIndex = 0;
            else if (m_SelectedStageIndex >= count)
                m_SelectedStageIndex = count - 1;
        }

        private bool TryGetSelectedStageModule(
            out ChapterPlacementStageModule stageModule,
            out string moduleId,
            out string displayName,
            out int stageNumber,
            out int totalStages)
        {
            stageModule = null;
            moduleId = string.Empty;
            displayName = string.Empty;
            stageNumber = 0;
            totalStages = GetConfiguredStageModuleCount();
            if (totalStages <= 0 || m_PlacementDirector?.Plan?.stageModules == null)
                return false;

            ClampSelectedStageIndex();
            int currentValidIndex = 0;
            for (int i = 0; i < m_PlacementDirector.Plan.stageModules.Count; i++)
            {
                var candidate = m_PlacementDirector.Plan.stageModules[i];
                if (!IsValidStageModule(candidate))
                    continue;

                if (currentValidIndex != m_SelectedStageIndex)
                {
                    currentValidIndex++;
                    continue;
                }

                stageModule = candidate;
                moduleId = string.IsNullOrWhiteSpace(candidate.moduleId) ? string.Empty : candidate.moduleId.Trim();
                displayName = candidate.DisplayName;
                stageNumber = currentValidIndex + 1;
                return true;
            }

            return false;
        }

        private bool TryGetSelectedStagePlacementTarget(out Transform target)
        {
            target = null;
            if (m_Spawner == null ||
                !TryGetSelectedStageModule(out _, out string moduleId, out _, out _, out _))
            {
                return false;
            }

            return m_Spawner.TryGetStageModulePlaced(moduleId, out target);
        }

        private bool TryGetCurrentExhibitPresentation(
            out string displayName,
            out string moduleId,
            out int stepNumber,
            out int totalSteps)
        {
            displayName = string.Empty;
            moduleId = string.Empty;
            stepNumber = 0;
            totalSteps = 0;

            if (m_PlacementDirector == null)
                return false;

            if (!m_PlacementDirector.EnsureExhibitStepSelectedForPlacementUi())
                return false;

            return m_PlacementDirector.TryGetCurrentStepPresentation(
                out displayName,
                out moduleId,
                out _,
                out stepNumber,
                out totalSteps,
                out _);
        }

        private static bool IsValidStageModule(ChapterPlacementStageModule stageModule)
        {
            return stageModule != null && !string.IsNullOrWhiteSpace(stageModule.moduleId);
        }

        private void SelectLastStageTab()
        {
            int count = GetConfiguredStageModuleCount();
            m_SelectedStageIndex = Mathf.Max(0, count - 1);
            SetPlacementTab(PlacementTabType.Stage);
        }
    }
}
