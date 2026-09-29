// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit
{
using Sirenix.Serialization;
using System;
using System.Collections.Generic;

public class SkillCustomEventData
{
    [NonSerialized, OdinSerialize]
    public Dictionary<int, SkillCustomEvent> FrameData = new Dictionary<int, SkillCustomEvent>();
}

}
