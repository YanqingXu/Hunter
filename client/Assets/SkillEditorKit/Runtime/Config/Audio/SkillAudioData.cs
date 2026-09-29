// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit
{
using Sirenix.Serialization;
using System;
using System.Collections.Generic;

public class SkillAudioData
{
    /// <summary>
    /// 音效帧事件
    /// </summary>
    [NonSerialized, OdinSerialize]
    public List<SkillAudioEvent> FrameData = new List<SkillAudioEvent>();
}
}
