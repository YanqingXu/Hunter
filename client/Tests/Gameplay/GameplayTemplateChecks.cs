using System;
using System.Collections;
using System.IO;
using System.Linq;
using BigWorld.Gameplay;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class GameplayPlayChecks
{
    // Older growth and touch fixtures require both guns. Buy that template through real commands.
    private IEnumerator RecruitDualGunFixture()
    {
        if (level.Career.Selected.TemplateId != HunterTemplateId.Relief) yield break;
        level.StartRun(); yield return null; level.ReturnToTitle();
        yield return Until(() => level.Phase == LevelPhase.Ready, 10, "Close the relief fixture before paid recruitment");
        level.RecruitHunter();
        if (level.Career.Pistols == 0) level.BuyEquipment(HunterEquipment.Pistol);
        level.ToggleEquipment(HunterEquipment.Pistol); yield return null;
        Check(level.Career.Selected.TemplateId == HunterTemplateId.Recruited && level.Career.Selected.Pistol,
            "The later dual-gun regression purchases a recruited hunter instead of bypassing relief capacity");
    }

    private IEnumerator TemplateChecks()
    {
        bool itemLoot = level.ItemLoot.enabled, skillLoot = level.SkillLoot.enabled;
        var loot = level.SkillLoot; var positions = loot.Positions; float chance = loot.SpawnChance;
        bool random = loot.RandomizeSkills; float recovery = player.HealthRecoveryRate;
        level.ItemLoot.enabled = false; level.Elites.enabled = false; isolateEliteNpcs = true;
        while (level.Career.HunterCount > 0)
        {
            if (level.Career.Selected.Weight == 0)
            { if (level.Career.Pistols == 0) level.BuyEquipment(HunterEquipment.Pistol); level.ToggleEquipment(HunterEquipment.Pistol); }
            level.StartRun(); yield return null; level.ReturnToTitle();
            yield return Until(() => level.Phase == LevelPhase.Ready, 10, "Clear test roster through actual abandonment");
        }
        yield return null; string disk = File.ReadAllText(level.CareerSavePath); int money = level.Career.Credits;
        using (var locked = new FileStream(level.CareerSavePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            ClickButton("全员阵亡救济"); yield return null;
            Check(level.Career.HunterCount == 0 && level.Career.Credits == money && File.ReadAllText(level.CareerSavePath) == disk,
                "A locked relief-button transaction grants neither a hunter nor gear and changes no credits or file");
        }
        ClickButton("全员阵亡救济"); yield return null;
        string relief = level.Career.Selected.Id;
        Check(level.Career.Selected.TemplateId == HunterTemplateId.Relief && level.Career.HunterCount == 1 && !FindButton("全员阵亡救济").interactable,
            "The real empty-roster button creates one relief template and disables repeated claims");
        if (level.Career.Selected.Rifle) level.ToggleEquipment(HunterEquipment.Rifle);
        if (!level.Career.Selected.Pistol)
        { if (level.Career.Pistols == 0) level.BuyEquipment(HunterEquipment.Pistol); level.ToggleEquipment(HunterEquipment.Pistol); }
        if (level.Career.Rifles == 0) level.BuyEquipment(HunterEquipment.Rifle);
        yield return null; disk = File.ReadAllText(level.CareerSavePath); int rifles = level.Career.Rifles;
        level.ToggleEquipment(HunterEquipment.Rifle); yield return null;
        Check(!level.Career.Selected.Rifle && level.Career.Rifles == rifles && File.ReadAllText(level.CareerSavePath) == disk && !FindButton("装备步枪").interactable,
            "Relief with a pistol rejects a second gun in both the real camp button and command without changing inventory");
        var description = hud.GetComponentsInChildren<Text>().First(t => t.text.Contains("救济猎人 · 负重"));
        Check(description.text.Contains("1/2") && description.text.Contains("携带技能 0/10") && description.preferredHeight <= description.rectTransform.rect.height,
            "The camp description displays relief weight and skill capacity without clipping");
        yield return Capture("template-relief-camp");
        loot.enabled = true; loot.SpawnChance = 1; loot.RandomizeSkills = false;
        // Catalogue order: melee, reload, aim, death, throw, once; second once copy at socket 11.
        loot.Positions = Enumerable.Range(0, 12).Select(i => new Vector2(6 + i * 2, 2.6f)).ToArray();
        player.HealthRecoveryRate = 0;
        level.StartRun(); yield return null; yield return EliteGround(new Vector2(6, 2.05f));
        Check(player.Weapons.WeaponCount == 1 && actor.MaxHealth == 100 && player.HealthSegments.Sum() == 100,
            "Relief deploys its actual single-gun loadout with the shared fixed maximum health");
        foreach (int socket in new[] { 1, 3, 4, 5 })
        {
            yield return EliteGround(new Vector2(loot.Positions[socket].x, 2.05f));
            Check(loot.Pickups[socket].TryCollect(player), "Relief collects a real skill within capacity: " + loot.Pickups[socket].Skill);
        }
        Check(level.Career.Selected.CarriedSkillValue == 10, "Four distinct real field cards reach the relief capacity exactly");
        yield return EliteGround(new Vector2(6, 2.05f));
        disk = File.ReadAllText(level.CareerSavePath); var weapons = player.Weapons;
        Check(!loot.Pickups[0].TryCollect(player) && !loot.Pickups[0].Used && loot.Pickups[0].Hint(level.Career.Selected).Contains("容量不足") &&
            File.ReadAllText(level.CareerSavePath) == disk && ReferenceEquals(weapons, player.Weapons),
            "A capacity-blocked real card remains available and preserves both disk and live combat state with an explanatory hint");
        actor.RestoreHealth(.5f);
        Check(player.TryUseOnceSkill() && actor.Health == 100 && level.Career.Selected.CarriedSkillValue == 8,
            "Actual emergency-bandage use heals and releases two carried-skill points immediately");
        yield return EliteGround(new Vector2(loot.Positions[11].x, 2.05f));
        Check(loot.Pickups[11].TryCollect(player) && level.Career.Selected.CarriedSkillValue == 10,
            "The newly available capacity admits a different real once-skill card in the same run");
        yield return SkillExtraction(); yield return null;
        ClickButton("猎人技能"); yield return null;
        var blocked = FindButton("购买格斗节能");
        Check(level.Career.Selected.SkillPoints >= 1 && !blocked.interactable && blocked.GetComponentInChildren<Text>().text.Contains("容量不足"),
            "A funded camp purchase is visibly disabled when the selected relief hunter is at skill capacity");
        var heading = hud.GetComponentsInChildren<Text>().First(t => t.name == "Hunter Points");
        Check(heading.text.Contains("救济猎人") && heading.text.Contains("10/10") && heading.preferredWidth <= heading.rectTransform.rect.width,
            "The skill menu fits the template, unspent points and current carrying capacity in its header");
        disk = File.ReadAllText(level.CareerSavePath); int points = level.Career.Selected.SkillPoints;
        blocked.onClick.Invoke();
        Check(level.Career.Selected.SkillPoints == points && File.ReadAllText(level.CareerSavePath) == disk,
            "Even a stale disabled purchase callback cannot spend points beyond template capacity");
        yield return Capture("template-skill-capacity");
        ClickButton("移除投球好手"); yield return null; ClickButton("购买格斗节能"); yield return null;
        Check(level.Career.Selected.CarriedSkillValue == 9 && level.Career.Selected.HasSkill(HunterSkillId.EfficientMelee),
            "Real camp removal frees capacity and enables a formerly blocked skill purchase");
        ClickButton("返回整备"); yield return null;
        money = level.Career.Credits; ClickButton("招募猎人"); yield return null; string recruited = level.Career.Selected.Id;
        Check(level.Career.Selected.TemplateId == HunterTemplateId.Recruited && level.Career.Credits == money - 100 && level.Career.Selected.CarriedSkillValue == 0,
            "Paid camp recruitment charges once and creates a distinct template without inheriting relief skills");
        if (level.Career.Rifles == 0) level.BuyEquipment(HunterEquipment.Rifle);
        if (level.Career.Pistols == 0) level.BuyEquipment(HunterEquipment.Pistol);
        level.ToggleEquipment(HunterEquipment.Rifle); level.ToggleEquipment(HunterEquipment.Pistol); yield return null;
        Check(level.Career.Selected.Weight == 3 && level.Career.CanDeploy, "The recruited template can actually equip both warehouse guns");
        yield return Capture("template-recruited-camp");
        level.StartRun(); yield return null; yield return EliteGround(new Vector2(6, 2.05f));
        Check(player.Weapons.WeaponCount == 2 && actor.MaxHealth == 100 && !level.Career.Selected.HasSkill(HunterSkillId.EfficientMelee),
            "Switching to the recruited hunter applies two real weapons and independent skills with shared base health");
        yield return SkillExtraction(); level.SelectHunter(relief); yield return null;
        var loaded = new HunterCareerStore(level.CareerSavePath, json => JsonUtility.FromJson<HunterCareerData>(json), data => JsonUtility.ToJson(data, true)); loaded.Load();
        Check(loaded.Current.Selected.TemplateId == HunterTemplateId.Relief && loaded.Current.Selected.CarriedSkillValue == 9 &&
            loaded.Current.Snapshot().Hunters.First(h => h.Id == recruited).TemplateId == HunterTemplateId.Recruited && loaded.Current.Snapshot().Version == 10,
            "Unity serialization and a fresh disk load preserve both independent templates after actual extractions");
        loot.Positions = positions; loot.SpawnChance = chance; loot.RandomizeSkills = random; loot.enabled = skillLoot;
        player.HealthRecoveryRate = recovery; level.ItemLoot.enabled = itemLoot; isolateEliteNpcs = false; RestoreFrozen();
    }
}
