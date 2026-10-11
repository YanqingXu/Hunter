using System.Collections;
using System.Collections.Generic;
using BigWorld.Map2D;
using BigWorld.YouYou2D;
using UnityEngine;

namespace BigWorld.Gameplay
{
    public enum LevelPhase { Loading, Ready, Playing, Paused, Dead, Respawning, Won, Error, Reviving }

    /// <summary>One playable level: title, combat, checkpoints, retry, victory and a fresh run.</summary>
    [DefaultExecutionOrder(-1400), DisallowMultipleComponent]
    public sealed class LevelSession2D : MonoBehaviour
    {
        public static LevelSession2D Instance { get; private set; }
        public WorldSession2D World;
        public PlayerController2D Player;
        public CameraFollow2D FollowCamera;
        public Transform StartPoint;
        public Transform ExitPoint;
        [Min(1)] public int RequiredKills = 3;
        public float FallDeathY = -4;
        public bool ReadKeyboard = true;
        [Tooltip("仅供旧检查点流程回归；正式游戏使用营地和永久死亡。")]
        public bool TrainingMode;
        [System.NonSerialized] public string CareerSavePathOverride;
        public HunterCareer Career => careerStore == null ? null : careerStore.Current;
        public string CareerSavePath => careerStore == null ? "" : careerStore.SavePath;
        public string CampMessage { get; private set; } = "";
        public bool HasCareerSaveError { get; private set; }
        private HunterCareerStore careerStore;
        private CareerOutcome pendingOutcome;
        private int pendingReward;
        private int pendingExperience;
        private string pendingRevivalHunterId;
        private Vector2 pendingRevivalPosition, lastSafePosition;
        private float undetectedUntil;
        public float RevivalGraceRemaining => Mathf.Max(0, undetectedUntil - Time.time);
        public bool IsPlayerUndetected => actor && actor.IsAlive && RevivalGraceRemaining > 0;
        public HunterMission2D Hunt { get; private set; }
        public HunterSkillLoot2D SkillLoot { get; private set; }
        public HunterItemLoot2D ItemLoot { get; private set; }
        public HunterSceneInteractions2D SceneInteractions { get; private set; }
        public HunterEliteDirector2D Elites { get; private set; }
        public LevelPhase Phase { get; private set; } = LevelPhase.Loading;
        public bool IsGameplayActive => Phase == LevelPhase.Playing && services && services.IsReady && World && World.IsReady;
        public float ElapsedSeconds { get; private set; }
        public int DeathCount { get; private set; }
        public int DefeatedEnemies => defeated.Count;
        public int RunExperience => HunterProgression.ExtractionReward(Hunt ? Hunt.State.ClueCount : 0,
            DefeatedEnemies + (Hunt ? Hunt.DefeatedReinforcements : 0) + (Elites ? Elites.DefeatedCount : 0),
            Hunt && Hunt.State.BossDefeated);
        public string CheckpointName { get; private set; } = "起点";
        public string StatusMessage { get; private set; } = "正在准备关卡…";
        public float Progress01 => !Player || !StartPoint || !ExitPoint ? 0 :
            Mathf.InverseLerp(StartPoint.position.x, ExitPoint.position.x, Player.transform.position.x);
        public Vector2 RespawnPosition => checkpointPosition;
        private readonly HashSet<string> defeated = new HashSet<string>();
        private SkillActor2D actor;
        private Rigidbody2D body;
        private GameServices2D services;
        private YouYou.CommonEvent frameworkEvents;
        private Vector2 checkpointPosition;
        private int checkpointOrder;
        private string initialMapState;
        private Coroutine transition;
        private bool subscribed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { Instance = null; }

        private void Awake()
        {
            if (Instance && Instance != this) { Debug.LogError("关卡中只能有一个 LevelSession2D。", this); enabled = false; return; }
            Instance = this;
            Hunt = GetComponent<HunterMission2D>();
            SkillLoot = GetComponent<HunterSkillLoot2D>();
            if (Hunt && !SkillLoot) SkillLoot = gameObject.AddComponent<HunterSkillLoot2D>();
            ItemLoot = GetComponent<HunterItemLoot2D>();
            if (Hunt && !ItemLoot) ItemLoot = gameObject.AddComponent<HunterItemLoot2D>();
            SceneInteractions = GetComponent<HunterSceneInteractions2D>();
            if (Hunt && !SceneInteractions) SceneInteractions = gameObject.AddComponent<HunterSceneInteractions2D>();
            Elites = GetComponent<HunterEliteDirector2D>();
            if (Hunt && !Elites) Elites = gameObject.AddComponent<HunterEliteDirector2D>();
            if (Player) { actor = Player.GetComponent<SkillActor2D>(); body = Player.GetComponent<Rigidbody2D>(); }
        }

        private IEnumerator Start()
        {
            services = GameServices2D.Instance;
            if (!World || !Player || !actor || !body || !StartPoint || !ExitPoint || !services)
            { Fail("关卡配置不完整，请检查世界、玩家和出入口引用。"); yield break; }
            SetPhase(LevelPhase.Loading, "正在准备关卡…");
            float timeout = Time.realtimeSinceStartup + 60;
            while (!services.IsReady)
            {
                if (!string.IsNullOrEmpty(services.InitializationError))
                { Fail("原版框架初始化失败：" + services.InitializationError); yield break; }
                if (Time.realtimeSinceStartup > timeout)
                { Fail("等待原版框架初始化超时，请检查运行内容是否已生成。"); yield break; }
                yield return null;
                if (!services) { Fail("原版框架服务已关闭。"); yield break; }
            }
            actor.Died += OnPlayerDied;
            frameworkEvents = YouYou.GameEntry.Event.CommonEvent;
            frameworkEvents.AddEventListener(GameEvents2D.ActorDamaged, OnActorDamaged);
            subscribed = true;
            timeout = Time.realtimeSinceStartup + 60;
            while (!World.IsReady)
            {
                if (!services || !services.IsReady)
                { Fail("原版框架服务未就绪：" + (services ? services.InitializationError : "入口已关闭")); yield break; }
                if (Time.realtimeSinceStartup > timeout) { Fail("出生区域加载失败，请重新开始。"); yield break; }
                yield return null;
            }
            checkpointPosition = StartPoint.position;
            lastSafePosition = checkpointPosition;
            Player.ResetForRespawn(checkpointPosition);
            // Let physics populate both interpolation samples before freezing the title screen.
            // WorldSession enables simulation only after terrain readiness, possibly in this same frame.
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return null;
            if (FollowCamera) FollowCamera.Snap();
            initialMapState = World.Map.SaveRuntimeState();
            if (!LoadCareer()) yield break;
            SetPhase(LevelPhase.Ready, "调查线索、击杀 Boss、拾取赏金并撤离。");
            Debug.Log("BIGWORLD_GAME_READY: BorderTrial");
        }

        private void Update()
        {
            if (ReadKeyboard)
            {
                if (Input.GetKeyDown(KeyCode.Escape) && (Phase == LevelPhase.Playing || Phase == LevelPhase.Paused)) TogglePause();
                // Production camp uses EventSystem submit; a separate Enter handler could also deploy
                // when the focused shop/recruit button is submitted in the same frame.
                else if (Input.GetKeyDown(KeyCode.Return) && Phase == LevelPhase.Ready && TrainingMode) StartRun();
                else if (Input.GetKeyDown(KeyCode.R))
                {
                    if (Phase == LevelPhase.Dead) RetryCheckpoint();
                    else if (Phase == LevelPhase.Won) RestartRun();
                }
            }
            if (!IsGameplayActive) return;
            ElapsedSeconds += Time.deltaTime;
            if (Player.IsGrounded && actor.IsAlive && Player.transform.position.y >= FallDeathY && TerrainReady(body.position))
                lastSafePosition = body.position;
            if (Player.transform.position.y < FallDeathY) OnPlayerDied();
        }

        public void StartRun()
        {
            if (Phase != LevelPhase.Ready) return;
            if (!TrainingMode)
            {
                if (Career == null || !Career.CanDeploy) { CampMessage = "请选择猎人并至少装备一把武器。"; return; }
                try { Player.EquipHunter(Career.Selected); }
                catch (System.Exception error) { CampMessage = error.Message; return; }
                if (!ChangeCareer(item => item.BeginRun(), "出战已保存。")) return;
            }
            if (Hunt && Hunt.UseRegionClues) Hunt.ResetMission();
            if (SkillLoot) SkillLoot.BeginRun();
            if (ItemLoot) ItemLoot.BeginRun();
            if (SceneInteractions) SceneInteractions.BeginRun();
            if (Elites) Elites.BeginRun();
            SetPhase(LevelPhase.Playing, "按住 V 寻找线索；靠近青色技能卡按 E 拾取。");
        }

        public void TogglePause()
        {
            if (Phase == LevelPhase.Playing) SetPhase(LevelPhase.Paused, "已暂停");
            else if (Phase == LevelPhase.Paused) SetPhase(LevelPhase.Playing, "继续前进。");
        }
        internal void NotifySceneInteraction(string message) { StatusMessage = message; }

        public void RetryCheckpoint()
        {
            if (!TrainingMode) return;
            if (Phase == LevelPhase.Dead) BeginTransition(false, true);
        }

        public void RestartRun()
        {
            if (!TrainingMode) { ReturnToTitle(); return; }
            if (Phase == LevelPhase.Loading || Phase == LevelPhase.Respawning || Phase == LevelPhase.Reviving || string.IsNullOrEmpty(initialMapState)) return;
            BeginTransition(true, true);
        }

        public void ReturnToTitle()
        {
            if (Phase == LevelPhase.Loading || Phase == LevelPhase.Respawning || Phase == LevelPhase.Reviving || string.IsNullOrEmpty(initialMapState)) return;
            if (HasCareerSaveError) return;
            if (!TrainingMode && Career != null && Career.InRun && !ChangeCareer(item => item.Settle(CareerOutcome.Lost, 0), "已放弃行动，出战猎人及装备永久遗失。")) return;
            BeginTransition(true, false);
        }

        public void ActivateCheckpoint(Checkpoint2D checkpoint)
        {
            if (!IsGameplayActive || !actor.IsAlive || !checkpoint || checkpoint.Order <= checkpointOrder) return;
            checkpointOrder = checkpoint.Order;
            checkpointPosition = checkpoint.SpawnPosition;
            CheckpointName = checkpoint.DisplayName;
            checkpoint.SetActivated(true);
            StatusMessage = (TrainingMode ? "检查点已激活：" : "到达地标：") + CheckpointName;
        }

        public bool TryComplete()
        {
            if (!IsGameplayActive || !actor.IsAlive) return false;
            if (Hunt)
            {
                if (Hunt.State.Stage != HuntStage.Extracted) return false;
                if (!TrainingMode) return SettleCareer(CareerOutcome.Extracted, Hunt.State.Reward, RunExperience);
                SetPhase(LevelPhase.Won, Hunt.State.CarriedBountyAtExit ? "携带赏金撤离成功！" : "无赏金撤离成功！"); return true;
            }
            if (DefeatedEnemies < RequiredKills)
            {
                StatusMessage = "出口尚未解锁：还需击败 " + (RequiredKills - DefeatedEnemies) + " 名守卫。";
                return false;
            }
            if (!TrainingMode) return SettleCareer(CareerOutcome.Extracted, 0, RunExperience);
            SetPhase(LevelPhase.Won, "试炼完成！");
            return true;
        }

        private void OnPlayerDied()
        {
            if (!IsGameplayActive) return;
            if (actor.IsAlive) actor.RestoreHealth(0); // Falling is lethal even during the spawn protection window.
            DeathCount++;
            if (Hunt) Hunt.OnPlayerDied();
            if (actor.Player) actor.Player.StopSkillClip(false);
            body.velocity = Vector2.zero;
            if (!TrainingMode && Career != null && Career.Selected != null && Career.CanRevive(Career.Selected.Id))
            {
                pendingRevivalHunterId = Career.Selected.Id;
                pendingRevivalPosition = body.position.y < FallDeathY ? lastSafePosition : body.position;
                body.simulated = false;
                SetPhase(LevelPhase.Reviving, "死灵法师正在唤醒猎人……");
                CommitRevival(); return;
            }
            if (!TrainingMode) { SettleCareer(CareerOutcome.Lost, 0); return; }
            SetPhase(LevelPhase.Dead, "在「" + CheckpointName + "」重新集结。已击败的守卫会保持进度。");
        }

        private void CommitRevival()
        {
            try
            {
                var hunter = Career.Selected;
                if (hunter == null || hunter.Id != pendingRevivalHunterId || !Career.CanRevive(hunter.Id))
                    throw new System.InvalidOperationException("复活记录与出战猎人不一致。");
                hunter.Skills.RemoveAll(id => HunterSkills.Find(id).Lifetime == HunterSkillLifetime.Death);
                var effects = new HunterSkillEffects(hunter.Skills);
                var weapons = Player.PrepareSkillUpdate(effects);
                if (!careerStore.Change(item => item.ConsumeRevival(pendingRevivalHunterId)))
                    throw new System.InvalidOperationException("无法保存复活技能消耗。");
                HasCareerSaveError = false; pendingRevivalHunterId = null;
                SetPhase(LevelPhase.Reviving, "死灵法师已消耗，正在原地复活……");
                transition = StartCoroutine(RevivePlayer(pendingRevivalPosition, weapons, effects));
            }
            catch (System.Exception error) { CareerSaveFailed(error); }
        }

        private IEnumerator RevivePlayer(Vector2 position, HunterWeapons weapons, HunterSkillEffects effects)
        {
            // Complete all death callbacks before restoring the actor and enabling its controller.
            yield return null;
            Player.ReviveAt(position);
            if (FollowCamera) FollowCamera.Snap();
            yield return new WaitForSecondsRealtime(.35f);
            float timeout = Time.realtimeSinceStartup + 10;
            while (!TerrainReady(position))
            {
                if (Time.realtimeSinceStartup > timeout)
                { transition = null; SetPhase(LevelPhase.Error, "复活位置的地形加载失败，可返回营地放弃行动。"); yield break; }
                yield return null;
            }
            Player.ApplySkillUpdate(weapons, effects);
            Player.ResolveSpawnPosture();
            actor.RestoreHealth(HunterSkills.RevivalHealthFraction);
            undetectedUntil = Time.time + HunterSkills.RevivalUndetectedSeconds;
            foreach (var guard in FindObjectsOfType<PatrolEnemy2D>()) guard.ForgetPlayer();
            foreach (var enemy in FindObjectsOfType<HuntEnemy2D>()) enemy.ForgetPlayer();
            foreach (var elite in FindObjectsOfType<HunterElite2D>()) elite.ForgetPlayer();
            Physics2D.SyncTransforms(); body.simulated = true;
            transition = null;
            SetPhase(LevelPhase.Playing, "死灵法师已消耗 · 原地复活，3秒内不触发怪物警戒。");
        }

        private bool LoadCareer()
        {
            try
            {
                string path = string.IsNullOrEmpty(CareerSavePathOverride) ? System.IO.Path.Combine(Application.persistentDataPath, "Hunter", "career-v1.json") : CareerSavePathOverride;
                careerStore = new HunterCareerStore(path, json => JsonUtility.FromJson<HunterCareerData>(json), data => JsonUtility.ToJson(data, true));
                careerStore.Load(); HasCareerSaveError = false;
                CampMessage = careerStore.RecoveredBackup ? "主存档损坏，已从备份恢复；损坏文件已保留。" :
                    Career.LastOutcome == CareerOutcome.Interrupted ? "上次行动未撤离，出战猎人及装备已遗失。" : "营地整备会自动保存。";
                if (careerStore.Migrated) CampMessage += " 旧存档已升级，原文件保留为备份。";
                return true;
            }
            catch (System.Exception error) { CareerSaveFailed(error); return false; }
        }

        private bool ChangeCareer(System.Func<HunterCareer, bool> command, string success)
        {
            try
            {
                if (careerStore == null || !careerStore.Change(command)) { CampMessage = "条件不满足，操作未执行。"; return false; }
                CampMessage = success; return true;
            }
            catch (System.Exception error) { CampMessage = "保存失败，操作未生效：" + error.Message; return false; }
        }

        private bool CanManageCamp => Phase == LevelPhase.Ready && !TrainingMode && Career != null && !HasCareerSaveError;
        public void RecruitHunter() { if (CanManageCamp) ChangeCareer(item => item.Recruit(), "招募成功，请从仓库装备武器。" ); }
        public void ClaimRelief() { if (CanManageCamp) ChangeCareer(item => item.ClaimRelief(), "救济猎人已加入并配备武器。" ); }
        public void SelectHunter(string id) { if (CanManageCamp) ChangeCareer(item => item.Select(id), "已切换出战猎人。" ); }
        public void BuyEquipment(HunterEquipment equipment) { if (CanManageCamp) ChangeCareer(item => item.Buy(equipment), "购买成功，装备已入库。" ); }
        public void ToggleEquipment(HunterEquipment equipment) { if (CanManageCamp) ChangeCareer(item => item.Toggle(equipment), "猎人装备已保存。" ); }
        public bool BuyItem(HunterItemId id) { return CanManageCamp && ChangeCareer(item => item.BuyItem(id), "道具已购入仓库。"); }
        public bool SetHealthLayout(string hunterId, float[] segments)
        { return CanManageCamp && ChangeCareer(item => item.SetHealthLayout(hunterId, segments), "血段配置已保存，下次出战生效。"); }
        public bool EquipItem(string hunterId, HunterItemId id)
        { return CanManageCamp && ChangeCareer(item => item.EquipItem(hunterId, id), "道具已装备，剩余次数随猎人保存。"); }
        public bool UnequipItem(string hunterId, int index, HunterItemId id)
        { return CanManageCamp && ChangeCareer(item => item.UnequipItem(hunterId, index, id), "道具已卸下，剩余次数保留在仓库。"); }
        public bool TryConsumeItem(int index, HunterItemId id)
        {
            if (TrainingMode || !IsGameplayActive || !actor.IsAlive || Career == null || !Career.InRun) return false;
            try
            {
                string hunter = Career.Selected.Id;
                if (!careerStore.Change(item => item.ConsumeItem(hunter, index, id))) return false;
                Player.ApplyItems(Career.Selected.Items);
                StatusMessage = "已使用" + HunterItems.Find(id).Name + "。";
                return true;
            }
            catch (System.Exception error)
            { StatusMessage = "道具保存失败，次数保留；检查磁盘后按 F 重试。"; Debug.LogWarning("Item save failed: " + error.Message); return false; }
        }
        public bool BuySkill(string hunterId, HunterSkillId skill)
        {
            return CanManageCamp && ChangeCareer(item => item.BuySkill(hunterId, skill), "技能已购买，下次出战生效。");
        }
        public bool RemoveSkill(string hunterId, HunterSkillId skill)
        {
            return CanManageCamp && ChangeCareer(item => item.RemoveSkill(hunterId, skill), "技能已移除，返还的技能点已保存。");
        }
        public bool TryUseOnceSkill(HunterSkillId skill)
        {
            if (TrainingMode || !IsGameplayActive || !Player || !Player.CanInteract || !actor.IsAlive ||
                Career == null || !Career.InRun || skill != HunterSkillId.EmergencyBandage) return false;
            var hunter = Career.Selected;
            if (hunter == null || !Career.CanConsumeOnceSkill(hunter.Id, skill)) return false;
            if (actor.Health >= actor.MaxHealth) { StatusMessage = "生命已满，应急包扎保留。"; return false; }
            try
            {
                hunter.Skills.Remove(skill);
                var effects = new HunterSkillEffects(hunter.Skills);
                var weapons = Player.PrepareSkillUpdate(effects);
                if (!careerStore.Change(item => item.ConsumeOnceSkill(hunter.Id, skill))) return false;
                Player.ApplySkillUpdate(weapons, effects);
                actor.Heal(HunterSkills.EmergencyBandageHealing);
                StatusMessage = "已使用应急包扎，恢复生命；一次性技能已移除。";
                return true;
            }
            catch (System.Exception error)
            {
                StatusMessage = "技能保存失败，应急包扎保留且未治疗；检查磁盘后按 G 重试。";
                Debug.LogWarning("Once skill save failed: " + error.Message); return false;
            }
        }

        public bool RetireHunter(string id)
        {
            return CanManageCamp && ChangeCareer(item => item.Retire(id),
                "猎人已退役，账号经验 +" + HunterProgression.RetirementExperience + "，携带武器与剩余道具已归还仓库。");
        }

        public bool TryCollectItem(ItemSupplyPoint2D point)
        {
            if (!ItemLoot || !ItemLoot.CanCollect(point, Player) || Career == null || !Career.InRun) return false;
            var hunter = Career.Selected;
            var plan = point.IsSupply ? point.PrepareSupply(hunter) : null;
            if (point.IsSupply ? plan == null : !Career.CanAcquireConsumable(hunter.Id, point.Item))
            { StatusMessage = point.Hint(hunter); return false; }
            try
            {
                if (!careerStore.Change(item => point.IsSupply ? item.ApplyItemSupply(plan) : item.AcquireConsumable(hunter.Id, point.Item))) return false;
                Player.ApplyItems(Career.Selected.Items); point.MarkUsed();
                StatusMessage = point.IsSupply ? "补给成功：" + HunterItems.Find(plan.Item).Name + (plan.Index < 0 ? " ×1" : " +" + plan.Amount + "次") :
                    "已拾取" + HunterItems.Find(point.Item).Name + "，按 Q 切换，F 使用。";
                return true;
            }
            catch (System.Exception error)
            { StatusMessage = "补给保存失败，道具保留；检查磁盘后按 E 重试。"; Debug.LogWarning("Supply save failed: " + error.Message); return false; }
        }

        public bool TryCollectSkill(SkillPickup2D pickup)
        {
            if (TrainingMode || !IsGameplayActive || !SkillLoot || !SkillLoot.CanCollect(pickup, Player) || Career == null || !Career.InRun) return false;
            var hunter = Career.Selected;
            if (hunter.HasSkill(pickup.Skill)) { StatusMessage = pickup.Hint(hunter); return false; }
            if (!Career.CanAcquireFieldSkill(hunter.Id, pickup.Skill)) { StatusMessage = pickup.Hint(hunter); return false; }
            try
            {
                hunter.Skills.Add(pickup.Skill);
                var effects = new HunterSkillEffects(hunter.Skills);
                // Prepare the runtime snapshot first; nothing changes if validation or the disk write fails.
                var weapons = Player.PrepareSkillUpdate(effects);
                if (!careerStore.Change(item => item.AcquireFieldSkill(hunter.Id, pickup.Skill))) return false;
                Player.ApplySkillUpdate(weapons, effects);
                pickup.MarkCollected();
                StatusMessage = "获得「" + HunterSkills.Find(pickup.Skill).Name + "」· 已立即生效，撤离后保留。";
                return true;
            }
            catch (System.Exception error)
            {
                StatusMessage = "技能保存失败，技能卡保留，请稍后按 E 重试。";
                CampMessage = "保存失败，技能未获得：" + error.Message;
                return false;
            }
        }

        private bool SettleCareer(CareerOutcome outcome, int reward, int experience = 0)
        {
            pendingOutcome = outcome; pendingReward = reward; pendingExperience = experience;
            try
            {
                if (careerStore == null || !careerStore.Change(item => item.Settle(outcome, reward, experience)))
                    throw new System.InvalidOperationException("没有可结算的出战记录。");
                pendingOutcome = CareerOutcome.None; HasCareerSaveError = false;
                CampMessage = outcome == CareerOutcome.Extracted ? "赏金 " + reward + "、经验 " + Career.LastExperience +
                    " 已入账，获得 " + Career.LastLevelsGained + " 技能点。" : "猎人及携带装备永久遗失；仓库和其他猎人保留。";
                SetPhase(outcome == CareerOutcome.Extracted ? LevelPhase.Won : LevelPhase.Dead, CampMessage);
                return true;
            }
            catch (System.Exception error) { CareerSaveFailed(error); return false; }
        }

        private void CareerSaveFailed(System.Exception error)
        {
            HasCareerSaveError = true;
            SetPhase(LevelPhase.Error, "存档失败，请检查磁盘空间和文件权限后重试。\n" + error.Message + "\n" + CareerSavePath);
        }

        public void RetryCareerSave()
        {
            if (Phase != LevelPhase.Error || !HasCareerSaveError) return;
            if (pendingRevivalHunterId != null) CommitRevival();
            else if (pendingOutcome != CareerOutcome.None) SettleCareer(pendingOutcome, pendingReward, pendingExperience);
            else if (LoadCareer()) SetPhase(LevelPhase.Ready, "存档已就绪。");
        }

        private void OnActorDamaged(object payload)
        {
            if (!(payload is ActorDamage2D hit) || hit.Health > 0 ||
                string.IsNullOrEmpty(hit.EntityId) || !hit.EntityId.StartsWith("spawn:trial-guard-")) return;
            if (defeated.Add(hit.EntityId))
                StatusMessage = Hunt ? "守卫击败：" + DefeatedEnemies + "；继续调查并追踪 Boss。" :
                    DefeatedEnemies >= RequiredKills ? "守卫已清除，前往右侧出口！" : "守卫击败：" + DefeatedEnemies + " / " + RequiredKills;
        }

        private void BeginTransition(bool resetLevel, bool playAfter)
        {
            if (transition != null) return;
            transition = StartCoroutine(RestoreAtCheckpoint(resetLevel, playAfter));
        }

        private IEnumerator RestoreAtCheckpoint(bool resetLevel, bool playAfter)
        {
            SetPhase(LevelPhase.Respawning, resetLevel ? "正在重置关卡…" : "正在返回检查点…");
            body.simulated = false;
            if (actor.Player) actor.Player.StopSkillClip(false);
            if (resetLevel)
            {
                if (Hunt) Hunt.ResetMission();
                if (SkillLoot) SkillLoot.Clear();
                if (ItemLoot) ItemLoot.Clear();
                foreach (var projectile in GetComponentsInChildren<HunterThrowable2D>(true)) Destroy(projectile.gameObject);
                undetectedUntil = 0; pendingRevivalHunterId = null;
                Player.ResetLoadout();
                foreach (var supply in FindObjectsOfType<AmmoSupply2D>()) supply.ResetSupply();
                checkpointPosition = StartPoint.position;
                lastSafePosition = checkpointPosition;
                checkpointOrder = 0; CheckpointName = "起点";
                defeated.Clear(); ElapsedSeconds = 0; DeathCount = 0;
                foreach (var checkpoint in FindObjectsOfType<Checkpoint2D>()) checkpoint.SetActivated(false);
                World.Map.LoadRuntimeState(initialMapState);
            }
            Player.ResetForRespawn(checkpointPosition);
            if (FollowCamera) FollowCamera.Snap();
            float timeout = Time.realtimeSinceStartup + 10;
            // Always yield once so map streaming can react to a changed target before physics resumes.
            yield return null;
            while (!TerrainReady(checkpointPosition))
            {
                if (Time.realtimeSinceStartup > timeout)
                {
                    transition = null; Fail("检查点区域未加载完成，可重新开始重试。"); yield break;
                }
                yield return null;
            }
            Physics2D.SyncTransforms();
            Player.ResolveSpawnPosture();
            actor.RestoreHealth(1, 1.5f);
            body.simulated = true;
            if (FollowCamera) FollowCamera.Snap();
            transition = null;
            SetPhase(playAfter ? LevelPhase.Playing : LevelPhase.Ready, playAfter ? "已返回「" + CheckpointName + "」，短暂无敌。" : "准备开始新的试炼。");
        }

        private bool TerrainReady(Vector2 position)
        {
            Vector2Int cell = World.Map.WorldToCell(position);
            Vector2Int feet = World.Map.WorldToCell(position + Vector2.down * .12f);
            return World.Map.IsCellLoaded(cell.x, cell.y) && World.Map.IsCellLoaded(feet.x, feet.y);
        }

        private void SetPhase(LevelPhase phase, string message)
        {
            Phase = phase; StatusMessage = message;
            bool paused = phase == LevelPhase.Ready || phase == LevelPhase.Paused || phase == LevelPhase.Dead || phase == LevelPhase.Won || phase == LevelPhase.Error || phase == LevelPhase.Reviving;
            if (services) services.SetPaused(paused);
            if (Player) Player.SetControlEnabled(phase == LevelPhase.Playing);
        }

        private void Fail(string message) { SetPhase(LevelPhase.Error, message); Debug.LogError(message, this); }

        private void OnDestroy()
        {
            if (subscribed)
            {
                if (actor) actor.Died -= OnPlayerDied;
                frameworkEvents?.RemoveEventListener(GameEvents2D.ActorDamaged, OnActorDamaged);
                frameworkEvents = null;
                subscribed = false;
            }
            if (Instance != this) return;
            if (services) services.SetPaused(false);
            Instance = null;
        }
    }
}
