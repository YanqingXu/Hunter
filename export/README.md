# 配置导出工具

正式 DEMO 按 `design/demo_sources.json` 读取根工作簿中明确选中的工作表和记录。当前选入
6 个工作簿、19 张表、51 条记录。每张表导出独立 Lua 文件，保留原英文字段名和数字主键。
生产不扫描整个 `design/`，不读取天赋草稿、重复关卡表或历史首版工作簿。

Python 只负责读取 OOXML、检查字段类型、选择记录及确定性序列化。`server/lua/game/cfg.lua`
及其配置模块负责字段转换、单位、引用、默认配装、地图几何和玩法语义校验。构建和服务端
运行同一份 Lua 规则；规范 JSON 及 `gameplay-v4:` 摘要从 Lua 返回内容派生。数值和语义未
变时摘要不变，未选草稿不参与身份。

## 正式导出

先构建开发 preset 的 `hunter_tools`，取得固定依赖版本的 `luax.exe`。在仓库根目录运行：

```powershell
$luax = 'server/build/win-dev/_deps/luax-build/Release/luax.exe'
python export/gameplay.py --source design/demo_sources.json --luax $luax --check
python export/gameplay.py --source design/demo_sources.json --luax $luax `
    --cfg server/build/win-dev/generated/cfg `
    --output server/build/win-dev/generated/content.json `
    --header server/build/win-dev/generated/ContentId.h
python server/tools/assemble.py --manifest server/lua/modules.json `
    --cfg server/build/win-dev/generated/cfg `
    --output server/build/win-dev/generated/game.lua `
    --map server/build/win-dev/generated/game.map.json
```

`--check` 执行完整读取和 Luax strict 校验，不发布文件或创建输出目录。省略 `--luax` 时，
依次使用 `HUNTER_LUAX`、`LUAX`，再查找当前开发／Bundle 构建树中的固定工具；不会调用
PATH 中另一个 Lua 实现。缺少解释器明确失败。跨机构建可显式传入同一固定依赖的宿主工具。

`generated/cfg/Manifest.json` 列出本次配置模块。每张表是 `kind:data`，直接返回配置表；
生成的 `Tables.lua` 是 `kind:factory`，通过显式依赖聚合成 `cfg.tables`。玩法模块清单
同样显式使用 `kind:factory`。组装器把配置和玩法共同生成 `game.lua`，开发 Runtime 加载
源码，发布 Runtime 加载其离线编译的签名 Bundle。运行时不调用 `require` 或 `dofile`。

共享 `content.json` 是客户端制品，`ContentId.h` 仅包含身份字符串，不再包含配置数值。
服务端启动由 Lua 加载配置，不读取共享 JSON、不内嵌完整配置头；C++ 保留结构边界和
权威状态。更新玩法数值只需重新导出并组装脚本或 Bundle，无需重新编译服务端核心。

清单缺表、缺记录、重复身份、未选依赖、非法血段、超重初装、弹药不匹配、错误单位范围
及场景阻断出生或巡逻均阻止发布。源字段缺值不回退旧表，整数空白不自动变成零。
源表选择与默认值见 [DEMO 内容说明](../design/DEMO内容说明.md)。

## 策划双击入口

1. 保存 `design/` 中源表的修改；生产选择由 `design/demo_sources.json` 明确指定。
2. 双击 `export/导出配置.exe`，查看中文结果窗口和 `export/导出结果.log`。

默认入口与 CMake 使用同一个 `gameplay.export_content` 流程，输出目录是
`server/build/generated/cfg`，共享 JSON 和身份头输出到其父目录。缺失清单、工具或 Lua
校验失败都明确报错，绝不改用旧草稿。策划无需安装 Python；项目必须包含固定 Luax 工具
与 Lua 配置脚本，开发人员可先构建 `hunter_tools` 准备好环境。

EXE 从自身位置查找项目，不受工作目录影响；可以复制整个项目到包含中文或空格的路径，
不要单独移动 EXE。程序读取磁盘已保存内容，错误会显示工作簿单元格或 Lua 语义原因。
`--no-ui` 只写结果日志与退出码；`--check` 不发布配置但仍写结果日志。

旧 `导出目标.txt` 仅用于显式 `导出配置.exe --draft`，不会影响默认生产导出。草稿入口
按文件选择全部服务端工作表，保留旧通用导表用途；它不执行玩法关联，也不被运行时加载。
草稿固定输出到 `server/build/draft-cfg`，与正式 `generated/cfg` 隔离；通用 `export.py`
不传 `--output` 时同样使用草稿目录，不会覆盖正式数据表或清单。
清单支持 UTF-8、UTF-16、GB18030，忽略空行和 `#` 说明行；重名、目录逃逸、空清单均拒绝。

## 通用 Excel 读取与 Lua 格式

`export.py` 是复用的类型读取和序列化底座。直接执行仅检查草稿字段格式，不产生正式配置
清单或经过玩法验证的内容：

```powershell
python export/export.py --source design --output server/build/draft-cfg --check
python export/launcher.py --draft --no-ui
```

未选草稿可以不完整，因此通用全目录扫描可能失败；正式构建只转换生产清单选中的记录。

### 输入约定

递归读取 `.xlsx`（扩展名大小写均可），跳过文件名以 `~$` 开头的 Office 临时文件。
没有 `|` 的说明工作表不导出。配置工作表使用 `中文名|EnglishName`，英文名必须匹配
`[A-Za-z_][A-Za-z0-9_]*`，且不能是 `CON`、`NUL`、`COM1` 等 Windows 保留名。
服务端输出表名在整个输入目录中不能重复，大小写不同也算重复。没有可导出的表会报错。

| 行 | 内容 | 示例 |
| --- | --- | --- |
| 1 | 非空中文说明 | 道具ID |
| 2 | 类型 | `int32` |
| 3 | 唯一英文字段名 | `ItemIdx` |
| 4 | 以 `/` 分隔的标记 | `server/client/key` |
| 5 起 | 数据 | `1` |

字段从 A 列连续声明，不得插入空列。字段名使用同样的英文标识符规则；保留源字段的
大小写，不强制改名。工具根据第三行确定字段范围，忽略右侧没有字段声明和导出标记的
说明区域。若第四行已有标记但第三行缺少字段名，则报错，避免漏导最后一列。

标记仅支持 `server`、`client`、`key`、`index`，不得重复，每列至少属于一端。
`server/client` 与 `client/server` 等价。只转换和输出含 `server` 的列；`index`
只作为已识别的元数据，本版不建立额外索引。纯客户端表跳过，客户端数据和资源存在性
不校验；配置表表头仍须合法。

每张含服务端列的表必须有且仅有一个 `int32` 主键，且该列标记包含 `server/key`。
主键值范围为 `1～2147483647`，每表独立编号，文本 `"3"` 与数字 `3` 算同一个 ID。

| 服务端类型 | 接受的值 | 拒绝的值 |
| --- | --- | --- |
| `int32` | `-2147483648～2147483647`；Excel 整数数值（含等值 `1.0`、`1E2`）；规范十进制整数文本 | 空值、小数、溢出、布尔；文本 `01`、`1.0`、`1e2`、前后空白 |
| `string` | 文本单元格，保留空格、中文、换行；空值输出 `""` | 非空数值、布尔、日期类型单元格 |

服务端公式单元格一律拒绝，即使有缓存值；Excel 错误值同样拒绝。类型未知时报错，
不截断、不自动补零。四条历史货币的 `Item.Type` 已在源表补为 `0`，不需要特殊兼容规则。
纯空行跳过；只有客户端列有数据的行仍须补主键与服务端必填整数。合法空表允许导出。
字符串没有通用的“必填名称”业务检查；Excel 表内的名称必填、枚举、外键、掉落概率、
几何和跨行唯一性约束仍按[填表说明](../design/配置表填写说明.md)维护，本工具不读取
Excel 输入校验来推导业务规则。

### 输出与失败边界

每个文件直接返回数字主键表，例如 `Player.lua`：

```lua
-- 自动生成，请修改 Excel 源表。
return {
    [1] = {
        ["PlayerIdx"] = 1,
        ["HPSetting"] = "50|50|25|25",
    },
}
```

文件名保留表名大小写，记录按数字主键排序，字段保留源表列顺序。输出为无 BOM UTF-8、
LF 换行，不包含时间戳或机器路径。字符串转义引号、反斜杠和控制字符；表格文本不会
作为代码执行。数据表不能声明模块依赖，配置清单只允许 `cfg.*` 名字空间，真实路径和
符号链接不能越过输出目录。目录里未被清单选中的旧文件不会被加载。

全部 Lua 文件先在临时目录生成，用固定 Luax strict 执行正式配置加载器；校验成功后才
统一调用 `server/tools/publish.py` 发布 Lua、清单、共享 JSON 和身份头。校验失败完全
不触碰正式输出，可捕获的发布异常恢复上一份有效字节。它不是跨文件崩溃原子事务，
消费者必须等待导出进程成功退出；回滚失败时保留恢复材料及锁，按错误提示处理。

只覆盖本次清单涉及的同名文件，不删除其他文件；生成目录不应混放手写内容。退出码：
成功 `0`，读取／校验／发布失败 `1`，参数错误 `2`。

## 夹具与验证

旧 `combat.py`、`demo.py`、`design/combat_demo.json` 与 `design/demo/首版.xlsx` 仅用于
明确隔离的历史测试。当前 Runtime 需要旧灰盒时，通过 Lua 升级为内容 v4：

```powershell
python export/gameplay.py --fixture design/combat_demo.json --luax $luax `
    --cfg server/build/win-dev/generated/fixture/cfg `
    --output server/build/win-dev/generated/fixture/content.json `
    --header server/build/win-dev/generated/fixture/ContentId.h
python -B -m unittest discover -s export/tests -v
python -B server/tests/scripts/test_gameplay_content.py
python -B server/tests/scripts/test_assemble.py
```

`--fixture` 同样接受规范 v4 JSON，生成独立 `Fixture.lua`；随后执行相同 Lua 校验并按
独立配置目录组装测试脚本／Bundle。它不是生产来源的回退，也不存在运行时上下文注入。
旧灰盒升级数值仅存在 `game.cfg_fixture`，生产 Python 不再维护一套玩法转换规则。

验证覆盖真实生产数值与旧摘要一致、原字段 Lua 输出、源表数值变化、未选草稿、非法
引用／单位／几何、显式夹具、发布异常回滚、data/factory 模块边界和 Windows 策划 EXE。
通用序列化测试还可使用 `LUAXC` 指定固定编译器执行 strict 编译；这些可选单表检查不能
替代正式导出必需的 Luax 配置加载校验。

## 重新打包 EXE

Windows 构建依赖 PyInstaller 6.20.0，策划机器不需要安装它：

```powershell
python -m pip install PyInstaller==6.20.0
python export/build.py
```

`build.py` 将 Python、Tk、转换和发布模块打包到 `export/导出配置.exe`，中间文件位于
已忽略的 `server/build/export-tool`。2026-09-26 实际使用 Windows 10 x64、Python 3.13.13、
PyInstaller 6.20.0 重新打包，并在 PATH 无 Python 的环境通过草稿与生产校验。
修改工具源码后必须重打包；配置 Lua 校验模块从当前项目读取，与 CMake 使用同一份代码。
