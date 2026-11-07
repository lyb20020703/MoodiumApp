# Moodium-WorldGuide 补丁 01：按钮样式修复

适用于已经接入的 `Moodium-WorldGuide` 包。

## 结论

**Unity 工程不需要任何修改。**

- `Unity/` 下的脚本、导入设置、测试和 34 张贴图都不用动，已经接好的代码保持原样。
- 这次的问题只出现在在线设计稿里：右下角「下一张 / 开始按压 / 去探索」和右上角「跳过」显示成白色方块、没有圆角。
- Unity 用的按钮贴图（`guide_btn_*.png`）一直是单独导出的，没有受影响。

## 改了什么

只替换下面 3 个存档 / 工具文件，覆盖原包里的同名文件即可：

| 文件 | 改动 | 是否影响 Unity |
|---|---|---|
| `Source/design/GuideCard.dc.html` | 按钮样式改成"固定样式 + 末尾一个变量"的写法，修复设计稿里按钮丢失样式变成白块的问题；导出模式下按钮改为直接移除，底部一行固定 54 px 高，保证页码点位置不变 | 否 |
| `Source/design/ExploreCard.dc.html` | 同上 | 否 |
| `Source/tools/export_assets.py` | 每张卡面导出后检查四个角是否透明。Chrome 偶尔会在角上画出一块杂色，检测到就自动重导（最多 5 次） | 否，只在以后重新导出贴图时用到 |

## 需要执行的操作

1. 用本补丁里的 3 个文件覆盖 `Moodium-WorldGuide/Source/` 下的同名文件。
2. 如果这几个文件已经复制到了工程的 `Assets/UI/Moodium/WorldGuide/Source/`（可选的存档位置），也一起覆盖。
3. 不需要重新编译，不需要 Reimport，不需要改 `CandyWorldManager` 或 `MoodiumSpatialInteractionController`。

## 不要做的事

- 不要因为这个补丁去替换 `Assets/Resources/MoodiumWorldGuide/` 里的贴图，原来的贴图是正确的。
- 不要修改 `WorldGuideCards.cs` 里按钮的位置数值，按钮位置没有变化。
