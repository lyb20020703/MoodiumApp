# 第三方依赖与排除清单

下表记录开发工程中的来源和发布处理。表内“未发布”表示本仓库不提供相应文件，不表示它在开发工程中未被使用。具体购买记录和再分发授权仍由项目维护者核对。

| 来源 | 开发工程中的用途 | 本次发布处理 |
|---|---|---|
| Unity、URP、PolySpatial、AR Foundation、XR Hands | 渲染、空间网格、追踪、输入 | 保留包清单，通过官方渠道获取。Assets/Samples 副本未发布 |
| Ivy Studio | 自然世界基础藤蔓生成、原生叶片资源 | 插件及其素材未发布。NatureIvySpatialGrowthController 直接依赖其 API |
| InfiniGRASS | 部分藤蔓材质依赖的植物纹理 | 未发布 |
| Animated Trees Package | 自然世界树木 | 未发布 |
| Epic Toon FX | 粒子效果、部分音效与材质 | 未发布 |
| FImpossible Creations / Bones Stimulator | 开发工程旧入口的骨骼效果组件 | 未发布 |
| Vefects Slime VFX | Moodi 水光材质输入贴图和旧水球材质 | 原包、材质和纹理未发布。保留项目 Shader Graph 生成器源码 |
| OccaSoftware Altos | 静态筛查未发现主项目直接引用 | 整个内嵌包未发布。原包许可证采用 Asset Store EULA |
| AR PL / Make Me a Hanzi、EMS Felix | 心愿预设笔画的原始来源 | 笔画数据和素材未发布，未来加入时须恢复对应许可与来源声明 |
| 字体、音乐、语音、视频、全景图、模型和点云 | 各体验环节内容 | 全部排除，待来源与授权逐项确认 |

## 排除的旧工程和导入内容

- Assets/_Game，包括 P00_SJTU130 章节、旧点云、旧交互模块、水球预制体和展品素材。
- Assets/ARTnGAME 中的完整插件、演示场景、导出模型与 Common Tools。
- Assets/Samples、TutorialInfo 和第三方示例工程内容。
- Chocolate shell、exported_textures、Blender 备份、预览截图及安装用 unitypackage。
- 完整开发过程记录、聊天交接包、构建输出、缓存和本地路径信息。
- unity-mcp、Collab Proxy、IDE 集成、Multiplayer Center、Visual Scripting 与 AI Navigation 的显式包条目。它们未被视为本源码发布版的必要依赖。

## 分发与历史范围

Unity Asset Store EULA 的应用内嵌分发授权不能自动推定为公开插件源码或原始素材的授权。核对依据为 https://unity.com/legal/as-terms 的第 2.2 节，以及每个资源包的具体许可。

本次源码发布采用独立 Git 历史。原 Moodium 仓库的既有文件和历史没有被本次操作清理，仍需要单独处理。排除第三方文件不改变项目已经使用过这些技术和素材的事实。
