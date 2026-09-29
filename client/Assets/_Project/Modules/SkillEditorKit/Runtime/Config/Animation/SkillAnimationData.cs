// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit
{
using System.Collections.Generic;
using System;
using Sirenix.Serialization;
/// <summary>
/// 技能动画数据
/// </summary>
[Serializable]
public class SkillAnimationData
{
    /// <summary>
    /// 动画帧事件
    /// Key:帧数
    /// Value:事件数据
    /// </summary>
    [NonSerialized,OdinSerialize]
    public Dictionary<int, SkillAnimationEvent> FrameData = new Dictionary<int, SkillAnimationEvent>();
}

}
