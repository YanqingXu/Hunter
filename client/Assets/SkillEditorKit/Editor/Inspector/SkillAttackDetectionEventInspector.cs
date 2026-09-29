// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit.Editor
{

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

public class SkillAttackDetectionEventInspector : SkillEventDataInspectorBase<AttackDetectionTrackItem, AttackDetectionTrack>
{
    private static readonly List<string> ShapeNames = new List<string> { "无", "武器碰撞体", "盒形", "球形", "扇形 / 半圆", "地面圆形（圆柱）" };
    private static readonly List<string> MotionNames = new List<string> { "跟随起点（近战）", "直线飞行（固定发射方向）", "片段开始时固定位置" };
    private bool Is2D => SkillEditorWindow.Instance?.SkillConfig?.Space == SkillSpace.TwoD;
    private SkillAttackDetectionEvent Data => trackItem.SkillAttackDetectionEvent;

    public override void OnDraw()
    {
        var duration = new IntegerField("持续帧数") { name = "AttackDurationField", value = Data.DurationFrame, isDelayed = true };
        duration.RegisterValueChangedCallback(evt =>
        {
            int value = Mathf.Max(1, evt.newValue);
            if (Edit("修改攻击持续帧数", () => { Data.DurationFrame = value; trackItem.CheckFrameCount(); }))
            { duration.SetValueWithoutNotify(value); trackItem.ResetView(); Show(); }
            else duration.SetValueWithoutNotify(Data.DurationFrame);
        });
        root.Add(duration);
        root.Add(new Button(() => duration.value = Mathf.Max(1, SkillEditorWindow.Instance.CurrentSelectFrameIndex - trackItem.FrameIndex))
            { text = "持续到当前播放头" });
        var names = Is2D ? new List<string> { "无", "武器碰撞体（2D）", "矩形（2D）", "圆形（2D）", "扇形 / 半圆（2D）", "整圆（2D）" } : new List<string>(ShapeNames);
        var shapeField = new DropdownField("伤害范围", names, (int)Data.AttackDetectionType) { name = "AttackShapeField" };
        shapeField.RegisterValueChangedCallback(evt =>
        {
            int type = names.IndexOf(evt.newValue);
            if (type >= 0 && Edit("修改攻击范围类型", () => Data.AttackDetectionType = (AttackDetectionType)type)) Show();
        });
        root.Add(shapeField);
        if (Data.AttackDetectionData is AttackWeaponDetectionData weapon) DrawWeapon(weapon);
        else if (Data.AttackDetectionData is AttackShapeDetectionDataBase shape) DrawShape(shape);
        else root.Add(new HelpBox("请选择盒形、球形或扇形；范围默认从角色脚下向正前方展开。", HelpBoxMessageType.Info));
        DrawRules();
        DrawHit();
    }

    private void DrawWeapon(AttackWeaponDetectionData data)
    {
        var field = new DropdownField("武器选择");
        var player = SkillEditorWindow.Instance.PreviewCharacterObj?.GetComponent<SkillPlayer>();
        if (player?.WeaponDic != null) field.choices = player.WeaponDic.Keys.ToList();
        field.SetValueWithoutNotify(data.weaponName);
        field.RegisterValueChangedCallback(evt => Edit("修改攻击武器", () => data.weaponName = evt.newValue));
        root.Add(field);
    }

    private void DrawShape(AttackShapeDetectionDataBase shape)
    {
        if (!shape.UseFootOrigin)
        {
            root.Add(new HelpBox("这是旧坐标配置，原范围仍保持不变。切换后将居中朝向角色前方，取消旧的横向偏移和额外旋转，保留范围尺寸；操作可撤销。", HelpBoxMessageType.Info));
            root.Add(new Button(() =>
            {
                if (Edit("攻击范围改为脚下朝前", () => SkillAttackShape.ConvertForward(shape, SkillEditorWindow.Instance.SkillConfig.Space))) Show();
            }) { name = "AttackConvertForwardButton", text = "改为脚下朝前配置" });
            var oldDimensions = new VisualElement(); root.Add(oldDimensions);
            DrawDimensions(oldDimensions, shape); oldDimensions.SetEnabled(false);
            return;
        }
        if (Is2D) root.Add(new HelpBox("2D：X 为前进方向，Y 为世界上方；忽略 Z 尺寸及扇形高度。", HelpBoxMessageType.Info));
        DrawAnchor(shape);
        var mode = new DropdownField("放置方式", new List<string>(MotionNames), (int)shape.Motion) { name = "AttackRangeModeField" };
        mode.RegisterValueChangedCallback(evt =>
        {
            int index = MotionNames.IndexOf(evt.newValue);
            if (index >= 0 && Edit("修改攻击范围移动方式", () =>
            { shape.Motion = (AttackRangeMotion)index; shape.EndDistance = Mathf.Max(shape.StartDistance, shape.EndDistance); })) Show();
        });
        root.Add(mode);
        bool flying = SkillAttackShape.IsFlight(shape);
        bool feet = shape.Anchor == SkillRangeAnchor.CharacterFeet;
        string distanceLabel = flying ? "起始距离（米）" : feet && !(shape is AttackFanDetectionData) ? "距脚下近端距离（米）" : "圆心 / 中心前移（米）";
        Number(root, distanceLabel, "AttackStartDistanceField", shape.StartDistance, value =>
        { shape.StartDistance = value; shape.EndDistance = Mathf.Max(value, shape.EndDistance); }, 0, float.MaxValue, true);
        if (flying)
        {
            Number(root, "结束距离（米）", "AttackEndDistanceField", shape.EndDistance,
                value => shape.EndDistance = value, shape.StartDistance);
            root.Add(new HelpBox("片段开始时锁定发射位置和水平朝向，按持续帧数匀速飞到结束距离；拖动播放头即可查看途中位置。飞行到片段末尾结束，打断技能会清理。", HelpBoxMessageType.Info));
            if (shape is AttackFanDetectionData)
                root.Add(new HelpBox("扇形飞行使用分段检测。高速、细小的子弹建议使用带帧间扫掠检测的球形或盒形。", HelpBoxMessageType.Info));
        }
        Number(root, shape.Anchor == SkillRangeAnchor.Socket ? "中心上移（米）" : "离地高度（米）", "AttackHeightOffsetField", shape.HeightOffset, value => shape.HeightOffset = value, 0);
        root.Add(new HelpBox(!feet ? (shape.Anchor == SkillRangeAnchor.Socket ? (Is2D ? "挂点为范围中心；前移沿朝向，上移沿世界 Y。" : "挂点为范围中心；前移和上移均沿所选朝向。") :
            "目标或落点为范围底部中心，前移 0 表示落在该位置。") : (shape is AttackFanDetectionData
            ? "距离从角色脚下量到扇形的圆心；0 表示圆心就在脚下，180° 半圆朝向正前方。"
            : "距离从角色脚下量到范围近端；0 表示从脚边向前开始。") +
            "离地高度是范围底部到脚下的高度，默认 0。模型缩放不改变填写的米数。", HelpBoxMessageType.Info));
        if (shape.Motion == AttackRangeMotion.FixedAtStart)
            root.Add(new HelpBox("在片段开始帧锁定位置和朝向；之后角色移动或转身不改变范围，片段结束后停止检测。", HelpBoxMessageType.Info));
        DrawDimensions(root, shape);
    }

    private void DrawAnchor(AttackShapeDetectionDataBase shape)
    {
        Choice(root, "范围起点", "AttackAnchorField", new[] { "角色脚下", "武器 / 骨骼挂点", "目标位置", "指定落点" },
            (int)shape.Anchor, value => shape.Anchor = (SkillRangeAnchor)value);
        Choice(root, "范围朝向", "AttackFacingField", new[] { "角色正前方", "朝向目标", "使用挂点方向" },
            (int)shape.Facing, value => shape.Facing = (SkillRangeFacing)value);
        if (shape.Anchor == SkillRangeAnchor.Socket || shape.Facing == SkillRangeFacing.SocketForward)
        {
            var socket = new TextField("挂点路径 / 唯一名称") { name = "AttackSocketField", value = shape.SocketPath, isDelayed = true };
            socket.RegisterValueChangedCallback(e => Edit("修改范围挂点", () => shape.SocketPath = e.newValue.Trim())); root.Add(socket);
        }
        if (shape.Anchor == SkillRangeAnchor.Target || shape.Anchor == SkillRangeAnchor.AimPoint || shape.Facing == SkillRangeFacing.TowardTarget)
            root.Add(new HelpBox("在窗口顶部设置预览目标 / 指定落点；游戏运行时由发射方通过 SetAimContext 提供，缺少引用时不产生判定并提示原因。", HelpBoxMessageType.Info));
    }

    private void Choice(VisualElement parent, string label, string name, string[] choices, int value, Action<int> setter)
    {
        var field = new DropdownField(label, new List<string>(choices), Mathf.Clamp(value, 0, choices.Length - 1)) { name = name };
        field.RegisterValueChangedCallback(e =>
        {
            int index = Array.IndexOf(choices, e.newValue);
            if (index >= 0 && Edit("修改" + label, () => setter(index))) Show();
        }); parent.Add(field);
    }

    private void DrawDimensions(VisualElement parent, AttackShapeDetectionDataBase shape)
    {
        if (shape is AttackBoxDetectionData box)
        {
            if (!Is2D) Number(parent, "长（前后，米）", "AttackLengthField", box.Scale.z, value => box.Scale.z = value);
            Number(parent, "宽（左右，米）", "AttackWidthField", box.Scale.x, value => box.Scale.x = value);
            Number(parent, "高（上下，米）", "AttackHeightField", box.Scale.y, value => box.Scale.y = value);
        }
        else if (shape is AttackSphereDetectionData sphere)
            Number(parent, "半径（米）", "AttackRadiusField", sphere.Radius, value => sphere.Radius = value);
        else if (shape is AttackFanDetectionData fan)
        {
            Number(parent, "半径（米）", "AttackRadiusField", fan.Radius, value =>
            { fan.Radius = value; fan.InsideRadius = Mathf.Min(fan.InsideRadius, Mathf.Max(0, value - .01f)); }, .01f, float.MaxValue, true);
            if (!Is2D) Number(parent, "高（米）", "AttackHeightField", fan.Height, value => fan.Height = value);
            if (!fan.GroundCircle)
            {
                Number(parent, "角度（度）", "AttackAngleField", fan.Angle, value => fan.Angle = value, .1f, 360);
                var presets = new VisualElement(); presets.style.flexDirection = FlexDirection.Row;
                foreach (int angle in new[] { 90, 180 })
                    presets.Add(new Button(() => { if (Edit("设置扇形预设", () => fan.Angle = angle)) Show(); })
                        { name = "AttackFanPreset" + angle, text = angle == 180 ? "180° 半圆" : "90° 扇形" });
                parent.Add(presets);
                parent.Add(new Label("180° 为前方半圆，360° 为整圆。") { name = "AttackFanAngleHint" });
                Number(parent, "内圈留空半径（米）", "AttackInnerRadiusField", fan.InsideRadius,
                    value => fan.InsideRadius = value, 0, Mathf.Max(0, fan.Radius - .01f));
            }
            else if (!Is2D) parent.Add(new HelpBox("地面圆形是有高度的圆柱，不是球体；半径控制地面覆盖，高度控制上下命中范围。", HelpBoxMessageType.Info));
        }
    }

    private void Number(VisualElement parent, string label, string name, float current, Action<float> setter,
        float minimum = .01f, float maximum = float.MaxValue, bool rebuild = false)
    {
        var field = new FloatField(label) { name = name, value = current, isDelayed = true };
        field.RegisterValueChangedCallback(evt =>
        {
            if (float.IsNaN(evt.newValue) || float.IsInfinity(evt.newValue)) { field.SetValueWithoutNotify(current); return; }
            float value = Mathf.Clamp(evt.newValue, minimum, maximum);
            if (!Edit("修改" + label, () => setter(value))) { field.SetValueWithoutNotify(current); return; }
            current = value;
            field.SetValueWithoutNotify(value);
            if (rebuild) Show();
        });
        parent.Add(field);
    }

    private void DrawHit()
    {
        root.Add(new Label("命中效果"));
        if (Data.AttackHitConfig == null)
        {
            root.Add(new Button(() => { if (Edit("创建命中配置", () => Data.AttackHitConfig = new AttackHitConfig())) Show(); }) { text = "创建命中配置" });
            return;
        }
        var hit = Data.AttackHitConfig;
        Number(root, "攻击力系数", "AttackMultiplierField", hit.AttackMultiply, value => hit.AttackMultiply = value, 0);
        if (Is2D) Number(root, "水平击退参数（X）", "AttackRepelForwardField", hit.RepelStrength.x, value => hit.RepelStrength.x = value, float.MinValue);
        else Number(root, "向前击退强度", "AttackRepelForwardField", hit.RepelStrength.z, value => hit.RepelStrength.z = value, float.MinValue);
        Number(root, "向上击退强度", "AttackRepelUpField", hit.RepelStrength.y, value => hit.RepelStrength.y = value, float.MinValue);
        if (!Is2D) Number(root, "侧向击退强度", "AttackRepelSideField", hit.RepelStrength.x, value => hit.RepelStrength.x = value, float.MinValue);
        Number(root, "击退时间（秒）", "AttackRepelTimeField", hit.RepelTime, value => hit.RepelTime = value, 0);
        var effect = new ObjectField("命中特效") { objectType = typeof(GameObject), allowSceneObjects = false, value = hit.HitEffectPrefab };
        effect.RegisterValueChangedCallback(evt => Edit("修改命中特效", () => hit.HitEffectPrefab = evt.newValue as GameObject)); root.Add(effect);
        var audio = new ObjectField("命中音效") { objectType = typeof(AudioClip), allowSceneObjects = false, value = hit.HitAudioClip };
        audio.RegisterValueChangedCallback(evt => Edit("修改命中音效", () => hit.HitAudioClip = evt.newValue as AudioClip)); root.Add(audio);
    }

    private void DrawRules()
    {
        var fold = new Foldout { name = "AttackHitRulesFoldout", text = "命中规则（展开配置）", value = SessionState.GetBool("SkillEditorKit.HitRulesExpanded", false) };
        fold.RegisterValueChangedCallback(e => SessionState.SetBool("SkillEditorKit.HitRulesExpanded", e.newValue)); root.Add(fold);
        var rules = Data.HitRules;
        if (rules == null)
        {
            fold.Add(new Button(() => { if (Edit("创建命中规则", () => Data.HitRules = new SkillHitRules())) Show(); }) { text = "配置命中规则" }); return;
        }
        Choice(fold, "命中方式", "AttackHitModeField", new[] { "旧行为：整次技能一次", "每段攻击命中一次", "每隔若干秒命中一次" },
            (int)rules.Mode, value => rules.Mode = (SkillHitMode)value);
        if (rules.Mode == SkillHitMode.LegacyOncePerSkill)
        { fold.Add(new HelpBox("保持旧技能去重及目标筛选，不增加伤害次数。选择其他命中方式后，才启用下方的新规则。", HelpBoxMessageType.Info)); return; }
        Choice(fold, "目标", "AttackTargetGroupField", new[] { "敌方", "友方", "全部" }, (int)rules.Targets, value => rules.Targets = (SkillTargetGroup)value);
        fold.Add(new HelpBox("仍受角色的攻击检测 LayerMask 限制；敌我由 SkillTeamId 判定，未提供阵营的目标只参与“全部”检测。", HelpBoxMessageType.Info));
        var self = new Toggle("排除自己") { name = "AttackExcludeSelfField", value = rules.ExcludeSelf };
        self.RegisterValueChangedCallback(e => Edit("修改自身排除", () => rules.ExcludeSelf = e.newValue)); fold.Add(self);
        if (rules.Mode == SkillHitMode.Interval)
            Number(fold, "重复命中间隔（秒）", "AttackHitIntervalField", rules.IntervalSeconds, value => rules.IntervalSeconds = value);
        var maximum = new IntegerField("最多目标数（0 不限）") { name = "AttackMaxTargetsField", value = rules.MaxTargets, isDelayed = true };
        maximum.RegisterValueChangedCallback(e =>
        { int n = Mathf.Max(0, e.newValue); if (Edit("修改目标上限", () => rules.MaxTargets = n)) maximum.SetValueWithoutNotify(n); }); fold.Add(maximum);
        var walls = new Toggle("被墙壁阻挡") { name = "AttackWallsField", value = rules.BlockedByWalls };
        walls.RegisterValueChangedCallback(e => { if (Edit("修改墙壁阻挡", () => rules.BlockedByWalls = e.newValue)) Show(); }); fold.Add(walls);
        if (rules.BlockedByWalls)
        {
            var mask = new LayerMaskField("墙壁所在层", rules.WallLayers.value) { name = "AttackWallLayersField" };
            mask.RegisterValueChangedCallback(e => Edit("修改阻挡层", () => rules.WallLayers = e.newValue)); fold.Add(mask);
        }
    }

    private bool Edit(string name, Action action)
    {
        if (track.Model?.Locked == true || SkillEditorWindow.Instance?.SkillConfig == null ||
            !SkillTimelineData.Read(SkillEditorWindow.Instance.SkillConfig).Any(e => ReferenceEquals(e.Data, Data))) return false;
        if (!SkillEditorChangeUtility.Apply(name, () =>
        {
            action();
            var shape = Data.AttackDetectionData as AttackShapeDetectionDataBase;
            if ((Data.HitRules != null && Data.HitRules.Mode != SkillHitMode.LegacyOncePerSkill) ||
                (shape?.UseFootOrigin == true && (shape.Anchor != SkillRangeAnchor.CharacterFeet || shape.Facing != SkillRangeFacing.CharacterForward ||
                    shape.Motion == AttackRangeMotion.FixedAtStart || (shape is AttackFanDetectionData fan && fan.GroundCircle))))
                SkillEditorWindow.Instance.SkillConfig.DataVersion = SkillClip.CurrentDataVersion;
        })) return false;
        SkillEditorWindow.Instance?.TickSkill(); SceneView.RepaintAll(); return true;
    }
    private static void Show() => SkillEditorInspector.Instance?.Show();
}

}
