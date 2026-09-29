# YouYou Client Framework

从悠游课堂客户端工程整理的独立 C# 框架。命名空间为 `YouYou.Framework`，包名为 `com.youyou.framework`。核心程序集不引用 Unity；Unity 层只依赖引擎与 UGUI。

## 安装与启动

将整个包目录放在稳定位置，在 Package Manager 中用 **Add package from disk...** 选择 `package.json`。也可将整个目录复制为目标项目的 `Packages/com.youyou.framework`。只选择一种安装方式。

在 Package Manager 导入 **Quick Start** 示例，将 `QuickStart` 挂到空场景中的一个空物体，运行即可看到计数器。示例自动创建入口，不需要服务器、预制体、配置表或第三方插件。示例使用 IMGUI，精简工程需启用 `com.unity.modules.imgui`。

正式项目用 **GameObject → YouYou → Framework Root** 创建一个场景根物体。运行时会创建 Canvas 与两个 AudioSource；入口默认跨场景保留，默认按 `Time.deltaTime` 更新，可在 Inspector 改为非缩放时间。UGUI 按钮所需的 EventSystem 和输入模块由项目按旧输入系统或新 Input System 配置。播放声音的场景需有 AudioListener。

```csharp
using UnityEngine;
using YouYou.Framework;

public sealed class AppBoot : MonoBehaviour
{
    private void Start()
    {
        var app = FrameworkEntry.Instance.Context;
        app.Event.CommonEvent.AddEventListener(100, data => Debug.Log(data));
        app.Time.CreateTimeAction().Init(delayTime: 1,
            onUpdate: _ => app.Event.CommonEvent.Dispatch(100, "启动完成")).Run();
    }
}
```

如果项目脚本有自己的 `.asmdef`，增加对 `YouYou.Framework.Core`、`YouYou.Framework.Unity` 的引用。

## 模块

| 能力 | 入口 | 项目需要提供 |
| --- | --- | --- |
| 事件 | `Context.Event.CommonEvent/SocketEvent` | 自己的事件编号与监听 |
| 定时器 | `Context.Time.CreateTimeAction()` | 回调、间隔、次数 |
| 状态机 | `Context.Fsm.Create(owner, states)` | 状态类 |
| 流程 | `Context.Procedure.Start(states)` | 启动、登录等具体流程 |
| 类对象池 | `Context.Pool.Dequeue<T>() / Enqueue(value)` | 可选 `IRecyclable.Clear()` |
| 物体对象池 | `new GameObjectPool(prefab, parent)` | 已加载的 prefab；使用后 Dispose |
| 数据/表注册 | `Context.Data.Set<T>() / Get<T>()` | 业务数据、生成表及加载代码 |
| 本地化 | `Context.Localization.SetLanguage(...)` | 语言和翻译字典 |
| 资源 | `FrameworkEntry.Resource` | 默认为 Resources；可实现 `IAssetProvider` |
| UI | `FrameworkEntry.UI` | 窗体编号、路径、继承 UIFormBase 的 prefab |
| 场景 | `FrameworkEntry.Scene.Load(...)` | Build Settings 中的场景 |
| 音频 | `FrameworkEntry.Audio` | AudioClip；可注入 `IAudioService` |
| HTTP | `FrameworkEntry.Http.Get/PostJson` | URL、请求体和认证头 |
| TCP | `new TcpChannel(codec)` | 服务地址、编解码器、生命周期和主线程 Pump |
| 二进制工具 | `MMO_MemoryStream`、`Crc16`、`BitUtil`、`GZipUtil` | 待处理数据 |

## 常用接入

### 项目流程

```csharp
public sealed class BootProcedure : YouYou.Framework.ProcedureBase
{
    public override void OnEnter()
    {
        // 完成初始化后切换到 states 数组索引 1。
        CurrFsm.ChangeState(1);
    }
}
public sealed class MenuProcedure : YouYou.Framework.ProcedureBase { }

// 在 AppBoot.Start 中调用：
// app.Procedure.Start(new ProcedureBase[] { new BootProcedure(), new MenuProcedure() });
```

状态编号为数组索引，范围 0–127。`Context.Tick` 会更新所有状态机；通过 FrameworkEntry 使用时不要再手动调用 Fsm.OnUpdate。OnEnter 可以切换状态；OnLeave 中不允许再次切换。

### UI 与资源

创建继承 `YouYou.Framework.UIFormBase` 的窗体脚本，挂在 prefab **根节点**。例如将 prefab 保存到 `Assets/Resources/UI/Home.prefab`：

```csharp
var entry = FrameworkEntry.Instance;
entry.UI.Register("Home", "UI/Home"); // Resources 路径不带扩展名
entry.UI.OpenUIForm("Home", userData: "hello");
entry.UI.CloseUIForm("Home");
```

同一个编号只打开一个实例，重复打开会置顶；关闭调用 OnClose 并销毁实例、释放对应加载引用。释放只匹配成功加载的资源。默认 Resources provider 不主动卸载共享资源；它没有引用计数句柄。项目可在合适时机调用 `Resources.UnloadUnusedAssets`。这版 UI 不包含原项目的表驱动遮罩、分组冻结、LuaForm 和过期缓存策略。

`IAssetProvider.LoadAsync` 可通过 `StartCoroutine` 调用。UI 目前使用同步 `Load`；使用纯异步资源系统时，应先预加载，再让项目适配器提供同步缓存访问。适配器 `Release` 需与每次 Load 成对处理；若也实现 IDisposable，入口关闭时会释放它。

### 自定义资源/音频适配器

在入口首次初始化前注入。下面代码假设 `myAssets` 和 `myAudio` 是项目实现的接口实例：

```csharp
var root = new GameObject("Framework");
root.SetActive(false); // 暂缓 Awake
var entry = root.AddComponent<FrameworkEntry>();
entry.Initialize(myAssets, myAudio);
root.SetActive(true);
```

只能有一个激活的 FrameworkEntry；使用以上代码时不要同时放置预先初始化的场景入口。项目持有接口实现即可接入 AssetBundle、Addressables 或音频中间件，不需要修改核心程序集。

### HTTP / TCP

```csharp
// 在业务 MonoBehaviour 中：
StartCoroutine(FrameworkEntry.Instance.Http.Get(apiUrl, response =>
{
    if (response.Success) Debug.Log(response.Text);
    else Debug.LogWarning(response.Error);
}));
```

HTTP 使用 UnityWebRequest，默认 15 秒超时，不附加原项目的设备信息、服务器时间和签名，不隐式重试 POST。认证头可通过 headers 参数传入。入口销毁会中止请求；业务方取消协程也应释放该枚举器，或让入口承载协程。

TCP 使用方式为 `await channel.ConnectAsync(host, port)`、`await channel.SendAsync(payload)`，在业务组件的 Update 调用 `channel.Pump()`，在 OnDestroy 调用 Dispose。消息和断开事件只在 Pump 调用线程派发。每个实例只建立一次连接，重连新建实例；握手、认证、心跳和重试由项目控制。连接状态变化与回调在项目主线程处理；发送和接收内部使用异步 I/O。

默认帧格式是 **4 字节小端 payload 长度 + payload**，默认单包上限 1 MiB、待处理队列上限 1024 条。它与原 MMO 的「2 字节长度 + 标志 + 协议 ID + 分类 + 加密数据」不兼容。接旧服务器要在项目层实现 `IFrameCodec`，并通过消息回调分发到 SocketEvent。TCP 不支持浏览器 WebGL；WebGL 项目需要自己的 WebSocket 适配器。

### 生命周期与语义

- 核心可 `using (var app = new ClientContext())` 独立实例化并手动 `app.Tick(deltaTime)`；它没有静态单例，适合单元检查与工具。Unity 入口负责驱动和释放。
- 除 TcpChannel 的异步收发外，服务均按单线程使用。事件派发使用监听快照，派发中添加/删除监听下次生效；相同监听不重复注册。监听抛异常会向调用方传播，后续监听不再执行。
- 定时器 `loop=0/1` 执行一次，`-1` 无限循环；延迟到期时首次执行，每帧最多一次，不补发掉帧错过的间隔。Pause 不消耗等待时间；Stop 仅取消，自然结束才调用完成回调。静态回调可正常使用。
- 类对象回池时调用 IRecyclable.Clear；默认每种类型最多保留 64 个空闲对象。不会自动清理没有实现接口的业务字段，也不会强制 GC。GameObjectPool 只处理激活和变换；组件业务状态由项目重置。
- UI/资源/音频由入口统一释放。独立创建的 GameObjectPool、TcpChannel 由创建它们的业务模块 Dispose。

## 验证与打包

核心检查需要 .NET 8 SDK，不需要 Unity 或 NuGet 第三方包：

```powershell
& './Tools~/Test-Framework.ps1'
```

额外验证独立 Unity 导入、资源、UI、物体池及 Play Mode：

```powershell
& './Tools~/Test-Framework.ps1' -UnityPath 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3c1/Editor/Unity.exe'
```

脚本创建全新的临时工程，输出位置和日志；不会打开或升级原游戏工程。实际验证记录见 [Validation.md](Documentation~/Validation.md)。Unity 2020.3 是代码基线，不代表所有 Unity 版本、移动端、IL2CPP 或 WebGL 都经过实测。

`Tools~/Export-Framework.ps1` 可生成不包含 bin/obj 的 zip。保持 `.meta` 随包保存，以确保跨项目引用稳定。包内没有原游戏 GUID，能够与原 `YouYou` 命名空间共存。

详见 [迁移清单](Documentation~/Migration.md) 和 [来源说明](SOURCE-NOTICES.md)。安装方式与目录结构参考 Unity 官方文档：[本地包安装](https://docs.unity3d.com/2020.3/Documentation/Manual/upm-ui-local.html)、[包目录结构](https://docs.unity3d.com/2020.3/Documentation/Manual/cus-layout.html)。
