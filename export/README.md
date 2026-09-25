# 配置导出工具

服务端首版构建使用 `demo.py`，复用 `export.py` 读取 `design/demo/首版.xlsx`，生成统一
JSON、C++ 头文件和内容摘要；修改该工作簿后重新构建即可改变正式玩法。
该适配器校验地图 2 的出生、技能、枪械、Boss、掉落、堆叠、容量和撤离引用。
工作簿来源及与现行策划表的区别见 [首版内容说明](../design/demo/README.md)。

原 `export.py` 的 Excel → Lua 桌面导表工具继续保留；这些普通 Lua 配置不在运行时执行。
`combat.py` 只生成灰盒回归夹具。三个 Python 工具均只依赖标准库。

## 策划使用：改清单，双击导出

1. 用记事本打开本目录的 `导出目标.txt`，每行填写本次需要导出的 Excel 文件名并保存。
2. 双击本目录的 `导出配置.exe`，查看中文结果窗口；成功后可点击“打开输出目录”。

例如仅导出道具和技能：

```text
道具表.xlsx
技能表.xlsx
```

清单默认列出当前全部 7 个 Excel，可以删除不需要导出的行；空行与 `#` 开头的说明行
忽略。可以省略 `.xlsx` 后缀，子目录使用 `子目录/文件名.xlsx`。名称大小写不敏感，
同名文件有歧义时必须填相对路径。清单不能为空，重复、找不到文件及目录外路径都会报错，
不会退回“导出全部”。支持记事本的 UTF-8、带 BOM 的 UTF-16 和旧版中文编码。

Excel 源文件放在 `design`，EXE 与清单放在相邻的 `export`，从 EXE 位置定位项目，
不受双击时工作目录影响。复制整个项目到含中文或空格的新路径也可使用，不要单独移动
EXE。每个选中文件的全部服务端工作表都会导出；例如道具表会生成 `Item.lua` 和
`Equip.lua`。未选文件不读取，其上次输出保留；跨表引用的依赖不会自动加入本次目标。
修改关联表后应把它们一起写入清单。

输出目录固定为 `server/build/generated/cfg`，结果同时保存到本目录的 `导出结果.log`
（每次覆盖）。错误窗口和日志会指出清单行号或 Excel 单元格。策划的简明操作说明另见
`策划使用说明.txt`。工具读取已保存的 Excel 内容，导出前请先保存源文件。

## Excel → 服务端 Lua

以下命令供开发使用。命令行入口默认扫描全部 Excel，不读取策划清单。在仓库目录运行：

```powershell
# 生成服务端配置
py -3 export/export.py

# 完整读取、校验并生成内存中的 Lua 内容，不创建任何输出文件或目录
py -3 export/export.py --check

# 自定义目录
py -3 export/export.py --source design --output server/build/generated/cfg
```

`--source` 默认 `design`，`--output` 默认 `server/build/generated/cfg`。参数中的相对路径
始终相对仓库根目录，绝对路径直接使用；从其他目录调用时应使用脚本的绝对路径。
默认输出位于已有的 Git 忽略目录。源 Excel 保持人工维护，输出 Lua 可以重新生成。

当前 7 个工作簿、15 张配置表、103 条记录会生成 `Item.lua`、`Equip.lua`、`Map.lua`、
`MapSolid.lua`、`PlayerSpawn.lua`、`MonsterSpawn.lua`、`ExtractPoint.lua`、`Player.lua`、
`PlayerSkill.lua`、`Monster.lua`、`MonsterSkill.lua`、`Weapon.lua`、`Skill.lua`、`Drop.lua`
和 `DropEntry.lua`。工具按表头驱动，新表不需要修改 Python 代码。

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

### 输出格式与加载边界

每个文件直接返回以数字主键索引的 Lua 表，记录中仍保留主键字段，例如：

```lua
-- 自动生成，请修改 Excel 源表。
return {
    [1] = {
        ["ItemIdx"] = 1,
        ["Name"] = "红钻",
        ["Kind"] = 10,
        ["Type"] = 0,
        ["SubType"] = 0,
        ["MaxStack"] = 0,
    },
}
```

文件名按英文表名、记录按数字主键排序，字段保留源表列顺序。输出为无 BOM 的 UTF-8、
LF 换行，不包含时间戳或机器路径，相同配置可生成相同字节。引号、反斜杠和控制字符会
按 Lua 5.1 语法转义，文本不会作为代码执行。空表输出 `return {}`。

独立 Lua 环境可以 `local items = dofile(".../Item.lua")` 后用 `items[1]` 查询。
这只是文件格式用法：Hunter 当前通过宿主注入 JSON，脚本模块使用 `factory(deps)` 约定，
不能直接把这些返回表的文件加入 `server/lua/modules.json`。本轮没有修改模块清单、
运行时、内容版本、协议、CMake 或任何构建入口。

### 失败处理

错误包含文件路径、工作表、单元格及原因，例如：

```text
导表失败: .../道具表.xlsx [道具|Item]!E5: Type: 必须填写 int32 整数，不能留空
```

成功退出码为 `0`；读取、校验或发布失败为 `1`，命令行参数错误为 `2`。
全部输入校验和 Lua 序列化完成后，统一调用现有 `server/tools/publish.py` 暂存并发布。
可捕获的发布异常会回滚旧字节；不提供崩溃、掉电或并发读者的跨文件原子性，消费者须
等待进程成功退出。回滚失败时保留恢复材料和锁，按错误提示的恢复日志处理。

仅覆盖本次生成的同名文件，不自动删除其他文件。删除或重命名工作表后，需人工清理旧
Lua 文件。请为生成配置使用独立输出目录，不在生成文件中维护手写内容。

### 测试

```powershell
py -3 -m unittest discover -s export/tests -v
py -3 server/tests/scripts/test_content.py
```

测试覆盖 OOXML 字符串、包内关系、表头与主键、字段过滤、无写入检查模式、稳定输出、
非法数据保留旧输出、发布失败回滚以及当前配置回归。真实源表记录数量变动后，应同步
更新回归用例中的预期数量。

在 PATH 有 `lua` 时执行全部生成记录的加载和值检查；有 `luaxc` 时逐文件执行 strict
编译检查。可设置 `LUAXC` 指定编译器，也会探测当前开发构建的
`server/build/win-dev/_deps/luax-build/Release/luaxc.exe`。缺少引擎时相关用例明确标为
跳过，通用导出和校验功能本身无需安装引擎。

### 开发重新打包 EXE

`launcher.py` 提供清单选择和中文结果窗口，核心转换仍复用 `export.py`。
Windows 上重新打包需要 PyInstaller（仅构建依赖，不需要策划安装）：

```powershell
py -3 -m pip install PyInstaller==6.20.0
py -3 export/build.py
```

`build.py` 生成 `export/导出配置.exe`，Python 运行时、Tk 窗口库及发布模块包含在单个
64 位 EXE 中。打包中间文件位于已忽略的 `server/build/export-tool`，不会改变运行时构建。
本次打包环境为 Windows 10 x64、Python 3.14.4、PyInstaller 6.20.0。修改 Python 源码后需
重新打包并重新交付 EXE，源文件改动不会自动进入已有 EXE。

EXE 支持开发验收参数 `--no-ui`（不显示窗口，仍写日志并设置退出码）与 `--check`
（不写 Lua，仍写结果日志）。策划无需使用这些参数。源脚本 `export.py --check` 的
不写文件约定保持不变。

## 原灰盒战斗内容导出

`design/combat_demo.json` 是当前运行时地图、角色与枪械数值的唯一来源。`combat.py` 仅依赖 Python
标准库，构建时生成 `generated/content.json` 和 `generated/ContentSpec.h`；Unity 可读取同一
JSON，服务端通过生成头中的 `hunter::content::json_text` 加载相同数据。

```powershell
py -3 export/combat.py --source design/combat_demo.json `
    --output server/build/win-dev/generated/content.json `
    --header server/build/win-dev/generated/ContentSpec.h
py -3 server/tests/scripts/test_content.py
```

内容 v2 固定 60 Hz。距离使用整数毫米，`speed`、`jump_speed` 使用毫米每 Tick，`gravity`
使用每 Tick 的速度增量，冷却与换弹使用 Tick。角色坐标为脚底中心，X 向右、Y 向上；
宽高必须是偶数毫米。地图边界提供隐式地面、左右墙与顶部限制；`solids` 是左下角
`x/y` 加 `w/h` 的实心矩形，两端采用相同碰撞几何。

24×10 米灰盒含两座平台、一堵低墙及两名普通怪。角色为 100 生命，移动 100 毫米/Tick；
枪械每发 20 伤害、6 发弹匣和 30 发备弹、10 Tick 射击间隔、90 Tick 换弹。普通怪 60 生命，
每次攻击 10 伤害、60 Tick 间隔。该配置只用于基础战斗切片。

`players`、`monsters`、`weapons` 是以配置 ID 为键的独立配置表。`map.spawn` 保存
`x/y/cfg_id/weapon_cfg_id`；`map.enemies` 保存各怪物出生的
`spawn_id/cfg_id/x/y/patrol_min/patrol_max`。公共 `gravity` 归入 `map`，
玩家配置只保留自己的 `jump_speed`。默认三个配置表均使用 ID `1`，原地图和数值不变。

导出器精确校验对象字段、整数类型和值域，拒绝重复 JSON 键、同命名空间重复 ID、障碍越界/重叠、
出生穿透/悬空/重叠、越界或穿墙/缺少支撑的巡逻区。ID 是无前导零的正十进制字符串，
上限为 2147483647；玩家配置、怪物配置、枪械配置、出生点和静态障碍分别使用独立 ID 空间。
运行时实体 ID 由世界单独分配，不来自出生点 ID；同一出生点再次生成会获得新实体 ID。
配置表非空，所有配置引用必须存在；出生和巡逻检查使用该出生实际引用的角色体型。
静态障碍最多 128 个，怪物出生点 1～32 个，内容不超过 60 KiB。各数值上限由导出器固定，
非法数据不会自动截断或补默认值，旧 v1 内容明确拒绝。

规范 JSON 使用 UTF-8、对象键排序、无无效空格和末尾换行；数组顺序保留并计入身份。
`hunter::content::version` 为 `combat-v2:` 加规范 JSON 字节的 SHA-256，任何有效数值变化
都会改变握手内容版本，源文件空白与对象字段重排不改变身份。

导出复用 `server/tools/publish.py`：先校验全部内容，再暂存完整 JSON 与头文件，发布异常时
恢复旧字节。两个输出文件不提供崩溃、掉电或并发读者的跨文件原子性；构建消费者必须等待
导出进程成功退出。回滚本身失败时保留恢复材料及锁，按照工具错误中的路径恢复。
