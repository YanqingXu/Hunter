// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit.Editor
{
using System;
using UnityEditor;
using UnityEngine.UIElements;

public class SkillAudioTrackItemStyle : SkillTrackItemStyleBase
{
    private const string trackItemAssetPath = "Assets/SkillEditorKit/Editor/Track/Assets/TrackItem/AudioTrackItem.uxml";
    private Label titleLabel;
    public VisualElement mainDragArea { get; private set; }
    public bool isInit { get; private set; }
    public void Init(float frameUnitWidth, SkillAudioEvent skillAudioEvent, SkillMultilineTrackStyle.ChildTrack childTrack)
    {
        if (!isInit)
        {
            titleLabel = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(trackItemAssetPath).Instantiate().Query<Label>();
            root = titleLabel;
            mainDragArea = root.Q<VisualElement>("Main");
            childTrack.InitContent(root);
            isInit = true;
        }
    }

    public void ResetView(float frameUnitWidth, SkillAudioEvent skillAudioEvent)
    {
        if (!isInit) return;
        if (skillAudioEvent.AudioClip != null)
        {
            SetTitle(skillAudioEvent.AudioClip.name);
            SetWidth(frameUnitWidth * SkillMediaTiming.AudioFrames(skillAudioEvent, SkillEditorWindow.Instance.SkillConfig.FrameRote));
            SetPosition(frameUnitWidth * skillAudioEvent.FrameIndex);
        }
        else
        {
            SetTitle("未设置音效");
            SetWidth(frameUnitWidth * 1);
            SetPosition(frameUnitWidth * skillAudioEvent.FrameIndex);
        }
    }

    public void SetTitle(string title)
    {
        titleLabel.text = title;
    }
}

}
