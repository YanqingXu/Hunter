namespace SkillEditorKit
{
using System.Collections.Generic;
using UnityEngine;
/// <summary>XY attack queries, including continuous box/circle sweeps.</summary>
public static class SkillAttackDetection2D
{
    public static Collider2D[] Detect(AttackShapeDetectionDataBase shape, SkillAttackShape.Pose pose, int mask,
        SkillAttackShape.Pose? previous=null, PhysicsScene2D? scene=null)
    {
        var found = new HashSet<Collider2D>(); AddAt(shape,pose,mask,scene,found);
        if(previous.HasValue && SkillAttackShape.IsFlight(shape))
        {
            var from=previous.Value; Vector2 delta=pose.Position-from.Position; float length=delta.magnitude;
            if(length > .000001f)
            {
                if(shape is AttackBoxDetectionData box)
                {
                    foreach(var hit in SkillPhysicsQuery2D.BoxCast(from.Position,box.Scale,from.Rotation.eulerAngles.z,delta/length,length,mask,scene))
                        if(hit.collider!=null) found.Add(hit.collider);
                }
                else if(shape is AttackSphereDetectionData circle)
                {
                    foreach(var hit in SkillPhysicsQuery2D.CircleCast(from.Position,circle.Radius,delta/length,length,mask,scene))
                        if(hit.collider!=null) found.Add(hit.collider);
                }
                else
                {
                    var fan=(AttackFanDetectionData)shape;
                    int steps=Mathf.Clamp(Mathf.CeilToInt(length/Mathf.Max(.01f,(fan.Radius-fan.InsideRadius)*.25f)),1,128);
                    for(int i=0;i<steps;i++) { var p=pose; p.Position=Vector3.Lerp(from.Position,pose.Position,i/(float)steps); AddAt(shape,p,mask,scene,found); }
                }
            }
        }
        var result=new Collider2D[found.Count]; found.CopyTo(result); return result;
    }
    private static void AddAt(AttackShapeDetectionDataBase shape, SkillAttackShape.Pose pose,int mask,PhysicsScene2D? scene,HashSet<Collider2D> found)
    {
        if(shape is AttackBoxDetectionData box)
        { foreach(var c in SkillPhysicsQuery2D.Box(pose.Position,box.Scale,pose.Rotation.eulerAngles.z,mask,scene)) found.Add(c); return; }
        float radius=shape is AttackSphereDetectionData circle ? circle.Radius : ((AttackFanDetectionData)shape).Radius;
        var candidates=SkillPhysicsQuery2D.Circle(pose.Position,radius,mask,scene);
        if(!(shape is AttackFanDetectionData fan)) { foreach(var c in candidates) found.Add(c); return; }
        var inverse=Quaternion.Inverse(pose.Rotation);
        foreach(var c in candidates)
        {
            Vector2 local=inverse*(SkillCollider.ClosestPoint(c,pose.Position)-pose.Position);
            if(local.magnitude+1e-5f>=fan.InsideRadius && (fan.Angle>=360 || Mathf.Abs(Mathf.Atan2(local.y,local.x)*Mathf.Rad2Deg)<=fan.Angle*.5f)) found.Add(c);
        }
        // Check the sector boundary too: a wide collider can cross a radial edge
        // even when its point closest to the origin is outside the sector.
        var boundary=new List<Vector2>(); int segments=Mathf.Max(2,Mathf.CeilToInt(fan.Angle/3));
        for(int i=0;i<=segments;i++) boundary.Add(Point(pose,fan.Radius,Mathf.Lerp(-fan.Angle*.5f,fan.Angle*.5f,i/(float)segments)));
        if(fan.InsideRadius>0)
            for(int i=segments;i>=0;i--) boundary.Add(Point(pose,fan.InsideRadius,Mathf.Lerp(-fan.Angle*.5f,fan.Angle*.5f,i/(float)segments)));
        else if(fan.Angle<360) boundary.Add(pose.Position);
        for(int i=0;i<boundary.Count;i++)
        {
            Vector2 start=boundary[i],delta=boundary[(i+1)%boundary.Count]-start;
            foreach(var c in candidates) if(!found.Contains(c) && c.OverlapPoint(start)) found.Add(c);
            if(delta.sqrMagnitude<1e-10f) continue;
            foreach(var hit in SkillPhysicsQuery2D.Ray(start,delta.normalized,delta.magnitude,mask,scene,true)) if(hit.collider!=null) found.Add(hit.collider);
        }
    }
    private static Vector2 Point(SkillAttackShape.Pose pose,float radius,float angle) =>
        pose.Position + pose.Rotation * (Quaternion.Euler(0,0,angle)*Vector3.right*radius);
}
}
