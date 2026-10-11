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
    [DefaultExecutionOrder(1100), AddComponentMenu("BigWorld/Game/Game HUD 2D")]
    [RequireComponent(typeof(Canvas), typeof(GraphicRaycaster))]
    public sealed partial class GameHud2D : UIFormBase
    {
        public Font UIFont;
        [Tooltip("Enable the same touch controls used on mobile devices in the editor or desktop player.")]
        public bool ForceTouchControls;
        public HunterTouchControls2D TouchControls { get; private set; }
        public HunterRegionMapUI RegionMap { get; private set; }
        private bool TouchActive => TouchControls && TouchControls.TouchMode;
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
        private Text staminaText;
        private Text weaponText, ammoText, weaponHint;
        private Text runSkillsText;
        private Image reloadFill;
        private Image staminaFill;
        private RectTransform healthTrack;
        private RectTransform aimReticle;
        private string segmentLayout;
        private readonly List<GameObject> segmentMarkers = new List<GameObject>();
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
        private RectTransform camp;
        private Text campCredits, campHunter, campInventory, campStatus, campAccount, growthText, retirementText;
        private readonly List<Button> rosterButtons = new List<Button>();
        private Button recruitButton, reliefButton, rifleButton, pistolButton, buyRifleButton, buyPistolButton, deployButton, retireButton;
        private Button confirmRetireButton, cancelRetireButton;
        private GameObject retirementOverlay;
        private string retiringHunterId;
        private Button skillsButton, closeSkillsButton;
        private readonly List<Button> skillButtons = new List<Button>();
        private GameObject skillsOverlay;
        private Text skillsHeading, skillsStatus;
        private string skillsHunterId;
        private ScrollRect skillsScroll;
        private GameObject lastSkillSelection;
        private Button itemsButton, closeItemsButton;
        private GameObject itemsOverlay;
        private Text itemsHeading, itemsStatus, runItemsText;
        private string itemsHunterId;
        private readonly List<Button> itemBuyButtons = new List<Button>(), itemEquipButtons = new List<Button>(), itemRemoveButtons = new List<Button>();
        private readonly List<Text> itemStocks = new List<Text>();
        private bool displayedTrainingMode, abandonConfirm;

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
            if (TouchControls) TouchControls.SetTouchMode(false);
            TouchControls = null;
            RegionMap = null;
            foreach (var button in buttons) if (button) button.onClick.RemoveAllListeners();
            buttons.Clear();
            menuButtons.Clear();
            rosterButtons.Clear();
            skillButtons.Clear(); skillsHunterId = null;
            skillsScroll = null; lastSkillSelection = null;
            itemBuyButtons.Clear(); itemEquipButtons.Clear(); itemRemoveButtons.Clear(); itemStocks.Clear(); itemsHunterId = null;
            retiringHunterId = null;
            healthDraft = null; healthHunterId = null; healthEditButtons.Clear(); healthRows.Clear(); healthBars.Clear();
            healthRowLabels.Clear(); healthSplitButtons.Clear(); healthMergeButtons.Clear();
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
            segmentMarkers.Clear();
            segmentLayout = null;
        }

        protected override void OnBeforeDestroy() { OnClose(); }
        private void Update()
        {
            if (!canvasRoot) return;
            if (TouchControls)
            {
                bool touch = Application.isMobilePlatform || ForceTouchControls;
                if (TouchControls.TouchMode != touch) lastStatusMessage = null;
                TouchControls.SetTouchMode(touch);
            }
            Render();
        }
        private void LateUpdate()
        {
            RefreshAimReticle(); // After CameraFollow2D (order 1000), so the reticle does not lag camera movement.
            if (!skillsScroll || !skillsOverlay.activeInHierarchy || !EventSystem.current) return;
            var selected = EventSystem.current.currentSelectedGameObject;
            if (selected == lastSkillSelection) return;
            lastSkillSelection = selected;
            if (!selected || !selected.transform.IsChildOf(skillsScroll.content)) return;
            Canvas.ForceUpdateCanvases();
            var row = selected.transform.parent as RectTransform;
            var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(skillsScroll.viewport, row);
            var view = skillsScroll.viewport.rect;
            float delta = bounds.min.y < view.yMin ? view.yMin - bounds.min.y : bounds.max.y > view.yMax ? view.yMax - bounds.max.y : 0;
            var position = skillsScroll.content.anchoredPosition;
            position.y = Mathf.Clamp(position.y + delta, 0, Mathf.Max(0, skillsScroll.content.rect.height - view.height));
            skillsScroll.StopMovement(); skillsScroll.content.anchoredPosition = position;
        }

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
            Place(hud, new Vector2(0, 1), new Vector2(0, 1), new Vector2(26, -24), new Vector2(330, 164));
            Label("Section", hud, "边境试炼 / 行动状态", 16, Accent, new Vector2(18, -12), new Vector2(296, 26));
            healthText = Label("Health", hud, "生命值", 22, Color.white, new Vector2(18, -36), new Vector2(296, 40));
            healthTrack = Panel("Health Track", hud, new Color(.16f, .22f, .29f));
            Place(healthTrack, new Vector2(0, 1), new Vector2(0, 1), new Vector2(18, -82), new Vector2(294, 10));
            healthFill = Fill("Health Fill", healthTrack, Accent);
            staminaText = Label("Stamina", hud, "体力", 16, Muted, new Vector2(18, -103), new Vector2(296, 26));
            var staminaTrack = Panel("Stamina Track", hud, new Color(.16f, .22f, .29f));
            Place(staminaTrack, new Vector2(0, 1), new Vector2(0, 1), new Vector2(18, -138), new Vector2(294, 8));
            staminaFill = Fill("Stamina Fill", staminaTrack, new Color(.95f, .77f, .32f));

            var skillPanel = Panel("Run Skills", canvasRoot.transform, Ink);
            Place(skillPanel, new Vector2(0, 1), new Vector2(0, 1), new Vector2(26, -200), new Vector2(330, 170));
            Label("Skill Heading", skillPanel, "已学技能 · 青色技能卡可拾取", 16, Muted, new Vector2(18, -8), new Vector2(294, 26));
            runSkillsText = Label("Active Skills", skillPanel, "", 17, Accent, new Vector2(18, -38), new Vector2(294, 124));

            var mission = Panel("Mission", canvasRoot.transform, Ink);
            Place(mission, Vector2.one, Vector2.one, new Vector2(-26, -24), new Vector2(360, 146));
            objective = Label("Objective", mission, "清除守卫，抵达出口", 20, Color.white, new Vector2(18, -12), new Vector2(324, 30));
            clock = Label("Clock", mission, "00:00", 15, Muted, new Vector2(18, -48), new Vector2(324, 24));
            checkpoint = Label("Checkpoint", mission, "检查点：起点", 15, Accent, new Vector2(18, -78), new Vector2(324, 24));
            growthText = Label("Run Experience", mission, "", 15, Accent, new Vector2(18, -108), new Vector2(324, 26));

            var route = Panel("Route", canvasRoot.transform, Ink);
            Place(route, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 79), new Vector2(560, 94));
            progressText = Label("Progress", route, "关卡进度", 14, Muted, new Vector2(14, -6), new Vector2(532, 22));
            var track = Panel("Progress Track", route, new Color(.16f, .22f, .29f));
            Place(track, new Vector2(0, 1), new Vector2(0, 1), new Vector2(14, -87), new Vector2(532, 3));
            progressFill = Fill("Progress Fill", track, Accent);
            RegionMap = route.gameObject.AddComponent<HunterRegionMapUI>();
            RegionMap.Initialize(session, UIFont);

            var help = Panel("Controls", canvasRoot.transform, Ink);
            Place(help, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 12), new Vector2(1040, 56));
            var keys = Label("Keys", help, "A/D 行走 · W/S 瞄准 · Shift 奔跑 · C 匍匐 · 空格 跳跃 · J 射击 · K 近战\n1/2 切枪 · R 换弹 · E 交互 · Q/F 道具 · G 包扎 · V 调查 · Esc 暂停", 14, Muted,
                new Vector2(12, -4), new Vector2(1016, 48));
            keys.alignment = TextAnchor.MiddleCenter;
            aimReticle = Panel("Aim Reticle", canvasRoot.transform, Color.clear);
            Place(aimReticle, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(14, 14));
            Place(Panel("Horizontal", aimReticle, Accent), new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(14, 2));
            Place(Panel("Vertical", aimReticle, Accent), new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(2, 14));
            aimReticle.gameObject.SetActive(false);
            var weaponPanel = Panel("Weapon", canvasRoot.transform, Ink);
            Place(weaponPanel, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-26, 140), new Vector2(330, 122));
            weaponText = Label("Weapon Name", weaponPanel, "武器", 18, Color.white, new Vector2(18, -8), new Vector2(296, 28));
            ammoText = Label("Ammo", weaponPanel, "弹药", 18, Accent, new Vector2(18, -37), new Vector2(296, 30));
            ammoText.verticalOverflow = VerticalWrapMode.Overflow;
            weaponHint = Label("Weapon Hint", weaponPanel, "", 15, Muted, new Vector2(18, -70), new Vector2(296, 24));
            var reloadTrack = Panel("Reload Track", weaponPanel, new Color(.16f, .22f, .29f));
            Place(reloadTrack, new Vector2(0, 1), new Vector2(0, 1), new Vector2(18, -105), new Vector2(294, 5));
            reloadFill = Fill("Reload Fill", reloadTrack, Accent);
            var itemPanel = Panel("Run Items", canvasRoot.transform, Ink);
            Place(itemPanel, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-26, 274), new Vector2(330, 132));
            runItemsText = Label("Equipped Items", itemPanel, "", 17, Accent, new Vector2(18, -8), new Vector2(294, 116));
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
            var foot = Label("Footer", card, "调查狩猎 · 刷怪练级 · 边缘撤离 · 营地整备", 14, Muted,
                new Vector2(44, -484), new Vector2(562, 24));
            foot.alignment = TextAnchor.MiddleCenter;
            BuildCamp(shade);
            var touchRoot = new GameObject("Touch Controls", typeof(RectTransform));
            touchRoot.transform.SetParent(canvasRoot.transform, false); Stretch(touchRoot.GetComponent<RectTransform>());
            touchRoot.transform.SetSiblingIndex(overlay.transform.GetSiblingIndex());
            TouchControls = touchRoot.AddComponent<HunterTouchControls2D>();
            TouchControls.Initialize(session, UIFont, canvasRoot.transform);
            TouchControls.SetTouchMode(Application.isMobilePlatform || ForceTouchControls);
        }

        private void BuildCamp(Transform parent)
        {
            camp = Panel("Hunter Camp", parent, new Color(.055f, .095f, .16f, 1));
            Place(camp, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(1000, 600));
            Label("Camp Title", camp, "猎人营地", 38, Color.white, new Vector2(36, -28), new Vector2(400, 64));
            campCredits = Label("Credits", camp, "", 24, Accent, new Vector2(620, -28), new Vector2(344, 44));
            campAccount = Label("Account Growth", camp, "", 17, Muted, new Vector2(348, -74), new Vector2(616, 32));
            Label("Roster", camp, "猎人名单 / 选择出战角色", 18, Muted, new Vector2(36, -110), new Vector2(270, 34));
            for (int i = 0; i < HunterCareer.RosterLimit; i++)
            {
                int index = i;
                var button = CampButton("Hunter " + i, new Vector2(36, -160 - i * 52), new Vector2(270, 44), () => {
                    if (session && session.Career != null && index < session.Career.HunterCount) session.SelectHunter(session.Career.GetHunter(index).Id);
                });
                rosterButtons.Add(button);
            }
            recruitButton = CampButton("Recruit", new Vector2(36, -426), new Vector2(270, 38), () => session.RecruitHunter());
            reliefButton = CampButton("Relief", new Vector2(36, -472), new Vector2(270, 38), () => session.ClaimRelief());
            retireButton = CampButton("Retire", new Vector2(36, -518), new Vector2(270, 38), ShowRetirement);
            campHunter = Label("Selected Hunter", camp, "", 20, Color.white, new Vector2(348, -110), new Vector2(616, 110));
            campInventory = Label("Warehouse", camp, "", 20, Muted, new Vector2(348, -224), new Vector2(616, 40));
            rifleButton = CampButton("Equip Rifle", new Vector2(348, -284), new Vector2(296, 46), () => session.ToggleEquipment(HunterEquipment.Rifle));
            pistolButton = CampButton("Equip Pistol", new Vector2(660, -284), new Vector2(304, 46), () => session.ToggleEquipment(HunterEquipment.Pistol));
            buyRifleButton = CampButton("Buy Rifle", new Vector2(348, -344), new Vector2(296, 46), () => session.BuyEquipment(HunterEquipment.Rifle));
            buyPistolButton = CampButton("Buy Pistol", new Vector2(660, -344), new Vector2(304, 46), () => session.BuyEquipment(HunterEquipment.Pistol));
            skillsButton = CampButton("Skills", new Vector2(348, -410), new Vector2(192, 52), ShowSkills);
            itemsButton = CampButton("Items", new Vector2(552, -410), new Vector2(192, 52), ShowItems);
            deployButton = CampButton("Deploy", new Vector2(756, -410), new Vector2(208, 52), () => session.StartRun());
            healthLayoutButton = CampButton("Health Layout", new Vector2(756, -488), new Vector2(208, 46), ShowHealthLayout);
            Label("Deployment Rules", camp,
                "死亡或放弃会失去猎人及携带装备。\n无赏金也可到边缘出口等待10秒撤离。\n撤离保留成长与装备；出战补满弹药。", 16, Muted, new Vector2(348, -470), new Vector2(392, 84));
            campStatus = Label("Camp Status", camp, "", 16, Accent, new Vector2(36, -564), new Vector2(928, 28));
            BuildSkills();
            BuildItems();
            BuildHealthLayout();
            BuildRetirementConfirmation();
            camp.gameObject.SetActive(false);
        }

        private void BuildItems()
        {
            var panel = Panel("Hunter Items", camp, new Color(.035f, .065f, .12f, 1));
            Stretch(panel); panel.GetComponent<Image>().raycastTarget = true; itemsOverlay = panel.gameObject;
            Label("Title", panel, "道具整备", 30, Color.white, new Vector2(36, -10), new Vector2(440, 52));
            itemsHeading = Label("Item Capacity", panel, "", 18, Accent, new Vector2(36, -60), new Vector2(928, 30));
            for (int i = 0; i < HunterItems.Catalogue.Count; i++)
            {
                var item = HunterItems.Catalogue[i];
                var row = Panel("Item " + item.Id, panel, new Color(.065f, .105f, .17f, 1));
                Place(row, new Vector2(0, 1), new Vector2(0, 1), new Vector2(36, -100 - i * 64), new Vector2(928, 58));
                itemStocks.Add(Label("Stock", row, "", 18, Color.white, new Vector2(12, -1), new Vector2(470, 30)));
                Label("Description", row, item.Description, 15, Muted, new Vector2(12, -30), new Vector2(470, 26));
                var buy = MakeButton(row, "Buy Item " + item.Id, new Vector2(490, -7));
                buy.GetComponent<RectTransform>().sizeDelta = new Vector2(182, 44);
                buy.GetComponentInChildren<Text>().fontSize = 18;
                buy.onClick.AddListener(() => { session.BuyItem(item.Id); RenderCamp(); }); itemBuyButtons.Add(buy);
                var equip = MakeButton(row, "Equip Item " + item.Id, new Vector2(684, -7));
                equip.GetComponent<RectTransform>().sizeDelta = new Vector2(232, 44);
                equip.GetComponentInChildren<Text>().fontSize = 18;
                equip.onClick.AddListener(() => { session.EquipItem(itemsHunterId, item.Id); RenderCamp(); }); itemEquipButtons.Add(equip);
            }
            for (int i = 0; i < HunterItems.ToolSlots + HunterItems.ConsumableSlots; i++)
            {
                int index = i;
                var remove = MakeButton(panel, "Unequip Item " + i, new Vector2(36 + i * 235, -434));
                remove.GetComponent<RectTransform>().sizeDelta = new Vector2(223, 44);
                remove.GetComponentInChildren<Text>().fontSize = 17;
                remove.onClick.AddListener(() => {
                    var hunter = session.Career.Selected;
                    if (hunter != null && hunter.Id == itemsHunterId && index < hunter.Items.Count)
                        session.UnequipItem(itemsHunterId, index, hunter.Items[index].Id);
                    RenderCamp();
                });
                itemRemoveButtons.Add(remove);
            }
            Label("Item Rules", panel, "Q 切换道具，F 使用；医疗与投掷准备时移速减半。\n常规工具耗尽保留，消耗品用完移除。\n剩余次数随撤离保留；死亡遗失，退役归还仓库。", 16, Muted,
                new Vector2(36, -488), new Vector2(630, 70));
            closeItemsButton = MakeButton(panel, "Close Items", new Vector2(686, -510));
            closeItemsButton.GetComponent<RectTransform>().sizeDelta = new Vector2(278, 46);
            SetButton(closeItemsButton, "返回整备", true);
            closeItemsButton.onClick.AddListener(() => {
                itemsHunterId = null; itemsOverlay.SetActive(false); RenderCamp();
                if (EventSystem.current) EventSystem.current.SetSelectedGameObject(itemsButton.gameObject);
            });
            itemsStatus = Label("Item Status", panel, "", 16, Accent, new Vector2(36, -564), new Vector2(928, 28));
            itemsOverlay.SetActive(false);
        }

        private void ShowItems()
        {
            if (!session || session.Phase != LevelPhase.Ready || session.Career == null || session.Career.Selected == null ||
                retirementOverlay.activeSelf || skillsOverlay.activeSelf || healthOverlay.activeSelf) return;
            itemsHunterId = session.Career.Selected.Id; itemsOverlay.SetActive(true); RenderCamp();
            if (EventSystem.current) EventSystem.current.SetSelectedGameObject(closeItemsButton.gameObject);
        }

        private void RenderItems(HunterCareer profile, HunterRecord hunter)
        {
            int toolCount = hunter.Items.FindAll(s => !HunterItems.Find(s.Id).Consumable).Count;
            itemsHeading.text = hunter.Name + "   常规工具 " + toolCount + "/2   消耗品 " + (hunter.Items.Count - toolCount) + "/2   资金 " + profile.Credits;
            var stock = profile.WarehouseItems;
            for (int i = 0; i < HunterItems.Catalogue.Count; i++)
            {
                var item = HunterItems.Catalogue[i];
                itemStocks[i].text = item.Name + "    仓库 " + stock.FindAll(s => s.Id == item.Id).Count + " 件";
                SetButton(itemBuyButtons[i], "购买" + item.Name + " · " + item.Cost, profile.CanBuyItem(item.Id));
                SetButton(itemEquipButtons[i], "装备" + item.Name, profile.CanEquipItem(hunter.Id, item.Id));
            }
            for (int i = 0; i < itemRemoveButtons.Count; i++)
            {
                var stack = i < hunter.Items.Count ? hunter.Items[i] : null;
                var item = stack == null ? null : HunterItems.Find(stack.Id);
                SetButton(itemRemoveButtons[i], item == null ? "空栏位" : "卸下" + item.Name + (item.Kind == HunterItemKind.Melee ? "" : " · " + stack.Uses + "次"),
                    item != null && stock.Count < HunterItems.WarehouseLimit);
            }
            itemsStatus.text = session.CampMessage;
            closeItemsButton.interactable = true;
            foreach (var button in buttons)
                if (button != closeItemsButton && !itemBuyButtons.Contains(button) && !itemEquipButtons.Contains(button) && !itemRemoveButtons.Contains(button))
                    button.interactable = false;
        }

        private void BuildSkills()
        {
            var panel = Panel("Hunter Skills", camp, new Color(.035f, .065f, .12f, 1));
            Stretch(panel); panel.GetComponent<Image>().raycastTarget = true; skillsOverlay = panel.gameObject;
            Label("Title", panel, "猎人技能", 34, Color.white, new Vector2(36, -24), new Vector2(560, 52));
            skillsHeading = Label("Hunter Points", panel, "", 21, Accent, new Vector2(36, -84), new Vector2(928, 36));
            Label("Rules", panel, "携带容量按已学技能价格合计 · 消耗或移除后释放 · 升级点数另计 · 滚动查看", 17, Muted,
                new Vector2(36, -122), new Vector2(928, 32));
            var scrollRoot = Panel("Skill List", panel, Color.clear);
            Place(scrollRoot, new Vector2(0, 1), new Vector2(0, 1), new Vector2(36, -160), new Vector2(928, 330));
            skillsScroll = scrollRoot.gameObject.AddComponent<ScrollRect>();
            skillsScroll.horizontal = false; skillsScroll.movementType = ScrollRect.MovementType.Clamped;
            skillsScroll.inertia = false; skillsScroll.scrollSensitivity = 32;
            var viewport = Panel("Viewport", scrollRoot, Color.clear); Stretch(viewport); viewport.offsetMax = new Vector2(-20, 0);
            viewport.GetComponent<Image>().raycastTarget = true; viewport.gameObject.AddComponent<RectMask2D>();
            var content = Panel("Skill Rows", viewport, Color.clear);
            Place(content, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, new Vector2(908, HunterSkills.Catalogue.Count * 80 - 8));
            skillsScroll.viewport = viewport; skillsScroll.content = content;
            var track = Panel("Scrollbar", scrollRoot, new Color(.12f, .19f, .26f));
            Place(track, new Vector2(1, 1), new Vector2(1, 1), Vector2.zero, new Vector2(10, 330));
            track.GetComponent<Image>().raycastTarget = true;
            var handle = Panel("Handle", track, Muted); Stretch(handle); handle.GetComponent<Image>().raycastTarget = true;
            var scrollbar = track.gameObject.AddComponent<Scrollbar>(); scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.handleRect = handle; scrollbar.targetGraphic = handle.GetComponent<Image>(); skillsScroll.verticalScrollbar = scrollbar;
            for (int i = 0; i < HunterSkills.Catalogue.Count; i++)
            {
                var skill = HunterSkills.Catalogue[i];
                var row = Panel("Skill " + skill.Id, content, new Color(.065f, .105f, .17f, 1));
                Place(row, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -i * 80), new Vector2(908, 72));
                string category = skill.Category == HunterSkillCategory.Item ? "道具" : skill.Category == HunterSkillCategory.Gun ? "枪械" : "角色";
                string kind = skill.EffectKind == HunterSkillEffectKind.Mechanic ? "机制" : "数值";
                Label("Name", row, skill.Name + "  /  " + skill.Cost + " 技能点 · " + category + " / " + kind, 18, Color.white,
                    new Vector2(20, -2), new Vector2(568, 32));
                Label("Effect", row, skill.Description, 16, Muted, new Vector2(20, -36), new Vector2(568, 28));
                var button = MakeButton(row, "Skill Action " + skill.Id, new Vector2(608, -14));
                button.GetComponent<RectTransform>().sizeDelta = new Vector2(280, 44);
                button.GetComponentInChildren<Text>().fontSize = 19;
                button.onClick.AddListener(() => {
                    if (!session || session.Career == null || session.Career.Selected == null || session.Career.Selected.Id != skillsHunterId) return;
                    if (session.Career.Selected.HasSkill(skill.Id)) session.RemoveSkill(skillsHunterId, skill.Id);
                    else session.BuySkill(skillsHunterId, skill.Id);
                    RenderCamp();
                    if (EventSystem.current && !button.interactable) EventSystem.current.SetSelectedGameObject(closeSkillsButton.gameObject);
                });
                skillButtons.Add(button);
            }
            Label("Refund Rules", panel, "移除返还：1点技能全返；其他返还一半并向下取整。\n已学技能随猎人永久死亡或退役消失，不会转交其他猎人。", 17, Muted,
                new Vector2(36, -498), new Vector2(624, 58));
            closeSkillsButton = MakeButton(panel, "Close Skills", new Vector2(686, -510));
            closeSkillsButton.GetComponent<RectTransform>().sizeDelta = new Vector2(278, 46);
            SetButton(closeSkillsButton, "返回整备", true);
            closeSkillsButton.onClick.AddListener(() => {
                skillsHunterId = null; skillsOverlay.SetActive(false); RenderCamp();
                if (EventSystem.current) EventSystem.current.SetSelectedGameObject(skillsButton.gameObject);
            });
            skillsStatus = Label("Skill Status", panel, "", 16, Accent, new Vector2(36, -564), new Vector2(928, 28));
            skillsOverlay.SetActive(false);
        }

        private void ShowSkills()
        {
            if (!session || session.Phase != LevelPhase.Ready || session.Career == null || session.Career.Selected == null || retirementOverlay.activeSelf || itemsOverlay.activeSelf || healthOverlay.activeSelf) return;
            skillsHunterId = session.Career.Selected.Id; skillsOverlay.SetActive(true); RenderCamp();
            skillsScroll.StopMovement(); skillsScroll.verticalNormalizedPosition = 1; lastSkillSelection = null;
            if (EventSystem.current) EventSystem.current.SetSelectedGameObject(closeSkillsButton.gameObject);
        }

        private void RenderSkills(HunterCareer profile, HunterRecord hunter)
        {
            skillsHeading.text = hunter.Name + " · " + hunter.Template.Name + "   可用点数 " + hunter.SkillPoints +
                "   携带技能 " + hunter.CarriedSkillValue + "/" + hunter.Template.CarriedSkillValueLimit;
            skillsStatus.text = session.CampMessage;
            for (int i = 0; i < skillButtons.Count; i++)
            {
                var skill = HunterSkills.Catalogue[i]; bool owned = hunter.HasSkill(skill.Id);
                SetButton(skillButtons[i], owned ? "移除" + skill.Name + " · 返还" + skill.Refund + "点" :
                    "购买" + skill.Name + " · " + skill.Cost + "点" +
                        (HunterTemplates.CanAddSkill(hunter.TemplateId, hunter.CarriedSkillValue, skill.Cost) ? "" : " · 容量不足"),
                    owned ? profile.CanRemoveSkill(hunter.Id, skill.Id) : profile.CanBuySkill(hunter.Id, skill.Id));
            }
            closeSkillsButton.interactable = true;
            foreach (var button in buttons)
                if (button != closeSkillsButton && !skillButtons.Contains(button)) button.interactable = false;
        }

        private void BuildRetirementConfirmation()
        {
            var shade = Panel("Retirement Confirmation", camp, new Color(.018f, .03f, .06f, .96f));
            Stretch(shade); shade.GetComponent<Image>().raycastTarget = true;
            retirementOverlay = shade.gameObject;
            var panel = Panel("Retirement Card", shade, Ink);
            Place(panel, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(700, 320));
            Label("Title", panel, "确认猎人退役", 30, Color.white, new Vector2(28, -24), new Vector2(644, 52));
            retirementText = Label("Consequences", panel, "", 19, Muted, new Vector2(28, -88), new Vector2(644, 116));
            confirmRetireButton = MakeButton(panel, "Confirm Retirement", new Vector2(28, -240));
            confirmRetireButton.GetComponent<RectTransform>().sizeDelta = new Vector2(304, 48);
            SetButton(confirmRetireButton, "确认退役", true);
            confirmRetireButton.onClick.AddListener(() => {
                if (session && session.RetireHunter(retiringHunterId)) CloseRetirement();
                else if (session) retirementText.text = session.CampMessage + "\n猎人尚未退役，可修复后重试或取消。";
            });
            cancelRetireButton = MakeButton(panel, "Cancel Retirement", new Vector2(348, -240));
            cancelRetireButton.GetComponent<RectTransform>().sizeDelta = new Vector2(324, 48);
            SetButton(cancelRetireButton, "取消，保留猎人", true);
            cancelRetireButton.onClick.AddListener(CloseRetirement);
            retirementOverlay.SetActive(false);
        }

        private void ShowRetirement()
        {
            if (!session || session.Phase != LevelPhase.Ready || session.Career == null || !session.Career.CanRetireSelected || skillsOverlay.activeSelf || itemsOverlay.activeSelf || healthOverlay.activeSelf) return;
            var hunter = session.Career.Selected;
            retiringHunterId = hunter.Id;
            retirementText.text = "永久移除「" + hunter.Name + "」，已学技能与 " + hunter.SkillPoints + " 技能点不继承。\n账号经验 +" +
                HunterProgression.RetirementExperience + "；账号 Lv." + session.Career.AccountLevel + " → Lv." +
                HunterAccountProgression.Level(session.Career.AccountExperience + HunterProgression.RetirementExperience) +
                "。\n武器和剩余道具返仓；退役不可撤销。";
            confirmRetireButton.interactable = cancelRetireButton.interactable = true;
            retirementOverlay.SetActive(true); RenderCamp();
            if (EventSystem.current) EventSystem.current.SetSelectedGameObject(cancelRetireButton.gameObject);
        }

        private void CloseRetirement()
        {
            retiringHunterId = null; retirementOverlay.SetActive(false); RenderCamp();
            if (EventSystem.current) EventSystem.current.SetSelectedGameObject(deployButton.interactable ? deployButton.gameObject : recruitButton.gameObject);
        }

        private Button CampButton(string name, Vector2 position, Vector2 size, UnityAction action)
        {
            var button = MakeButton(camp, name, position);
            button.GetComponent<RectTransform>().sizeDelta = size;
            button.GetComponentInChildren<Text>().fontSize = 19;
            button.onClick.AddListener(() => { if (session) action(); });
            return button;
        }

        private static void SetButton(Button button, string text, bool enabled)
        { button.GetComponentInChildren<Text>().text = text; button.interactable = enabled; }

        private void RenderCamp()
        {
            var profile = session.Career;
            if (profile == null) return;
            var selected = profile.Selected;
            if (retirementOverlay.activeSelf && (selected == null || selected.Id != retiringHunterId || !profile.CanRetireSelected))
            { retiringHunterId = null; retirementOverlay.SetActive(false); }
            if (skillsOverlay.activeSelf && (selected == null || selected.Id != skillsHunterId))
            { skillsHunterId = null; skillsOverlay.SetActive(false); }
            if (itemsOverlay.activeSelf && (selected == null || selected.Id != itemsHunterId))
            { itemsHunterId = null; itemsOverlay.SetActive(false); }
            if (healthOverlay.activeSelf && (selected == null || selected.Id != healthHunterId))
            { healthHunterId = null; healthDraft = null; healthOverlay.SetActive(false); }
            campCredits.text = "资金  " + profile.Credits + "    /    " + profile.HunterCount + "/5 人";
            campAccount.text = "账号 Lv." + profile.AccountLevel + (profile.IsAccountMaxLevel ? "（满级）" :
                " / " + HunterAccountProgression.MaximumLevel + " · " + profile.AccountLevelExperience + "/" + HunterAccountProgression.ExperiencePerLevel) +
                "   累计 " + profile.AccountExperience + "   退役 " + profile.Retirements + " 人";
            for (int i = 0; i < rosterButtons.Count; i++)
            {
                var button = rosterButtons[i];
                button.gameObject.SetActive(i < profile.HunterCount);
                if (i >= profile.HunterCount) continue;
                var hunter = profile.GetHunter(i);
                SetButton(button, (selected != null && selected.Id == hunter.Id ? "出战 · " : "选择 · ") + hunter.Name + "  Lv." + hunter.Level,
                    selected == null || selected.Id != hunter.Id);
            }
            campHunter.text = selected == null ? "暂无猎人\n招募或领取救济后继续行动。" : selected.Name + "    Lv." + selected.Level + "/" + HunterProgression.MaximumLevel + "\n" +
                (selected.IsMaxLevel ? "经验已满，可退役" : "升级进度 " + selected.LevelExperience + "/" + HunterProgression.ExperiencePerLevel) +
                "    技能点 " + selected.SkillPoints + " · 携带技能 " + selected.CarriedSkillValue + "/" + selected.Template.CarriedSkillValueLimit +
                "\n" + selected.Template.Name + " · 负重 " + selected.Weight + "/" + selected.Template.WeaponWeightLimit + "  ·  " +
                (selected.Rifle ? "步枪  " : "") + (selected.Pistol ? "手枪" : "") + (selected.Weight == 0 ? "尚未装备武器" : "");
            campInventory.text = "仓库未装备：步枪 × " + profile.Rifles + "    手枪 × " + profile.Pistols;
            SetButton(recruitButton, "招募猎人  ·  100", profile.CanRecruit);
            SetButton(reliefButton, "全员阵亡救济", profile.CanClaimRelief);
            SetButton(retireButton, selected != null && selected.IsMaxLevel ? "满级退役  ·  账号经验 +500" : "满级退役  ·  需10级", profile.CanRetireSelected);
            retireButton.GetComponentInChildren<Text>().fontSize = 17;
            SetButton(rifleButton, selected != null && selected.Rifle ? "卸下步枪 → 仓库" : "装备步枪  ·  负重 2", profile.CanToggle(HunterEquipment.Rifle));
            SetButton(pistolButton, selected != null && selected.Pistol ? "卸下手枪 → 仓库" : "装备手枪  ·  负重 1", profile.CanToggle(HunterEquipment.Pistol));
            SetButton(buyRifleButton, "购买步枪  ·  150", profile.CanBuy(HunterEquipment.Rifle));
            SetButton(buyPistolButton, "购买手枪  ·  75", profile.CanBuy(HunterEquipment.Pistol));
            SetButton(deployButton, "开始行动", profile.CanDeploy);
            SetButton(skillsButton, "猎人技能  ·  " + (selected == null ? 0 : selected.SkillPoints) + " 点", selected != null && !profile.InRun);
            SetButton(itemsButton, "道具整备 · " + (selected == null ? 0 : selected.Items.Count) + "/4", selected != null && !profile.InRun);
            SetButton(healthLayoutButton, "血段设置", selected != null && !profile.InRun);
            campStatus.text = session.CampMessage;
            if (skillsOverlay.activeSelf) RenderSkills(profile, selected);
            if (itemsOverlay.activeSelf) RenderItems(profile, selected);
            if (healthOverlay.activeSelf) RenderHealthLayout(selected);
            if (retirementOverlay.activeSelf)
                foreach (var button in buttons)
                    if (button != confirmRetireButton && button != cancelRetireButton) button.interactable = false;
        }

        internal void RefreshAimReticle()
        {
            if (!aimReticle) return;
            bool visible = session && session.IsGameplayActive && session.Player &&
                session.Player.GetComponent<SkillActor2D>().IsAlive && !session.Player.IsOnLadder && (!session.Hunt || !session.Hunt.IsSpecialView);
            aimReticle.gameObject.SetActive(visible);
            if (!visible) return;
            var camera = session.FollowCamera ? session.FollowCamera.GetComponent<Camera>() : Camera.main;
            if (!camera) { aimReticle.gameObject.SetActive(false); return; }
            var screen = camera.WorldToScreenPoint(session.Player.AimOrigin + session.Player.AimDirection * 2);
            var rootCanvas = CurrCanvas.rootCanvas;
            var uiCamera = rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : rootCanvas.worldCamera;
            if (screen.z <= 0 || !RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRoot.GetComponent<RectTransform>(), screen, uiCamera, out var point))
            { aimReticle.gameObject.SetActive(false); return; }
            aimReticle.anchoredPosition = point;
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
            var hunter = session.Player;
            SetFill(staminaFill, hunter ? Mathf.Clamp01(hunter.Stamina / Mathf.Max(1, hunter.MaxStamina)) : 0);
            staminaText.text = hunter ? "体力  " + hunter.Stamina.ToString("0") + " / " + hunter.MaxStamina.ToString("0") +
                (hunter.IsProne ? " · 匍匐" : "") : "体力  —";
            if (hunter && actor) RenderSegments(hunter, actor.MaxHealth);
            RenderWeapon(hunter);
            var equipped = hunter ? hunter.Items : null;
            runItemsText.text = equipped == null || equipped.Count == 0 ? "道具栏为空\n营地可购买并装备" :
                (TouchActive ? "切道具 · 使用\n" : "Q 切换 · F 使用\n") + (hunter.ItemIndex + 1) + "/" + equipped.Count + "  " + HunterItems.Find(equipped[hunter.ItemIndex].Id).Name + "  " +
                (HunterItems.Find(equipped[hunter.ItemIndex].Id).Kind == HunterItemKind.Melee ? "不限次" : equipped[hunter.ItemIndex].Uses + "次") + "\n" +
                (hunter.IsUsingItem ? "准备中 " + Mathf.RoundToInt(hunter.ItemUseProgress * 100) + "% · 移速减半" : hunter.ItemStatus ?? "准备时减速，受伤会打断");
            if (equipped != null && equipped.Count > 0 && HunterItems.Find(equipped[hunter.ItemIndex].Id).Kind == HunterItemKind.Throwable)
                runItemsText.text += "\n最远距离 " + (HunterItems.Find(equipped[hunter.ItemIndex].Id).Range * hunter.ThrowRangeMultiplier).ToString("0.#") +
                    (hunter.IsUsingItem ? " · 方向已锁定" : TouchActive ? " · 使用时锁定" : " · F 锁定方向");
            var activeHunter = session.TrainingMode || session.Career == null ? null : session.Career.Selected;
            var skillLines = new List<string>();
            if (activeHunter != null) for (int i = 0; i < activeHunter.Skills.Count; i += 2)
                skillLines.Add(HunterSkills.Find(activeHunter.Skills[i]).Name + (i + 1 < activeHunter.Skills.Count ?
                    " · " + HunterSkills.Find(activeHunter.Skills[i + 1]).Name : ""));
            runSkillsText.text = skillLines.Count == 0 ? "暂无 · 探索地图寻找技能卡" : string.Join("\n", skillLines);
            if (activeHunter != null && activeHunter.HasSkill(HunterSkillId.EmergencyBandage))
                runSkillsText.text += TouchActive ? "\n包扎技 +50生命 · 用后移除" : "\nG 包扎 +50生命 · 用后移除";
            if (session.IsPlayerUndetected) runSkillsText.text += "\n不被警戒 · 剩余 " + session.RevivalGraceRemaining.ToString("0.0") + " 秒";
            objective.fontSize = session.Hunt ? 16 : 20;
            objective.text = session.Hunt ? session.Hunt.Objective : session.DefeatedEnemies >= session.RequiredKills ? "守卫已清除 · 前往出口" :
                "清除守卫  " + session.DefeatedEnemies + " / " + session.RequiredKills;
            objective.text = InputHint(objective.text);
            clock.text = "用时 " + FormatTime(session.ElapsedSeconds) + "    失误 " + session.DeathCount;
            checkpoint.text = (session.TrainingMode ? "检查点：" : "地标：") + (string.IsNullOrEmpty(session.CheckpointName) ? "起点" : session.CheckpointName);
            growthText.text = session.TrainingMode || session.Career == null ? "" :
                "预计经验 " + session.RunExperience + " · 成功撤离后入账";
            SetFill(progressFill, Mathf.Clamp01(session.Progress01));
            progressText.text = session.Hunt ? session.Hunt.SearchRegions : "边境通路  /  " + Mathf.RoundToInt(Mathf.Clamp01(session.Progress01) * 100) + "%";
            if (RegionMap) RegionMap.Refresh();
            if (session.Phase == LevelPhase.Playing && lastStatusMessage != session.StatusMessage)
            {
                lastStatusMessage = session.StatusMessage;
                if (!string.IsNullOrEmpty(lastStatusMessage))
                {
                    toast.text = InputHint(lastStatusMessage);
                    toastUntil = Time.unscaledTime + 3;
                }
            }
            if (lastCheckpoint != session.CheckpointName)
            {
                if (!string.IsNullOrEmpty(lastCheckpoint) && session.Phase == LevelPhase.Playing)
                {
                    toast.text = (session.TrainingMode ? "检查点已激活  /  " : "到达地标  /  ") + session.CheckpointName;
                    toastUntil = Time.unscaledTime + 3;
                }
                lastCheckpoint = session.CheckpointName;
            }
            toast.gameObject.SetActive(session.Phase == LevelPhase.Playing && Time.unscaledTime < toastUntil);
            ShowPhase(session.Phase);
            if (session.Phase == LevelPhase.Ready && !session.TrainingMode) RenderCamp();
            if (session.Phase == LevelPhase.Paused && session.CampMessage.StartsWith("保存失败")) subtitle.text = session.CampMessage;
            if (session.Phase == LevelPhase.Error && !string.IsNullOrEmpty(session.StatusMessage)) subtitle.text = session.StatusMessage;
        }

        private void ShowPhase(LevelPhase phase)
        {
            bool training = !session || session.TrainingMode;
            if (displayedPhase == phase && displayedTrainingMode == training) return;
            displayedPhase = phase;
            displayedTrainingMode = training;
            if (phase != LevelPhase.Paused) abandonConfirm = false;
            if (phase != LevelPhase.Ready)
            {
                retiringHunterId = skillsHunterId = itemsHunterId = null;
                retirementOverlay.SetActive(false); skillsOverlay.SetActive(false); itemsOverlay.SetActive(false);
                healthOverlay.SetActive(false); healthHunterId = null; healthDraft = null;
            }
            overlay.SetActive(phase != LevelPhase.Playing);
            bool showCamp = phase == LevelPhase.Ready && !training;
            camp.gameObject.SetActive(showCamp);
            card.gameObject.SetActive(!showCamp);
            foreach (var button in menuButtons) { button.onClick.RemoveAllListeners(); button.gameObject.SetActive(false); }
            badge.text = "BORDER TRIAL  /  01";
            switch (phase)
            {
                case LevelPhase.Ready:
                    if (!training)
                    {
                        headline.text = "猎人营地"; RenderCamp();
                        if (EventSystem.current) EventSystem.current.SetSelectedGameObject(session.Career != null && session.Career.CanDeploy ? deployButton.gameObject : reliefButton.gameObject);
                        break;
                    }
                    headline.text = "边境试炼";
                    subtitle.text = InputHint("按住 V 寻找线索，松开后按 E 调查。\n猎杀 Boss 或刷怪练级，均可到边缘出口撤离。");
                    Action(0, "开始游戏    ↵", () => session.StartRun()); break;
                case LevelPhase.Paused:
                    headline.text = "稍作休整";
                    subtitle.text = "旅途已暂停。\n准备好后，继续向边境前进。";
                    Action(0, "继续游戏    Esc", () => session.TogglePause());
                    if (!training)
                    {
                        subtitle.text = abandonConfirm ? "确认放弃本次行动？\n出战猎人与携带装备将永久遗失。" : "行动已暂停。\n放弃行动会永久失去出战猎人与装备。";
                        Action(1, abandonConfirm ? "确认放弃，返回营地" : "放弃行动…", () => {
                            if (abandonConfirm) session.ReturnToTitle();
                            else { abandonConfirm = true; displayedPhase = null; ShowPhase(LevelPhase.Paused); }
                        });
                        break;
                    }
                    Action(1, "重新开始", () => session.RestartRun());
                    Action(2, "返回标题", () => session.ReturnToTitle()); break;
                case LevelPhase.Dead:
                    if (!training)
                    {
                        headline.text = "猎人已阵亡";
                        subtitle.text = "本次收益为 0。猎人及携带装备永久遗失。\n仓库和其他猎人已保留，返回营地重新整备。";
                        Action(0, "返回营地", () => session.ReturnToTitle()); break;
                    }
                    headline.text = "再试一次";
                    subtitle.text = "本次尝试失败，赏金收益为 0。\n从「" + session.CheckpointName + "」重试；携带的赏金掉落原地。";
                    Action(0, "检查点重试    R", () => session.RetryCheckpoint());
                    Action(1, "返回标题", () => session.ReturnToTitle()); break;
                case LevelPhase.Won:
                    badge.text = "MISSION COMPLETE";
                    headline.text = "试炼完成";
                    subtitle.text = "撤离成功，赏金收益 " + (session.Hunt ? session.Hunt.State.Reward : 0) + "。\n用时 " + FormatTime(session.ElapsedSeconds) + "    失误 " + session.DeathCount + " 次";
                    if (!training)
                    {
                        subtitle.text = "赏金 +" + session.Career.LastReward + "    经验 +" + session.Career.LastExperience +
                            "    技能点 +" + session.Career.LastLevelsGained + "\n" + session.Career.Selected.Name + "  Lv." + session.Career.Selected.Level +
                            " / " + HunterProgression.MaximumLevel + (session.Career.Selected.IsMaxLevel ? "（已满级，可退役）" : "") +
                            "\n资金 " + session.Career.Credits + "    用时 " + FormatTime(session.ElapsedSeconds);
                        Action(0, "返回营地    R", () => session.ReturnToTitle()); break;
                    }
                    Action(0, "再玩一次    R", () => session.RestartRun());
                    Action(1, "返回标题", () => session.ReturnToTitle()); break;
                case LevelPhase.Error:
                    headline.text = "关卡暂不可用";
                    subtitle.text = "关卡加载失败，请返回标题后重试。";
                    Action(0, session.HasCareerSaveError ? "重试存档" : "返回营地", () => { if (session.HasCareerSaveError) session.RetryCareerSave(); else session.ReturnToTitle(); }); break;
                case LevelPhase.Reviving:
                    headline.text = "死灵法师"; subtitle.text = "技能已消耗，正在复活……\n恢复50%生命，3秒内不触发怪物警戒。"; break;
                case LevelPhase.Respawning:
                    headline.text = training ? "返回检查点" : "返回营地"; subtitle.text = "正在准备周围地形……"; break;
                default:
                    headline.text = "边境试炼"; subtitle.text = "正在准备关卡……"; break;
            }
            if (overlay.activeSelf && menuButtons[0].gameObject.activeSelf && EventSystem.current)
                EventSystem.current.SetSelectedGameObject(menuButtons[0].gameObject);
        }

        private void RenderWeapon(PlayerController2D hunter)
        {
            var guns = hunter ? hunter.Weapons : null;
            weaponText.text = guns == null ? "武器 —" : (guns.ActiveIndex + 1) + " / " + guns.WeaponCount + "  " + guns.Name;
            ammoText.text = guns == null ? "弹药 —" : "弹匣 " + guns.Magazine + " / " + guns.MagazineCapacity + "    备弹 " + guns.Reserve;
            SetFill(reloadFill, guns == null ? 0 : guns.ReloadProgress);
            weaponHint.text = TouchActive ? "换弹 · 切枪" : "R 换弹 · 1 / 2 切换武器";
            if (hunter.IsSceneInteracting)
            { weaponHint.text = "开柜 " + Mathf.RoundToInt(hunter.SceneInteraction.Progress * 100) + "% · 操作会中断"; return; }
            if (guns == null) return;
            if (hunter.IsOnLadder) { weaponHint.text = hunter.LadderStatus ?? "上下攀爬 · 左右离梯"; return; }
            if (guns.IsReloading) { weaponHint.text = "换弹中 " + Mathf.RoundToInt(guns.ReloadProgress * 100) + "%"; return; }
            if (!string.IsNullOrEmpty(hunter.PostureStatus)) { weaponHint.text = hunter.PostureStatus; return; }
            if (session.Hunt && !string.IsNullOrEmpty(session.Hunt.Hint)) { weaponHint.text = TouchActive ? session.Hunt.Hint.Replace("E ", "交互：") : session.Hunt.Hint; return; }
            if (!hunter.CanInteract) return;
            string nearby = hunter.NearbyLootHint();
            if (!string.IsNullOrEmpty(nearby)) weaponHint.text = TouchActive ? nearby.Replace("E ", "交互：") : nearby;
            else if (guns.Magazine == 0) weaponHint.text = guns.Reserve > 0 ? (TouchActive ? "弹匣已空，点换弹" : "弹匣已空，按 R 换弹") : "弹药耗尽，切枪或使用近战";
        }

        private void RenderSegments(PlayerController2D hunter, float maximum)
        {
            string layout = maximum + ":" + (hunter.HealthSegments == null ? "" : string.Join(",", hunter.HealthSegments));
            if (layout == segmentLayout) return;
            segmentLayout = layout;
            foreach (var marker in segmentMarkers) if (marker) Destroy(marker);
            segmentMarkers.Clear();
            if (!HunterHealthSegments.IsValid(maximum, hunter.HealthSegments)) return;
            float sum = 0;
            for (int i = 0; i < hunter.HealthSegments.Length - 1; i++)
            {
                sum += hunter.HealthSegments[i];
                var marker = Panel("Health Segment " + i, healthTrack, Ink);
                marker.anchorMin = new Vector2(sum / maximum, 0);
                marker.anchorMax = new Vector2(sum / maximum, 1);
                marker.sizeDelta = new Vector2(3, 0);
                marker.anchoredPosition = Vector2.zero;
                segmentMarkers.Add(marker.gameObject);
            }
        }

        private void Action(int index, string title, UnityAction callback)
        {
            var button = menuButtons[index];
            button.gameObject.SetActive(true);
            button.interactable = true;
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
        private string InputHint(string value)
        {
            return !TouchActive || string.IsNullOrEmpty(value) ? value : value.Replace("按住 V", "按住调查").Replace("松开 V", "松开调查")
                .Replace("按 E", "点交互").Replace("E 交互", "交互").Replace("按 G", "点包扎技").Replace("按 F", "点使用");
        }
    }
}
