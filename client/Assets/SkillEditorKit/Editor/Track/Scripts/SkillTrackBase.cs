// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit.Editor
{
using System;
using UnityEngine.UIElements;

public abstract class SkillTrackBase
{
    protected float frameWidth;
    public SkillTrackData Model { get; set; }
    public int StartFrame(SkillFrameEventBase data, int fallback) => Model?.Clips.Find(c => ReferenceEquals(c.Data, data))?.Frame ?? fallback;
    public SkillTrackLane Lane { get; protected set; }
    protected void InitLane(VisualElement menuParent, VisualElement trackParent)
    {
        Lane = new SkillTrackLane();
        Lane.Init(menuParent, trackParent, this);
    }
    public virtual System.Collections.Generic.IEnumerable<TrackItemBase> Items => System.Linq.Enumerable.Empty<TrackItemBase>();

    public virtual void Init(VisualElement menuParent, VisualElement trackParent, float frameWidth)
    {
        this.frameWidth = frameWidth;
    }

    public virtual void ResetView()
    {
        ResetView(frameWidth);
    }

    public virtual void ResetView(float frameWdith)
    {
        this.frameWidth = frameWdith;
    }

    public virtual void DeleteTrackItem(int frameIndex) { }
    public virtual void OnConfigChanged() { }
    public virtual void OnPlay(int startFrameIndex) { }
    public virtual void TickView(int frameIndex) { }
    public virtual void OnStop() { }
    public virtual void Destory() { }
    public virtual void DrawGizmos() { }
    public virtual TrackItemBase FindItemByData(object data) { return null; }

    public virtual void OnSceneGUI() { }
}

}
