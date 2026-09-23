我看了 `Hunter` 当前仓库结构和规划。这个项目其实已经把 SQLite 持久化的核心原则定下来了，只是还没有进入实现阶段：[`server/plan.md`](https://github.com/YanqingXu/Hunter/blob/main/server/plan.md) 明确要求 **SQLite 单写入、结算/去重/累计存档同一事务**；[`storage.intent.md`](https://github.com/YanqingXu/Hunter/blob/main/server/intents/modules/storage.intent.md) 也规定了 **Accepted ≠ Committed、同 ID 同内容幂等、不同内容冲突、未完成局不恢复/不发奖**。

结合现在的 `World / Player / Item / Weapon / Monster` 结构，我建议把 Hunter 的持久化做成下面这套。

## 1. 先明确：哪些数据该进 SQLite

当前 [`World::save/load`](https://github.com/YanqingXu/Hunter/blob/main/server/src/game/World.h) 已经能序列化 Tick、实体、Monster AI、位置、HP、武器冷却、输入状态等，但它是给 **热更新/测试状态迁移** 用的，不能直接拿来当游戏存档。

应该明确分成两套：

| 数据                             | SQLite | 原因       |
| ------------------------------ | -----: | -------- |
| `Entity.id/x/y/facing`         |      ❌ | 对局临时状态   |
| Monster HP / AI / attack_ticks |      ❌ | 不恢复未完成局  |
| Player 位置 / HP / 输入状态          |      ❌ | 对局临时状态   |
| Weapon 当前 ammo / reload_ticks  |      ❌ | 当前局状态    |
| World tick / seq / event_id    |      ❌ | 当前运行实例状态 |
| 玩家永久身份                         |      ✅ | 跨启动存在    |
| 永久获得的 Item                     |      ✅ | 核心存档     |
| 装备/技能/货币/等级                    |   ✅，后续 | 永久成长     |
| 已完成对局结果                        |      ✅ | 防重复结算    |
| 对局结算输入                         |      ✅ | 幂等判断     |
| 存档 revision                    |      ✅ | 一致性检查    |
| `next_match_id`                |      ✅ | 跨重启稳定 ID |

这是整个设计里最重要的一点：

> **SQLite 存的是“已提交的永久世界”，不是 World 的运行时镜像。**

因此应用被强杀时，当前猎杀局直接消失；最后一次成功 `COMMIT` 的永久数据仍然存在。

这正好符合 Hunter 当前设计。

---

# 2. 推荐架构

不要让 `Player`、Lua、网络层直接调用 SQLite。

推荐：

```text
                     Logic Thread
                         │
                         │ owned value
                         ▼
                ┌─────────────────┐
                │     Storage     │
                │ submit(task)    │
                └────────┬────────┘
                         │
                 BoundedQueue<Task>
                         │
                         ▼
                ┌─────────────────┐
                │ Storage Thread  │
                │   std::jthread  │
                │                 │
                │ sqlite3*        │
                │ 唯一连接拥有者   │
                └────────┬────────┘
                         │
                    hunter.db
                         │
                         ▼
                  CompletionQueue
                         │
                    asio::post
                         │
                         ▼
                     Logic Thread
```

这和你现在 `Async.cpp` 的架构非常匹配。

现有 `Async` 已经采用：

```text
后台线程
   ↓
拥有数据的完成包
   ↓
Inbox
   ↓
asio::post
   ↓
逻辑线程
```

Storage 可以复用这个设计思想，但**不要让 Storage 依赖 Luax**。

建议依赖方向：

```text
game / runtime
      ↓
   Storage
      ↓
 SQLite

Lua
 ↓
game logic
 ↓
C++ Settlement
 ↓
Storage
```

而不是：

```text
Lua → sqlite3
Player → sqlite3
World → sqlite3
```

这样以后 SQLite 换 MySQL/PostgreSQL，游戏对象都不用动。

---

# 3. 建议新增目录

和当前 `server/src/game`、`src/script` 的布局保持一致：

```text
server/src/storage/

    Db.h
    Db.cpp

    Schema.h
    Schema.cpp

    Model.h

    Storage.h
    Storage.cpp
```

职责建议：

| 文件        | 职责                                         |
| --------- | ------------------------------------------ |
| `Db`      | `sqlite3*` RAII、Statement、Transaction      |
| `Schema`  | 建库、版本、migration                            |
| `Model`   | `PlayerSave / ItemSave / CommitMatch` 等纯数据 |
| `Storage` | jthread、任务队列、完成队列、业务事务                     |

不要上 ORM。

Hunter 现在规模很小，而且你最关注的是**事务边界、Crash Safety、线程所有权**，直接 SQLite C API 会比 ORM 更清晰。

---

# 4. SQLite Schema V1

我建议第一版**不要提前创建 Skill、Equip、Quest 等几十张未来表**。

现在真正需要支撑 P4 的只有：

```text
永久玩家
永久物品
对局 ID 分配
已提交结算
```

Schema 可以这样：

```sql
CREATE TABLE save_meta (
    key         TEXT PRIMARY KEY,
    int_value   INTEGER,
    text_value  TEXT
);

CREATE TABLE player_save (
    player_id       INTEGER PRIMARY KEY,
    revision        INTEGER NOT NULL,
    last_match_id   INTEGER NOT NULL DEFAULT 0,
    created_at_ms   INTEGER NOT NULL,
    updated_at_ms   INTEGER NOT NULL
);

CREATE TABLE player_item (
    item_uid            INTEGER PRIMARY KEY,
    player_id           INTEGER NOT NULL,
    cfg_id              INTEGER NOT NULL,
    count               INTEGER NOT NULL CHECK(count > 0),
    acquired_match_id   INTEGER,

    FOREIGN KEY(player_id)
        REFERENCES player_save(player_id)
);

CREATE INDEX idx_player_item_player
ON player_item(player_id);

CREATE TABLE match_result (
    match_id            INTEGER PRIMARY KEY,
    player_id           INTEGER NOT NULL,

    outcome             TEXT NOT NULL,

    content_key         TEXT NOT NULL,

    request_json        TEXT NOT NULL,
    result_json         TEXT NOT NULL,

    revision_before     INTEGER NOT NULL,
    revision_after      INTEGER NOT NULL,

    committed_at_ms     INTEGER NOT NULL,

    FOREIGN KEY(player_id)
        REFERENCES player_save(player_id)
);
```

初始化：

```sql
INSERT INTO save_meta(key, int_value)
VALUES ('next_match_id', 1);

INSERT INTO player_save(
    player_id,
    revision,
    last_match_id,
    created_at_ms,
    updated_at_ms
)
VALUES (1, 1, 0, ?, ?);
```

数据库版本不要复用当前 `World` 的：

```cpp
"v": 4
```

那个是热更新状态版本。

数据库单独使用：

```sql
PRAGMA user_version = 1;
```

两者必须独立演进。

---

# 5. Match ID 必须交给 SQLite 分配

这个地方我建议你修改现在的逻辑。

当前 `World` 有：

```cpp
u64 match_id = 0;
u64 last_match = 0;
```

而 `State.cpp` 现在实际上假定：

```text
after_match_id + 1 == match_id
```

如果 `match_id` 只存在内存里，那么：

```text
启动
match_id = 1

强杀

重新启动
match_id = 1
```

持久化以后这是不能接受的。

应该改成：

```text
StartReq
   ↓
Storage::alloc_match()
   ↓
BEGIN IMMEDIATE
   ↓
读取 next_match_id
   ↓
next_match_id++
   ↓
COMMIT
   ↓
match_id
   ↓
World::begin(match_id)
```

即使：

```text
分配 match_id=100
↓
游戏进行
↓
Android 强杀
```

下次也是：

```text
101
```

100 只是一个空洞。

**不要恢复 100，也不要重复使用 100。**

空洞没有任何问题。

---

# 6. 最核心的事务：CommitMatch

整个持久化系统真正需要做到“绝对正确”的其实就是这一条事务。

定义类似：

```cpp
struct CommitMatch
{
    u64 match_id;
    u64 player_id;
    u64 expected_revision;

    Str outcome;
    Str content_key;

    Str request_json;
};
```

Lua 的 `settlement.lua` 负责计算：

```text
撤离成功
获得哪些物品
死亡
奖励多少
……
```

C++ 把它转换成经过完整校验的 `CommitMatch`。

然后 Storage：

```text
BEGIN IMMEDIATE

查 match_result(match_id)

存在
 ├─ request_json 完全一致
 │      → 返回之前 result_json
 │      → IdempotentSuccess
 │
 └─ request_json 不一致
        → Conflict

不存在
 ↓
检查 player_save.revision == expected_revision
 ↓
修改永久数据
 ↓
INSERT player_item ...
 ↓
UPDATE player_save
 ↓
INSERT match_result
 ↓
COMMIT
```

关键点是：

```text
INSERT match_result
+
发奖
+
修改玩家存档
```

必须是**同一个 SQLite transaction**。

绝对不能：

```text
INSERT match_result
COMMIT

然后再

UPDATE player_item
COMMIT
```

否则 Crash 就可能产生：

```text
显示已结算
但奖励没发
```

或者反过来：

```text
奖励已发
但没有去重记录
```

进而重复发奖。

---

# 7. 这样就天然解决了最棘手的 Crash 场景

假设撤离成功。

### 情况 A

```text
BEGIN
发奖
写 result
进程被杀
```

还没有 COMMIT。

SQLite 回滚：

```text
奖励不存在
match_result 不存在
```

重新进入游戏，认为该局没有成功结算。

符合 Hunter 规则。

### 情况 B

```text
BEGIN
发奖
写 result
COMMIT
进程被杀
```

此时客户端还没收到：

```text
SettlementCommitted
```

重新启动。

客户端/逻辑再次查询：

```sql
SELECT *
FROM match_result
WHERE match_id = ?;
```

发现已经成功：

```text
返回之前保存的 result
```

不会再发奖励。

这就是：

> **Exactly-once effect + at-least-once request**

游戏服务端很适合采用这种模型。

---

# 8. Player 不直接等于 PlayerSave

我建议再做一个很重要的领域划分。

现在：

```cpp
class Player
{
    Unit unit;
    Weapon weapon;

    u64 player_id;
    i32 reserve;

    ...
};
```

这是：

```text
Runtime Player
```

不要不断给它塞：

```cpp
gold
exp
inventory
achievement
quest
skill
equip
...
```

然后把整个 Player 存数据库。

建议引入：

```cpp
struct PlayerSave
{
    u64 player_id;
    u64 revision;

    Vec<ItemSave> items;

    // 后续
    // currencies
    // skills
    // equips
};
```

于是：

```text
PlayerSave
   │
   ├─ 永久状态
   │
   ▼
SQLite

Player
   │
   ├─ 当前对局状态
   │
   ▼
World
```

开始一局时：

```text
PlayerSave
   ↓
选择 Loadout
   ↓
创建 Runtime Player
```

结算：

```text
Runtime Result
       ↓
settlement.lua
       ↓
SettlementDelta
       ↓
SQLite Transaction
       ↓
新的 PlayerSave
```

边界会非常干净。

---

# 9. Item 的 ID 也要区分两个概念

你现在的：

```cpp
class Item
{
    u64 id;
    u32 cfg_id;
    i32 count;
};
```

这个 `id` 当前属于 World 内对象。

以后持久化后不要让它同时承担永久物品 ID。

建议明确：

```text
runtime_item_id
```

和：

```text
item_uid
```

例如：

```text
本局掉落：

Runtime Item
id = 27
cfg_id = 10003

撤离成功：

SQLite
item_uid = 917
cfg_id   = 10003
```

`27` 随当前局消失。

`917` 永久存在。

这样以后：

```text
装备
强化
词条
耐久
绑定
交易
```

全部引用：

```text
item_uid
```

而不是 World entity id。

---

# 10. Skill / Equip 暂时不要进 V1

虽然以后 Hunter 很可能会有：

```text
Skill
Equip
Talent
Currency
Quest
Achievement
```

但当前仓库实际上只有：

```text
Entity
Unit
Player
Monster
Item
Weapon
World
```

`settlement.lua` 甚至还是：

```lua
-- 模块占位
```

所以我不建议现在就设计二十张表。

等领域模型稳定后通过 migration 添加：

```sql
CREATE TABLE player_skill (...);

CREATE TABLE player_equip (
    player_id INTEGER,
    slot      INTEGER,
    item_uid  INTEGER,
    ...
);
```

这里的设计原则应该是：

> **数据库跟随已经稳定的领域模型，而不是提前猜整个游戏以后会有什么。**

---

# 11. SQLite 推荐配置

Hunter 是：

```text
单机
单服务进程
单 Storage Thread
单 SQLite Connection
低频结算写入
```

这是 SQLite 非常舒服的工作负载。

我建议打开：

```sql
PRAGMA foreign_keys = ON;

PRAGMA journal_mode = WAL;

PRAGMA synchronous = FULL;

PRAGMA busy_timeout = 2000;
```

其中我会特别推荐：

```text
WAL + FULL
```

因为你不是在做每秒几万次数据库写入。

一局最多才提交几次重要事务。

所以这里应该优先：

```text
Crash durability
```

而不是为了几毫秒，把：

```text
synchronous=NORMAL/OFF
```

当成性能优化。

---

# 12. SQLite 依赖怎么加入 Hunter

你现在的 `Deps.cmake` 对依赖版本控制非常严格，这一点不要破坏。

SQLite 推荐直接固定一个 **amalgamation**：

```text
sqlite3.c
sqlite3.h
```

作为固定版本依赖编译进：

```text
libhunter_server.so
```

而不是依赖手机系统 SQLite。

这样：

```text
Windows
Android
CI
```

运行的是完全一样的 SQLite 实现。

CMake 层建议：

```text
hunter_sqlite
      ↓
hunter_storage
      ↓
hunter_server_core
```

即：

```cmake
add_library(hunter_sqlite STATIC
    third_party/sqlite/sqlite3.c)

add_library(hunter_storage STATIC
    src/storage/Db.cpp
    src/storage/Schema.cpp
    src/storage/Storage.cpp)

target_link_libraries(hunter_storage
    PUBLIC
        hunter_sqlite
        Threads::Threads)
```

你当前：

```cmake
project(HunterServer LANGUAGES CXX)
```

如果直接编译官方 `sqlite3.c`，这里最好改成：

```cmake
project(HunterServer LANGUAGES C CXX)
```

这是实际接 SQLite 时一个容易遗漏的小点。

---

# 13. Storage 生命周期

最终 Runtime 的启动顺序建议变成：

```text
Runtime Start

       ↓

resolve data path

       ↓

Storage Thread
       ↓
sqlite3_open
       ↓
PRAGMA
       ↓
schema migration
       ↓
quick_check
       ↓
load PlayerSave

       ↓

Luax Runtime

       ↓

Transport listen

       ↓

Ready
```

即：

> **数据库没有成功打开，就不要宣称 Server Ready。**

P4 之后存档已经属于游戏正确性的组成部分，不应该：

```text
SQLite Failed
但服务器还能正常 Ready
```

否则用户可能打一局最后才发现无法保存。

---

# 14. Storage shutdown

正常退出则反过来：

```text
Stopping
   ↓
停止接受新游戏操作
   ↓
停止接受新持久化请求
   ↓
等待已经 Accepted 的事务完成
   ↓
处理 Completion
   ↓
Storage Thread close
   ↓
sqlite3_close
   ↓
Runtime exit
```

这里正好落实你已有的：

```text
Accepted != Committed
```

以及：

> shutdown 不依赖最后一次“保存全部”。

你不需要传统游戏经常出现的：

```cpp
Player::~Player()
{
    Save();
}
```

这种方案。

对于 Hunter，这种设计反而应该明确禁止。

---

# 15. 我建议的具体实施顺序

如果现在开始做，我建议按下面 5 个增量推进，而不是一次实现完整背包/技能/装备：

1. **Storage Core**：把 `SRV-007` 从 deferred 拆成可执行 intent，引入固定 SQLite、`Db/Schema/Storage`、`PRAGMA user_version`，完成空库创建、迁移、损坏库拒绝、单线程连接所有权测试。
2. **永久 Player + Match ID**：实现 `player_save/save_meta`，启动加载 `PlayerSave`，由 SQLite 分配 `match_id`，验证重启后 ID 不重复；此时还不做奖励。
3. **事务结算**：实现 `match_result + player_item + CommitMatch`，保证结果、去重记录、物品变化同一事务，并完成“相同请求幂等 / 不同请求冲突 / revision 不一致拒绝”。
4. **玩法接入**：实现现在还是占位的 `lua/game/settlement.lua`，让 Lua 只负责计算 `SettlementDelta`，C++ 完整校验后交给 Storage；World 增加 `Settling → Finished` 流程，客户端只在 `Committed` 后显示最终奖励。
5. **Crash 验证**：重点测“事务前强杀、事务中强杀、COMMIT 后回包前强杀、重复提交、磁盘写失败、损坏 DB、连续 10 局和重启读取”；Android P4 验收必须覆盖真实进程被杀，而不仅仅是 Windows 单元测试。

---

## 最终结构

我认为 Hunter 最终应该形成这样的数据模型：

```text
                    ┌──────────────────┐
                    │   Content Data   │
                    │ cfg_id / Excel   │
                    └────────┬─────────┘
                             │
                             │ reference
                             ▼
┌──────────────────────────────────────────────┐
│                 Runtime World                │
│                                              │
│ Entity → Unit → Player / Monster             │
│             │                                │
│             └→ Weapon                        │
│                                              │
│ Item(runtime id)                             │
│                                              │
│ tick / input / AI / position / hp / ammo     │
└───────────────────┬──────────────────────────┘
                    │
                    │ settlement
                    ▼
             settlement.lua
                    │
                    ▼
             SettlementDelta
                    │
                    ▼
┌──────────────────────────────────────────────┐
│                Storage Thread                │
│                                              │
│ BEGIN IMMEDIATE                              │
│                                              │
│ MatchResult                                  │
│ + PlayerSave                                 │
│ + ItemSave                                   │
│ + Dedup                                      │
│                                              │
│ COMMIT                                       │
└───────────────────┬──────────────────────────┘
                    │
                    ▼
                 SQLite
                hunter.db
```

这套结构跟你现在 Hunter 的架构非常契合：**C++ 继续掌握状态和生命周期，Lua 只负责规则，SQLite 只负责已确认永久状态。**

尤其要坚持三个边界：

```text
World::save() ≠ 游戏存档

Runtime Item ID ≠ Persistent Item UID

Settlement Accepted ≠ Settlement Committed
```

把这三个原则守住，后面再往 `Inventory / Equip / Skill / Currency / Achievement` 扩展时，数据库基本不会推倒重来。

如果下一步直接进入实现，我建议第一个改动就做 **`SRV-007 + src/storage/ + SQLite Schema V1 + Storage 契约测试`**，暂时不碰玩法结算；这样可以先把持久化底座本身做扎实。
