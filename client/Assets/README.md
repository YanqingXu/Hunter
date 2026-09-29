# BigWorld 资源分类

```text
Assets/
├── _Game/                         正式游戏内容与项目级构建工具
│   ├── Scenes/                    正式游戏场景
│   ├── Scripts/                   玩家、战斗、任务等业务脚本
│   ├── Editor/Build/              只在 Unity 编辑器运行的打包工具
│   ├── Data/Maps/                 共用格子类型、属性、面板、结构模板
│   ├── Data/Skills/               正式技能配置
│   ├── Prefabs/                   正式角色、敌人、物件等预制体
│   ├── Art/                       自有与加工后的美术；MapTiles 为地图图块
│   ├── Animations/                正式动画
│   ├── Audio/                     音乐、音效
│   └── UI/                        正式界面素材
├── _Framework/                    可复用模块，按模块维护 Runtime / Editor
│   ├── Map2D/                     分区地图与地图编辑器
│   ├── Pooling/                   共享对象池
│   └── YouYou2D/                  悠游框架与项目模块的 2D 接入层
├── _Examples/                     示例内容，按示例归集依赖
│   ├── Framework2D/               Scenes、Scripts、Prefabs、Data
│   ├── Map2D/                     地图编辑示例场景与地图
│   └── Pooling/Scripts/           对象池调用示例
├── SkillEditorKit/               技能编辑器、运行时与供应方示例
├── Plugins/Sirenix/              Odin 依赖
└── ThirdParty/                    第三方原始美术包
```

当前正式入口是 `_Game/Scenes/BorderTrial.unity`，已选入 Build Settings。框架整合示例保留为 `_Examples/Framework2D/Scenes/Framework2DDemo.unity`。`_Examples/Map2D/Scenes/MapEditorExample.unity` 是原 SampleScene，保留地图编辑用途。

## 操作入口

- **正式横版关卡**：Tools → BigWorld → 正式关卡 → 打开边境试炼。
- **框架接入示例**：Tools → BigWorld → 2D 框架 → 打开运行示例。
- **地图编辑**：Tools → BigWorld → 2D 地图编辑器。
- **技能编辑**：技能编辑器（独立版）→ 打开工作台。
- **打包**：Tools → BigWorld → 打包 → Windows 64 位（发布 / 开发调试）。
- **输出目录**：工程根目录的 `Builds`，不属于 Assets。每次构建使用独立时间戳目录并生成完整 ZIP。

## 放置与打包约定

`_Game` 放实际业务，`_Framework` 放可复用能力，`_Examples` 放试验与教学场景。Editor 代码仍处于 Unity 有特殊含义的 `Editor` 文件夹；运行时代码不得引用 UnityEditor。

目录名本身不决定打包范围。构建工具读取 Build Settings 中启用的场景；Unity 收集这些场景的资源依赖。被试玩引用的技能示例精灵、动画和子弹也会随依赖进入试玩包。`Resources`、`StreamingAssets` 与脚本程序集另有 Unity 的包含规则，不要仅靠放进 `_Examples` 判断它们是否会被打包。

`SkillEditorKit` 使用固定路径读取编辑器布局；Odin 使用插件目录约定，因此保留它们的安装位置。悠游包位于工程 `Packages/com.youyou.framework`。

移动任何 Unity 资源时必须保留配套 `.meta`。本次分类保留原 GUID，场景、预制体、地图与技能资产继续按原引用关联。

更多内容：[2D 接入说明](_Framework/YouYou2D/README.md)、[地图说明](_Framework/Map2D/README.md)、[对象池说明](_Framework/Pooling/README.md)。工程级目录与构建步骤见根目录 `Docs/ProjectStructure.md`、`Docs/Build.md`。
