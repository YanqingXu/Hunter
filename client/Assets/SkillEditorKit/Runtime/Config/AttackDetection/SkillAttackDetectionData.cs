// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit
{
using Sirenix.Serialization;
using System;
using System.Collections.Generic;

public class SkillAttackDetectionData
{
    [NonSerialized, OdinSerialize]
    public List<SkillAttackDetectionEvent> FrameData = new List<SkillAttackDetectionEvent>();
}
}
