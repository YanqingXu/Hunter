// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit.Editor
{
using System;
using System.Linq;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

public static class SkillMediaRangeInspector
{
    public static void Draw(VisualElement root, SkillFrameEventBase data)
    {
        var window = SkillEditorWindow.Instance;
        var entry = SkillTimelineData.Read(window.SkillConfig).FirstOrDefault(e => ReferenceEquals(e.Data, data));
        if (entry == null) return;
        var enabled = new Toggle("启用运行时裁剪") { value = SkillMediaTiming.HasRange(data) };
        root.Add(enabled);
        var start = new FloatField("源起点（秒）") { value = SkillMediaTiming.ClipIn(data), isDelayed = true };
        var duration = new IntegerField("裁剪长度（技能帧）") { value = entry.Duration(window.SkillConfig.FrameRote), isDelayed = true };
        start.SetEnabled(enabled.value); duration.SetEnabled(enabled.value);
        root.Add(start); root.Add(duration);
        root.Add(new HelpBox("拖片段两端也可裁剪。左边保留右端，右边保留左端；原资源不变。裁剪模式不向源片段外循环扩展。", HelpBoxMessageType.Info));
        enabled.RegisterValueChangedCallback(evt =>
        {
            int frames = entry.Duration(window.SkillConfig.FrameRote);
            if (evt.newValue) frames = Math.Min(frames, Math.Max(1, Mathf.CeilToInt(SkillMediaTiming.SourceLength(data) * window.SkillConfig.FrameRote)));
            if (!Commit(data, evt.newValue, 0, frames)) enabled.SetValueWithoutNotify(!evt.newValue);
        });
        start.RegisterValueChangedCallback(evt =>
        {
            if (!Commit(data, true, evt.newValue, entry.Duration(window.SkillConfig.FrameRote))) start.SetValueWithoutNotify(SkillMediaTiming.ClipIn(data));
        });
        duration.RegisterValueChangedCallback(evt =>
        {
            if (!Commit(data, true, SkillMediaTiming.ClipIn(data), evt.newValue)) duration.SetValueWithoutNotify(entry.Duration(window.SkillConfig.FrameRote));
        });
        root.Add(new Button(() => Commit(data, true, 0,
            Mathf.Max(1, Mathf.CeilToInt(SkillMediaTiming.SourceLength(data) * window.SkillConfig.FrameRote)))) { text = "恢复完整源片段范围" });
    }

    public static bool Commit(SkillFrameEventBase data, bool enabled, float clipIn, int duration)
    {
        var window = SkillEditorWindow.Instance;
        if (window == null) return false;
        var entry = SkillTimelineData.Read(window.SkillConfig).FirstOrDefault(e => ReferenceEquals(e.Data, data));
        if (entry == null || duration < 1 || duration > SkillTimelineData.MaxFrame) return false;
        var copy = SkillTimelineData.Clone(entry);
        Set(copy.Data, enabled, clipIn, duration);
        if (!SkillTimelineData.ValidatePlacement(window.SkillConfig, new[] { copy }, new[] { data }, out string reason))
        { Debug.LogWarning(reason); return false; }
        return SkillEditorClipboard.Commit("修改媒体裁剪范围", () =>
        {
            Set(data, enabled, clipIn, duration);
            if (enabled) window.SkillConfig.DataVersion = System.Math.Max(window.SkillConfig.DataVersion, SkillClip.MediaRangeDataVersion);
            window.SkillConfig.FrameCount = Mathf.Max(window.SkillConfig.FrameCount, entry.Frame + entry.Duration(window.SkillConfig.FrameRote));
        }, new[] { entry });
    }

    private static void Set(SkillFrameEventBase data, bool enabled, float clipIn, int duration)
    {
        SkillMediaTiming.SetRange(data, clipIn, duration);
        if (data is SkillAnimationEvent a) a.UseClipRange = enabled;
        if (data is SkillAudioEvent b) b.UseClipRange = enabled;
    }
}

}
