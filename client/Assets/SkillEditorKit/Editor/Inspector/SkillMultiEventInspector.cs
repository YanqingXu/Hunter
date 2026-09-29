// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit.Editor
{
using System.Collections.Generic;
using System.Linq;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

public static class SkillMultiEventInspector
{
    public static void Draw(VisualElement root, List<SkillEventEntry> selected)
    {
        var window = SkillEditorWindow.Instance;
        var clip = window.SkillConfig;
        root.Add(new Label($"已选中 {selected.Count} 个事件（{selected.Select(e => e.Kind).Distinct().Count()} 种类型）"));
        root.Add(new HelpBox("起始帧按整组偏移，保留相对间距。混合类型只显示共同可编辑字段。", HelpBoxMessageType.Info));
        int first = selected.Min(e => e.Frame);
        var start = new IntegerField("整组起始帧") { value = first, isDelayed = true };
        start.RegisterValueChangedCallback(evt =>
        {
            long delta = (long)evt.newValue - first;
            if (delta < int.MinValue || delta > int.MaxValue || !SkillEditorClipboard.MoveSelected((int)delta))
                start.SetValueWithoutNotify(first);
        });
        root.Add(start);

        if (selected.All(e => e.Kind == SkillEventKind.Animation || e.Kind == SkillEventKind.Effect || e.Kind == SkillEventKind.Attack))
        {
            var duration = new IntegerField("统一持续帧数") { value = selected[0].Duration(clip.FrameRote), isDelayed = true };
            duration.showMixedValue = selected.Any(e => e.Duration(clip.FrameRote) != duration.value);
            duration.RegisterValueChangedCallback(evt =>
            {
                int frames = Mathf.Clamp(evt.newValue, 1, SkillTimelineData.MaxFrame);
                var proposed = selected.Select(SkillTimelineData.Clone).ToList();
                foreach (var entry in proposed) SetDuration(entry.Data, frames);
                if (!SkillTimelineData.ValidatePlacement(clip, proposed, selected.Select(e => e.Data).ToList(), out string reason))
                {
                    Debug.LogWarning(reason);
                    duration.SetValueWithoutNotify(selected[0].Duration(clip.FrameRote));
                    return;
                }
                SkillEditorClipboard.Commit("批量修改持续帧数", () =>
                {
                    foreach (var entry in selected) SetDuration(entry.Data, frames);
                    clip.FrameCount = Mathf.Max(clip.FrameCount, selected.Max(e => e.Frame + frames));
                }, selected);
            });
            root.Add(duration);
        }
        if (selected.All(e => e.Data is SkillAudioEvent))
        {
            var audio = selected.Select(e => (SkillAudioEvent)e.Data).ToList();
            var volume = new FloatField("统一音量") { value = audio[0].Voluem, isDelayed = true };
            volume.showMixedValue = audio.Any(e => e.Voluem != volume.value);
            volume.RegisterValueChangedCallback(evt => SkillEditorClipboard.Commit("批量修改音量", () =>
            {
                foreach (var data in audio) data.Voluem = float.IsNaN(evt.newValue) ? 1 : Mathf.Clamp01(evt.newValue);
            }, selected));
            root.Add(volume);
        }
        if (selected.All(e => e.Data is SkillAnimationEvent))
        {
            var animations = selected.Select(e => (SkillAnimationEvent)e.Data).ToList();
            var motion = new Toggle("应用根运动") { value = animations[0].ApplyRootMotion };
            motion.showMixedValue = animations.Any(e => e.ApplyRootMotion != motion.value);
            motion.RegisterValueChangedCallback(evt => SkillEditorClipboard.Commit("批量修改根运动", () =>
            {
                foreach (var data in animations) data.ApplyRootMotion = evt.newValue;
            }, selected));
            root.Add(motion);
        }
        root.Add(new Button(() => SkillEditorClipboard.DuplicateSelected()) { text = "复制一组" });
        root.Add(new Button(() => SkillEditorClipboard.DeleteSelected()) { text = "删除选中事件" });
    }

    private static void SetDuration(SkillFrameEventBase data, int frames)
    {
        if (data is SkillAnimationEvent animation) animation.DurationFrame = frames;
        else if (data is SkillEffectEvent effect) effect.Duration = frames;
        else if (data is SkillAttackDetectionEvent attack) attack.DurationFrame = frames;
    }
}

}
