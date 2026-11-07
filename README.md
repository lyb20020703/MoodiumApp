# Moodium

Moodium 是面向 Apple Vision Pro 的空间交互项目，围绕感官袋按压、情绪记录、现实房间探索、点云世界和心愿风铃组织体验。

**这个仓库是经过资源分发范围筛查的源码发布版。当前不能直接打开即运行或打包。** 商业插件、Unity 示例副本、模型、贴图、字体、音频、视频、场景与预制体未随仓库发布。部分代码直接依赖 Ivy Studio 和 PolySpatial 示例类型，补齐依赖前 Unity 可能报告编译错误。

完整开发工程的历史来源为 [Moodium](https://github.com/lyb20020703/Moodium)。本仓库采用新的 Git 历史，发布清单记录在 [RELEASE_CONTENTS.json](RELEASE_CONTENTS.json)。

## 内容

- `Assets/Scripts` 包含按压交互、三种模式、开机与选关流程、情绪记录、Moodi、点云与时空之门、心愿风铃和空间藤蔓的集成代码。
- `Assets/Editor` 包含配套设置工具、资源生成器和编辑器测试。部分生成器和测试需要未发布的素材。
- `Assets/Tests` 包含项目测试代码。
- `Packages/manifest.json` 记录 Unity 包依赖。Package Manager 获取的包适用各自许可。
- `ProjectSettings/ProjectVersion.txt` 记录 Unity 编辑器版本。

本次发布基于 `86f5a550`，并包含后续已在本地实现的“进度满格后时空之门自动展开”改动。

## 运行环境与复现边界

开发版本使用 Unity 6000.0.30f1、URP 17.0.3、PolySpatial 2.4.3 和 visionOS。Unity 包版本及设备开发权限需要自行满足。

如果要还原完整体验，需要取得相应插件和内容的有效授权，再恢复其原路径和 GUID。当前发布版缺少主场景、Prefab、Resources 内容、XR 项目设置和部分第三方源代码，因此这里提供的是研究和集成源码，而不是可独立复现完整体验的工程包。

1. 使用指定版本 Unity 新建 URP 工程，并通过 Package Manager 安装清单中的依赖。
2. 对照 [第三方依赖说明](THIRD_PARTY.md) 安装 Ivy Studio、必要的 PolySpatial/XR Hands 示例。
3. 准备具有再分发或使用授权的素材，补齐场景、预制体、Resources 配置和 XR 设置。资源路径可从设置工具和测试中查阅。
4. 在依赖和内容完整的工程内运行测试，再进行设备验证。发布源码副本未执行 Unity 编译，也未进行真机验收。

## 原创范围与第三方来源

Moodium 将 Unity/PolySpatial 的空间能力、Ivy Studio 的藤蔓生成能力及第三方视觉素材，与项目的按压计分、体验流程、空间交互和心愿记录逻辑集成。

藤蔓基础生成器来自 Ivy Studio。树木、部分视觉音效和材质输入在开发工程中使用第三方资源。使用这些资源时，需要遵守其各自许可。不要将这些基础能力或素材整体描述为团队从零实现。

本仓库中的部分代码和资源生成工具由 AI 辅助开发，团队对产品设计、集成与验收负责。源码保留现有注释和技术依赖名称。

## 许可证

本仓库发布的 Moodium 项目代码采用 [MIT License](LICENSE)。第三方包、素材、字体、数据及其衍生内容遵循各自许可，不因本仓库的 MIT 许可取得额外授权。商业资源未随本仓库分发。

素材再分发授权尚未逐一确认，因此本次将全部媒体内容排除。未来可以在完成来源审查后逐项加入允许公开的素材。
