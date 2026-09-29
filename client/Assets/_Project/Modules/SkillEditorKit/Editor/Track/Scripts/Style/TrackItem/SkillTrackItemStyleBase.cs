// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit.Editor
{
using UnityEngine;
using UnityEngine.UIElements;

public abstract class SkillTrackItemStyleBase 
{
    public VisualElement root { get; protected set; }

    public virtual void SetBGColor(Color color)
    {
        root.style.backgroundColor = color;
    }
    public virtual void SetWidth(float width)
    {
        root.style.width = width;
    }
    public virtual void SetPosition(float x)
    {
        Vector3 pos = root.transform.position;
        pos.x = x;
        root.transform.position = pos;
    }

}

}
