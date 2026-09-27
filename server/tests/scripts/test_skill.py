# 用固定 Luax 真实执行天赋模块，验证有效配置、预算及复活参数且不依赖原生状态。
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / "export"))
import gameplay


# 构造仅用于测试的完整效果数值，不写回任何生产工作簿。
def sample():
    effects = {
        "1": [("revive_hp", "add", 25), ("revive_stamina", "add", 40)],
        "2": [("revive_full_hp", "add", 1)],
        "4": [("weight", "add", 2)],
        "5": [("run_speed", "mul_bp", 12000)],
        "7": [("melee_damage", "mul_bp", 15000)],
        "8": [("reserve", "add", 20)],
        "9": [("reload_ticks", "mul_bp", 5000)],
        "10": [("shot_ticks", "mul_bp", 5000)],
        "11": [("medkit_ticks", "mul_bp", 5000)],
        "12": [("medkit_heal", "mul_bp", 20000)],
        "13": [("throw_range", "mul_bp", 15000)],
    }
    skills = {}
    for key, values in effects.items():
        skills[key] = {"name": "测试天赋" + key, "target": "player", "category": "numeric",
                       "kind": "passive", "cost": 1,
                       "effects": [{"stat": stat, "op": op, "value": value}
                                   for stat, op, value in values]}
    skills["1"].update(category="mechanic", kind="once")
    skills["2"].update(category="mechanic", kind="death")
    return {"skills": skills, "key": "source-identity",
            "players": {"1": {"hp": 150, "stamina": 100, "weight": 3, "run_speed": 150,
                              "skill_points": 11}},
            "weapons": {"1": {"melee_damage": 50, "reserve": 30, "fire_ticks": 10,
                              "reload_ticks": 90}},
            "tools": {"1": {"kind": "medkit", "use_ticks": 120, "heal": 25},
                      "2": {"kind": "bomb", "throw_range": 10000}}}


class SkillTest(unittest.TestCase):
    # 每个用例使用独立解释器和临时脚本，直接加载真实模块工厂。
    def run_lua(self, body, doc=None):
        source = []
        for name, filename in (("state", "framework/state.lua"), ("skill", "game/skill.lua")):
            raw = (ROOT / "server/lua" / filename).read_text(encoding="utf-8")
            source.append("local " + name + "_factory = (function()\n" + raw + "\nend)()")
        source += ['local state = state_factory({})',
                   'local skill = skill_factory({["framework.state"] = state})',
                   "local doc = " + gameplay.literal(doc or sample()),
                   'local load = {player_cfg_id="1", skills=json.array({}),',
                   'weapons=json.array({{cfg_id="1"},{cfg_id="1"}}),',
                   'tools=json.array({"1","1"}),consumables=json.array({"2","2"})}', body]
        with tempfile.TemporaryDirectory(prefix="hunter-skill-") as folder:
            path = Path(folder) / "test.lua"
            path.write_text("\n".join(source), encoding="utf-8")
            run = subprocess.run([str(gameplay.luax_cli()), "--profile", "strict", str(path)],
                                 capture_output=True, timeout=60)
        self.assertEqual(run.returncode, 0, run.stderr.decode("utf-8", errors="replace"))

    # 首批十一项天赋实际影响对应属性，重复武器槽不能把同一配置应用两遍。
    def test_all_effects_and_source_isolation(self):
        self.run_lua('''
skill.validate(doc.skills)
load.skills = json.array({"13","12","11","10","9","8","7","5","4","2","1"})
local ids = assert(skill.check(doc,"1",load.skills))
assert(ids[1]=="1" and ids[11]=="13")
local cfg = skill.effective(doc,load)
assert(cfg.key==doc.key and cfg.players["1"].weight==5)
assert(cfg.players["1"].run_speed==180 and doc.players["1"].run_speed==150)
assert(cfg.weapons["1"].melee_damage==75 and cfg.weapons["1"].reserve==50)
assert(cfg.weapons["1"].fire_ticks==5 and cfg.weapons["1"].reload_ticks==45)
assert(cfg.tools["1"].use_ticks==60 and cfg.tools["1"].heal==50)
assert(cfg.tools["2"].throw_range==15000 and doc.weapons["1"].reserve==30)
local revived = assert(skill.revive(doc,load,ids))
assert(revived.hp==150 and revived.stamina==40 and revived.aggro_ticks==180)
assert(revived.skill_id=="1")
local plain = skill.effective(doc,load,json.array({}))
assert(plain.weapons["1"].reserve==30)
assert(skill.revive(doc,load,json.array({"2"}))==nil)
assert(skill.revive(doc,load,json.array({"1"})).hp==25)
''')

    # 重复、未知、超预算和未实现技能不得被接受，缺复活参数也不默认填补。
    def test_rejections(self):
        self.run_lua('''
assert(skill.check(doc,"1",json.array({"1","1"}))==nil)
assert(skill.check(doc,"1",json.array({"999"}))==nil)
doc.players["1"].skill_points=0
assert(skill.check(doc,"1",json.array({"1"}))==nil)
assert(skill.check(doc,"1",json.array({"1"}),true)~=nil)
assert(skill.check(doc,"1",json.array({"1","1"}),true)==nil)
doc.skills["1"].effects=json.array({{stat="revive_hp",op="add",value=25}})
assert(not pcall(skill.validate,doc.skills))
doc.skills["1"].effects=json.array({{stat="noise_range",op="add",value=5}})
assert(not pcall(skill.validate,doc.skills))
''')

    # 数值越界必须拒绝；移除技能后重算恢复基础值，且不修改原始内容。
    def test_bounds_and_recompute(self):
        self.run_lua('''
load.skills=json.array({"5"})
doc.skills["5"].effects[1].value=100000
assert(not pcall(skill.effective,doc,load))
assert(doc.players["1"].run_speed==150)
doc.skills["5"].effects[1].value=12000
assert(skill.effective(doc,load).players["1"].run_speed==180)
assert(skill.effective(doc,load,json.array({})).players["1"].run_speed==150)
load.skills=json.array({"11"})
doc.skills["11"].effects[1]={stat="medkit_ticks",op="add",value=36000}
assert(not pcall(skill.effective,doc,load))
doc.skills["11"].effects[1].value=-120
assert(not pcall(skill.effective,doc,load))
doc.skills["11"].effects[1].value=35880
assert(skill.effective(doc,load).tools["1"].use_ticks==36000)
load.skills=json.array({"13"})
doc.skills["13"].effects[1]={stat="throw_range",op="add",value=90001}
assert(not pcall(skill.effective,doc,load))
doc.skills["13"].effects[1].value=90000
assert(skill.effective(doc,load).tools["2"].throw_range==100000)
''')


if __name__ == "__main__":
    unittest.main()
