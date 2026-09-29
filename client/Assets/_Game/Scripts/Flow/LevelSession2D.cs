using System.Collections;
using System.Collections.Generic;
using BigWorld.Map2D;
using BigWorld.YouYou2D;
using UnityEngine;

namespace BigWorld.Gameplay
{
    public enum LevelPhase { Loading, Ready, Playing, Paused, Dead, Respawning, Won, Error }

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
        public LevelPhase Phase { get; private set; } = LevelPhase.Loading;
        public bool IsGameplayActive => Phase == LevelPhase.Playing && World && World.IsReady;
        public float ElapsedSeconds { get; private set; }
        public int DeathCount { get; private set; }
        public int DefeatedEnemies => defeated.Count;
        public string CheckpointName { get; private set; } = "起点";
        public string StatusMessage { get; private set; } = "正在准备关卡…";
        public float Progress01 => !Player || !StartPoint || !ExitPoint ? 0 :
            Mathf.InverseLerp(StartPoint.position.x, ExitPoint.position.x, Player.transform.position.x);
        public Vector2 RespawnPosition => checkpointPosition;
        private readonly HashSet<string> defeated = new HashSet<string>();
        private SkillActor2D actor;
        private Rigidbody2D body;
        private GameServices2D services;
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
            if (Player) { actor = Player.GetComponent<SkillActor2D>(); body = Player.GetComponent<Rigidbody2D>(); }
        }

        private IEnumerator Start()
        {
            services = GameServices2D.Instance;
            if (!World || !Player || !actor || !body || !StartPoint || !ExitPoint || !services)
            { Fail("关卡配置不完整，请检查世界、玩家和出入口引用。"); yield break; }
            actor.Died += OnPlayerDied;
            services.Framework.Context.Event.CommonEvent.AddEventListener(GameEvents2D.ActorDamaged, OnActorDamaged);
            subscribed = true;
            SetPhase(LevelPhase.Loading, "正在准备关卡…");
            float timeout = Time.realtimeSinceStartup + 12;
            while (!World.IsReady)
            {
                if (Time.realtimeSinceStartup > timeout) { Fail("出生区域加载失败，请重新开始。"); yield break; }
                yield return null;
            }
            checkpointPosition = StartPoint.position;
            Player.ResetForRespawn(checkpointPosition);
            // Let physics populate both interpolation samples before freezing the title screen.
            // WorldSession enables simulation only after terrain readiness, possibly in this same frame.
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return null;
            if (FollowCamera) FollowCamera.Snap();
            initialMapState = World.Map.SaveRuntimeState();
            SetPhase(LevelPhase.Ready, "击败 3 名守卫，越过断桥，到达出口。");
        }

        private void Update()
        {
            if (ReadKeyboard)
            {
                if (Input.GetKeyDown(KeyCode.Escape) && (Phase == LevelPhase.Playing || Phase == LevelPhase.Paused)) TogglePause();
                else if (Input.GetKeyDown(KeyCode.Return) && Phase == LevelPhase.Ready) StartRun();
                else if (Input.GetKeyDown(KeyCode.R))
                {
                    if (Phase == LevelPhase.Dead) RetryCheckpoint();
                    else if (Phase == LevelPhase.Won) RestartRun();
                }
            }
            if (!IsGameplayActive) return;
            ElapsedSeconds += Time.deltaTime;
            if (Player.transform.position.y < FallDeathY) OnPlayerDied();
        }

        public void StartRun()
        {
            if (Phase != LevelPhase.Ready) return;
            SetPhase(LevelPhase.Playing, "击败守卫并前往右侧出口。J 射击 / K 近战。");
        }

        public void TogglePause()
        {
            if (Phase == LevelPhase.Playing) SetPhase(LevelPhase.Paused, "已暂停");
            else if (Phase == LevelPhase.Paused) SetPhase(LevelPhase.Playing, "继续前进。");
        }

        public void RetryCheckpoint()
        {
            if (Phase == LevelPhase.Dead) BeginTransition(false, true);
        }

        public void RestartRun()
        {
            if (Phase == LevelPhase.Loading || Phase == LevelPhase.Respawning || string.IsNullOrEmpty(initialMapState)) return;
            BeginTransition(true, true);
        }

        public void ReturnToTitle()
        {
            if (Phase == LevelPhase.Loading || Phase == LevelPhase.Respawning || string.IsNullOrEmpty(initialMapState)) return;
            BeginTransition(true, false);
        }

        public void ActivateCheckpoint(Checkpoint2D checkpoint)
        {
            if (!IsGameplayActive || !actor.IsAlive || !checkpoint || checkpoint.Order <= checkpointOrder) return;
            checkpointOrder = checkpoint.Order;
            checkpointPosition = checkpoint.SpawnPosition;
            CheckpointName = checkpoint.DisplayName;
            checkpoint.SetActivated(true);
            StatusMessage = "检查点已激活：" + CheckpointName;
        }

        public bool TryComplete()
        {
            if (!IsGameplayActive || !actor.IsAlive) return false;
            if (DefeatedEnemies < RequiredKills)
            {
                StatusMessage = "出口尚未解锁：还需击败 " + (RequiredKills - DefeatedEnemies) + " 名守卫。";
                return false;
            }
            SetPhase(LevelPhase.Won, "试炼完成！");
            return true;
        }

        private void OnPlayerDied()
        {
            if (!IsGameplayActive) return;
            if (actor.IsAlive) actor.RestoreHealth(0); // Falling is lethal even during the spawn protection window.
            DeathCount++;
            if (actor.Player) actor.Player.StopSkillClip(false);
            body.velocity = Vector2.zero;
            SetPhase(LevelPhase.Dead, "在「" + CheckpointName + "」重新集结。已击败的守卫会保持进度。");
        }

        private void OnActorDamaged(object payload)
        {
            if (!(payload is ActorDamage2D hit) || hit.Health > 0 ||
                string.IsNullOrEmpty(hit.EntityId) || !hit.EntityId.StartsWith("spawn:trial-guard-")) return;
            if (defeated.Add(hit.EntityId))
                StatusMessage = DefeatedEnemies >= RequiredKills ? "守卫已清除，前往右侧出口！" : "守卫击败：" + DefeatedEnemies + " / " + RequiredKills;
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
                checkpointPosition = StartPoint.position;
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
            bool paused = phase == LevelPhase.Ready || phase == LevelPhase.Paused || phase == LevelPhase.Dead || phase == LevelPhase.Won || phase == LevelPhase.Error;
            if (services) services.SetPaused(paused);
            if (Player) Player.SetControlEnabled(phase == LevelPhase.Playing);
        }

        private void Fail(string message) { SetPhase(LevelPhase.Error, message); Debug.LogError(message, this); }

        private void OnDestroy()
        {
            if (subscribed)
            {
                if (actor) actor.Died -= OnPlayerDied;
                if (services && services.Framework) services.Framework.Context.Event.CommonEvent.RemoveEventListener(GameEvents2D.ActorDamaged, OnActorDamaged);
            }
            if (Instance != this) return;
            if (services) services.SetPaused(false);
            Instance = null;
        }
    }
}
