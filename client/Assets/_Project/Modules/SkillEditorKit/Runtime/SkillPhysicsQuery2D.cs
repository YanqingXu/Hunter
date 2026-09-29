namespace SkillEditorKit
{
using System;
using UnityEngine;
/// <summary>Scene-local queries. Contact filters do not change global Physics2D settings.</summary>
public static class SkillPhysicsQuery2D
{
    private static ContactFilter2D Filter(int mask, bool triggers)
    { var f = new ContactFilter2D { useTriggers = triggers }; f.SetLayerMask(mask); return f; }
    private static T[] Read<T>(PhysicsScene2D? scene, Func<PhysicsScene2D,T[],int> query)
    {
        var physics = scene ?? Physics2D.defaultPhysicsScene;
        if (!physics.IsValid()) throw new InvalidOperationException("2D 物理场景无效。");
        for (int size=32; size<=32768; size*=2)
        { var hits=new T[size]; int count=query(physics,hits); if(count<size) { Array.Resize(ref hits,count); return hits; } }
        throw new InvalidOperationException("2D 查询超过 32768 个结果。");
    }
    public static Collider2D[] Circle(Vector2 point,float radius,int mask,PhysicsScene2D? scene=null,bool triggers=true) =>
        Read<Collider2D>(scene,(s,r)=>s.OverlapCircle(point,radius,Filter(mask,triggers),r));
    public static Collider2D[] Box(Vector2 point,Vector2 size,float angle,int mask,PhysicsScene2D? scene=null) =>
        Read<Collider2D>(scene,(s,r)=>s.OverlapBox(point,size,angle,Filter(mask,true),r));
    public static RaycastHit2D[] CircleCast(Vector2 point,float radius,Vector2 direction,float distance,int mask,PhysicsScene2D? scene=null) =>
        Read<RaycastHit2D>(scene,(s,r)=>s.CircleCast(point,radius,direction,distance,Filter(mask,true),r));
    public static RaycastHit2D[] BoxCast(Vector2 point,Vector2 size,float angle,Vector2 direction,float distance,int mask,PhysicsScene2D? scene=null) =>
        Read<RaycastHit2D>(scene,(s,r)=>s.BoxCast(point,size,angle,direction,distance,Filter(mask,true),r));
    public static RaycastHit2D[] Ray(Vector2 point,Vector2 direction,float distance,int mask,PhysicsScene2D? scene=null,bool triggers=false) =>
        Read<RaycastHit2D>(scene,(s,r)=>s.Raycast(point,direction,distance,Filter(mask,triggers),r));
}
public static class SkillCollider
{
    public static Vector3 ClosestPoint(Component collider, Vector3 point)
    {
        if (collider is Collider c) return c.ClosestPoint(point);
        Vector2 p=((Collider2D)collider).ClosestPoint(point); return new Vector3(p.x,p.y,point.z);
    }
    public static bool IsTrigger(Component collider) => collider is Collider c ? c.isTrigger : ((Collider2D)collider).isTrigger;
}
}
