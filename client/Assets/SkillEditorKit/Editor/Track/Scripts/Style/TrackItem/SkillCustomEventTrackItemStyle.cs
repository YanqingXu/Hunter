// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit.Editor
{
using UnityEditor;
using UnityEngine.UIElements;

public class SkillCustomEventTrackItemStyle : SkillTrackItemStyleBase
{
    private const string trackItemAssetPath = "Assets/SkillEditorKit/Editor/Track/Assets/TrackItem/EventTrackItem.uxml";
    public void Init(SkillTrackStyleBase tracStyle)
    {
        root = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(trackItemAssetPath).Instantiate().Query<Label>();
        tracStyle.AddItem(root);
    }
}

}
