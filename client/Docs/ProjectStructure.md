# 项目目录与职责

| 目录 | 用途 | 交接工程源码时 |
| --- | --- | --- |
| Assets | 游戏资源、框架、示例、编辑器、第三方插件 | 保留全部资源与 .meta |
| Packages | Unity 依赖清单、锁定文件与内嵌 YouYou 包 | 保留 |
| ProjectSettings | Unity 项目设置与构建场景列表 | 保留 |
| Docs | 项目级使用、结构和构建说明 | 保留 |
| Tools | Windows 构建与源码归档脚本 | 保留 |
| Tests | 模块测试源码，通常不进入 Assets | 保留，方便复验 |
| Builds | 可运行程序、ZIP、源码 ZIP 和构建日志 | 按需要单独发送，不纳入源码 ZIP |
| TestResults | 本机验证日志、截图和临时验证资产 | 通常无需交接 |
| Library / Temp / obj | Unity / 编译缓存 | 无需交接，会重新生成 |
| Logs / UserSettings | 本机日志和个人编辑器设置 | 无需交接 |
| .codex / .idea / .vscode | 工具工作记录和本机 IDE 配置 | 默认不纳入源码 ZIP |

Assets 的细分结构见 [资源目录](../Assets/README.md)。

## 本次分类

- 原 `_Game/Scripts/Map2D`、`_Game/Scripts/Framework/Pooling`、`_Game/Scripts/Framework/YouYou2D` 收拢为 `_Framework` 下的三个模块。
- Framework2D 示例原本散落的场景、脚本、预制体、资源表、地图与技能配置，统一到 `_Examples/Framework2D`。
- 原 SampleScene 改放到 `_Examples/Map2D/Scenes/MapEditorExample.unity`；示例地图放到同示例的 Data 文件夹。游戏共用类型、图块、面板与结构模板继续留在 `_Game`。
- 对象池示例脚本放到 `_Examples/Pooling/Scripts`。
- 打包工具放到 `_Game/Editor/Build`，Windows 产物统一输出到根目录 Builds。
- SkillEditorKit、Odin、第三方美术及 UPM 包保留各自安装结构。

建立正式关卡后，在 `_Game/Scenes` 存放场景，在 `_Game/Data` 存放正式配置。共用配置优先直接引用；不要为了整理复制同一资源并生成另一套 GUID。

备份与逐文件迁移清单位于 `TestResults/BuildOrganization`；整理前备份位于 `.codex/backups/before-build-organization-*.zip`。
