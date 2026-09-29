// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit
{
using System;
/// <summary>
/// 帧事件基类
/// </summary>
[Serializable]
public abstract class SkillFrameEventBase
{
    // Empty for legacy assets. Assigned only by explicit migration or event creation.
    [UnityEngine.HideInInspector] public string EventId;
}

}
