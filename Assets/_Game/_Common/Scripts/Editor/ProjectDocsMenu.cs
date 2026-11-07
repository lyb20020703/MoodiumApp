using UnityEditor;
using UnityEngine;

public static class ProjectDocsMenu
{
    private const string ProjectDocsUrl =
        "https://my.feishu.cn/wiki/ZRR2wm6PkiBai2kS179c7wJqnkc?from=from_copylink";
    private const string InteractionSheetUrl =
        "https://my.feishu.cn/wiki/Fwhtw5fiBieFBWk1YtFcye18nsd";

    [MenuItem("工具/项目文档/查看项目文档", priority = 1)]
    private static void OpenProjectDocsMenu()
    {
        Application.OpenURL(ProjectDocsUrl);
    }

    [MenuItem("工具/项目文档/查看交互模块表格", priority = 2)]
    private static void OpenInteractionSheetMenu()
    {
        Application.OpenURL(InteractionSheetUrl);
    }
}
