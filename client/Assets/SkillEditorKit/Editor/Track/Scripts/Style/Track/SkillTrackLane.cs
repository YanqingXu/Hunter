// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit.Editor
{
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>One persisted track = one row. Its children are independent clips.</summary>
public sealed class SkillTrackLane : SkillTrackStyleBase
{
    private SkillTrackData model;
    public void Init(VisualElement menuParent, VisualElement contentParent, SkillTrackBase owner)
    {
        model = owner.Model;
        this.menuParent = menuParent; this.contentParent = contentParent;
        menuRoot = new VisualElement { name = "TrackHeader-" + model.TrackId };
        contentRoot = new VisualElement { name = "TrackLane-" + model.TrackId, userData = owner };
        foreach (var element in new[] { menuRoot, contentRoot })
        {
            element.style.height = 50; element.style.flexShrink = 0;
            element.style.marginBottom = 1;
            element.style.backgroundColor = new Color(.23f, .23f, .23f);
        }
        contentRoot.style.width = Length.Percent(100);
        menuParent.Add(menuRoot); contentParent.Add(contentRoot);
        var title = new VisualElement { name = "TrackTitleRow" };
        title.style.flexDirection = FlexDirection.Row;
        title.style.alignItems = Align.Center;
        title.style.height = 22; title.style.flexShrink = 0;
        var name = new TextField { name = "TrackNameField", value = model.Name, isDelayed = true, tooltip = SkillTrackActions.Label(model.Kind) + "轨道（右键显示操作）" };
        name.style.flexGrow = 1; name.style.flexShrink = 1; name.style.minWidth = 0;
        name.SetEnabled(!model.Locked);
        name.RegisterValueChangedCallback(evt => SkillTrackActions.Change(model, "重命名轨道", () => model.Name = evt.newValue));
        title.Add(name);
        var delete = new Button(() => SkillTrackActions.Delete(model))
        {
            name = "DeleteTrackButton", text = "×",
            tooltip = model.Locked ? "轨道已锁定，请先解锁再删除" : "删除此轨道及其中全部片段（可撤销）；不删除源资源"
        };
        CompactButton(delete);
        delete.style.fontSize = 16;
        delete.SetEnabled(!model.Locked);
        title.Add(delete);
        menuRoot.Add(title);
        var buttons = new VisualElement { name = "TrackActionsRow" };
        buttons.style.flexDirection = FlexDirection.Row;
        buttons.style.alignItems = Align.Center;
        buttons.style.height = 22; buttons.style.flexShrink = 0;
        var enabled = new Toggle("启用") { name = "TrackEnabledToggle", value = model.Enabled, tooltip = "影响预览及实际运行；不删除数据" };
        CompactToggle(enabled);
        enabled.SetEnabled(!model.Locked);
        enabled.RegisterValueChangedCallback(evt => SkillTrackActions.Change(model, "切换轨道启用", () => model.Enabled = evt.newValue));
        var locked = new Toggle("锁") { name = "TrackLockedToggle", value = model.Locked, tooltip = "锁定轨道内容编辑；不影响播放" };
        CompactToggle(locked);
        locked.RegisterValueChangedCallback(evt => SkillTrackActions.Change(model, "切换轨道锁定", () => model.Locked = evt.newValue, true));
        var add = new Button(() => SkillTrackActions.AddClip(model, SkillEditorWindow.Instance.CurrentSelectFrameIndex)) { name = "AddTrackClipButton", text = "+", tooltip = "在播放头新增片段；也可双击轨道空白或拖入资源" };
        CompactButton(add);
        add.SetEnabled(!model.Locked);
        buttons.Add(enabled); buttons.Add(locked); buttons.Add(add);
        var more = new Button(ShowMenu) { name = "TrackMenuButton", text = "⋮", tooltip = "排序、复制、移动片段、删除轨道" };
        CompactButton(more); buttons.Add(more);
        menuRoot.Add(buttons);
        menuRoot.RegisterCallback<MouseDownEvent>(evt => Activate(), TrickleDown.TrickleDown);
        contentRoot.RegisterCallback<MouseDownEvent>(evt =>
        {
            Activate();
            if (evt.button != 0 || evt.clickCount != 2) return;
            for (var target = evt.target as VisualElement; target != null && target != contentRoot; target = target.parent)
                if (target.userData is TrackItemBase) return;
            SkillTrackActions.AddClip(model, SkillEditorWindow.Instance.GetFrameIndexByPos(contentRoot.WorldToLocal(evt.mousePosition).x));
            evt.StopPropagation();
        }, TrickleDown.TrickleDown);
        menuRoot.AddManipulator(new ContextualMenuManipulator(evt => { ShowMenu(); evt.StopPropagation(); }));
        contentRoot.RegisterCallback<DragUpdatedEvent>(evt =>
        {
            if (!CanDrop()) return;
            DragAndDrop.visualMode = model.Locked ? DragAndDropVisualMode.Rejected : DragAndDropVisualMode.Copy;
            evt.StopPropagation();
        });
        contentRoot.RegisterCallback<DragPerformEvent>(evt =>
        {
            if (!CanDrop() || model.Locked) return;
            int frame = SkillEditorWindow.Instance.GetFrameIndexByPos(contentRoot.WorldToLocal(evt.mousePosition).x);
            if (SkillTrackActions.AddClip(model, frame, DragAndDrop.objectReferences[0])) DragAndDrop.AcceptDrag();
            evt.StopPropagation();
        });
        RefreshState();
    }

    private static void CompactButton(Button button)
    {
        button.style.width = 22; button.style.minWidth = 22; button.style.height = 20;
        button.style.flexGrow = 0; button.style.flexShrink = 0;
        button.style.marginLeft = 2; button.style.marginRight = 2;
        button.style.marginTop = 0; button.style.marginBottom = 0;
        button.style.paddingLeft = 0; button.style.paddingRight = 0;
    }

    private static void CompactToggle(Toggle toggle)
    {
        // Editor fields normally reserve a wide label column; a track header has only 290 px.
        toggle.style.flexGrow = 0; toggle.style.flexShrink = 0;
        toggle.style.marginRight = 8;
        toggle.labelElement.style.minWidth = 0;
        toggle.labelElement.style.width = StyleKeyword.Auto;
        toggle.labelElement.style.flexGrow = 0;
        toggle.labelElement.style.marginRight = 4;
    }

    public void LayoutItems(System.Collections.Generic.IEnumerable<TrackItemBase> items)
    {
        var window = SkillEditorWindow.Instance;
        var entries = SkillTimelineData.Read(window.SkillConfig);
        var ends = new System.Collections.Generic.List<long>();
        foreach (var item in items.OrderBy(i => i.FrameIndex))
        {
            var entry = entries.First(e => ReferenceEquals(e.Data, item.Data));
            int lane = model.Kind == SkillEventKind.Animation && ends.Count > 0 ? 0 : ends.FindIndex(end => end <= entry.Frame);
            if (lane < 0) { lane = ends.Count; ends.Add(0); }
            ends[lane] = (long)entry.Frame + entry.Duration(window.SkillConfig.FrameRote);
            item.Element.style.top = 10 + lane * 32;
            item.Element.style.height = 30;
            item.Element.transform.position = new Vector3((entry.Frame - (entry.Kind == SkillEventKind.Custom ? .5f : 0)) * window.FrameUnitWidth, 0, 0);
        }
        float height = Mathf.Max(50, 20 + ends.Count * 32);
        menuRoot.style.height = height; contentRoot.style.height = height;
    }

    public override void AddItem(VisualElement element) { element.style.top = 10; base.AddItem(element); }
    private void Activate() => SkillEditorWindow.Instance.SetActiveTrack(model.TrackId);
    public void RefreshState()
    {
        bool active = SkillEditorWindow.Instance.ActiveTrackId == model.TrackId;
        menuRoot.style.backgroundColor = active ? new Color(.19f, .32f, .43f) : new Color(.23f, .23f, .23f);
        contentRoot.style.opacity = model.Enabled ? 1 : .4f;
    }
    private bool CanDrop()
    {
        var resource = DragAndDrop.objectReferences.FirstOrDefault();
        return (model.Kind == SkillEventKind.Animation && resource is AnimationClip) ||
            (model.Kind == SkillEventKind.Audio && resource is AudioClip) ||
            ((model.Kind == SkillEventKind.Effect || model.Kind == SkillEventKind.Projectile) && resource is GameObject && EditorUtility.IsPersistent(resource));
    }
    private void ShowMenu()
    {
        Activate();
        var menu = new GenericMenu();
        Add(menu, "新增片段（播放头）", !model.Locked, () => SkillTrackActions.AddClip(model, SkillEditorWindow.Instance.CurrentSelectFrameIndex));
        Add(menu, "粘贴片段到此轨道", !model.Locked, () => SkillEditorClipboard.PasteAt(SkillEditorWindow.Instance.CurrentSelectFrameIndex));
        Add(menu, "移动选中片段到此轨道", !model.Locked, () => SkillEditorClipboard.MoveSelected(0, model.TrackId));
        menu.AddSeparator("");
        Add(menu, "上移轨道", !model.Locked, () => SkillTrackActions.Reorder(model, -1));
        Add(menu, "下移轨道", !model.Locked, () => SkillTrackActions.Reorder(model, 1));
        Add(menu, "复制整条轨道", model.Kind != SkillEventKind.Animation, () => SkillTrackActions.Duplicate(model));
        menu.AddSeparator("");
        Add(menu, "删除轨道及片段", !model.Locked, () => SkillTrackActions.Delete(model));
        menu.ShowAsContext();
    }
    private static void Add(GenericMenu menu, string label, bool enabled, System.Action callback)
    {
        if (enabled) menu.AddItem(new GUIContent(label), false, () => callback());
        else menu.AddDisabledItem(new GUIContent(label));
    }
}

}
