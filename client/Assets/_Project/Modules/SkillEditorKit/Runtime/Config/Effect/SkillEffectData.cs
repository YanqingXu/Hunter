// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit
{

using Sirenix.Serialization;
using System;
using System.Collections.Generic;

public class SkillEffectData
{
    /// <summary>
    /// 特效事件
    /// </summary>
    [NonSerialized, OdinSerialize]
    public List<SkillEffectEvent> FrameData = new List<SkillEffectEvent>();
}
}
