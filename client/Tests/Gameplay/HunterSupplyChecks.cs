using System;
using System.IO;
using BigWorld.Gameplay;

public static class HunterSupplyChecks
{
    public static void Run(Action<bool, string> check, Func<string, HunterCareerData> decode,
        Func<HunterCareerData, string> encode, string directory)
    {
        Directory.CreateDirectory(directory);
        var pool = new[] {
            new HunterConsumableWeight { Item = HunterItemId.HealthShot, Weight = 3 },
            new HunterConsumableWeight { Item = HunterItemId.Dynamite, Weight = 1 }
        };
        check(HunterItemSupply.TotalWeight(pool) == 4 &&
            HunterItemSupply.ChooseConsumable(pool, n => 0) == HunterItemId.HealthShot &&
            HunterItemSupply.ChooseConsumable(pool, n => 2) == HunterItemId.HealthShot &&
            HunterItemSupply.ChooseConsumable(pool, n => 3) == HunterItemId.Dynamite,
            "Consumable weighted intervals include every boundary of the configured three-to-one distribution");
        check(HunterItemSupply.ChooseConsumable(pool, n => -1) == 0 &&
            HunterItemSupply.ChooseConsumable(pool, n => n) == 0 && HunterItemSupply.ChooseConsumable(pool, null) == 0,
            "Out-of-range or absent random providers cannot create a reward");
        pool[0].Weight = 0;
        check(HunterItemSupply.ChooseConsumable(pool, n => 0) == HunterItemId.Dynamite, "Zero-weight consumables are never selected");
        pool[1].Weight = 0;
        check(HunterItemSupply.ChooseConsumable(pool, n => { throw new Exception("Unexpected roll"); }) == 0,
            "A zero-weight pool produces nothing without invoking random selection");
        pool[0].Weight = -1;
        check(HunterItemSupply.TotalWeight(pool) == 0 && HunterItemSupply.TotalWeight(null) == 0,
            "Negative weights and absent pools disable consumable rewards");
        pool[0].Weight = 1000001;
        check(HunterItemSupply.TotalWeight(pool) == 0, "Excessive weights cannot overflow the random total");
        pool[0] = new HunterConsumableWeight { Item = HunterItemId.FirstAid, Weight = 1 };
        check(HunterItemSupply.TotalWeight(pool) == 0, "Regular tools cannot be generated as consumable pickups");
        pool[0] = null;
        check(HunterItemSupply.TotalWeight(pool) == 0, "Null pool entries are rejected safely");
        pool[0] = new HunterConsumableWeight { Item = HunterItemId.HealthShot, Weight = 3 }; pool[1].Weight = 1;

        var career = HunterCareer.CreateNew(); string id = career.Selected.Id;
        check(!career.AcquireConsumable(id, HunterItemId.HealthShot) && !career.ApplyItemSupply(HunterItemSupply.Plan(career.Selected, pool, n => 0)),
            "Camp cannot grant field supplies through public inventory commands");
        career.BuyItem(HunterItemId.FirstAid); career.EquipItem(id, HunterItemId.FirstAid);
        career.BuyItem(HunterItemId.ThrowingKnife); career.EquipItem(id, HunterItemId.ThrowingKnife);
        career.Recruit(); string other = career.Selected.Id; career.Select(id); career.BeginRun();
        check(!career.AcquireConsumable(other, HunterItemId.HealthShot) && !career.AcquireConsumable(id, HunterItemId.Knife) &&
            !career.AcquireConsumable(id, (HunterItemId)99), "Only the deployed hunter can acquire known consumables");
        career.ConsumeItem(id, 0, HunterItemId.FirstAid); career.ConsumeItem(id, 0, HunterItemId.FirstAid);
        career.ConsumeItem(id, 1, HunterItemId.ThrowingKnife);
        string before = encode(career.Snapshot()); int randomLimit = -1;
        var first = HunterItemSupply.Plan(career.Selected, pool, n => { randomLimit = n; return 0; });
        var second = HunterItemSupply.Plan(career.Selected, pool, n => 1);
        check(randomLimit == 2 && first.Index == 0 && second.Index == 1 && first.Item == HunterItemId.FirstAid &&
            second.Item == HunterItemId.ThrowingKnife, "Multiple incomplete tools each occupy one equally selectable outcome");
        check(encode(career.Snapshot()) == before && first.Amount == 1 && first.ExpectedUses == 1,
            "Planning supply is side-effect free and gives the tool's fixed increment instead of filling it");
        check(HunterItemSupply.Plan(career.Selected, pool, n => n) == null, "Invalid tool selection cannot fall through into a consumable reward");
        check(career.ApplyItemSupply(first) && career.Selected.Items[0].Uses == 2 && career.Selected.Items[1].Uses == 2 &&
            career.Selected.Items.Count == 2, "One box replenishes only one incomplete tool even with empty consumable slots");
        check(!career.ApplyItemSupply(first), "Reapplying a completed tool plan cannot grant another increment");
        var invalidPool = new[] { new HunterConsumableWeight { Item = HunterItemId.Knife, Weight = 1 } };
        check(HunterItemSupply.CanSupply(career.Selected, invalidPool) &&
            career.ApplyItemSupply(HunterItemSupply.Plan(career.Selected, invalidPool, n => 0)) && career.Selected.Items[0].Uses == 3,
            "A malformed consumable pool does not prevent legitimate tool refills or exceed capacity");
        check(career.AcquireConsumable(id, HunterItemId.HealthShot) && career.Selected.Items[1].Uses == 2,
            "A loose consumable can occupy its free slot even while regular tools need refilling");
        check(career.ApplyItemSupply(second) && career.Selected.Items[1].Uses == 3,
            "Unchanged tool identity and remaining uses allow a prepared refill after an unrelated pickup");
        var weighted = HunterItemSupply.Plan(career.Selected, pool, n => 3);
        check(weighted.Index == -1 && weighted.Item == HunterItemId.Dynamite && weighted.Amount == 1 && career.ApplyItemSupply(weighted),
            "Only after all tools are full does a supply box grant the weighted consumable");
        check(!career.ApplyItemSupply(weighted) && !career.AcquireConsumable(id, HunterItemId.HealthShot) &&
            !HunterItemSupply.CanSupply(career.Selected, pool) && HunterItemSupply.Plan(career.Selected, pool, n => 0) == null,
            "Full tool and consumable slots preserve boxes and reject duplicate or extra rewards");
        var snapshot = career.Snapshot();
        check(snapshot.Hunters.Find(h => h.Id == other).Items.Count == 0 && snapshot.Items.Count == 0 && career.Credits == 330 &&
            career.Selected.Experience == 0 && career.Selected.SkillPoints == 0, "Field supply changes no reserve hunter, warehouse, currency or progression");
        career.Settle(CareerOutcome.Extracted, 0);
        check(career.Selected.Items.Count == 4 && career.Selected.Items[0].Uses == 3 && career.Selected.Items[1].Uses == 3,
            "Successful extraction retains refilled charges and field-acquired consumables");
        career.BeginRun(); career.Settle(CareerOutcome.Lost, 0);
        check(career.Selected.Id == other && career.Selected.Items.Count == 0 && career.WarehouseItems.Count == 0,
            "Permanent loss removes field supplies with their owner instead of moving them to another hunter");

        var daggerCareer = HunterCareer.CreateNew(); string daggerId = daggerCareer.Selected.Id;
        daggerCareer.BuyItem(HunterItemId.Knife); daggerCareer.EquipItem(daggerId, HunterItemId.Knife); daggerCareer.BeginRun();
        var consumable = HunterItemSupply.Plan(daggerCareer.Selected, pool, n => 0);
        check(HunterItemSupply.RefillableTools(daggerCareer.Selected).Count == 0 && consumable.Index == -1 &&
            daggerCareer.ApplyItemSupply(consumable), "Unlimited melee tools never occupy a refill outcome");
        check(!daggerCareer.ApplyItemSupply(consumable), "A consumable plan rejects immediate replay even when a second slot is empty");
        check(!career.ApplyItemSupply(consumable), "A supply plan is bound to the original deployed hunter");

        string path = Path.Combine(directory, "supply.json");
        var store = new HunterCareerStore(path, decode, encode); store.Load(); id = store.Current.Selected.Id;
        store.Change(c => c.BuyItem(HunterItemId.FirstAid)); store.Change(c => c.EquipItem(id, HunterItemId.FirstAid));
        store.Change(c => c.BeginRun()); store.Change(c => c.ConsumeItem(id, 0, HunterItemId.FirstAid));
        var retry = HunterItemSupply.Plan(store.Current.Selected, pool, n => 0); before = File.ReadAllText(path);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            bool failed = false; try { store.Change(c => c.ApplyItemSupply(retry)); } catch (IOException) { failed = true; }
            check(failed && store.Current.Selected.Items[0].Uses == 2 && File.ReadAllText(path) == before &&
                HunterItemSupply.Matches(store.Current.Selected, retry), "Failed refill writes preserve live charges, disk and the exact retry plan");
        }
        check(store.Change(c => c.ApplyItemSupply(retry)) && store.Current.Selected.Items[0].Uses == 3,
            "Retrying the same supply plan commits exactly one refill");
        before = File.ReadAllText(path);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            bool failed = false; try { store.Change(c => c.AcquireConsumable(id, HunterItemId.Dynamite)); } catch (IOException) { failed = true; }
            check(failed && store.Current.Selected.Items.Count == 1 && File.ReadAllText(path) == before,
                "Failed loose-pickup writes publish no consumable in memory or on disk");
        }
        store.Change(c => c.AcquireConsumable(id, HunterItemId.Dynamite)); store.Change(c => c.Settle(CareerOutcome.Extracted, 0));
        var reload = new HunterCareerStore(path, decode, encode); reload.Load();
        check(reload.Current.Selected.Items.Count == 2 && reload.Current.Selected.Items[0].Uses == 3 &&
            reload.Current.Selected.Items[1].Id == HunterItemId.Dynamite && !reload.Current.InRun,
            "Fresh account loading retains extracted refill counts and acquired consumables");
        reload.Change(c => c.BeginRun());
        var interrupted = new HunterCareerStore(path, decode, encode); interrupted.Load();
        check(interrupted.Current.HunterCount == 0 && interrupted.Current.LastOutcome == CareerOutcome.Interrupted,
            "Immediate supply saves keep the active-run marker and cannot rescue items after an interrupted expedition");
    }
}
