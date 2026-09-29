using UnityEngine;
using SkillEditorKit;

namespace BigWorld.YouYou2D
{
    public static class GameEvents2D
    {
        public const ushort WorldReady = 2000;
        public const ushort SkillStarted = 2001;
        public const ushort ActorDamaged = 2002;
        public const ushort PauseChanged = 2003;
    }

    public sealed class SkillStarted2D
    {
        public readonly SkillActor2D Actor;
        public readonly SkillClip Skill;
        public SkillStarted2D(SkillActor2D actor, SkillClip skill) { Actor = actor; Skill = skill; }
    }

    /// <summary>Snapshot values survive a pooled target being recycled by the hit.</summary>
    public sealed class ActorDamage2D
    {
        public readonly GameObject Target;
        public readonly string EntityId;
        public readonly float Damage, Health;
        public ActorDamage2D(GameObject target, string entityId, float damage, float health)
        { Target = target; EntityId = entityId; Damage = damage; Health = health; }
    }
}
