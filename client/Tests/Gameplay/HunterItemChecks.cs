using System;
using System.Collections.Generic;
using System.IO;
using BigWorld.Gameplay;

public static class HunterItemChecks
{
    public static void Run(Action<bool, string> check, Func<string, HunterCareerData> decode,
        Func<HunterCareerData, string> encode, string directory)
    {
        Directory.CreateDirectory(directory);
        var career = HunterCareer.CreateNew(); string id = career.Selected.Id;
        check(career.Selected.Items.Count == 0 && career.WarehouseItems.Count == 0, "New hunters and warehouses do not invent free tools");
        check(!career.CanBuyItem((HunterItemId)99) && !career.EquipItem(id, HunterItemId.FirstAid) &&
            !career.ConsumeItem(id, 0, HunterItemId.FirstAid), "Unknown, unowned and camp-use item commands are rejected");
        check(career.BuyItem(HunterItemId.FirstAid) && career.Credits == 460 && career.WarehouseItems[0].Uses == 3,
            "A purchased medical tool costs forty credits and enters the warehouse with three uses");
        career.Recruit(); string other = career.Selected.Id;
        check(!career.EquipItem(id, HunterItemId.FirstAid) && career.EquipItem(other, HunterItemId.FirstAid) && career.WarehouseItems.Count == 0,
            "Only the selected hunter can equip stored items and the copy leaves storage");
        career.Select(id); career.BuyItem(HunterItemId.FirstAid); career.EquipItem(id, HunterItemId.FirstAid);
        career.BuyItem(HunterItemId.FirstAid);
        check(!career.CanEquipItem(id, HunterItemId.FirstAid), "Regular tools cannot occupy duplicate slots");
        career.BuyItem(HunterItemId.Knife); career.EquipItem(id, HunterItemId.Knife);
        career.BuyItem(HunterItemId.ThrowingKnife);
        check(!career.CanEquipItem(id, HunterItemId.ThrowingKnife) && career.Selected.Weight == 3,
            "Two regular tools fill their category without changing the gun weight budget");
        career.BuyItem(HunterItemId.HealthShot); career.EquipItem(id, HunterItemId.HealthShot);
        career.BuyItem(HunterItemId.HealthShot); career.EquipItem(id, HunterItemId.HealthShot);
        check(career.Selected.Items.Count == 4 && !career.CanEquipItem(id, HunterItemId.HealthShot),
            "Two identical consumables are allowed in their own two slots");
        var selected = career.Selected; selected.Items[0].Uses = 0; selected.Items.Clear();
        var snapshot = career.Snapshot(); snapshot.Items[0].Uses = 0; snapshot.Hunters[0].Items[0].Uses = 0;
        check(career.Selected.Items[0].Uses == 3 && career.WarehouseItems[0].Uses == 3,
            "Selected hunters, warehouse items and serialized snapshots do not expose mutable inventory");
        career.BeginRun();
        check(!career.BuyItem(HunterItemId.Knife) && !career.EquipItem(id, HunterItemId.ThrowingKnife) &&
            !career.UnequipItem(id, 0, HunterItemId.FirstAid) && !career.ConsumeItem(other, 0, HunterItemId.FirstAid),
            "Deployment blocks camp trades and consumption by a reserve hunter");
        check(!career.ConsumeItem(id, -1, HunterItemId.FirstAid) && !career.ConsumeItem(id, 0, HunterItemId.HealthShot) &&
            !career.ConsumeItem(id, 1, HunterItemId.Knife), "Invalid slots, changed identities and unlimited melee tools cannot spend charges");
        check(career.ConsumeItem(id, 0, HunterItemId.FirstAid) && career.Selected.Items[0].Uses == 2 && career.Selected.Items.Count == 4,
            "One medical-tool use decrements exactly once and preserves its slot");
        career.ConsumeItem(id, 0, HunterItemId.FirstAid); career.ConsumeItem(id, 0, HunterItemId.FirstAid);
        check(!career.ConsumeItem(id, 0, HunterItemId.FirstAid) && career.Selected.Items[0].Uses == 0 && career.Selected.Items.Count == 4,
            "An exhausted regular tool stays equipped and cannot go negative");
        check(career.ConsumeItem(id, 3, HunterItemId.HealthShot) && career.Selected.Items.Count == 3 &&
            !career.ConsumeItem(id, 3, HunterItemId.HealthShot), "The last consumable disappears and a repeated stale slot is rejected");
        career.Settle(CareerOutcome.Extracted, 250, 100);
        check(career.Selected.Items[0].Uses == 0 && career.Selected.Items.Count == 3,
            "Extraction retains remaining inventory without recharging tools or restoring consumed items");
        check(!career.UnequipItem(other, 0, HunterItemId.FirstAid) && career.UnequipItem(id, 0, HunterItemId.FirstAid) &&
            career.WarehouseItems.Exists(s => s.Id == HunterItemId.FirstAid && s.Uses == 0),
            "Unloading is identity-checked and retains an exhausted tool in storage");
        career.EquipItem(id, HunterItemId.FirstAid);
        check(career.Selected.Items[2].Uses == 3 && career.WarehouseItems.Exists(s => s.Id == HunterItemId.FirstAid && s.Uses == 0),
            "Equipping picks the fullest owned copy without refreshing the exhausted one");
        career.BeginRun(); career.ConsumeItem(id, 2, HunterItemId.FirstAid); career.Settle(CareerOutcome.Extracted, 0, 800);
        int stock = career.WarehouseItems.Count;
        check(career.Retire(id) && career.WarehouseItems.Count == stock + 3 &&
            career.WarehouseItems.Exists(s => s.Id == HunterItemId.FirstAid && s.Uses == 2) && career.Selected.Id == other,
            "Retirement returns all remaining items including partial tools and consumables");
        check(career.Selected.Items.Count == 1 && career.Selected.Items[0].Uses == 3,
            "Another hunter's medical tool is independent of the retired hunter");
        career.Toggle(HunterEquipment.Pistol); stock = career.WarehouseItems.Count;
        career.BeginRun(); career.Settle(CareerOutcome.Lost, 0);
        check(career.HunterCount == 0 && career.WarehouseItems.Count == stock,
            "Permanent death loses carried tools while the warehouse survives");

        var invalid = HunterCareer.CreateNew().Snapshot();
        invalid.Items = null; check(Reject(invalid), "Version six rejects a missing warehouse instead of inventing supplies");
        invalid = HunterCareer.CreateNew().Snapshot(); invalid.Hunters[0].Items = null;
        check(Reject(invalid), "Version six rejects a missing hunter item list");
        invalid = HunterCareer.CreateNew().Snapshot(); invalid.Items.Add(new HunterItemStack { Id = (HunterItemId)999, Uses = 1 });
        check(Reject(invalid), "Unknown saved item identities are invalid");
        invalid.Items[0] = new HunterItemStack { Id = HunterItemId.FirstAid, Uses = 4 };
        check(Reject(invalid), "Saved tool charges cannot exceed their configured capacity");
        invalid.Items[0] = new HunterItemStack { Id = HunterItemId.FirstAid, Uses = -1 };
        check(Reject(invalid), "Negative saved tool charges are invalid");
        invalid.Items[0] = new HunterItemStack { Id = HunterItemId.HealthShot, Uses = 0 };
        check(Reject(invalid), "Empty consumables cannot persist as occupied slots");
        invalid.Items.Clear(); invalid.Hunters[0].Items.Add(new HunterItemStack { Id = HunterItemId.Knife, Uses = 1 });
        invalid.Hunters[0].Items.Add(new HunterItemStack { Id = HunterItemId.Knife, Uses = 1 });
        check(Reject(invalid), "Duplicate regular tools in a saved loadout are invalid");
        invalid.Hunters[0].Items.Clear();
        for (int i = 0; i < 3; i++) invalid.Hunters[0].Items.Add(new HunterItemStack { Id = HunterItemId.HealthShot, Uses = 1 });
        check(Reject(invalid), "Consumable capacity is enforced even when regular slots are free");
        invalid.Hunters[0].Items.Clear();
        for (int i = 0; i < HunterItems.WarehouseLimit; i++) invalid.Items.Add(new HunterItemStack { Id = HunterItemId.Knife, Uses = 1 });
        var full = new HunterCareer(invalid);
        check(!full.CanBuyItem(HunterItemId.Knife), "A full warehouse cannot accept purchases");
        invalid.Hunters[0].Experience = HunterProgression.MaximumExperience;
        invalid.Hunters[0].Items.Add(new HunterItemStack { Id = HunterItemId.Knife, Uses = 1 });
        full = new HunterCareer(invalid);
        check(!full.CanRetireSelected && !full.UnequipItem(full.Selected.Id, 0, HunterItemId.Knife),
            "Retirement and unloading cannot overflow the warehouse or discard items");

        string path = Path.Combine(directory, "items.json");
        var store = new HunterCareerStore(path, decode, encode); store.Load(); id = store.Current.Selected.Id;
        string before = File.ReadAllText(path);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            bool failed = false; try { store.Change(c => c.BuyItem(HunterItemId.FirstAid)); } catch (IOException) { failed = true; }
            check(failed && store.Current.Credits == 500 && store.Current.WarehouseItems.Count == 0 && File.ReadAllText(path) == before,
                "Failed item purchases neither charge currency nor publish inventory");
        }
        store.Change(c => c.BuyItem(HunterItemId.FirstAid)); store.Change(c => c.EquipItem(id, HunterItemId.FirstAid));
        store.Change(c => c.BeginRun()); before = File.ReadAllText(path);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            bool failed = false; try { store.Change(c => c.ConsumeItem(id, 0, HunterItemId.FirstAid)); } catch (IOException) { failed = true; }
            check(failed && store.Current.Selected.Items[0].Uses == 3 && File.ReadAllText(path) == before,
                "Failed item consumption retains the live and disk charges for a retry");
        }
        check(store.Change(c => c.ConsumeItem(id, 0, HunterItemId.FirstAid)) && store.Current.Selected.Items[0].Uses == 2,
            "Retrying item consumption saves exactly one use");
        store.Change(c => c.Settle(CareerOutcome.Extracted, 0));
        var reload = new HunterCareerStore(path, decode, encode); reload.Load();
        check(reload.Current.Selected.Items[0].Uses == 2 && reload.Current.WarehouseItems.Count == 0,
            "Partial item charges survive extraction and a fresh store reload");
        reload.Change(c => c.BeginRun());
        var interrupted = new HunterCareerStore(path, decode, encode); interrupted.Load();
        check(interrupted.Current.HunterCount == 0 && interrupted.Current.LastOutcome == CareerOutcome.Interrupted,
            "Restarting an unfinished run cannot rescue carried items");

        string legacyPath = Path.Combine(directory, "v5.json");
        string legacy = "{\"Version\":5,\"Credits\":321,\"Rifles\":2,\"Pistols\":1,\"SelectedId\":\"legacy\",\"ActiveId\":\"\",\"Recruits\":1,\"Hunters\":[{\"Id\":\"legacy\",\"Name\":\"旧猎人\",\"Rifle\":true,\"Experience\":200,\"SkillPoints\":1,\"Skills\":[1],\"FieldSkillValue\":0}]}";
        File.WriteAllText(legacyPath, legacy);
        var migration = new HunterCareerStore(legacyPath, decode, encode);
        using (var locked = new FileStream(legacyPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            bool failed = false; try { migration.Load(); } catch (IOException) { failed = true; }
            check(failed && migration.Current == null && File.ReadAllText(legacyPath) == legacy,
                "Failed v5 inventory migration preserves the original and publishes no account");
        }
        migration.Load();
        check(migration.Migrated && migration.Current.Snapshot().Version == HunterCareer.CurrentVersion &&
            migration.Current.Credits == 321 && migration.Current.Selected.Items.Count == 0 && migration.Current.WarehouseItems.Count == 0 &&
            migration.Current.Selected.SkillPoints == 1 && migration.Current.Selected.HasSkill(HunterSkillId.EfficientMelee) &&
            File.ReadAllText(legacyPath + ".bak") == legacy, "Version five gains empty inventory lists while retaining all growth and its original backup");
    }
    private static bool Reject(HunterCareerData data)
    { try { new HunterCareer(data); return false; } catch (ArgumentException) { return true; } }
}
