using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BigWorld.Gameplay;
using BigWorld.Map2D;
using BigWorld.YouYou2D;
using SkillEditorKit;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Runs on a temporary copy of the real scene, against real streamed terrain and skills.</summary>
public sealed class GameplayPlayChecks : MonoBehaviour
{
    [Serializable] private sealed class Report
    {
        public string unityVersion;
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
    private bool finished;
    private LevelSession2D level;
    private PlayerController2D player;
    private SkillActor2D actor;
    private Rigidbody2D body;
    private GridMapEntityStreamer streamer;
    private GameHud2D hud;
    private int damageEvents;
    private static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../TestResults/VerticalSlice"));

    private IEnumerator Start()
    {
        Directory.CreateDirectory(Output);
        screenshotDirectory = Path.Combine(Output, "Screenshots-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(screenshotDirectory);
        Application.logMessageReceived += OnLog;
        deadline = Time.realtimeSinceStartup + 180;
        // Flatten nested iterators so assertions in helpers are caught and written to JSON too.
        var stack = new Stack<IEnumerator>();
        stack.Push(Checks());
        while (stack.Count > 0 && !finished)
        {
            object wait = null;
            try
            {
                if (failure != null) throw new Exception(failure);
                if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("Gameplay checks exceeded 180 seconds.");
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
        if (!finished && deadline > 0 && Time.realtimeSinceStartup > deadline)
            Finish("Gameplay checks exceeded 180 seconds while waiting for a frame or phase.");
    }

    private IEnumerator Checks()
    {
        yield return Until(() => LevelSession2D.Instance && LevelSession2D.Instance.Phase == LevelPhase.Ready, 15, "Initial Ready phase");
        level = LevelSession2D.Instance;
        player = level.Player; player.ReadKeyboard = false; level.ReadKeyboard = false;
        actor = player.GetComponent<SkillActor2D>(); body = player.GetComponent<Rigidbody2D>();
        streamer = level.World.Map.GetComponent<GridMapEntityStreamer>();
        actor.Damaged += OnPlayerDamaged;
        yield return Until(() => FindObjectOfType<GameHud2D>(), 3, "Framework-loaded gameplay HUD");
        hud = FindObjectOfType<GameHud2D>();
        yield return null;
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
        try { yield return MovementChecks(); }
        finally { RestoreFrozen(); }
        yield return EnemyChecks();
        yield return ShootGuard(1, true);
        Check(level.DefeatedEnemies == 1, "A real projectile kill records the first guard exactly once");
        yield return Capture("playing");

        yield return Warp(new Vector2(74, 2.05f));
        yield return new WaitForSeconds(.12f);
        Check(level.Phase == LevelPhase.Playing && level.StatusMessage.Contains("出口尚未解锁"),
            "Entering the actual exit trigger before all kills keeps the run active");

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
        yield return Warp(new Vector2(74, 2.05f));
        yield return Until(() => level.Phase == LevelPhase.Won, 2, "Actual exit trigger completes the run");
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
              level.ElapsedSeconds < .5f && actor.Health == actor.MaxHealth && Guard(1).Health == 50 &&
              Array.TrueForAll(checkpoints, item => !item.Activated) && Vector2.Distance(body.position, level.StartPoint.position) < .2f,
            "Restart clears kills, checkpoints, deaths and timer and restores the original enemy state");
        level.TogglePause(); yield return null;
        ClickButton("返回标题");
        yield return Until(() => level.Phase == LevelPhase.Ready, 10, "Return to title");
        yield return null;
        Check(hud.IsMenuVisible && hud.DisplayedPhaseTitle == "边境试炼" && !player.ControlEnabled && Time.timeScale == 0 &&
              level.DefeatedEnemies == 0 && level.ElapsedSeconds == 0, "ReturnToTitle resets the run and restores a usable title screen");
        var shutdown = GameServices2D.Instance.ShutdownAsync();
        yield return Until(() => shutdown.IsCompleted, 10, "Framework shutdown drains the active level and pool");
        if (shutdown.IsFaulted) throw shutdown.Exception;
        yield return null; yield return null;
        Check(!GameServices2D.Instance && FindObjectsOfType<BigWorld.Pooling.Unity.PoolDriver>().Length == 0,
            "Closing the game drains streamed guards and pending pool requests before scene teardown");
    }

    private IEnumerator MovementChecks()
    {
        float x = body.position.x;
        player.SetInput(1, false, false);
        yield return new WaitForFixedUpdate();
        Check(body.velocity.x > 0 && body.velocity.x < player.MoveSpeed, "Horizontal input accelerates through physics instead of instantly setting full speed");
        yield return new WaitForSeconds(.18f);
        Check(body.position.x > x + .45f && Mathf.Abs(body.velocity.x - player.MoveSpeed) < .2f && Mathf.Abs(player.transform.position.z) < .001f,
            "Held movement reaches running speed without leaving the XY plane");
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
        yield return new WaitForSeconds(.18f);
        player.SetInput(1, true, true);
        yield return Until(() => body.position.x > 28.1f && player.IsGrounded, 2, "A running jump crosses the three-cell broken bridge");
        Check(level.Phase == LevelPhase.Playing && body.position.y > 1.9f,
            "The authored three-cell bridge gap is traversable with a real running jump");
        player.SetInput(0, false, false);
        yield return Warp(new Vector2(4.5f, 2.05f));
    }

    private IEnumerator EnemyChecks()
    {
        var guard = Guard(1); var enemy = guard.GetComponent<PatrolEnemy2D>(); var enemyBody = guard.GetComponent<Rigidbody2D>();
        float x = enemyBody.position.x;
        yield return new WaitForSeconds(.25f);
        Check(enemy.State == PatrolEnemyState.Patrol && Mathf.Abs(enemyBody.position.x - x) > .1f && !guard.CombatPinned,
            "A distant guard patrols under physics without keeping an unnecessary combat pin");
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
            if (Time.realtimeSinceStartup > stop) throw new TimeoutException(description);
            yield return null;
        }
    }

    private MapStreamedEntity Guard(int number)
    {
        return streamer && streamer.TryGetActor("spawn:trial-guard-" + number + ":0", out var result) ? result : null;
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

    private IEnumerator Capture(string name)
    {
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) yield break;
        yield return null;
        string path = Path.Combine(screenshotDirectory, name + ".png");
        // Batch mode does not submit Game View screenshots. Render the actual UI hierarchy through
        // the same camera temporarily, restoring the production overlay canvas immediately afterward.
        var camera = Camera.main;
        var render = new RenderTexture(1280, 720, 24);
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
            camera.Render(); RenderTexture.active = render;
            texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); texture.Apply();
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
            RenderTexture.active = oldActive; render.Release(); Destroy(render); if (texture) Destroy(texture);
        }
    }

    private void ClickButton(string prefix)
    {
        foreach (var button in hud.GetComponentsInChildren<Button>())
        {
            var label = button.GetComponentInChildren<Text>();
            if (button.isActiveAndEnabled && label && label.text.StartsWith(prefix)) { button.onClick.Invoke(); return; }
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
        report.unityVersion = Application.unityVersion; report.failure = error;
        Directory.CreateDirectory(Output); File.WriteAllText(Path.Combine(Output, "play-checks.json"), JsonUtility.ToJson(report, true));
        Time.timeScale = 1;
        if (error == null) Debug.Log("BIGWORLD_GAMEPLAY_VALIDATION_PASSED"); else Debug.LogError(error);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(error == null ? 0 : 1);
#endif
    }
}
