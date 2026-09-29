# 项目目录与职责

| 目录 | 用途 | 交接工程源码时 |
| --- | --- | --- |
| Assets | 游戏资源、框架、示例、编辑器、第三方插件 | 保留全部资源与 .meta |
| Packages | Unity 依赖清单与锁定文件；完整 YouYou 框架位于 Assets | 保留 |
| ProjectSettings | Unity 项目设置与构建场景列表 | 保留 |
| Docs | 项目级使用、结构和构建说明 | 保留 |
| Tools | Windows 构建与源码归档脚本 | 保留 |
| Tests | 模块测试源码，通常不进入 Assets | 保留，方便复验 |
| Builds | 可运行程序、ZIP、源码 ZIP 和构建日志 | 按需要单独发送，不纳入源码 ZIP |
| TestResults | 本机验证日志、截图和临时验证资产 | 通常无需交接 |
| Library / Temp / obj | Unity / 编译缓存 | 无需交接，会重新生成 |
| HybridCLRData | 项目本地 HybridCLR 原生运行时、临时 DLL 和裁剪输出 | 无需交接，初始化后重新生成 |
| Builds/HotUpdate/Bases | 已发布主包的冻结 AOT 基线 | 必须另存，后续补丁依赖它 |
| Logs / UserSettings | 本机日志和个人编辑器设置 | 无需交接 |
| .codex / .idea / .vscode | 工具工作记录和本机 IDE 配置 | 默认不纳入源码 ZIP |

Assets 的细分结构见 [资源目录](../Assets/README.md)。

## Assets 目录

日常游戏开发进入 `_Project`。完整课程框架及依赖保留原根目录，以兼容原代码、生成绑定、配置资产和固定资源路径。

| 目录 | 内容 |
| --- | --- |
| `Assets/_Project/Game` | 正式关卡、业务代码、配置、预制体、美术，以及 Editor 下的关卡生成和打包工具 |
| `Assets/_Project/Modules` | Map2D、Pooling、YouYou2D、SkillEditorKit，以及 HybridCLR 的 HotUpdate 模块 |
| `Assets/_Project/Examples` | Framework2D 整合示例，以及使用项目配置的 Map2D、Pooling 接入示例 |
| `Assets/YouYouFramework` | 原 GameEntry、18 管理器、Core、组件、Editor、YouYouAssets 配置及配置类型 |
| `Assets/YouYouScript` | 原课程业务与协议类型、生成 FlatBuffers 表及扩展、UI、角色、相机等原管理器依赖 |
| `Assets/Download` | 原压缩表、FMOD Bank bytes、UI、Reporter；作为原格式内容构建输入 |
| `Assets/StreamingAssets` | 生成的 VersionFile.bytes、AssetInfo.bytes、原运行资源包，以及 hotupdate DLL、AOT 元数据、场景包和清单 |
| `Assets/Plugins` | 现有 Odin，以及原 FMOD 本机库、Google.Protobuf、LitJson、zlib、FingerGestures、PathologicalGames 等 |
| `Assets/ThirdPlugins` | 原 Demigiant/DOTween、SuperScrollView、MTE、TextMesh Pro 资源等依赖，保留课程路径 |
| `Assets/ThirdParty` | 第三方原始美术包 |

模块自带的独立示例随模块维护，例如 `Modules/SkillEditorKit/Samples`。项目接入示例的场景、脚本、预制体与配置集中到对应的 `Examples/<示例名>`；共用地图类型、图块、面板与结构模板位于 `Game`。

Unity 包依赖仍由 `Packages` 管理，包含嵌入的 HybridCLR 包；Windows 构建产物输出到根目录 `Builds`。Player 先进入 AOT 的 HybridBoot，再加载原框架及游戏热更 DLL，最终使用 `YouYou.GameEntry` 启动原框架。详细程序集边界和发布规则见 [HybridCLR 热更新](HybridCLR.md)。

原框架目录不随 `_Project` 的分类调整而移动。特别是 `YouYouFramework/YouYouAssets`、`Download` 和插件目录，仍有原代码与工具按固定路径访问。移动 Unity 资源必须连同 `.meta` 并核对所有路径引用，不能只依赖 GUID 自动修复。

构建范围由启用场景、资源引用、Resources、StreamingAssets 和脚本程序集共同决定。`Examples`、`ThirdPlugins` 等目录名称本身不排除构建；正式入口使用 `_Project/Game/Scenes/BorderTrial.unity`，原课程联网启动场景和验证场景不加入正式入口。内容生成与平台规则见 [构建步骤](Build.md)。

原框架验证源码保存在 `Tests/YouYouFull`，2D 接入与玩法探针分别保存在 `Tests/YouYou2D` 和 `Tests/Gameplay`。runner 只在执行期间临时复制进 Assets；结束后归档该次验证资源与 `.meta` 到 TestResults。原框架 runner 还恢复执行前的 Build Settings 与所调整的宏状态。临时验证目录仍存在时禁止源码导出，避免将探针或夹具误带进交付工程。

原框架迁移范围、接口审计、验证入口及 2D 启动流程见 [完整迁移说明](YouYouFramework-Migration.md)。地图实体仍使用 `BigWorld.Pooling` 共享池；原版 `GameEntry.Pool` 保留并承担原 UI/资源等池管理，两者职责不同。

建立正式关卡后，在 `_Project/Game/Scenes` 存放场景，在 `_Project/Game/Data` 存放正式配置。共用配置优先直接引用；不要为了整理复制同一资源并生成另一套 GUID。

本次迁移保留原资源和目录的 `.meta`，同步更新编辑器布局加载、示例生成、资源导出、构建设置、测试及文档路径。迁移映射和校验记录位于 `TestResults/ProjectOrganization-20260929`。较早的分类备份与记录仍位于 `TestResults/BuildOrganization` 和 `.codex/backups`。
