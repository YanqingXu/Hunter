// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit
{
using System;
using UnityEngine;

/// <summary>Shared numerical attack geometry. New ranges use metre-sized dimensions,
/// a foot-level root origin and horizontal character facing, independent of model scale.</summary>
public static class SkillAttackShape
{
    public struct Origin
    {
        public SkillSpace Space;
        public Vector3 Position, Scale;
        public Quaternion Rotation;
        public static Origin Capture(Transform root, SkillSpace space = SkillSpace.ThreeD) => new Origin
        { Space = space, Position = root.position, Rotation = space == SkillSpace.TwoD ? SkillFacing2D.Rotation(root) : root.rotation, Scale = root.lossyScale };
    }

    public struct Pose
    {
        public SkillSpace Space;
        // Box/sphere: volume centre. Fan: bottom centre of the sector.
        public Vector3 Position;
        public Quaternion Rotation;
    }

    public static bool IsFlight(AttackShapeDetectionDataBase data) =>
        data != null && data.UseFootOrigin && data.Motion == AttackRangeMotion.LinearFlight;

    public static bool IsFrozen(AttackShapeDetectionDataBase data) => data != null && data.UseFootOrigin &&
        (data.Motion == AttackRangeMotion.LinearFlight || data.Motion == AttackRangeMotion.FixedAtStart);

    public static Transform FindSocket(Transform root, string path)
    {
        if (root == null || string.IsNullOrWhiteSpace(path)) return null;
        var exact = root.Find(path);
        if (exact != null) return exact;
        // A unique bone name is convenient; ambiguous names must use a full relative path.
        Transform result = null;
        foreach (var item in root.GetComponentsInChildren<Transform>(true))
            if (item.name == path) { if (result != null) return null; result = item; }
        return result;
    }

    public static bool TryCapture(AttackShapeDetectionDataBase data, Transform root, Transform target,
        Vector3? aimPoint, out Origin origin, out string reason, SkillSpace space = SkillSpace.ThreeD)
    {
        origin = default; reason = null;
        if (root == null) { reason = "未选择预览角色"; return false; }
        origin = Origin.Capture(root, space);
        if (data == null || !data.UseFootOrigin) return true;
        Transform socket = null;
        if (data.Anchor == SkillRangeAnchor.Socket || data.Facing == SkillRangeFacing.SocketForward)
        {
            socket = FindSocket(root, data.SocketPath);
            if (socket == null) { reason = "缺少挂点或名称重复：" + (data.SocketPath ?? "未填写"); return false; }
        }
        if ((data.Anchor == SkillRangeAnchor.Target || data.Facing == SkillRangeFacing.TowardTarget) && target == null)
        { reason = "未指定目标位置"; return false; }
        if (data.Anchor == SkillRangeAnchor.AimPoint && !aimPoint.HasValue)
        { reason = "未指定落点"; return false; }
        if (data.Anchor == SkillRangeAnchor.Socket) origin.Position = socket.position;
        else if (data.Anchor == SkillRangeAnchor.Target) origin.Position = target.position;
        else if (data.Anchor == SkillRangeAnchor.AimPoint) origin.Position = aimPoint.Value;
        if (data.Facing == SkillRangeFacing.SocketForward) origin.Rotation = space == SkillSpace.TwoD ? SkillFacing2D.Look(socket.TransformVector(Vector3.right)) : socket.rotation;
        else if (data.Facing == SkillRangeFacing.TowardTarget)
        {
            // Facing is measured from the caster, including when the range is centred on the target.
            var direction = Vector3.ProjectOnPlane(target.position - root.position, space == SkillSpace.TwoD ? Vector3.forward : Vector3.up);
            if (direction.sqrMagnitude > .000001f) origin.Rotation = space == SkillSpace.TwoD ? SkillFacing2D.Look(direction) : Quaternion.LookRotation(direction, Vector3.up);
        }
        return true;
    }

    public static float Progress(int frame, int start, int duration) =>
        Mathf.Clamp01((float)((frame - (double)start) / Math.Max(1, duration)));

    public static Quaternion Facing(Quaternion rotation)
    {
        var forward = Vector3.ProjectOnPlane(rotation * Vector3.forward, Vector3.up);
        return forward.sqrMagnitude < .000001f ? Quaternion.identity : Quaternion.LookRotation(forward.normalized, Vector3.up);
    }

    public static Pose Resolve(AttackShapeDetectionDataBase data, Origin origin, float progress = 0)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        if (origin.Space == SkillSpace.TwoD) return Resolve2D(data, origin, progress);
        if (!data.UseFootOrigin)
        {
            var legacyRotation = data is AttackBoxDetectionData box ? box.Rotation :
                data is AttackFanDetectionData fan ? fan.Rotation : Vector3.zero;
            var pose = new Pose { Position = Matrix4x4.TRS(origin.Position, origin.Rotation, origin.Scale).MultiplyPoint3x4(data.Position),
                Rotation = origin.Rotation * Quaternion.Euler(legacyRotation) };
            // Old fan collision has always been centred vertically on Position.
            if (data is AttackFanDetectionData oldFan) pose.Position -= pose.Rotation * Vector3.up * oldFan.Height * .5f;
            return pose;
        }
        var facing = data.Facing == SkillRangeFacing.SocketForward ? origin.Rotation : Facing(origin.Rotation);
        float distance = IsFlight(data) ? Mathf.Lerp(data.StartDistance, data.EndDistance, Mathf.Clamp01(progress)) : data.StartDistance;
        var local = new Vector3(0, data.HeightOffset, distance);
        bool feet = data.Anchor == SkillRangeAnchor.CharacterFeet;
        bool centred = data.Anchor == SkillRangeAnchor.Socket;
        if (data is AttackBoxDetectionData newBox) local += new Vector3(0, centred ? 0 : newBox.Scale.y * .5f, feet ? newBox.Scale.z * .5f : 0);
        else if (data is AttackSphereDetectionData sphere) local += new Vector3(0, centred ? 0 : sphere.Radius, feet ? sphere.Radius : 0);
        else if (data is AttackFanDetectionData sector && centred) local.y -= sector.Height * .5f;
        return new Pose { Position = origin.Position + facing * local, Rotation = facing };
    }

    private static Pose Resolve2D(AttackShapeDetectionDataBase data, Origin origin, float progress)
    {
        var forward = origin.Rotation * Vector3.right;
        float distance = IsFlight(data) ? Mathf.Lerp(data.StartDistance,data.EndDistance,Mathf.Clamp01(progress)) : data.StartDistance;
        float height = data.HeightOffset;
        var rotation = origin.Rotation;
        if (!data.UseFootOrigin)
        {
            distance = data.Position.x * Mathf.Abs(origin.Scale.x); height = data.Position.y * Mathf.Abs(origin.Scale.y);
            float z = data is AttackBoxDetectionData b ? b.Rotation.z : data is AttackFanDetectionData f ? f.Rotation.z : 0;
            rotation *= Quaternion.Euler(0,0,z);
        }
        else
        {
            bool feet = data.Anchor == SkillRangeAnchor.CharacterFeet, socket = data.Anchor == SkillRangeAnchor.Socket;
            if (data is AttackBoxDetectionData box) { distance += feet ? box.Scale.x*.5f : 0; height += socket ? 0 : box.Scale.y*.5f; }
            else if (data is AttackSphereDetectionData circle) { distance += feet ? circle.Radius : 0; height += socket ? 0 : circle.Radius; }
        }
        // Height stays world-up when turning left; Z never participates in a 2D query.
        return new Pose { Space=SkillSpace.TwoD, Position=origin.Position + forward*distance + Vector3.up*height, Rotation=rotation };
    }

    public static void UseForwardDefaults(AttackShapeDetectionDataBase data)
    {
        data.UseFootOrigin = true; data.Motion = AttackRangeMotion.FollowCharacter;
        data.StartDistance = 0; data.EndDistance = 6; data.HeightOffset = 0;
        if (data is AttackFanDetectionData fan) { fan.InsideRadius = 0; fan.Angle = fan.GroundCircle ? 360 : 180; fan.Height = 2; }
    }

    // Explicit editor action only. Old off-axis rotation/offset cannot be represented
    // in the simpler forward-only model; the Inspector explains this before conversion.
    public static void ConvertForward(AttackShapeDetectionDataBase data, SkillSpace space = SkillSpace.ThreeD)
    {
        float distance = space == SkillSpace.TwoD ? data.Position.x : data.Position.z, height = data.Position.y;
        if (data is AttackBoxDetectionData box) { distance -= (space == SkillSpace.TwoD ? box.Scale.x : box.Scale.z) * .5f; height -= box.Scale.y * .5f; }
        else if (data is AttackSphereDetectionData sphere) { distance -= sphere.Radius; height -= sphere.Radius; }
        else if (space != SkillSpace.TwoD && data is AttackFanDetectionData fan) height -= fan.Height * .5f;
        data.UseFootOrigin = true; data.Motion = AttackRangeMotion.FollowCharacter;
        data.Anchor = SkillRangeAnchor.CharacterFeet; data.Facing = SkillRangeFacing.CharacterForward; data.SocketPath = null;
        data.StartDistance = Mathf.Max(0, distance); data.EndDistance = data.StartDistance + 6;
        data.HeightOffset = Mathf.Max(0, height);
    }

    public static bool Validate(AttackShapeDetectionDataBase data, out string reason, SkillSpace space = SkillSpace.ThreeD)
    {
        reason = null;
        if (data == null) return true;
        if (!Finite(data.Position)) reason = "攻击范围旧坐标必须为有限数值。";
        if (data is AttackBoxDetectionData box && (!Positive(box.Scale.x) || !Positive(box.Scale.y) || (space != SkillSpace.TwoD && !Positive(box.Scale.z)) || !Finite(box.Rotation)))
            reason = "盒形攻击的长、宽、高必须大于 0，旋转必须为有限数值。";
        if (data is AttackSphereDetectionData sphere && !Positive(sphere.Radius)) reason = "球形攻击半径必须大于 0。";
        if (data is AttackFanDetectionData fan && (!Positive(fan.Radius) || (space != SkillSpace.TwoD && !Positive(fan.Height)) || !Finite(fan.InsideRadius) ||
            fan.InsideRadius < 0 || fan.InsideRadius >= fan.Radius || !Positive(fan.Angle) || fan.Angle > 360 || !Finite(fan.Rotation)))
            reason = "扇形攻击需要有效的半径、高度和角度，内半径必须小于外半径。";
        if (data.UseFootOrigin && (!Enum.IsDefined(typeof(AttackRangeMotion), data.Motion) || !Nonnegative(data.StartDistance) ||
            !Nonnegative(data.HeightOffset) || (IsFlight(data) && (!Nonnegative(data.EndDistance) || data.EndDistance < data.StartDistance))))
            reason = "攻击距离和离地高度必须为非负有限数值，结束距离不得小于起始距离。";
        if (data.UseFootOrigin && (!Enum.IsDefined(typeof(SkillRangeAnchor), data.Anchor) || !Enum.IsDefined(typeof(SkillRangeFacing), data.Facing)))
            reason = "未知的范围起点或朝向。";
        if (data.UseFootOrigin && (data.Anchor == SkillRangeAnchor.Socket || data.Facing == SkillRangeFacing.SocketForward) && string.IsNullOrWhiteSpace(data.SocketPath))
            reason = "请填写范围使用的挂点路径或唯一名称。";
        if (data is AttackFanDetectionData circle && circle.GroundCircle && (circle.Angle != 360 || circle.InsideRadius != 0))
            reason = "地面圆形应使用 360° 且不留内圈。";
        return reason == null;
    }

    private static bool Positive(float value) => Finite(value) && value > 0;
    private static bool Nonnegative(float value) => Finite(value) && value >= 0;
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
}

}
