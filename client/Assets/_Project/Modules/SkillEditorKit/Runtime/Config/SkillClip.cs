// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit
{
using Sirenix.OdinInspector;
using Sirenix.Serialization;
using System;
using UnityEngine;

[CreateAssetMenu(menuName = "Skill Editor Kit/Skill Clip", fileName = "SkillClip")]
public class SkillClip : SerializedScriptableObject
{
    public const int CurrentDataVersion = 5;
    public const int ProjectileDataVersion = 4;
    public const int MediaRangeDataVersion = 2;
    // v3: explicit tracks. Legacy containers are read only when UseTrackModel is false.
    [HideInInspector] public int DataVersion;
    [HideInInspector] public bool UseTrackModel;
    [NonSerialized, OdinSerialize, HideInInspector] public System.Collections.Generic.List<SkillTrackData> Tracks = new System.Collections.Generic.List<SkillTrackData>();
    [LabelText("游戏空间（2D 使用 XY 平面）")] public SkillSpace Space;
    [LabelText("技能名称")] public string SkillName;
    [LabelText("帧数上限")] public int FrameCount = 100;
    [LabelText("帧率")] public int FrameRote = 30;

    [NonSerialized, OdinSerialize, ShowIf("@!UseTrackModel")] public SkillCustomEventData skillCustomEventData = new SkillCustomEventData();
    [NonSerialized, OdinSerialize, ShowIf("@!UseTrackModel")] public SkillAnimationData SkillAnimationData = new SkillAnimationData();
    [NonSerialized, OdinSerialize, ShowIf("@!UseTrackModel")] public SkillAudioData SkillAudioData = new SkillAudioData();
    [NonSerialized, OdinSerialize, ShowIf("@!UseTrackModel")] public SkillEffectData SkillEffectData = new SkillEffectData();
    [NonSerialized, OdinSerialize, ShowIf("@!UseTrackModel")] public SkillAttackDetectionData SkillAttackDetectionData = new SkillAttackDetectionData();

#if UNITY_EDITOR
    private static Action skillConfigValidate;
    public static void SetValidateAction(Action action)
    {
        skillConfigValidate = action;
    }

    private void OnValidate()
    {
        // OnValidate may run during deserialization or on an import worker.
        // Never rebuild editor views or initialize Odin graphs here.
        skillConfigValidate?.Invoke();
    }
#endif
}

}
