// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit.Editor
{
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Bounded deterministic test using the runtime player in a private physics scene.</summary>
public static class SkillCombatValidation
{
    [Serializable] public sealed class Target
    {
        public Vector3 Position = new Vector3(0, 1, 3);
        public Vector3 Velocity;
        public Vector3 Size = new Vector3(.6f, 2, .6f);
        public int Team = 2;
    }
    [Serializable] public sealed class Settings
    {
        public SkillClip Skill;
        public GameObject ActorPrefab;
        public List<Target> Targets = new List<Target> { new Target() };
        public int OwnerTeam = 1, TargetLayer = 30, WallLayer;
        public LayerMask AttackLayers = 1 << 30;
        public float BaseAttack = 10;
        public bool Wall;
        public Vector3 WallPosition = new Vector3(0, 1, 2), WallSize = new Vector3(4, 2, .3f);
        public Vector3 AimPoint = new Vector3(0, 0, 5);
        public int ExpectedHits = -1;
    }
    public sealed class Entry
    {
        public int Frame;
        public string EventId, Target, ProjectileId, Result;
        public float Damage;
    }
    public sealed class Report
    {
        public readonly List<Entry> Entries = new List<Entry>();
        public readonly List<string> Warnings = new List<string>();
        public int Hits, Stops, LastSampleFrame;
        public float Damage;
        public string Error;
        public bool Complete;
        public bool? ExpectedMatched;
    }
    private sealed class Behaviour : SkillBehaviourBase
    {
        public Action<int> Sample;
        public SkillInstance Execution;
        public Action<SkillProjectileContext, SkillProjectileStopReason> Stopped;
        public override SkillBehaviourBase DeepCopy() => new Behaviour();
        public override void OnTickSkill(int frame) { Execution = CurrentInstance; Sample(frame); }
        public override SkillCustomEvent BeforeSkillCustomEvent(SkillCustomEvent data) => null;
        public override SkillAudioEvent BeforeSkillAudioEvent(SkillAudioEvent data) => null;
        public override SkillEffectEvent BeforeSkillEffectEvent(SkillEffectEvent data) => null;
        public override SkillAttackDetectionEvent BeforeSkillAttackDetectionEvent(SkillAttackDetectionEvent data)
            => data.GetAttackDetectionType() == AttackDetectionType.Weapon ? null : data;
        public override SkillProjectileEvent BeforeSkillProjectileEvent(SkillProjectileEvent data)
        {
            var copy = data.Copy(); copy.Prefab = null; copy.EndEffectPrefab = null;
            copy.Hit.HitEffectPrefab = null; copy.Hit.HitAudioClip = null; return copy;
        }
        public override void OnHitTarget(ISkillHitTarget target, SkillHitData data) => target.BeHit(data);
        public override void OnProjectileHitTarget(ISkillHitTarget target, SkillHitData data) => target.BeHit(data);
        public override void OnProjectileStopped(SkillProjectileContext context, SkillProjectileStopReason reason, Vector3 position)
            => Stopped(context, reason);
    }

    public static Report Run(Settings settings)
    {
        var report = new Report(); Scene scene = default;
        SkillPreviewSession preview = null; SkillPlayer player = null; GameObject actor = null;
        try
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || AnimationMode.InAnimationMode())
                throw new InvalidOperationException("请先停止表现预览，并退出运行模式，再运行独立命中测试。");
            int last = Validate(settings, report);
            scene = EditorSceneManager.NewPreviewScene();
            var physics = scene.GetPhysicsScene();
            var physics2D = scene.GetPhysicsScene2D();
            bool is2D = settings.Skill.Space == SkillSpace.TwoD;
            if (is2D && (!physics2D.IsValid() || physics2D.Equals(Physics2D.defaultPhysicsScene))) throw new InvalidOperationException("无法建立独立 2D 物理场景。");
            if (!physics.IsValid() || physics.Equals(Physics.defaultPhysicsScene)) throw new InvalidOperationException("无法建立独立物理场景。");
            actor = settings.ActorPrefab == null ? new GameObject("测试施法者") : SkillPreviewObjects.CreateCharacter(settings.ActorPrefab);
            foreach (var tr in actor.GetComponentsInChildren<Transform>(true)) tr.gameObject.hideFlags = HideFlags.None;
            SceneManager.MoveGameObjectToScene(actor, scene);
            actor.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); actor.SetActive(true);
            var owner = actor.AddComponent<SkillValidationTarget>(); owner.Team = settings.OwnerTeam; owner.BaseAttack = settings.BaseAttack;
            player = actor.GetComponent<SkillPlayer>() ?? actor.AddComponent<SkillPlayer>();
            player.attackDetectionLayer = settings.AttackLayers; player.QueryScene = physics; player.QueryScene2D = physics2D;
            player.Init(owner, null, actor.transform);
            var targets = new List<SkillValidationTarget>();
            foreach (var data in settings.Targets)
            {
                var root = new GameObject("目标 " + (targets.Count + 1)); SceneManager.MoveGameObjectToScene(root, scene);
                root.layer = settings.TargetLayer; root.transform.position = data.Position;
                if (is2D) root.AddComponent<BoxCollider2D>().size = data.Size;
                else root.AddComponent<BoxCollider>().size = data.Size;
                var target = root.AddComponent<SkillValidationTarget>(); target.Team = data.Team;
                target.Received = (hitTarget, hit) =>
                {
                    report.Hits++; report.Damage += hit.attackValue;
                    if (report.Entries.Count < 2000) report.Entries.Add(new Entry { Frame = hit.Instance?.CurrentFrame ?? report.LastSampleFrame,
                        EventId = hit.detectionEvent.EventId, Target = hitTarget.name, ProjectileId = hit.Projectile?.Id.ToString("N"),
                        Damage = hit.attackValue, Result = "命中" });
                };
                targets.Add(target);
            }
            if (settings.Wall)
            {
                var wall = new GameObject("测试墙"); SceneManager.MoveGameObjectToScene(wall, scene);
                wall.layer = settings.WallLayer; wall.transform.position = settings.WallPosition;
                if (is2D) wall.AddComponent<BoxCollider2D>().size = settings.WallSize;
                else wall.AddComponent<BoxCollider>().size = settings.WallSize;
            }
            player.SetAimContext(targets[0].transform, settings.AimPoint);
            // Resolve every configured anchor before running: missing sockets are failures, not silent zero hits.
            foreach (var entry in SkillTrackModel.Read(settings.Skill, true))
            {
                if (entry.Data is SkillAttackDetectionEvent socketAttack && socketAttack.AttackDetectionData is AttackShapeDetectionDataBase socketShape && socketShape.UseFootOrigin &&
                    (socketShape.Anchor == SkillRangeAnchor.Socket || socketShape.Facing == SkillRangeFacing.SocketForward))
                    ValidateSocket(actor.transform, socketShape.SocketPath);
                if (entry.Data is SkillProjectileEvent socketProjectile && (socketProjectile.Anchor == SkillRangeAnchor.Socket || socketProjectile.Facing == SkillRangeFacing.SocketForward))
                    ValidateSocket(actor.transform, socketProjectile.SocketPath);
                if (entry.Data is SkillAttackDetectionEvent attack && attack.AttackDetectionData is AttackShapeDetectionDataBase shape && shape.UseFootOrigin &&
                    !SkillAttackShape.TryCapture(shape, actor.transform, targets[0].transform, settings.AimPoint, out _, out string shapeError, settings.Skill.Space))
                    throw new InvalidOperationException(shapeError);
                if (entry.Data is SkillProjectileEvent projectile &&
                    !SkillProjectileMotion.TryLaunch(projectile, actor.transform, targets[0].transform, settings.AimPoint, 0, out _, out string launchError, settings.Skill.Space))
                    throw new InvalidOperationException(launchError);
            }
            preview = new SkillPreviewSession(actor);
            var behaviour = new Behaviour();
            behaviour.Sample = frame =>
            {
                report.LastSampleFrame = frame; preview.Evaluate(settings.Skill, frame);
                for (int i = 0; i < targets.Count; i++) targets[i].transform.position = settings.Targets[i].Position + settings.Targets[i].Velocity * (frame / (float)settings.Skill.FrameRote);
                Physics.SyncTransforms(); Physics2D.SyncTransforms();
            };
            behaviour.Stopped = (context, reason) =>
            {
                report.Stops++;
                if (reason == SkillProjectileStopReason.Error) report.Error = "投射物运行异常，请查看 Console。";
                if (report.Entries.Count < 2000) report.Entries.Add(new Entry { Frame = report.LastSampleFrame,
                    EventId = context.EventId, ProjectileId = context.Id.ToString("N"), Result = StopLabel(reason) });
            };
            behaviour.Init(owner, player); player.StartPlaySkillBehaviour(behaviour);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            Physics.SyncTransforms(); Physics2D.SyncTransforms(); player.PlaySkillClip(settings.Skill);
            var instance = behaviour.Execution;
            if (instance == null || instance.State == SkillInstanceState.Cancelled) throw new InvalidOperationException("技能在起始帧运行失败，请查看 Console。");
            // Advance an integer-frame clock, compensating float conversion only at the boundary.
            // Newly launched projectiles already consume the player's fractional remainder.
            for (int frame = 1; frame <= last; frame++)
            {
                if (clock.Elapsed.TotalSeconds > 10) throw new InvalidOperationException("测试运行超过 10 秒预算，已停止；请缩短技能或减少目标。");
                report.LastSampleFrame = frame;
                var existing = Owned(scene);
                double time = frame / (double)settings.Skill.FrameRote;
                if (player.CurrentInstance != null)
                    player.AdvanceBy((float)Math.Max(0, time + 1e-6 / settings.Skill.FrameRote - player.CurrentInstance.ElapsedSeconds));
                else
                {
                    for (int i = 0; i < targets.Count; i++) targets[i].transform.position = settings.Targets[i].Position + settings.Targets[i].Velocity * (float)time;
                    Physics.SyncTransforms(); Physics2D.SyncTransforms();
                }
                foreach (var projectile in existing) if (projectile != null && projectile.Alive) projectile.AdvanceBy(1d / settings.Skill.FrameRote);
                if (instance != null && instance.State == SkillInstanceState.Cancelled) throw new InvalidOperationException("技能运行被异常中断，请查看 Console。");
                if (!player.IsPlaying && Owned(scene).Length == 0) break;
            }
            if (player.IsPlaying || Owned(scene).Length > 0) throw new InvalidOperationException("测试达到帧数上限，结果不完整。");
            report.Complete = report.Error == null;
            if (settings.ExpectedHits >= 0 && report.Complete) report.ExpectedMatched = report.Hits == settings.ExpectedHits;
        }
        catch (Exception ex) { report.Error = ex.Message; report.Complete = false; }
        finally
        {
            // Close only this private scene. Never unload, save or simulate the active game scene.
            try { if (player != null) player.StopSkillClip(false); }
            finally
            {
                try { foreach (var projectile in Owned(scene)) if (projectile != null) projectile.Stop(); }
                finally
                {
                    try { preview?.Dispose(); }
                    finally
                    {
                        if (actor != null) Object.DestroyImmediate(actor);
                        if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
                    }
                }
            }
        }
        return report;
    }

    private static SkillProjectile[] Owned(Scene scene) => !scene.IsValid() ? new SkillProjectile[0] :
        SkillProjectile.Active.Where(p => p != null && p.gameObject.scene == scene).ToArray();
    private static void ValidateSocket(Transform actor, string path)
    {
        for (var socket = SkillAttackShape.FindSocket(actor, path); socket != null; socket = socket.parent)
        {
            if (socket.GetComponents<Component>().OfType<UnityEngine.Animations.IConstraint>().Any(c =>
                c.constraintActive && c.weight > 0 && (!(c is UnityEngine.Behaviour b) || b.enabled)))
                throw new InvalidOperationException("挂点“" + path + "”由原生约束驱动。逐帧独立测试不能保证约束求值顺序，请在 Play Mode 验证该挂点。");
            if (socket == actor) break;
        }
    }
    private static int Validate(Settings s, Report report)
    {
        if (s?.Skill == null) throw new ArgumentException("请先选择技能。");
        if (s.ActorPrefab != null && !EditorUtility.IsPersistent(s.ActorPrefab)) throw new ArgumentException("测试角色必须是预制体资源；不会复制运行中的场景角色。");
        if (s.Targets == null || s.Targets.Count < 1 || s.Targets.Count > 12 || s.Targets.Any(t => t == null || !Finite(t.Position) || !Finite(t.Velocity) || !Size(t.Size)) ||
            !Finite(s.AimPoint) || !Finite(s.WallPosition) || !Size(s.WallSize) || s.TargetLayer < 0 || s.TargetLayer > 31 || s.WallLayer < 0 || s.WallLayer > 31 ||
            float.IsNaN(s.BaseAttack) || float.IsInfinity(s.BaseAttack) || s.BaseAttack < 0 || s.ExpectedHits < -1)
            throw new ArgumentException("测试参数无效：目标 1～12 个，尺寸须大于 0，数值必须有限。");
        var errors = SkillClipValidator.ReadIssues(s.Skill).Where(i => i.IsError).ToList();
        if (errors.Count > 0) throw new ArgumentException("请先修正配置：" + errors[0].Message);
        var entries = SkillTrackModel.Read(s.Skill, true).ToList();
        if (s.Skill.FrameRote < 1 || s.Skill.FrameRote > 240 || entries.Count > 200) throw new ArgumentException("测试支持帧率 1～240、最多 200 个片段。");
        double last = s.Skill.FrameCount; int count = 0;
        foreach (var entry in entries)
        {
            if (entry.Data is SkillProjectileEvent p)
            { count += p.Count; last = Math.Max(last, entry.Frame + Math.Ceiling(p.LifeSeconds(s.Skill.FrameRote) * s.Skill.FrameRote) + 1); }
            if (entry.Data is SkillAttackDetectionEvent a && a.GetAttackDetectionType() == AttackDetectionType.Weapon)
                report.Warnings.Add("武器触发器片段已跳过：需要在正式 Play Mode 中验证。");
            if (entry.Data is SkillAnimationEvent animation && animation.AnimationClip != null && s.ActorPrefab == null)
                throw new ArgumentException("技能包含动画，请选择角色预制体以验证动画与根运动后的命中位置。");
        }
        if (last > 1800 || last / s.Skill.FrameRote > 30 || count > 128) throw new ArgumentException("独立测试上限：30 秒、1800 帧、128 个投射物；请缩短测试技能。");
        report.Warnings.Add("仅验证范围/投射物命中。伤害 = 基础攻击 × 技能倍率；不含防御、Buff、资源消耗、冷却、自定义事件和受击反馈。");
        report.Warnings.Add(s.Skill.Space == SkillSpace.TwoD ? "2D 使用 XY 平面；角色从原点施放，朝向读取角色配置，默认 +X；首个目标为追踪对象。" : "角色从原点朝 +Z 方向施放；首个目标用作追踪对象。投射物结果按技能采样帧记录。");
        if ((s.AttackLayers.value & (1 << s.TargetLayer)) == 0) report.Warnings.Add("目标层不在攻击检测层中，本次应当无法命中目标。");
        return (int)Math.Ceiling(last);
    }
    private static bool Finite(Vector3 v) => !float.IsNaN(v.x + v.y + v.z) && !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);
    private static bool Size(Vector3 v) => Finite(v) && v.x > 0 && v.y > 0 && v.z > 0;
    public static string StopLabel(SkillProjectileStopReason reason)
    {
        switch (reason)
        {
            case SkillProjectileStopReason.Wall: return "墙壁阻挡";
            case SkillProjectileStopReason.HitLimit: return "达到穿透/目标上限";
            case SkillProjectileStopReason.SkillEnded: return "随技能结束销毁";
            case SkillProjectileStopReason.Cancelled: return "技能取消";
            case SkillProjectileStopReason.Lifetime: return "到达飞行/寿命终点";
            case SkillProjectileStopReason.Error: return "运行异常";
            default: return "停止（" + reason + "）";
        }
    }
}

}
