using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BigWorld.Gameplay
{
    public sealed partial class GameHud2D
    {
        private GameObject healthOverlay;
        private Button healthLayoutButton, saveHealthButton, cancelHealthButton;
        private Text healthHeading, healthStatus;
        private string healthHunterId;
        private HunterHealthLayout healthDraft;
        private readonly List<Button> healthEditButtons = new List<Button>(), healthSplitButtons = new List<Button>(), healthMergeButtons = new List<Button>();
        private readonly List<RectTransform> healthRows = new List<RectTransform>(), healthBars = new List<RectTransform>();
        private readonly List<Text> healthRowLabels = new List<Text>();
        public bool IsHealthLayoutOpen => healthOverlay && healthOverlay.activeInHierarchy;

        private void BuildHealthLayout()
        {
            var panel = Panel("Hunter Health Layout", camp, new Color(.035f, .065f, .12f, 1));
            Stretch(panel); panel.GetComponent<Image>().raycastTarget = true; healthOverlay = panel.gameObject;
            Label("Title", panel, "血段设置", 34, Color.white, new Vector2(36, -22), new Vector2(560, 52));
            healthHeading = Label("Health Layout Heading", panel, "", 20, Accent, new Vector2(36, -80), new Vector2(928, 34));
            Label("Rules", panel, "从 0 生命向满血排列 · 50 可拆为 25＋25，相邻两个 25 可合并 · 保存后下次出战生效", 16, Muted,
                new Vector2(36, -116), new Vector2(928, 30));
            for (int i = 0; i < (int)(HunterCareer.MaximumHealth / 25); i++)
            {
                int index = i;
                healthBars.Add(Panel("Health Preview Segment " + i, panel, Accent));
                var row = Panel("Health Edit Row " + i, panel, new Color(.065f, .105f, .17f, 1));
                Place(row, new Vector2(0, 1), new Vector2(0, 1), new Vector2(36, -210 - i * 64), new Vector2(928, 58));
                healthRows.Add(row);
                healthRowLabels.Add(Label("Threshold", row, "", 21, Color.white, new Vector2(14, -8), new Vector2(450, 42)));
                healthSplitButtons.Add(HealthButton(row, "Split Health " + i, new Vector2(470, -7), new Vector2(210, 44),
                    () => { if (CanEditHealth() && healthDraft.TrySplit(index)) { healthStatus.text = "草稿已修改，尚未保存。"; RenderCamp(); } }));
                healthMergeButtons.Add(HealthButton(row, "Merge Health " + i, new Vector2(694, -7), new Vector2(220, 44),
                    () => { if (CanEditHealth() && healthDraft.TryMerge(index)) { healthStatus.text = "草稿已修改，尚未保存。"; RenderCamp(); } }));
            }
            cancelHealthButton = HealthButton(panel, "Cancel Health Layout", new Vector2(472, -490), new Vector2(236, 48), CloseHealthLayout);
            saveHealthButton = HealthButton(panel, "Save Health Layout", new Vector2(728, -490), new Vector2(236, 48), () => {
                if (!CanEditHealth()) return;
                if (session.SetHealthLayout(healthHunterId, healthDraft.Segments)) CloseHealthLayout();
                else { healthStatus.text = session.CampMessage; RenderCamp(); }
            });
            healthStatus = Label("Health Layout Status", panel, "", 16, Accent, new Vector2(36, -548), new Vector2(928, 46));
            healthOverlay.SetActive(false);
        }
        private Button HealthButton(Transform parent, string name, Vector2 position, Vector2 size, UnityEngine.Events.UnityAction action)
        {
            var button = MakeButton(parent, name, position); button.GetComponent<RectTransform>().sizeDelta = size;
            button.GetComponentInChildren<Text>().fontSize = 19; button.onClick.AddListener(action); healthEditButtons.Add(button); return button;
        }
        private bool CanEditHealth() => session && session.Phase == LevelPhase.Ready && !session.TrainingMode && !session.HasCareerSaveError &&
            healthDraft != null && healthOverlay.activeSelf && session.Career != null && session.Career.Selected != null && session.Career.Selected.Id == healthHunterId;
        private void ShowHealthLayout()
        {
            if (!session || session.Phase != LevelPhase.Ready || session.TrainingMode || session.HasCareerSaveError || session.Career == null ||
                session.Career.Selected == null || skillsOverlay.activeSelf || itemsOverlay.activeSelf || retirementOverlay.activeSelf) return;
            var hunter = session.Career.Selected; healthHunterId = hunter.Id;
            healthDraft = new HunterHealthLayout(HunterCareer.MaximumHealth, hunter.HealthSegments);
            healthStatus.text = "调整血段不会提高最大生命，不消耗资金或技能点。取消会放弃本次草稿。";
            healthOverlay.SetActive(true); RenderCamp();
            if (EventSystem.current) EventSystem.current.SetSelectedGameObject(cancelHealthButton.gameObject);
        }
        private void CloseHealthLayout()
        {
            healthDraft = null; healthHunterId = null; healthOverlay.SetActive(false); RenderCamp();
            if (EventSystem.current) EventSystem.current.SetSelectedGameObject(healthLayoutButton.gameObject);
        }
        private void RenderHealthLayout(HunterRecord hunter)
        {
            if (healthDraft == null) return;
            bool touch = TouchActive;
            var panel = (RectTransform)healthOverlay.transform;
            float height = touch ? 860 : 600;
            Place(panel, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(1000, height));
            var available = ((RectTransform)overlay.transform).rect.size;
            panel.localScale = Vector3.one * Mathf.Min(1, (available.x - 32) / 1000, (available.y - 32) / height);
            Place(cancelHealthButton.GetComponent<RectTransform>(), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(472, touch ? -694 : -490), new Vector2(236, touch ? 84 : 48));
            Place(saveHealthButton.GetComponent<RectTransform>(), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(728, touch ? -694 : -490), new Vector2(236, touch ? 84 : 48));
            healthStatus.rectTransform.anchoredPosition = new Vector2(36, touch ? -794 : -548);
            foreach (var button in healthEditButtons) button.GetComponentInChildren<Text>().fontSize = touch ? 26 : 19;
            var values = healthDraft.Segments; float sum = 0;
            healthHeading.text = hunter.Name + "  ·  固定生命 " + HunterCareer.MaximumHealth + "  ·  " + string.Join(" + ", values);
            bool changed = values.Length != hunter.HealthSegments.Length;
            if (!changed) for (int i = 0; i < values.Length; i++) changed |= values[i] != hunter.HealthSegments[i];
            for (int i = 0; i < healthRows.Count; i++)
            {
                bool visible = i < values.Length; healthRows[i].gameObject.SetActive(visible); healthBars[i].gameObject.SetActive(visible);
                if (!visible) continue;
                Place(healthRows[i], new Vector2(0, 1), new Vector2(0, 1), new Vector2(36, -(touch ? 236 : 210) - i * (touch ? 110 : 64)),
                    new Vector2(928, touch ? 96 : 58));
                healthRowLabels[i].fontSize = touch ? 26 : 21;
                healthRowLabels[i].rectTransform.sizeDelta = new Vector2(450, touch ? 80 : 42);
                healthSplitButtons[i].GetComponent<RectTransform>().sizeDelta = new Vector2(210, touch ? 84 : 44);
                healthMergeButtons[i].GetComponent<RectTransform>().sizeDelta = new Vector2(220, touch ? 84 : 44);
                Place(healthBars[i], new Vector2(0, 1), new Vector2(0, 1), new Vector2(36 + sum / healthDraft.Maximum * 928, -162),
                    new Vector2(values[i] / healthDraft.Maximum * 928 - 3, 28));
                sum += values[i]; healthRowLabels[i].text = "第 " + (i + 1) + " 段：" + values[i] + "    恢复节点 " + sum;
                SetButton(healthSplitButtons[i], "拆分第 " + (i + 1) + " 段", CanEditHealth() && healthDraft.CanSplit(i));
                SetButton(healthMergeButtons[i], "合并第 " + (i + 1) + "、" + (i + 2) + " 段", CanEditHealth() && healthDraft.CanMerge(i));
            }
            SetButton(saveHealthButton, "保存血段", CanEditHealth() && changed);
            SetButton(cancelHealthButton, "取消并返回", true);
            foreach (var button in buttons) if (!healthEditButtons.Contains(button)) button.interactable = false;
        }
        public void RefreshHealthLayout()
        { if (IsHealthLayoutOpen && CanEditHealth()) RenderHealthLayout(session.Career.Selected); }
    }
}
