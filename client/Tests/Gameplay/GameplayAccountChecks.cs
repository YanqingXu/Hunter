using System.Collections;
using System.IO;
using System.Linq;
using BigWorld.Gameplay;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class GameplayPlayChecks
{
    private IEnumerator AccountChecks()
    {
        bool itemLoot = level.ItemLoot.enabled, skillLoot = level.SkillLoot.enabled;
        level.TrainingMode = false; level.ItemLoot.enabled = level.SkillLoot.enabled = false;
        level.Elites.enabled = false; isolateEliteNpcs = true; level.ClaimRelief();
        int initialExperience = level.Career.AccountExperience, initialLevel = level.Career.AccountLevel;
        var accountText = hud.GetComponentsInChildren<Text>().First(t => t.name == "Account Growth");
        yield return null;
        Check(accountText.text.Contains("账号 Lv." + initialLevel) && accountText.text.Contains("累计 " + initialExperience) &&
            accountText.preferredWidth <= accountText.rectTransform.rect.width && accountText.preferredHeight <= accountText.rectTransform.rect.height,
            "The real camp displays derived account level, total experience and progress without clipping");
        yield return Capture("account-level-camp");
        for (int retirement = 0; retirement < 2; retirement++)
        {
            if (level.Career.Selected == null) level.ClaimRelief();
            if (level.Career.Selected.Weight == 0)
            { if (level.Career.Pistols == 0) level.BuyEquipment(HunterEquipment.Pistol); level.ToggleEquipment(HunterEquipment.Pistol); }
            string veteran = level.Career.Selected.Id;
            int previousExperience = level.Career.AccountExperience, previousLevel = level.Career.AccountLevel;
            int previousRetirements = level.Career.Retirements;
            // The extraction scenario already tests physical departure. Here actual settlements earn a retireable hunter.
            while (!level.Career.Selected.IsMaxLevel)
            {
                level.StartRun(); yield return null; yield return SkillExtraction();
            }
            Check(level.Career.AccountExperience == previousExperience && level.Career.AccountLevel == previousLevel,
                "Ordinary hunter experience earned by repeated extraction does not directly level the shared account: retirement " + retirement);
            yield return null; ClickButton("满级退役"); yield return null;
            var consequences = hud.GetComponentsInChildren<Text>().First(t => t.name == "Consequences");
            int expectedLevel = HunterAccountProgression.Level(previousExperience + 500);
            Check(consequences.text.Contains("账号 Lv." + previousLevel + " → Lv." + expectedLevel) &&
                consequences.preferredHeight <= consequences.rectTransform.rect.height,
                "The real retirement confirmation previews the exact account level change within its text area: retirement " + retirement);
            if (retirement == 0)
            {
                ClickButton("取消，保留猎人"); yield return null;
                Check(level.Career.Selected.Id == veteran && level.Career.AccountExperience == previousExperience &&
                    accountText.text.Contains("账号 Lv." + previousLevel), "Cancelling the account-growth preview preserves the hunter, level and progress");
                ClickButton("满级退役"); yield return null;
                string disk = File.ReadAllText(level.CareerSavePath);
                using (var locked = new FileStream(level.CareerSavePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    ClickButton("确认退役"); yield return null;
                    Check(level.Career.Selected.Id == veteran && level.Career.AccountExperience == previousExperience && level.Career.AccountLevel == previousLevel &&
                        File.ReadAllText(level.CareerSavePath) == disk && FindButton("确认退役").interactable,
                        "A locked real retirement leaves the level preview retryable without publishing account growth or removing the hunter");
                }
                yield return Capture("account-retirement-preview");
            }
            ClickButton("确认退役"); yield return null;
            Check(level.Career.AccountExperience == previousExperience + 500 && level.Career.AccountLevel == expectedLevel &&
                level.Career.Retirements == previousRetirements + 1 && accountText.text.Contains("账号 Lv." + expectedLevel) &&
                accountText.text.Contains(level.Career.AccountLevelExperience + "/1000"),
                "The actual confirmation saves one retirement and immediately refreshes camp level and progress: retirement " + retirement);
            Check(!level.RetireHunter(veteran) && level.Career.AccountExperience == previousExperience + 500,
                "A repeated retired-hunter command cannot grant duplicate account growth: retirement " + retirement);
            var loaded = new HunterCareerStore(level.CareerSavePath, json => JsonUtility.FromJson<HunterCareerData>(json), data => JsonUtility.ToJson(data, true)); loaded.Load();
            Check(loaded.Current.AccountLevel == expectedLevel && loaded.Current.AccountExperience == previousExperience + 500 &&
                loaded.Current.AccountLevelExperience == level.Career.AccountLevelExperience && loaded.Current.Snapshot().Version == 10,
                "Unity serialization and a fresh disk load retain the displayed account growth without a new save schema: retirement " + retirement);
        }
        Check(level.Career.AccountExperience == initialExperience + 1000 && level.Career.AccountLevel == initialLevel + 1 &&
            accountText.preferredWidth <= accountText.rectTransform.rect.width,
            "Two actual retirements advance exactly one account level while the updated account row still fits");
        yield return Capture("account-level-up");
        level.ClaimRelief(); string selected = level.Career.Selected.Id;
        if (level.Career.Selected.Weight == 0)
        { if (level.Career.Pistols == 0) level.BuyEquipment(HunterEquipment.Pistol); level.ToggleEquipment(HunterEquipment.Pistol); }
        var before = level.Career.Selected; int accountLevel = level.Career.AccountLevel;
        level.StartRun(); yield return null;
        Check(actor.MaxHealth == 100 && level.Career.Selected.Experience == before.Experience && level.Career.Selected.SkillPoints == before.SkillPoints,
            "Deploying after an account-level increase grants no unconfigured hunter health, growth or skill points");
        level.ReturnToTitle(); yield return Until(() => level.Phase == LevelPhase.Ready, 10, "Return to camp after account-level loss check"); yield return null;
        Check(level.Career.Snapshot().Hunters.All(h => h.Id != selected) && level.Career.AccountLevel == accountLevel &&
            level.Career.AccountExperience == initialExperience + 1000 && accountText.text.Contains("账号 Lv." + accountLevel),
            "Actual abandonment loses the selected hunter while camp retains the account's level and cumulative progress");
        level.ClaimRelief();
        level.ItemLoot.enabled = itemLoot; level.SkillLoot.enabled = skillLoot; isolateEliteNpcs = false; RestoreFrozen();
    }
}
