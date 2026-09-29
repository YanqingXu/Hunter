using System.Collections.Generic;
using BigWorld.YouYou2D;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using YouYou;

namespace BigWorld.Gameplay
{
    /// <summary>The level's screen-space HUD and menus, owned by the framework UI service.</summary>
    [AddComponentMenu("BigWorld/Game/Game HUD 2D")]
    [RequireComponent(typeof(Canvas), typeof(GraphicRaycaster))]
    public sealed class GameHud2D : UIFormBase
    {
        public Font UIFont;
        public bool IsMenuVisible => overlay && overlay.activeSelf;
        public string DisplayedPhaseTitle => headline ? headline.text : string.Empty;

        private static readonly Color Ink = new Color(.035f, .065f, .12f, .94f);
        private static readonly Color Accent = new Color(.25f, .89f, .76f);
        private static readonly Color Muted = new Color(.63f, .72f, .81f);
        private readonly List<Button> buttons = new List<Button>();
        private readonly List<Button> menuButtons = new List<Button>();
        private GameObject canvasRoot, overlay, ownedEventSystem;
        private Text healthText, objective, clock, checkpoint, progressText, headline, subtitle, badge, toast;
        private Image healthFill, progressFill;
        private RectTransform card;
        private CanvasScaler sharedScaler;
        private CanvasScaler.ScaleMode previousScaleMode;
        private CanvasScaler.ScreenMatchMode previousMatchMode;
        private Vector2 previousReference;
        private LevelSession2D session;
        private WorldSession2D world;
        private LevelPhase? displayedPhase;
        private string lastCheckpoint, lastStatusMessage;
        private float toastUntil;

        protected override void OnOpen(object userData)
        {
            OnClose();
            world = userData as WorldSession2D;
            session = LevelSession2D.Instance;
            if (!UIFont) UIFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Build();
            Render();
        }

        protected override void OnClose()
        {
            foreach (var button in buttons) if (button) button.onClick.RemoveAllListeners();
            buttons.Clear();
            menuButtons.Clear();
            if (canvasRoot) { canvasRoot.SetActive(false); Destroy(canvasRoot); }
            if (ownedEventSystem) { ownedEventSystem.SetActive(false); Destroy(ownedEventSystem); }
            if (sharedScaler)
            {
                sharedScaler.uiScaleMode = previousScaleMode;
                sharedScaler.referenceResolution = previousReference;
                sharedScaler.screenMatchMode = previousMatchMode;
                sharedScaler = null;
            }
            canvasRoot = overlay = ownedEventSystem = null;
            session = null;
            world = null;
            displayedPhase = null;
            lastCheckpoint = null;
            lastStatusMessage = null;
            toastUntil = 0;
        }

        protected override void OnBeforeDestroy() { OnClose(); }
        private void Update() { if (canvasRoot) Render(); }

        private void Build()
        {
            // UIManager instantiates this prefab under its root Canvas; both rectangles must fill it.
            if (transform is RectTransform formRect) Stretch(formRect);
            canvasRoot = new GameObject("Game HUD Content", typeof(RectTransform));
            canvasRoot.transform.SetParent(transform, false);
            Stretch(canvasRoot.GetComponent<RectTransform>());
            // The original UIFormBase owns the prefab Canvas and its sorting/freezing lifecycle.
            var canvas = CurrCanvas;
            if (canvas && canvas.rootCanvas.TryGetComponent(out sharedScaler))
            {
                previousScaleMode = sharedScaler.uiScaleMode;
                previousReference = sharedScaler.referenceResolution;
                previousMatchMode = sharedScaler.screenMatchMode;
                sharedScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                sharedScaler.referenceResolution = new Vector2(1280, 720);
                sharedScaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            }
            if (!EventSystem.current)
            {
                ownedEventSystem = new GameObject("Game HUD Event System", typeof(EventSystem), typeof(StandaloneInputModule));
                ownedEventSystem.transform.SetParent(transform, false);
            }

            var hud = Panel("Vitals", canvasRoot.transform, Ink);
            Place(hud, new Vector2(0, 1), new Vector2(0, 1), new Vector2(26, -24), new Vector2(330, 116));
            Label("Section", hud, "边境试炼 / 行动状态", 16, Accent, new Vector2(18, -12), new Vector2(296, 26));
            healthText = Label("Health", hud, "生命值", 22, Color.white, new Vector2(18, -36), new Vector2(296, 40));
            var healthTrack = Panel("Health Track", hud, new Color(.16f, .22f, .29f));
            Place(healthTrack, new Vector2(0, 1), new Vector2(0, 1), new Vector2(18, -82), new Vector2(294, 10));
            healthFill = Fill("Health Fill", healthTrack, Accent);

            var mission = Panel("Mission", canvasRoot.transform, Ink);
            Place(mission, Vector2.one, Vector2.one, new Vector2(-26, -24), new Vector2(360, 116));
            objective = Label("Objective", mission, "清除守卫，抵达出口", 20, Color.white, new Vector2(18, -12), new Vector2(324, 30));
            clock = Label("Clock", mission, "00:00", 15, Muted, new Vector2(18, -48), new Vector2(324, 24));
            checkpoint = Label("Checkpoint", mission, "检查点：起点", 15, Accent, new Vector2(18, -78), new Vector2(324, 24));

            var route = Panel("Route", canvasRoot.transform, Ink);
            Place(route, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 79), new Vector2(620, 43));
            progressText = Label("Progress", route, "关卡进度", 14, Muted, new Vector2(14, -6), new Vector2(592, 22));
            var track = Panel("Progress Track", route, new Color(.16f, .22f, .29f));
            Place(track, new Vector2(0, 1), new Vector2(0, 1), new Vector2(14, -31), new Vector2(592, 4));
            progressFill = Fill("Progress Fill", track, Accent);

            var help = Panel("Controls", canvasRoot.transform, Ink);
            Place(help, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 24), new Vector2(740, 40));
            var keys = Label("Keys", help, "A / D 移动    空格 跳跃    J 射击    K 近战    Esc 暂停", 17, Muted,
                new Vector2(12, -4), new Vector2(716, 32));
            keys.alignment = TextAnchor.MiddleCenter;
            toast = Label("Checkpoint Toast", canvasRoot.transform, "", 20, Accent, Vector2.zero, new Vector2(720, 38));
            Place(toast.rectTransform, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -155), new Vector2(720, 38));
            toast.alignment = TextAnchor.MiddleCenter;

            var shade = Panel("Menu Overlay", canvasRoot.transform, new Color(.018f, .03f, .06f, .88f));
            Stretch(shade);
            shade.GetComponent<Image>().raycastTarget = true;
            overlay = shade.gameObject;
            card = Panel("Menu Card", shade, new Color(.055f, .095f, .16f, 1));
            Place(card, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(650, 520));
            var line = Panel("Accent", card, Accent);
            Place(line, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, new Vector2(650, 4));
            badge = Label("Badge", card, "BORDER TRIAL  /  01", 16, Accent, new Vector2(44, -36), new Vector2(562, 26));
            headline = Label("Heading", card, "边境试炼", 46, Color.white, new Vector2(40, -76), new Vector2(570, 66));
            subtitle = Label("Description", card, "", 21, Muted, new Vector2(44, -162), new Vector2(560, 108));
            subtitle.verticalOverflow = VerticalWrapMode.Truncate;
            subtitle.horizontalOverflow = HorizontalWrapMode.Wrap;
            for (int i = 0; i < 3; i++)
            {
                var button = MakeButton(card, "Action " + i, new Vector2(44, -298 - i * 60));
                menuButtons.Add(button);
            }
            var foot = Label("Footer", card, "穿过边境 · 激活检查点 · 清除守卫 · 抵达出口", 14, Muted,
                new Vector2(44, -484), new Vector2(562, 24));
            foot.alignment = TextAnchor.MiddleCenter;
        }

        private void Render()
        {
            if (!session) session = LevelSession2D.Instance;
            if (!session) { ShowPhase(LevelPhase.Loading); return; }
            var actor = session.Player ? session.Player.GetComponent<SkillActor2D>() : null;
            float health = actor ? actor.Health : 0;
            SetFill(healthFill, actor ? Mathf.Clamp01(health / Mathf.Max(1, actor.MaxHealth)) : 0);
            healthFill.color = healthFill.fillAmount <= .3f ? new Color(1, .4f, .42f) : Accent;
            healthText.text = actor ? "生命值  " + health.ToString("0") + " / " + actor.MaxHealth.ToString("0") : "生命值  —";
            objective.text = session.DefeatedEnemies >= session.RequiredKills ? "守卫已清除 · 前往出口" :
                "清除守卫  " + session.DefeatedEnemies + " / " + session.RequiredKills;
            clock.text = "用时 " + FormatTime(session.ElapsedSeconds) + "    失误 " + session.DeathCount;
            checkpoint.text = "检查点：" + (string.IsNullOrEmpty(session.CheckpointName) ? "起点" : session.CheckpointName);
            SetFill(progressFill, Mathf.Clamp01(session.Progress01));
            progressText.text = "边境通路  /  " + Mathf.RoundToInt(Mathf.Clamp01(session.Progress01) * 100) + "%";
            if (session.Phase == LevelPhase.Playing && lastStatusMessage != session.StatusMessage)
            {
                lastStatusMessage = session.StatusMessage;
                if (!string.IsNullOrEmpty(lastStatusMessage))
                {
                    toast.text = lastStatusMessage;
                    toastUntil = Time.unscaledTime + 3;
                }
            }
            if (lastCheckpoint != session.CheckpointName)
            {
                if (!string.IsNullOrEmpty(lastCheckpoint) && session.Phase == LevelPhase.Playing)
                {
                    toast.text = "检查点已激活  /  " + session.CheckpointName;
                    toastUntil = Time.unscaledTime + 3;
                }
                lastCheckpoint = session.CheckpointName;
            }
            toast.gameObject.SetActive(session.Phase == LevelPhase.Playing && Time.unscaledTime < toastUntil);
            ShowPhase(session.Phase);
            if (session.Phase == LevelPhase.Error && !string.IsNullOrEmpty(session.StatusMessage)) subtitle.text = session.StatusMessage;
        }

        private void ShowPhase(LevelPhase phase)
        {
            if (displayedPhase == phase) return;
            displayedPhase = phase;
            overlay.SetActive(phase != LevelPhase.Playing);
            foreach (var button in menuButtons) { button.onClick.RemoveAllListeners(); button.gameObject.SetActive(false); }
            badge.text = "BORDER TRIAL  /  01";
            switch (phase)
            {
                case LevelPhase.Ready:
                    headline.text = "边境试炼";
                    subtitle.text = "越过断层，清除沿途守卫。\n激活检查点后，抵达最右侧出口完成试炼。";
                    Action(0, "开始游戏    ↵", () => session.StartRun()); break;
                case LevelPhase.Paused:
                    headline.text = "稍作休整";
                    subtitle.text = "旅途已暂停。\n准备好后，继续向边境前进。";
                    Action(0, "继续游戏    Esc", () => session.TogglePause());
                    Action(1, "重新开始", () => session.RestartRun());
                    Action(2, "返回标题", () => session.ReturnToTitle()); break;
                case LevelPhase.Dead:
                    headline.text = "再试一次";
                    subtitle.text = "你倒在了边境通路上。\n从「" + session.CheckpointName + "」继续，已激活的检查点会保留。";
                    Action(0, "检查点重试    R", () => session.RetryCheckpoint());
                    Action(1, "返回标题", () => session.ReturnToTitle()); break;
                case LevelPhase.Won:
                    badge.text = "MISSION COMPLETE";
                    headline.text = "试炼完成";
                    subtitle.text = "边境通路已经打通。\n用时 " + FormatTime(session.ElapsedSeconds) + "    失误 " + session.DeathCount + " 次";
                    Action(0, "再玩一次    R", () => session.RestartRun());
                    Action(1, "返回标题", () => session.ReturnToTitle()); break;
                case LevelPhase.Error:
                    headline.text = "关卡暂不可用";
                    subtitle.text = "关卡加载失败，请返回标题后重试。";
                    Action(0, "返回标题", () => session.ReturnToTitle()); break;
                case LevelPhase.Respawning:
                    headline.text = "返回检查点"; subtitle.text = "正在准备周围地形……"; break;
                default:
                    headline.text = "边境试炼"; subtitle.text = "正在准备关卡……"; break;
            }
            if (overlay.activeSelf && menuButtons[0].gameObject.activeSelf && EventSystem.current)
                EventSystem.current.SetSelectedGameObject(menuButtons[0].gameObject);
        }

        private void Action(int index, string title, UnityAction callback)
        {
            var button = menuButtons[index];
            button.gameObject.SetActive(true);
            button.GetComponentInChildren<Text>().text = title;
            button.onClick.AddListener(() => { if (session) callback(); });
        }

        private Button MakeButton(Transform parent, string name, Vector2 position)
        {
            var rect = Panel(name, parent, Accent);
            Place(rect, new Vector2(0, 1), new Vector2(0, 1), position, new Vector2(562, 48));
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = rect.GetComponent<Image>();
            button.targetGraphic.raycastTarget = true;
            var colors = button.colors;
            colors.highlightedColor = new Color(.79f, 1, .94f);
            colors.pressedColor = new Color(.58f, .8f, .76f);
            colors.selectedColor = colors.highlightedColor;
            button.colors = colors;
            var text = Label("Label", rect, "", 21, Ink, Vector2.zero, Vector2.zero);
            Stretch(text.rectTransform); text.alignment = TextAnchor.MiddleCenter;
            buttons.Add(button);
            return button;
        }

        private Text Label(string name, Transform parent, string value, int size, Color color, Vector2 position, Vector2 dimensions)
        {
            var rect = new GameObject(name, typeof(RectTransform), typeof(Text)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            Place(rect, new Vector2(0, 1), new Vector2(0, 1), position, dimensions);
            var text = rect.GetComponent<Text>();
            text.font = UIFont; text.fontSize = size; text.text = value; text.color = color;
            text.raycastTarget = false; text.alignment = TextAnchor.MiddleLeft;
            return text;
        }

        private static RectTransform Panel(string name, Transform parent, Color color)
        {
            var rect = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.GetComponent<Image>().color = color;
            rect.GetComponent<Image>().raycastTarget = false;
            return rect;
        }

        private static Image Fill(string name, Transform parent, Color color)
        {
            var rect = Panel(name, parent, color); Stretch(rect);
            return rect.GetComponent<Image>();
        }

        // A sprite-less UGUI Image ignores Image.Type.Filled, so crop its rectangle instead.
        private static void SetFill(Image fill, float value)
        { fill.fillAmount = value; fill.rectTransform.anchorMax = new Vector2(value, 1); }

        private static void Place(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        { rect.anchorMin = rect.anchorMax = anchor; rect.pivot = pivot; rect.anchoredPosition = position; rect.sizeDelta = size; }
        private static void Stretch(RectTransform rect)
        { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
        private static string FormatTime(float seconds)
        { int value = Mathf.Max(0, Mathf.FloorToInt(seconds)); return (value / 60).ToString("00") + ":" + (value % 60).ToString("00"); }
    }
}
