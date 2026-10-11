using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BigWorld.Gameplay;
using BigWorld.Map2D;
using BigWorld.YouYou2D;
using SkillEditorKit;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using YouYou;

/// <summary>Runs on a temporary copy of the real scene, against real streamed terrain and skills.</summary>
public sealed partial class GameplayPlayChecks : MonoBehaviour
{
    [Serializable] private sealed class Report
    {
        public string unityVersion;
        public string scenario;
        public int passed;
        public List<string> checks = new List<string>();
        public List<string> screenshots = new List<string>();
        public string failure;
    }
    private sealed class FrozenGuard
    {
        public PatrolEnemy2D enemy;
        public Rigidbody2D body;
        public bool enabled;
        public RigidbodyConstraints2D constraints;
    }
    private readonly Report report = new Report();
    private readonly List<FrozenGuard> frozen = new List<FrozenGuard>();
    private string failure, screenshotDirectory;
    private float deadline;
    // Includes three additional physical ten-second departures and their return-to-camp transitions.
    private const int ValidationSeconds = 420;
    private bool finished;
    private LevelSession2D level;
    private PlayerController2D player;
    private SkillActor2D actor;
    private Rigidbody2D body;
    private GridMapEntityStreamer streamer;
    private GameHud2D hud;
    private int damageEvents;
    private static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../TestResults/VerticalSlice"));
    private static string Scenario
    {
        get { var value = Environment.GetEnvironmentVariable("HUNTER_GAMEPLAY_SCENARIO"); return value == "interactions" || value == "ladder" || value == "barrels" || value == "elites" || value == "boss" || value == "alert" || value == "search" || value == "health" || value == "templates" || value == "ballistics" || value == "extraction" || value == "account" ? value : "all"; }
    }

    private void Awake()
    {
        // Neither legacy regression nor career checks may touch a player's real profile.
        var session = FindObjectOfType<LevelSession2D>();
        session.TrainingMode = Scenario == "all";
        session.CareerSavePathOverride = Path.Combine(Output, "Career-" + Guid.NewGuid().ToString("N"), "career.json");
    }

    private IEnumerator Start()
    {
        Directory.CreateDirectory(Output);
        screenshotDirectory = Path.Combine(Output, "Screenshots-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(screenshotDirectory);
        Application.logMessageReceived += OnLog;
        deadline = Time.realtimeSinceStartup + ValidationSeconds;
        // Flatten nested iterators so assertions in helpers are caught and written to JSON too.
        var stack = new Stack<IEnumerator>();
        stack.Push(Checks());
        while (stack.Count > 0 && !finished)
        {
            object wait = null;
            try
            {
                if (failure != null) throw new Exception(failure);
                if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("Gameplay checks exceeded " + ValidationSeconds + " seconds.");
                var current = stack.Peek();
                if (!current.MoveNext()) { (current as IDisposable)?.Dispose(); stack.Pop(); continue; }
                if (current.Current is IEnumerator nested) { stack.Push(nested); continue; }
                wait = current.Current;
            }
            catch (Exception error)
            {
                while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
                Finish(error.ToString());
                yield break;
            }
            yield return wait;
        }
        if (!finished) Finish(null);
    }

    private void Update()
    {
        if (isolateEliteNpcs) IsolateEliteNpcs();
        if (!finished && deadline > 0 && Time.realtimeSinceStartup > deadline)
            Finish("Gameplay checks exceeded " + ValidationSeconds + " seconds while waiting for a frame or phase.");
    }

    private IEnumerator Checks()
    {
        yield return Until(() => GameServices2D.Instance && GameServices2D.Instance.IsReady, 60, "Original framework content bootstrap");
        yield return Until(() => LevelSession2D.Instance && LevelSession2D.Instance.Phase == LevelPhase.Ready, 60, "Initial Ready phase");
        level = LevelSession2D.Instance;
        // Legacy fixtures isolate their original actors; the elite scenario below enables the production director.
        level.Elites.enabled = false;
        level.Hunt.UseRegionClues = false; level.Hunt.RandomizeBossRegion = false; level.Hunt.ResetMission();
        // Existing combat fixtures predate wooden cover; the dedicated ammunition scenario enables actual production cover.
        level.SceneInteractions.BallisticCoverEnabled = false;
        player = level.Player; player.ReadKeyboard = false; level.ReadKeyboard = false;
        actor = player.GetComponent<SkillActor2D>(); body = player.GetComponent<Rigidbody2D>();
        streamer = level.World.Map.GetComponent<GridMapEntityStreamer>();
        actor.Damaged += OnPlayerDamaged;
        yield return Until(() => FindObjectOfType<GameHud2D>(), 60, "Original framework-loaded gameplay HUD");
        hud = FindObjectOfType<GameHud2D>();
        yield return null;
        if (Scenario != "all")
        {
            Check(true, "Focused scenario: " + Scenario + "; this run is not the full regression suite");
            if (Scenario == "interactions") yield return SceneInteractionChecks();
            else if (Scenario == "ladder") yield return LadderChecks();
            else if (Scenario == "barrels") yield return BarrelChecks();
            else if (Scenario == "elites") yield return EliteChecks();
            else if (Scenario == "boss") yield return BossChecks();
            else if (Scenario == "alert") yield return AlertChecks();
            else if (Scenario == "search") yield return SearchChecks();
            else if (Scenario == "templates") yield return TemplateChecks();
            else if (Scenario == "ballistics") yield return BallisticChecks();
            else if (Scenario == "extraction") yield return ExtractionChecks();
            else if (Scenario == "account") yield return AccountChecks();
            else yield return HealthChecks();
            var focusedShutdown = GameServices2D.Instance.ShutdownAsync();
            yield return Until(() => focusedShutdown.IsCompleted, 10, "Focused scenario framework shutdown");
            if (focusedShutdown.IsFaulted) throw focusedShutdown.Exception;
            yield return null; yield return null; yield break;
        }
        var services = GameServices2D.Instance;
        Check(services.Framework == GameEntry.Instance && services.Framework.IsInitialized && !services.Framework.AutoLaunchProcedure &&
            Array.TrueForAll(NativeManagers(), manager => manager != null) &&
            GameEntry.DataTable.AlreadyLoadTable.Count >= 17, "The playable level boots all 18 original managers, C# session data and course tables");
        Check(GameEntry.DataTable.Sys_UIFormList.GetEntity(9001).HasValue && GameEntry.DataTable.Sys_UIFormList.GetEntity(9002).HasValue &&
            GameEntry.DataTable.Sys_UIFormList.GetEntity(UIFormId.UI_Dialog).HasValue,
            "The original FlatBuffers UI table preserves course forms alongside both 2D HUDs");
        yield return PureCSharpChecks.Run(Check);
        string hudPath = services.Assets.GetAssetPath("ui.game");
        var indexedHud = GameEntry.Resource.ResourceLoaderManager.GetAssetEntity(AssetCategory.UIPrefab, hudPath);
        Check(indexedHud != null && indexedHud.AssetFullName == hudPath && hud.UIFormId == 9001 &&
            hud.transform.IsChildOf(services.Framework.UIRootRectTransform) && ReferenceEquals(hud.UserData, level.World),
            "The gameplay HUD is original UI form 9001 loaded through the native resource index and UI hierarchy");
        Check(hud.IsMenuVisible && hud.DisplayedPhaseTitle == "边境试炼" && !player.ControlEnabled && Time.timeScale == 0,
            "Ready opens the real title HUD and freezes gameplay");
        Check(Vector2.Distance(body.position, level.StartPoint.position) < .15f &&
              Vector2.Distance(player.transform.position, body.position) < .1f,
            "Title freezes the player at the authored spawn with synchronized physics and render transforms" +
            " (body=" + body.position + ", transform=" + player.transform.position + ")");
        Vector2 readyPosition = body.position;
        player.SetInput(1, true, true);
        yield return new WaitForSecondsRealtime(.2f);
        Check(Vector2.Distance(body.position, readyPosition) < .001f && level.ElapsedSeconds == 0 && !player.TryShoot(),
            "Title screen rejects movement, jump and shooting without starting the clock");
        yield return Capture("ready");
        ClickButton("开始游戏");
        yield return null;
        Check(level.IsGameplayActive && player.ControlEnabled && !hud.IsMenuVisible && Time.timeScale > 0,
            "StartRun unlocks controls, physics and the gameplay HUD");
        yield return new WaitForSeconds(.3f);
        Check(player.IsGrounded && Mathf.Abs(body.position.y - 2) < .15f, "Player lands on the real streamed TilemapCollider2D");
        yield return Until(() => Guard(1), 4, "First streamed guard");
        Freeze(Guard(1).GetComponent<PatrolEnemy2D>());
        try { yield return MovementChecks(); yield return WeaponChecks(); }
        finally { RestoreFrozen(); }
        yield return EnemyChecks();
        yield return ShootGuard(1, true);
        Check(level.DefeatedEnemies == 1, "A real projectile kill records the first guard exactly once");
        yield return Capture("playing");

        yield return Warp(new Vector2(74, 2.05f));
        yield return new WaitForSeconds(.12f);
        Check(level.Phase == LevelPhase.Playing && !level.TryComplete(),
            "Entering the actual exit trigger without bounty still requires the full countdown before completion");
        if (level.Hunt && level.Hunt.Boss) level.Hunt.Boss.enabled = false;

        yield return Warp(new Vector2(31, 2.05f));
        yield return new WaitForSeconds(.12f);
        var checkpoints = FindObjectsOfType<Checkpoint2D>();
        var first = Array.Find(checkpoints, item => item.Order == 1);
        Check(first && first.Activated && level.CheckpointName == first.DisplayName && Vector2.Distance(level.RespawnPosition, new Vector2(29.5f, 2.05f)) < .05f,
            "Crossing checkpoint one activates its trigger and safe respawn position");
        actor.RestoreHealth();
        actor.BeHit(new SkillHitData { attackValue = actor.MaxHealth + 1 });
        yield return null;
        Check(level.Phase == LevelPhase.Dead && !player.ControlEnabled && !actor.IsAlive && Time.timeScale == 0 &&
              hud.IsMenuVisible && hud.DisplayedPhaseTitle == "再试一次", "Lethal damage locks control and opens the checkpoint retry menu");
        yield return Capture("dead");
        level.TogglePause();
        player.SetInput(1, true, true);
        Check(level.Phase == LevelPhase.Dead && Time.timeScale == 0 && !player.TryShoot(), "Pause and gameplay input cannot escape the Dead phase");
        int deaths = level.DeathCount;
        ClickButton("检查点重试"); level.RetryCheckpoint(); level.TogglePause(); level.RetryCheckpoint();
        yield return Until(() => level.Phase == LevelPhase.Playing, 10, "Checkpoint retry finishes");
        Check(actor.Health == actor.MaxHealth && actor.IsInvulnerable && player.ControlEnabled && body.simulated &&
              Vector2.Distance(body.position, new Vector2(29.5f, 2.05f)) < .2f && level.DeathCount == deaths && level.DefeatedEnemies == 1,
            "Repeated retry and pause requests create one respawn, preserving kills and granting full health with protection");

        body.position = new Vector2(29.5f, level.FallDeathY - 1);
        body.transform.position = new Vector3(body.position.x, body.position.y, 0);
        yield return Until(() => level.Phase == LevelPhase.Dead, 2, "Falling is lethal during respawn protection");
        Check(!actor.IsAlive && player.State == PlayerMotionState.Dead && level.DeathCount == deaths + 1,
            "Falling during spawn protection still enters a real dead state instead of leaving a living frozen actor");
        level.RetryCheckpoint();
        yield return Until(() => level.Phase == LevelPhase.Playing, 10, "Retry after falling");

        yield return ShootGuard(2, false);
        yield return Warp(new Vector2(57, 2.05f));
        yield return new WaitForSeconds(.12f);
        var second = Array.Find(checkpoints, item => item.Order == 2);
        Check(second && second.Activated && Vector2.Distance(level.RespawnPosition, new Vector2(55.5f, 2.05f)) < .05f,
            "Checkpoint two advances the saved respawn position");
        yield return ShootGuard(3, false);
        Check(level.DefeatedEnemies == 3, "Real skill projectiles defeat all three streamed guards");
        yield return MissionChecks();
        yield return Warp(new Vector2(74, 2.05f));
        yield return Until(() => level.Phase == LevelPhase.Won, 15, "Carrying bounty completes the extraction countdown");
        yield return null;
        Check(hud.IsMenuVisible && hud.DisplayedPhaseTitle == "试炼完成" && !player.ControlEnabled && Time.timeScale == 0,
            "The unlocked exit enters Won and shows the result menu");
        float wonTime = level.ElapsedSeconds; Vector2 wonPosition = body.position;
        player.SetInput(1, true, true); yield return new WaitForSecondsRealtime(.2f);
        Check(level.ElapsedSeconds == wonTime && Vector2.Distance(body.position, wonPosition) < .001f && !player.TryShoot(),
            "Victory freezes the timer and ignores gameplay input");
        yield return Capture("won");
        ClickButton("再玩一次"); level.RestartRun();
        yield return Until(() => level.Phase == LevelPhase.Playing && Guard(1), 10, "Fresh run and first guard restoration");
        Check(level.DefeatedEnemies == 0 && level.DeathCount == 0 && level.CheckpointName == "起点" &&
              level.Hunt.State.Stage == HuntStage.Tracking && level.Hunt.State.ClueCount == 0 && !level.Hunt.State.SecondWaveStarted &&
              level.ElapsedSeconds < .5f && actor.Health == actor.MaxHealth && Guard(1).Health == 50 &&
              Array.TrueForAll(checkpoints, item => !item.Activated) && Vector2.Distance(body.position, level.StartPoint.position) < .2f,
            "Restart clears kills, checkpoints, deaths and timer and restores the original enemy state");
        level.TogglePause(); yield return null;
        ClickButton("返回标题");
        yield return Until(() => level.Phase == LevelPhase.Ready, 10, "Return to title");
        yield return null;
        Check(hud.IsMenuVisible && hud.DisplayedPhaseTitle == "边境试炼" && !player.ControlEnabled && Time.timeScale == 0 &&
              level.DefeatedEnemies == 0 && level.ElapsedSeconds == 0, "ReturnToTitle resets the run and restores a usable title screen");
        HunterCareerChecks.Run(Check, json => JsonUtility.FromJson<HunterCareerData>(json), data => JsonUtility.ToJson(data, true),
            Path.Combine(Output, "CareerRules-" + Guid.NewGuid().ToString("N")));
        HunterProgressionChecks.Run(Check, json => JsonUtility.FromJson<HunterCareerData>(json), data => JsonUtility.ToJson(data, true),
            Path.Combine(Output, "GrowthRules-" + Guid.NewGuid().ToString("N")));
        HunterSkillChecks.Run(Check, json => JsonUtility.FromJson<HunterCareerData>(json), data => JsonUtility.ToJson(data, true),
            Path.Combine(Output, "SkillRules-" + Guid.NewGuid().ToString("N")));
        HunterFieldSkillChecks.Run(Check, json => JsonUtility.FromJson<HunterCareerData>(json), data => JsonUtility.ToJson(data, true),
            Path.Combine(Output, "FieldSkillRules-" + Guid.NewGuid().ToString("N")));
        HunterRevivalChecks.Run(Check, json => JsonUtility.FromJson<HunterCareerData>(json), data => JsonUtility.ToJson(data, true),
            Path.Combine(Output, "RevivalRules-" + Guid.NewGuid().ToString("N")));
        HunterItemChecks.Run(Check, json => JsonUtility.FromJson<HunterCareerData>(json), data => JsonUtility.ToJson(data, true),
            Path.Combine(Output, "ItemRules-" + Guid.NewGuid().ToString("N")));
        HunterSupplyChecks.Run(Check, json => JsonUtility.FromJson<HunterCareerData>(json), data => JsonUtility.ToJson(data, true),
            Path.Combine(Output, "SupplyRules-" + Guid.NewGuid().ToString("N")));
        HunterItemSkillChecks.Run(Check, json => JsonUtility.FromJson<HunterCareerData>(json), data => JsonUtility.ToJson(data, true),
            Path.Combine(Output, "ItemSkillRules-" + Guid.NewGuid().ToString("N")));
        HunterOnceSkillChecks.Run(Check, json => JsonUtility.FromJson<HunterCareerData>(json), data => JsonUtility.ToJson(data, true),
            Path.Combine(Output, "OnceSkillRules-" + Guid.NewGuid().ToString("N")));
        HunterAimChecks.Run(Check);
        HunterPointerStickChecks.Run(Check);
        HunterInteractionChecks.Run(Check);
        HunterLadderChecks.Run(Check);
        HunterBarrelChecks.Run(Check);
        HunterEliteChecks.Run(Check);
        HunterBossAbilityChecks.Run(Check);
        HunterAlertMemoryChecks.Run(Check);
        HunterSearchLayoutChecks.Run(Check);
        HunterHealthLayoutChecks.Run(Check);
        HunterTemplateChecks.Run(Check);
        HunterPenetrationChecks.Run(Check);
        HunterRoundChecks.Run(Check);
        HunterExtractionAttemptChecks.Run(Check);
        HunterMissionExtractionChecks.Run(Check);
        HunterAccountProgressionChecks.Run(Check);
        HunterAccountCareerChecks.Run(Check, json => JsonUtility.FromJson<HunterCareerData>(json), data => JsonUtility.ToJson(data, true),
            Path.Combine(Output, "AccountLevelRules-" + Guid.NewGuid().ToString("N")));
        HunterTemplateCareerChecks.Run(Check, json => JsonUtility.FromJson<HunterCareerData>(json), data => JsonUtility.ToJson(data, true),
            Path.Combine(Output, "TemplateRules-" + Guid.NewGuid().ToString("N")));
        HunterHealthCareerChecks.Run(Check, json => JsonUtility.FromJson<HunterCareerData>(json), data => JsonUtility.ToJson(data, true),
            Path.Combine(Output, "HealthLayoutRules-" + Guid.NewGuid().ToString("N")));
        HunterPostureChecks.Run(Check);
        HunterThrowPathChecks.Run(Check);
        yield return CareerChecks();
        yield return GrowthChecks();
        yield return SkillChecks();
        yield return FieldSkillChecks();
        yield return RevivalChecks();
        yield return ItemChecks();
        yield return SupplyChecks();
        yield return ItemSkillChecks();
        yield return OnceSkillChecks();
        yield return AimChecks();
        yield return PostureChecks();
        yield return ThrowAimChecks();
        yield return TouchChecks();
        yield return SceneInteractionChecks();
        yield return LadderChecks();
        yield return BarrelChecks();
        yield return EliteChecks();
        yield return BossChecks();
        yield return AlertChecks();
        yield return SearchChecks();
        yield return HealthChecks();
        yield return TemplateChecks();
        yield return BallisticChecks();
        yield return ExtractionChecks();
        yield return AccountChecks();
        var shutdown = GameServices2D.Instance.ShutdownAsync();
        yield return Until(() => shutdown.IsCompleted, 10, "Framework shutdown drains the active level and pool");
        if (shutdown.IsFaulted) throw shutdown.Exception;
        yield return null; yield return null;
        Check(!GameServices2D.Instance && FindObjectsOfType<BigWorld.Pooling.Unity.PoolDriver>().Length == 0 && !GameEntry.Instance &&
            Array.TrueForAll(NativeManagers(), manager => manager == null),
            "Closing the game drains streamed guards and pending pool requests and releases every original manager");
    }

    private IEnumerator CareerChecks()
    {
        level.TrainingMode = false;
        yield return null;
        Check(hud.DisplayedPhaseTitle == "猎人营地" && hud.IsMenuVisible && level.Career.Credits == 500,
            "Production mode opens the camp with a persistent initial roster");
        string starter = level.Career.Selected.Id;
        ClickButton("招募猎人"); yield return null;
        string recruit = level.Career.Selected.Id;
        Check(level.Career.HunterCount == 2 && level.Career.Credits == 400 && recruit != starter && level.Career.Selected.Weight == 0,
            "Camp recruit button charges funds and selects the new hunter");
        level.StartRun();
        Check(level.Phase == LevelPhase.Ready && !level.Career.InRun, "Start input cannot deploy an unequipped recruit");
        ClickButton("购买手枪"); yield return null;
        ClickButton("装备手枪"); yield return null;
        Check(level.Career.Credits == 325 && level.Career.Pistols == 1 && level.Career.Selected.Pistol,
            "Shop and equip buttons transfer a purchased pistol from storage");
        ClickButton("选择"); yield return null;
        Check(level.Career.Selected.Id == starter && level.Career.Selected.Weight == 3, "Roster button restores the other hunter's independent loadout");
        level.SelectHunter(recruit); yield return null;
        yield return Capture("camp");
        ClickButton("开始行动"); yield return null;
        Check(level.IsGameplayActive && level.Career.InRun && player.Weapons.WeaponCount == 1 && player.Weapons.Name == "手枪" && player.Weapons.Magazine == 8,
            "Deployment persists first, then equips the actual player with only the selected pistol");
        level.RecruitHunter(); level.BuyEquipment(HunterEquipment.Rifle); level.SelectHunter(starter);
        Check(level.Career.Credits == 325 && level.Career.Selected.Id == recruit, "Session blocks camp mutations during gameplay");
        // The preceding mission regression exercises real boss projectiles and the complete countdown.
        // Drive the mission state here to isolate the account transaction and failed-save recovery.
        level.Hunt.State.DefeatBoss(); level.Hunt.State.PickUpBounty(); level.Hunt.State.TickExtraction(10, true, false);
        using (var locked = new FileStream(level.CareerSavePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Check(!level.TryComplete() && level.Phase == LevelPhase.Error && level.HasCareerSaveError && level.Career.Credits == 325 && level.Career.InRun,
                "Failed extraction save freezes in an error menu without publishing reward or clearing the deployment");
            Check(level.Career.Selected.Experience == 0 && level.Career.Selected.SkillPoints == 0,
                "Failed extraction save does not prematurely publish hunter experience or skill points");
            level.ReturnToTitle();
            Check(level.Phase == LevelPhase.Error, "Return-to-camp cannot discard a pending failed settlement");
        }
        yield return null;
        ClickButton("重试存档"); yield return null;
        Check(level.Phase == LevelPhase.Won && !level.HasCareerSaveError && level.Career.Credits == 575 && !level.Career.InRun && level.Career.HunterCount == 2,
            "Retry save completes the pending extraction once and preserves both hunters");
        Check(level.Career.Selected.Experience == 200 && level.Career.Selected.Level == 3 && level.Career.Selected.SkillPoints == 2 && level.Career.GetHunter(0).Experience == 0,
            "Extraction retry awards the captured experience once to the deployed hunter only");
        level.RetryCareerSave();
        Check(!level.TryComplete() && level.Career.Credits == 575, "Repeated completion and retry cannot duplicate the bounty");
        var reload = new HunterCareerStore(level.CareerSavePath, json => JsonUtility.FromJson<HunterCareerData>(json), data => JsonUtility.ToJson(data, true));
        reload.Load();
        Check(reload.Current.Credits == 575 && reload.Current.Selected.Id == recruit && reload.Current.Selected.Pistol,
            "Scene reward and carried equipment survive disk reload");
        yield return Capture("career-won");
        ClickButton("返回营地");
        yield return Until(() => level.Phase == LevelPhase.Ready, 10, "Return to camp after extraction");
        yield return null;
        ClickButton("开始行动"); yield return null;
        body.position = new Vector2(4.5f, level.FallDeathY - 1);
        body.transform.position = new Vector3(body.position.x, body.position.y, 0);
        yield return Until(() => level.Phase == LevelPhase.Dead, 3, "Production fall death");
        yield return null;
        Check(hud.DisplayedPhaseTitle == "猎人已阵亡" && !level.Career.InRun && level.Career.HunterCount == 1 &&
            level.Career.Selected.Id == starter && level.Career.Pistols == 1 && level.Career.Credits == 575,
            "Real player death permanently removes only the deployed hunter and gear while preserving stored items and money");
        level.RetryCheckpoint(); yield return null;
        Check(level.Phase == LevelPhase.Dead && !actor.IsAlive, "Checkpoint retry cannot revive a career hunter");
        reload.Load();
        Check(reload.Current.HunterCount == 1 && reload.Current.Selected.Id == starter, "Permanent death is persisted before returning to camp");
        yield return Capture("career-dead");
        ClickButton("返回营地");
        yield return Until(() => level.Phase == LevelPhase.Ready, 10, "Return to camp after permanent death");
        yield return null;
        ClickButton("开始行动"); yield return null;
        Check(player.Weapons.WeaponCount == 2 && player.Weapons.Name == "步枪", "Selecting the starter restores the real two-gun loadout next run");
        level.TogglePause(); yield return null;
        ClickButton("放弃行动"); yield return null;
        Check(level.Phase == LevelPhase.Paused && level.Career.InRun && level.Career.HunterCount == 1, "First abandon click only presents the permanent-loss confirmation");
        ClickButton("确认放弃");
        yield return Until(() => level.Phase == LevelPhase.Ready, 10, "Confirmed abandonment returns to camp");
        yield return null;
        Check(level.Career.HunterCount == 0 && !level.Career.CanDeploy && level.Career.LastOutcome == CareerOutcome.Lost,
            "Confirmed abandonment removes the hunter instead of granting a free restart");
        ClickButton("全员阵亡救济"); yield return null;
        Check(level.Career.HunterCount == 1 && level.Career.CanDeploy && level.Career.Selected.Pistol && level.Career.Pistols == 0 && level.Career.Credits == 575,
            "Wiped-roster rescue button equips a new hunter from storage without duplicating money or gear");
        yield return Capture("camp-relief");
    }

    private IEnumerator GrowthChecks()
    {
        yield return RecruitDualGunFixture();
        string veteran = level.Career.Selected.Id;
        Check(!level.RetireHunter(veteran) && level.Career.HunterCount == 1, "Scene retirement rejects a level-one hunter");
        level.RecruitHunter(); string reserve = level.Career.Selected.Id;
        level.SelectHunter(veteran); level.ToggleEquipment(HunterEquipment.Rifle); yield return null;
        Check(level.Career.Selected.Weight == 3 && level.Career.GetHunter(1).Experience == 0, "Growth fixture keeps an independent reserve hunter in camp");
        for (int run = 0; run < 4; run++)
        {
            ClickButton("开始行动"); yield return null;
            Check(level.RunExperience == 100 && !level.RetireHunter(veteran), "New run resets objective experience and prevents retirement during deployment");
            for (int clue = 0; clue < 3; clue++) level.Hunt.State.Investigate(clue.ToString());
            level.Hunt.State.DefeatBoss(); level.Hunt.State.PickUpBounty(); level.Hunt.State.TickExtraction(10, true, false);
            int previous = level.Career.Selected.Experience;
            Check(level.RunExperience == 275 && level.TryComplete(), "Successful objective state settles clue, boss and extraction experience");
            yield return null;
            int expected = Mathf.Min(900, (run + 1) * 275);
            Check(level.Career.Selected.Experience == expected && level.Career.Selected.Level == 1 + expected / 100 &&
                level.Career.Selected.SkillPoints == expected / 100 && level.Career.LastExperience == expected - previous && level.Career.GetHunter(1).Experience == 0,
                "Successive scene extractions advance only the active hunter and clamp the last gain at max level");
            if (run == 3) yield return Capture("growth-won");
            ClickButton("返回营地");
            yield return Until(() => level.Phase == LevelPhase.Ready, 10, "Return to camp for next growth step");
            yield return null;
        }
        Check(level.Career.CanRetireSelected && level.Career.Selected.SkillPoints == 9, "Max-level hunter becomes eligible for camp retirement");
        yield return Capture("growth-camp");
        int rifles = level.Career.Rifles, pistols = level.Career.Pistols, money = level.Career.Credits;
        ClickButton("满级退役"); yield return null;
        Check(level.Career.HunterCount == 2 && level.Career.AccountExperience == 0 &&
            !Array.Find(FindObjectsOfType<Button>(), button => button.name == "Deploy").interactable,
            "Retirement prompt leaves the profile untouched and blocks background camp controls");
        yield return Capture("retirement-confirm");
        ClickButton("取消，保留猎人"); yield return null;
        Check(level.Career.Selected.Id == veteran && level.Career.Selected.SkillPoints == 9 && level.Career.Rifles == rifles,
            "Cancel retirement retains hunter identity, growth and equipment");
        ClickButton("满级退役"); yield return null;
        using (var locked = new FileStream(level.CareerSavePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            ClickButton("确认退役"); yield return null;
            Check(level.Career.HunterCount == 2 && level.Career.Selected.Id == veteran && level.Career.AccountExperience == 0 && level.Career.Rifles == rifles,
                "Failed retirement write keeps the dialog retryable and publishes no account or inventory changes");
        }
        ClickButton("确认退役"); yield return null;
        Check(level.Career.HunterCount == 1 && level.Career.Selected.Id == reserve && level.Career.AccountExperience == 500 &&
            level.Career.Retirements == 1 && level.Career.Rifles == rifles + 1 && level.Career.Pistols == pistols + 1 && level.Career.Credits == money && level.Career.Selected.SkillPoints == 0,
            "Confirmed retirement returns both weapons, awards account experience and keeps the reserve hunter independent");
        Check(!level.RetireHunter(veteran) && level.Career.Retirements == 1 && level.Career.AccountExperience == 500,
            "Stale retirement requests cannot repeat the account reward");
        var reloaded = new HunterCareerStore(level.CareerSavePath, json => JsonUtility.FromJson<HunterCareerData>(json), data => JsonUtility.ToJson(data, true));
        reloaded.Load();
        Check(reloaded.Current.HunterCount == 1 && reloaded.Current.Selected.Id == reserve && reloaded.Current.AccountExperience == 500 &&
            reloaded.Current.Rifles == rifles + 1 && reloaded.Current.Pistols == pistols + 1,
            "Scene retirement survives disk reload with the returned equipment and account experience");
        yield return Capture("retired-camp");
    }

    private IEnumerator SkillChecks()
    {
        string skilled = level.Career.Selected.Id;
        level.ToggleEquipment(HunterEquipment.Rifle); level.ToggleEquipment(HunterEquipment.Pistol);
        for (int run = 0; run < 3; run++)
        {
            level.StartRun(); yield return null;
            for (int clue = 0; clue < 3; clue++) level.Hunt.State.Investigate(clue.ToString());
            yield return SkillExtraction();
        }
        Check(level.Career.Selected.SkillPoints == 8 && level.Career.Selected.Level == 9, "Skill fixture earns its points through successful scene extractions");
        level.RecruitHunter(); string reserve = level.Career.Selected.Id;
        level.BuyEquipment(HunterEquipment.Pistol); level.ToggleEquipment(HunterEquipment.Pistol);
        level.SelectHunter(skilled); yield return null;
        ClickButton("猎人技能"); yield return null;
        Check(!Array.Find(FindObjectsOfType<Button>(), button => button.name == "Deploy").interactable &&
            level.Career.Selected.Skills.Count == 0, "Skill screen opens without spending points and blocks background camp actions");
        yield return Capture("skills-shop");
        using (var locked = new FileStream(level.CareerSavePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            ClickButton("购买稳定瞄准"); yield return null;
            Check(level.Career.Selected.SkillPoints == 8 && !level.Career.Selected.HasSkill(HunterSkillId.SteadyAim),
                "Scene purchase write failure preserves both points and ownership");
        }
        ClickButton("购买稳定瞄准"); ClickButton("购买快速装填"); ClickButton("购买格斗节能"); yield return null;
        Check(level.Career.Selected.SkillPoints == 2 && level.Career.Selected.Skills.Count == 3 && level.Career.GetHunter(1).Skills.Count == 0,
            "Real skill buttons buy all three passives only for the selected hunter");
        using (var locked = new FileStream(level.CareerSavePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            ClickButton("移除稳定瞄准"); yield return null;
            Check(level.Career.Selected.SkillPoints == 2 && level.Career.Selected.HasSkill(HunterSkillId.SteadyAim),
                "Scene removal write failure keeps the learned skill and withholds its refund");
        }
        ClickButton("移除稳定瞄准"); yield return null;
        Check(level.Career.Selected.SkillPoints == 3 && !level.Career.Selected.HasSkill(HunterSkillId.SteadyAim),
            "Scene removal returns one point for the three-point skill");
        ClickButton("购买稳定瞄准"); yield return null;
        Check(level.Career.Selected.SkillPoints == 0 && level.Career.Selected.Skills.Count == 3,
            "Scene repurchase charges the full price after the partial refund");
        yield return Capture("skills-learned");
        var reload = new HunterCareerStore(level.CareerSavePath, json => JsonUtility.FromJson<HunterCareerData>(json), data => JsonUtility.ToJson(data, true));
        reload.Load();
        Check(reload.Current.Selected.Skills.Count == 3 && reload.Current.Selected.SkillPoints == 0, "Scene skill purchases survive reloading the real test save");
        ClickButton("返回整备"); yield return null;
        Check(Array.Find(FindObjectsOfType<Button>(), button => button.name == "Deploy").interactable,
            "Closing the skill screen restores camp controls");
        ClickButton("开始行动"); yield return null;
        Check(Mathf.Abs(player.Weapons.FireInterval - .64f) < .001f && player.Weapons.MagazineCapacity == 6 &&
            Mathf.Abs(player.EffectiveMeleeStaminaCost - 20) < .001f && player.Weapons.Reserve == 58,
            "Deployment applies owned skills to actual weapons and melee cost without changing magazine capacity or ammo totals");
        Check(!level.RemoveSkill(skilled, HunterSkillId.SteadyAim) && !level.BuySkill(skilled, HunterSkillId.SteadyAim),
            "Session rejects skill management while playing");
        yield return Warp(new Vector2(4.5f, 2.05f));
        player.GetComponent<SkillFacing2D>().SetFacing(-1);
        Check(player.TryShoot(), "Skilled hunter fires through the real controller");
        float shotAt = Time.time;
        Check(!player.TryShoot(), "Skilled hunter cannot bypass cooldown with repeated input");
        yield return Until(() => player.TryShoot(), 2, "Second shot using improved cadence");
        Check(Time.time - shotAt >= .63f && Time.time - shotAt < .79f && player.Weapons.Magazine == 4,
            "Real controller fires the second shot at the improved interval before the base interval expires");
        yield return Until(() => player.TryReload(), 3, "Reload after skilled shot animation");
        float reloadAt = Time.time;
        level.TogglePause(); yield return null;
        float progress = player.Weapons.ReloadProgress;
        yield return new WaitForSecondsRealtime(.15f);
        Check(player.Weapons.IsReloading && player.Weapons.ReloadProgress == progress,
            "Pausing freezes the modified reload timer");
        level.TogglePause();
        yield return Until(() => !player.Weapons.IsReloading, 3, "Modified rifle reload completes");
        Check(Time.time - reloadAt >= 1.48f && Time.time - reloadAt < 1.85f && player.Weapons.Magazine == 6 && player.Weapons.Reserve == 56,
            "Real reload uses the 1.5-second skill duration and conserves rounds");
        Check(player.TrySwitchWeapon(1) && Mathf.Abs(player.Weapons.FireInterval - .28f) < .001f && player.Weapons.MagazineCapacity == 8,
            "Switching to the pistol retains gun skills without altering its eight-round magazine");
        float staminaBefore = player.Stamina;
        yield return Until(() => player.TryMelee(), 2, "Skilled melee attack");
        Check(Mathf.Abs(staminaBefore - player.Stamina - 20) < .001f && !player.TryMelee(),
            "A real melee attack costs twenty stamina and rejected repeated input spends nothing");
        Check(Mathf.Abs(player.Guns[0].FireInterval - .8f) < .001f && Mathf.Abs(player.Guns[0].ReloadSeconds - 2) < .001f &&
            Mathf.Abs(player.MeleeStaminaCost - 25) < .001f, "Skill deployment leaves authored gun and stamina settings unchanged");
        yield return SkillExtraction();
        Check(level.Career.Selected.Skills.Count == 3 && level.Career.Selected.SkillPoints == 1 && level.Career.Selected.IsMaxLevel,
            "A skilled hunter retains skills and earns only the newly crossed level's point on extraction");
        ClickButton("猎人技能"); yield return null;
        ClickButton("移除稳定瞄准"); ClickButton("移除快速装填"); ClickButton("移除格斗节能"); yield return null;
        Check(level.Career.Selected.Skills.Count == 0 && level.Career.Selected.SkillPoints == 4,
            "All removal buttons obey their per-skill refund rules");
        ClickButton("返回整备"); yield return null;
        // Also verify opening a different modal after skills restores its own controls.
        ClickButton("满级退役"); yield return null;
        Check(Array.Find(FindObjectsOfType<Button>(), button => button.name == "Confirm Retirement").interactable,
            "Retirement confirmation remains usable after closing the skill screen");
        ClickButton("取消，保留猎人"); yield return null;
        ClickButton("开始行动"); yield return null;
        Check(Mathf.Abs(player.Weapons.FireInterval - .8f) < .001f && Mathf.Abs(player.EffectiveMeleeStaminaCost - 25) < .001f,
            "Removing skills restores base combat values on the next deployment");
        float plainStamina = player.Stamina;
        Check(player.TryMelee() && Mathf.Abs(plainStamina - player.Stamina - 25) < .001f, "Unskilled melee again consumes the base twenty-five stamina");
        yield return SkillExtraction();
        level.BuySkill(skilled, HunterSkillId.EfficientMelee); level.SelectHunter(reserve); yield return null;
        ClickButton("开始行动"); yield return null;
        Check(player.Weapons.WeaponCount == 1 && Mathf.Abs(player.Weapons.FireInterval - .35f) < .001f &&
            Mathf.Abs(player.EffectiveMeleeStaminaCost - 25) < .001f && level.Career.Selected.Skills.Count == 0,
            "A reserve hunter never inherits another hunter's gun or stamina modifiers");
        yield return SkillExtraction();
        level.SelectHunter(skilled); level.StartRun(); yield return null;
        Check(Mathf.Abs(player.EffectiveMeleeStaminaCost - 20) < .001f, "Returning to the skilled hunter applies its own saved effect exactly once");
        body.position = new Vector2(4.5f, level.FallDeathY - 1);
        body.transform.position = new Vector3(body.position.x, body.position.y, 0);
        yield return Until(() => level.Phase == LevelPhase.Dead, 3, "Skilled hunter permanent death");
        reload.Load();
        Check(reload.Current.HunterCount == 1 && reload.Current.Selected.Id == reserve && reload.Current.Selected.Skills.Count == 0,
            "Real permanent death removes learned skills from disk without transferring them to the survivor");
        ClickButton("返回营地");
        yield return Until(() => level.Phase == LevelPhase.Ready, 10, "Return to camp after skill regression");
        yield return null;
    }

    private IEnumerator SkillExtraction()
    {
        level.Hunt.State.DefeatBoss(); level.Hunt.State.PickUpBounty(); level.Hunt.State.TickExtraction(10, true, false);
        Check(level.TryComplete(), "Skill scenario settles through the production extraction transaction");
        yield return null;
        ClickButton("返回营地");
        yield return Until(() => level.Phase == LevelPhase.Ready, 10, "Skill scenario returns to camp");
        yield return null;
    }

    private IEnumerator FieldSkillChecks()
    {
        var loot = level.SkillLoot;
        Check(loot && loot.Positions.Length == 3, "Existing authored scene automatically provides three configurable field-skill sockets");
        loot.SpawnChance = 1; loot.RandomizeSkills = false;
        string hunterId = level.Career.Selected.Id;
        Check(level.BuySkill(hunterId, HunterSkillId.QuickReload), "Field fixture buys one existing skill using the survivor's earned points");
        level.BuyEquipment(HunterEquipment.Rifle); level.ToggleEquipment(HunterEquipment.Rifle);
        int points = level.Career.Selected.SkillPoints;
        level.StartRun(); yield return null;
        Check(loot.Pickups.Count == 3 && loot.Pickups[0].Skill == HunterSkillId.EfficientMelee &&
            loot.Pickups[1].Skill == HunterSkillId.QuickReload && loot.Pickups[2].Skill == HunterSkillId.SteadyAim,
            "Configured field sockets roll once at deployment and support deterministic content for validation");
        var melee = loot.Pickups[0]; var reloadCard = loot.Pickups[1]; var aim = loot.Pickups[2];
        Check(!melee.TryCollect(player) && !melee.Used, "A remote skill card cannot be collected through its public action");
        yield return Warp(new Vector2(16, 2.05f));
        foreach (var enemy in FindObjectsOfType<PatrolEnemy2D>()) if (enemy.enabled) Freeze(enemy);
        yield return Until(() => player.CanInteract, 3, "Player ready beside a skill card");
        var wall = new GameObject("Skill pickup line-of-sight fixture");
        wall.transform.position = new Vector3(16.5f, 2.65f);
        var wallCollider = wall.AddComponent<BoxCollider2D>(); wallCollider.size = new Vector2(.15f, 1);
        Physics2D.SyncTransforms();
        Check(!melee.TryCollect(player) && loot.FindNearest(player) == null && !melee.Used, "Solid terrain blocks both skill hints and pickup interaction");
        wallCollider.enabled = false; Destroy(wall); Physics2D.SyncTransforms();
        Check(loot.FindNearest(player) == melee && melee.Hint(level.Career.Selected).StartsWith("E 拾取"),
            "An unobstructed nearby skill card exposes its exact skill and pickup prompt");
        level.TogglePause();
        Check(!melee.TryCollect(player) && !player.TryInteract() && !melee.Used, "Paused gameplay cannot consume a skill card");
        level.TogglePause(); yield return null;
        player.GetComponent<SkillFacing2D>().SetFacing(-1);
        Check(player.TryShoot() && !melee.TryCollect(player), "An active attack animation blocks field-skill pickup");
        yield return Until(() => player.TryReload(), 3, "Reload before pickup-block check");
        Check(!melee.TryCollect(player) && !melee.Used, "Reloading blocks field-skill pickup without consuming the card");
        yield return Until(() => !player.Weapons.IsReloading && player.CanInteract, 3, "Reload finishes before pickup transaction");
        Check(player.TryShoot(), "Field pickup fixture spends a round before updating skills");
        yield return Until(() => player.CanInteract, 3, "Shooting animation ends before pickup");
        int magazine = player.Weapons.Magazine, reserve = player.Weapons.Reserve;
        float health = actor.Health, stamina = player.Stamina;
        var previousWeapons = player.Weapons;
        using (var locked = new FileStream(level.CareerSavePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Check(!player.TryInteract() && !melee.Used && !level.Career.Selected.HasSkill(HunterSkillId.EfficientMelee) &&
                level.Career.Selected.FieldSkillValue == 0 && ReferenceEquals(previousWeapons, player.Weapons) &&
                Mathf.Abs(player.EffectiveMeleeStaminaCost - 25) < .001f && level.Phase == LevelPhase.Playing,
                "Failed field pickup save retains the card, profile and exact live combat state for retry");
        }
        yield return Capture("field-skill-ready");
        Check(player.TryInteract() && melee.Used && level.Career.Selected.HasSkill(HunterSkillId.EfficientMelee) &&
            level.Career.Selected.FieldSkillValue == 1 && level.Career.Selected.SkillPoints == points &&
            player.Weapons.Magazine == magazine && player.Weapons.Reserve == reserve && actor.Health == health && player.Stamina == stamina &&
            Mathf.Abs(player.EffectiveMeleeStaminaCost - 20) < .001f,
            "Retrying real E interaction grants the skill immediately while preserving points, ammo, health and stamina");
        Check(!melee.TryCollect(player) && level.Career.Selected.FieldSkillValue == 1,
            "A used card cannot grant its skill or acquisition value twice");
        float beforeMelee = player.Stamina;
        Check(player.TryMelee() && Mathf.Abs(beforeMelee - player.Stamina - 20) < .001f,
            "A field-acquired character skill changes the next real melee attack in the same run");
        yield return Capture("field-skill-acquired");
        yield return Until(() => player.CanInteract, 3, "Melee animation finishes before moving between sockets");
        yield return Warp(new Vector2(37, 2.05f));
        foreach (var enemy in FindObjectsOfType<PatrolEnemy2D>()) if (enemy.enabled) Freeze(enemy);
        yield return Until(() => player.CanInteract, 3, "Player reaches the duplicate skill card");
        Check(!player.TryInteract() && !reloadCard.Used && reloadCard.Hint(level.Career.Selected).Contains("已学会") &&
            level.Career.Selected.FieldSkillValue == 1, "An already-owned skill is neither stacked nor converted to points and its card remains intact");
        yield return Warp(new Vector2(55, 2.05f));
        foreach (var enemy in FindObjectsOfType<PatrolEnemy2D>()) if (enemy.enabled) Freeze(enemy);
        yield return Until(() => player.CanInteract && player.Weapons.CanShoot, 3, "Player reaches the gun skill card with the previous shot cooldown finished");
        player.GetComponent<SkillFacing2D>().SetFacing(-1);
        Check(player.TryShoot(), "Gun pickup fixture has a partially spent rifle magazine");
        yield return Until(() => player.CanInteract, 3, "Gun animation finishes before cadence update");
        magazine = player.Weapons.Magazine; reserve = player.Weapons.Reserve;
        Check(player.TryInteract() && aim.Used && Mathf.Abs(player.Weapons.FireInterval - .64f) < .001f &&
            player.Weapons.Magazine == magazine && player.Weapons.Reserve == reserve && level.Career.Selected.FieldSkillValue == 4,
            "A field-acquired gun skill applies immediately to the current partially spent loadout");
        yield return Until(() => player.TryShoot(), 2, "Real shot after live field-skill update");
        float shotAt = Time.time;
        yield return Until(() => player.TryShoot(), 2, "Second shot with newly acquired cadence");
        Check(Time.time - shotAt >= .63f && Time.time - shotAt < .79f,
            "Field-acquired steady aim changes the actual shot cadence before returning to camp");
        yield return Until(() => player.CanInteract, 3, "Attack completes before weapon switch");
        Check(player.TrySwitchWeapon(1) && Mathf.Abs(player.Weapons.FireInterval - .28f) < .001f && player.Weapons.Magazine == 8,
            "Live skill update affects the carried pistol without touching its independent magazine");
        yield return Warp(new Vector2(17, 2.05f));
        Check(loot.Pickups[0] == melee && melee.Used && aim.Used && !reloadCard.Used,
            "Leaving and reloading terrain preserves socket identity, drawn skills and consumed state");
        var diskRun = new HunterCareer(JsonUtility.FromJson<HunterCareerData>(File.ReadAllText(level.CareerSavePath)));
        Check(diskRun.InRun && diskRun.Selected.FieldSkillValue == 4 && diskRun.Selected.Skills.Count == 3,
            "Immediate pickup saves preserve the active-run marker so restart cannot bypass permanent loss");
        level.Hunt.State.DefeatBoss(); level.Hunt.State.PickUpBounty(); level.Hunt.State.TickExtraction(10, true, false);
        using (var locked = new FileStream(level.CareerSavePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Check(!level.TryComplete() && level.Phase == LevelPhase.Error && level.Career.InRun &&
                level.Career.Selected.FieldSkillValue == 4 && !reloadCard.TryCollect(player),
                "Extraction save failure freezes field-skill progress without consuming further pickups or dropping learned skills");
        }
        level.RetryCareerSave(); yield return null;
        Check(level.Phase == LevelPhase.Won && !level.Career.InRun && level.Career.Selected.FieldSkillValue == 4 &&
            level.Career.Selected.Skills.Count == 3, "Retrying extraction retains field skills and closes the run exactly once");
        ClickButton("返回营地"); yield return Until(() => level.Phase == LevelPhase.Ready, 10, "Camp after field-skill extraction"); yield return null;
        var reloaded = new HunterCareerStore(level.CareerSavePath, json => JsonUtility.FromJson<HunterCareerData>(json), data => JsonUtility.ToJson(data, true));
        reloaded.Load();
        Check(reloaded.Current.Selected.Skills.Count == 3 && reloaded.Current.Selected.FieldSkillValue == 4 &&
            reloaded.Current.Selected.SkillPoints == points + 2, "A successful extraction persists field skills alongside ordinary level gains");
        ClickButton("猎人技能"); yield return null;
        int beforeRefund = level.Career.Selected.SkillPoints;
        ClickButton("移除稳定瞄准"); ClickButton("移除格斗节能"); yield return null;
        Check(level.Career.Selected.SkillPoints == beforeRefund + 2 && level.Career.Selected.FieldSkillValue == 4 && level.Career.Selected.Skills.Count == 1,
            "Camp removal of field skills follows the existing one-point and odd-price refund rules");
        yield return Capture("field-skills-camp");
        ClickButton("返回整备"); yield return null;
        level.RecruitHunter(); string survivor = level.Career.Selected.Id;
        level.BuyEquipment(HunterEquipment.Pistol); level.ToggleEquipment(HunterEquipment.Pistol);
        level.SelectHunter(hunterId); level.StartRun(); yield return null;
        Check(loot.Pickups[0] != melee && !loot.Pickups[0].Used && loot.Pickups[0].Skill == HunterSkillId.EfficientMelee &&
            Mathf.Abs(player.Weapons.FireInterval - .8f) < .001f && Mathf.Abs(player.EffectiveMeleeStaminaCost - 25) < .001f,
            "A fresh run rerolls sockets and starts with only the skills retained in camp");
        yield return Warp(new Vector2(17, 2.05f));
        foreach (var enemy in FindObjectsOfType<PatrolEnemy2D>()) if (enemy.enabled) Freeze(enemy);
        yield return Until(() => player.CanInteract, 3, "Second run pickup ready");
        Check(player.TryInteract() && level.Career.Selected.FieldSkillValue == 5 && Mathf.Abs(player.EffectiveMeleeStaminaCost - 20) < .001f,
            "A removed skill can be acquired in a later run and applies once");
        body.position = new Vector2(4.5f, level.FallDeathY - 1); body.transform.position = new Vector3(body.position.x, body.position.y, 0);
        yield return Until(() => level.Phase == LevelPhase.Dead, 3, "Field-skilled hunter permanent death");
        reloaded.Load();
        Check(reloaded.Current.HunterCount == 1 && reloaded.Current.Selected.Id == survivor && reloaded.Current.Selected.Skills.Count == 0 &&
            reloaded.Current.Selected.FieldSkillValue == 0 && !loot.Pickups[0].TryCollect(player),
            "Real death deletes acquired field skills and value from disk without giving them to the reserve hunter");
        ClickButton("返回营地"); yield return Until(() => level.Phase == LevelPhase.Ready, 10, "Camp after field-skilled death"); yield return null;
        loot.SpawnChance = 0; level.StartRun(); yield return null;
        Check(loot.Pickups.Count == 3 && loot.Pickups[0].Skill == 0 && loot.Pickups[1].Skill == 0 && loot.Pickups[2].Skill == 0 &&
            Mathf.Abs(player.EffectiveMeleeStaminaCost - 25) < .001f, "Zero spawn chance leaves empty sockets and an unskilled successor has base stats");
        yield return SkillExtraction();
        loot.SpawnChance = .75f; loot.RandomizeSkills = true; RestoreFrozen();
    }

    private IEnumerator RevivalChecks()
    {
        var loot = level.SkillLoot;
        var originalPositions = (Vector2[])loot.Positions.Clone();
        loot.SpawnChance = 0;
        string veteran = level.Career.Selected.Id;
        for (int run = 0; run < 2; run++) { level.StartRun(); yield return null; yield return SkillExtraction(); }
        Check(level.Career.Selected.SkillPoints == 6, "Revival fixture earns enough points through normal extraction settlements");
        ClickButton("猎人技能"); yield return null;
        ClickButton("购买死灵法师"); ClickButton("购买格斗节能"); yield return null;
        Check(level.Career.Selected.HasSkill(HunterSkillId.Necromancer) && level.Career.Selected.SkillPoints == 1,
            "The fourth camp skill card purchases necromancer for four points");
        yield return Capture("necromancer-shop");
        ClickButton("返回整备"); yield return null;
        level.RecruitHunter(); string survivor = level.Career.Selected.Id;
        level.BuyEquipment(HunterEquipment.Pistol); level.ToggleEquipment(HunterEquipment.Pistol); level.SelectHunter(veteran);
        level.StartRun(); yield return null;
        yield return Warp(new Vector2(11, 2.05f));
        var guard = Guard(1).GetComponent<PatrolEnemy2D>();
        Freeze(guard);
        yield return Until(() => player.CanInteract, 3, "Revival fixture can prepare ammunition and stamina");
        player.GetComponent<SkillFacing2D>().SetFacing(-1);
        Check(player.TryShoot(), "Revival fixture spends ammunition before death");
        yield return Until(() => player.TryMelee(), 3, "Revival fixture spends stamina before death");
        yield return Until(() => player.CanInteract, 3, "Revival fixture finishes attack animation");
        int magazine = player.Weapons.Magazine, reserve = player.Weapons.Reserve;
        float stamina = player.Stamina;
        var guardBody = guard.GetComponent<Rigidbody2D>();
        guardBody.position = new Vector2(12.2f, body.position.y); guard.transform.position = guardBody.position;
        RestoreFrozen();
        // Production AI components isolate all three enemy classes beside the same revival site.
        var boss = new GameObject("Revival Boss Probe").AddComponent<HuntEnemy2D>();
        boss.transform.position = new Vector2(12.6f, body.position.y); boss.Configure(true, level.Hunt.EnemySprite, level.Hunt.EnemyMaterial);
        var reinforcement = new GameObject("Revival Reinforcement Probe").AddComponent<HuntEnemy2D>();
        reinforcement.transform.position = new Vector2(9.6f, body.position.y); reinforcement.Configure(false, level.Hunt.EnemySprite, level.Hunt.EnemyMaterial);
        Physics2D.SyncTransforms();
        yield return Until(() => guard.Target && boss.IsAlerted && reinforcement.IsAlerted, 2, "All enemy classes acquire the player before death");
        boss.Actor.BeHit(new SkillHitData { attackValue = 50 });
        Check(boss.IsEnraged && boss.BossCombat.Clock.Active == HunterBossAbility.RagePulse,
            "The revival fixture begins with a genuine threshold-triggered boss rage warning");
        level.Hunt.State.Investigate("revival-clue"); level.Hunt.State.DefeatBoss(); level.Hunt.State.PickUpBounty();
        Vector2 deathPosition = body.position;
        int money = level.Career.Credits, losses = level.Career.Snapshot().Losses, points = level.Career.Selected.SkillPoints;
        actor.RestoreHealth(); actor.BeHit(new SkillHitData { attackValue = actor.MaxHealth + 1 });
        Check(level.Phase == LevelPhase.Reviving && !actor.IsAlive && !level.Career.Selected.HasSkill(HunterSkillId.Necromancer) &&
            level.Career.InRun && !player.TryShoot() && !level.TryComplete(),
            "Lethal damage saves necromancer consumption before reviving and blocks gameplay during the transition");
        level.ReturnToTitle(); level.RetryCheckpoint();
        Check(level.Phase == LevelPhase.Reviving && level.Career.Selected.Id == veteran, "Camp and checkpoint actions cannot interrupt a pending revival");
        yield return Until(() => level.Phase == LevelPhase.Playing && actor.IsAlive, 5, "Automatic necromancer revival");
        Check(Mathf.Abs(actor.Health - actor.MaxHealth * .5f) < .001f && Vector2.Distance(body.position, deathPosition) < .1f &&
            player.ControlEnabled && body.simulated && Mathf.Abs(player.Stamina - stamina) < .001f &&
            player.Weapons.Magazine == magazine && player.Weapons.Reserve == reserve,
            "Automatic revival restores half health at the death site without refilling ammunition or stamina");
        Check(level.Career.Selected.Id == veteran && level.Career.Selected.HasSkill(HunterSkillId.EfficientMelee) &&
            level.Career.Selected.SkillPoints == points && level.Career.Credits == money && level.Career.Snapshot().Losses == losses &&
            level.DeathCount == 1 && level.Career.InRun && !level.Career.CanRevive(veteran),
            "Revival keeps hunter identity, regular skills, equipment and money while consuming exactly one life");
        Check(level.Hunt.State.Stage == HuntStage.BountyAvailable && level.Hunt.State.SecondWaveStarted && level.Hunt.State.ClueCount == 1 &&
            Vector2.Distance(level.Hunt.BountyPosition, deathPosition + Vector2.up * .5f) < .15f,
            "Revival preserves mission and wave progress while the carried bounty drops at the death site");
        Check(level.IsPlayerUndetected && level.RevivalGraceRemaining > 2.8f && !actor.IsInvulnerable &&
            !guard.Target && !guard.IsAlerted && !guard.GetComponent<MapStreamedEntity>().CombatPinned && !boss.IsAlerted && !reinforcement.IsAlerted &&
            !boss.BossCombat.Clock.IsWinding && boss.BossCombat.ActiveProjectiles == 0,
            "Revival clears existing targets for guards, bosses and reinforcements and grants detection suppression rather than invulnerability");
        yield return Capture("necromancer-revived");
        Check(player.TryShoot(), "A revived player can act during the detection suppression window");
        yield return new WaitForSeconds(.15f);
        Check(!guard.Target && !boss.IsAlerted && !reinforcement.IsAlerted && actor.Health == actor.MaxHealth * .5f,
            "Existing enemy windups cannot hit or reacquire the revived player during suppression");
        level.TogglePause(); float grace = level.RevivalGraceRemaining;
        yield return new WaitForSecondsRealtime(.2f);
        Check(level.IsPlayerUndetected && Mathf.Abs(level.RevivalGraceRemaining - grace) < .001f,
            "Pausing preserves the remaining three-second revival detection window");
        level.TogglePause();
        actor.BeHit(new SkillHitData { attackValue = 5 });
        Check(actor.Health == actor.MaxHealth * .5f - 5 && level.IsPlayerUndetected,
            "Detection suppression does not prevent other valid damage");
        yield return Until(() => !level.IsPlayerUndetected && boss.IsAlerted && reinforcement.IsAlerted && guard.Target, 5,
            "All enemy classes resume detection after revival protection expires");
        Check(level.RevivalGraceRemaining == 0, "The temporary detection effect expires using gameplay time");
        Check(!boss.IsEnraged && !HunterBossAbilityClock.IsRageAbility(boss.BossCombat.Clock.Active),
            "Revival detection grace suppresses boss attacks without extending its three-second rage duration");
        var savedRevival = new HunterCareer(JsonUtility.FromJson<HunterCareerData>(File.ReadAllText(level.CareerSavePath)));
        Check(savedRevival.InRun && savedRevival.Selected.Id == veteran && !savedRevival.Selected.HasSkill(HunterSkillId.Necromancer),
            "The real revival consumption is durable while keeping the expedition marked unfinished");
        actor.RestoreHealth(); actor.BeHit(new SkillHitData { attackValue = actor.MaxHealth + 1 }); yield return null;
        Check(level.Phase == LevelPhase.Dead && level.Career.Selected.Id == survivor && level.Career.HunterCount == 1 && level.DeathCount == 2,
            "The next lethal hit permanently loses a hunter whose revival skill has been consumed");
        Destroy(boss.gameObject); Destroy(reinforcement.gameObject);
        ClickButton("返回营地"); yield return Until(() => level.Phase == LevelPhase.Ready, 10, "Camp after exhausted revival"); yield return null;
        Check(!level.IsPlayerUndetected && level.Career.Selected.Skills.Count == 0, "A reserve hunter inherits neither the consumed skill nor its detection suppression");

        // The fourth deterministic socket selects the fourth catalogue entry, at an accessible test location.
        loot.Positions = new[] { new Vector2(17, 2.6f), new Vector2(37, 2.6f), new Vector2(55, 2.6f), new Vector2(6, 2.6f) };
        loot.SpawnChance = 1; loot.RandomizeSkills = false; level.StartRun(); yield return null;
        yield return Warp(new Vector2(6, 2.05f));
        Check(loot.Pickups[3].Skill == HunterSkillId.Necromancer && player.TryInteract() && level.Career.CanRevive(survivor),
            "A necromancer card acquired in the field can revive immediately without any level points");
        magazine = player.Weapons.Magazine; reserve = player.Weapons.Reserve;
        using (var locked = new FileStream(level.CareerSavePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            actor.RestoreHealth(); actor.BeHit(new SkillHitData { attackValue = actor.MaxHealth + 1 });
            Check(level.Phase == LevelPhase.Error && level.HasCareerSaveError && !actor.IsAlive && !body.simulated &&
                level.Career.CanRevive(survivor) && level.Career.Selected.FieldSkillValue == 4,
                "A failed revival save freezes the dead actor and retains the unconsumed skill for retry");
            level.StartRun(); level.ReturnToTitle(); level.TogglePause(); level.RetryCheckpoint();
            Check(level.Phase == LevelPhase.Error && !actor.IsAlive && level.Career.InRun,
                "Gameplay, camp and retry-checkpoint commands cannot bypass an unsaved revival");
            yield return Capture("necromancer-save-error");
        }
        ClickButton("重试存档"); level.RetryCareerSave();
        yield return Until(() => level.Phase == LevelPhase.Playing && actor.IsAlive, 5, "Revival persistence retry succeeds");
        Check(!level.HasCareerSaveError && !level.Career.Selected.HasSkill(HunterSkillId.Necromancer) &&
            level.Career.Selected.SkillPoints == 0 && level.Career.Selected.FieldSkillValue == 4 && level.DeathCount == 1 &&
            player.Weapons.Magazine == magazine && player.Weapons.Reserve == reserve,
            "Retrying a field revival consumes it once without refunds, duplicate deaths or inventory changes");
        yield return SkillExtraction();
        var reload = new HunterCareerStore(level.CareerSavePath, json => JsonUtility.FromJson<HunterCareerData>(json), data => JsonUtility.ToJson(data, true)); reload.Load();
        Check(reload.Current.Selected.Id == survivor && !reload.Current.Selected.HasSkill(HunterSkillId.Necromancer) &&
            reload.Current.Selected.SkillPoints == 2 && reload.Current.Selected.FieldSkillValue == 4 && !level.IsPlayerUndetected,
            "Extraction after revival retains the hunter and earned levels but never restores the consumed skill or suppression");

        loot.SpawnChance = 0; level.StartRun(); yield return null; yield return SkillExtraction();
        Check(level.BuySkill(survivor, HunterSkillId.Necromancer), "A surviving hunter can buy another copy after earning new skill points");
        level.StartRun(); yield return null;
        yield return Warp(new Vector2(8.5f, 2.05f));
        yield return Until(() => player.IsGrounded, 2, "Record a safe landing before the void-fall fixture"); yield return null;
        Vector2 landing = body.position;
        body.position = new Vector2(25, level.FallDeathY - 1); body.transform.position = new Vector3(body.position.x, body.position.y, 0);
        yield return Until(() => level.Phase == LevelPhase.Reviving, 3, "Void fall enters revival");
        yield return Until(() => level.Phase == LevelPhase.Playing && actor.IsAlive, 5, "Void revival returns to safe terrain");
        Check(Vector2.Distance(body.position, landing) < .15f && body.position.y > level.FallDeathY && level.DeathCount == 1 &&
            !level.Career.CanRevive(survivor) && level.IsPlayerUndetected,
            "A lethal fall consumes the skill and returns to the most recent landing instead of reviving into the void");
        yield return Capture("necromancer-fall-revival");
        yield return SkillExtraction();
        loot.Positions = originalPositions; loot.SpawnChance = .75f; loot.RandomizeSkills = true;
    }

    private IEnumerator ItemChecks()
    {
        float recovery = player.HealthRecoveryRate; player.HealthRecoveryRate = 0;
        level.SkillLoot.SpawnChance = 0;
        string id = level.Career.Selected.Id;
        ClickButton("道具整备"); yield return null;
        Check(!FindButton("开始行动").interactable && !FindButton("猎人技能").interactable,
            "Item management isolates camp controls while its panel is open");
        int credits = level.Career.Credits;
        using (var locked = new FileStream(level.CareerSavePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            ClickButton("购买急救包");
            Check(level.Career.Credits == credits && level.Career.WarehouseItems.Count == 0,
                "The real item-shop button does not charge or grant inventory when saving fails");
        }
        foreach (var name in new[] { "急救包", "飞刀", "生命针剂", "炸药束" })
        { ClickButton("购买" + name); yield return null; ClickButton("装备" + name); yield return null; }
        Check(level.Career.Selected.Items.Count == 4 && level.Career.WarehouseItems.Count == 0 && level.Career.Credits == credits - 165,
            "Camp buttons buy and equip two regular tools plus two consumables with separate capacity");
        yield return Capture("items-shop");
        ClickButton("返回整备"); yield return null;
        level.StartRun(); yield return null; yield return Warp(new Vector2(6, 2.05f));
        foreach (var guard in FindObjectsOfType<PatrolEnemy2D>()) Freeze(guard);
        Check(player.Items.Count == 4 && player.Items[0].Uses == 3 && !player.TryUseItem(),
            "Deployment loads the chosen hunter's items and full health does not waste a medical charge");
        actor.RestoreHealth(.2f);
        Check(player.TryUseItem() && player.IsUsingItem && !player.TryUseItem() && !player.TryShoot() && !player.TryMelee() &&
            !player.TryReload() && !player.TrySelectItem(1) && !player.TryInteract(),
            "Medical preparation blocks repeated use, weapon actions, item switches and interaction");
        player.SetInput(1, false, false); yield return new WaitForSeconds(.2f);
        Check(body.velocity.x > 1.3f && body.velocity.x < 1.7f && player.ItemMoveMultiplier == .5f,
            "Preparing a medical item halves the player's actual walking speed");
        player.SetInput(0, false, false);
        level.TogglePause(); float progress = player.ItemUseProgress;
        yield return new WaitForSecondsRealtime(.2f);
        Check(player.IsUsingItem && Mathf.Abs(progress - player.ItemUseProgress) < .001f && player.Items[0].Uses == 3,
            "Pause freezes item preparation without consuming the pending charge");
        level.TogglePause();
        actor.BeHit(new SkillHitData { attackValue = 5 });
        Check(!player.IsUsingItem && player.Items[0].Uses == 3 && actor.Health == 15 && player.ItemMoveMultiplier == 1,
            "Damage interrupts preparation, restores movement speed and retains the medical charge");
        yield return Until(() => player.CanInteract, 2, "Finish item interruption hit stun");
        actor.RestoreHealth(.2f);
        using (var locked = new FileStream(level.CareerSavePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Check(player.TryUseItem(), "An interrupted medical preparation can be restarted");
            yield return Until(() => !player.IsUsingItem, 4, "A locked medical consumption is rejected at completion");
            Check(actor.Health == 20 && player.Items[0].Uses == 3 && level.Career.Selected.Items[0].Uses == 3 &&
                player.ItemStatus.Contains("失败"), "Failed consumption saves grant no healing and retain both runtime and persisted charges");
        }
        float stamina = player.Stamina; int magazine = player.Weapons.Magazine, reserve = player.Weapons.Reserve;
        Check(player.TryUseItem(), "The same medical item can be retried after storage becomes writable");
        yield return new WaitForSeconds(.3f); yield return Capture("item-healing");
        yield return Until(() => !player.IsUsingItem, 4, "Medical retry completes");
        Check(actor.Health == 70 && player.Items[0].Uses == 2 && level.Career.Selected.Items[0].Uses == 2 && player.Stamina == stamina &&
            player.Weapons.Magazine == magazine && player.Weapons.Reserve == reserve,
            "Healing crosses health-segment boundaries, consumes one charge and preserves stamina and ammunition; health=" + actor.Health +
            ", uses=" + player.Items[0].Uses + ", stamina=" + player.Stamina + ", status=" + player.ItemStatus);
        for (int i = 0; i < 2; i++)
        {
            actor.RestoreHealth(.1f); Check(player.TryUseItem(), "Another medical use starts while a charge remains");
            yield return Until(() => !player.IsUsingItem, 4, "Spend remaining medical charges");
        }
        Check(player.Items[0].Uses == 0 && player.Items.Count == 4 && !player.TryUseItem(),
            "An exhausted medical tool remains in the real loadout and cannot be used again");
        actor.RestoreHealth(.25f); player.TrySelectItem(2); Check(player.TryUseItem(), "The medical consumable starts its one-second preparation");
        yield return Until(() => !player.IsUsingItem, 3, "Health-shot consumption completes");
        Check(actor.Health == 100 && player.Items.Count == 3 && player.Items.TrueForAll(s => s.Id != HunterItemId.HealthShot),
            "The health shot heals to the maximum and disappears from its slot");
        var disk = new HunterCareer(JsonUtility.FromJson<HunterCareerData>(File.ReadAllText(level.CareerSavePath)));
        Check(disk.InRun && disk.Selected.Items[0].Uses == 0 && disk.Selected.Items.Count == 3,
            "Real item consumption is durable before its effect is published and keeps the expedition marker");

        yield return Warp(new Vector2(6, 2.05f));
        var target = ItemTarget(new Vector2(9, 2.05f));
        player.GetComponent<SkillFacing2D>().SetFacing(1); player.TrySelectItem(1);
        Check(player.TryUseItem(), "A regular throwing knife begins its preparation");
        yield return Until(() => target.Health < 100, 3, "The real thrown knife hits its target");
        Check(target.Health == 70 && player.Items[1].Uses == 2 && player.Stamina == stamina && player.Weapons.Magazine == magazine,
            "A swept throwing knife deals configured damage and consumes no stamina or gun ammunition");
        var wall = new GameObject("Item Cover Probe"); wall.transform.position = new Vector2(7.5f, 3.5f);
        wall.AddComponent<BoxCollider2D>().size = new Vector2(.2f, 3);
        Physics2D.SyncTransforms();
        Check(player.TryUseItem(), "A second throwing knife can be thrown toward cover");
        yield return Until(() => !player.IsUsingItem && !level.GetComponentInChildren<HunterThrowable2D>(), 3, "Knife stops at cover");
        Check(target.Health == 70 && player.Items[1].Uses == 1, "Solid cover blocks a thrown knife without refunding the already thrown charge");

        wall.transform.position = new Vector2(8.5f, 3.5f);
        var front = ItemTarget(new Vector2(7.5f, 2.05f));
        front.gameObject.AddComponent<CircleCollider2D>().offset = Vector2.up * .75f;
        Physics2D.SyncTransforms();
        player.TrySelectItem(2); Check(player.TryUseItem(), "Dynamite uses its own preparation and consumable slot");
        yield return Until(() => level.GetComponentInChildren<HunterThrowable2D>(), 3, "Dynamite enters real flight after its use is saved");
        var grenade = level.GetComponentInChildren<HunterThrowable2D>();
        level.TogglePause(); Vector3 location = grenade.transform.position;
        yield return new WaitForSecondsRealtime(.2f);
        Check(grenade && grenade.transform.position == location && front.Health == 100,
            "Pause freezes throwable flight and explosion timing");
        level.TogglePause();
        yield return Until(() => !grenade, 3, "Dynamite detonates after landing");
        Check(front.Health == 40 && target.Health == 70 && actor.Health == 40 && player.Items.Count == 2,
            "Dynamite damages each visible actor once including its owner, respects cover and removes the spent consumable");
        yield return Capture("item-dynamite");
        Destroy(front.gameObject); Destroy(target.gameObject); Destroy(wall); yield return null;
        RestoreFrozen();
        yield return SkillExtraction();
        Check(level.Career.Selected.Items.Count == 2 && level.Career.Selected.Items[0].Uses == 0 && level.Career.Selected.Items[1].Uses == 1,
            "Extraction preserves exhausted and partially used tools without restoring consumables");
        ClickButton("道具整备"); yield return null; ClickButton("卸下急救包"); yield return null;
        Check(level.Career.WarehouseItems[0].Uses == 0 && level.Career.Selected.Items.Count == 1,
            "Camp unloading returns an exhausted medical tool with zero uses");
        ClickButton("购买匕首"); yield return null; ClickButton("装备匕首"); yield return null;
        yield return Capture("items-returned");
        ClickButton("返回整备"); yield return null;
        level.RecruitHunter(); string survivor = level.Career.Selected.Id;
        level.BuyEquipment(HunterEquipment.Pistol); level.ToggleEquipment(HunterEquipment.Pistol); level.SelectHunter(id);
        level.StartRun(); yield return null; yield return Warp(new Vector2(6, 2.05f));
        foreach (var guard in FindObjectsOfType<PatrolEnemy2D>()) Freeze(guard);
        target = ItemTarget(new Vector2(7.1f, 2.05f));
        player.TrySelectItem(1); player.GetComponent<SkillFacing2D>().SetFacing(1); stamina = player.Stamina;
        Check(player.Items[0].Uses == 1 && player.TryUseItem(), "Redeployment preserves a partial tool and permits an equipped dagger attack");
        yield return Until(() => target.Health < 100, 3, "Dagger connects through the real melee timeline");
        Check(target.Health == 70 && Mathf.Abs(player.Stamina - (stamina - 25)) < .01f && player.Items[1].Uses == 1,
            "Dagger attacks deal thirty damage, spend twenty-five stamina and retain unlimited tool uses");
        Destroy(target.gameObject);
        RestoreFrozen();
        actor.RestoreHealth(); actor.BeHit(new SkillHitData { attackValue = 1000 }); yield return null;
        Check(level.Phase == LevelPhase.Dead && level.Career.Selected.Id == survivor && level.Career.Selected.Items.Count == 0 &&
            level.Career.WarehouseItems.Count == 1 && level.Career.WarehouseItems[0].Uses == 0,
            "Real permanent death loses carried tools without changing the reserve hunter or warehouse");
        ClickButton("返回营地"); yield return Until(() => level.Phase == LevelPhase.Ready, 10, "Camp after item-owner death");
        player.HealthRecoveryRate = recovery; level.SkillLoot.SpawnChance = .75f;
    }

    private IEnumerator SupplyChecks()
    {
        var loot = level.ItemLoot;
        Check(loot && loot.SupplyPositions.Length == 3 && loot.PickupPositions.Length == 3,
            "Existing authored scenes gain three default supply boxes and three consumable sockets");
        var originalSupply = loot.SupplyPositions; var originalPickup = loot.PickupPositions;
        var originalPool = loot.Consumables; float originalChance = loot.PickupChance;
        float recovery = player.HealthRecoveryRate; player.HealthRecoveryRate = 0;
        float skillChance = level.SkillLoot.SpawnChance; level.SkillLoot.SpawnChance = 0;
        loot.SupplyPositions = new[] { new Vector2(10, 2.6f), new Vector2(12, 2.6f), new Vector2(14, 2.6f), new Vector2(16, 2.6f) };
        loot.PickupPositions = new[] { new Vector2(10.8f, 2.6f), new Vector2(55, 2.6f) };
        loot.PickupChance = 1;
        loot.Consumables = new[] { new HunterConsumableWeight { Item = HunterItemId.Dynamite, Weight = 1 } };
        string id = level.Career.Selected.Id;
        foreach (var item in new[] { HunterItemId.FirstAid, HunterItemId.ThrowingKnife, HunterItemId.HealthShot, HunterItemId.HealthShot })
            if (!level.BuyItem(item) || !level.EquipItem(id, item)) throw new Exception("Cannot equip supply fixture: " + item);
        level.StartRun(); yield return null;
        var first = loot.Points[0]; var pickup = loot.Points[4]; var remote = loot.Points[5];
        Check(loot.Points.Count == 6 && pickup.Available && remote.Available && pickup.Item == HunterItemId.Dynamite && !first.Used,
            "A deterministic field configuration spawns boxes and weighted loose consumables once at deployment");
        Check(!first.TryCollect(player) && !remote.TryCollect(player), "Distant item points reject direct pickup commands");
        yield return Warp(new Vector2(9.4f, 2.05f));
        foreach (var guard in FindObjectsOfType<PatrolEnemy2D>()) Freeze(guard);
        var wall = new GameObject("Supply visibility probe"); wall.transform.position = new Vector3(9.5f, 2.65f);
        var shape = wall.AddComponent<BoxCollider2D>(); shape.size = new Vector2(.15f, 1);
        Physics2D.SyncTransforms();
        Check(!first.TryCollect(player) && loot.FindNearest(player) == null && !first.Used,
            "A solid wall blocks item interaction and its nearby hint");
        shape.enabled = false; Destroy(wall); Physics2D.SyncTransforms();
        Check(loot.FindNearest(player) == first && !player.TryInteract() && !first.Used &&
            player.NearbyLootHint().Contains("已满"), "Full tools and consumable slots keep a supply box available with an explanatory hint");
        level.TogglePause();
        Check(!first.TryCollect(player) && !player.TryInteract() && !first.Used, "Pause blocks item collection without consuming the point");
        level.TogglePause(); yield return null;
        player.GetComponent<SkillFacing2D>().SetFacing(-1);
        Check(player.TryShoot() && !first.TryCollect(player), "Weapon attack animation blocks item supply");
        yield return Until(() => player.TryReload(), 3, "Reload for item interaction gate");
        Check(!first.TryCollect(player), "Reloading blocks item supply");
        yield return Until(() => !player.Weapons.IsReloading && player.CanInteract, 3, "Finish supply fixture reload");
        player.TryShoot(); yield return Until(() => player.CanInteract, 3, "Leave a spent round before item supply");
        for (int i = 0; i < 2; i++)
        {
            actor.RestoreHealth(.1f); player.TrySelectItem(0);
            if (!player.TryUseItem()) throw new Exception("Supply fixture medical use failed");
            if (i == 0) Check(!first.TryCollect(player) && !first.Used, "Medical preparation blocks supply pickup while retaining the box");
            yield return Until(() => !player.IsUsingItem, 4, "Spend medical charges before refilling");
        }
        actor.RestoreHealth(.2f); player.TrySelectItem(2); player.TryUseItem();
        yield return Until(() => !player.IsUsingItem, 3, "Empty one consumable slot");
        player.TrySelectItem(1); player.TryUseItem();
        yield return Until(() => !player.IsUsingItem && !level.GetComponentInChildren<HunterThrowable2D>(), 3, "Spend throwing-tool charge");
        Check(player.Items[0].Uses == 1 && player.Items[1].Uses == 2 && player.Items.Count == 3,
            "Real medical and throwing actions create two incomplete tools and an empty consumable slot");
        Check(player.NearbyLootHint() == first.Hint(level.Career.Selected),
            "The supply transaction fixture targets the nearby item box rather than the separate ammunition box");
        int magazine = player.Weapons.Magazine, reserve = player.Weapons.Reserve;
        float health = actor.Health, stamina = player.Stamina; int credits = level.Career.Credits;
        string before = File.ReadAllText(level.CareerSavePath);
        using (var locked = new FileStream(level.CareerSavePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Check(!player.TryInteract() && !first.Used && player.Items[0].Uses == 1 && player.Items[1].Uses == 2 &&
                level.Career.Selected.Items[0].Uses == 1 && File.ReadAllText(level.CareerSavePath) == before && level.StatusMessage.Contains("保存失败"),
                "A real locked save retains the supply box, live inventory and persisted counts for retry; hint=" +
                player.NearbyLootHint() + ", status=" + level.StatusMessage);
        }
        var randomState = JsonUtility.ToJson(UnityEngine.Random.state);
        Check(player.TryInteract() && first.Used && player.Items[0].Uses + player.Items[1].Uses == 4 && player.Items.Count == 3 &&
            JsonUtility.ToJson(UnityEngine.Random.state) == randomState,
            "Retrying E commits one fixed tool increment before filling consumables and reuses the failed random draw");
        Check(player.Weapons.Magazine == magazine && player.Weapons.Reserve == reserve && actor.Health == health &&
            player.Stamina == stamina && level.Career.Credits == credits, "Tool supply grants no ammunition, healing, stamina or currency");
        Check(!first.TryCollect(player), "A used item box cannot be claimed twice in one run");
        yield return Capture("item-supply-refilled");
        yield return Warp(new Vector2(10, 2.05f));
        Check(loot.FindNearest(player) == pickup && player.NearbyLootHint().Contains("炸药束"),
            "An unused nearby consumable takes interaction priority over a closer already-used box");
        before = File.ReadAllText(level.CareerSavePath);
        using (var locked = new FileStream(level.CareerSavePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Check(!player.TryInteract() && !pickup.Used && player.Items.Count == 3 && File.ReadAllText(level.CareerSavePath) == before,
                "Failed loose-consumable writes preserve the world pickup and empty inventory slot");
        }
        Check(player.TryInteract() && pickup.Used && player.Items.Count == 4 && player.Items[3].Id == HunterItemId.Dynamite &&
            player.Items[0].Uses + player.Items[1].Uses == 4 && !pickup.TryCollect(player),
            "E picks up the configured consumable despite missing tool charges and cannot duplicate it");
        yield return Capture("item-supply-picked-up");
        for (int i = 1; i <= 2; i++)
        {
            yield return Warp(new Vector2(loot.Points[i].transform.position.x, 2.05f));
            Check(loot.Points[i].TryCollect(player) && loot.Points[i].Used && player.Items[0].Uses + player.Items[1].Uses == 4 + i,
                "Each additional box increments one tool exactly once even with a full consumable inventory");
        }
        yield return Warp(new Vector2(16, 2.05f));
        var last = loot.Points[3];
        Check(!last.TryCollect(player) && !last.Used && player.Items[0].Uses == 3 && player.Items[1].Uses == 3,
            "After reaching both tool capacities, a full consumable inventory preserves the next box");
        actor.RestoreHealth(.2f); player.TrySelectItem(2); player.TryUseItem();
        yield return Until(() => !player.IsUsingItem, 3, "Make room for a weighted box reward");
        Check(last.TryCollect(player) && last.Used && player.Items.Count == 4 && player.Items[2].Id == HunterItemId.Dynamite &&
            player.Items[3].Id == HunterItemId.Dynamite && player.Items[0].Uses == 3 && player.Items[1].Uses == 3,
            "A preserved box grants its weighted consumable only when tools are full and a consumable slot becomes free");
        yield return Warp(new Vector2(55, 2.05f));
        foreach (var guard in FindObjectsOfType<PatrolEnemy2D>()) if (guard.enabled) Freeze(guard);
        Check(!remote.TryCollect(player) && !remote.Used && remote.Hint(level.Career.Selected).Contains("已满"),
            "Loose pickups also remain in the world while consumable slots are full");
        var firstCell = level.World.Map.WorldToCell(first.transform.position);
        yield return Until(() => !level.World.Map.IsCellLoaded(firstCell.x, firstCell.y) &&
            !first.GetComponentInChildren<SpriteRenderer>().enabled, 6, "Distant supply terrain and its visual finish unloading");
        Check(!first.TryCollect(player) && !first.GetComponentInChildren<SpriteRenderer>().enabled && first.Used,
            "Terrain unload hides consumed boxes without resetting their per-run ownership state");
        yield return Warp(new Vector2(10, 2.05f));
        yield return Until(() => first.GetComponentInChildren<SpriteRenderer>().enabled, 3, "Reloaded supply point becomes visible");
        Check(loot.Points[0] == first && first.Used && pickup.Used && !first.TryCollect(player) &&
            first.GetComponentInChildren<SpriteRenderer>().enabled,
            "Terrain reload reveals the same used points without rerolling or granting more items");
        var disk = new HunterCareer(JsonUtility.FromJson<HunterCareerData>(File.ReadAllText(level.CareerSavePath)));
        Check(disk.InRun && disk.Selected.Items.Count == 4 && disk.Selected.Items[0].Uses == 3 && disk.Selected.Items[3].Id == HunterItemId.Dynamite,
            "Supply transactions durably save acquired inventory while retaining the active expedition marker");
        RestoreFrozen(); yield return SkillExtraction();
        var reload = new HunterCareerStore(level.CareerSavePath, json => JsonUtility.FromJson<HunterCareerData>(json), data => JsonUtility.ToJson(data, true));
        reload.Load();
        Check(reload.Current.Selected.Items.Count == 4 && reload.Current.Selected.Items[0].Uses == 3 && reload.Current.Selected.Items[1].Uses == 3 &&
            reload.Current.Selected.Items[3].Id == HunterItemId.Dynamite && !first.Available && !pickup.Available,
            "Extraction and fresh store loading retain field items and refills while clearing the completed run's points");
        loot.PickupChance = 0; level.StartRun(); yield return null;
        Check(loot.Points.Count == 6 && loot.Points[0] != first && loot.Points[0].Available && !loot.Points[0].Used &&
            !loot.Points[4].Available && !loot.Points[5].Available,
            "A new run refreshes box state and zero pickup chance produces no usable loose consumables");
        yield return SkillExtraction();
        level.TrainingMode = true; level.StartRun(); yield return null;
        Check(loot.Points.Count == 0, "Training mode does not create account-backed item rewards");
        level.ReturnToTitle(); yield return Until(() => level.Phase == LevelPhase.Ready, 10, "Return from item training guard");
        level.TrainingMode = false; loot.SupplyPositions = originalSupply; loot.PickupPositions = originalPickup;
        loot.Consumables = originalPool; loot.PickupChance = originalChance;
        level.SkillLoot.SpawnChance = skillChance; player.HealthRecoveryRate = recovery;
    }

    private IEnumerator ItemSkillChecks()
    {
        yield return Until(() => level.Phase == LevelPhase.Ready && hud.DisplayedPhaseTitle == "猎人营地", 3,
            "Production camp UI refreshes after the preceding training-mode check");
        var loot = level.SkillLoot; var positions = loot.Positions;
        float chance = loot.SpawnChance; bool randomize = loot.RandomizeSkills;
        bool itemLootEnabled = level.ItemLoot.enabled; level.ItemLoot.enabled = false;
        loot.SpawnChance = 1; loot.RandomizeSkills = false;
        loot.Positions = new[] { new Vector2(54, 2.6f), new Vector2(60, 2.6f), new Vector2(69, 2.6f), new Vector2(75, 2.6f), new Vector2(16, 2.6f) };
        string id = level.Career.Selected.Id; int points = level.Career.Selected.SkillPoints;
        ClickButton("猎人技能"); yield return null;
        var scroll = hud.GetComponentInChildren<ScrollRect>();
        Check(scroll && scroll.viewport.GetComponent<RectMask2D>() && scroll.content.rect.height > scroll.viewport.rect.height,
            "The expanded skill shop clips a scrollable list instead of overlapping its footer");
        scroll.OnScroll(new PointerEventData(EventSystem.current) { scrollDelta = new Vector2(0, -3) }); yield return null;
        Check(scroll.verticalNormalizedPosition < .99f, "The actual skill ScrollRect responds to mouse-wheel events");
        EventSystem.current.SetSelectedGameObject(FindButton("购买格斗节能").gameObject); yield return null; yield return null;
        Check(scroll.verticalNormalizedPosition > .99f, "Keyboard selection reveals the first skill after scrolling down");
        var buy = FindButton("购买投球好手"); EventSystem.current.SetSelectedGameObject(buy.gameObject);
        yield return null; yield return null;
        var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport, buy.transform.parent);
        Check(scroll.verticalNormalizedPosition < .99f && bounds.min.y >= scroll.viewport.rect.yMin - .1f && bounds.max.y <= scroll.viewport.rect.yMax + .1f,
            "Selecting the fifth skill brings its full row into view for keyboard and gamepad users");
        using (var locked = new FileStream(level.CareerSavePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            ClickButton("购买投球好手");
            Check(!level.Career.Selected.HasSkill(HunterSkillId.Pitcher) && level.Career.Selected.SkillPoints == points && player.ThrowRangeMultiplier == 1,
                "The real Pitcher purchase button retains points and runtime effects when its save fails");
        }
        ClickButton("购买投球好手"); yield return null;
        Check(level.Career.Selected.HasSkill(HunterSkillId.Pitcher) && level.Career.Selected.SkillPoints == points - 2 &&
            FindButton("移除投球好手").GetComponentInChildren<Text>().text.Contains("返还1"),
            "The fifth skill purchases for two points and displays its one-point refund");
        yield return Capture("pitcher-shop");
        ClickButton("返回整备"); yield return null;
        level.StartRun(); yield return null; yield return Warp(new Vector2(16, 2.05f));
        foreach (var guard in FindObjectsOfType<PatrolEnemy2D>())
        { Freeze(guard); guard.GetComponent<SkillActor2D>().BeHit(new SkillHitData { attackValue = 10000 }); }
        yield return null;
        Check(player.ThrowRangeMultiplier == 1.5f && loot.Pickups[4].Skill == HunterSkillId.Pitcher &&
            !loot.Pickups[4].TryCollect(player) && !loot.Pickups[4].Used,
            "Deployment applies the purchased range bonus and duplicate field copies remain unconsumed");
        var target = ItemTarget(new Vector2(5.2f, 2.05f));
        player.GetComponent<SkillFacing2D>().SetFacing(-1); player.TrySelectItem(1);
        float stamina = player.Stamina; int magazine = player.Weapons.Magazine, reserve = player.Weapons.Reserve;
        Check(player.TryUseItem(), "A skilled regular throwing knife begins normal preparation");
        yield return Until(() => target.Health < 100, 3, "The extended knife reaches beyond its original eight-unit limit");
        Check(target.Health == 70 && player.Items[1].Uses == 2 && player.Stamina == stamina &&
            player.Weapons.Magazine == magazine && player.Weapons.Reserve == reserve,
            "Pitcher extends a real knife hit without increasing damage or changing its charge, stamina or ammunition cost");
        player.TrySelectItem(2); float origin = player.transform.position.x;
        Check(player.TryUseItem(), "The same item skill applies to an equipped consumable explosive");
        yield return Until(() => !player.IsUsingItem && level.GetComponentInChildren<HunterThrowable2D>(), 3, "Skilled dynamite enters flight after preparation");
        var bomb = level.GetComponentInChildren<HunterThrowable2D>(); float minimum = bomb.transform.position.x;
        float deadline = Time.realtimeSinceStartup + 4;
        while (bomb && Time.realtimeSinceStartup < deadline) { minimum = Mathf.Min(minimum, bomb.transform.position.x); yield return null; }
        Check(!bomb && Mathf.Abs(origin - minimum - 9) < .05f && player.Items.Count == 3 &&
            level.Career.Selected.HasSkill(HunterSkillId.Pitcher) && HunterItems.Find(HunterItemId.Dynamite).Range == 6,
            "A real explosive flies nine units, consumes its own item and preserves the regular skill and base catalogue; distance=" +
            (origin - minimum) + ", items=" + player.Items.Count + ", stillFlying=" + (bool)bomb);
        yield return Capture("pitcher-throw");
        Destroy(target.gameObject); RestoreFrozen(); yield return SkillExtraction();
        var reloaded = new HunterCareerStore(level.CareerSavePath, json => JsonUtility.FromJson<HunterCareerData>(json), data => JsonUtility.ToJson(data, true)); reloaded.Load();
        Check(reloaded.Current.Selected.HasSkill(HunterSkillId.Pitcher) && reloaded.Current.Selected.Items[1].Uses == 2 &&
            reloaded.Current.Selected.Items.Count == 3 && player.ThrowRangeMultiplier == 1,
            "Extraction persists the regular skill and partial tools while camp clears the live combat bonus");
        ClickButton("猎人技能"); yield return null; points = level.Career.Selected.SkillPoints;
        ClickButton("移除投球好手"); yield return null;
        Check(!level.Career.Selected.HasSkill(HunterSkillId.Pitcher) && level.Career.Selected.SkillPoints == points + 1 &&
            level.Career.Selected.Items[1].Uses == 2, "Removing Pitcher through camp UI refunds one point without recharging items");
        ClickButton("返回整备"); yield return null;
        level.StartRun(); yield return null; yield return Warp(new Vector2(16, 2.05f));
        foreach (var guard in FindObjectsOfType<PatrolEnemy2D>())
        { Freeze(guard); guard.GetComponent<SkillActor2D>().BeHit(new SkillHitData { attackValue = 10000 }); }
        yield return null;
        target = ItemTarget(new Vector2(5.2f, 2.05f)); var pickup = loot.Pickups[4];
        player.GetComponent<SkillFacing2D>().SetFacing(-1); player.TrySelectItem(1);
        Check(player.ThrowRangeMultiplier == 1 && player.TryUseItem(), "The next unskilled deployment restores the eight-unit throwing baseline");
        yield return Until(() => !player.IsUsingItem && level.GetComponentInChildren<HunterThrowable2D>(), 3, "Base-range knife enters flight before skill acquisition");
        var inFlight = level.GetComponentInChildren<HunterThrowable2D>(); var oldWeapons = player.Weapons;
        points = level.Career.Selected.SkillPoints; magazine = oldWeapons.Magazine; reserve = oldWeapons.Reserve;
        float health = actor.Health; stamina = player.Stamina;
        using (var locked = new FileStream(level.CareerSavePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Check(!player.TryInteract() && !pickup.Used && player.ThrowRangeMultiplier == 1 &&
                !level.Career.Selected.HasSkill(HunterSkillId.Pitcher) && ReferenceEquals(oldWeapons, player.Weapons),
                "Failed live Pitcher acquisition retains the card, original range and exact weapon snapshot");
        }
        Check(player.TryInteract() && pickup.Used && player.ThrowRangeMultiplier == 1.5f &&
            level.Career.Selected.SkillPoints == points && player.Items[1].Uses == 1 &&
            player.Weapons.Magazine == magazine && player.Weapons.Reserve == reserve && actor.Health == health && player.Stamina == stamina,
            "Retrying E applies the item skill immediately without refilling a partially used tool or other combat resources");
        yield return Until(() => !inFlight, 3, "The previously released knife reaches its original limit");
        Check(target.Health == 100 && !pickup.TryCollect(player) && player.ThrowRangeMultiplier == 1.5f,
            "Acquiring Pitcher during flight leaves the old projectile at eight units and duplicate pickup cannot stack the bonus");
        Check(player.TryUseItem(), "The next throw after live acquisition uses the newly learned skill");
        yield return Until(() => target.Health < 100, 3, "Live-acquired Pitcher extends the next knife");
        Check(target.Health == 70 && player.Items[1].Uses == 0 && player.ThrowRangeMultiplier == 1.5f,
            "The next actual knife reaches the distant target with unchanged damage and exhausts its retained tool slot");
        yield return Capture("pitcher-field-skill");
        Destroy(target.gameObject); RestoreFrozen(); yield return SkillExtraction();
        Check(level.Career.Selected.HasSkill(HunterSkillId.Pitcher) && level.Career.Selected.Items[1].Uses == 0 &&
            level.Career.Selected.FieldSkillValue == 2, "Field-acquired Pitcher survives extraction alongside an exhausted throwing tool");
        level.RecruitHunter(); string reserveHunter = level.Career.Selected.Id;
        level.BuyEquipment(HunterEquipment.Pistol); level.ToggleEquipment(HunterEquipment.Pistol);
        level.StartRun(); yield return null;
        Check(player.ThrowRangeMultiplier == 1 && player.Items.Count == 0 && !level.Career.Selected.HasSkill(HunterSkillId.Pitcher),
            "Another hunter deploys without inheriting the previous hunter's throwing bonus or items");
        yield return SkillExtraction();
        level.SelectHunter(id); loot.SpawnChance = 0; level.StartRun(); yield return null;
        Check(player.ThrowRangeMultiplier == 1.5f, "Reselecting the original hunter rebuilds the bonus once from its saved skills");
        yield return Until(() => !actor.IsInvulnerable, 3, "Spawn protection expires before testing permanent skill loss");
        actor.BeHit(new SkillHitData { attackValue = 10000 }); yield return null;
        Check(level.Phase == LevelPhase.Dead && level.Career.Selected.Id == reserveHunter && !level.Career.Selected.HasSkill(HunterSkillId.Pitcher),
            "Permanent death removes the throwing skill and leaves the reserve hunter unchanged");
        ClickButton("返回营地"); yield return Until(() => level.Phase == LevelPhase.Ready, 10, "Camp after item-skill regression");
        loot.Positions = positions; loot.SpawnChance = chance; loot.RandomizeSkills = randomize; level.ItemLoot.enabled = itemLootEnabled;
    }

    private IEnumerator OnceSkillChecks()
    {
        yield return Until(() => level.Phase == LevelPhase.Ready && hud.DisplayedPhaseTitle == "猎人营地", 3, "Camp is ready for once skills");
        var skill = HunterSkillId.EmergencyBandage; var loot = level.SkillLoot; var positions = loot.Positions;
        float chance = loot.SpawnChance, recovery = player.HealthRecoveryRate; bool randomize = loot.RandomizeSkills;
        bool itemLootEnabled = level.ItemLoot.enabled; level.ItemLoot.enabled = false; player.HealthRecoveryRate = 0;
        loot.SpawnChance = 1; loot.RandomizeSkills = false;
        loot.Positions = new[] { new Vector2(54, 2.6f), new Vector2(14, 2.6f), new Vector2(69, 2.6f),
            new Vector2(75, 2.6f), new Vector2(16, 2.6f), new Vector2(18, 2.6f) };
        string id = level.Career.Selected.Id; int points = level.Career.Selected.SkillPoints;
        ClickButton("猎人技能"); yield return null;
        var buy = FindButton("购买应急包扎"); EventSystem.current.SetSelectedGameObject(buy.gameObject);
        yield return null; yield return null;
        var scroll = hud.GetComponentInChildren<ScrollRect>();
        var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport, buy.transform.parent);
        Check(bounds.min.y >= scroll.viewport.rect.yMin - .1f && bounds.max.y <= scroll.viewport.rect.yMax + .1f,
            "The sixth once-skill row is fully visible when selected in the real camp menu");
        using (var locked = new FileStream(level.CareerSavePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            ClickButton("购买应急包扎");
            Check(!level.Career.Selected.HasSkill(skill) && level.Career.Selected.SkillPoints == points,
                "The once-skill purchase button preserves points and ownership on a real save failure");
        }
        ClickButton("购买应急包扎"); yield return null;
        Check(level.Career.Selected.HasSkill(skill) && level.Career.Selected.SkillPoints == points - 2 && !player.TryUseOnceSkill(),
            "Purchasing grants one copy for two points but camp cannot activate it");
        yield return Capture("once-skill-shop");
        ClickButton("返回整备"); yield return null; level.StartRun(); yield return null;
        Check(actor.Health == actor.MaxHealth && !player.TryUseOnceSkill() && level.Career.Selected.HasSkill(skill),
            "A full-health deployment preserves the once skill without healing or consuming it");
        yield return SkillExtraction();
        var loaded = new HunterCareerStore(level.CareerSavePath, json => JsonUtility.FromJson<HunterCareerData>(json), data => JsonUtility.ToJson(data, true)); loaded.Load();
        Check(loaded.Current.Selected.HasSkill(skill), "An unused purchased once skill survives actual extraction and disk reload");
        ClickButton("猎人技能"); yield return null; points = level.Career.Selected.SkillPoints;
        ClickButton("移除应急包扎"); yield return null;
        Check(!level.Career.Selected.HasSkill(skill) && level.Career.Selected.SkillPoints == points + 1,
            "Explicit removal of an unused once skill through camp UI refunds one point");
        ClickButton("购买应急包扎"); yield return null; ClickButton("返回整备"); yield return null;
        Check(level.BuyItem(HunterItemId.ThrowingKnife) && level.EquipItem(id, HunterItemId.ThrowingKnife) &&
            level.BuyItem(HunterItemId.HealthShot) && level.EquipItem(id, HunterItemId.HealthShot),
            "The once-skill scenario equips a separate regular tool and medical consumable");
        level.StartRun(); yield return null; yield return Warp(new Vector2(16, 2.05f));
        foreach (var guard in FindObjectsOfType<PatrolEnemy2D>()) Freeze(guard);
        Check(loot.Pickups[4].TryCollect(player), "Once skills coexist with a field-acquired throwing passive");
        yield return Warp(new Vector2(14, 2.05f));
        Check(loot.Pickups[1].TryCollect(player), "Once skills coexist with a field-acquired weapon passive");
        yield return Warp(new Vector2(18, 2.05f));
        var card = loot.Pickups[5];
        Check(card.Skill == skill && !card.TryCollect(player) && !card.Used, "An owned once skill leaves a duplicate world card available");
        actor.RestoreHealth(.5f); level.TogglePause();
        Check(!player.TryUseOnceSkill() && !level.TryUseOnceSkill(skill) && actor.Health == 50 && level.Career.Selected.HasSkill(skill),
            "Both player and session APIs reject once-skill activation while paused");
        level.TogglePause();
        // Explicit state fixture: validates the runtime gate without pretending to synthesize a physical V key.
        var viewSetter = typeof(HunterMission2D).GetProperty("IsSpecialView").GetSetMethod(true);
        viewSetter.Invoke(level.Hunt, new object[] { true });
        Check(!player.TryUseOnceSkill() && level.Career.Selected.HasSkill(skill), "The actual special-view state blocks once-skill activation");
        viewSetter.Invoke(level.Hunt, new object[] { false });
        player.GetComponent<SkillFacing2D>().SetFacing(-1);
        Check(player.TryShoot() && !player.TryUseOnceSkill() && actor.Health == 50,
            "A real weapon attack blocks simultaneous once-skill healing");
        yield return Until(() => player.CanInteract, 3, "Attack finishes before the reload test");
        Check(player.TryReload() && !player.TryUseOnceSkill() && actor.Health == 50,
            "A real reload blocks once-skill healing without consuming the skill");
        yield return Until(() => player.CanInteract, 3, "Reload finishes before the medical preparation test");
        player.TrySelectItem(1);
        Check(player.TryUseItem() && player.IsUsingItem && !player.TryUseOnceSkill() && actor.Health == 50,
            "Medical preparation blocks once-skill healing");
        actor.BeHit(new SkillHitData { attackValue = 1 });
        Check(player.IsHurt && !player.IsUsingItem && !player.TryUseOnceSkill() && actor.Health == 49 && player.Items[1].Uses == 1,
            "Hit stun blocks once-skill healing and the interrupted medical consumable remains unused");
        yield return Until(() => player.CanInteract, 3, "Hit stun ends before throwing preparation");
        player.TrySelectItem(0);
        Check(player.TryUseItem() && !player.TryUseOnceSkill() && level.Career.Selected.HasSkill(skill),
            "Throwing preparation also blocks once-skill consumption");
        yield return Warp(new Vector2(18, 2.05f)); // Cancel the throw before it commits an item charge.
        Check(!player.IsUsingItem && player.ItemStatus == "使用已取消，次数保留" && player.Items[0].Uses == 3,
            "Cancelling preparation clears the stale using-item hint and retains the throwing charge");
        actor.RestoreHealth(.2f); points = level.Career.Selected.SkillPoints;
        int fieldValue = level.Career.Selected.FieldSkillValue, credits = level.Career.Credits;
        var weapons = player.Weapons; int magazine = weapons.Magazine, reserve = weapons.Reserve;
        float stamina = player.Stamina;
        string before = File.ReadAllText(level.CareerSavePath);
        Check(!level.TryUseOnceSkill(HunterSkillId.Pitcher) && !level.TryUseOnceSkill((HunterSkillId)999),
            "The runtime activation route rejects passive and unknown skill identifiers");
        using (var locked = new FileStream(level.CareerSavePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Check(!player.TryUseOnceSkill() && actor.Health == 20 && level.Career.Selected.HasSkill(skill) &&
                ReferenceEquals(weapons, player.Weapons) && File.ReadAllText(level.CareerSavePath) == before &&
                level.StatusMessage.Contains("按 G 重试"),
                "A real locked activation preserves health, skill, weapon snapshot and disk bytes and shows a G retry hint");
            yield return Capture("once-skill-save-retry");
        }
        Check(player.TryUseOnceSkill() && actor.Health == 70 && !level.Career.Selected.HasSkill(skill),
            "Retrying the same action after unlocking storage heals across segments and removes the once skill");
        Check(player.ThrowRangeMultiplier == 1.5f && level.Career.Selected.HasSkill(HunterSkillId.QuickReload) &&
            player.Weapons.Magazine == magazine && player.Weapons.Reserve == reserve && player.Stamina == stamina &&
            player.Items[0].Uses == 3 && player.Items[1].Uses == 1 && level.Career.Selected.SkillPoints == points &&
            level.Career.Selected.FieldSkillValue == fieldValue && level.Career.Credits == credits,
            "Activation preserves existing item and weapon passives, ammo, stamina, item charges, points and acquisition value");
        var persisted = new HunterCareer(JsonUtility.FromJson<HunterCareerData>(File.ReadAllText(level.CareerSavePath)));
        Check(persisted.InRun && !persisted.Selected.HasSkill(skill) && !player.TryUseOnceSkill() && actor.Health == 70,
            "The consumed skill is durably absent and repeated activation grants no second heal");
        yield return null;
        var skillText = Array.Find(hud.GetComponentsInChildren<Text>(), t => t.name == "Active Skills");
        Check(!skillText.text.Contains("G 包扎") && !skillText.text.Contains("应急包扎"),
            "The live HUD removes the used once skill and its activation hint");
        Check(player.TryInteract() && card.Used && level.Career.Selected.HasSkill(skill) &&
            level.Career.Selected.FieldSkillValue == fieldValue + 2 && actor.Health == 70,
            "E collects the previously preserved field copy after consumption without granting free healing");
        yield return null;
        Check(skillText.text.Contains("G 包扎 +50生命") && skillText.preferredHeight <= skillText.rectTransform.rect.height,
            "A newly acquired once skill immediately restores a readable G healing prompt");
        yield return Capture("once-skill-field-ready");
        // Keep an existing weapon cooldown active without starting another animation.
        Check(player.Weapons.TryFire() && !player.Weapons.CanShoot && player.TryUseOnceSkill() && actor.Health == actor.MaxHealth &&
            !player.Weapons.CanShoot && !level.Career.Selected.HasSkill(skill),
            "A new copy heals only to maximum health while preserving an existing firearm cooldown");
        bool reloadStarted = player.TryReload(); player.Weapons.Tick(.89f); bool stillReloading = player.Weapons.IsReloading;
        player.Weapons.Tick(.02f);
        Check(reloadStarted && stillReloading && !player.Weapons.IsReloading,
            "The retained quick-reload passive still completes the actual pistol reload at 0.9 seconds after once-skill use");
        Check(!card.TryCollect(player) && !player.TryUseOnceSkill() && level.Career.Selected.FieldSkillValue == fieldValue + 2,
            "An already collected card and repeated input cannot create another once-skill copy");
        yield return Capture("once-skill-used");
        RestoreFrozen(); yield return SkillExtraction(); loaded.Load();
        Check(!loaded.Current.Selected.HasSkill(skill) && loaded.Current.Selected.HasSkill(HunterSkillId.Pitcher) &&
            loaded.Current.Selected.HasSkill(HunterSkillId.QuickReload) && loaded.Current.Selected.Items[0].Uses == 3 &&
            loaded.Current.Selected.Items[1].Uses == 1 && loaded.Current.Selected.FieldSkillValue == fieldValue + 2,
            "Actual extraction and disk reload retain passives and supplies without resurrecting the consumed once skill");
        Check(level.BuySkill(id, skill), "A later camp purchase can provide a genuinely new once-skill copy");
        loot.SpawnChance = 0; level.StartRun(); yield return null;
        yield return Until(() => !actor.IsInvulnerable, 3, "Spawn protection expires before once-skill death validation");
        actor.BeHit(new SkillHitData { attackValue = 10000 }); yield return null;
        Check(level.Phase == LevelPhase.Dead && !actor.IsAlive && !player.TryUseOnceSkill() &&
            !level.TryUseOnceSkill(skill) && level.Career.HunterCount == 0,
            "An unused emergency bandage cannot revive a dead hunter and is lost with that hunter");
        ClickButton("返回营地"); yield return Until(() => level.Phase == LevelPhase.Ready, 10, "Camp after once-skill regression");
        loot.Positions = positions; loot.SpawnChance = chance; loot.RandomizeSkills = randomize;
        level.ItemLoot.enabled = itemLootEnabled; player.HealthRecoveryRate = recovery;
    }

    private IEnumerator AimChecks()
    {
        yield return Until(() => level.Phase == LevelPhase.Ready, 3, "Camp is ready for aiming checks");
        Check(!player.SetAimInput(Vector2.up), "Camp cannot update gameplay aiming input");
        AimLaunchChecks();
        float chance = level.SkillLoot.SpawnChance, recovery = player.HealthRecoveryRate;
        bool itemLootEnabled = level.ItemLoot.enabled;
        level.SkillLoot.SpawnChance = 0; level.ItemLoot.enabled = false; player.HealthRecoveryRate = 0;
        level.ClaimRelief(); level.StartRun(); yield return null; yield return Warp(new Vector2(6, 2.05f));
        foreach (var guard in FindObjectsOfType<PatrolEnemy2D>()) Freeze(guard);
        var facing = player.GetComponent<SkillFacing2D>();
        Check(player.AimDirection == Vector2.right && player.Weapons.WeaponCount == 1,
            "A new relief deployment starts with horizontal aim and its own equipped weapon");
        float ground = body.position.y;
        player.SetDirectionalInput(new Vector2(1, 1), false, false); yield return new WaitForSeconds(.2f);
        Check(body.velocity.x > 2.8f && body.velocity.x < 3.1f && Mathf.Abs(body.position.y - ground) < .1f &&
            Vector2.Dot(player.AimDirection, new Vector2(1, 1).normalized) > .999f && Mathf.Abs(body.rotation) < .001f,
            "Diagonal stick input walks only horizontally at normal speed and aims upward without rotating the physics body");
        player.SetDirectionalInput(Vector2.zero, false, false); yield return new WaitForSeconds(.1f);
        Vector2 direction = player.AimDirection;
        Check(Mathf.Abs(body.velocity.x) < .01f && direction.y > .7f && !player.SetAimInput(new Vector2(float.NaN, 1)) &&
            !player.SetAimInput(new Vector2(.1f, .1f)) && player.AimDirection == direction,
            "Releasing the stick stops walking while neutral, drift and invalid input preserve the last aim");
        var marker = Array.Find(hud.GetComponentsInChildren<RectTransform>(), t => t.name == "Aim Reticle");
        Check(marker && marker.gameObject.activeInHierarchy && !marker.GetComponent<Image>().raycastTarget,
            "The gameplay HUD exposes a visible aiming crosshair without intercepting input");
        var keys = Array.Find(hud.GetComponentsInChildren<Text>(), t => t.name == "Keys");
        Check(keys && keys.text.Contains("W/S") && keys.text.Contains("Q/F") && keys.preferredHeight <= keys.rectTransform.rect.height,
            "Both rows of the updated movement, aiming and combat help fit without clipping");
        level.TogglePause(); yield return null;
        Check(!player.SetAimInput(Vector2.down) && !player.TryShoot() && player.AimDirection == direction && !marker.gameObject.activeSelf,
            "Pause freezes aiming and firing and hides the crosshair");
        level.TogglePause();
        var viewSetter = typeof(HunterMission2D).GetProperty("IsSpecialView").GetSetMethod(true);
        viewSetter.Invoke(level.Hunt, new object[] { true });
        Check(!player.SetAimInput(Vector2.down) && !player.TryShoot() && player.AimDirection == direction,
            "The investigation-view state rejects aiming and firing through the same gameplay gate");
        viewSetter.Invoke(level.Hunt, new object[] { false });
        player.SetControlEnabled(false);
        Check(!player.SetAimInput(Vector2.down), "Disabled player control rejects aim input");
        player.SetControlEnabled(true);
        var hit = ItemTarget(player.AimOrigin + direction * 3 - Vector2.up * .75f);
        var miss = ItemTarget(player.AimOrigin + new Vector2(-direction.x, direction.y) * 3 - Vector2.up * .75f);
        int magazine = player.Weapons.Magazine; float damage = player.Weapons.Damage;
        Check(player.TryShoot() && player.Weapons.Magazine == magazine - 1 && actor.Player.CurrentInstance.ProjectileDirection2D.HasValue &&
            Vector2.Dot(actor.Player.CurrentInstance.ProjectileDirection2D.Value, direction) > .999f,
            "A real shot snapshots the chosen diagonal direction and consumes exactly one round");
        var cast = actor.Player.CurrentInstance;
        Check(!actor.Player.TryPlaySkillClip(player.Shoot, new Vector2(float.NaN, 1)) && ReferenceEquals(cast, actor.Player.CurrentInstance),
            "Invalid per-cast aim is rejected without cancelling the existing shot");
        Check(player.SetAimInput(new Vector2(-1, 1)) && !player.TryShoot() && player.Weapons.Magazine == magazine - 1 &&
            actor.Player.CurrentInstance.ProjectileDirection2D.Value == direction,
            "Turning during the firing windup changes the next aim but cannot redirect or duplicate the accepted shot");
        yield return Until(() => hit.Health < 100, 3, "The diagonal projectile reaches its actual collider target");
        Check(hit.Health == 100 - damage && miss.Health == 100 && facing.Direction == -1 && Mathf.Abs(body.rotation) < .001f,
            "The accepted diagonal shot hits its original target with normal damage after the player turns away");
        yield return Capture("aim-diagonal-snapshot");
        yield return Until(() => player.CanInteract && player.Weapons.CanShoot, 3, "The next aimed shot clears its normal cooldown");
        Check(player.TryShoot(), "The next shot uses the newly selected left-up aim");
        yield return Until(() => miss.Health < 100, 3, "The left-up shot reaches its separate target");
        Check(miss.Health == 100 - damage && hit.Health == 100 - damage, "Consecutive casts keep independent directions and damage targets once");
        Destroy(hit.gameObject); Destroy(miss.gameObject);
        yield return Until(() => player.CanInteract && player.Weapons.CanShoot, 3, "Vertical shot cooldown");
        player.SetDirectionalInput(Vector2.up, false, false); yield return new WaitForSeconds(.1f);
        Check(player.AimDirection == Vector2.up && facing.Direction == -1 && Mathf.Abs(body.velocity.x) < .01f && player.IsGrounded,
            "A pure vertical stick aims straight up without walking, jumping or flipping left-facing orientation");
        hit = ItemTarget(player.AimOrigin + Vector2.up * 3 - Vector2.up * .75f);
        Check(player.TryShoot(), "A straight-up shot starts through the normal weapon action");
        yield return Until(() => hit.Health < 100, 3, "The vertical projectile reaches an elevated target");
        Check(hit.Health == 100 - damage, "A real vertical projectile delivers configured damage");
        yield return Capture("aim-vertical"); Destroy(hit.gameObject);
        yield return Until(() => player.CanInteract && player.Weapons.CanShoot, 3, "Reload and melee gate after aiming checks");
        Check(player.TryReload() && player.SetAimInput(new Vector2(1, 1)) && !player.TryShoot(),
            "Reloading permits choosing the next aim while still blocking firing");
        yield return Until(() => player.CanInteract, 3, "Aimed reload completes normally");
        hit = ItemTarget((Vector2)player.transform.position + Vector2.right * 1.1f);
        float meleeDamage = player.Weapons.MeleeDamage;
        foreach (var entry in SkillTrackModel.Read(player.Melee))
            if (entry.Data is SkillAttackDetectionEvent attack) { meleeDamage *= attack.AttackHitConfig.AttackMultiply; break; }
        Check(player.TryMelee() && !actor.Player.CurrentInstance.ProjectileDirection2D.HasValue,
            "Gun melee starts with no projectile direction override despite a diagonal aim");
        yield return Until(() => hit.Health < 100, 3, "Horizontal melee reaches its ground-level target");
        Check(hit.Health == 100 - meleeDamage, "Aiming upward preserves horizontal melee and its authored damage multiplier; health=" +
            hit.Health + ", expectedDamage=" + meleeDamage);
        Destroy(hit.gameObject); yield return Until(() => player.CanInteract, 3, "Melee ends before input lifecycle checks");
        actor.RestoreHealth(); actor.BeHit(new SkillHitData { attackValue = 1 });
        direction = player.AimDirection;
        Check(player.IsHurt && !player.SetAimInput(Vector2.down) && player.AimDirection == direction,
            "Hit stun rejects aiming without corrupting the previous direction");
        yield return Until(() => player.CanInteract, 3, "Hit stun ends before downward aiming");
        // Move above clear terrain and hold the body still while testing a lower target in real physics.
        yield return Warp(new Vector2(6, 7)); body.constraints = RigidbodyConstraints2D.FreezeAll;
        player.SetAimInput(Vector2.down); hit = ItemTarget(player.AimOrigin + Vector2.down * 2 - Vector2.up * .75f);
        Check(player.TryShoot(), "A downward shot is legal in the standing aim mode");
        yield return Until(() => hit.Health < 100, 3, "Downward projectile reaches a target below the player");
        Check(hit.Health == 100 - damage, "The real downward projectile hits below the muzzle rather than firing horizontally");
        Destroy(hit.gameObject); yield return Until(() => player.CanInteract, 3, "Downward shot ends");
        body.constraints = RigidbodyConstraints2D.FreezeRotation;
        player.SetAimInput(new Vector2(-1, 1)); player.enabled = false;
        Check(!player.SetAimInput(Vector2.up) && player.AimDirection == Vector2.left,
            "Disabling the input component rejects new input and resets its retained angle to the current horizontal facing");
        player.enabled = true; yield return Warp(new Vector2(6, 2.05f));
        Check(player.AimDirection == Vector2.right && facing.Direction == 1,
            "Respawn positioning clears the previous aim and restores the right-facing baseline");
        SkillProjectileEvent authored = null;
        foreach (var entry in SkillTrackModel.Read(player.Shoot)) if (entry.Data is SkillProjectileEvent projectile) { authored = projectile; break; }
        Check(authored != null && authored.Facing == SkillRangeFacing.CharacterForward && authored.LaunchForward == .65f && authored.LaunchHeight == 1.1f,
            "Aimed casts leave the authored shared projectile facing and launch offsets unchanged");
        RestoreFrozen(); yield return SkillExtraction();
        Check(!player.SetAimInput(Vector2.up), "Returning to camp rejects the old deployment's aim input");
        level.SkillLoot.SpawnChance = chance; level.ItemLoot.enabled = itemLootEnabled; player.HealthRecoveryRate = recovery;
    }

    private IEnumerator PostureChecks()
    {
        Check(!player.TrySetProne(true), "Camp rejects posture changes");
        string id = level.Career.Selected.Id;
        Check(level.BuyItem(HunterItemId.FirstAid) && level.EquipItem(id, HunterItemId.FirstAid),
            "Prone regression equips a real medical tool through camp transactions");
        float chance = level.SkillLoot.SpawnChance, recovery = player.HealthRecoveryRate;
        bool itemLootEnabled = level.ItemLoot.enabled;
        level.SkillLoot.SpawnChance = 0; level.ItemLoot.enabled = false; player.HealthRecoveryRate = 0;
        level.StartRun(); yield return null; yield return Warp(new Vector2(6, 2.05f));
        foreach (var guard in FindObjectsOfType<PatrolEnemy2D>()) Freeze(guard);
        yield return Until(() => player.IsGrounded, 3, "Ground contact before prone input");
        var shape = player.GetComponent<BoxCollider2D>(); var visual = player.GetComponent<PlayerVisual2D>();
        Vector2 size = shape.size, offset = shape.offset;
        float feet = shape.bounds.min.y, stamina = player.Stamina;
        player.SetAimInput(Vector2.down);
        Check(player.TrySetProne(true) && player.IsProne && player.AimDirection == Vector2.right &&
            Mathf.Abs(shape.size.y - size.y * .5f) < .001f && shape.size.x == size.x && Mathf.Abs(shape.bounds.min.y - feet) < .001f,
            "Entering prone halves collider height while preserving feet, width and a legal forward aim");
        player.SetInput(1, false, false); player.SetSprint(true); yield return new WaitForSeconds(.25f);
        Check(body.velocity.x > 1.4f && body.velocity.x < 1.6f && !player.IsSprinting && player.Stamina == stamina && player.IsGrounded,
            "Crawling uses half walk speed and Shift cannot restore running or consume sprint stamina");
        player.SetDirectionalInput(Vector2.zero, false, false); yield return new WaitForSeconds(.2f);
        Check(visual.Visual.localScale.y < .53f && visual.Visual.localScale.x > 1.15f && Mathf.Abs(body.rotation) < .001f &&
            Mathf.Abs(player.AimOrigin.y - player.transform.position.y - .55f) < .001f,
            "The prone visual and muzzle lower together without rotating or scaling the physics root");
        Check(player.SetAimInput(new Vector2(-1, -1)) && player.AimDirection == Vector2.left &&
            player.SetAimInput(new Vector2(-1, 1)) && player.AimDirection.x < -.7f && player.AimDirection.y > .7f,
            "Prone permits turning left and left-up aim but clamps downward input to the local horizon");
        Check(player.SetAimInput(Vector2.up) && player.AimDirection == Vector2.up && player.SetAimInput(Vector2.down) &&
            player.AimDirection == Vector2.left, "Vertical prone input keeps facing and clamps straight-down to forward");
        var keys = Array.Find(hud.GetComponentsInChildren<Text>(), t => t.name == "Keys");
        var staminaText = Array.Find(hud.GetComponentsInChildren<Text>(), t => t.name == "Stamina");
        Check(keys.text.Contains("C 匍匐") && keys.preferredHeight <= keys.rectTransform.rect.height && staminaText.text.Contains("匍匐"),
            "Prone controls and the current posture fit the live HUD");
        yield return Capture("prone-open-ground");
        Check(player.TrySetProne(false) && shape.size == size && shape.offset == offset && !player.IsProne,
            "Standing restores the exact authored box without cumulative resizing");
        Check(player.TrySetProne(true), "Prone can be re-entered on open ground");
        player.SetInput(0, true, true); yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
        Check(!player.IsProne && body.velocity.y > 1 && !player.TrySetProne(true),
            "Jumping from prone first stands in free space and airborne posture changes are rejected");
        player.SetInput(0, false, false);
        yield return Until(() => player.IsGrounded, 4, "The posture jump lands normally");

        var ceiling = new GameObject("Prone Low Ceiling"); ceiling.transform.position = new Vector2(7, 3.05f);
        var roof = ceiling.AddComponent<BoxCollider2D>(); roof.size = new Vector2(4, .2f);
        var white = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, Texture2D.whiteTexture.width, Texture2D.whiteTexture.height),
            Vector2.one * .5f, Texture2D.whiteTexture.width);
        var roofArt = new GameObject("Ceiling Visual"); roofArt.transform.SetParent(ceiling.transform, false);
        roofArt.transform.localScale = new Vector3(4, .2f, 1);
        var roofSprite = roofArt.AddComponent<SpriteRenderer>(); roofSprite.sprite = white; roofSprite.color = new Color(.35f, .5f, .6f);
        roofSprite.sortingOrder = 10;
        yield return Warp(new Vector2(3.9f, 2.05f));
        player.SetInput(1, false, false); yield return new WaitForSeconds(.6f); player.SetInput(0, false, false);
        Check(body.position.x < 4.75f && !player.IsProne, "The real low-ceiling collider blocks a standing player's entry");
        Check(player.TrySetProne(true), "The blocked standing player can lower into prone");
        player.SetInput(1, false, false); yield return new WaitForSeconds(1.15f); player.SetInput(0, false, false);
        yield return new WaitForSeconds(.1f);
        Check(body.position.x > 6 && body.position.x < 7 && shape.bounds.max.y < roof.bounds.min.y && player.IsGrounded,
            "The reduced collider crawls through a real one-unit-high passage without intersecting the ceiling");
        Vector2 underRoof = body.position;
        Check(!player.TrySetProne(false) && player.IsProne && player.PostureStatus.Contains("受阻"),
            "Standing beneath a solid ceiling is refused with a posture hint");
        player.SetInput(0, true, true); yield return new WaitForFixedUpdate();
        Check(player.IsProne && Mathf.Abs(body.velocity.y) < .1f && Mathf.Abs(body.position.y - underRoof.y) < .02f,
            "Jump input cannot bypass a blocked stand-up check or launch the prone collider into the roof");
        player.SetInput(0, false, false); yield return null;
        var hint = Array.Find(hud.GetComponentsInChildren<Text>(), t => t.name == "Weapon Hint");
        Check(hint.text.Contains("上方受阻"), "The blocked stand-up reason is visible in the game HUD");
        level.TogglePause(); yield return null;
        Check(player.IsProne && !player.TrySetProne(false) && !player.TrySetProne(true), "Pause retains prone geometry and rejects posture input");
        level.TogglePause();
        player.SetControlEnabled(false);
        Check(player.IsProne && !player.TrySetProne(false), "Disabled control preserves safe prone geometry");
        player.SetControlEnabled(true);
        var viewSetter = typeof(HunterMission2D).GetProperty("IsSpecialView").GetSetMethod(true);
        viewSetter.Invoke(level.Hunt, new object[] { true });
        Check(!player.TrySetProne(false) && player.IsProne, "Investigation view cannot change posture");
        viewSetter.Invoke(level.Hunt, new object[] { false });
        player.enabled = false;
        Check(player.IsProne && shape.size.y == size.y * .5f && !player.TrySetProne(false),
            "Disabling the controller never expands its collider inside a low ceiling");
        player.enabled = true;
        Check(!player.TrySetProne(false) && shape.size.y == size.y * .5f,
            "Re-enabling preserves the original standing-box cache and still rejects a blocked stand");

        player.SetAimInput(new Vector2(1, -1));
        var hit = ItemTarget(player.AimOrigin + Vector2.right * 2.2f);
        var targetShape = hit.GetComponent<BoxCollider2D>(); targetShape.offset = Vector2.zero; targetShape.size = new Vector2(.25f, .2f);
        var targetArt = new GameObject("Low Shot Target"); targetArt.transform.SetParent(hit.transform, false);
        targetArt.transform.localScale = new Vector3(.25f, .2f, 1);
        var targetSprite = targetArt.AddComponent<SpriteRenderer>(); targetSprite.sprite = white; targetSprite.color = Color.yellow;
        targetSprite.sortingOrder = 10;
        Physics2D.SyncTransforms();
        int magazine = player.Weapons.Magazine;
        Check(player.TryShoot() && actor.Player.CurrentInstance.ProjectileHeightMultiplier2D == .5f &&
            actor.Player.CurrentInstance.ProjectileDirection2D == Vector2.right && player.Weapons.Magazine == magazine - 1 && !player.TrySetProne(false),
            "A prone shot snapshots its lowered height and legal direction and blocks posture changes during the attack");
        var cast = actor.Player.CurrentInstance;
        Check(!actor.Player.TryPlaySkillClip(player.Shoot, Vector2.right, float.NaN) && ReferenceEquals(cast, actor.Player.CurrentInstance),
            "An invalid height multiplier does not cancel a valid running shot");
        yield return Until(() => hit.Health < 100, 3, "The lowered shot hits the narrow target inside the passage");
        Check(hit.Health == 100 - player.Weapons.Damage, "The real prone shot travels beneath the roof at the lowered muzzle height with normal damage");
        yield return Capture("prone-low-ceiling-shot"); Destroy(hit.gameObject);
        yield return Until(() => player.CanInteract, 3, "Prone shot ends before reload");
        Check(player.TryReload() && !player.TrySetProne(false), "Reloading blocks posture changes without losing prone geometry");
        yield return Until(() => player.CanInteract, 3, "Prone reload completes");
        actor.RestoreHealth(.4f);
        Check(player.TryUseItem() && !player.TrySetProne(false), "A real medical preparation starts prone and locks posture switching");
        player.SetInput(1, false, false); player.SetSprint(true); yield return new WaitForSeconds(.25f);
        Check(body.velocity.x > .7f && body.velocity.x < .8f && !player.IsSprinting && player.IsProne,
            "Medical and prone slowdown multiply to one-quarter walking speed");
        player.SetInput(0, false, false);
        float progress = player.ItemUseProgress;
        level.TogglePause(); yield return new WaitForSecondsRealtime(.15f);
        Check(player.IsProne && player.ItemUseProgress == progress && !player.TrySetProne(false),
            "Pausing during prone treatment freezes preparation and retains posture");
        level.TogglePause();
        yield return Until(() => !player.IsUsingItem, 3, "Prone treatment completes");
        Check(actor.Health == 90 && player.Items[0].Uses == 2 && player.IsProne,
            "Prone treatment heals and consumes exactly one durable use without standing up");
        actor.RestoreHealth(.4f); Check(player.TryUseItem(), "A second prone treatment starts before interruption");
        actor.BeHit(new SkillHitData { attackValue = 1 });
        Check(player.IsHurt && player.IsProne && !player.IsUsingItem && player.Items[0].Uses == 2 && !player.TrySetProne(false),
            "Damage interrupts prone treatment without expanding the collider or spending the pending charge");
        yield return Until(() => player.CanInteract, 3, "Prone hit stun ends");
        roof.enabled = false; body.simulated = false; player.ReviveAt(underRoof);
        Check(!player.IsProne && shape.size == size, "A pose reset before destination terrain arrives initially finds free standing space");
        roof.enabled = true; Physics2D.SyncTransforms(); player.ResolveSpawnPosture();
        Check(player.IsProne && shape.size.y == size.y * .5f && visual.Visual.localScale.y == .5f && !body.simulated,
            "The post-stream spawn check lowers collision and visual pose before physics resumes beneath newly loaded terrain");
        body.simulated = true;
        player.ReviveAt(underRoof); actor.RestoreHealth(.5f); player.SetControlEnabled(true);
        yield return new WaitForFixedUpdate();
        Check(player.IsProne && shape.size.y == size.y * .5f && !player.TrySetProne(false),
            "The revival pose reset tests the actual destination and retains prone beneath a ceiling");
        // Trigger volumes and explicitly ignored collision pairs do not occupy standing space.
        roof.isTrigger = true; Physics2D.SyncTransforms();
        Check(player.TrySetProne(false) && shape.size == size, "A trigger overhead does not prevent standing");
        Check(player.TrySetProne(true), "Posture can lower again without collider-size drift");
        roof.isTrigger = false; Physics2D.IgnoreCollision(shape, roof, true);
        Check(player.TrySetProne(false), "An explicitly ignored solid collision pair does not block standing");
        player.TrySetProne(true); Physics2D.IgnoreCollision(shape, roof, false);
        yield return Capture("prone-blocked-rise");
        // Real death must also retain the low collider until the next safe spawn reset.
        actor.RestoreHealth(); actor.BeHit(new SkillHitData { attackValue = 10000 }); yield return null;
        Check(level.Phase == LevelPhase.Dead && player.IsProne && !player.TrySetProne(false) && shape.size.y == size.y * .5f,
            "Death under a low ceiling disables controls without forcibly expanding the collider");
        Destroy(ceiling); Destroy(white); RestoreFrozen();
        ClickButton("返回营地"); yield return Until(() => level.Phase == LevelPhase.Ready, 10, "Camp after prone death");
        level.ClaimRelief(); level.StartRun(); yield return null; yield return Warp(new Vector2(6, 2.05f));
        Check(!player.IsProne && shape.size == size && shape.offset == offset && player.AimDirection == Vector2.right &&
            Mathf.Abs(player.AimOrigin.y - player.transform.position.y - 1.1f) < .001f,
            "A new deployment in free space restores standing size, aim and muzzle height after death");
        yield return SkillExtraction();
        level.SkillLoot.SpawnChance = chance; level.ItemLoot.enabled = itemLootEnabled; player.HealthRecoveryRate = recovery;
    }

    private IEnumerator ThrowAimChecks()
    {
        yield return Until(() => level.Phase == LevelPhase.Ready, 3, "Camp before directional throwing checks");
        yield return RecruitDualGunFixture();
        string id = level.Career.Selected.Id;
        Check(level.BuyItem(HunterItemId.ThrowingKnife) && level.EquipItem(id, HunterItemId.ThrowingKnife) &&
            level.BuyItem(HunterItemId.Dynamite) && level.EquipItem(id, HunterItemId.Dynamite) &&
            level.BuyItem(HunterItemId.Dynamite) && level.EquipItem(id, HunterItemId.Dynamite),
            "Directional throwing uses a purchased knife tool and two independently equipped explosive consumables");
        float chance = level.SkillLoot.SpawnChance, recovery = player.HealthRecoveryRate;
        bool itemLootEnabled = level.ItemLoot.enabled;
        level.SkillLoot.SpawnChance = 0; level.ItemLoot.enabled = false; player.HealthRecoveryRate = 0;
        level.StartRun(); yield return null; yield return Warp(new Vector2(6, 2.05f));
        foreach (var guard in FindObjectsOfType<PatrolEnemy2D>()) Freeze(guard);
        Vector2 direction = new Vector2(1, 1).normalized;
        int magazine = player.Weapons.Magazine, reserve = player.Weapons.Reserve; float stamina = player.Stamina;
        float initialX = body.position.x;
        player.SetAimInput(direction);
        Check(player.TryUseItem() && !player.TryUseItem() && !player.TrySelectItem(1),
            "Starting a diagonal throw preserves the existing preparation and item-switch gates");
        player.SetDirectionalInput(direction, false, false); yield return new WaitForSeconds(.15f);
        player.SetDirectionalInput(Vector2.zero, false, false); player.SetAimInput(new Vector2(-1, 1));
        yield return new WaitForSeconds(.08f);
        Vector2 origin = body.position + Vector2.up * .75f;
        var hit = ThrowTarget(origin + direction * 3 + Vector2.up * (.25f * Mathf.Sin(3f / 8 * Mathf.PI)));
        var miss = ThrowTarget(origin + new Vector2(-direction.x, direction.y) * 3 + Vector2.up * (.25f * Mathf.Sin(3f / 8 * Mathf.PI)));
        var itemText = Array.Find(hud.GetComponentsInChildren<Text>(), t => t.name == "Equipped Items");
        Check(player.IsUsingItem && player.Items[0].Uses == 3 && itemText && itemText.text.Contains("方向已锁定") &&
            itemText.preferredHeight <= itemText.rectTransform.rect.height,
            "The preparation HUD explains the locked throw direction and fits its item panel");
        yield return Until(() => !player.IsUsingItem && level.GetComponentInChildren<HunterThrowable2D>(), 3, "The aimed knife is durably released");
        var knife = level.GetComponentInChildren<HunterThrowable2D>();
        Check(knife.LaunchDirection == direction && knife.Range == 8 && knife.LaunchOrigin.x > initialX + .05f &&
            Vector2.Distance(knife.LaunchOrigin, origin) < .03f && player.AimDirection.x < 0 && player.Items[0].Uses == 2,
            "Moving during preparation updates the release position while turning cannot replace the captured aim");
        var line = knife.GetComponent<LineRenderer>(); Vector3 trail = line.GetPosition(0) - line.GetPosition(1);
        Check(trail.x > .1f && trail.y > .1f, "A diagonal knife's visible trail follows its actual flight instead of remaining horizontal");
        level.TogglePause(); Vector3 pausedPosition = knife.transform.position;
        yield return new WaitForSecondsRealtime(.15f);
        Check(knife && knife.transform.position == pausedPosition && hit.Health == 100 && !player.TryUseItem(),
            "Pausing freezes the aimed knife in flight and prevents a second throw");
        level.TogglePause();
        yield return Until(() => hit.Health < 100, 3, "The original right-up target receives the captured throw");
        Check(hit.Health == 70 && miss.Health == 100 && player.Stamina == stamina && player.Weapons.Magazine == magazine && player.Weapons.Reserve == reserve,
            "The original diagonal target takes one knife hit while the new aim target, stamina and gun ammunition stay unchanged");
        yield return Capture("throw-diagonal-snapshot");
        yield return Until(() => !level.GetComponentInChildren<HunterThrowable2D>(), 2, "The first knife is destroyed after hitting");
        Check(player.TryUseItem(), "A second throw accepts the retained left-up direction");
        yield return Until(() => miss.Health < 100, 3, "The next knife reaches the left-up target");
        Check(miss.Health == 70 && hit.Health == 70 && player.Items[0].Uses == 1,
            "Consecutive knives keep independent direction snapshots and consume one charge each");
        Destroy(hit.gameObject); Destroy(miss.gameObject);
        yield return Until(() => !level.GetComponentInChildren<HunterThrowable2D>(), 2, "The left-up knife finishes");

        yield return Warp(new Vector2(6, 7)); body.constraints = RigidbodyConstraints2D.FreezeAll; body.velocity = Vector2.zero;
        origin = body.position + Vector2.up * .75f;
        hit = ThrowTarget(origin + Vector2.down * 3 + Vector2.up * (.25f * Mathf.Sin(3f / 8 * Mathf.PI)));
        player.SetAimInput(Vector2.down); string bytes = File.ReadAllText(level.CareerSavePath);
        using (var locked = new FileStream(level.CareerSavePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Check(player.TryUseItem(), "A downward knife can prepare while storage is locked");
            yield return Until(() => !player.IsUsingItem, 3, "The downward throw save fails before activation");
            Check(!level.GetComponentInChildren<HunterThrowable2D>() && hit.Health == 100 && player.Items[0].Uses == 1 &&
                File.ReadAllText(level.CareerSavePath) == bytes && player.ItemStatus.Contains("失败"),
                "Failed directional consumption creates no active projectile, hit or durable charge loss");
        }
        Check(player.TryUseItem() && player.SetAimInput(Vector2.up), "A failed throw retries with a fresh downward snapshot before the next aim changes");
        yield return Until(() => hit.Health < 100, 3, "A standing downward knife reaches its lower target");
        Check(hit.Health == 70 && player.Items[0].Uses == 0 && !player.TryUseItem(),
            "The downward retry hits once and leaves an exhausted tool rather than granting extra throws");
        yield return null;
        Check(level.StatusMessage == "已使用飞刀。" && Array.Exists(hud.GetComponentsInChildren<Text>(), t => t.text == "已使用飞刀。"),
            "Successful item consumption replaces the earlier save-failure status in both the session and visible HUD");
        yield return Capture("throw-downward"); Destroy(hit.gameObject);
        yield return Until(() => !level.GetComponentInChildren<HunterThrowable2D>(), 2, "The downward knife finishes");

        origin = body.position + Vector2.up * .75f;
        hit = ThrowTarget(origin + Vector2.up * 6 + Vector2.right * 1.3f);
        Check(player.TrySelectItem(1) && player.SetAimInput(Vector2.up) && player.TryUseItem() && player.SetAimInput(Vector2.right),
            "An explosive captures straight-up aim independently of later turning");
        yield return Until(() => !player.IsUsingItem && level.GetComponentInChildren<HunterThrowable2D>(), 3, "The vertical explosive enters flight");
        var bomb = level.GetComponentInChildren<HunterThrowable2D>();
        Check(bomb.LaunchDirection == Vector2.up && bomb.Range == 6 && player.Items.Count == 2 && player.Items[1].Id == HunterItemId.Dynamite,
            "A vertical explosive removes only its own consumable slot and retains the second equipped copy");
        yield return Until(() => bomb && Vector2.Distance(bomb.transform.position, origin + Vector2.up * 6) < .01f, 3,
            "The vertical explosive reaches the exact six-unit endpoint");
        level.TogglePause(); pausedPosition = bomb.transform.position;
        yield return new WaitForSecondsRealtime(.2f);
        Check(bomb && bomb.transform.position == pausedPosition && hit.Health == 100,
            "The endpoint fuse also pauses for a vertical explosive");
        level.TogglePause(); yield return Until(() => !bomb, 3, "The vertical explosive completes its normal fuse");
        Check(hit.Health == 40 && actor.Health == 100 && player.Items.Count == 2,
            "The aimed explosion hits a nearby off-path target once without damaging its distant owner");
        Destroy(hit.gameObject); body.constraints = RigidbodyConstraints2D.FreezeRotation;

        yield return Warp(new Vector2(6, 2.05f));
        origin = body.position + Vector2.up * .75f;
        var cover = new GameObject("Diagonal Throw Cover"); cover.transform.position = origin + new Vector2(2.2f, 2.3f);
        cover.AddComponent<BoxCollider2D>().size = new Vector2(.2f, 4);
        hit = ThrowTarget(origin + new Vector2(1.4f, 3.2f));
        miss = ThrowTarget(origin + new Vector2(3, 3.2f));
        Physics2D.SyncTransforms();
        Check(player.SetAimInput(direction) && player.TryUseItem(), "The remaining explosive can be aimed diagonally toward solid cover");
        yield return Until(() => !player.IsUsingItem && !level.GetComponentInChildren<HunterThrowable2D>(), 4, "The diagonal explosive collides and detonates");
        Check(hit.Health == 40 && miss.Health == 100 && player.Items.Count == 1 && player.Items[0].Uses == 0 && actor.Health == 100,
            "An aimed explosive stops at cover, damages the visible side and preserves the shielded target and distant owner; front=" +
            hit.Health + ", behind=" + miss.Health + ", owner=" + actor.Health);
        Destroy(cover); Destroy(hit.gameObject); Destroy(miss.gameObject); RestoreFrozen(); yield return SkillExtraction();
        Check(level.BuySkill(id, HunterSkillId.Pitcher) && level.UnequipItem(id, 0, HunterItemId.ThrowingKnife) &&
            level.BuyItem(HunterItemId.ThrowingKnife) && level.EquipItem(id, HunterItemId.ThrowingKnife),
            "After extraction the hunter buys Pitcher and equips a genuinely new full knife tool");

        level.StartRun(); yield return null; yield return Warp(new Vector2(6, 2.05f));
        foreach (var guard in FindObjectsOfType<PatrolEnemy2D>()) Freeze(guard);
        yield return Until(() => player.IsGrounded, 3, "Ground contact before a prone aimed throw");
        Check(player.TrySetProne(true) && player.SetAimInput(new Vector2(1, -1)) && player.AimDirection == Vector2.right,
            "Prone throwing shares the player's local forward-up aim restriction");
        origin = body.position + Vector2.up * .375f;
        hit = ThrowTarget(origin + Vector2.right * 3 + Vector2.up * (.25f * Mathf.Sin(3f / 12 * Mathf.PI)));
        hit.GetComponent<BoxCollider2D>().size = new Vector2(.3f, .2f);
        cover = new GameObject("Prone Throw Ceiling"); cover.transform.position = new Vector2(7, 3.05f);
        cover.AddComponent<BoxCollider2D>().size = new Vector2(4, .2f); Physics2D.SyncTransforms();
        Check(player.TryUseItem() && !player.TrySetProne(false) && player.SetAimInput(new Vector2(-1, 1)),
            "A prone preparation locks posture and captures the clamped horizon while accepting the next aim");
        level.TogglePause(); float progress = player.ItemUseProgress;
        yield return new WaitForSecondsRealtime(.15f);
        Check(player.IsProne && player.ItemUseProgress == progress && player.Items[0].Uses == 3,
            "Pausing a prone directional preparation retains its pending charge and posture");
        level.TogglePause();
        yield return Until(() => !player.IsUsingItem && level.GetComponentInChildren<HunterThrowable2D>(), 3, "The prone knife is released below the ceiling");
        knife = level.GetComponentInChildren<HunterThrowable2D>();
        Vector2 expectedRelease = (Vector2)player.transform.position + Vector2.up * .375f;
        Check(knife.LaunchDirection == Vector2.right && knife.Range == 12 && Vector2.Distance(knife.LaunchOrigin, expectedRelease) < .03f,
            "Prone release keeps the half-height origin, captured horizon and Pitcher range on the same projectile; direction=" +
            knife.LaunchDirection + ", range=" + knife.Range + ", origin=" + knife.LaunchOrigin + ", expectedRelease=" + expectedRelease +
            ", preparationPosition=" + origin);
        yield return Until(() => hit.Health < 100, 3, "The prone knife hits its narrow target beneath the real ceiling");
        Check(hit.Health == 70 && player.Items[0].Uses == 2 && player.IsProne,
            "Prone aim cannot throw downward or inherit a later upward turn, and consumes one knife after a low hit");
        yield return Capture("throw-prone"); Destroy(hit.gameObject); Destroy(cover); yield return null;
        yield return Until(() => !level.GetComponentInChildren<HunterThrowable2D>(), 2, "The prone knife finishes");
        Check(player.TrySetProne(false), "The player stands once the throwing test ceiling is removed");
        origin = body.position + Vector2.up * .75f;
        hit = ThrowTarget(origin + Vector2.up * 10 + Vector2.up * (.25f * Mathf.Sin(10f / 12 * Mathf.PI)));
        Check(player.SetAimInput(Vector2.up) && player.TryUseItem(), "A skilled vertical knife begins through the same real item action");
        yield return Until(() => hit.Health < 100, 3, "Pitcher reaches a vertical target beyond the base eight-unit range");
        Check(hit.Health == 70 && player.Items[0].Uses == 1 && HunterItems.Find(HunterItemId.ThrowingKnife).Range == 8,
            "The actual upward knife gains extended range without changing damage or the shared base item");
        Destroy(hit.gameObject);
        yield return Until(() => !level.GetComponentInChildren<HunterThrowable2D>(), 2, "The skilled vertical knife finishes");
        player.SetAimInput(direction); Check(player.TryUseItem(), "Another diagonal preparation begins before damage interruption");
        actor.RestoreHealth(); actor.BeHit(new SkillHitData { attackValue = 1 });
        Check(!player.IsUsingItem && player.Items[0].Uses == 1 && !level.GetComponentInChildren<HunterThrowable2D>(),
            "Damage discards the pending direction without activating a projectile or spending its charge");
        yield return Until(() => player.CanInteract, 3, "Directional throw interruption ends");
        yield return Warp(new Vector2(6, 2.05f));
        origin = body.position + Vector2.up * .75f;
        hit = ThrowTarget(origin + new Vector2(-direction.x, direction.y) * 3 + Vector2.up * (.25f * Mathf.Sin(3f / 12 * Mathf.PI)));
        Check(player.SetAimInput(new Vector2(-1, 1)) && player.TryUseItem(), "Retrying after interruption captures a new left-up direction");
        yield return Until(() => hit.Health < 100, 3, "The retried knife reaches its new target");
        Check(hit.Health == 70 && player.Items[0].Uses == 0, "The interrupted right-up aim never leaks into the later left-up retry");
        Destroy(hit.gameObject); RestoreFrozen(); yield return SkillExtraction();
        var saved = new HunterCareer(JsonUtility.FromJson<HunterCareerData>(File.ReadAllText(level.CareerSavePath)));
        Check(saved.Selected.HasSkill(HunterSkillId.Pitcher) && saved.Selected.Items.Count == 1 && saved.Selected.Items[0].Uses == 0,
            "Extraction persists the range skill and the exact remaining charges after directional throws");
        level.SkillLoot.SpawnChance = chance; level.ItemLoot.enabled = itemLootEnabled; player.HealthRecoveryRate = recovery;
    }

    private SkillActor2D ThrowTarget(Vector2 position)
    {
        var target = ItemTarget(position); target.name = "Directional Throw Target";
        var shape = target.GetComponent<BoxCollider2D>(); shape.offset = Vector2.zero; shape.size = new Vector2(.35f, .35f);
        if (level.Hunt.MarkerSprite)
        {
            var marker = new GameObject("Target Marker"); marker.transform.SetParent(target.transform, false);
            var renderer = marker.AddComponent<SpriteRenderer>(); renderer.sprite = level.Hunt.MarkerSprite;
            renderer.color = new Color(1, .65f, .2f); renderer.sortingOrder = 20;
            marker.transform.localScale = new Vector3(.35f / renderer.sprite.bounds.size.x, .35f / renderer.sprite.bounds.size.y, 1);
        }
        Physics2D.SyncTransforms(); return target;
    }

    private void AimLaunchChecks()
    {
        var root = new GameObject("Aim Launch Probe"); var facing = root.AddComponent<SkillFacing2D>(); facing.SetFacing(-1);
        var data = new SkillProjectileEvent { LaunchForward = .65f, LaunchHeight = 1.1f, Count = 3, SpreadAngle = 20 };
        Check(SkillProjectileMotion.TryLaunch(data, root.transform, null, null, 1, out var low, out _, SkillSpace.TwoD, Vector2.right, .5f) &&
            Vector2.Distance(low.Position, new Vector2(.65f, .55f)) < .0001f && data.LaunchHeight == 1.1f,
            "Per-cast prone height scales only the launch height and preserves the authored asset");
        Check(!SkillProjectileMotion.TryLaunch(data, root.transform, null, null, 1, out _, out _, SkillSpace.TwoD, Vector2.right, 0) &&
            !SkillProjectileMotion.TryLaunch(data, root.transform, null, null, 1, out _, out _, SkillSpace.TwoD, Vector2.right, float.PositiveInfinity),
            "Invalid per-cast height multipliers cannot create a projectile pose");
        Check(SkillProjectileMotion.TryLaunch(data, root.transform, null, null, 1, out var legacy, out _, SkillSpace.TwoD, null, .5f) &&
            Mathf.Abs(legacy.Position.y - 1.1f) < .0001f,
            "Legacy launches without an aimed cast keep their authored muzzle height");
        Check(SkillProjectileMotion.TryLaunch(data, root.transform, null, null, 1, out var pose, out _, SkillSpace.TwoD, new Vector2(3, 4)) &&
            Vector2.Distance(pose.Position, new Vector2(.39f, 1.62f)) < .0001f && Vector2.Dot(pose.Rotation * Vector3.right, new Vector2(.6f, .8f)) > .999f,
            "Two-dimensional projectile launch rotates its forward muzzle offset using the normalized cast direction");
        SkillProjectileMotion.TryLaunch(data, root.transform, null, null, 0, out var left, out _, SkillSpace.TwoD, Vector2.up);
        SkillProjectileMotion.TryLaunch(data, root.transform, null, null, 2, out var right, out _, SkillSpace.TwoD, Vector2.up);
        Check(Mathf.Abs(Mathf.DeltaAngle(left.Rotation.eulerAngles.z, 80)) < .001f && Mathf.Abs(Mathf.DeltaAngle(right.Rotation.eulerAngles.z, 100)) < .001f,
            "Authored multi-pellet spread remains centered on the cast's chosen aim");
        Check(!SkillProjectileMotion.TryLaunch(data, root.transform, null, null, 1, out _, out _, SkillSpace.TwoD, Vector2.zero) &&
            !SkillProjectileMotion.TryLaunch(data, root.transform, null, null, 1, out _, out _, SkillSpace.TwoD, new Vector2(float.PositiveInfinity, 1)),
            "Invalid launch directions are rejected instead of creating malformed projectile poses");
        Check(SkillProjectileMotion.TryLaunch(data, root.transform, null, null, 1, out pose, out _, SkillSpace.TwoD) &&
            Vector2.Distance(pose.Position, new Vector2(-.65f, 1.1f)) < .0001f && Vector2.Dot(pose.Rotation * Vector3.right, Vector2.left) > .999f,
            "Legacy two-dimensional casts with no aim override retain their original facing and muzzle offsets");
        Check(SkillProjectileMotion.TryLaunch(data, root.transform, null, null, 1, out pose, out _, SkillSpace.ThreeD, Vector2.left) &&
            Vector3.Distance(pose.Position, new Vector3(0, 1.1f, .65f)) < .0001f,
            "The optional two-dimensional override does not modify existing three-dimensional launch geometry");
        var target = new GameObject("Aim Launch Target"); target.transform.position = new Vector3(3, 4, 0);
        data.Facing = SkillRangeFacing.TowardTarget;
        Check(SkillProjectileMotion.TryLaunch(data, root.transform, target.transform, null, 1, out pose, out _, SkillSpace.TwoD, Vector2.down) &&
            Vector2.Dot(pose.Rotation * Vector3.right, (target.transform.position - pose.Position).normalized) > .999f &&
            !SkillProjectileMotion.TryLaunch(data, root.transform, null, null, 1, out _, out _, SkillSpace.TwoD, Vector2.up),
            "Authored target-directed projectiles retain their target logic and missing-target validation");
        var socket = new GameObject("Muzzle"); socket.transform.SetParent(root.transform, false);
        socket.transform.localRotation = Quaternion.Euler(0, 0, 30); data.Facing = SkillRangeFacing.SocketForward; data.SocketPath = "Muzzle";
        Check(SkillProjectileMotion.TryLaunch(data, root.transform, null, null, 1, out pose, out _, SkillSpace.TwoD, Vector2.down) &&
            Mathf.Abs(Mathf.DeltaAngle(pose.Rotation.eulerAngles.z, 30)) < .001f,
            "Socket-directed projectiles retain their authored socket orientation");
        Destroy(root); Destroy(target);
    }

    private SkillActor2D ItemTarget(Vector2 position)
    {
        var target = new GameObject("Item Damage Probe"); target.transform.position = position; target.layer = 30;
        var shape = target.AddComponent<BoxCollider2D>(); shape.size = new Vector2(.65f, 1.45f); shape.offset = Vector2.up * .75f;
        var health = target.AddComponent<SkillActor2D>(); health.Team = 2; health.MaxHealth = 100; health.RestoreHealth();
        Physics2D.SyncTransforms(); return health;
    }

    private IEnumerator MissionChecks()
    {
        var hunt = level.Hunt;
        Check(hunt && hunt.State != null, "Authored scene has the hunt mission");
        foreach (var position in hunt.CluePositions)
        {
            yield return Warp(new Vector2(position.x, 2.05f));
            yield return Until(() => player.CanInteract, 3, "Attack animation finishes before clue interaction");
            Check(player.TryInteract(), "Nearby clue interaction accepted at " + position + " (player=" + player.transform.position + ", stage=" + hunt.State.Stage + ")");
            Check(!hunt.TryInteract(), "Collected clue cannot count twice");
        }
        Check(hunt.State.Stage == HuntStage.Hunting, "All clues reveal the boss objective");
        Check(level.RunExperience == 235, "Real clue interactions and first-wave kills contribute to extraction experience");
        yield return Warp(hunt.BossPosition - Vector2.right * 3);
        yield return Until(() => hunt.Boss && hunt.Boss.Actor, 4, "Boss spawns on loaded terrain");
        var boss = hunt.Boss;
        boss.enabled = false; boss.GetComponent<Rigidbody2D>().constraints = RigidbodyConstraints2D.FreezeAll;
        player.ResetLoadout(); actor.RestoreHealth();
        player.GetComponent<SkillFacing2D>().SetFacing(1);
        for (int shot = 0; shot < 6; shot++)
        {
            yield return Until(() => player.TryShoot(), 3, "Rifle shot against boss");
            yield return new WaitForSeconds(.85f);
            if (shot == 1) Check(boss.IsEnraged && boss.Actor.Health == 100, "Crossing boss health threshold triggers rage");
        }
        Check(!boss.Actor.IsAlive && hunt.State.Stage == HuntStage.BountyAvailable && !hunt.State.SecondWaveStarted,
            "Real projectiles kill boss and drop bounty without spawning wave early");
        Check(level.RunExperience == 335, "A real boss kill contributes boss experience exactly once");
        yield return Warp((Vector2)boss.transform.position);
        Check(player.TryInteract() && hunt.State.Stage == HuntStage.Carrying, "Bounty pickup advances objective");
        yield return null;
        Check(hunt.State.SecondWaveStarted && FindObjectsOfType<HuntEnemy2D>().Length >= 2,
            "Bounty pickup creates independent reinforcements");
        actor.RestoreHealth(); actor.BeHit(new SkillHitData { attackValue = actor.MaxHealth + 1 });
        yield return null;
        Check(level.Phase == LevelPhase.Dead && hunt.State.Stage == HuntStage.BountyAvailable && hunt.State.Reward == 0,
            "Death drops bounty and grants no reward");
        level.RetryCheckpoint();
        yield return Until(() => level.Phase == LevelPhase.Playing, 10, "Retry after bounty death");
        yield return Warp(new Vector2(hunt.BountyPosition.x, 2.05f));
        yield return Until(() => player.CanInteract, 3, "Recovery interaction is ready");
        Check(player.TryInteract() && hunt.State.SecondWaveStarted,
            "Recovering bounty retains the second wave (token=" + hunt.BountyPosition + ", player=" + player.transform.position + ", stage=" + hunt.State.Stage + ")");
        foreach (var enemy in FindObjectsOfType<HuntEnemy2D>())
            if (enemy.Actor.IsAlive) enemy.Actor.BeHit(new SkillHitData { attackValue = enemy.Actor.MaxHealth + 1 });
        int waveKills = hunt.DefeatedReinforcements;
        Check(waveKills > 0 && level.RunExperience == 335 + waveKills * 20, "Second-wave deaths contribute experience independently of the streamed first wave");
        foreach (var enemy in FindObjectsOfType<HuntEnemy2D>())
            enemy.Actor.BeHit(new SkillHitData { attackValue = enemy.Actor.MaxHealth + 1 });
        Check(hunt.DefeatedReinforcements == waveKills && level.RunExperience == 335 + waveKills * 20, "Repeated damage to dead reinforcements cannot duplicate experience");
        yield return Warp(new Vector2(74, 2.05f));
        yield return new WaitForSeconds(.3f);
        Check(hunt.State.ExtractionElapsed > 0 && level.Phase == LevelPhase.Playing, "Extraction requires time in the exit");
        level.TogglePause(); float frozen = hunt.State.ExtractionElapsed;
        yield return new WaitForSecondsRealtime(.2f);
        Check(hunt.State.ExtractionElapsed == frozen, "Pause freezes extraction countdown");
        level.TogglePause();
        yield return Warp(new Vector2(71, 2.05f));
        yield return null;
        Check(hunt.State.ExtractionElapsed == 0, "Leaving exit resets extraction countdown");
    }

    private IEnumerator MovementChecks()
    {
        float x = body.position.x;
        player.SetInput(1, false, false);
        yield return new WaitForFixedUpdate();
        Check(body.velocity.x > 0 && body.velocity.x < player.MoveSpeed, "Horizontal input accelerates through physics instead of instantly setting full speed");
        yield return new WaitForSeconds(.18f);
        Check(body.position.x > x + .3f && Mathf.Abs(body.velocity.x - player.WalkSpeed) < .2f && Mathf.Abs(player.transform.position.z) < .001f,
            "Default movement reaches walking speed without leaving the XY plane");
        float stamina = player.Stamina;
        player.SetSprint(true);
        yield return new WaitForSeconds(.2f);
        Check(player.IsSprinting && Mathf.Abs(body.velocity.x - player.MoveSpeed) < .2f && player.Stamina == stamina,
            "Sprint reaches running speed without consuming stamina");
        player.SetSprint(false);
        player.SetInput(0, false, false); yield return new WaitForSeconds(.18f);
        Check(Mathf.Abs(body.velocity.x) < .1f, "Releasing horizontal input decelerates to a stop");
        yield return Warp(new Vector2(5, 2.05f)); yield return Until(() => player.IsGrounded, 2, "Grounding before full jump");
        float baseY = body.position.y, fullPeak = baseY;
        player.SetInput(0, true, true);
        float until = Time.time + 1.35f;
        while (Time.time < until) { fullPeak = Mathf.Max(fullPeak, body.position.y); yield return null; }
        player.SetInput(0, false, false);
        Check(fullPeak > baseY + 1.6f, "Holding jump produces a full physics-driven jump arc");
        yield return Warp(new Vector2(5, 2.05f)); yield return Until(() => player.IsGrounded, 2, "Grounding before short jump");
        player.SetInput(0, true, true); yield return new WaitForSeconds(.04f);
        player.SetInput(0, false, false);
        float shortPeak = body.position.y; until = Time.time + .9f;
        while (Time.time < until) { shortPeak = Mathf.Max(shortPeak, body.position.y); yield return null; }
        Check(shortPeak > baseY + .2f && shortPeak < fullPeak - .5f, "Releasing jump early creates a measurably shorter jump");
        yield return Warp(new Vector2(5, 3.1f));
        yield return Until(() => body.velocity.y < -1 && body.position.y < 2.3f, 2, "Descending into jump-buffer window");
        player.SetInput(0, true, true);
        yield return Until(() => body.velocity.y > 2, .5f, "Buffered input triggers after landing");
        Check(!player.IsGrounded && body.velocity.y > 2, "A jump pressed just before landing is buffered and launches on contact");
        player.SetInput(0, false, false);
        yield return Warp(new Vector2(21.9f, 2.05f));
        yield return Until(() => player.IsGrounded, 2, "Grounding before the broken bridge");
        player.SetInput(1, false, false);
        player.SetSprint(true);
        yield return new WaitForSeconds(.18f);
        player.SetInput(1, true, true);
        yield return Until(() => body.position.x > 28.1f && player.IsGrounded, 2, "A running jump crosses the three-cell broken bridge");
        Check(level.Phase == LevelPhase.Playing && body.position.y > 1.9f,
            "The authored three-cell bridge gap is traversable with a real running jump");
        player.SetInput(0, false, false);
        yield return Warp(new Vector2(4.5f, 2.05f));
    }

    private IEnumerator WeaponChecks()
    {
        yield return Warp(new Vector2(7.2f, 2.05f));
        player.ResetLoadout();
        var supply = AmmoSupply2D.FindNearest(player);
        Check(supply && !supply.Used && !player.TryUseNearbySupply() && !supply.Used,
            "Authored supply remains unused when all carried ammunition is full");
        player.GetComponent<SkillFacing2D>().SetFacing(-1);
        int magazine = player.Weapons.Magazine;
        Check(player.TryShoot() && player.Weapons.Magazine == magazine - 1,
            "A real rifle skill consumes exactly one round");
        Check(!player.TryShoot() && player.Weapons.Magazine == magazine - 1,
            "Rejected repeat casts consume no ammunition");
        yield return Until(() => player.TryReload(), 3, "Reload starts after shot animation");
        int reserve = player.Weapons.Reserve;
        float progress = player.Weapons.ReloadProgress;
        level.TogglePause();
        yield return new WaitForSecondsRealtime(.2f);
        Check(player.Weapons.IsReloading && player.Weapons.ReloadProgress == progress && !player.TryShoot() && !player.TryUseNearbySupply(),
            "Pause freezes reload and rejects combat and supply interaction");
        level.TogglePause();
        yield return Until(() => !player.Weapons.IsReloading, 4, "Reload completes after resume");
        Check(player.Weapons.Magazine == magazine && player.Weapons.Reserve == reserve - 1,
            "Reload transfers reserve into the real player's magazine");
        Check(player.TrySwitchWeapon(1) && player.Weapons.Damage == 15 && player.Weapons.Magazine == 8,
            "Pistol has a separate magazine and configured damage");
        Check(player.TryShoot() && player.Weapons.Magazine == 7,
            "A real pistol cast consumes the pistol magazine");
        yield return Until(() => !actor.Player.IsPlaying, 3, "Pistol animation ends before interaction");
        Check(player.TryUseNearbySupply() && supply.Used && player.Weapons.Total(HunterAmmoType.Standard) == 72,
            "Supply restores only the missing shared ammunition and is consumed once");
        Check(!player.TryUseNearbySupply(), "Used supply cannot grant ammunition twice");
        actor.Player.StopSkillClip(false);
        player.ResetLoadout();
        supply.ResetSupply();
        yield return Warp(new Vector2(4.5f, 2.05f));
    }

    private IEnumerator EnemyChecks()
    {
        var guard = Guard(1); var enemy = guard.GetComponent<PatrolEnemy2D>(); var enemyBody = guard.GetComponent<Rigidbody2D>();
        float x = enemyBody.position.x;
        string beforePatrol = EnemySnapshot(guard);
        yield return new WaitForSeconds(.25f);
        bool patrolPassed = enemy.State == PatrolEnemyState.Patrol && Mathf.Abs(enemyBody.position.x - x) > .1f && !guard.CombatPinned;
        Check(patrolPassed, "A distant guard patrols under physics without keeping an unnecessary combat pin" +
            (patrolPassed ? "" : "\nBefore: " + beforePatrol + "\nAfter: " + EnemySnapshot(guard)));
        yield return Warp(new Vector2(enemyBody.position.x - 4.5f, 2.05f));
        yield return new WaitForSeconds(.2f);
        Check(enemy.State == PatrolEnemyState.Chase && enemy.Target == player.transform && enemyBody.velocity.x < -.5f && guard.CombatPinned,
            "A guard detects the player, faces them and chases with combat residency enabled");
        yield return Warp(new Vector2(enemyBody.position.x - 1.05f, 2.05f));
        yield return Until(() => enemy.State == PatrolEnemyState.Windup, 3, "First attack windup");
        float health = actor.Health;
        yield return new WaitForSeconds(.1f);
        Check(enemy.State == PatrolEnemyState.Windup && actor.Health == health && Mathf.Abs(enemyBody.velocity.x) < .1f,
            "Melee attacks visibly wind up while stationary before dealing damage");
        yield return Warp(new Vector2(enemyBody.position.x - 4.5f, 2.05f));
        yield return new WaitForSeconds(.4f);
        Check(actor.Health == health, "Leaving melee range during the windup avoids the strike");
        yield return Warp(new Vector2(enemyBody.position.x - 1.05f, 2.05f));
        yield return Until(() => actor.Health < health, 4, "A guard's real melee attack lands");
        Check(actor.Health == health - enemy.AttackDamage && player.IsHurt && actor.IsInvulnerable && body.velocity.x < 0,
            "A landed enemy strike applies damage, directional knockback, hit stun and temporary invulnerability");
        int events = damageEvents; health = actor.Health;
        actor.BeHit(new SkillHitData { soure = guard.GetComponent<SkillActor2D>(), attackValue = enemy.AttackDamage });
        actor.BeHit(new SkillHitData { soure = guard.GetComponent<SkillActor2D>(), attackValue = enemy.AttackDamage });
        Check(actor.Health == health && damageEvents == events, "Repeated hits inside the invulnerability window neither deduct health nor emit damage events");
        level.TogglePause();
        float elapsed = level.ElapsedSeconds; Vector2 playerPosition = body.position, enemyPosition = enemyBody.position;
        player.SetInput(1, true, true); yield return new WaitForSecondsRealtime(.2f);
        Check(level.Phase == LevelPhase.Paused && hud.IsMenuVisible && !player.TryShoot() && level.ElapsedSeconds == elapsed &&
              Vector2.Distance(body.position, playerPosition) < .001f && Vector2.Distance(enemyBody.position, enemyPosition) < .001f,
            "Pause freezes both actors and the level clock and blocks new skill casts");
        level.TogglePause();
        yield return Warp(new Vector2(enemyBody.position.x - 4.5f, 2.05f));
        actor.RestoreHealth();
    }

    private IEnumerator ShootGuard(int number, bool verifyFirstHit)
    {
        float spawnX = number == 1 ? 14 : number == 2 ? 40 : 65;
        yield return Warp(new Vector2(spawnX - 6, 2.05f));
        yield return Until(() => Guard(number), 5, "Guard " + number + " loads for projectile combat");
        var guard = Guard(number); var enemy = guard.GetComponent<PatrolEnemy2D>();
        Freeze(enemy);
        try
        {
            yield return Warp(new Vector2(guard.transform.position.x - 3.2f, 2.05f));
            player.GetComponent<SkillFacing2D>().SetFacing(1);
            yield return Until(() => player.IsGrounded && !player.IsHurt, 2, "Player can shoot on stable ground");
            yield return Until(() => player.TryShoot(), 2, "First real projectile cast at guard " + number);
            yield return Until(() => !Guard(number) || Guard(number).Health < 50, 3, "First projectile hits guard " + number);
            if (verifyFirstHit) Check(Guard(number) && Guard(number).Health == 25 && level.DefeatedEnemies == 0,
                "A real skill projectile deals the configured 25 damage to the 50-health guard");
            yield return Until(() => player.TryShoot(), 3, "Second real projectile cast at guard " + number);
            yield return Until(() => !Guard(number), 3, "Lethal projectile returns guard " + number + " to its pool");
        }
        finally { RestoreFrozen(); }
    }

    private IEnumerator Warp(Vector2 position)
    {
        player.SetInput(0, false, false);
        body.simulated = false;
        player.ResetForRespawn(position);
        if (level.FollowCamera) level.FollowCamera.Snap();
        yield return null;
        var map = level.World.Map;
        Vector2Int cell = map.WorldToCell(position), feet = map.WorldToCell(position + Vector2.down * .12f);
        yield return Until(() => map.IsCellLoaded(cell.x, cell.y) && map.IsCellLoaded(feet.x, feet.y), 6, "Terrain ready after test positioning");
        Physics2D.SyncTransforms(); body.simulated = true;
        yield return new WaitForFixedUpdate();
    }

    private static IEnumerator Until(Func<bool> condition, float seconds, string description)
    {
        float stop = Time.realtimeSinceStartup + seconds;
        while (!condition())
        {
            if (GameServices2D.Instance && !string.IsNullOrEmpty(GameServices2D.Instance.InitializationError))
                throw new InvalidOperationException("Original framework initialization failed: " + GameServices2D.Instance.InitializationError);
            if (LevelSession2D.Instance && LevelSession2D.Instance.Phase == LevelPhase.Error)
                throw new InvalidOperationException("Level initialization failed: " + LevelSession2D.Instance.StatusMessage);
            if (Time.realtimeSinceStartup > stop) throw new TimeoutException(description);
            yield return null;
        }
    }

    private static object[] NativeManagers() => new object[]
    {
        GameEntry.Logger, GameEntry.Event, GameEntry.Time, GameEntry.Fsm, GameEntry.Procedure,
        GameEntry.DataTable, GameEntry.Socket, GameEntry.Http, GameEntry.Data, GameEntry.Localization,
        GameEntry.Pool, GameEntry.Scene, GameEntry.Resource, GameEntry.Download, GameEntry.UI,
        GameEntry.Audio, GameEntry.Input, GameEntry.Task
    };

    private MapStreamedEntity Guard(int number)
    {
        return streamer && streamer.TryGetActor("spawn:trial-guard-" + number + ":0", out var result) ? result : null;
    }

    private string EnemySnapshot(MapStreamedEntity guard)
    {
        if (!guard) return "missing guard";
        var enemy = guard.GetComponent<PatrolEnemy2D>();
        var enemyBody = guard.GetComponent<Rigidbody2D>();
        var shape = guard.GetComponent<BoxCollider2D>();
        var bounds = shape.bounds;
        int facing = guard.GetComponent<SkillFacing2D>().Direction;
        float reach = bounds.extents.x + .2f + enemy.MoveSpeed * Time.fixedDeltaTime;
        Vector2 floor = new Vector2(bounds.center.x + facing * reach, bounds.min.y + .2f);
        Vector2Int feet = level.World.Map.WorldToCell(new Vector2(bounds.center.x, bounds.min.y - .05f));
        return "id=" + guard.EntityId + "/" + guard.GetInstanceID() + ", current=" + ReferenceEquals(guard, Guard(1)) +
            ", state=" + enemy.State + ", enabled=" + enemy.enabled + ", active=" + guard.gameObject.activeInHierarchy +
            ", health=" + guard.Health + ", pin=" + guard.CombatPinned + ", target=" + (enemy.Target ? enemy.Target.name : "none") +
            ", pos=" + enemyBody.position.ToString("F4") + ", render=" + guard.transform.position.ToString("F4") +
            ", velocity=" + enemyBody.velocity.ToString("F4") + ", simulated=" + enemyBody.simulated +
            ", sleeping=" + enemyBody.IsSleeping() +
            ", drag=" + enemyBody.drag + ", gravity=" + enemyBody.gravityScale + ", collisionMode=" + enemyBody.collisionDetectionMode +
            ", friction=" + (shape.sharedMaterial ? shape.sharedMaterial.friction.ToString() : "default") +
            ", contacts=" + ContactSnapshot(enemyBody) +
            ", bodyType=" + enemyBody.bodyType + ", constraints=" + enemyBody.constraints + ", facing=" + facing +
            ", player=" + body.position.ToString("F4") + ", distance=" + Vector2.Distance(body.position, enemyBody.position).ToString("F4") +
            ", detection=" + enemy.DetectionDistance + ", phase=" + level.Phase + ", gameplay=" + level.IsGameplayActive +
            ", moveSpeed=" + enemy.MoveSpeed + ", playerHealth=" + actor.Health + ", playerAlive=" + actor.IsAlive +
            ", fixedTime=" + Time.fixedTime.ToString("F4") + ", fixedDelta=" + Time.fixedDeltaTime +
            ", groundCell=" + feet + "/" + level.World.Map.IsCellLoaded(feet.x, feet.y) +
            ", floor=" + TerrainRay(floor, Vector2.down, .65f, enemy) +
            ", wall=" + TerrainRay(bounds.center, Vector2.right * facing, reach, enemy) +
            ", lowWall=" + TerrainRay(new Vector2(bounds.center.x, bounds.min.y + .22f), Vector2.right * facing, reach, enemy);
    }

    private static string TerrainRay(Vector2 origin, Vector2 direction, float distance, PatrolEnemy2D enemy)
    {
        var hits = Physics2D.RaycastAll(origin, direction, distance, enemy.GroundLayers);
        var output = new System.Text.StringBuilder("[");
        foreach (var hit in hits)
        {
            if (!hit.collider || hit.collider.isTrigger || hit.collider.transform.IsChildOf(enemy.transform) ||
                hit.collider.GetComponentInParent<PatrolEnemy2D>() || hit.collider.GetComponentInParent<PlayerController2D>()) continue;
            output.Append(hit.collider.name).Append('/').Append(hit.collider.GetType().Name).Append("/trigger=").Append(hit.collider.isTrigger)
                .Append('@').Append(hit.distance.ToString("F4")).Append(';');
        }
        return output.Append(']').ToString();
    }

    private static string ContactSnapshot(Rigidbody2D target)
    {
        var contacts = new ContactPoint2D[32];
        int count = target.GetContacts(contacts);
        var output = new System.Text.StringBuilder("[");
        for (int i = 0; i < count; i++)
        {
            var contact = contacts[i];
            output.Append(contact.collider ? contact.collider.name : "none").Append('/')
                .Append(contact.otherCollider ? contact.otherCollider.name : "none")
                .Append(" point=").Append(contact.point.ToString("F4"))
                .Append(" normal=").Append(contact.normal.ToString("F4"))
                .Append(" separation=").Append(contact.separation.ToString("F4"))
                .Append(" normalImpulse=").Append(contact.normalImpulse.ToString("F4"))
                .Append(" tangentImpulse=").Append(contact.tangentImpulse.ToString("F4")).Append(';');
        }
        return output.Append(']').ToString();
    }

    private void Freeze(PatrolEnemy2D enemy)
    {
        if (!enemy) throw new InvalidOperationException("Cannot isolate a missing guard.");
        var targetBody = enemy.GetComponent<Rigidbody2D>();
        frozen.Add(new FrozenGuard { enemy = enemy, body = targetBody, enabled = enemy.enabled, constraints = targetBody.constraints });
        enemy.enabled = false;
        targetBody.velocity = Vector2.zero;
        targetBody.constraints |= RigidbodyConstraints2D.FreezePositionX;
    }

    private void RestoreFrozen()
    {
        foreach (var snapshot in frozen)
        {
            if (snapshot.body) snapshot.body.constraints = snapshot.constraints;
            if (snapshot.enemy) snapshot.enemy.enabled = snapshot.enabled;
        }
        frozen.Clear();
    }

    private IEnumerator Capture(string name, int width = 1280, int height = 720, Rect? safeArea = null)
    {
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) yield break;
        yield return null;
        string path = Path.Combine(screenshotDirectory, name + ".png");
        // Batch mode does not submit Game View screenshots. Render the actual UI hierarchy through
        // the same camera temporarily, restoring the production overlay canvas immediately afterward.
        var camera = Camera.main;
        var render = new RenderTexture(width, height, 24);
        var oldTarget = camera.targetTexture;
        var oldActive = RenderTexture.active;
        var roots = new List<Canvas>();
        var modes = new List<RenderMode>();
        var cameras = new List<Camera>();
        var distances = new List<float>();
        Texture2D texture = null;
        try
        {
            camera.targetTexture = render;
            foreach (var canvas in FindObjectsOfType<Canvas>())
            {
                if (!canvas.isRootCanvas) continue;
                roots.Add(canvas); modes.Add(canvas.renderMode); cameras.Add(canvas.worldCamera); distances.Add(canvas.planeDistance);
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
            }
            level.FollowCamera.Snap();
            Canvas.ForceUpdateCanvases();
            if (hud.TouchControls && hud.TouchControls.TouchMode)
            {
                var safe = safeArea ?? new Rect(0, 0, width, height);
                hud.TouchControls.SetViewportPreview(new Vector2(width, height), safe);
                Canvas.ForceUpdateCanvases();
                if (hud.IsHealthLayoutOpen)
                { hud.RefreshHealthLayout(); Canvas.ForceUpdateCanvases(); CheckHealthTouchLayout(camera, safe, name); }
                else CheckTouchLayout(camera, safe, name);
            }
            hud.RefreshAimReticle(); // The capture changes both the viewport and canvas render mode.
            camera.Render(); RenderTexture.active = render;
            texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0); texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG()); report.screenshots.Add(path);
        }
        finally
        {
            camera.targetTexture = oldTarget;
            for (int i = 0; i < roots.Count; i++)
            {
                roots[i].renderMode = modes[i]; roots[i].worldCamera = cameras[i]; roots[i].planeDistance = distances[i];
            }
            Canvas.ForceUpdateCanvases(); level.FollowCamera.Snap();
            if (hud.TouchControls) hud.TouchControls.SetViewportPreview(Vector2.zero, default(Rect));
            RenderTexture.active = oldActive; render.Release(); Destroy(render); if (texture) Destroy(texture);
        }
    }

    private Button FindButton(string prefix)
    {
        foreach (var button in hud.GetComponentsInChildren<Button>())
        {
            var label = button.GetComponentInChildren<Text>();
            if (button.isActiveAndEnabled && label && label.text.StartsWith(prefix)) return button;
        }
        throw new Exception("Missing UI button: " + prefix);
    }

    private void ClickButton(string prefix)
    {
        foreach (var button in hud.GetComponentsInChildren<Button>())
        {
            var label = button.GetComponentInChildren<Text>();
            if (button.isActiveAndEnabled && button.interactable && label && label.text.StartsWith(prefix)) { button.onClick.Invoke(); return; }
        }
        throw new Exception("Missing actionable UI button: " + prefix);
    }

    private void OnPlayerDamaged(SkillHitData hit) { damageEvents++; }
    private void Check(bool valid, string description)
    {
        if (!valid) throw new Exception(description);
        report.passed++; report.checks.Add(description); Debug.Log("GAMEPLAY CHECK PASSED: " + description);
    }
    private void OnLog(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) failure = message + "\n" + stack;
    }
    private void Finish(string error)
    {
        if (finished) return;
        finished = true;
        Application.logMessageReceived -= OnLog;
        if (actor) actor.Damaged -= OnPlayerDamaged;
        RestoreFrozen();
        report.unityVersion = Application.unityVersion; report.failure = error; report.scenario = Scenario;
        Directory.CreateDirectory(Output); File.WriteAllText(Path.Combine(Output, "play-checks.json"), JsonUtility.ToJson(report, true));
        Time.timeScale = 1;
        if (error == null) Debug.Log("BIGWORLD_GAMEPLAY_VALIDATION_PASSED"); else Debug.LogError(error);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(error == null ? 0 : 1);
#endif
    }
}
