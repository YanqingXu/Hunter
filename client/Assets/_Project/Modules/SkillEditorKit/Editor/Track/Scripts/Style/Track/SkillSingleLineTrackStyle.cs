// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit.Editor
{
using UnityEditor;
using UnityEngine.UIElements;

public class SkillSingleLineTrackStyle : SkillTrackStyleBase
{
    private const string menuAssetPath = "Assets/_Project/Modules/SkillEditorKit/Editor/Track/Assets/SinglineTrackStyle/SingleLineTrackMenu.uxml";
    private const string trackAssetPath = "Assets/_Project/Modules/SkillEditorKit/Editor/Track/Assets/SinglineTrackStyle/SingleLineTrackContent.uxml";

    public void Init(VisualElement menuParent,VisualElement contentParent,string title)
    {
        this.menuParent = menuParent;
        this.contentParent = contentParent;
        menuRoot = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(menuAssetPath).Instantiate().Query().ToList()[1];
        titleLabel = (Label)menuRoot;
        titleLabel.text = title;
        contentRoot = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(trackAssetPath).Instantiate().Query().ToList()[1];
        menuParent.Add(menuRoot);
        contentParent.Add(contentRoot);

    }
}

}
