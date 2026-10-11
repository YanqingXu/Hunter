using System;
using System.Collections.Generic;

namespace BigWorld.Gameplay
{
    public enum HunterEquipment { Rifle, Pistol }
    public enum CareerOutcome { None, Extracted, Lost, Interrupted, Retired }

    [Serializable]
    public sealed class HunterRecord
    {
        public string Id;
        public string Name;
        // Zero stays invalid in current saves; only legacy migration supplies a default.
        public HunterTemplateId TemplateId;
        public bool Rifle;
        public bool Pistol;
        public int Experience;
        public int SkillPoints;
        // Historical value of field-acquired skills, including those later removed. Not spendable points.
        public int FieldSkillValue;
        public List<HunterSkillId> Skills = new List<HunterSkillId>();
        public List<HunterItemStack> Items = new List<HunterItemStack>();
        // Null is intentional: a missing field in a v9 save is invalid, not an implicit migration.
        public float[] HealthSegments;
        public bool HasSkill(HunterSkillId skill) { return Skills != null && Skills.Contains(skill); }
        public int Level { get { return HunterProgression.Level(Experience); } }
        public bool IsMaxLevel { get { return Level == HunterProgression.MaximumLevel; } }
        public int LevelExperience { get { return IsMaxLevel ? 0 : Experience % HunterProgression.ExperiencePerLevel; } }
        public int Weight { get { return (Rifle ? 2 : 0) + (Pistol ? 1 : 0); } }
        public HunterTemplateDefinition Template { get { return HunterTemplates.Find(TemplateId); } }
        public int CarriedSkillValue
        {
            get
            {
                int value = 0;
                if (Skills != null) foreach (var id in Skills) { var skill = HunterSkills.Find(id); if (skill != null) value += skill.Cost; }
                return value;
            }
        }
        public HunterRecord Copy()
        {
            var copy = (HunterRecord)MemberwiseClone();
            copy.Skills = Skills == null ? null : new List<HunterSkillId>(Skills);
            copy.Items = HunterItems.Copy(Items);
            copy.HealthSegments = HealthSegments == null ? null : (float[])HealthSegments.Clone();
            return copy;
        }
    }

    [Serializable]
    public sealed class HunterCareerData
    {
        public int Version;
        public int Credits;
        public int Rifles;
        public int Pistols;
        public string SelectedId;
        public string ActiveId;
        public int Recruits;
        public int Extractions;
        public int Losses;
        public CareerOutcome LastOutcome;
        public int LastReward;
        public int LastExperience;
        public int LastLevelsGained;
        public int AccountExperience;
        public int Retirements;
        public List<HunterRecord> Hunters;
        public List<HunterItemStack> Items = new List<HunterItemStack>();
    }

    /// <summary>Account inventory and roster. Exposes copies so all changes pass through validated commands.</summary>
    public sealed class HunterCareer
    {
        // Version 10 persists the template and enforces its equipment and learned-skill capacity.
        public const int CurrentVersion = 10;
        public const float MaximumHealth = 100;
        public static float[] DefaultHealthSegments() { return new float[] { 25, 25, 25, 25 }; }
        public const int FieldSkillValueLimit = 1000000;
        public const int RecruitCost = HunterTemplates.PaidRecruitCost;
        public const int RifleCost = 150;
        public const int PistolCost = 75;
        public const int RosterLimit = HunterTemplates.RosterLimit;
        private const int NumberLimit = 1000000;
        private const int CreditLimit = 1000000000;
        private readonly HunterCareerData data;
        public int Credits { get { return data.Credits; } }
        public int Rifles { get { return data.Rifles; } }
        public int Pistols { get { return data.Pistols; } }
        public int HunterCount { get { return data.Hunters.Count; } }
        public bool InRun { get { return !string.IsNullOrEmpty(data.ActiveId); } }
        public CareerOutcome LastOutcome { get { return data.LastOutcome; } }
        public int LastReward { get { return data.LastReward; } }
        public int LastExperience { get { return data.LastExperience; } }
        public int LastLevelsGained { get { return data.LastLevelsGained; } }
        public int AccountExperience { get { return data.AccountExperience; } }
        public int AccountLevel { get { return HunterAccountProgression.Level(data.AccountExperience); } }
        public int AccountLevelExperience { get { return HunterAccountProgression.LevelExperience(data.AccountExperience); } }
        public int AccountExperienceToNextLevel { get { return HunterAccountProgression.ExperienceToNextLevel(data.AccountExperience); } }
        public bool IsAccountMaxLevel { get { return AccountLevel == HunterAccountProgression.MaximumLevel; } }
        public int Retirements { get { return data.Retirements; } }
        public List<HunterItemStack> WarehouseItems { get { return HunterItems.Copy(data.Items); } }
        public HunterRecord Selected { get { var hunter = Find(data.SelectedId); return hunter == null ? null : hunter.Copy(); } }
        public bool CanDeploy { get { var hunter = Selected; return !InRun && hunter != null && hunter.Weight > 0 &&
            HunterTemplates.CanCarryWeapons(hunter.TemplateId, hunter.Weight) && HunterTemplates.CanCarrySkills(hunter.TemplateId, hunter.CarriedSkillValue); } }
        public bool CanRecruit { get { return !InRun && HunterTemplates.CanAcquire(HunterTemplateId.Recruited, HunterCount, Credits) && data.Recruits < NumberLimit; } }
        public bool CanClaimRelief { get { return !InRun && HunterTemplates.CanAcquire(HunterTemplateId.Relief, HunterCount, Credits) && data.Recruits < NumberLimit; } }
        public bool CanRetireSelected
        {
            get
            {
                var hunter = Selected;
                return !InRun && hunter != null && hunter.IsMaxLevel && data.Retirements < NumberLimit &&
                    data.AccountExperience <= CreditLimit - HunterProgression.RetirementExperience &&
                    (!hunter.Rifle || Rifles < NumberLimit) && (!hunter.Pistol || Pistols < NumberLimit) &&
                    data.Items.Count + hunter.Items.Count <= HunterItems.WarehouseLimit;
            }
        }

        public HunterCareer(HunterCareerData source)
        {
            Validate(source);
            data = Copy(source);
            if (data.Version == 1)
            {
                // Version 1 has no growth history; do not retroactively invent rewards or revive lost hunters.
                foreach (var hunter in data.Hunters) { hunter.Experience = 0; hunter.SkillPoints = 0; }
                data.AccountExperience = data.Retirements = data.LastExperience = data.LastLevelsGained = 0;
            }
            if (data.Version < 3)
                foreach (var hunter in data.Hunters) hunter.Skills = new List<HunterSkillId>();
            if (data.Version < 4)
                foreach (var hunter in data.Hunters) hunter.FieldSkillValue = 0;
            if (data.Version < 6)
            {
                data.Items = new List<HunterItemStack>();
                foreach (var hunter in data.Hunters) hunter.Items = new List<HunterItemStack>();
            }
            if (data.Version < 9)
                foreach (var hunter in data.Hunters) hunter.HealthSegments = DefaultHealthSegments();
            // Earlier saves have no trustworthy acquisition history. Preserve their full legal loadout.
            if (data.Version < 10)
                foreach (var hunter in data.Hunters) hunter.TemplateId = HunterTemplateId.Recruited;
            data.Version = CurrentVersion;
        }

        public static HunterCareer CreateNew()
        {
            var first = new HunterRecord { Id = Guid.NewGuid().ToString("N"), Name = "猎人 001", Rifle = true, Pistol = true,
                TemplateId = HunterTemplateId.Recruited, HealthSegments = DefaultHealthSegments() };
            return new HunterCareer(new HunterCareerData {
                Version = CurrentVersion, Credits = 500, Rifles = 1, Pistols = 1, Recruits = 1,
                SelectedId = first.Id, ActiveId = "", Hunters = new List<HunterRecord> { first }
            });
        }

        public HunterCareerData Snapshot() { return Copy(data); }
        public HunterRecord GetHunter(int index) { return data.Hunters[index].Copy(); }
        private HunterRecord Find(string id) { return data.Hunters.Find(item => item.Id == id); }

        public bool Select(string id)
        {
            if (InRun || Find(id) == null || data.SelectedId == id) return false;
            data.SelectedId = id; return true;
        }

        public bool Recruit()
        {
            if (!CanRecruit) return false;
            data.Credits -= RecruitCost; AddHunter(HunterTemplateId.Recruited); return true;
        }

        // Recovery is only available with an empty roster; equipment already in storage is used first.
        public bool ClaimRelief()
        {
            if (!CanClaimRelief) return false;
            var hunter = AddHunter(HunterTemplateId.Relief);
            if (data.Pistols > 0) { data.Pistols--; hunter.Pistol = true; }
            else if (data.Rifles > 0) { data.Rifles--; hunter.Rifle = true; }
            else if (data.Credits >= PistolCost) { data.Credits -= PistolCost; hunter.Pistol = true; }
            else hunter.Pistol = true;
            return true;
        }

        private HunterRecord AddHunter(HunterTemplateId template)
        {
            data.Recruits++;
            var hunter = new HunterRecord { Id = Guid.NewGuid().ToString("N"), Name = "猎人 " + data.Recruits.ToString("000"),
                TemplateId = template, HealthSegments = DefaultHealthSegments() };
            data.Hunters.Add(hunter); data.SelectedId = hunter.Id; return hunter;
        }

        public bool SetHealthLayout(string hunterId, float[] segments)
        {
            var hunter = Find(hunterId);
            if (InRun || hunter == null || hunterId != data.SelectedId) return false;
            var layout = new HunterHealthLayout(MaximumHealth, hunter.HealthSegments);
            if (!layout.TrySet(segments)) return false;
            hunter.HealthSegments = layout.Segments; return true;
        }

        public bool CanBuy(HunterEquipment equipment)
        {
            return !InRun && (equipment == HunterEquipment.Rifle ? Credits >= RifleCost && Rifles < NumberLimit :
                equipment == HunterEquipment.Pistol && Credits >= PistolCost && Pistols < NumberLimit);
        }
        public bool Buy(HunterEquipment equipment)
        {
            if (!CanBuy(equipment)) return false;
            if (equipment == HunterEquipment.Rifle) { data.Credits -= RifleCost; data.Rifles++; }
            else { data.Credits -= PistolCost; data.Pistols++; }
            return true;
        }

        public bool CanToggle(HunterEquipment equipment)
        {
            var hunter = Selected;
            if (InRun || hunter == null) return false;
            if (equipment == HunterEquipment.Rifle) return hunter.Rifle ? Rifles < NumberLimit : Rifles > 0 && HunterTemplates.CanCarryWeapons(hunter.TemplateId, hunter.Weight + 2);
            return equipment == HunterEquipment.Pistol && (hunter.Pistol ? Pistols < NumberLimit : Pistols > 0 && HunterTemplates.CanCarryWeapons(hunter.TemplateId, hunter.Weight + 1));
        }
        public bool Toggle(HunterEquipment equipment)
        {
            if (!CanToggle(equipment)) return false;
            var hunter = Find(data.SelectedId);
            if (equipment == HunterEquipment.Rifle) { data.Rifles += hunter.Rifle ? 1 : -1; hunter.Rifle = !hunter.Rifle; }
            else { data.Pistols += hunter.Pistol ? 1 : -1; hunter.Pistol = !hunter.Pistol; }
            return true;
        }

        public bool CanBuyItem(HunterItemId id)
        {
            var item = HunterItems.Find(id);
            return !InRun && item != null && Credits >= item.Cost && data.Items.Count < HunterItems.WarehouseLimit;
        }
        public bool BuyItem(HunterItemId id)
        {
            if (!CanBuyItem(id)) return false;
            var item = HunterItems.Find(id);
            data.Credits -= item.Cost; data.Items.Add(new HunterItemStack { Id = id, Uses = item.Capacity }); return true;
        }
        public bool CanEquipItem(string hunterId, HunterItemId id)
        {
            var hunter = Find(hunterId); var item = HunterItems.Find(id);
            if (InRun || hunterId != data.SelectedId || hunter == null || item == null || !data.Items.Exists(s => s.Id == id)) return false;
            int count = hunter.Items.FindAll(s => HunterItems.Find(s.Id).Consumable == item.Consumable).Count;
            return count < (item.Consumable ? HunterItems.ConsumableSlots : HunterItems.ToolSlots) &&
                (item.Consumable || !hunter.Items.Exists(s => s.Id == id));
        }
        public bool EquipItem(string hunterId, HunterItemId id)
        {
            if (!CanEquipItem(hunterId, id)) return false;
            // Use the fullest stored copy first; unloading never replenishes its charges.
            HunterItemStack best = null;
            foreach (var stack in data.Items) if (stack.Id == id && (best == null || stack.Uses > best.Uses)) best = stack;
            data.Items.Remove(best); Find(hunterId).Items.Add(best); return true;
        }
        public bool UnequipItem(string hunterId, int index, HunterItemId expected)
        {
            var hunter = Find(hunterId);
            if (InRun || hunter == null || hunterId != data.SelectedId || index < 0 || index >= hunter.Items.Count ||
                hunter.Items[index].Id != expected || data.Items.Count >= HunterItems.WarehouseLimit) return false;
            data.Items.Add(hunter.Items[index]); hunter.Items.RemoveAt(index); return true;
        }
        public bool ConsumeItem(string hunterId, int index, HunterItemId expected)
        {
            var hunter = Find(hunterId);
            if (!InRun || hunterId != data.ActiveId || hunter == null || index < 0 || index >= hunter.Items.Count) return false;
            var stack = hunter.Items[index]; var item = HunterItems.Find(stack.Id);
            if (stack.Id != expected || stack.Uses <= 0 || item.Kind == HunterItemKind.Melee) return false;
            stack.Uses--;
            if (item.Consumable && stack.Uses == 0) hunter.Items.RemoveAt(index);
            return true;
        }

        public bool CanAcquireConsumable(string hunterId, HunterItemId id)
        {
            var hunter = Find(hunterId); var item = HunterItems.Find(id);
            return InRun && data.ActiveId == hunterId && item != null && item.Consumable && HunterItemSupply.HasConsumableSlot(hunter);
        }
        public bool AcquireConsumable(string hunterId, HunterItemId id)
        {
            if (!CanAcquireConsumable(hunterId, id)) return false;
            Find(hunterId).Items.Add(new HunterItemStack { Id = id, Uses = HunterItems.Find(id).Capacity }); return true;
        }
        public bool ApplyItemSupply(HunterItemSupplyPlan plan)
        {
            if (!InRun || plan == null || data.ActiveId != plan.HunterId || !HunterItemSupply.Matches(Find(plan.HunterId), plan)) return false;
            if (plan.Index < 0) return AcquireConsumable(plan.HunterId, plan.Item);
            Find(plan.HunterId).Items[plan.Index].Uses += plan.Amount; return true;
        }

        public bool CanBuySkill(string hunterId, HunterSkillId id)
        {
            var hunter = Find(hunterId); var skill = HunterSkills.Find(id);
            return !InRun && hunterId == data.SelectedId && hunter != null && skill != null &&
                !hunter.HasSkill(id) && hunter.SkillPoints >= skill.Cost && HunterTemplates.CanAddSkill(hunter.TemplateId, hunter.CarriedSkillValue, skill.Cost);
        }

        public bool BuySkill(string hunterId, HunterSkillId id)
        {
            if (!CanBuySkill(hunterId, id)) return false;
            var hunter = Find(hunterId);
            hunter.SkillPoints -= HunterSkills.Find(id).Cost; hunter.Skills.Add(id); return true;
        }

        public bool CanRemoveSkill(string hunterId, HunterSkillId id)
        {
            var hunter = Find(hunterId);
            return !InRun && hunterId == data.SelectedId && hunter != null && HunterSkills.Find(id) != null && hunter.HasSkill(id);
        }

        public bool RemoveSkill(string hunterId, HunterSkillId id)
        {
            if (!CanRemoveSkill(hunterId, id)) return false;
            var hunter = Find(hunterId);
            hunter.Skills.Remove(id); hunter.SkillPoints += HunterSkills.Find(id).Refund; return true;
        }

        public bool CanAcquireFieldSkill(string hunterId, HunterSkillId id)
        {
            var hunter = Find(hunterId); var skill = HunterSkills.Find(id);
            return InRun && data.ActiveId == hunterId && hunter != null && skill != null &&
                !hunter.HasSkill(id) && hunter.FieldSkillValue <= FieldSkillValueLimit - skill.Cost &&
                HunterTemplates.CanAddSkill(hunter.TemplateId, hunter.CarriedSkillValue, skill.Cost);
        }

        public bool AcquireFieldSkill(string hunterId, HunterSkillId id)
        {
            if (!CanAcquireFieldSkill(hunterId, id)) return false;
            var hunter = Find(hunterId);
            hunter.Skills.Add(id); hunter.FieldSkillValue += HunterSkills.Find(id).Cost;
            return true;
        }

        public bool CanConsumeOnceSkill(string hunterId, HunterSkillId id)
        {
            var hunter = Find(hunterId); var skill = HunterSkills.Find(id);
            return InRun && data.ActiveId == hunterId && hunter != null && skill != null &&
                skill.Lifetime == HunterSkillLifetime.Once && hunter.HasSkill(id);
        }

        public bool ConsumeOnceSkill(string hunterId, HunterSkillId id)
        {
            if (!CanConsumeOnceSkill(hunterId, id)) return false;
            // Using a skill grants no camp refund and retains its historical field-acquisition value.
            Find(hunterId).Skills.Remove(id); return true;
        }

        public bool CanRevive(string hunterId)
        {
            var hunter = Find(hunterId);
            return InRun && data.ActiveId == hunterId && hunter != null && hunter.HasSkill(HunterSkillId.Necromancer);
        }

        public bool ConsumeRevival(string hunterId)
        {
            if (!CanRevive(hunterId)) return false;
            // Automatic consumption is not a camp removal: no refund, reward or mission settlement.
            Find(hunterId).Skills.RemoveAll(id => HunterSkills.Find(id).Lifetime == HunterSkillLifetime.Death);
            return true;
        }

        public bool BeginRun()
        {
            if (!CanDeploy) return false;
            data.ActiveId = data.SelectedId; data.LastOutcome = CareerOutcome.None;
            data.LastReward = data.LastExperience = data.LastLevelsGained = 0; return true;
        }

        public bool Settle(CareerOutcome outcome, int reward, int experience = 0)
        {
            if (!InRun || (outcome != CareerOutcome.Extracted && outcome != CareerOutcome.Lost && outcome != CareerOutcome.Interrupted) ||
                reward < 0 || reward > CreditLimit - Credits || experience < 0 || (outcome != CareerOutcome.Extracted && (reward != 0 || experience != 0)) ||
                data.Losses >= NumberLimit || data.Extractions >= NumberLimit) return false;
            data.LastExperience = data.LastLevelsGained = 0;
            if (outcome == CareerOutcome.Extracted)
            {
                var hunter = Find(data.ActiveId);
                int oldLevel = hunter.Level;
                data.LastExperience = Math.Min(experience, HunterProgression.MaximumExperience - hunter.Experience);
                hunter.Experience += data.LastExperience;
                data.LastLevelsGained = hunter.Level - oldLevel;
                hunter.SkillPoints += data.LastLevelsGained;
                data.Credits += reward; data.Extractions++;
            }
            else
            {
                data.Hunters.Remove(Find(data.ActiveId)); data.Losses++;
                data.SelectedId = HunterCount == 0 ? "" : data.Hunters[0].Id;
            }
            data.ActiveId = ""; data.LastOutcome = outcome; data.LastReward = reward; return true;
        }

        // The requested identity must still be selected: repeated/stale confirmations cannot retire the next hunter.
        public bool Retire(string hunterId)
        {
            if (!CanRetireSelected || data.SelectedId != hunterId) return false;
            var hunter = Find(hunterId);
            if (hunter.Rifle) data.Rifles++;
            if (hunter.Pistol) data.Pistols++;
            data.Items.AddRange(HunterItems.Copy(hunter.Items));
            data.AccountExperience += HunterProgression.RetirementExperience; data.Retirements++;
            data.Hunters.Remove(hunter);
            data.SelectedId = HunterCount == 0 ? "" : data.Hunters[0].Id;
            data.LastOutcome = CareerOutcome.Retired;
            data.LastReward = data.LastExperience = data.LastLevelsGained = 0;
            return true;
        }

        public static void Validate(HunterCareerData value)
        {
            if (value == null) throw new ArgumentException("存档内容为空。");
            if (value.Version > CurrentVersion) throw new NotSupportedException("不支持的存档版本：" + value.Version);
            if (value.Version < 1) throw new ArgumentException("存档版本缺失或无效。");
            if (value.Hunters == null || value.Hunters.Count > RosterLimit || value.Credits < 0 || value.Credits > CreditLimit ||
                value.Rifles < 0 || value.Rifles > NumberLimit || value.Pistols < 0 || value.Pistols > NumberLimit ||
                value.Recruits < value.Hunters.Count || value.Recruits > NumberLimit || value.Losses < 0 || value.Losses > NumberLimit ||
                value.Extractions < 0 || value.Extractions > NumberLimit || value.LastReward < 0 || value.LastReward > CreditLimit ||
                !Enum.IsDefined(typeof(CareerOutcome), value.LastOutcome) || (value.LastOutcome != CareerOutcome.Extracted && value.LastReward != 0))
                throw new ArgumentException("存档数值不合法。");
            if (value.Version == 1 && value.LastOutcome == CareerOutcome.Retired) throw new ArgumentException("旧存档结算状态不合法。");
            if (value.Version >= 2 && (value.AccountExperience < 0 || value.AccountExperience > CreditLimit ||
                value.Retirements < 0 || value.Retirements > NumberLimit || value.LastExperience < 0 || value.LastExperience > HunterProgression.MaximumExperience ||
                value.LastLevelsGained < 0 || value.LastLevelsGained >= HunterProgression.MaximumLevel ||
                value.LastLevelsGained > (value.LastExperience + HunterProgression.ExperiencePerLevel - 1) / HunterProgression.ExperiencePerLevel ||
                (value.LastOutcome != CareerOutcome.Extracted && (value.LastExperience != 0 || value.LastLevelsGained != 0))))
                throw new ArgumentException("存档成长数据不合法。");
            var ids = new HashSet<string>();
            foreach (var hunter in value.Hunters)
                if (hunter == null || string.IsNullOrWhiteSpace(hunter.Id) || !ids.Add(hunter.Id) ||
                    string.IsNullOrWhiteSpace(hunter.Name) || hunter.Name.Length > 40)
                    throw new ArgumentException("存档猎人信息不合法。");
            if (value.Version >= 2)
                foreach (var hunter in value.Hunters)
                    if (hunter.Experience < 0 || hunter.Experience > HunterProgression.MaximumExperience ||
                        hunter.SkillPoints < 0 || hunter.SkillPoints > hunter.Level - 1 + (value.Version >= 4 ? (long)hunter.FieldSkillValue : 0))
                        throw new ArgumentException("猎人经验或技能点不合法。");
            if (value.Version >= 4)
                foreach (var hunter in value.Hunters)
                    if (hunter.FieldSkillValue < 0 || hunter.FieldSkillValue > FieldSkillValueLimit)
                        throw new ArgumentException("猎人局内技能获取记录不合法。");
            if (value.Version >= 3)
                foreach (var hunter in value.Hunters)
                {
                    if (hunter.Skills == null) throw new ArgumentException("猎人技能列表缺失。");
                    var skills = new HashSet<HunterSkillId>();
                    int spent = 0;
                    foreach (var id in hunter.Skills)
                    {
                        var skill = HunterSkills.Find(id);
                        if (skill == null || !skills.Add(id)) throw new ArgumentException("猎人技能未知或重复。");
                        spent += skill.Cost;
                    }
                    if ((long)spent + hunter.SkillPoints > hunter.Level - 1 + (value.Version >= 4 ? (long)hunter.FieldSkillValue : 0))
                        throw new ArgumentException("猎人技能超出升级和局内获取的总价值。");
                }
            if (value.Version >= 6)
            {
                HunterItems.Validate(value.Items, false);
                foreach (var hunter in value.Hunters) HunterItems.Validate(hunter.Items, true);
            }
            if (value.Version >= 9)
                foreach (var hunter in value.Hunters)
                    if (!HunterHealthLayout.IsAllowed(MaximumHealth, hunter.HealthSegments))
                        throw new ArgumentException("猎人血段缺失或不符合固定生命上限。");
            if (value.Version >= 10)
                foreach (var hunter in value.Hunters)
                    if (!HunterTemplates.CanCarryWeapons(hunter.TemplateId, hunter.Weight) ||
                        !HunterTemplates.CanCarrySkills(hunter.TemplateId, hunter.CarriedSkillValue))
                        throw new ArgumentException("猎人模板缺失、未知或携带容量超限。");
            if ((ids.Count == 0 ? !string.IsNullOrEmpty(value.SelectedId) : !ids.Contains(value.SelectedId)) ||
                (!string.IsNullOrEmpty(value.ActiveId) && (value.ActiveId != value.SelectedId || !ids.Contains(value.ActiveId) ||
                    value.Hunters.Find(item => item.Id == value.ActiveId).Weight == 0)))
                throw new ArgumentException("存档出战状态不合法。");
        }

        private static HunterCareerData Copy(HunterCareerData source)
        {
            var copy = new HunterCareerData {
                Version = source.Version, Credits = source.Credits, Rifles = source.Rifles, Pistols = source.Pistols,
                SelectedId = source.SelectedId, ActiveId = source.ActiveId, Recruits = source.Recruits,
                Extractions = source.Extractions, Losses = source.Losses, LastOutcome = source.LastOutcome, LastReward = source.LastReward,
                LastExperience = source.LastExperience, LastLevelsGained = source.LastLevelsGained,
                AccountExperience = source.AccountExperience, Retirements = source.Retirements,
                Hunters = new List<HunterRecord>(), Items = HunterItems.Copy(source.Items)
            };
            foreach (var hunter in source.Hunters) copy.Hunters.Add(hunter.Copy());
            return copy;
        }
    }
}
