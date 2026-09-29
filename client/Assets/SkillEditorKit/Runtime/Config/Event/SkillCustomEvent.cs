// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit
{
public class SkillCustomEvent : SkillFrameEventBase
{
    public SkillEventType EventType;
    public string CustomEventName;
    public int IntArg;
    public string StringArg;
    public float FloatArg;
    public UnityEngine.Object ObjectArg;
}
public enum SkillEventType
{
    Custom,
    CanSkillRelease,
    CanRotate,
    CantRotate,
    AddBuff
}

}
