// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit
{
# if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class SkillGizmosTool
{
    private static readonly Dictionary<Vector4, Mesh> fanMeshes = new Dictionary<Vector4, Mesh>();
    static SkillGizmosTool()
    {
        AssemblyReloadEvents.beforeAssemblyReload += ClearMeshes;
        EditorApplication.quitting += ClearMeshes;
    }
    private static void ClearMeshes()
    {
        foreach (var mesh in fanMeshes.Values) if (mesh != null) Object.DestroyImmediate(mesh);
        fanMeshes.Clear();
    }
    private static Mesh FanMesh(AttackFanDetectionData fan)
    {
        var key = new Vector4(fan.InsideRadius, fan.Radius, fan.Height, fan.Angle);
        if (fanMeshes.TryGetValue(key, out var mesh) && mesh != null) return mesh;
        if (fanMeshes.Count >= 64) ClearMeshes();
        mesh = MeshGenerator.GenarteFanMesh(fan.InsideRadius, fan.Radius, fan.Height, fan.Angle);
        mesh.hideFlags = HideFlags.HideAndDontSave; fanMeshes[key] = mesh; return mesh;
    }

    public static void DrawDetection(SkillAttackDetectionEvent data, SkillPlayer player, SkillAttackShape.Pose? resolvedPose = null)
    {
        if (data?.AttackDetectionData == null || player == null) return;
        var oldColor = Gizmos.color; var oldMatrix = Gizmos.matrix;
        try
        {
            Gizmos.color = new Color(0, 1, .2f, .22f); Gizmos.matrix = Matrix4x4.identity;
            if (data.AttackDetectionData is AttackWeaponDetectionData weaponData)
            {
                if (string.IsNullOrEmpty(weaponData.weaponName) || player.WeaponDic == null ||
                    !player.WeaponDic.TryGetValue(weaponData.weaponName, out var weapon) || weapon == null) return;
                if (player.CurrentInstance?.Space == SkillSpace.TwoD)
                { var c=weapon.GetComponent<Collider2D>(); if(c!=null) Gizmos.DrawWireCube(c.bounds.center,c.bounds.size); return; }
                var collider = weapon.GetComponent<Collider>();
                if (collider == null) return;
                Gizmos.matrix = collider.transform.localToWorldMatrix;
                if (collider is BoxCollider boxCollider) Gizmos.DrawWireCube(boxCollider.center, boxCollider.size);
                else if (collider is SphereCollider sphereCollider) Gizmos.DrawWireSphere(sphereCollider.center, sphereCollider.radius);
                return;
            }
            var shape = data.AttackDetectionData as AttackShapeDetectionDataBase;
            if (shape == null || !SkillAttackShape.Validate(shape, out _, player.CurrentInstance?.Space ?? SkillSpace.ThreeD)) return;
            var root = player.ModelTransform != null ? player.ModelTransform : player.transform;
            var pose = resolvedPose ?? SkillAttackShape.Resolve(shape, SkillAttackShape.Origin.Capture(root, player.CurrentInstance?.Space ?? SkillSpace.ThreeD));
            Gizmos.matrix = Matrix4x4.TRS(pose.Position, pose.Rotation, Vector3.one);
            if (pose.Space == SkillSpace.TwoD)
            {
                using (new Handles.DrawingScope(Color.green,Gizmos.matrix))
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
                }
                return;
            }
            if (shape is AttackBoxDetectionData box)
            {
                Gizmos.DrawCube(Vector3.zero, box.Scale);
                Gizmos.color = new Color(0, 1, .2f, .85f); Gizmos.DrawWireCube(Vector3.zero, box.Scale);
            }
            else if (shape is AttackSphereDetectionData sphere)
            {
                Gizmos.DrawSphere(Vector3.zero, sphere.Radius);
                Gizmos.color = new Color(0, 1, .2f, .85f); Gizmos.DrawWireSphere(Vector3.zero, sphere.Radius);
            }
            else if (shape is AttackFanDetectionData fan) Gizmos.DrawMesh(FanMesh(fan));
        }
        finally { Gizmos.color = oldColor; Gizmos.matrix = oldMatrix; }
    }
}
#endif


}
