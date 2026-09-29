// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit
{
using System;
using System.Collections.Generic;
using UnityEngine;

public static class MeshGenerator
{
    public static Mesh GenarteFanMesh(float insideRadius, float radius, float height, float angle)
    {
        if (float.IsNaN(angle) || float.IsInfinity(angle) || angle <= 0 || angle > 360 ||
            float.IsNaN(radius) || float.IsInfinity(radius) || radius <= 0 ||
            float.IsNaN(height) || float.IsInfinity(height) || height <= 0 ||
            float.IsNaN(insideRadius) || float.IsInfinity(insideRadius) || insideRadius < 0 || insideRadius >= radius)
            throw new ArgumentException("Invalid fan dimensions");
        int segments = Mathf.Max(1, Mathf.CeilToInt(angle / 2.5f));
        int top = (segments + 1) * 2;
        var vertices = new Vector3[top * 2];
        var triangles = new List<int>(segments * 24 + 12);
        for (int i = 0; i <= segments; i++)
        {
            var direction = Quaternion.AngleAxis(angle * .5f - angle * i / segments, Vector3.up) * Vector3.forward;
            vertices[i * 2] = direction * insideRadius;
            vertices[i * 2 + 1] = direction * radius;
            vertices[top + i * 2] = vertices[i * 2] + Vector3.up * height;
            vertices[top + i * 2 + 1] = vertices[i * 2 + 1] + Vector3.up * height;
            if (i == segments) continue;
            int a = i * 2, b = a + 1, c = a + 2, d = a + 3;
            Quad(triangles, b, d, a, c);
            Quad(triangles, top + a, top + c, top + b, top + d);
            if (insideRadius > 0) Quad(triangles, a, c, top + a, top + c);
            Quad(triangles, b, top + b, d, top + d);
        }
        // A complete circle has no radial closing walls.
        if (angle < 360)
        {
            Quad(triangles, 0, top, 1, top + 1);
            int last = segments * 2;
            Quad(triangles, last, last + 1, top + last, top + last + 1);
        }
        var mesh = new Mesh { name = "Skill Attack Fan" };
        mesh.vertices = vertices; mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return mesh;
    }

    private static void Quad(List<int> triangles, int a, int b, int c, int d)
    {
        triangles.Add(a); triangles.Add(b); triangles.Add(c);
        triangles.Add(c); triangles.Add(b); triangles.Add(d);
    }
}

}
