// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit
{
using UnityEngine;

using System.Collections.Generic;

public static class SkillAttackDetectionTool
{
    /// <summary>Forward ranges share a pose with the Scene renderer. Linear box and
    /// sphere flight also sweep the interval between logical frames to avoid tunnelling.</summary>
    public static Collider[] ForwardDetection(AttackShapeDetectionDataBase data, SkillAttackShape.Pose pose,
        LayerMask layerMask, SkillAttackShape.Pose? previous = null, PhysicsScene? scene = null)
    {
        if (!SkillAttackShape.Validate(data, out _) || data == null) return new Collider[0];
        var results = new HashSet<Collider>();
        AddAt(data, pose, layerMask, results, scene);
        if (previous.HasValue && SkillAttackShape.IsFlight(data))
        {
            var from = previous.Value;
            Vector3 delta = pose.Position - from.Position;
            float distance = delta.magnitude;
            if (distance > .00001f)
            {
                AddAt(data, from, layerMask, results, scene);
                RaycastHit[] swept = null;
                if (data is AttackBoxDetectionData box)
                    swept = SkillPhysicsQuery.BoxCast(from.Position, box.Scale * .5f, delta / distance, from.Rotation, distance, layerMask, scene);
                else if (data is AttackSphereDetectionData sphere)
                    swept = SkillPhysicsQuery.SphereCast(from.Position, sphere.Radius, delta / distance, distance, layerMask, scene);
                if (swept != null) foreach (var hit in swept) if (hit.collider != null) results.Add(hit.collider);
                // Sector flight is evaluated at intermediate positions. This is a
                // bounded approximation; use a box/sphere for thin high-speed bullets.
                if (data is AttackFanDetectionData fan)
                {
                    float step = Mathf.Max(.05f, Mathf.Min(fan.Radius - fan.InsideRadius, fan.Height) * .5f);
                    int count = Mathf.Clamp(Mathf.CeilToInt(distance / step), 1, 128);
                    for (int i = 1; i < count; i++)
                        AddAt(data, new SkillAttackShape.Pose { Position = Vector3.Lerp(from.Position, pose.Position, i / (float)count),
                            Rotation = pose.Rotation }, layerMask, results, scene);
                }
            }
        }
        var array = new Collider[results.Count]; results.CopyTo(array); return array;
    }

    private static void AddAt(AttackShapeDetectionDataBase data, SkillAttackShape.Pose pose, LayerMask mask, HashSet<Collider> results, PhysicsScene? scene)
    {
        Collider[] colliders;
        if (data is AttackBoxDetectionData box) colliders = SkillPhysicsQuery.Box(pose.Position, box.Scale * .5f, pose.Rotation, mask, scene);
        else if (data is AttackSphereDetectionData sphere) colliders = SkillPhysicsQuery.Sphere(pose.Position, sphere.Radius, mask, scene);
        else if (data is AttackFanDetectionData fan)
        {
            Vector3 middle = pose.Position + pose.Rotation * Vector3.up * fan.Height * .5f;
            colliders = SkillPhysicsQuery.Box(middle, new Vector3(fan.Radius, fan.Height * .5f, fan.Radius), pose.Rotation, mask, scene);
            foreach (var collider in colliders)
            {
                // Horizontal annular sector extruded from the foot height. Vertical
                // distance must not shorten its horizontal radius.
                Vector3 local = Quaternion.Inverse(pose.Rotation) * (collider.ClosestPoint(middle) - pose.Position);
                float radius = new Vector2(local.x, local.z).magnitude;
                if (local.y < -.0001f || local.y > fan.Height + .0001f || radius < fan.InsideRadius || radius > fan.Radius) continue;
                if (radius > .00001f && Mathf.Abs(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg) > fan.Angle * .5f) continue;
                results.Add(collider);
            }
            return;
        }
        else return;
        foreach (var collider in colliders) if (collider != null) results.Add(collider);
    }

    private static Collider[] detectionResluts = new Collider[20];
    public static Collider[] ShapeDetection(Transform modelTransform, AttackDetectionDataBase data, AttackDetectionType attackDetectionType, LayerMask layerMask, PhysicsScene? scene = null)
    {
        if (modelTransform == null || data == null)
        {
            CleanDetectionResluts();
            return detectionResluts;
        }

        if (data is AttackShapeDetectionDataBase forward && forward.UseFootOrigin)
            return ForwardDetection(forward, SkillAttackShape.Resolve(forward, SkillAttackShape.Origin.Capture(modelTransform)), layerMask, null, scene);

        switch (attackDetectionType)
        {
            case AttackDetectionType.Box:
                return data is AttackBoxDetectionData boxData
                    ? BoxDetection(modelTransform, boxData, layerMask, scene)
                    : EmptyDetectionResults();
            case AttackDetectionType.Sphere:
                return data is AttackSphereDetectionData sphereData
                    ? SphereDetection(modelTransform, sphereData, layerMask, scene)
                    : EmptyDetectionResults();
            case AttackDetectionType.Fan:
                return data is AttackFanDetectionData fanData
                    ? FanDetection(modelTransform, fanData, layerMask, scene)
                    : EmptyDetectionResults();
        }
        return EmptyDetectionResults();
    }

    public static Collider[] BoxDetection(Transform modelTransform, AttackBoxDetectionData data, LayerMask layerMask, PhysicsScene? scene = null)
    {
        if (modelTransform == null || data == null) return EmptyDetectionResults();
        CleanDetectionResluts();
        Vector3 scale = new Vector3(Mathf.Abs(data.Scale.x), Mathf.Abs(data.Scale.y), Mathf.Abs(data.Scale.z));
        if (scene.HasValue) scene.Value.OverlapBox(modelTransform.TransformPoint(data.Position), scale / 2, detectionResluts, modelTransform.rotation * Quaternion.Euler(data.Rotation), layerMask);
        else Physics.OverlapBoxNonAlloc(modelTransform.TransformPoint(data.Position), scale / 2, detectionResluts, modelTransform.rotation * Quaternion.Euler(data.Rotation), layerMask);
        return detectionResluts;
    }
    public static Collider[] SphereDetection(Transform modelTransform, AttackSphereDetectionData data, LayerMask layerMask, PhysicsScene? scene = null)
    {
        if (modelTransform == null || data == null) return EmptyDetectionResults();
        CleanDetectionResluts();
        if (scene.HasValue) scene.Value.OverlapSphere(modelTransform.TransformPoint(data.Position), Mathf.Max(0, data.Radius), detectionResluts, layerMask, QueryTriggerInteraction.UseGlobal);
        else Physics.OverlapSphereNonAlloc(modelTransform.TransformPoint(data.Position), Mathf.Max(0, data.Radius), detectionResluts, layerMask);
        return detectionResluts;

    }
    public static Collider[] FanDetection(Transform modelTransform, AttackFanDetectionData data, LayerMask layerMask, PhysicsScene? scene = null)
    {
        if (modelTransform == null || data == null) return EmptyDetectionResults();
        CleanDetectionResluts();
        Vector3 size = new Vector3();
        float radius = Mathf.Max(0, data.Radius);
        size.x = radius * 2;
        size.z = size.x;
        size.y = Mathf.Max(0, data.Height);
        Vector3 fanPosition = modelTransform.TransformPoint(data.Position);
        if (scene.HasValue) scene.Value.OverlapBox(fanPosition, size / 2, detectionResluts, modelTransform.rotation * Quaternion.Euler(data.Rotation), layerMask);
        else Physics.OverlapBoxNonAlloc(fanPosition, size / 2, detectionResluts, modelTransform.rotation * Quaternion.Euler(data.Rotation), layerMask);

        // 过滤无效检测
        Vector3 fanForward = modelTransform.rotation * Quaternion.Euler(data.Rotation) * Vector3.forward;
        for (int i = 0; i < detectionResluts.Length; i++)
        {
            if (detectionResluts[i] == null) break;
            // 过滤内半径内的、外半径外的
            Vector3 point = detectionResluts[i].ClosestPoint(fanPosition);
            float distance = Vector3.Distance(point, fanPosition);
            bool remove = distance < Mathf.Max(0, data.InsideRadius) || distance > radius;
            // 过滤不在角度范围内的
            if (!remove)
            {
                Vector3 dir = point - fanPosition;
                float angle = Vector3.Angle(fanForward, dir);
                remove = angle > Mathf.Clamp(data.Angle, 0, 360) * 0.5f;
            }
            if (remove)
            {
                detectionResluts[i] = null;
            }
        }
        return detectionResluts;
    }

    private static void CleanDetectionResluts()
    {
        for (int i = 0; i < detectionResluts.Length; i++)
        {
            detectionResluts[i] = null;
        }
    }

    private static Collider[] EmptyDetectionResults()
    {
        CleanDetectionResluts();
        return detectionResluts;
    }
}

}
