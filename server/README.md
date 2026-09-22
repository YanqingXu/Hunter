# Hunter Server

技术栈为 **C++23 / Standalone Asio 1.36.0 / Luax / Protobuf / CMake**。
构建显式定义 `ASIO_STANDALONE`，使用独立 Asio 头文件，不依赖 Boost。

当前交付 Windows 服务端基础闭环：本机 TCP 握手、Luax 权威探针状态、固定 Tick、暂停恢复、
有界背压和资源收尾。它验证基础框架，不包含战斗、SQLite、热更新或 Android Service。
设计见 [规划](plan.md)，契约见 [intents](intents/README.md)，实际结果见 [验证记录](VERIFICATION.md)。

## 构建与测试

需要 CMake 3.28+、支持 C++23 `std::expected/std::jthread` 的 x64 MSVC、Windows SDK、
Python 3.10+ 和 Git。本机验证使用 VS 2026；CI 使用 Windows 2025 镜像上的 MSVC。
从仓库根目录进入 `server`，执行：

```powershell
Set-Location server
cmake --preset win-dev -A x64 -DFETCHCONTENT_SOURCE_DIR_LUAX=G:/github/luax
cmake --build --preset win-dev --parallel 8
ctest --preset win-dev
```

本地 Luax 覆盖必须是固定提交的干净检出；其他机器可换成自己的路径，或省略覆盖参数让
FetchContent 按固定 Git 提交获取。访问私有依赖需要 Git 已有读取权限。CI 的 Luax 若为私有仓库，
需要仓库 secret `LUAX_READ_TOKEN`；本轮没有配置远程 secret，也没有运行远程 CI。

源码锁定在 `cmake/Deps.cmake`：Luax `d8a8160`、Standalone Asio 1.36.0、Protobuf 33.0、
Abseil 20250512.1、nlohmann/json 3.12.0，全部固定完整提交；公共归档另验 SHA-256。
可用 `FETCHCONTENT_SOURCE_DIR_<大写依赖名>` 指向干净的同版本源码。
依赖缓存位于构建目录，首次构建需要准备依赖；服务运行不访问远程地址。

生成产物为 `build/win-dev/generated/game.lua`、源映射、C++ Protobuf 与
`generated/csharp/Hunter.cs`。`generated/SchemaSpec.h` 从 `lua/contract.json` 生成字段及边界，
Luax 输出提交与 Protobuf 转换共用同一校验器。C# 产物尚未进入 Unity 联调。`hunter_tools` 可单独构建
本机 `protoc`、`luaxc`、`luax-bundle`。头文件与实现按模块并置，公共包含根为 `src`。

## 桌面控制与协议

```powershell
./build/win-dev/Release/hunter_server_desktop.exe `
    --source build/win-dev/generated/game.lua
```

stdin 每行一个 JSON 对象；`req_id` 为非空字符串，最多 128 字节。标准输出只有控制 JSON，
诊断写 stderr，调用方须同时读取两个通道。输入示例：

```json
{"req_id":"start-1","cmd":"Start"}
{"req_id":"pause-1","cmd":"Pause"}
{"req_id":"resume-1","cmd":"Resume"}
{"req_id":"stop-1","cmd":"Stop"}
```

Start 成功返回 `type=Ready`，包含 `port/instance/token/protocol_version/content_version`。
后续控制返回 `Rsp` 或 `Error`，关联原始 `req_id`。错误与状态的精确定义见
[宿主契约](intents/architecture/host.intent.md)。一个进程承载一次服务实例，Stop 后退出；
重复 Start 在 Ready 状态返回同一个实例。标准输入 EOF、输出管道断开也触发收尾。
正常停止返回退出码 0；脚本 shutdown 返回 false、抛错或原生关闭失败会保留有界诊断，
进入 Faulted 并返回 1，同时继续释放资源。管道取消超时返回 2。

客户端连接 `127.0.0.1:<port>`；每帧为四字节大端正文长度加 Protobuf `hunter.wire.Envelope`。
第一次消息必须为 Hello，逐项回传 Ready 的版本、实例和令牌，5 秒内完成握手。
正式定义位于根目录 `protobuf/hunter.proto`，不能另建私有协议来源。

FrameInput 的 `seq` 是递增非零 uint64，`value` 在 -1000～1000 内；框架脚本将 value 累加，
只在 Tick 边界返回 InputAck，20 Hz 发送 Snapshot。重复输入不会再次累计。
JSON 桥接中序号与 Tick 使用十进制字符串。累计值在 ±1,000,000,000 内，越界中止会话。
这套计数消息只用于验证框架，未来玩法协议另行冻结。

当前只接纳首个 TCP 客户端；额外连接关闭。首次客户端无效握手、超限输入、断连或脚本错误
都会终止当前会话并关闭监听；需要新进程和新实例重新开始。暂停保留有界离散输入，恢复按序执行，
不补算暂停时间；未来持续移动输入的清除策略随玩法协议实现。

默认 60 Hz、最多补算 4 Tick；单 Tick 最多处理 64 条输入，一次定时回调共享 4 ms 输入工作预算，
其余输入保留 FIFO。脚本入口不可中途抢占，因此该预算是让出事件循环的软预算；回调最多额外
完成一个输入入口和一个 Tick 入口，各入口仍受独立执行预算约束。
消息和 JSON 上限 64 KiB，主要队列各 256 条／1 MiB，默认限制集中在 `Cfg`。
测试参数只接受完整正十进制：`--handshake-ms/--stop-ms` 为 1～60000 毫秒，
`--queue-count` 为 1～65536 条，`--send-bytes` 为 1～16777216 字节；负号、零、尾随内容和
越界值均拒绝。异步探针适配器独立测试真实 continuation、定时器和后台完成；游戏入口保持同步。

## 生产 Bundle 模式

开发 CTest 的 `hunter_bundle_contract` 会生成公开测试向量签名的测试制品；只用于验证。
使用它们验证生产 Runtime：

```powershell
$devBuild = (Resolve-Path build/win-dev).Path
cmake --preset win-bundle -A x64 -DFETCHCONTENT_SOURCE_DIR_LUAX=G:/github/luax `
    "-DHUNTER_HOST_PROTOC=$devBuild/_deps/protobuf-build/Release/protoc.exe" `
    "-DHUNTER_TEST_BUNDLE=$devBuild/bundle-test/game.luxb" `
    "-DHUNTER_TEST_POLICY=$devBuild/bundle-test/policy.json"
cmake --build --preset win-bundle --parallel 8
ctest --preset win-bundle
./build/win-bundle/Release/hunter_server_desktop.exe `
    --bundle "$devBuild/bundle-test/game.luxb" --policy "$devBuild/bundle-test/policy.json"
```

生产模式编译链接 `Luax::Runtime`，拒绝源码加载；不能用运行参数切换到开发 Runtime。
CTest 检查真实链接参数不含 Compiler、AST、DevelopmentRuntime，并验证错误公钥、身份和篡改拒绝。
生产运行时仅接受可信交付的公钥 policy 和签名 Bundle。正式离线签名步骤见
[工具说明](tools/README.md)；私钥不进入可执行文件、运行资源或仓库。

`cmake --install build/win-bundle --config Release --prefix build/stage` 只安装桌面可执行文件。
正式发行需要另外交付可信制品、公钥策略及系统运行库；本轮不宣称干净机器发行包已验收。

## Android 探针与后续边界

本轮没有安装 NDK。preset 接受客户端后续锁定的 `ANDROID_NDK_HOME`，目标为
`arm64-v8a`、API 26；原生探针使用独立可执行文件和静态 C++ 运行库，不定义最终 AAR 的运行库策略。

```powershell
cmake --preset android-probe -DFETCHCONTENT_SOURCE_DIR_LUAX=G:/github/luax `
    "-DHUNTER_HOST_PROTOC=$devBuild/_deps/protobuf-build/Release/protoc.exe"
cmake --build --preset android-probe --target hunter_android_probe
py -3 tools/android_probe.py --exe build/android-probe/hunter_android_probe `
    --bundle "$devBuild/bundle-test/game.luxb" --policy "$devBuild/bundle-test/policy.json" `
    --evidence build/android-probe/device-evidence.json
```

缺少 NDK、adb 或设备时必须报告未验证，不能跳过后算成功。探针核对真实签名加载、Host 调用和
Asio Tick，并记录设备与制品摘要；它不替代 AAR、Binder、Unity、APK、16 KB 页面真机和生命周期验收。
P0 整体仍待 Android 证据，P1 及以后按规划推进。
