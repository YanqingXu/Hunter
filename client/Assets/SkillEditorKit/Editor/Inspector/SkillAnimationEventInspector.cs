// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit.Editor
{
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

public class SkillAnimationEventInspector : SkillEventDataInspectorBase<AnimationTrackItem, AnimationTrack>
{
    private Label clipFrameLabel;
    private Toggle rootMotionToggle;
    private Toggle mainWeaponOnLeftHandToggle;
    private Label isLoopLable;
    private IntegerField durationField;
    private Label blendFrameLabel;

    public override void OnDraw()
    {
        // 动画资源
        ObjectField animationClipAssetField = new ObjectField("动画资源");
        animationClipAssetField.objectType = typeof(AnimationClip);
        animationClipAssetField.value = trackItem.AnimationEvent.AnimationClip;
        animationClipAssetField.RegisterValueChangedCallback(AnimationClipAssetFiedlValueChanged);
        root.Add(animationClipAssetField);

        // 根运动
        rootMotionToggle = new Toggle("应用根运动");
        rootMotionToggle.value = trackItem.AnimationEvent.ApplyRootMotion;
        rootMotionToggle.RegisterValueChangedCallback(RootMotionToggleValueChanged);
        root.Add(rootMotionToggle);

        // 主武器左右手
        mainWeaponOnLeftHandToggle = new Toggle("武器左手位置");
        mainWeaponOnLeftHandToggle.value = trackItem.AnimationEvent.MainWeaponOnLeftHand;
        mainWeaponOnLeftHandToggle.RegisterValueChangedCallback(MainWeaponOnLeftHandValueChanged);
        root.Add(mainWeaponOnLeftHandToggle);

        // 轨道长度
        durationField = new IntegerField("轨道长度");
        durationField.value = trackItem.AnimationEvent.DurationFrame;
        durationField.RegisterCallback<FocusInEvent>(DurationFieldFocusIn);
        durationField.RegisterCallback<FocusOutEvent>(DurationFieldFocusOut);
        root.Add(durationField);
        durationField.SetEnabled(!trackItem.AnimationEvent.UseClipRange);
        SkillMediaRangeInspector.Draw(root, trackItem.AnimationEvent);

        // Derived from timeline overlap; no second independently editable duration.
        blendFrameLabel = new Label { name = "AnimationBlendFramesLabel" };
        root.Add(blendFrameLabel);
        RefreshBlendInfo();
        root.Add(new HelpBox("把后一个片段向左拖，与前一个片段产生重叠即可融合。重叠多少帧就融合多少帧，两段动画在重叠区都继续播放。分开或首尾相接不会融合。按住 Alt 可暂时关闭吸附，微调重叠长度。", HelpBoxMessageType.Info));
        rootMotionToggle.tooltip = "当前片段开始后，该开关控制整个融合姿态的根运动是否应用；关闭时角色根位置保持不动。";

        // 动画相关的信息
        AnimationClip animationClip = trackItem.AnimationEvent.AnimationClip;
        int clipFrameCount = animationClip == null ? 0 : (int)(animationClip.length * animationClip.frameRate);
        clipFrameLabel = new Label("动画资源长度:" + clipFrameCount);
        root.Add(clipFrameLabel);
        isLoopLable = new Label("循环动画:" + (animationClip != null && animationClip.isLooping));
        root.Add(isLoopLable);

        // 删除
        Button deleteButton = new Button(DeleteAnimationTrackItemButtonClick);
        deleteButton.text = "删除";
        deleteButton.style.backgroundColor = new Color(1, 0, 0, 0.5f);
        root.Add(deleteButton);

        // 设置持续帧数至选中帧
        Button setFrameButton = new Button(SetAnimationDurationFrameButton);
        setFrameButton.text = "设置持续帧数至选中帧";
        root.Add(setFrameButton);
    }

    private void AnimationClipAssetFiedlValueChanged(ChangeEvent<UnityEngine.Object> evt)
    {
        AnimationClip clip = evt.newValue as AnimationClip;
        if (trackItem.AnimationEvent.UseClipRange)
        {
            // An incompatible replacement must not leave an invisible invalid trim.
            var data = trackItem.AnimationEvent;
            var copy = SkillTimelineData.Clone(new SkillEventEntry(SkillEventKind.Animation, data, trackItem.FrameIndex, 0));
            ((SkillAnimationEvent)copy.Data).AnimationClip = clip;
            if (!SkillMediaTiming.Validate(copy.Data, SkillEditorWindow.Instance.SkillConfig.FrameRote, out string reason))
            {
                ((ObjectField)evt.target).SetValueWithoutNotify(data.AnimationClip);
                Debug.LogWarning(reason + " 请先关闭裁剪或调整范围。");
                return;
            }
        }
        // 修改自身显示效果
        clipFrameLabel.text = "动画资源长度:" + (clip == null ? 0 : (int)(clip.length * clip.frameRate));
        isLoopLable.text = "循环动画:" + (clip != null && clip.isLooping);
        // 保存到配置
        SkillEditorChangeUtility.Apply("修改技能动画", () => trackItem.AnimationEvent.AnimationClip = clip);
        track.ResetView();
        SkillEditorWindow.Instance.TimelineSelection.Rebind();
        SkillEditorWindow.Instance.TickSkill();
    }

    private void RootMotionToggleValueChanged(ChangeEvent<bool> evt)
    {
        SkillEditorChangeUtility.Apply("修改技能根运动", () => trackItem.AnimationEvent.ApplyRootMotion = evt.newValue);
        SkillEditorWindow.Instance.TickSkill();
    }

    private void MainWeaponOnLeftHandValueChanged(ChangeEvent<bool> evt)
    {
        SkillEditorChangeUtility.Apply("修改技能武器挂点", () => trackItem.AnimationEvent.MainWeaponOnLeftHand = evt.newValue);
        SkillEditorWindow.Instance.TickSkill();
    }

    int oldDurationValue;
    private void DurationFieldFocusIn(FocusInEvent evt)
    {
        oldDurationValue = durationField.value;
    }
    private void DurationFieldFocusOut(FocusOutEvent evt)
    {
        if (durationField.value != oldDurationValue)
        {
            // 安全校验
            int duration = Mathf.Max(1, durationField.value);
            if (track.CheckFrameIndexOnDrag(itemFrameIndex + duration, itemFrameIndex, false))
            {
                // 修改数据，刷新视图
                SkillEditorChangeUtility.Apply("修改动画持续帧数", () =>
                {
                    trackItem.AnimationEvent.DurationFrame = duration;
                    trackItem.CheckFrameCount();
                });
                durationField.SetValueWithoutNotify(duration);
                trackItem.ResetView();
                track.ResetView();
                SkillEditorWindow.Instance.TimelineSelection.Rebind();
            }
            else
            {
                durationField.value = oldDurationValue;
            }
            SkillEditorWindow.Instance.TickSkill();
        }
    }
    private void SetAnimationDurationFrameButton()
    {
        if (trackItem.AnimationEvent.UseClipRange)
        {
            SkillMediaRangeInspector.Commit(trackItem.AnimationEvent, true, trackItem.AnimationEvent.ClipIn,
                SkillEditorWindow.Instance.CurrentSelectFrameIndex - trackItem.FrameIndex);
            return;
        }
        DurationFieldFocusIn(null);
        durationField.value = SkillEditorWindow.Instance.CurrentSelectFrameIndex - trackItem.FrameIndex;
        DurationFieldFocusOut(null);
    }

    private void RefreshBlendInfo()
    {
        int rate = SkillEditorWindow.Instance.SkillConfig.FrameRote;
        var previous = SkillAnimationTiming.Previous(track.AnimationData.FrameData, trackItem.FrameIndex);
        int frames = SkillAnimationTiming.OverlapFrames(previous.Key, previous.Value, trackItem.FrameIndex, trackItem.AnimationEvent);
        blendFrameLabel.text = frames > 0 ? $"重叠融合：{frames} 帧 / {frames / (double)System.Math.Max(1, rate):0.###} 秒（自动计算）" : "无融合：与前一个动画没有重叠";
    }

    private void DeleteAnimationTrackItemButtonClick()
    {
        SkillEditorClipboard.DeleteSelected();
    }
}

}
