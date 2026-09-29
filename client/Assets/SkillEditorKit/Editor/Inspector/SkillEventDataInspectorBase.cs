// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit.Editor
{
using UnityEngine.UIElements;

public abstract class SkillEventDataInspectorBase
{
    protected VisualElement root;
    protected int itemFrameIndex;
    public void SetFrameIndex(int itemFrameIndex)
    {
        this.itemFrameIndex = itemFrameIndex;
    }
    public virtual void Draw(VisualElement root, TrackItemBase trackItem, SkillTrackBase track)
    {
        this.root = root;
        SetFrameIndex(trackItem.FrameIndex);
    }
}

public abstract class SkillEventDataInspectorBase<TrackItem, Track> : SkillEventDataInspectorBase where TrackItem : TrackItemBase where Track : SkillTrackBase
{
    protected TrackItem trackItem;
    protected Track track;
    public override void Draw(VisualElement root, TrackItemBase trackItem, SkillTrackBase track)
    {
        base.Draw(root, trackItem, track);
        this.trackItem = (TrackItem)trackItem;
        this.track = (Track)track;
        OnDraw();
    }

    public abstract void OnDraw();

}
}
