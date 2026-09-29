// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit
{
using UnityEngine;

public class SkillAttackDetectionEvent : SkillFrameEventBase
{
#if UNITY_EDITOR
    public string TrackName = "攻击检测轨道";
#endif
    public int FrameIndex = 0;
    public int DurationFrame = 10;
    public AttackDetectionDataBase AttackDetectionData;
    public AttackHitConfig AttackHitConfig = new AttackHitConfig();
    // Zero/default retains the previous per-skill hit behaviour for existing assets.
    public SkillHitRules HitRules = new SkillHitRules();
    public AttackDetectionType GetAttackDetectionType()
    {
        if (AttackDetectionData == null) return AttackDetectionType.None;
        if (AttackDetectionData is AttackWeaponDetectionData) return AttackDetectionType.Weapon;
        if (AttackDetectionData is AttackBoxDetectionData) return AttackDetectionType.Box;
        if (AttackDetectionData is AttackSphereDetectionData) return AttackDetectionType.Sphere;
        if (AttackDetectionData is AttackFanDetectionData fan) return fan.GroundCircle ? AttackDetectionType.GroundCircle : AttackDetectionType.Fan;
        return AttackDetectionType.None;
    }

#if UNITY_EDITOR
    public AttackDetectionType AttackDetectionType
    {
        get
        {
            return GetAttackDetectionType();
        }
        set
        {
            if (value != AttackDetectionType) // 类型发生了变化
            {
                switch (value)
                {
                    case AttackDetectionType.None:
                        AttackDetectionData = null;
                        break;
                    case AttackDetectionType.Weapon:
                        AttackDetectionData = new AttackWeaponDetectionData();
                        break;
                    case AttackDetectionType.Box:
                        AttackDetectionData = new AttackBoxDetectionData();
                        break;
                    case AttackDetectionType.Sphere:
                        AttackDetectionData = new AttackSphereDetectionData();
                        break;
                    case AttackDetectionType.Fan:
                        AttackDetectionData = new AttackFanDetectionData();
                        break;
                    case AttackDetectionType.GroundCircle:
                        AttackDetectionData = new AttackFanDetectionData { GroundCircle = true };
                        break;

                }
                if (AttackDetectionData is AttackShapeDetectionDataBase shape)
                    SkillAttackShape.UseForwardDefaults(shape);
            }
        }

    }
#endif
}
#region 检测
// 攻击检测类型
public enum AttackDetectionType
{
    None, Weapon, Box, Sphere, Fan, GroundCircle
}

// 攻击检测基类
public abstract class AttackDetectionDataBase
{ }

// 武器检测
public class AttackWeaponDetectionData : AttackDetectionDataBase
{
    public string weaponName;
}

// 形状检测
public abstract class AttackShapeDetectionDataBase : AttackDetectionDataBase
{
    // Legacy local-space data remains intact until the user explicitly converts it.
    public Vector3 Position;
    public bool UseFootOrigin;
    public AttackRangeMotion Motion;
    public float StartDistance;
    public float EndDistance = 6;
    public float HeightOffset;
    public SkillRangeAnchor Anchor;
    public SkillRangeFacing Facing;
    public string SocketPath;
}

public enum AttackRangeMotion { FollowCharacter, LinearFlight, FixedAtStart }
public enum SkillRangeAnchor { CharacterFeet, Socket, Target, AimPoint }
public enum SkillRangeFacing { CharacterForward, TowardTarget, SocketForward }

// 盒型检测
public class AttackBoxDetectionData : AttackShapeDetectionDataBase
{
    public Vector3 Rotation;
    public Vector3 Scale = Vector3.one;
}

// 球形检测
public class AttackSphereDetectionData : AttackShapeDetectionDataBase
{
    public float Radius = 1;
}

// 扇形检测
public class AttackFanDetectionData : AttackShapeDetectionDataBase
{
    public bool GroundCircle;
    public Vector3 Rotation;
    public float InsideRadius = 1;
    public float Radius = 3;
    public float Height = 0.5f;
    public float Angle = 90;
}
#endregion
#region 命中
public class AttackHitConfig
{
    public float AttackMultiply = 1;
    public Vector3 RepelStrength; // 0,0,2
    public float RepelTime; // 1秒 位移了两米
    public GameObject HitEffectPrefab;
    public AudioClip HitAudioClip;
}
#endregion

}
