// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit.Editor
{
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;


public class SkillAudioEventInspector : SkillEventDataInspectorBase<AudioTrackItem, AudioTrack>
{
    private FloatField voluemFiled;

    public override void OnDraw()
    {
        // 动画资源
        ObjectField audioClipAssetField = new ObjectField("音效资源");
        audioClipAssetField.objectType = typeof(AudioClip);
        audioClipAssetField.value = trackItem.SkillAudioEvent.AudioClip;
        audioClipAssetField.RegisterValueChangedCallback(AudioClipAssetFiedlValueChanged);
        root.Add(audioClipAssetField);
        SkillMediaRangeInspector.Draw(root, trackItem.SkillAudioEvent);

        // 音量
        voluemFiled = new FloatField("播放音量");
        voluemFiled.value = trackItem.SkillAudioEvent.Voluem;
        voluemFiled.RegisterCallback<FocusInEvent>(VoluemFiledFocusIn);
        voluemFiled.RegisterCallback<FocusOutEvent>(VoluemFiledFocusOut);
        root.Add(voluemFiled);
    }
    private void AudioClipAssetFiedlValueChanged(ChangeEvent<UnityEngine.Object> evt)
    {
        AudioClip audioClip = evt.newValue as AudioClip;
        if (trackItem.SkillAudioEvent.UseClipRange)
        {
            var data = trackItem.SkillAudioEvent;
            var copy = SkillTimelineData.Clone(new SkillEventEntry(SkillEventKind.Audio, data, trackItem.FrameIndex, 0));
            ((SkillAudioEvent)copy.Data).AudioClip = audioClip;
            if (!SkillMediaTiming.Validate(copy.Data, SkillEditorWindow.Instance.SkillConfig.FrameRote, out string reason))
            {
                ((ObjectField)evt.target).SetValueWithoutNotify(data.AudioClip);
                Debug.LogWarning(reason + " 请先关闭裁剪或调整范围。");
                return;
            }
        }
        // 保存到配置中
        SkillEditorChangeUtility.Apply("修改技能音效", () => trackItem.SkillAudioEvent.AudioClip = audioClip);
        trackItem.ResetView();
    }

    float oldVoluemFiledValue;
    private void VoluemFiledFocusIn(FocusInEvent evt)
    {
        oldVoluemFiledValue = voluemFiled.value;
    }
    private void VoluemFiledFocusOut(FocusOutEvent evt)
    {
        if (voluemFiled.value != oldVoluemFiledValue)
        {
            float volume = Mathf.Clamp01(voluemFiled.value);
            SkillEditorChangeUtility.Apply("修改技能音量", () => trackItem.SkillAudioEvent.Voluem = volume);
            voluemFiled.SetValueWithoutNotify(volume);
        }
    }
}

}
