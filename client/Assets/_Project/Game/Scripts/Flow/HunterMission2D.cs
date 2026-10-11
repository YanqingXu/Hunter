using System.Collections.Generic;
using BigWorld.YouYou2D;
using UnityEngine;

namespace BigWorld.Gameplay
{
    [DefaultExecutionOrder(-1300), DisallowMultipleComponent]
    public sealed class HunterMission2D : MonoBehaviour
    {
        public Sprite EnemySprite;
        public Sprite MarkerSprite;
        public PhysicsMaterial2D EnemyMaterial;
        [Min(.1f)] public float ExtractionSeconds = 10;
        [Min(0)] public int BountyValue = 250;
        public Vector2[] CluePositions = { new Vector2(12, 2.5f), new Vector2(34, 2.5f), new Vector2(58, 2.5f) };
        public bool UseRegionClues = true;
        public Vector2[] RegionClueSockets = { new Vector2(10, 2.5f), new Vector2(14, 2.5f),
            new Vector2(30, 2.5f), new Vector2(38, 2.5f), new Vector2(54, 2.5f), new Vector2(58, 2.5f),
            new Vector2(66, 2.5f), new Vector2(77, 2.5f) };
        public float[] RegionEdges = { 0, 24, 48, 64, 80 };
        public Vector2 BossPosition = new Vector2(68, 2.05f);
        public bool RandomizeBossRegion = true;
        public Vector2[] BossRegions = { new Vector2(22, 2.05f), new Vector2(40, 2.05f), new Vector2(60, 2.05f), new Vector2(68, 2.05f) };
        public Vector2[] ReinforcementPositions = { new Vector2(14, 2.05f), new Vector2(40, 2.05f), new Vector2(65, 2.05f) };
        public HunterMission State { get; private set; }
        public HuntEnemy2D Boss { get; private set; }
        public bool IsSpecialView { get; private set; }
        public string Hint { get; private set; } = "";
        public bool ExitContested { get; private set; }
        public Vector2 BountyPosition => bountyPosition;
        public HunterSearchLayout SearchLayout { get; private set; }
        public int RegionCount => BossRegions.Length;
        public int BossRegion => bossRegion;
        public bool IsBossLocated => State != null && (State.BossDefeated || State.ClueCount >= State.RequiredClues);
        public bool InExit { get; private set; }
        public bool ExitMarkerVisible => State != null && State.SecondWaveStarted;
        public int ClueSocketCount => clues.Length;
        public Vector2 CluePosition(int socket) => UseRegionClues ? RegionClueSockets[socket] : CluePositions[socket];
        public bool IsRegionCandidate(int region) => SearchLayout != null ? SearchLayout.IsCandidate(region) : candidateRegions[region];
        public bool IsClueAvailable(int socket) => socket >= 0 && socket < clues.Length && State.Stage == HuntStage.Tracking &&
            (SearchLayout != null ? SearchLayout.IsClueAvailable(socket) : !State.HasClue(socket.ToString()));
        public bool IsClueVisible(int socket) => socket >= 0 && socket < clues.Length && clues[socket] && clues[socket].enabled;
        public int DefeatedReinforcements
        {
            get
            {
                int count = 0;
                foreach (var enemy in enemies)
                    if (enemy && !enemy.IsBoss && enemy.Actor && enemy.Actor.Health <= 0) count++;
                return count;
            }
        }
        private LevelSession2D level;
        private Transform markers;
        private SpriteRenderer[] clues;
        private SpriteRenderer bounty;
        private Vector2 bountyPosition;
        private readonly List<HuntEnemy2D> enemies = new List<HuntEnemy2D>();
        private bool[] spawned;
        private bool bossSpawned;
        private int bossRegion;
        private bool[] candidateRegions;
        private Transform viewTarget;
        private bool touchView;
        private Vector2 touchPan;
        private readonly RaycastHit2D[] hits = new RaycastHit2D[16];

        private void Awake()
        {
            level = GetComponent<LevelSession2D>();
            markers = new GameObject("Hunt Objectives").transform; markers.SetParent(transform, false);
            viewTarget = new GameObject("Investigation Camera Target").transform; viewTarget.SetParent(transform, false);
            bounty = Marker("Boss Bounty", BossPosition, new Color(1, .85f, .1f));
            ResetMission();
        }
        private SpriteRenderer Marker(string title, Vector2 position, Color color)
        {
            var marker = new GameObject(title).AddComponent<SpriteRenderer>(); marker.transform.SetParent(markers, false);
            marker.transform.position = position; marker.transform.localScale = new Vector3(.5f, .6f, 1);
            marker.sprite = MarkerSprite; marker.sortingOrder = 28; marker.color = color; return marker;
        }
        public void ResetMission()
        {
            if (BossRegions == null || BossRegions.Length < 2 || RegionEdges == null || RegionEdges.Length != BossRegions.Length + 1)
                throw new System.ArgumentException("Search map requires one interval per boss region.");
            if (UseRegionClues && (RegionClueSockets == null || RegionClueSockets.Length != BossRegions.Length * 2))
                throw new System.ArgumentException("Each search region requires two authored clue sockets.");
            for (int i = 0; i < BossRegions.Length; i++)
            {
                if (!(RegionEdges[i + 1] > RegionEdges[i]) || BossRegions[i].x < RegionEdges[i] || BossRegions[i].x >= RegionEdges[i + 1])
                    throw new System.ArgumentException("Search regions must be ordered and contain their boss spawn.");
                if (UseRegionClues)
                    for (int j = i * 2; j < i * 2 + 2; j++)
                        if (RegionClueSockets[j].x < RegionEdges[i] || RegionClueSockets[j].x >= RegionEdges[i + 1])
                            throw new System.ArgumentException("Clue sockets must lie in their search region.");
            }
            if (level && level.Elites) level.Elites.ClearRun();
            if (Boss && Boss.Actor) Boss.Actor.Died -= OnBossDied;
            foreach (var enemy in enemies) if (enemy) { enemy.gameObject.SetActive(false); Destroy(enemy.gameObject); }
            enemies.Clear(); Boss = null; bossSpawned = false;
            bossRegion = RandomizeBossRegion ? Random.Range(0, BossRegions.Length) : BossRegions.Length - 1;
            BossPosition = BossRegions[bossRegion];
            SearchLayout = UseRegionClues ? new HunterSearchLayout(BossRegions.Length, bossRegion, Random.Range(0, int.MaxValue)) : null;
            State = new HunterMission(UseRegionClues ? SearchLayout.RequiredClues : CluePositions.Length, ExtractionSeconds, BountyValue);
            if (clues != null) foreach (var clue in clues) if (clue) { clue.gameObject.SetActive(false); Destroy(clue.gameObject); }
            clues = new SpriteRenderer[UseRegionClues ? RegionClueSockets.Length : CluePositions.Length];
            for (int i = 0; i < clues.Length; i++)
                if (SearchLayout == null || SearchLayout.HasClueSocket(i))
                { clues[i] = Marker("Clue " + (i + 1), CluePosition(i), new Color(.2f, .7f, 1)); clues[i].enabled = false; }
            candidateRegions = new bool[BossRegions.Length];
            for (int i = 0; i < candidateRegions.Length; i++) candidateRegions[i] = true;
            spawned = new bool[ReinforcementPositions.Length]; State.Reset();
            bountyPosition = BossPosition; bounty.enabled = false; IsSpecialView = false; touchView = false; touchPan = Vector2.zero; Hint = "";
            InExit = ExitContested = false;
            if (level && level.Player && level.FollowCamera) level.FollowCamera.Target = level.Player.transform;
        }
        private bool TerrainReady(Vector2 position)
        {
            if (!level || !level.World || !level.World.IsReady) return false;
            var map = level.World.Map;
            var cell = map.WorldToCell(position + Vector2.down * .12f);
            return map.IsCellLoaded(cell.x, cell.y);
        }
        private HuntEnemy2D Spawn(Vector2 position, bool boss)
        {
            var root = new GameObject(boss ? "Hunt Boss" : "Second Wave Guard");
            root.transform.SetParent(transform, false); root.transform.position = position;
            var enemy = root.AddComponent<HuntEnemy2D>(); enemy.Configure(boss, EnemySprite, EnemyMaterial);
            enemies.Add(enemy); return enemy;
        }
        private void Update()
        {
            if (!level || State == null) return;
            IsSpecialView = CanInvestigate() && (level.Player.ReadKeyboard ? Input.GetKey(KeyCode.V) : touchView);
            if (IsSpecialView) level.Player.CancelSceneInteraction("调查操作中断场景交互。");
            UpdateInvestigationCamera();
            if (!bossSpawned && TerrainReady(BossPosition))
            { Boss = Spawn(BossPosition, true); Boss.Actor.Died += OnBossDied; bossSpawned = true; }
            if (State.SecondWaveStarted)
                for (int i = 0; i < spawned.Length; i++)
                    if (!spawned[i] && TerrainReady(ReinforcementPositions[i])) { Spawn(ReinforcementPositions[i], false); spawned[i] = true; }
            // Freeze scene-owned actors when their terrain is streamed out. Health and wave identity remain run-owned.
            foreach (var enemy in enemies)
                if (enemy)
                {
                    enemy.ReturnToSpawnIfFallen(level.FallDeathY);
                    enemy.gameObject.SetActive(TerrainReady(enemy.transform.position));
                }
            for (int i = 0; i < clues.Length; i++)
                if (clues[i]) clues[i].enabled = IsSpecialView && IsClueAvailable(i) && TerrainReady(CluePosition(i)) &&
                    Vector2.Distance(level.Player.transform.position, CluePosition(i)) <= 12;
            bounty.transform.position = bountyPosition;
            bounty.enabled = State.Stage == HuntStage.BountyAvailable;
            Hint = "";
            if (!level.IsGameplayActive || !level.Player) return;
            if (State.Stage == HuntStage.Tracking && NearestClue() >= 0) Hint = "E 调查线索";
            if (State.Stage == HuntStage.BountyAvailable && InReach(bountyPosition)) Hint = "E 拾取 Boss 赏金";
            if (IsSpecialView) Hint = level.Player.ReadKeyboard ? "V + 方向键移动视角；松开 V 后 E 调查" : "摇杆移动视角；松开调查后点交互";
            ExitContested = IsExitContested();
            InExit = level.ExitPoint && Mathf.Abs(level.Player.transform.position.x - level.ExitPoint.position.x) <= .9f &&
                Mathf.Abs(level.Player.transform.position.y - level.ExitPoint.position.y) <= 2;
            if (State.TickExtraction(Time.deltaTime, InExit, ExitContested)) level.TryComplete();
        }
        private void UpdateInvestigationCamera()
        {
            if (!level.Player || !level.FollowCamera) return;
            var follow = level.FollowCamera;
            if (!IsSpecialView)
            { if (follow.Target == viewTarget) { follow.Target = level.Player.transform; follow.Snap(); } return; }
            if (follow.Target != viewTarget) { viewTarget.position = level.Player.transform.position; follow.Target = viewTarget; follow.Snap(); }
            var input = level.Player.ReadKeyboard ? new Vector2((Input.GetKey(KeyCode.RightArrow) ? 1 : 0) - (Input.GetKey(KeyCode.LeftArrow) ? 1 : 0),
                (Input.GetKey(KeyCode.UpArrow) ? 1 : 0) - (Input.GetKey(KeyCode.DownArrow) ? 1 : 0)) : touchPan;
            Vector2 offset = (Vector2)(viewTarget.position - level.Player.transform.position) + Vector2.ClampMagnitude(input, 1) * 6 * Time.deltaTime;
            viewTarget.position = level.Player.transform.position + (Vector3)Vector2.ClampMagnitude(offset, 10);
        }
        private bool CanInvestigate() => level && level.IsGameplayActive && level.Player && level.Player.isActiveAndEnabled &&
            level.Player.ControlEnabled && !level.Player.IsHurt && !level.Player.IsUsingItem && !level.Player.IsOnLadder;
        public void SetTouchInvestigation(bool held, Vector2 pan)
        {
            touchView = held && CanInvestigate() && !level.Player.ReadKeyboard;
            if (touchView) level.Player.CancelSceneInteraction("调查操作中断场景交互。");
            touchPan = float.IsNaN(pan.x) || float.IsNaN(pan.y) || float.IsInfinity(pan.x) || float.IsInfinity(pan.y)
                ? Vector2.zero : Vector2.ClampMagnitude(pan, 1);
            if (level && level.Player && !level.Player.ReadKeyboard) IsSpecialView = touchView;
        }
        private int NearestClue()
        {
            if (State.Stage != HuntStage.Tracking) return -1;
            int nearest = -1; float distance = float.PositiveInfinity;
            for (int i = 0; i < clues.Length; i++)
            {
                float value = Vector2.Distance(level.Player.transform.position, CluePosition(i));
                if (IsClueAvailable(i) && value < distance && InReach(CluePosition(i))) { nearest = i; distance = value; }
            }
            return nearest;
        }
        private bool InReach(Vector2 position)
        {
            if (!level.Player || Vector2.Distance(level.Player.transform.position, position) > 2) return false;
            int count = Physics2D.LinecastNonAlloc(level.Player.transform.position + Vector3.up * .7f, position, hits, 1);
            for (int i = 0; i < count; i++) if (hits[i].collider && !hits[i].collider.isTrigger) return false;
            return true;
        }
        public bool TryInteract()
        {
            if (!level.IsGameplayActive || !level.Player || !level.Player.CanInteract || IsSpecialView) return false;
            int clue = NearestClue();
            if (clue >= 0 && State.Investigate(clue.ToString()))
            {
                if (SearchLayout != null)
                {
                    int excluded;
                    if (!SearchLayout.TryCollect(clue, out excluded)) throw new System.InvalidOperationException("Scene clue and search state diverged.");
                }
                else for (int i = 0; i < candidateRegions.Length; i++)
                        if (i != bossRegion && candidateRegions[i]) { candidateRegions[i] = false; break; }
                return true;
            }
            if (State.Stage != HuntStage.BountyAvailable || !InReach(bountyPosition)) return false;
            return State.PickUpBounty();
        }
        private void OnBossDied()
        {
            bountyPosition = Boss.transform.position + Vector3.up * .5f;
            State.DefeatBoss();
        }
        public void OnPlayerDied()
        {
            if (State.DropBounty())
            {
                // The body position is the feet and can sit slightly inside the ground contact skin.
                // Raise the token so its line-of-sight ray does not end inside terrain.
                bountyPosition = level.Player.GetComponent<Rigidbody2D>().position + Vector2.up * .5f;
                // Keep a dropped token reachable after a lethal fall into the map void.
                if (bountyPosition.y < level.RespawnPosition.y - 1) bountyPosition = level.RespawnPosition + Vector2.up * .5f;
            }
        }
        private bool IsExitContested()
        {
            if (!level.ExitPoint) return false;
            if (level.Elites && level.Elites.Contests(level.ExitPoint.position)) return true;
            foreach (var enemy in enemies)
                if (enemy && enemy.Actor && enemy.Actor.IsAlive && Vector2.Distance(enemy.transform.position, level.ExitPoint.position) < 4) return true;
            foreach (var guard in FindObjectsOfType<PatrolEnemy2D>())
                if (guard.GetComponent<SkillActor2D>().IsAlive && Vector2.Distance(guard.transform.position, level.ExitPoint.position) < 4) return true;
            return false;
        }
        public string Objective
        {
            get
            {
                if (State.Stage == HuntStage.Extracted) return "撤离成功 · 赏金 " + State.Reward;
                if (InExit)
                    return ExitContested ? "撤离受阻：清除出口附近敌人" :
                        (State.Stage == HuntStage.Carrying ? "携赏金撤离 · " : "无赏金撤离 · ") +
                        Mathf.CeilToInt(State.ExtractionSeconds - State.ExtractionElapsed) + " 秒";
                switch (State.Stage)
                {
                    case HuntStage.Tracking: return "调查线索 " + State.ClueCount + " / " + State.RequiredClues + " · 按住 V 寻找";
                    case HuntStage.Hunting: return "Boss (" + BossPosition.x.ToString("0") + ") · " + (Boss && Boss.Actor ? Boss.Actor.Health.ToString("0") + "/150" + (Boss.IsEnraged ? " 狂暴" : "") : "前往标记区域");
                    case HuntStage.BountyAvailable: return "拾取赏金 · E 交互";
                    case HuntStage.Carrying: return ExitContested ? "撤离受阻：清除出口附近敌人" : "前往右侧撤离点 (74) · " + Mathf.CeilToInt(State.ExtractionSeconds - State.ExtractionElapsed) + " 秒";
                    default: return "撤离成功 · 赏金 " + State.Reward;
                }
            }
        }
        public string SearchRegions
        {
            get
            {
                if (InExit || State.Stage == HuntStage.Carrying || State.Stage == HuntStage.Extracted) return "撤离点：东侧边缘 (74)";
                if (State.Stage == HuntStage.BountyAvailable) return "赏金位置：" + bountyPosition.x.ToString("0");
                var regions = new List<string>();
                for (int i = 0; i < candidateRegions.Length; i++)
                    regions.Add(IsRegionCandidate(i) ? (i + 1).ToString() : "×");
                return IsBossLocated ? "已定位 Boss · 区域 " + (bossRegion + 1) : "搜索区域：" + string.Join(" / ", regions) + " · 线索仅调查视角可见";
            }
        }
        private void OnDestroy() { if (Boss && Boss.Actor) Boss.Actor.Died -= OnBossDied; }
    }
}
