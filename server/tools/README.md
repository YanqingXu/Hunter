# 脚本和 Bundle 工具

工具只使用 Python 标准库及固定 Luax 构建产物。下列命令在仓库根目录运行。
Windows 示例使用 `py -3`；CMake 使用其找到的 Python 解释器。源码及生产 Bundle 由同一清单组装。

## 单模块组装

```powershell
py -3 server/tools/assemble.py `
    --manifest server/lua/modules.json `
    --output server/build/win-dev/generated/game.lua `
    --map server/build/win-dev/generated/game.map.json
```

清单使用 `version:1`、入口模块名 `entry` 和 `modules` 数组。每项包含 `name/file/deps`；
文件返回 `function(deps)` 工厂，依赖用 `deps["framework.state"]` 访问。
入口必须传递依赖全部模块。清单顺序不会改变生成源码；相同就绪节点按模块名排序。
清单不允许重名、依赖环、缺失依赖、目录穿越或链接逃逸。模块名和玩法 Lua 路径必须小写；
校验包含未列入清单的脚本，并拒绝实际路径或源码依赖引用的大小写不一致。独立导表不受影响。

`game.map.json` 记录每段源码的原文件、生成起止行和摘要。原始行号为
`生成报错行 - generated_start + source_start`；包装层报错不伪造原文件位置。
框架入口、Host 签名及状态和效果 schema 的版本化来源为 `server/lua/contract.json`。
未列入清单的玩法占位文件不会加载。

构建同时运行 `gen_schema.py --contract server/lua/contract.json --output <构建目录>/SchemaSpec.h`，
从同一契约生成原生 Protobuf 校验、冷路径 JSON 描述与宿主契约摘要；高频路径不解析 JSON。
桥接和传输共用生成常量；不支持的 schema
变化会令生成失败，避免静默沿用旧范围。单头文件也在完整落盘后替换，写入失败保留旧文件。

## 离线生产制品

先构建开发 preset 的 `hunter_tools`；`luaxc.exe` 和 `luax-bundle.exe` 实际位置可从构建输出确认。
示例变量指向所用构建树内的工具、已验证干净的固定 Luax checkout 以及外部签名材料。
工具要求公钥和 Ed25519 seed 均为 **32 字节原始二进制文件**，不是十六进制文本。
签名密钥由发行流程提供；Hunter 工具不默认生成或分发生产密钥。

```powershell
$luaxRoot = 'G:/github/luax'
$luaxc = 'server/build/win-dev/_deps/luax-build/Release/luaxc.exe'
$bundleTool = 'server/build/win-dev/_deps/luax-build/Release/luax-bundle.exe'
$publicKey = 'D:/release-keys/hunter-public.key'
$secretSeed = 'D:/release-keys/hunter-signing.seed'
$releaseDir = 'server/build/release-artifacts'

py -3 server/tools/bundle.py identity --luax-root $luaxRoot `
    --public-key $publicKey --epoch 1 --output "$releaseDir/policy.json"

py -3 server/tools/bundle.py build --luaxc $luaxc --bundle-tool $bundleTool `
    --source server/build/win-dev/generated/game.lua `
    --policy "$releaseDir/policy.json" --secret-key $secretSeed `
    --output "$releaseDir/game.luxb"

py -3 server/tools/bundle.py verify --bundle-tool $bundleTool `
    --source "$releaseDir/game.luxb" --policy "$releaseDir/policy.json"
```

`identity` 拒绝非固定提交和已修改的 Luax 源码。九项 SHA-256 身份绑定真实 Luax 文件及
Hunter 契约；Runtime 身份包含预算成本模型、标准库和 VM 实现。`policy.json` 只保存公钥、
兼容身份、提交及发行代次；相邻 `policy.provenance.json` 保存身份来源，供构建审查。
签名构建在构建目录内临时编译、打包和签名，公钥验证及全部身份验证成功后创建最终文件；
已有 `.luxb` 不被覆盖。最终运行只需 `game.luxb` 和可信的 `policy.json`，不携带 seed、编译器
或签名工具。公钥及 policy 是宿主信任根，发行流程必须与可执行文件一起可信交付。

生产程序以 `--bundle <game.luxb> --policy <policy.json>` 启动；不提供 policy、签名不符、
身份不符或 Bundle 损坏都必须失败。测试公钥只用于测试，不能复制成发行公钥。
宿主还将 policy 的 Host、capability、state、effect 摘要与编译时契约核对，旧 Bundle 和旧
policy 即使彼此匹配，也不能与当前 v4 Host 混用。网络仍为 v3，内容仍为 v2。

## 发布失败与恢复边界

源码和映射、policy 和 provenance 各自作为一组发布。工具先检查所有目标，再取得目标旁的
`.hunter-publish.lock` 合作锁，在各目标目录创建 `.hunter-publish-*` 暂存目录。每个新文件
完整写入、`fsync` 后才开始替换；已有文件先备份。准备失败不改变已有制品；替换期间捕获
异常时恢复之前的整套字节，原本没有的文件被撤销。正常失败会清理暂存目录和本次取得的锁。
允许源码与映射位于不同目录，但调用者必须在工具成功返回后才读取整套产物。

最终 Bundle 通过同目录完整暂存文件的硬链接排他发布，不打开最终路径写入；目标文件系统
必须支持硬链接，不支持时明确失败。已有文件、符号链接、重复目标不会被覆盖。合作锁防止
这些工具同时改写同一路径；绕过锁的外部写入不属于保证范围。
文件名后缀 `.hunter-publish.lock` 不区分大小写地保留给锁，不能用作制品名；Windows
目标文件名也拒绝末尾点或空格，避免路径别名绕过重复目标与锁检查。

这不是跨两个路径的原子文件系统事务：并发读者可能看到短暂混合版本，强制终止、崩溃或掉电
也可能发生在两次替换之间；文件内容的 `fsync` 不等于跨目录持久提交。若回滚自身也失败，
工具明确报错并保留 `.hunter-publish-*` 中的备份、`recovery.json` 及合作锁，不伪装回滚成功。
恢复前停止消费和发布，按清单核对 `target/existed/backup/prepared`，恢复仍存在的旧备份并
核对已恢复目标，撤销原本不存在的本次目标；完成整套核对后才清理暂存目录及锁并重新构建。
进程退出前若已整套提交成功、仅清理临时文件失败，则报告警告并保留成功制品；可按提示清理
残留。工具可能留下准备阶段创建的父目录，父目录不作为制品回滚。
Bundle 外层编译暂存目录同样遵循提交后清理警告语义；若编译失败又清理失败，则同时报告
原始构建原因和残留目录，保持构建失败状态。

## 验证

```powershell
py -3 server/tests/scripts/test_assemble.py
py -3 server/tests/scripts/test_schema.py
py -3 server/tests/scripts/test_bundle.py --luax-root $luaxRoot `
    --luaxc $luaxc --bundle-tool $bundleTool `
    --source server/build/win-dev/generated/game.lua `
    --entity-source server/build/win-dev/generated/entities.lua `
    --output-dir server/build/win-dev/bundle-test
```

Bundle 测试使用与上游一致的 RFC 8032 公共测试向量，seed 只短暂写入指定构建目录后删除。
保留的 `game.luxb/policy.json/public.key/policy.provenance.json` 可供生产 Runtime 契约测试。
测试覆盖重复构建、错误公钥、九项身份、代次、签名与载荷篡改、已有制品、编译失败，
以及密码学签名有效但 Runtime 兼容版本错误的制品。另保留 host-v3.luxb/host-v3.json
作为旧 Host 元数据夹具，生产测试验证旧制品与旧 policy 成对提供时仍被拒绝。
这些工具测试不替代真实 Runtime 加载、Windows 进程或 Android ARM64 测试。
