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

public sealed class SkillProjectileEventInspector : SkillEventDataInspectorBase<ProjectileTrackItem, ProjectileTrack>
{
    private SkillProjectileEvent Data => trackItem.Event;
    public override void OnDraw()
    {
        var launch = Group("发射", true);
        if (SkillEditorWindow.Instance.SkillConfig.Space == SkillSpace.TwoD) launch.Add(new HelpBox("2D 子弹沿 XY 平面飞行。默认沿 +X；挂点的红色 X 轴为枪口方向，散射围绕 Z 轴。SpriteRenderer.flipX 不移动枪口挂点，使用挂点时请同步镜像挂点父物体。", HelpBoxMessageType.Info));
        Asset(launch, "子弹外观预制体", "ProjectilePrefabField", typeof(GameObject), Data.Prefab, value => Data.Prefab = value as GameObject);
        launch.Add(new HelpBox("预制体只提供外观：模型与判定使用同一位置，预制体自带碰撞体和移动脚本不参与飞行。未指定外观时仍会产生伤害判定。", HelpBoxMessageType.Info));
        Choice(launch, "发射起点", "ProjectileAnchorField", new[] { "角色脚下", "武器 / 骨骼挂点", "目标位置", "指定落点" }, (int)Data.Anchor,
            value => { Data.Anchor = (SkillRangeAnchor)value; if (Data.Anchor == SkillRangeAnchor.Socket) Data.LaunchHeight = 0; });
        Choice(launch, "发射方向", "ProjectileFacingField", new[] { "角色正前方", "朝向目标", "使用挂点方向" }, (int)Data.Facing,
            value => Data.Facing = (SkillRangeFacing)value);
        if (Data.Anchor == SkillRangeAnchor.Socket || Data.Facing == SkillRangeFacing.SocketForward)
        {
            var path = new TextField("挂点路径 / 唯一名称") { name = "ProjectileSocketField", value = Data.SocketPath, isDelayed = true };
            path.RegisterValueChangedCallback(e => Edit("修改发射挂点", () => Data.SocketPath = e.newValue.Trim())); launch.Add(path);
        }
        Number(launch, "起点上移（米）", "ProjectileHeightField", Data.LaunchHeight, value => Data.LaunchHeight = value, 0);
        Number(launch, "起点前移（米）", "ProjectileForwardField", Data.LaunchForward, value => Data.LaunchForward = value, 0);
        Integer(launch, "发射数量", "ProjectileCountField", Data.Count, value => Data.Count = value, 1, 64);
        Number(launch, "散射角（度）", "ProjectileSpreadField", Data.SpreadAngle, value => Data.SpreadAngle = value, 0, 180);

        var flight = Group("飞行", true);
        Choice(flight, "轨迹", "ProjectileFlightField", new[] { "直线", "抛物线", "追踪" }, (int)Data.Flight, value => Data.Flight = (SkillProjectileFlight)value);
        Choice(flight, "配置方式", "ProjectileTimingField", new[] { "距离 + 飞行时长", "速度 + 最大距离" }, (int)Data.Timing, value => Data.Timing = (SkillProjectileTiming)value);
        Number(flight, Data.Flight == SkillProjectileFlight.Arc ? "前向射程（米）" : "最大距离（米）", "ProjectileDistanceField", Data.Distance, value => Data.Distance = value);
        if (Data.Timing == SkillProjectileTiming.DistanceAndTime)
            Integer(flight, "飞行时长（技能帧）", "ProjectileFlightFramesField", Data.FlightFrames, value => Data.FlightFrames = value, 1, 1000000);
        else Number(flight, "速度（米 / 秒）", "ProjectileSpeedField", Data.Speed, value => Data.Speed = value);
        int rate = Math.Max(1, SkillEditorWindow.Instance.SkillConfig.FrameRote);
        double seconds = Data.FlightSeconds(rate);
        flight.Add(new Label($"预计 {seconds:0.##} 秒 · {Math.Ceiling(seconds * rate):0} 帧 · 前向 / 路程速度 {Data.Distance / seconds:0.##} 米/秒") { name = "ProjectileTimingSummary" });
        if (Data.Flight == SkillProjectileFlight.Arc)
        {
            Number(flight, "弧顶抬高（米）", "ProjectileArcHeightField", Data.ArcHeight, value => Data.ArcHeight = value, 0, 1000);
            flight.Add(new HelpBox("抛物线沿发射方向到达射程终点，中点达到所填抬高量；速度表示前向进度，空中实际速度随弧线变化。", HelpBoxMessageType.Info));
        }
        if (Data.Flight == SkillProjectileFlight.Homing)
        {
            Number(flight, "转向速度（度 / 秒）", "ProjectileTurnField", Data.TurnDegreesPerSecond, value => Data.TurnDegreesPerSecond = value);
            flight.Add(new HelpBox("需要预览目标；运行时由 SetAimContext 提供。目标丢失后沿最后方向继续飞行。预览按静止目标、固定 60 Hz 重放，不预测目标未来移动或实际碰撞。", HelpBoxMessageType.Info));
        }

        var collision = Group("碰撞与伤害", true);
        Number(collision, SkillEditorWindow.Instance.SkillConfig.Space == SkillSpace.TwoD ? "判定圆半径（米）" : "判定球半径（米）", "ProjectileRadiusField", Data.Radius, value => Data.Radius = value);
        Integer(collision, "额外穿透目标数", "ProjectilePierceField", Data.PierceCount, value => Data.PierceCount = value, 0, 1000);
        collision.Add(new Label("0 表示命中第一个有效目标即消失；同一目标多个碰撞体只计算一次。"));
        if (Data.HitRules == null)
            collision.Add(new Button(() => Edit("修复投射物命中规则", () => Data.HitRules = new SkillHitRules { Mode = SkillHitMode.OncePerAttack }, true)) { text = "创建命中规则" });
        else
        {
            Choice(collision, "目标", "ProjectileTargetsField", new[] { "敌方", "友方", "全部" }, (int)Data.HitRules.Targets, value => Data.HitRules.Targets = (SkillTargetGroup)value);
            Toggle(collision, "排除自己", "ProjectileExcludeSelfField", Data.HitRules.ExcludeSelf, value => Data.HitRules.ExcludeSelf = value);
            collision.Add(new HelpBox("目标还受角色的攻击 LayerMask 限制；敌我由 SkillTeamId 判定，未知阵营仅参与“全部”。", HelpBoxMessageType.Info));
        }
        Toggle(collision, "撞墙消失", "ProjectileWallsField", Data.StopAtWalls, value => Data.StopAtWalls = value, true);
        if (Data.StopAtWalls)
        {
            var layers = new LayerMaskField("墙壁所在层", Data.WallLayers.value) { name = "ProjectileWallLayersField" };
            layers.RegisterValueChangedCallback(e => Edit("修改投射物阻挡层", () => Data.WallLayers = e.newValue)); collision.Add(layers);
            if (Data.WallLayers.value == 0) collision.Add(new HelpBox("没有选择墙壁层，当前不会被墙壁阻挡。", HelpBoxMessageType.Warning));
        }
        if (Data.Hit != null)
        {
            Number(collision, "攻击力系数", "ProjectileDamageField", Data.Hit.AttackMultiply, value => Data.Hit.AttackMultiply = value, 0);
            Asset(collision, "命中特效", "ProjectileHitEffectField", typeof(GameObject), Data.Hit.HitEffectPrefab, value => Data.Hit.HitEffectPrefab = value as GameObject);
            if (Data.Hit.HitEffectPrefab != null) Number(collision, "命中特效寿命（秒）", "ProjectileHitFxLifetimeField", Data.HitEffectLifetime, value => Data.HitEffectLifetime = value, .01f, 60);
            Asset(collision, "命中音效", "ProjectileHitAudioField", typeof(AudioClip), Data.Hit.HitAudioClip, value => Data.Hit.HitAudioClip = value as AudioClip);
        }

        var end = Group("结束与清理", true);
        Number(end, "存活上限（秒）", "ProjectileLifetimeField", Data.MaxLifetime, value => Data.MaxLifetime = value, .01f, 60);
        Choice(end, "技能正常结束", "ProjectileOnEndField", new[] { "继续飞行", "立即销毁" }, (int)Data.OnSkillEnd, value => Data.OnSkillEnd = (SkillProjectileEndPolicy)value);
        Choice(end, "技能被打断", "ProjectileOnCancelField", new[] { "继续飞行", "立即销毁" }, (int)Data.OnSkillCancel, value => Data.OnSkillCancel = (SkillProjectileEndPolicy)value);
        Asset(end, "消失特效（可选）", "ProjectileEndEffectField", typeof(GameObject), Data.EndEffectPrefab, value => Data.EndEffectPrefab = value as GameObject);
        if (Data.EndEffectPrefab != null) Number(end, "消失特效寿命（秒）", "ProjectileEndFxLifetimeField", Data.EndEffectLifetime, value => Data.EndEffectLifetime = value, .01f, 60);
        end.Add(new HelpBox("到达距离、存活上限、穿透上限或撞墙后消失。正常命中 / 飞完播放消失特效；打断清理不播放。切换场景或退出预览会清理临时对象。", HelpBoxMessageType.Info));
        if (!SkillProjectileEvent.Validate(Data, rate, out string error)) root.Add(new HelpBox(error, HelpBoxMessageType.Error));
    }

    private Foldout Group(string label, bool expanded)
    { var group = new Foldout { text = label, value = expanded }; root.Add(group); return group; }
    private void Number(VisualElement parent, string label, string name, float current, Action<float> setter, float min = .01f, float max = 1000000)
    {
        var field = new FloatField(label) { name = name, value = current, isDelayed = true };
        field.RegisterValueChangedCallback(e =>
        {
            if (float.IsNaN(e.newValue) || float.IsInfinity(e.newValue)) { field.SetValueWithoutNotify(current); return; }
            float next = Mathf.Clamp(e.newValue, min, max);
            if (Edit("修改" + label, () => setter(next))) { current = next; field.SetValueWithoutNotify(next); }
            else field.SetValueWithoutNotify(current);
        }); parent.Add(field);
    }
    private void Integer(VisualElement parent, string label, string name, int current, Action<int> setter, int min, int max)
    {
        var field = new IntegerField(label) { name = name, value = current, isDelayed = true };
        field.RegisterValueChangedCallback(e =>
        {
            int next = Mathf.Clamp(e.newValue, min, max);
            if (Edit("修改" + label, () => setter(next))) { current = next; field.SetValueWithoutNotify(next); }
            else field.SetValueWithoutNotify(current);
        }); parent.Add(field);
    }
    private void Choice(VisualElement parent, string label, string name, string[] choices, int value, Action<int> setter)
    {
        var field = new DropdownField(label, new List<string>(choices), Mathf.Clamp(value, 0, choices.Length - 1)) { name = name };
        field.RegisterValueChangedCallback(e => { int i = Array.IndexOf(choices, e.newValue); if (i >= 0) Edit("修改" + label, () => setter(i), true); }); parent.Add(field);
    }
    private void Toggle(VisualElement parent, string label, string name, bool current, Action<bool> setter, bool rebuild = false)
    {
        var field = new Toggle(label) { name = name, value = current };
        field.RegisterValueChangedCallback(e => { if (Edit("修改" + label, () => setter(e.newValue), rebuild)) current = e.newValue; else field.SetValueWithoutNotify(current); }); parent.Add(field);
    }
    private void Asset(VisualElement parent, string label, string name, Type type, UnityEngine.Object current, Action<UnityEngine.Object> setter)
    {
        var field = new ObjectField(label) { name = name, objectType = type, allowSceneObjects = false, value = current };
        field.RegisterValueChangedCallback(e => Edit("修改" + label, () => setter(e.newValue), true)); parent.Add(field);
    }
    private bool Edit(string label, Action setter, bool rebuild = false)
    {
        var window = SkillEditorWindow.Instance;
        if (track.Model?.Locked == true || window?.SkillConfig == null || !SkillTimelineData.Read(window.SkillConfig).Any(e => ReferenceEquals(e.Data, Data))) return false;
        if (!SkillEditorChangeUtility.Apply(label, () => { setter(); window.SkillConfig.DataVersion = SkillClip.CurrentDataVersion; })) return false;
        if (rebuild) window.RefreshAfterDataChange(window.CurrentSelectFrameIndex, Data);
        else { trackItem.ResetView(); window.CurrentFrameCount = window.SkillConfig.FrameCount; window.TickSkill(); }
        SkillEditorInspector.Instance?.Show(); SceneView.RepaintAll(); return true;
    }
}

}
