// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit.Editor
{
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

public class SkillEffectEventInspector : SkillEventDataInspectorBase<EffectTrackItem, EffectTrack>
{
    private IntegerField effectDurationFiled;
    public override void OnDraw()
    {
        // 预制体
        ObjectField effectPrefabAssetField = new ObjectField("特效预制体");
        effectPrefabAssetField.objectType = typeof(GameObject);
        effectPrefabAssetField.value = trackItem.SkillEffectEvent.Prefab;
        effectPrefabAssetField.RegisterValueChangedCallback(EffectPrefabAssetFiedlValueChanged);
        root.Add(effectPrefabAssetField);

        // 坐标
        Vector3Field posFiled = new Vector3Field("坐标");
        posFiled.value = trackItem.SkillEffectEvent.Position;
        posFiled.RegisterValueChangedCallback(EffectPosFiledValueChanged);
        root.Add(posFiled);

        // 旋转
        Vector3Field rotFiled = new Vector3Field("旋转");
        rotFiled.value = trackItem.SkillEffectEvent.Rotation;
        rotFiled.RegisterValueChangedCallback(EffectRotFiledValueChanged);
        root.Add(rotFiled);

        // 旋转
        Vector3Field scaleFiled = new Vector3Field("缩放");
        scaleFiled.value = trackItem.SkillEffectEvent.Scale;
        scaleFiled.RegisterValueChangedCallback(EffectScaleFiledValueChanged);
        root.Add(scaleFiled);

        // 自动销毁
        Toggle autoDestructToggle = new Toggle("自动销毁");
        autoDestructToggle.value = trackItem.SkillEffectEvent.AutoDestruct;
        autoDestructToggle.RegisterValueChangedCallback(EffectAutoDestructToggleValueChanged);
        root.Add(autoDestructToggle);

        // 时间
        effectDurationFiled = new IntegerField("持续时间");
        effectDurationFiled.value = trackItem.SkillEffectEvent.Duration;
        effectDurationFiled.RegisterCallback<FocusInEvent>(EffectDurationFiledFocusIn);
        effectDurationFiled.RegisterCallback<FocusOutEvent>(EffectDurationFiledFocusOut);
        root.Add(effectDurationFiled);

        // 时间计算按钮
        Button calculateEffectButton = new Button(CalculateEffectDuration);
        calculateEffectButton.text = "重新计时";
        root.Add(calculateEffectButton);

        // 引用模型Transform属性
        Button applyModelTransformDataButton = new Button(ApplyModelTransformData);
        applyModelTransformDataButton.text = "引用模型Transform属性";
        root.Add(applyModelTransformDataButton);

        // 设置持续帧数至选中帧
        Button setFrameButton = new Button(SetEffectDurationFrameButton);
        setFrameButton.text = "设置持续帧数至选中帧";
        root.Add(setFrameButton);
    }
    private void ApplyModelTransformData()
    {
        EffectTrackItem effectTrackItem = trackItem;
        SkillEditorChangeUtility.Apply("应用特效预览变换", effectTrackItem.ApplyModelTransformData);
        SkillEditorInspector.Instance?.Show();
    }

    private void CalculateEffectDuration()
    {
        EffectTrackItem effectTrackItem = trackItem;
        GameObject prefab = effectTrackItem.SkillEffectEvent.Prefab;
        if (prefab == null)
        {
            effectDurationFiled.SetValueWithoutNotify(1);
            SkillEditorChangeUtility.Apply("重算特效持续帧数", () => effectTrackItem.SkillEffectEvent.Duration = 1);
            effectTrackItem.ResetView();
            return;
        }
        ParticleSystem[] particleSystems = prefab.GetComponentsInChildren<ParticleSystem>(true);

        float max = -1;
        int curr = -1;
        for (int i = 0; i < particleSystems.Length; i++)
        {
            if (particleSystems[i].main.duration > max)
            {
                max = particleSystems[i].main.duration;
                curr = i;
            }
        }

        int frameRate = Mathf.Max(1, SkillEditorWindow.Instance.SkillConfig.FrameRote);
        int duration = curr < 0 ? 1 : Mathf.Max(1, Mathf.CeilToInt(particleSystems[curr].main.duration * frameRate));
        SkillEditorChangeUtility.Apply("重算特效持续帧数", () => effectTrackItem.SkillEffectEvent.Duration = duration);
        effectDurationFiled.SetValueWithoutNotify(duration);
        effectTrackItem.ResetView();
    }

    private void EffectPrefabAssetFiedlValueChanged(ChangeEvent<UnityEngine.Object> evt)
    {
        EffectTrackItem effectTrackItem = trackItem;
        SkillEditorChangeUtility.Apply("修改技能特效", () => effectTrackItem.SkillEffectEvent.Prefab = evt.newValue as GameObject);
        // 重新计时
        CalculateEffectDuration();
        effectTrackItem.ResetView();
        SkillEditorWindow.Instance.TickSkill();
    }

    private void EffectPosFiledValueChanged(ChangeEvent<Vector3> evt)
    {
        EffectTrackItem effectTrackItem = trackItem;
        SkillEditorChangeUtility.Apply("修改特效位置", () => effectTrackItem.SkillEffectEvent.Position = evt.newValue);
        effectTrackItem.ResetView();
        SkillEditorWindow.Instance.TickSkill();
    }

    private void EffectRotFiledValueChanged(ChangeEvent<Vector3> evt)
    {
        EffectTrackItem effectTrackItem = trackItem;
        SkillEditorChangeUtility.Apply("修改特效旋转", () => effectTrackItem.SkillEffectEvent.Rotation = evt.newValue);
        effectTrackItem.ResetView();
        SkillEditorWindow.Instance.TickSkill();
    }

    private void EffectScaleFiledValueChanged(ChangeEvent<Vector3> evt)
    {
        EffectTrackItem effectTrackItem = trackItem;
        SkillEditorChangeUtility.Apply("修改特效缩放", () => effectTrackItem.SkillEffectEvent.Scale = evt.newValue);
        effectTrackItem.ResetView();
        SkillEditorWindow.Instance.TickSkill();
    }

    private void EffectAutoDestructToggleValueChanged(ChangeEvent<bool> evt)
    {
        EffectTrackItem effectTrackItem = trackItem;
        SkillEditorChangeUtility.Apply("修改特效自动回收", () => effectTrackItem.SkillEffectEvent.AutoDestruct = evt.newValue);
    }

    float oldEffectDurationFiled;
    private void EffectDurationFiledFocusIn(FocusInEvent evt)
    {
        oldEffectDurationFiled = effectDurationFiled.value;
    }
    private void EffectDurationFiledFocusOut(FocusOutEvent evt)
    {
        if (effectDurationFiled.value != oldEffectDurationFiled)
        {
            EffectTrackItem effectTrackItem = trackItem;
            int duration = Mathf.Max(1, effectDurationFiled.value);
            SkillEditorChangeUtility.Apply("修改特效持续帧数", () => effectTrackItem.SkillEffectEvent.Duration = duration);
            effectDurationFiled.SetValueWithoutNotify(duration);
            effectTrackItem.ResetView();
            SkillEditorWindow.Instance.TickSkill();
        }
    }

    private void SetEffectDurationFrameButton()
    {
        EffectDurationFiledFocusIn(null);
        effectDurationFiled.value = SkillEditorWindow.Instance.CurrentSelectFrameIndex - trackItem.FrameIndex;
        EffectDurationFiledFocusOut(null);
    }
}

}
