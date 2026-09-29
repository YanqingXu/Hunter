// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit.Editor
{
using UnityEditor;
using UnityEngine;

/// <summary>Scene overlays are independent of runtime debug flags and selection gizmos.</summary>
public static class SkillSceneRangeRenderer
{
    public static void Draw(AttackShapeDetectionDataBase shape, SkillAttackShape.Pose pose, Color color)
    {
        using (new Handles.DrawingScope(color, Matrix4x4.TRS(pose.Position, pose.Rotation, Vector3.one)))
        {
            if (pose.Space == SkillSpace.TwoD)
            {
                if(shape is AttackBoxDetectionData b) Handles.DrawWireCube(Vector3.zero,new Vector3(b.Scale.x,b.Scale.y,0));
                else if(shape is AttackSphereDetectionData c) Handles.DrawWireDisc(Vector3.zero,Vector3.forward,c.Radius);
                else if(shape is AttackFanDetectionData f)
                {
                    var start=Quaternion.Euler(0,0,-f.Angle*.5f)*Vector3.right;
                    var end=Quaternion.Euler(0,0,f.Angle*.5f)*Vector3.right;
                    Handles.DrawWireArc(Vector3.zero,Vector3.forward,start,f.Angle,f.Radius);
                    if(f.InsideRadius>0) Handles.DrawWireArc(Vector3.zero,Vector3.forward,start,f.Angle,f.InsideRadius);
                    if(f.Angle<360) { Handles.DrawLine(start*f.InsideRadius,start*f.Radius); Handles.DrawLine(end*f.InsideRadius,end*f.Radius); }
                }
                return;
            }
            if (shape is AttackBoxDetectionData box) Handles.DrawWireCube(Vector3.zero, box.Scale);
            else if (shape is AttackSphereDetectionData sphere)
            {
                Handles.DrawWireDisc(Vector3.zero, Vector3.up, sphere.Radius);
                Handles.DrawWireDisc(Vector3.zero, Vector3.right, sphere.Radius);
                Handles.DrawWireDisc(Vector3.zero, Vector3.forward, sphere.Radius);
            }
            else if (shape is AttackFanDetectionData fan)
            {
                var start = Quaternion.Euler(0, -fan.Angle * .5f, 0) * Vector3.forward;
                var end = Quaternion.Euler(0, fan.Angle * .5f, 0) * Vector3.forward;
                foreach (float height in new[] { 0f, fan.Height })
                {
                    var p = Vector3.up * height;
                    Handles.DrawWireArc(p, Vector3.up, start, fan.Angle, fan.Radius);
                    if (fan.InsideRadius > 0) Handles.DrawWireArc(p, Vector3.up, start, fan.Angle, fan.InsideRadius);
                    if (fan.Angle < 360)
                    {
                        Handles.DrawLine(p + start * fan.InsideRadius, p + start * fan.Radius);
                        Handles.DrawLine(p + end * fan.InsideRadius, p + end * fan.Radius);
                    }
                }
                foreach (var direction in new[] { start, end, Vector3.forward })
                    Handles.DrawLine(direction * fan.Radius, direction * fan.Radius + Vector3.up * fan.Height);
            }
        }
    }
}

}
