// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit.Editor
{
using UnityEngine;

public abstract class TrackItemBase
{
    protected int frameIndex;
    public int FrameIndex { get => ParentTrack == null ? frameIndex : ParentTrack.StartFrame(Data, frameIndex); }
    public abstract SkillFrameEventBase Data { get; }
    public abstract SkillTrackBase ParentTrack { get; }
    public abstract UnityEngine.UIElements.VisualElement Element { get; }
    protected float frameUnitWidth;
    public abstract void Select();
    public abstract void OnSelect();
    public abstract void OnUnSelect();
    public virtual void OnConfigChanged() { }
    public virtual void ResetView()
    {
        ResetView(frameUnitWidth);
    }
    public virtual void ResetView(float frameUnitWidth)
    {
        this.frameUnitWidth = frameUnitWidth;
    }
}
public abstract class TrackItemBase<T> : TrackItemBase where T : SkillTrackBase
{
    protected T track;
    protected Color normalColor;
    protected Color selectColor;
    public SkillTrackItemStyleBase itemStyle { get; protected set; }
    public override SkillTrackBase ParentTrack => track;
    public override UnityEngine.UIElements.VisualElement Element => itemStyle?.root;

    protected void BindInteraction(UnityEngine.UIElements.VisualElement dragArea)
    {
        Element.userData = this;
        dragArea.RegisterCallback<UnityEngine.UIElements.MouseDownEvent>(evt =>
            SkillEditorWindow.Instance?.TimelineSelection.BeginItemDrag(this, dragArea, evt));
        if (SkillClipTrim.Supports(Data))
        {
            var sheet = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.StyleSheet>("Assets/SkillEditorKit/Editor/Track/Assets/TrimHandles.uss");
            if (sheet != null) Element.styleSheets.Add(sheet);
            if (!(Data is SkillProjectileEvent)) AddTrimHandle(true);
            AddTrimHandle(false);
        }
    }

    private void AddTrimHandle(bool left)
    {
        var handle = new UnityEngine.UIElements.VisualElement { name = left ? "TrimLeft" : "TrimRight",
            tooltip = Data is SkillProjectileEvent ? "拖动调整飞行时长；发射帧和技能动作时长不变" :
                left ? "拖动裁剪起点（右端保持不动）；Alt 关闭吸附" : "拖动裁剪终点（左端保持不动）；Alt 关闭吸附" };
        handle.AddToClassList("skill-trim-handle");
        if (left) handle.style.left = 0; else handle.style.right = 0;
        handle.RegisterCallback<UnityEngine.UIElements.MouseDownEvent>(evt =>
            SkillEditorWindow.Instance?.TimelineSelection.BeginTrim(this, handle, evt, left));
        Element.Add(handle);
    }

    public override void Select()
    {
        SkillEditorWindow.Instance.TimelineSelection.SelectOnly(this);
    }
    public override void OnSelect()
    {
        itemStyle.SetBGColor(selectColor);
    }
    public override void OnUnSelect()
    {
        itemStyle.SetBGColor(normalColor);
    }
}

}
