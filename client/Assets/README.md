# BigWorld 资源分类

日常开发从 `_Project` 进入：`Game` 放正式游戏，`Modules` 放可复用模块，`Examples` 放项目接入示例。

```text
Assets/
├── _Project/                      项目维护的游戏内容、模块和示例
│   ├── Game/                      正式游戏内容与项目级工具
│   │   ├── Scenes/                正式关卡；入口为 BorderTrial.unity
│   │   ├── Scripts/               玩家、敌人、关卡流程与界面逻辑
│   │   ├── Editor/                关卡生成与打包工具
│   │   ├── Data/                  地图、技能、资源表与物理配置
│   │   ├── Prefabs/               正式角色、敌人、子弹与界面
│   │   ├── Art/                   自有与加工后的美术
│   │   ├── Animations/            正式动画
│   │   ├── Audio/                 音乐、音效
│   │   └── UI/                    界面素材与字体
│   ├── Modules/                   按功能维护的可复用模块
│   │   ├── Map2D/                 地图运行时与编辑器
│   │   ├── Pooling/               共享对象池
│   │   ├── YouYou2D/              原版 GameEntry 的 2D 接入与内容构建
│   │   └── SkillEditorKit/        技能运行时、编辑器与模块自带 Samples
│   └── Examples/                  使用本项目配置的接入示例
│       ├── Framework2D/           框架、地图、技能整合示例
│       ├── Map2D/                 地图编辑示例场景与数据
│       └── Pooling/               对象池调用示例
├── YouYouFramework/               完整原框架：GameEntry、18 管理器、工具与原配置
├── YouYouScript/                  原课程业务类型、协议、生成表、UI 与流程依赖
├── Download/                      原 DataTable、Audio、UI、Reporter 内容
├── StreamingAssets/               生成的原格式清单与运行资源包
├── Plugins/                       FMOD 本机库、协议/压缩库及现有 Odin 等
├── ThirdPlugins/                  原 DOTween、SuperScrollView、TMP 资源等依赖
└── ThirdParty/                    第三方原始美术包
```

## 操作入口

- **正式横版关卡**：Tools → BigWorld → 正式关卡 → 打开边境试炼。场景为 `_Project/Game/Scenes/BorderTrial.unity`，已选入 Build Settings。
- **框架接入示例**：Tools → BigWorld → 2D 框架 → 打开运行示例。场景为 `_Project/Examples/Framework2D/Scenes/Framework2DDemo.unity`。
- **地图编辑**：Tools → BigWorld → 2D 地图编辑器。示例场景为 `_Project/Examples/Map2D/Scenes/MapEditorExample.unity`。
- **技能编辑**：技能编辑器（独立版）→ 打开工作台。模块自带示例位于 `_Project/Modules/SkillEditorKit/Samples`。
- **原框架内容**：Tools → BigWorld → 2D 框架 → 生成原框架运行内容。按当前目标平台生成原格式 AssetBundle、VersionFile 和 AssetInfo；第一次运行或修改打包内容后执行。正式 Windows 打包会自动执行此步骤。
- **打包**：Tools → BigWorld → 打包 → Windows 64 位（发布 / 开发调试）。输出到工程根目录 `Builds`，每次构建使用独立时间戳目录并生成完整 ZIP。

## 放置约定

- 游戏业务代码和正式资产放在 `_Project/Game`，按现有资源类型分类。
- 可复用能力放在 `_Project/Modules/<模块名>`；编辑器代码保留在 `Editor` 文件夹，运行时代码不得引用 UnityEditor。
- 模块独立分发所需的自带示例保留在模块的 `Samples` 中；使用项目配置的接入、整合示例集中到 `_Project/Examples`，依赖按示例归集。
- `ThirdParty` 保留导入的原始素材；正式使用的加工版本放到 `Game/Art`。有安装位置约定的插件保留在 `Plugins`。
- 完整悠游源码位于 `Assets/YouYouFramework`，入口是 `YouYou.GameEntry`。原来的 `Packages/com.youyou.framework` 抽取版不再承担运行服务；`YouYou2D` 只负责本项目接入，不是原框架源码目录。
- `YouYouFramework/YouYouAssets`、`YouYouScript`、`Download`、`Plugins` 与 `ThirdPlugins` 保留原布局。原代码、生成工具和资源表含固定路径，不要为了分类直接搬进 `_Project`。
- `Download` 是内容构建输入；`StreamingAssets` 是随 Player 分发的原格式清单和资源包。更新内容后重新生成，不手工拼接或改名。

目录名本身不决定打包范围。构建工具读取 Build Settings 中启用的场景，Unity 收集场景引用的资源；`Resources`、`StreamingAssets` 和脚本程序集有各自的包含规则。

原框架及 2D/玩法验证源码保存在工程根目录 `Tests`。执行脚本时才临时复制进 Assets，结束后归档到 `TestResults`；`Assets/YouYouFullValidation`、`__Framework2DChecks`、`__GameplayChecks` 都不是长期维护的游戏目录。验证未清理完成时先不要打包或导出源码。

技能编辑器的界面加载、示例创建与导出路径已同步到 `Assets/_Project/Modules/SkillEditorKit`。移动任何 Unity 资源时必须保留配套 `.meta`；本次迁移保留原 GUID，并同步更新代码、Build Settings、测试和说明中的路径。

更多内容：[原版框架迁移说明](../Docs/YouYouFramework-Migration.md)、[2D 接入说明](_Project/Modules/YouYou2D/README.md)、[地图说明](_Project/Modules/Map2D/README.md)、[对象池说明](_Project/Modules/Pooling/README.md)、[技能编辑器说明](_Project/Modules/SkillEditorKit/README_先读我.md)。工程级说明见 [项目目录](../Docs/ProjectStructure.md) 和 [构建步骤](../Docs/Build.md)。
