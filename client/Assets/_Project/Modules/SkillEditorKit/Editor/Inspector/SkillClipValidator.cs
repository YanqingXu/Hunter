// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit.Editor
{
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Performs non-destructive validation of a skill asset before it is handed to
/// gameplay or committed to source control.
/// </summary>
public static class SkillClipValidator
{
    public sealed class ValidationIssue
    {
        public readonly bool IsError;
        public readonly string Message;
        public readonly SkillFrameEventBase Data;

        public ValidationIssue(bool isError, string message, SkillFrameEventBase data = null)
        {
            IsError = isError;
            Message = message;
            Data = data;
        }
    }

    public static List<ValidationIssue> ReadIssues(SkillClip clip)
    {
        try { return clip == null ? new List<ValidationIssue>() : Validate(clip); }
        catch (System.Exception ex) { return new List<ValidationIssue> { new ValidationIssue(true, "配置无法检查：" + ex.Message) }; }
    }

    [MenuItem("SkillEditorKit/校验当前技能配置")]
    private static void ValidateCurrentSkill()
    {
        SkillClip clip = SkillEditorWindow.Instance == null
            ? Selection.activeObject as SkillClip
            : SkillEditorWindow.Instance.SkillConfig;

        if (clip == null)
        {
            EditorUtility.DisplayDialog("技能配置校验", "请先在技能编辑器中加载配置，或在 Project 面板中选中一个 SkillClip。", "确定");
            return;
        }

        List<ValidationIssue> issues = Validate(clip);
        int errorCount = issues.Count(issue => issue.IsError);
        int warningCount = issues.Count - errorCount;

        if (issues.Count == 0)
        {
            Debug.Log($"技能配置校验通过：{clip.name}", clip);
            EditorUtility.DisplayDialog("技能配置校验", $"{clip.name} 校验通过，未发现问题。", "确定");
            return;
        }

        foreach (ValidationIssue issue in issues)
        {
            string message = $"[{clip.name}] {issue.Message}";
            if (issue.IsError) Debug.LogError(message, clip);
            else Debug.LogWarning(message, clip);
        }

        EditorUtility.DisplayDialog(
            "技能配置校验",
            $"{clip.name}：{errorCount} 个错误，{warningCount} 个警告。\n详细信息已输出到 Console。",
            "确定");
    }

    [MenuItem("SkillEditorKit/校验全部技能配置")]
    private static void ValidateAllSkills()
    {
        string[] guids = AssetDatabase.FindAssets("t:SkillClip");
        int skillCount = 0;
        int errorCount = 0;
        int warningCount = 0;
        try
        {
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                EditorUtility.DisplayProgressBar("校验全部技能配置", path, guids.Length == 0 ? 1 : (float)i / guids.Length);
                SkillClip clip = AssetDatabase.LoadAssetAtPath<SkillClip>(path);
                if (clip == null) continue;
                skillCount++;
                List<ValidationIssue> issues = Validate(clip);
                foreach (ValidationIssue issue in issues)
                {
                    string message = $"[{clip.name}] {issue.Message}";
                    if (issue.IsError)
                    {
                        errorCount++;
                        Debug.LogError(message, clip);
                    }
                    else
                    {
                        warningCount++;
                        Debug.LogWarning(message, clip);
                    }
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        EditorUtility.DisplayDialog(
            "全部技能配置校验",
            $"已校验 {skillCount} 个 SkillClip：{errorCount} 个错误，{warningCount} 个警告。\n详细信息已输出到 Console。",
            "确定");
    }

    private static List<ValidationIssue> Validate(SkillClip clip)
    {
        List<ValidationIssue> issues = new List<ValidationIssue>();
        if (!System.Enum.IsDefined(typeof(SkillSpace), clip.Space)) Error(issues, "未知的游戏空间。");
        if (clip.DataVersion > SkillClip.CurrentDataVersion) Error(issues, "配置版本高于当前编辑器版本，禁止编辑。");
        else if (clip.DataVersion < SkillClip.CurrentDataVersion) Warning(issues, "旧版配置：可从 数据版本迁移预览 补充稳定事件 ID。");
        if (!SkillTrackModel.ValidateStructure(clip, out string structureError))
        { Error(issues, structureError); return issues; }
        var entries = SkillTimelineData.Read(clip);
        foreach (var entry in entries)
            if (!SkillMediaTiming.Validate(entry.Data, clip.FrameRote, out string rangeError)) Error(issues, $"{entry.Kind}@{entry.Frame}: {rangeError}", entry.Data);
        if (entries.Any(e => !System.Guid.TryParseExact(e.Data.EventId, "N", out _)))
            Warning(issues, "部分事件缺少有效 ID；请先预览迁移。");
        if (entries.Where(e => !string.IsNullOrEmpty(e.Data.EventId)).GroupBy(e => e.Data.EventId, System.StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            Error(issues, "存在重复事件 ID；请使用迁移预览修复。");
        if (clip.FrameRote <= 0) Error(issues, "帧率必须大于 0。");
        if (clip.FrameCount < 0) Error(issues, "最大帧不能小于 0。");

        if (clip.UseTrackModel)
        {
            // Validate each lane using isolated legacy-shaped views, never a second saved graph.
            foreach (var track in clip.Tracks)
            {
                if (track.Kind == SkillEventKind.Projectile)
                {
                    foreach (var item in track.Clips)
                    {
                        ValidateFrameIndex(item.Frame, clip.FrameCount, "投射物发射", issues, item.Data);
                        var projectile = (SkillProjectileEvent)item.Data;
                        if (projectile.Prefab == null) Warning(issues, "投射物未指定外观预制体：运行时仍有伤害判定，但不会显示模型。", item.Data);
                        if (projectile.StopAtWalls && projectile.WallLayers.value == 0) Warning(issues, "投射物墙壁层为空，不会被墙壁阻挡。", item.Data);
                    }
                    continue;
                }
                var view = ScriptableObject.CreateInstance<SkillClip>();
                var originals = new Dictionary<SkillFrameEventBase, SkillFrameEventBase>();
                try
                {
                    view.Space = clip.Space; view.FrameCount = clip.FrameCount; view.FrameRote = clip.FrameRote;
                    foreach (var item in track.Clips)
                    {
                        var copy = SkillTimelineData.Clone(new SkillEventEntry(track.Kind, item.Data, item.Frame, 0));
                        originals[copy.Data] = item.Data;
                        SkillTimelineData.Add(view, copy);
                    }
                    view.FrameCount = clip.FrameCount;
                    var laneIssues = new List<ValidationIssue>();
                    ValidateCustomEvents(view, laneIssues); ValidateAnimationEvents(view, laneIssues);
                    ValidateAudioEvents(view, laneIssues); ValidateEffectEvents(view, laneIssues); ValidateAttackEvents(view, laneIssues);
                    foreach (var issue in laneIssues) issues.Add(new ValidationIssue(issue.IsError, $"轨道“{track.Name}”：{issue.Message}",
                        issue.Data != null && originals.TryGetValue(issue.Data, out var original) ? original : null));
                }
                finally { Object.DestroyImmediate(view); }
            }
            return issues;
        }
        ValidateCustomEvents(clip, issues);
        ValidateAnimationEvents(clip, issues);
        ValidateAudioEvents(clip, issues);
        ValidateEffectEvents(clip, issues);
        ValidateAttackEvents(clip, issues);
        return issues;
    }

    private static void ValidateCustomEvents(SkillClip clip, List<ValidationIssue> issues)
    {
        if (clip.skillCustomEventData == null || clip.skillCustomEventData.FrameData == null)
        {
            Error(issues, "自定义事件数据为空。");
            return;
        }

        foreach (KeyValuePair<int, SkillCustomEvent> pair in clip.skillCustomEventData.FrameData)
        {
            ValidateFrameIndex(pair.Key, clip.FrameCount, $"自定义事件@{pair.Key}", issues);
            if (pair.Value == null) Error(issues, $"自定义事件@{pair.Key} 的事件对象为空。");
        }
    }

    private static void ValidateAnimationEvents(SkillClip clip, List<ValidationIssue> issues)
    {
        if (clip.SkillAnimationData == null || clip.SkillAnimationData.FrameData == null)
        {
            Error(issues, "动画事件数据为空。");
            return;
        }

        List<KeyValuePair<int, SkillAnimationEvent>> events = clip.SkillAnimationData.FrameData.OrderBy(pair => pair.Key).ToList();
        if (!SkillAnimationTiming.ValidateSequence(events, out string overlapError)) Error(issues, overlapError);
        for (int i = 0; i < events.Count; i++)
        {
            int frame = events[i].Key;
            SkillAnimationEvent animationEvent = events[i].Value;
            ValidateFrameIndex(frame, clip.FrameCount, $"动画事件@{frame}", issues, animationEvent);
            if (animationEvent == null)
            {
                Error(issues, $"动画事件@{frame} 的事件对象为空。");
                continue;
            }
            if (animationEvent.AnimationClip == null) Warning(issues, $"动画事件@{frame} 未指定 AnimationClip。", animationEvent);
            if (animationEvent.DurationFrame <= 0) Error(issues, $"动画事件@{frame} 的持续帧必须大于 0。", animationEvent);
            if (frame + animationEvent.DurationFrame > clip.FrameCount) Warning(issues, $"动画事件@{frame} 超出最大帧 {clip.FrameCount}。", animationEvent);
        }
    }

    private static void ValidateAudioEvents(SkillClip clip, List<ValidationIssue> issues)
    {
        if (clip.SkillAudioData == null || clip.SkillAudioData.FrameData == null)
        {
            Error(issues, "音效事件数据为空。");
            return;
        }

        for (int i = 0; i < clip.SkillAudioData.FrameData.Count; i++)
        {
            SkillAudioEvent audioEvent = clip.SkillAudioData.FrameData[i];
            if (audioEvent == null)
            {
                Error(issues, $"音效轨道[{i}] 的事件对象为空。");
                continue;
            }
            ValidateFrameIndex(audioEvent.FrameIndex, clip.FrameCount, $"音效轨道[{i}]", issues, audioEvent);
            if (audioEvent.AudioClip == null) Warning(issues, $"音效轨道[{i}] 未指定 AudioClip。", audioEvent);
            if (audioEvent.Voluem < 0 || audioEvent.Voluem > 1) Warning(issues, $"音效轨道[{i}] 的音量不在 0~1 范围内。", audioEvent);
        }
    }

    private static void ValidateEffectEvents(SkillClip clip, List<ValidationIssue> issues)
    {
        if (clip.SkillEffectData == null || clip.SkillEffectData.FrameData == null)
        {
            Error(issues, "特效事件数据为空。");
            return;
        }

        for (int i = 0; i < clip.SkillEffectData.FrameData.Count; i++)
        {
            SkillEffectEvent effectEvent = clip.SkillEffectData.FrameData[i];
            if (effectEvent == null)
            {
                Error(issues, $"特效轨道[{i}] 的事件对象为空。");
                continue;
            }
            ValidateFrameIndex(effectEvent.FrameIndex, clip.FrameCount, $"特效轨道[{i}]", issues, effectEvent);
            if (effectEvent.Prefab == null) Warning(issues, $"特效轨道[{i}] 未指定预制体。", effectEvent);
            if (effectEvent.Duration <= 0) Error(issues, $"特效轨道[{i}] 的持续帧必须大于 0。", effectEvent);
            if (Mathf.Approximately(effectEvent.Scale.x, 0) || Mathf.Approximately(effectEvent.Scale.y, 0) || Mathf.Approximately(effectEvent.Scale.z, 0))
            {
                Warning(issues, $"特效轨道[{i}] 至少一个缩放轴为 0，预览或运行时可能不可见。", effectEvent);
            }
            if (effectEvent.FrameIndex + effectEvent.Duration > clip.FrameCount) Warning(issues, $"特效轨道[{i}] 超出最大帧 {clip.FrameCount}。", effectEvent);
        }
    }

    private static void ValidateAttackEvents(SkillClip clip, List<ValidationIssue> issues)
    {
        if (clip.SkillAttackDetectionData == null || clip.SkillAttackDetectionData.FrameData == null)
        {
            Error(issues, "攻击检测事件数据为空。");
            return;
        }

        for (int i = 0; i < clip.SkillAttackDetectionData.FrameData.Count; i++)
        {
            SkillAttackDetectionEvent attackEvent = clip.SkillAttackDetectionData.FrameData[i];
            if (attackEvent == null)
            {
                Error(issues, $"攻击检测轨道[{i}] 的事件对象为空。");
                continue;
            }
            ValidateFrameIndex(attackEvent.FrameIndex, clip.FrameCount, $"攻击检测轨道[{i}]", issues, attackEvent);
            if (attackEvent.DurationFrame <= 0) Error(issues, $"攻击检测轨道[{i}] 的持续帧必须大于 0。", attackEvent);
            if (attackEvent.AttackDetectionData == null) Error(issues, $"攻击检测轨道[{i}] 未指定检测类型。", attackEvent);
            if (attackEvent.AttackHitConfig == null) Error(issues, $"攻击检测轨道[{i}] 的命中配置为空。", attackEvent);
            if (attackEvent.FrameIndex + attackEvent.DurationFrame > clip.FrameCount) Warning(issues, $"攻击检测轨道[{i}] 超出最大帧 {clip.FrameCount}。", attackEvent);
            if (!SkillAttackShape.Validate(attackEvent.AttackDetectionData as AttackShapeDetectionDataBase, out string shapeError, clip.Space))
                Error(issues, $"攻击检测轨道[{i}]：{shapeError}", attackEvent);
            if (!SkillHitRules.Validate(attackEvent.HitRules, out string hitError))
                Error(issues, $"攻击检测轨道[{i}]：{hitError}", attackEvent);

            if (attackEvent.AttackDetectionData is AttackWeaponDetectionData weapon && string.IsNullOrWhiteSpace(weapon.weaponName))
                Error(issues, $"攻击检测轨道[{i}] 未指定武器名称。", attackEvent);
            if (attackEvent.AttackDetectionData is AttackBoxDetectionData box && (box.Scale.x <= 0 || box.Scale.y <= 0 || (clip.Space != SkillSpace.TwoD && box.Scale.z <= 0)))
                Error(issues, $"攻击检测轨道[{i}] 的盒体尺寸必须全部大于 0。", attackEvent);
            if (attackEvent.AttackDetectionData is AttackSphereDetectionData sphere && sphere.Radius <= 0)
                Error(issues, $"攻击检测轨道[{i}] 的球体半径必须大于 0。", attackEvent);
            if (attackEvent.AttackDetectionData is AttackFanDetectionData fan)
            {
                if (fan.Radius <= 0 || (clip.Space != SkillSpace.TwoD && fan.Height <= 0)) Error(issues, $"攻击检测轨道[{i}] 的扇形外半径和高度必须大于 0。", attackEvent);
                if (fan.InsideRadius < 0 || fan.InsideRadius >= fan.Radius) Error(issues, $"攻击检测轨道[{i}] 的扇形内半径必须在 0 到外半径之间。", attackEvent);
                if (fan.Angle <= 0 || fan.Angle > 360) Error(issues, $"攻击检测轨道[{i}] 的扇形角度必须在 0~360 度之间。", attackEvent);
            }
        }
    }

    private static void ValidateFrameIndex(int frame, int maximumFrame, string context, List<ValidationIssue> issues, SkillFrameEventBase data = null)
    {
        if (frame < 0) Error(issues, $"{context} 的起始帧不能小于 0。", data);
        else if (frame > maximumFrame) Error(issues, $"{context} 的起始帧超过最大帧 {maximumFrame}。", data);
    }

    private static void Error(List<ValidationIssue> issues, string message, SkillFrameEventBase data = null) => issues.Add(new ValidationIssue(true, message, data));
    private static void Warning(List<ValidationIssue> issues, string message, SkillFrameEventBase data = null) => issues.Add(new ValidationIssue(false, message, data));
}

}
