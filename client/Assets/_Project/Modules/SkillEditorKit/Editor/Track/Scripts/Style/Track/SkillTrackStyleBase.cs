// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit.Editor
{
using UnityEngine;
using UnityEngine.UIElements;

public abstract class SkillTrackStyleBase 
{
    public Label titleLabel;
    #region �������ڵ�
    public VisualElement menuParent;
    public VisualElement contentParent;
    #endregion
    #region �������ڵ�
    public VisualElement menuRoot;
    public VisualElement contentRoot;
    #endregion

    public virtual void AddItem(VisualElement ve)
    {
        contentRoot.Add(ve);
    }

    public virtual void DeleteItem(VisualElement ve)
    { 
        contentRoot.Remove(ve);
    }

    public virtual void Destory()
    {
        if (menuRoot != null) menuParent.Remove(menuRoot);
        if (contentRoot != null) contentParent.Remove(contentRoot);
    }
}

}
