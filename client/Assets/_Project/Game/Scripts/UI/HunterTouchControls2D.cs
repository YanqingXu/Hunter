using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BigWorld.Gameplay
{
    /// <summary>Touch adapter runs before the mission and player, so a frame has one input owner.</summary>
    [DefaultExecutionOrder(-1400)]
    public sealed class HunterTouchControls2D : MonoBehaviour
    {
        public bool TouchMode { get; private set; }
        public bool SprintSelected { get; private set; }
        public RectTransform SafeContent { get; private set; }
        public HunterTouchControl Stick { get; private set; }
        public bool CanReceiveInput => TouchMode && isActiveAndEnabled && focused && !applicationPaused &&
            level && level.IsGameplayActive && player && player.isActiveAndEnabled && player.ControlEnabled &&
            !player.IsHurt && Time.timeScale > 0;
        private LevelSession2D level;
        private PlayerController2D player;
        private RectTransform safeArea;
        private RectTransform menu;
        private Font font;
        private bool focused = true, applicationPaused, ownsInput, previousPlayerKeyboard, previousLevelKeyboard, jumpPressed;
        private bool arrangeOnUpdate;
        private readonly Dictionary<string, HunterTouchControl> controls = new Dictionary<string, HunterTouchControl>();
        private readonly List<System.Action> pending = new List<System.Action>();
        private Text guide;
        private Vector2 lastSize;
        private Rect lastSafe;
        private Vector2 viewportOverride;
        private Rect safeOverride;
        private readonly List<PanelLayout> panels = new List<PanelLayout>();
        private sealed class PanelLayout
        {
            public RectTransform rect;
            public Transform parent;
            public Vector2 min, max, pivot, position, size;
            public Vector3 scale;
            public bool active;
        }

        public void Initialize(LevelSession2D session, Font uiFont, Transform hud)
        {
            level = session; player = level.Player; font = uiFont;
            menu = hud.Find("Menu Overlay") as RectTransform;
            safeArea = Rect("Touch Safe Area", transform);
            SafeContent = Rect("Touch Content", safeArea);
            Stick = Control("Stick", "移动 / 瞄准", new Vector2(112, 104), new Vector2(172, 172), Vector2.zero);
            Stick.IsStick = true;
            Stick.Label.text = ""; // The guide describes the stick; the thumb must not cover its label.
            Stick.Knob = Rect("Knob", Stick.transform);
            Place(Stick.Knob, new Vector2(.5f, .5f), Vector2.zero, new Vector2(54, 54));
            var knob = Stick.Knob.gameObject.AddComponent<Image>(); knob.color = new Color(.25f, .89f, .76f, .65f); knob.raycastTarget = false;
            Control("Sprint", "奔跑", new Vector2(62, 230), new Vector2(98, 58), Vector2.zero,
                () => {
                    if (player.IsOnLadder || player.NearbyLadder || player.IsSceneInteracting || player.NearbySceneInteraction) { SprintSelected = false; player.TryInteract(); }
                    else if (SprintSelected) SprintSelected = false;
                    else if (!player.IsProne && player.Stamina > 1 && !level.Hunt.IsSpecialView) SprintSelected = true;
                });
            Control("Prone", "匍匐", new Vector2(168, 230), new Vector2(98, 58), Vector2.zero,
                () => { if (player.TrySetProne(!player.IsProne)) SprintSelected = false; });
            Control("Jump", "跳跃", new Vector2(-64, 54), new Vector2(104, 80), new Vector2(1, 0), () => jumpPressed = true);
            Control("Shoot", "射击\n长按", new Vector2(-64, 151), new Vector2(104, 96), new Vector2(1, 0));
            Control("Melee", "近战", new Vector2(-176, 54), new Vector2(100, 80), new Vector2(1, 0), () => player.TryMelee());
            Control("Interact", "交互", new Vector2(-176, 151), new Vector2(100, 80), new Vector2(1, 0), () => player.TryInteract());
            Control("Reload", "换弹", new Vector2(-282, 54), new Vector2(96, 80), new Vector2(1, 0), () => player.TryReload());
            Control("Weapon", "切枪", new Vector2(-282, 151), new Vector2(96, 80), new Vector2(1, 0),
                () => player.TrySwitchWeapon((player.Weapons.ActiveIndex + 1) % Mathf.Max(1, player.Weapons.WeaponCount)));
            Control("NextItem", "切道具", new Vector2(-282, 230), new Vector2(96, 58), new Vector2(1, 0), () => player.TrySelectItem(player.ItemIndex + 1));
            Control("UseItem", "使用", new Vector2(-176, 230), new Vector2(100, 58), new Vector2(1, 0), () => player.TryUseItem());
            Control("OnceSkill", "包扎技", new Vector2(-64, 230), new Vector2(104, 58), new Vector2(1, 0), () => player.TryUseOnceSkill());
            Control("Investigate", "调查\n按住", new Vector2(277, 151), new Vector2(96, 80), Vector2.zero);
            var pause = Control("Pause", "暂停", new Vector2(277, 54), new Vector2(96, 80), Vector2.zero);
            // Pause is a session command and remains available during hit stun or disabled character control.
            pause.AcceptInput = () => TouchMode && isActiveAndEnabled && focused && !applicationPaused && level && level.IsGameplayActive;
            pause.Pressed = () => { level.TogglePause(); CancelAll(); safeArea.gameObject.SetActive(false); };
            guide = TextLabel("Touch Guide", SafeContent, "摇杆左右移动，上下调整瞄准；按住调查可移动视角", 14);
            Place(guide.rectTransform, new Vector2(.5f, 0), new Vector2(0, 287), new Vector2(770, 34));

            foreach (string name in new[] { "Vitals", "Run Skills", "Mission", "Weapon", "Run Items", "Route", "Controls", "Checkpoint Toast" })
            {
                var rect = hud.Find(name) as RectTransform;
                if (!rect) continue;
                panels.Add(new PanelLayout { rect = rect, parent = rect.parent, min = rect.anchorMin, max = rect.anchorMax,
                    pivot = rect.pivot, position = rect.anchoredPosition, size = rect.sizeDelta, scale = rect.localScale, active = rect.gameObject.activeSelf });
            }
            safeArea.gameObject.SetActive(false);
        }

        public HunterTouchControl FindControl(string name) { HunterTouchControl value; return controls.TryGetValue(name, out value) ? value : null; }
        public void SetTouchMode(bool enabled)
        {
            if (TouchMode == enabled) return;
            if (player) { player.CancelInput(); player.CancelSceneInteraction("输入模式切换，交互已取消。"); }
            CancelAll(); TouchMode = enabled;
            if (enabled && isActiveAndEnabled) AcquireInput(); else RestoreInput();
            ArrangePanels(enabled); RefreshLayout();
            safeArea.gameObject.SetActive(enabled && CanReceiveInput);
        }
        private void AcquireInput()
        {
            if (ownsInput || !player || !level) return;
            previousPlayerKeyboard = player.ReadKeyboard; previousLevelKeyboard = level.ReadKeyboard;
            player.ReadKeyboard = false; level.ReadKeyboard = false; ownsInput = true;
        }
        private void RestoreInput()
        {
            if (!ownsInput) return;
            if (player) player.ReadKeyboard = previousPlayerKeyboard;
            if (level) level.ReadKeyboard = previousLevelKeyboard;
            ownsInput = false;
        }
        private void OnEnable() { if (TouchMode) { AcquireInput(); arrangeOnUpdate = true; } }
        private void OnDisable()
        {
            if (player) player.CancelSceneInteraction("操作界面停用，交互已取消。");
            CancelAll(); RestoreInput();
            // Unity forbids changing sibling order while a parent is being deactivated.
            // Explicit component disable can restore desktop panels; parent shutdown already owns their lifetime.
            if (TouchMode && gameObject.activeInHierarchy) ArrangePanels(false);
            if (safeArea) safeArea.gameObject.SetActive(false);
        }
        private void OnApplicationFocus(bool value)
        { focused = value; if (!value) { CancelAll(); if (player) player.CancelSceneInteraction("失去焦点，交互已取消。"); } }
        private void OnApplicationPause(bool value)
        { applicationPaused = value; if (value) { CancelAll(); if (player) player.CancelSceneInteraction("应用挂起，交互已取消。"); } }
        public void CancelAll()
        {
            foreach (var control in controls.Values) if (control) control.Cancel();
            pending.Clear(); jumpPressed = false; SprintSelected = false;
            if (ownsInput && player) player.CancelInput();
            if (level && level.Hunt) level.Hunt.SetTouchInvestigation(false, Vector2.zero);
        }
        private void Update()
        {
            if (!TouchMode || !safeArea) return;
            if (arrangeOnUpdate) { ArrangePanels(true); arrangeOnUpdate = false; }
            RefreshLayout();
            bool visible = focused && !applicationPaused && level && level.IsGameplayActive;
            if (safeArea.gameObject.activeSelf != visible) safeArea.gameObject.SetActive(visible);
            if (!CanReceiveInput) { CancelAll(); return; }
            // Camera input uses the same stick but never moves or attacks with the character.
            bool view = FindControl("Investigate").Held && !player.IsUsingItem && !player.IsOnLadder;
            level.Hunt.SetTouchInvestigation(view, Stick.Value);
            if (view) { player.CancelInput(); SprintSelected = false; }
            else player.SetDirectionalInput(Stick.Value, false, FindControl("Jump").Held);
            foreach (var action in pending.ToArray())
            { if (!CanReceiveInput) break; action(); }
            pending.Clear();
            if (!CanReceiveInput)
            {
                if (!level.IsGameplayActive) safeArea.gameObject.SetActive(false);
                CancelAll(); return;
            }
            if (player.IsProne || player.Stamina <= 0 || view || player.IsSceneInteracting || player.IsOnLadder) SprintSelected = false;
            player.SetSprint(SprintSelected);
            player.SetDirectionalInput(Stick.Value, jumpPressed, FindControl("Jump").Held);
            jumpPressed = false;
            if (FindControl("Shoot").Held) player.TryShoot();
            FindControl("Sprint").Label.text = player.IsOnLadder ? "攀爬中" : player.IsSceneInteracting ? "取消" : player.NearbyLadder || player.NearbySceneInteraction ? "交互" : SprintSelected ? "奔跑 开" : "奔跑";
            guide.text = player.IsOnLadder ? "摇杆上下攀爬，左右安全离梯；其他动作不可用" : "摇杆左右移动，上下调整瞄准；按住调查可移动视角";
            FindControl("Prone").Label.text = player.IsProne ? "起身" : "匍匐";
        }

        /// <summary>Preview a device viewport in the editor; zero size returns to the real Screen.safeArea.</summary>
        public void SetViewportPreview(Vector2 size, Rect safe)
        { viewportOverride = size; safeOverride = safe; lastSize = Vector2.zero; RefreshLayout(); }
        public void RefreshLayout()
        {
            if (!safeArea || !TouchMode) return;
            Vector2 size = viewportOverride.x > 0 && viewportOverride.y > 0 ? viewportOverride : new Vector2(Screen.width, Screen.height);
            Rect safe = viewportOverride.x > 0 && viewportOverride.y > 0 ? safeOverride : Screen.safeArea;
            if (safe.width <= 0 || safe.height <= 0) safe = new Rect(Vector2.zero, size);
            var parent = (RectTransform)transform;
            Vector2 extent = Vector2.Scale(parent.rect.size, new Vector2(safe.width / size.x, safe.height / size.y));
            if (size != lastSize || safe != lastSafe)
            { CancelAll(); if (player) player.CancelSceneInteraction("画面布局改变，交互已取消。"); lastSize = size; lastSafe = safe; }
            safeArea.anchorMin = new Vector2(safe.xMin / size.x, safe.yMin / size.y);
            safeArea.anchorMax = new Vector2(safe.xMax / size.x, safe.yMax / size.y);
            safeArea.offsetMin = safeArea.offsetMax = Vector2.zero;
            if (menu) { menu.anchorMin = safeArea.anchorMin; menu.anchorMax = safeArea.anchorMax; menu.offsetMin = menu.offsetMax = Vector2.zero; }
            float scale = Mathf.Max(.01f, Mathf.Min(extent.x / 800, extent.y / 620));
            SafeContent.anchorMin = SafeContent.anchorMax = SafeContent.pivot = new Vector2(.5f, .5f);
            SafeContent.anchoredPosition = Vector2.zero; SafeContent.sizeDelta = extent / scale; SafeContent.localScale = Vector3.one * scale;
        }
        private void ArrangePanels(bool touch)
        {
            if (!touch && menu) { menu.anchorMin = Vector2.zero; menu.anchorMax = Vector2.one; menu.offsetMin = menu.offsetMax = Vector2.zero; }
            foreach (var entry in panels)
            {
                var rect = entry.rect; if (!rect) continue;
                if (!touch)
                {
                    rect.SetParent(entry.parent, false); rect.SetSiblingIndex(0); rect.anchorMin = entry.min; rect.anchorMax = entry.max; rect.pivot = entry.pivot;
                    rect.anchoredPosition = entry.position; rect.sizeDelta = entry.size; rect.localScale = entry.scale; rect.gameObject.SetActive(entry.active);
                    continue;
                }
                rect.SetParent(SafeContent, false); rect.localScale = Vector3.one * .7f;
                rect.gameObject.SetActive(rect.name != "Controls");
                if (rect.name == "Vitals") PlacePanel(rect, new Vector2(0, 1), new Vector2(12, -12));
                if (rect.name == "Run Skills") PlacePanel(rect, new Vector2(0, 1), new Vector2(12, -135));
                if (rect.name == "Mission") PlacePanel(rect, Vector2.one, new Vector2(-12, -12));
                if (rect.name == "Weapon") PlacePanel(rect, Vector2.one, new Vector2(-12, -122));
                if (rect.name == "Run Items") PlacePanel(rect, new Vector2(.5f, 1), new Vector2(0, -12));
                if (rect.name == "Route")
                { PlacePanel(rect, new Vector2(.5f, 1), new Vector2(0, -212)); rect.localScale = Vector3.one * .55f; }
                if (rect.name == "Checkpoint Toast")
                { PlacePanel(rect, new Vector2(.5f, 1), new Vector2(0, -264)); rect.sizeDelta = new Vector2(1080, 54); }
            }
        }
        private static void PlacePanel(RectTransform rect, Vector2 anchor, Vector2 position)
        { rect.anchorMin = rect.anchorMax = rect.pivot = anchor; rect.anchoredPosition = position; }
        private HunterTouchControl Control(string name, string caption, Vector2 position, Vector2 size, Vector2 anchor, System.Action action = null)
        {
            var rect = Rect(name, SafeContent); Place(rect, anchor, position, size);
            rect.gameObject.AddComponent<Image>();
            var control = rect.gameObject.AddComponent<HunterTouchControl>();
            control.AcceptInput = () => CanReceiveInput;
            if (action != null) control.Pressed = () => pending.Add(action);
            control.Label = TextLabel("Label", rect, caption, 20);
            var label = control.Label.rectTransform; label.anchorMin = Vector2.zero; label.anchorMax = Vector2.one; label.offsetMin = label.offsetMax = Vector2.zero;
            control.Cancel(); controls.Add(name, control); return control;
        }
        private Text TextLabel(string name, Transform parent, string value, int size)
        {
            var text = Rect(name, parent).gameObject.AddComponent<Text>();
            text.font = font; text.fontSize = size; text.text = value; text.color = new Color(.82f, .95f, .95f);
            text.alignment = TextAnchor.MiddleCenter; text.raycastTarget = false; return text;
        }
        private static RectTransform Rect(string name, Transform parent)
        { var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent, false); return rect; }
        private static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
        { rect.anchorMin = rect.anchorMax = anchor; rect.pivot = new Vector2(.5f, .5f); rect.anchoredPosition = position; rect.sizeDelta = size; }
    }
}
