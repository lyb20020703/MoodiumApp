using System;
using System.Collections.Generic;
using UnityEngine;

namespace VFXViewer
{
    [CreateAssetMenu(fileName = "ChapterPlacementPlan", menuName = "Museum/Chapter Placement Plan")]
    public class ChapterPlacementPlan : ScriptableObject
    {
        [Header("定位点")]
        public ChapterPlacementRootSettings rootSettings = new ChapterPlacementRootSettings();

        [Header("章节与步骤")]
        public List<ChapterPlacementChapter> chapters = new List<ChapterPlacementChapter>();

        [Header("展台")]
        public List<ChapterPlacementStageModule> stageModules = new List<ChapterPlacementStageModule>();

        public bool HasAnyStep
        {
            get
            {
                if (chapters == null || chapters.Count == 0) return false;
                for (int i = 0; i < chapters.Count; i++)
                {
                    var c = chapters[i];
                    if (c != null && c.steps != null && c.steps.Count > 0) return true;
                }
                return false;
            }
        }
    }

    [Serializable]
    public class ChapterPlacementStageModule
    {
        [Tooltip("模块 ID（建议和 InteractionModule 命名一致）")]
        public string moduleId = "Stage-01";

        [Tooltip("模块显示名（可选）")]
        public string moduleName = "";

        [Tooltip("该模块对应的预制体")]
        public GameObject prefab;

        [Tooltip("该模块放置时距离摄像机的默认距离（米）")]
        public float spawnDistance = 1f;

        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(moduleName)) return moduleName;
                return string.IsNullOrWhiteSpace(moduleId) ? "UnnamedStageModule" : moduleId;
            }
        }
    }

    [Serializable]
    public class ChapterPlacementRootSettings
    {
        [Tooltip("布展时 Root 使用的可视化预制体。为空时使用场景中的兜底配置。")]
        public GameObject markerPrefab;

        [Tooltip("首次自动创建 Root 时，距离摄像机的默认距离（米）。")]
        public float spawnDistance = 1.2f;
    }

    [Serializable]
    public class ChapterPlacementChapter
    {
        [Tooltip("章节 ID，例如 P00 / P01 / ... / P07")]
        public string chapterId = "P00";

        [Tooltip("章节显示名（可选）")]
        public string chapterName = "";

        [Tooltip("该章节需要依次放置并保存的模块清单")]
        public List<ChapterPlacementStep> steps = new List<ChapterPlacementStep>();

        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(chapterName)) return chapterName;
                return string.IsNullOrWhiteSpace(chapterId) ? "UnknownChapter" : chapterId;
            }
        }
    }

    [Serializable]
    public enum ChapterPlacementStepKind
    {
        Exhibit = 0,
        Root = 1,
    }

    [Serializable]
    public class ChapterPlacementStep
    {
        [Tooltip("步骤类型：Root=定位点，Exhibit=普通展品")]
        public ChapterPlacementStepKind stepKind = ChapterPlacementStepKind.Exhibit;

        [Tooltip("模块 ID（建议和 InteractionModule 命名一致）")]
        public string moduleId = "Module-01";

        [Tooltip("模块显示名（可选）")]
        public string moduleName = "";

        [Tooltip("可选分组键：留空=按顺序单步；相邻且同键步骤将作为一组，组内全部完成后才进入下一步。")]
        public string groupKey = "";

        [Tooltip("该模块对应的预制体")]
        public GameObject prefab;

        [Tooltip("该模块放置时距离摄像机的默认距离（米）")]
        public float spawnDistance = 1f;

        [Tooltip("可选：固定实例 ID（为空则自动分配）")]
        public string fixedInstanceId = "";

        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(moduleName)) return moduleName;
                return string.IsNullOrWhiteSpace(moduleId) ? "UnnamedModule" : moduleId;
            }
        }
    }
}
