// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit.Editor
{
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

public sealed class SkillCombatValidationWindow : EditorWindow
{
    [SerializeField] private SkillCombatValidation.Settings settings = new SkillCombatValidation.Settings();
    [SerializeField] private SkillEditorWindow editor;
    private SkillCombatValidation.Report report;
    private Vector2 scroll;
    private string settingsSnapshot;
    private int sourceRevision;
    private bool details = true;

    public static void Open(SkillEditorWindow owner, SkillClip skill, GameObject prefab)
    {
        var window = GetWindow<SkillCombatValidationWindow>("独立命中测试");
        window.minSize = new Vector2(470, 440); window.editor = owner;
        if (window.settings.Skill == null || window.settings.Skill.Space != skill.Space)
        {
            window.settings.Targets = new System.Collections.Generic.List<SkillCombatValidation.Target> { new SkillCombatValidation.Target {
                Position = skill.Space == SkillSpace.TwoD ? new Vector3(3,1,0) : new Vector3(0,1,3) } };
            window.settings.WallPosition = skill.Space == SkillSpace.TwoD ? new Vector3(2,1,0) : new Vector3(0,1,2);
            window.settings.WallSize = skill.Space == SkillSpace.TwoD ? new Vector3(.3f,3,1) : new Vector3(4,2,.3f);
            window.settings.AimPoint = skill.Space == SkillSpace.TwoD ? new Vector3(5,1,0) : new Vector3(0,0,5);
        }
        window.settings.Skill = skill; window.settings.ActorPrefab = prefab;
        if (prefab != null && prefab.TryGetComponent<SkillPlayer>(out var player)) window.settings.AttackLayers = player.attackDetectionLayer;
        for (int layer = 0; layer < 32; layer++)
            if ((window.settings.AttackLayers.value & (1 << layer)) != 0) { window.settings.TargetLayer = layer; break; }
        window.report = null; window.Show();
    }
    private void OnGUI()
    {
        if (settings == null) settings = new SkillCombatValidation.Settings();
        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.LabelField("独立命中测试", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("在私有物理场景中施放一次技能，不进入 Play Mode，不修改正式场景。2D 使用 XY 平面，默认向右；3D 使用 +Z 方向。目标位置使用测试场景坐标。", MessageType.Info);
        settings.Skill = (SkillClip)EditorGUILayout.ObjectField("技能", settings.Skill, typeof(SkillClip), false);
        settings.ActorPrefab = (GameObject)EditorGUILayout.ObjectField("角色预制体", settings.ActorPrefab, typeof(GameObject), false);
        EditorGUILayout.LabelField("包含动画/挂点的技能须提供角色预制体。", EditorStyles.wordWrappedMiniLabel);
        settings.BaseAttack = EditorGUILayout.FloatField("基础攻击（理论伤害）", settings.BaseAttack);
        settings.OwnerTeam = EditorGUILayout.IntField("施法者阵营", settings.OwnerTeam);
        var names = Enumerable.Range(0, 32).Select(i => i + " · " + (string.IsNullOrEmpty(LayerMask.LayerToName(i)) ? "未命名" : LayerMask.LayerToName(i))).ToArray();
        settings.AttackLayers = EditorGUILayout.MaskField("测试攻击检测层", settings.AttackLayers, names);
        EditorGUILayout.LabelField("检测层仅用于本次测试，不回写角色；更换角色后请核对。", EditorStyles.wordWrappedMiniLabel);
        settings.TargetLayer = EditorGUILayout.LayerField("目标所在层", settings.TargetLayer);
        if (settings.Skill != null && settings.Skill.Space == SkillSpace.TwoD) EditorGUILayout.HelpBox("2D 碰撞忽略 Z；矩形使用尺寸 X/Y。目标放在 (3, 1, 0) 可测试向右发射。", MessageType.None);
        settings.AimPoint = EditorGUILayout.Vector3Field("指定落点", settings.AimPoint);
        EditorGUILayout.Space(); EditorGUILayout.LabelField("测试目标（首个目标用于追踪）", EditorStyles.boldLabel);
        for (int i = 0; i < settings.Targets.Count; i++)
        {
            var target = settings.Targets[i];
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal(); EditorGUILayout.LabelField("目标 " + (i + 1), EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(settings.Targets.Count == 1))
                if (GUILayout.Button("移除", GUILayout.Width(48))) { settings.Targets.RemoveAt(i); EditorGUILayout.EndHorizontal(); EditorGUILayout.EndVertical(); break; }
            EditorGUILayout.EndHorizontal();
            target.Position = EditorGUILayout.Vector3Field("碰撞盒中心", target.Position);
            target.Size = EditorGUILayout.Vector3Field("碰撞盒尺寸", target.Size);
            target.Velocity = EditorGUILayout.Vector3Field("移动速度（米/秒）", target.Velocity);
            target.Team = EditorGUILayout.IntField("阵营", target.Team);
            EditorGUILayout.EndVertical();
        }
        using (new EditorGUI.DisabledScope(settings.Targets.Count >= 12))
            if (GUILayout.Button("添加目标")) settings.Targets.Add(new SkillCombatValidation.Target { Position = settings.Skill != null && settings.Skill.Space == SkillSpace.TwoD ? new Vector3(3 + settings.Targets.Count * 2,1,0) : new Vector3(0, 1, 3 + settings.Targets.Count * 2) });
        settings.Wall = EditorGUILayout.Toggle("添加阻挡墙", settings.Wall);
        if (settings.Wall)
        {
            settings.WallPosition = EditorGUILayout.Vector3Field("墙中心", settings.WallPosition);
            settings.WallSize = EditorGUILayout.Vector3Field("墙尺寸", settings.WallSize);
            settings.WallLayer = EditorGUILayout.LayerField("墙所在层", settings.WallLayer);
            EditorGUILayout.HelpBox("墙层还须包含在技能的墙壁阻挡层中；否则它不会阻挡该技能。", MessageType.None);
        }
        settings.ExpectedHits = EditorGUILayout.IntField("预期命中次数（-1 不断言）", settings.ExpectedHits);
        using (new EditorGUI.DisabledScope(settings.Skill == null || EditorApplication.isPlayingOrWillChangePlaymode))
            if (GUILayout.Button("运行独立测试", GUILayout.Height(30)))
            {
                editor?.EndPreview();
                report = SkillCombatValidation.Run(settings);
                settingsSnapshot = JsonUtility.ToJson(settings); sourceRevision = SkillEditorChangeUtility.Revision(settings.Skill);
                GUI.FocusControl(null);
            }
        if (report != null)
        {
            EditorGUILayout.Space(); EditorGUILayout.LabelField("本次结果", EditorStyles.boldLabel);
            if (settingsSnapshot != JsonUtility.ToJson(settings) || sourceRevision != SkillEditorChangeUtility.Revision(settings.Skill))
                EditorGUILayout.HelpBox("配置或测试参数已变化；以下为旧结果，请重新运行。", MessageType.Warning);
            if (!report.Complete) EditorGUILayout.HelpBox("未完成：" + report.Error, MessageType.Error);
            EditorGUILayout.LabelField((report.Complete ? "执行完成" : "执行中断") + " · 命中 " + report.Hits + " 次 · 理论总伤害 " + report.Damage.ToString("0.###") + " · 投射物停止 " + report.Stops + " 次", EditorStyles.wordWrappedLabel);
            if (report.ExpectedMatched.HasValue) EditorGUILayout.HelpBox(report.ExpectedMatched.Value ? "命中次数符合预期（不代表完整战斗验收）。" : "命中次数与预期不符。", report.ExpectedMatched.Value ? MessageType.Info : MessageType.Warning);
            foreach (string warning in report.Warnings) EditorGUILayout.HelpBox(warning, MessageType.Warning);
            details = EditorGUILayout.Foldout(details, "命中与投射物停止记录", true);
            if (details)
            {
                foreach (var entry in report.Entries.Take(200))
                {
                    string id = string.IsNullOrEmpty(entry.EventId) ? "旧片段" : entry.EventId.Substring(0, Math.Min(8, entry.EventId.Length));
                    string projectile = string.IsNullOrEmpty(entry.ProjectileId) ? "" : " · 弹 " + entry.ProjectileId.Substring(0, 6);
                    EditorGUILayout.SelectableLabel("帧 " + entry.Frame + " · " + id + projectile + " · " + entry.Result +
                        (entry.Target == null ? "" : " · " + entry.Target + " · 伤害 " + entry.Damage.ToString("0.###")), EditorStyles.wordWrappedMiniLabel, GUILayout.Height(30));
                }
                if (report.Entries.Count > 200) EditorGUILayout.HelpBox("仅显示前 200 条明细；上方总次数仍包含全部命中。建议缩短测试或减少目标。", MessageType.Info);
            }
        }
        EditorGUILayout.EndScrollView();
    }
}

}
