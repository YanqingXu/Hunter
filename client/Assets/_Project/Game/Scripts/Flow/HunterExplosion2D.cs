using System.Collections.Generic;
using BigWorld.YouYou2D;
using SkillEditorKit;
using UnityEngine;

namespace BigWorld.Gameplay
{
    /// <summary>Claim the entire chain before damage callbacks; each blast deduplicates actor colliders.</summary>
    public static class HunterExplosion2D
    {
        private struct Blast { public Vector2 Origin; public float Radius, Damage; public ISkillActor Source; }
        private struct Hit { public SkillActor2D Target; public SkillHitData Data; }
        public static void Detonate(LevelSession2D level, Vector2 origin, float radius, float damage, ISkillActor source)
        {
            if (!level || !level.IsGameplayActive || !Finite(origin.x) || !Finite(origin.y) || !Finite(radius) || radius <= 0 || !Finite(damage) || damage <= 0) return;
            var pending = new Queue<Blast>(); var hits = new List<Hit>();
            pending.Enqueue(new Blast { Origin = origin, Radius = radius, Damage = damage, Source = source });
            while (pending.Count > 0)
            {
                var blast = pending.Dequeue(); var targets = new HashSet<SkillActor2D>();
                foreach (var collider in Physics2D.OverlapCircleAll(blast.Origin, blast.Radius))
                {
                    if (!collider || collider.isTrigger || collider.gameObject.scene != level.gameObject.scene) continue;
                    var barrel = collider.GetComponentInParent<HunterExplosiveBarrel2D>();
                    if (barrel && Visible(blast.Origin, barrel.transform.position) && barrel.ClaimExplosion())
                        pending.Enqueue(new Blast { Origin = barrel.transform.position, Radius = barrel.Radius, Damage = barrel.Damage });
                    var target = collider.GetComponentInParent<SkillActor2D>();
                    if (!target || !target.IsAlive || !Visible(blast.Origin, collider.bounds.center) || !targets.Add(target)) continue;
                    hits.Add(new Hit { Target = target, Data = new SkillHitData { soure = blast.Source, attackValue = blast.Damage, hitPoint = blast.Origin } });
                }
            }
            var player = level.Player ? level.Player.GetComponent<SkillActor2D>() : null;
            // A lethal player hit pauses the session. Enemy damage across every chained blast must precede it.
            foreach (var hit in hits) if (hit.Target && hit.Target != player && hit.Target.IsAlive) hit.Target.BeHit(hit.Data);
            foreach (var hit in hits) if (hit.Target && hit.Target == player && hit.Target.IsAlive) hit.Target.BeHit(hit.Data);
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Visible(Vector2 origin, Vector2 end)
        {
            foreach (var hit in Physics2D.LinecastAll(origin, end))
                if (hit.collider && !hit.collider.isTrigger && !hit.collider.GetComponentInParent<SkillActor2D>() &&
                    !hit.collider.GetComponentInParent<HunterExplosiveBarrel2D>()) return false;
            return true;
        }
    }
}
