# 从原客户端迁移

## 目录与依赖边界

```text
新项目业务：启动/登录/角色/战斗/配置表/协议/热更新策略
    ↓ 实现接口、注册数据与流程
YouYou.Framework.Unity：入口、UI、资源接口、场景、AudioSource、HTTP
    ↓
YouYou.Framework.Core：事件、时间、FSM、流程、类池、数据、本地化、TCP、二进制工具
```

Core 的 `.asmdef` 设置 `noEngineReferences: true`，保证核心无法隐式引用 Unity；Unity 程序集只引用 Core、UGUI 与引擎。Editor 工具单独编译。没有 Assembly-CSharp、XLua、Odin、FMOD、DOTween、LitJson、PathologicalGames、FlatBuffers 或业务生成代码引用。

独立包位于原工程 `Reusable/YouYouFramework` 下，不在原工程 Assets 或自动导入的 Packages 内，因此本次整理不会改变原工程的运行入口和资源引用。要迁移原游戏，需要按业务模块逐步改调用；不能直接删除原框架目录。

## 原模块去向

| 原实现 | 独立包处理 | 接入动作 |
| --- | --- | --- |
| GameEntry | 改为 FrameworkEntry + 可独立实例化 ClientContext | 项目创建入口；业务只持有所需服务 |
| CommonEvent / SocketEvent / EventManager | 抽取，去掉 XLua 属性并修正监听变更语义 | 保留 Add/RemoveEventListener、Dispatch 调用风格 |
| TimeManager / TimeAction | 注入所属管理器、显式 deltaTime，修正静态回调和暂停/停止行为 | 保留 Init().Run() 风格；Stop 不再触发完成 |
| Fsm 系列 | 抽取，保留 Owner、索引状态、OnEnter/OnLeave 等概念 | 状态实例不可共享；不要再额外手动 Tick |
| ProcedureManager | 移除固定 9 个业务流程和 ProcedureState 枚举 | 在项目注册 ProcedureBase[]，由项目定义编号 |
| ClassObjectPool | Type 键、引用比较、容量限制、IRecyclable | 回池时自己定义业务数据重置 |
| GameObjectPool / PathologicalGames | 提供无插件的单 prefab 池 | 项目负责 prefab 生命周期与组件状态重置 |
| DataManager / DataTableManager | 改为类型注册表 DataRegistry | 游戏表、FlatBuffers、生成器保留在业务项目 |
| LocalizationManager | 从固定 DTSys_LocalizationList 改为字典输入 | 项目解析语言表并调用 SetLanguage |
| AddressableManager / ResourceManager | 提取为资源加载接口，提供 Resources 默认实现 | 原实现并非 Unity Addressables；旧 AB 系统需项目适配 |
| YouYouUIManager / UIFormBase | 独立窗体注册、打开、关闭与加载释放 | 将 UI 表配置转换为注册；层级/遮罩/LuaForm 策略留项目实现 |
| YouYouSceneManager | 场景名 + 异步加载 | 场景加入 Build Settings；去掉原场景表和游戏流程事件 |
| AudioManager / FMOD | IAudioService + AudioSource 默认实现 | 音频 ID 到音效资源的映射放业务；FMOD 可另做适配 |
| HttpManager / HttpRoutine | 通用 GET/JSON POST、超时、释放 | 原 form 字段 json、签名、账号地址、自动重试不沿用 |
| SocketManager / SocketTcpRoutine | 异步 TCP + 独立编解码接口 | 编解码、协议 ID、心跳、重连按新服务器实现 |
| DownloadManager / 更新流程 | 原版本校验、断点续传、AB 清单未抽入通用版 | 属于项目发布与热更新策略；可围绕资源接口另建模块 |
| LuaManager / LuaAdapter / LuaForm / 生成绑定 | 没有内置 XLua 依赖与生成代码 | 项目安装 XLua 后针对新类型重新绑定，并决定 Lua 生命周期 |
| InputManager / 摇杆 / 摄像机控制 | 保留在原游戏 | 新项目选择 Input System 或旧输入模块 |
| TaskManager / TaskGroup | 旧池化回调任务组未移植 | 使用项目协程或 System.Threading.Tasks 组合，取消策略放业务 |
| Logger / Reporter | 原调试面板未打包 | Unity 项目使用 Debug 或接自己的日志设施 |
| 二进制/压缩/位操作工具 | 直接抽取，保留原作者说明 | MMO_MemoryStream 明确小端并拒绝不足长度读取 |
| 美术、特效、Playable、导航、战斗、生成协议 | 保留在原游戏 | 按具体游戏项目管理 |

## 迁移步骤

1. 先在空工程安装包，导入 Quick Start 验证启动。
2. 把自己的业务代码放在 Assets/Game 或另一个业务包，引用框架程序集。
3. 创建项目启动流程，加载配置后注册 UI、翻译表、业务数据。
4. 将业务直接访问静态 GameEntry 的调用，替换为构造参数、持有 Context，或在启动脚本获取 FrameworkEntry.Instance。
5. 优先使用 Resources 和 AudioSource 完成闭环；确有需求再接资产系统、Lua、FMOD 等。
6. 用目标项目的服务器协议实现 IFrameCodec，明确大小端、长度、压缩、加密和错误处理。默认 TCP codec 不能直接连接旧服务器。
7. 逐模块验证后再移除旧依赖；本包不自动改场景、预制体 GUID 或生成绑定。

## 对第三方插件的处理

插件仍保留在原项目。独立包没有复制插件二进制、商业插件编辑器或旧 XLua 生成包装。扩展接口已经提供，但本次不包含这些中间件的完成版适配器。原项目中的凭据、服务器地址、下载域名、打包路径和加密密钥也没有带入独立包。
