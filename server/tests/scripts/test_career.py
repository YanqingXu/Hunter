# 真实执行只读经济模块，验证实际支付退款、成长、装备剩余和局内技能持久集合。
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / "export"))
import gameplay


class CareerTest(unittest.TestCase):
    # 使用真实 Lua 模块和隔离配置，不向生产注入经济数值。
    def test_authoritative_transactions(self):
        source = []
        for name, filename in (("state", "framework/state.lua"), ("skill", "game/skill.lua"),
                               ("loadout", "game/loadout.lua"), ("career", "game/career.lua")):
            raw = (ROOT / "server/lua" / filename).read_text(encoding="utf-8")
            source.append("local " + name + "_factory = (function()\n" + raw + "\nend)()")
        source.append('''
local state = state_factory({})
local skill = skill_factory({["framework.state"]=state})
local loadout = loadout_factory({["framework.state"]=state,["game.skill"]=skill})
local career = career_factory({["game.loadout"]=loadout})
local doc = {players={["1"]={hp=100,hp_segments=json.array({50,50}),weight=3,
    stamina=100,run_speed=100,skill_points=0,recruit_cost={currency_id="0",amount=0}}},
    weapons={["1"]={item_cfg_id="101",ammo_cfg_id="1",weight=1,melee_damage=50,
        reserve=10,fire_ticks=10,reload_ticks=60}},ammo={["1"]={}},
    tools={["201"]={kind="medkit",uses=3,use_ticks=120,heal=25}},
    rules={weapon_slots=2,tool_slots=4,consumable_slots=4},
    skills={["5"]={cost=4,effects=json.array({{stat="run_speed",op="add",value=10}})}},
    career={levels=json.array({0,10,30}),kill_xp={["10"]=3},extract_xp=7,
        bounty_xp=2,bounty_currency=5,retire_xp=20}}
local owner={hunter_id=7,cfg_id=1,level=1,xp=0,points=0,state="ready",
    skills=json.array({{cfg_id=5,paid_cost=3,source="purchased"}}),
    equipment=json.array({{slot=1,item_uid=11,cfg_id=101,count=1},
        {slot=3,item_uid=12,cfg_id=201,count=8}})}
local profile={hunters=json.array({owner}),stash=json.array({})}
local request={v=8,kind="start",profile=profile,hunter_id="7"}
local result=career.check(doc,request)
assert(result.ok and #result.payload.loadout.weapons==1)
assert(result.payload.tool_counts[1].count==3)
request.kind="remove_skill";request.skill_id="5"
assert(career.check(doc,request).payload.refund==1)
owner.skills[1].paid_cost=1;assert(career.check(doc,request).payload.refund==1)
owner.skills[1].paid_cost=0;assert(career.check(doc,request).payload.refund==0)
owner.state="in_raid";assert(not career.check(doc,request).ok)
request.kind="finish_raid"
request.state={hunter_id="7",phase="Settling",raid={player_state="Extracted"},
    match_id="2",content_key="test",player_entity_id="1",player_id="1",bounty_id="0",
    entities={["1"]={kind="player",demo={skills=json.array({{cfg_id="5",spent=true},
        {cfg_id="8",spent=false}}),tools=json.array({{cfg_id="201",count=1}})}}},
    items={["1"]={place="Bag",owner_player_id="1",cfg_id="101",count=2}}}
request.state.entities["2"]={kind="monster",cfg_id="10",health={alive=false},ai={owner_id="0"}}
request.state.entities["3"]={kind="monster",cfg_id="10",health={alive=false},ai={owner_id="2"}}
result=career.check(doc,request)
assert(result.ok and result.payload.level==2 and result.payload.xp==10)
assert(result.payload.points==1 and #result.payload.skills==1)
assert(result.payload.skills[1].source=="loot" and result.payload.skills[1].paid_cost==0)
assert(result.payload.equipment[2].count==6 and result.payload.items[1].count==2)
request.state.entities["1"].demo.tools=json.array({})
result=career.check(doc,request)
assert(result.payload.equipment[2].count==5)
request.state.raid.player_state="Dead";result=career.check(doc,request)
assert(result.ok and #result.payload.equipment==0 and #result.payload.skills==0)
assert(result.payload.xp==0 and result.payload.points==0)
owner.state="ready";request.kind="start";doc.career=false
assert(career.check(doc,request).error=="career_not_configured")
''')
        with tempfile.TemporaryDirectory(prefix="hunter-career-") as folder:
            path = Path(folder) / "test.lua"
            path.write_text("\n".join(source), encoding="utf-8")
            run = subprocess.run([str(gameplay.luax_cli()), "--profile", "strict", str(path)],
                                 capture_output=True, timeout=60)
        self.assertEqual(run.returncode, 0, run.stderr.decode("utf-8", errors="replace"))


if __name__ == "__main__":
    unittest.main()
