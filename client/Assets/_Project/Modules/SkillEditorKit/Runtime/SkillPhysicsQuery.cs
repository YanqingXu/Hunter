// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit
{
using System;
using UnityEngine;

/// <summary>Optional isolated physics context. Null preserves the game's default world.</summary>
public static class SkillPhysicsQuery
{
    private static T[] Read<T>(PhysicsScene scene, Func<T[], int> query)
    {
        if (!scene.IsValid()) throw new InvalidOperationException("技能物理场景已失效。");
        for (int size = 32; size <= 32768; size *= 2)
        {
            var buffer = new T[size]; int count = query(buffer);
            if (count == size) continue;
            Array.Resize(ref buffer, count); return buffer;
        }
        throw new InvalidOperationException("技能物理查询超过 32768 个碰撞体，已停止以避免静默漏检。");
    }
    public static Collider[] Box(Vector3 centre, Vector3 half, Quaternion rotation, int mask,
        PhysicsScene? scene = null, QueryTriggerInteraction triggers = QueryTriggerInteraction.UseGlobal)
        => scene.HasValue ? Read<Collider>(scene.Value, b => scene.Value.OverlapBox(centre, half, b, rotation, mask, triggers))
            : Physics.OverlapBox(centre, half, rotation, mask, triggers);
    public static Collider[] Sphere(Vector3 centre, float radius, int mask,
        PhysicsScene? scene = null, QueryTriggerInteraction triggers = QueryTriggerInteraction.UseGlobal)
        => scene.HasValue ? Read<Collider>(scene.Value, b => scene.Value.OverlapSphere(centre, radius, b, mask, triggers))
            : Physics.OverlapSphere(centre, radius, mask, triggers);
    public static RaycastHit[] BoxCast(Vector3 centre, Vector3 half, Vector3 direction, Quaternion rotation,
        float distance, int mask, PhysicsScene? scene = null, QueryTriggerInteraction triggers = QueryTriggerInteraction.UseGlobal)
        => scene.HasValue ? Read<RaycastHit>(scene.Value, b => scene.Value.BoxCast(centre, half, direction, b, rotation, distance, mask, triggers))
            : Physics.BoxCastAll(centre, half, direction, rotation, distance, mask, triggers);
    public static RaycastHit[] SphereCast(Vector3 centre, float radius, Vector3 direction, float distance, int mask,
        PhysicsScene? scene = null, QueryTriggerInteraction triggers = QueryTriggerInteraction.UseGlobal)
        => scene.HasValue ? Read<RaycastHit>(scene.Value, b => scene.Value.SphereCast(centre, radius, direction, b, distance, mask, triggers))
            : Physics.SphereCastAll(centre, radius, direction, distance, mask, triggers);
    public static RaycastHit[] Ray(Vector3 origin, Vector3 direction, float distance, int mask,
        PhysicsScene? scene = null, QueryTriggerInteraction triggers = QueryTriggerInteraction.UseGlobal)
        => scene.HasValue ? Read<RaycastHit>(scene.Value, b => scene.Value.Raycast(origin, direction, b, distance, mask, triggers))
            : Physics.RaycastAll(origin, direction, distance, mask, triggers);
}

}
