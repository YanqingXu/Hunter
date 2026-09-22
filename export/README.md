# 灰盒战斗内容导出

`design/combat_demo.json` 是地图、角色与枪械数值的唯一来源。`combat.py` 仅依赖 Python
标准库，构建时生成 `generated/content.json` 和 `generated/ContentSpec.h`；Unity 可读取同一
JSON，服务端通过生成头中的 `hunter::content::json_text` 加载相同数据。

```powershell
py -3 export/combat.py --source design/combat_demo.json `
    --output server/build/win-dev/generated/content.json `
    --header server/build/win-dev/generated/ContentSpec.h
py -3 server/tests/scripts/test_content.py
```

内容 v1 固定 60 Hz。距离使用整数毫米，`speed`、`jump_speed` 使用毫米每 Tick，`gravity`
使用每 Tick 的速度增量，冷却与换弹使用 Tick。角色坐标为脚底中心，X 向右、Y 向上；
宽高必须是偶数毫米。地图边界提供隐式地面、左右墙与顶部限制；`solids` 是左下角
`x/y` 加 `w/h` 的实心矩形，两端采用相同碰撞几何。

24×10 米灰盒含两座平台、一堵低墙及两名普通怪。角色为 100 生命，移动 100 毫米/Tick；
枪械每发 20 伤害、6 发弹匣和 30 发备弹、10 Tick 射击间隔、90 Tick 换弹。普通怪 60 生命，
每次攻击 10 伤害、60 Tick 间隔。该配置只用于基础战斗切片。

导出器精确校验对象字段、整数类型和值域，拒绝重复 JSON 键、重复 ID、障碍越界/重叠、
出生穿透/悬空/重叠、越界或穿墙/缺少支撑的巡逻区。ID 是无前导零的正十进制字符串，
上限为 2147483647；玩家 ID `1` 保留。静态障碍最多 128 个，普通怪 1～32 个，内容不超过
60 KiB。各数值上限由导出器固定，非法数据不会自动截断或补默认值。

规范 JSON 使用 UTF-8、对象键排序、无无效空格和末尾换行；数组顺序保留并计入身份。
`hunter::content::version` 为 `combat-v1:` 加规范 JSON 字节的 SHA-256，任何有效数值变化
都会改变握手内容版本，源文件空白与对象字段重排不改变身份。

导出复用 `server/tools/publish.py`：先校验全部内容，再暂存完整 JSON 与头文件，发布异常时
恢复旧字节。两个输出文件不提供崩溃、掉电或并发读者的跨文件原子性；构建消费者必须等待
导出进程成功退出。回滚本身失败时保留恢复材料及锁，按照工具错误中的路径恢复。
