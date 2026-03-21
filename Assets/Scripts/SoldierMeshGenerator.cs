using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 程序化生成 Bad North 风格 Q 版小兵 Mesh。
/// 大头短腿比例，倒角/球面几何体，多部位顶点色标记。
/// 顶点色约定：R = 摆动标记(0/0.25/0.75)，G = 部位ID(0.0~0.8)
/// </summary>
public static class SoldierMeshGenerator
{
    // Q版比例参数
    private const float HeadRadius = 0.14f; // 头部 icosphere 半径 (~0.28 直径)
    private const float BodyW = 0.32f, BodyH = 0.30f, BodyD = 0.18f;
    private const float LimbW = 0.10f, LimbH = 0.22f, LimbD = 0.10f;

    private const float ArmOffsetX = BodyW / 2f + LimbW / 2f;
    private const float LegOffsetX = BodyW / 4f;
    private const float LegOffsetY = -BodyH / 2f - LimbH / 2f;
    private const float ArmOffsetY = BodyH / 2f - LimbH / 2f;

    // 部位 ID (存入顶点色 G 通道)
    private const float PartArmor  = 0.0f;
    private const float PartSkin   = 0.2f;
    private const float PartCloth  = 0.4f;
    private const float PartWeapon = 0.6f;
    private const float PartHelmet = 0.8f;

    public static Mesh Generate()
    {
        var parts = new List<CombineInstance>();

        float headY = BodyH / 2f + HeadRadius + 0.02f;

        // 身体 — 倒角盒
        parts.Add(MakePart(Vector3.zero,
            new Vector3(BodyW, BodyH, BodyD), 0f, PartArmor, useBevel: true));

        // 头 — icosphere
        parts.Add(MakeIcospherePart(
            new Vector3(0, headY, 0), HeadRadius, 0f, PartSkin));

        // 左臂 (swingGroup A)
        parts.Add(MakePart(new Vector3(-ArmOffsetX, ArmOffsetY, 0),
            new Vector3(LimbW, LimbH, LimbD), 0.25f, PartArmor, useBevel: true));

        // 右臂 (swingGroup B)
        parts.Add(MakePart(new Vector3(ArmOffsetX, ArmOffsetY, 0),
            new Vector3(LimbW, LimbH, LimbD), 0.75f, PartArmor, useBevel: true));

        // 左腿 (swingGroup B)
        parts.Add(MakePart(new Vector3(-LegOffsetX, LegOffsetY, 0),
            new Vector3(LimbW, LimbH, LimbD), 0.75f, PartCloth, useBevel: true));

        // 右腿 (swingGroup A)
        parts.Add(MakePart(new Vector3(LegOffsetX, LegOffsetY, 0),
            new Vector3(LimbW, LimbH, LimbD), 0.25f, PartCloth, useBevel: true));

        // 头盔 — 头部上方的半球壳
        parts.Add(MakeHelmetPart(
            new Vector3(0, headY + HeadRadius * 0.15f, 0), HeadRadius * 1.12f));

        // 盾牌 — 左臂外侧扁平圆角矩形
        float shieldX = -ArmOffsetX - LimbW / 2f - 0.03f;
        parts.Add(MakePart(new Vector3(shieldX, ArmOffsetY + 0.02f, 0),
            new Vector3(0.04f, 0.20f, 0.16f), 0.25f, PartWeapon, useBevel: true));

        // 长矛 — 细长四棱柱 + 锥形矛头
        float spearBaseY = ArmOffsetY + LimbH * 0.3f;
        // 矛杆
        parts.Add(MakePart(
            new Vector3(ArmOffsetX + LimbW * 0.3f, spearBaseY + 0.25f, 0),
            new Vector3(0.025f, 0.50f, 0.025f), 0.75f, PartWeapon));
        // 矛头 (小锥体用扁盒近似)
        parts.Add(MakePart(
            new Vector3(ArmOffsetX + LimbW * 0.3f, spearBaseY + 0.53f, 0),
            new Vector3(0.05f, 0.08f, 0.02f), 0.75f, PartWeapon));

        var arr = parts.ToArray();
        var mesh = new Mesh { name = "SoldierMesh" };
        mesh.CombineMeshes(arr, true, true);
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();

        for (int j = 0; j < arr.Length; j++)
            Object.Destroy(arr[j].mesh);

        return mesh;
    }

    private static CombineInstance MakePart(Vector3 center, Vector3 size,
        float swingTag, float partId, bool useBevel = false)
    {
        Mesh m = useBevel ? CreateBeveledBox(size, Mathf.Min(size.x, size.y, size.z) * 0.2f, swingTag, partId)
                          : CreateTaggedCube(size, swingTag, partId);
        return new CombineInstance
        {
            mesh = m,
            transform = Matrix4x4.Translate(center)
        };
    }

    private static CombineInstance MakeIcospherePart(Vector3 center, float radius,
        float swingTag, float partId)
    {
        var m = CreateIcosphere(radius, swingTag, partId);
        return new CombineInstance
        {
            mesh = m,
            transform = Matrix4x4.Translate(center)
        };
    }

    private static CombineInstance MakeHelmetPart(Vector3 center, float radius)
    {
        var m = CreateHemisphere(radius, 0f, PartHelmet);
        return new CombineInstance
        {
            mesh = m,
            transform = Matrix4x4.Translate(center)
        };
    }

    // ---- Geometry generators ----

    /// <summary>
    /// 带倒角的盒体 (chamfer cut on 8 corners).
    /// </summary>
    private static Mesh CreateBeveledBox(Vector3 size, float bevel, float swingTag, float partId)
    {
        // Strategy: start with cube verts, then chamfer each corner by
        // pulling it inward along each axis. This creates 3 new triangular faces per corner.
        float hx = size.x / 2f, hy = size.y / 2f, hz = size.z / 2f;
        float bx = Mathf.Min(bevel, hx * 0.5f);
        float by = Mathf.Min(bevel, hy * 0.5f);
        float bz = Mathf.Min(bevel, hz * 0.5f);

        var verts = new List<Vector3>();
        var tris = new List<int>();

        // For each of the 6 faces, create a beveled quad (inset from edges)
        // Then connect beveled edges with chamfer triangles.

        // Corner positions (8 corners of the box)
        // Each corner spawns 3 vertices (one per adjacent face)
        // c[i][axis] = corner vertex on that face
        Vector3[] corners = {
            new Vector3(-hx, -hy, -hz), new Vector3(-hx, -hy,  hz), new Vector3(-hx,  hy, -hz), new Vector3(-hx,  hy,  hz),
            new Vector3( hx, -hy, -hz), new Vector3( hx, -hy,  hz), new Vector3( hx,  hy, -hz), new Vector3( hx,  hy,  hz),
        };

        // For simplicity, build beveled box as 6 inset faces + 12 edge quads + 8 corner tris
        // Face normals and their 4 corners (indices into corners[])
        int[][] faceCorners = {
            new[]{0, 2, 6, 4}, // -Z front
            new[]{5, 7, 3, 1}, // +Z back
            new[]{2, 3, 7, 6}, // +Y top
            new[]{0, 4, 5, 1}, // -Y bottom
            new[]{0, 1, 3, 2}, // -X left
            new[]{4, 6, 7, 5}, // +X right
        };
        Vector3[] faceNormals = {
            Vector3.back, Vector3.forward, Vector3.up, Vector3.down, Vector3.left, Vector3.right
        };
        // Axis indices for bevel offset per face normal direction
        // For each face, define which axes are tangent
        // faceNormal axis -> the other two axes are tangent
        int[][] faceTangentAxes = {
            new[]{0, 1}, // -Z: x, y tangent
            new[]{0, 1}, // +Z
            new[]{0, 2}, // +Y: x, z tangent
            new[]{0, 2}, // -Y
            new[]{2, 1}, // -X: z, y tangent
            new[]{2, 1}, // +X
        };
        float[] halfSizes = { hx, hy, hz };
        float[] bevelSizes = { bx, by, bz };

        // Build each face as an inset quad
        for (int f = 0; f < 6; f++)
        {
            int baseIdx = verts.Count;
            var fc = faceCorners[f];
            for (int v = 0; v < 4; v++)
            {
                Vector3 corner = corners[fc[v]];
                Vector3 inset = corner;
                // Pull toward center along the two tangent axes
                int ax0 = faceTangentAxes[f][0];
                int ax1 = faceTangentAxes[f][1];
                inset[ax0] -= Mathf.Sign(corner[ax0]) * bevelSizes[ax0];
                inset[ax1] -= Mathf.Sign(corner[ax1]) * bevelSizes[ax1];
                verts.Add(inset);
            }
            tris.AddRange(new[]{baseIdx, baseIdx+1, baseIdx+2, baseIdx, baseIdx+2, baseIdx+3});
        }

        // Build 12 edge strips (connecting inset faces)
        // Each edge is shared by 2 faces. We connect the inset edges with a quad.
        int[][] edges = {
            // bottom ring (-Y)
            new[]{3,0, 0,3}, new[]{3,1, 5,2}, new[]{3,4, 4,1}, new[]{3,5, 1,0},
            // top ring (+Y)
            new[]{2,0, 0,1}, new[]{2,1, 1,2}, new[]{2,4, 4,3}, new[]{2,5, 5,0},
            // vertical edges
            new[]{0,4, 3,0}, new[]{0,5, 2,3}, new[]{1,4, 3,2}, new[]{1,5, 2,1},
        };
        // This is getting complex. Let me use a simpler approach: just scale vertices at corners.

        // Actually, let's use a cleaner approach: generate a standard cube and
        // pull corner vertices inward to create chamfers.
        verts.Clear();
        tris.Clear();

        // Generate cube with split normals (24 verts), then displace corner verts
        return CreateChamferedBoxSimple(hx, hy, hz, bx, by, bz, swingTag, partId);
    }

    private static Mesh CreateChamferedBoxSimple(float hx, float hy, float hz,
        float bx, float by, float bz, float swingTag, float partId)
    {
        // Build a beveled box by constructing 6 faces (each as inset quad)
        // + 12 edge quads + 8 corner triangles
        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var tris = new List<int>();

        // Helper to add a quad (2 tris)
        void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n)
        {
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
            norms.Add(n); norms.Add(n); norms.Add(n); norms.Add(n);
            tris.Add(i); tris.Add(i+1); tris.Add(i+2);
            tris.Add(i); tris.Add(i+2); tris.Add(i+3);
        }

        void AddTri(Vector3 a, Vector3 b, Vector3 c, Vector3 n)
        {
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c);
            norms.Add(n); norms.Add(n); norms.Add(n);
            tris.Add(i); tris.Add(i+1); tris.Add(i+2);
        }

        // 6 main faces (inset from edges by bevel amount)
        // +Y (top)
        AddQuad(new Vector3(-hx+bx, hy, -hz+bz), new Vector3(-hx+bx, hy, hz-bz),
                new Vector3(hx-bx, hy, hz-bz),   new Vector3(hx-bx, hy, -hz+bz), Vector3.up);
        // -Y (bottom)
        AddQuad(new Vector3(-hx+bx, -hy, hz-bz), new Vector3(-hx+bx, -hy, -hz+bz),
                new Vector3(hx-bx, -hy, -hz+bz), new Vector3(hx-bx, -hy, hz-bz), Vector3.down);
        // +X (right)
        AddQuad(new Vector3(hx, -hy+by, -hz+bz), new Vector3(hx, hy-by, -hz+bz),
                new Vector3(hx, hy-by, hz-bz),   new Vector3(hx, -hy+by, hz-bz), Vector3.right);
        // -X (left)
        AddQuad(new Vector3(-hx, -hy+by, hz-bz), new Vector3(-hx, hy-by, hz-bz),
                new Vector3(-hx, hy-by, -hz+bz), new Vector3(-hx, -hy+by, -hz+bz), Vector3.left);
        // -Z (front)
        AddQuad(new Vector3(-hx+bx, -hy+by, -hz), new Vector3(-hx+bx, hy-by, -hz),
                new Vector3(hx-bx, hy-by, -hz),   new Vector3(hx-bx, -hy+by, -hz), Vector3.back);
        // +Z (back)
        AddQuad(new Vector3(hx-bx, -hy+by, hz), new Vector3(hx-bx, hy-by, hz),
                new Vector3(-hx+bx, hy-by, hz), new Vector3(-hx+bx, -hy+by, hz), Vector3.forward);

        // 12 edge bevels (connecting inset faces)
        // Top-front edge
        AddQuad(new Vector3(-hx+bx, hy, -hz+bz), new Vector3(hx-bx, hy, -hz+bz),
                new Vector3(hx-bx, hy-by, -hz),   new Vector3(-hx+bx, hy-by, -hz),
                new Vector3(0, by, -bz).normalized);
        // Top-back edge
        AddQuad(new Vector3(hx-bx, hy, hz-bz), new Vector3(-hx+bx, hy, hz-bz),
                new Vector3(-hx+bx, hy-by, hz),   new Vector3(hx-bx, hy-by, hz),
                new Vector3(0, by, bz).normalized);
        // Top-left edge
        AddQuad(new Vector3(-hx+bx, hy, hz-bz), new Vector3(-hx+bx, hy, -hz+bz),
                new Vector3(-hx, hy-by, -hz+bz),  new Vector3(-hx, hy-by, hz-bz),
                new Vector3(-bx, by, 0).normalized);
        // Top-right edge
        AddQuad(new Vector3(hx-bx, hy, -hz+bz), new Vector3(hx-bx, hy, hz-bz),
                new Vector3(hx, hy-by, hz-bz),    new Vector3(hx, hy-by, -hz+bz),
                new Vector3(bx, by, 0).normalized);

        // Bottom-front edge
        AddQuad(new Vector3(hx-bx, -hy, -hz+bz), new Vector3(-hx+bx, -hy, -hz+bz),
                new Vector3(-hx+bx, -hy+by, -hz), new Vector3(hx-bx, -hy+by, -hz),
                new Vector3(0, -by, -bz).normalized);
        // Bottom-back edge
        AddQuad(new Vector3(-hx+bx, -hy, hz-bz), new Vector3(hx-bx, -hy, hz-bz),
                new Vector3(hx-bx, -hy+by, hz),   new Vector3(-hx+bx, -hy+by, hz),
                new Vector3(0, -by, bz).normalized);
        // Bottom-left edge
        AddQuad(new Vector3(-hx+bx, -hy, -hz+bz), new Vector3(-hx+bx, -hy, hz-bz),
                new Vector3(-hx, -hy+by, hz-bz),  new Vector3(-hx, -hy+by, -hz+bz),
                new Vector3(-bx, -by, 0).normalized);
        // Bottom-right edge
        AddQuad(new Vector3(hx-bx, -hy, hz-bz), new Vector3(hx-bx, -hy, -hz+bz),
                new Vector3(hx, -hy+by, -hz+bz),  new Vector3(hx, -hy+by, hz-bz),
                new Vector3(bx, -by, 0).normalized);

        // Front-left edge
        AddQuad(new Vector3(-hx, hy-by, -hz+bz), new Vector3(-hx+bx, hy-by, -hz),
                new Vector3(-hx+bx, -hy+by, -hz), new Vector3(-hx, -hy+by, -hz+bz),
                new Vector3(-bx, 0, -bz).normalized);
        // Front-right edge
        AddQuad(new Vector3(hx-bx, hy-by, -hz), new Vector3(hx, hy-by, -hz+bz),
                new Vector3(hx, -hy+by, -hz+bz),  new Vector3(hx-bx, -hy+by, -hz),
                new Vector3(bx, 0, -bz).normalized);
        // Back-left edge
        AddQuad(new Vector3(-hx+bx, hy-by, hz), new Vector3(-hx, hy-by, hz-bz),
                new Vector3(-hx, -hy+by, hz-bz),  new Vector3(-hx+bx, -hy+by, hz),
                new Vector3(-bx, 0, bz).normalized);
        // Back-right edge
        AddQuad(new Vector3(hx, hy-by, hz-bz), new Vector3(hx-bx, hy-by, hz),
                new Vector3(hx-bx, -hy+by, hz),   new Vector3(hx, -hy+by, hz-bz),
                new Vector3(bx, 0, bz).normalized);

        // 8 corner triangles
        // Each corner has 3 adjacent inset vertices
        // Top-front-left
        AddTri(new Vector3(-hx+bx, hy, -hz+bz), new Vector3(-hx, hy-by, -hz+bz), new Vector3(-hx+bx, hy-by, -hz),
               new Vector3(-1,1,-1).normalized);
        // Top-front-right
        AddTri(new Vector3(hx-bx, hy, -hz+bz), new Vector3(hx-bx, hy-by, -hz), new Vector3(hx, hy-by, -hz+bz),
               new Vector3(1,1,-1).normalized);
        // Top-back-left
        AddTri(new Vector3(-hx+bx, hy, hz-bz), new Vector3(-hx+bx, hy-by, hz), new Vector3(-hx, hy-by, hz-bz),
               new Vector3(-1,1,1).normalized);
        // Top-back-right
        AddTri(new Vector3(hx-bx, hy, hz-bz), new Vector3(hx, hy-by, hz-bz), new Vector3(hx-bx, hy-by, hz),
               new Vector3(1,1,1).normalized);
        // Bottom-front-left
        AddTri(new Vector3(-hx+bx, -hy, -hz+bz), new Vector3(-hx+bx, -hy+by, -hz), new Vector3(-hx, -hy+by, -hz+bz),
               new Vector3(-1,-1,-1).normalized);
        // Bottom-front-right
        AddTri(new Vector3(hx-bx, -hy, -hz+bz), new Vector3(hx, -hy+by, -hz+bz), new Vector3(hx-bx, -hy+by, -hz),
               new Vector3(1,-1,-1).normalized);
        // Bottom-back-left
        AddTri(new Vector3(-hx+bx, -hy, hz-bz), new Vector3(-hx, -hy+by, hz-bz), new Vector3(-hx+bx, -hy+by, hz),
               new Vector3(-1,-1,1).normalized);
        // Bottom-back-right
        AddTri(new Vector3(hx-bx, -hy, hz-bz), new Vector3(hx-bx, -hy+by, hz), new Vector3(hx, -hy+by, hz-bz),
               new Vector3(1,-1,1).normalized);

        var colors = new Color[verts.Count];
        var tagColor = new Color(swingTag, partId, 0, 1);
        for (int i = 0; i < colors.Length; i++)
            colors[i] = tagColor;

        var mesh = new Mesh();
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetTriangles(tris, 0);
        mesh.colors = colors;
        return mesh;
    }

    /// <summary>
    /// 程序化 icosphere (20 面体, 12 顶点) 用于头部。
    /// </summary>
    private static Mesh CreateIcosphere(float radius, float swingTag, float partId)
    {
        var mesh = new Mesh();

        float t = (1f + Mathf.Sqrt(5f)) / 2f; // golden ratio

        var rawVerts = new List<Vector3>
        {
            new Vector3(-1,  t,  0), new Vector3( 1,  t,  0), new Vector3(-1, -t,  0), new Vector3( 1, -t,  0),
            new Vector3( 0, -1,  t), new Vector3( 0,  1,  t), new Vector3( 0, -1, -t), new Vector3( 0,  1, -t),
            new Vector3( t,  0, -1), new Vector3( t,  0,  1), new Vector3(-t,  0, -1), new Vector3(-t,  0,  1),
        };

        // Normalize and scale
        for (int i = 0; i < rawVerts.Count; i++)
            rawVerts[i] = rawVerts[i].normalized * radius;

        int[] rawTris =
        {
            0,11,5,  0,5,1,   0,1,7,   0,7,10,  0,10,11,
            1,5,9,   5,11,4,  11,10,2, 10,7,6,  7,1,8,
            3,9,4,   3,4,2,   3,2,6,   3,6,8,   3,8,9,
            4,9,5,   2,4,11,  6,2,10,  8,6,7,   9,8,1,
        };

        // Flat shading: duplicate vertices per triangle for hard edges
        var verts = new Vector3[rawTris.Length];
        var normals = new Vector3[rawTris.Length];
        var triIndices = new int[rawTris.Length];
        for (int i = 0; i < rawTris.Length; i += 3)
        {
            Vector3 a = rawVerts[rawTris[i]];
            Vector3 b = rawVerts[rawTris[i+1]];
            Vector3 c = rawVerts[rawTris[i+2]];
            Vector3 n = Vector3.Cross(b - a, c - a).normalized;
            verts[i] = a; verts[i+1] = b; verts[i+2] = c;
            normals[i] = n; normals[i+1] = n; normals[i+2] = n;
            triIndices[i] = i; triIndices[i+1] = i+1; triIndices[i+2] = i+2;
        }

        var colors = new Color[verts.Length];
        var tagColor = new Color(swingTag, partId, 0, 1);
        for (int i = 0; i < colors.Length; i++)
            colors[i] = tagColor;

        mesh.vertices = verts;
        mesh.normals = normals;
        mesh.triangles = triIndices;
        mesh.colors = colors;
        return mesh;
    }

    /// <summary>
    /// 上半球壳用于头盔。
    /// </summary>
    private static Mesh CreateHemisphere(float radius, float swingTag, float partId)
    {
        var mesh = new Mesh();
        int segments = 8;
        int rings = 4; // only upper hemisphere

        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var tris = new List<int>();

        // Generate hemisphere vertices
        for (int ring = 0; ring <= rings; ring++)
        {
            float phi = Mathf.PI * 0.5f * ring / rings; // 0 to PI/2
            float y = Mathf.Sin(phi) * radius;
            float r = Mathf.Cos(phi) * radius;

            for (int seg = 0; seg <= segments; seg++)
            {
                float theta = 2f * Mathf.PI * seg / segments;
                float x = Mathf.Cos(theta) * r;
                float z = Mathf.Sin(theta) * r;
                verts.Add(new Vector3(x, y, z));
                norms.Add(new Vector3(x, y, z).normalized);
            }
        }

        // Generate triangles
        int stride = segments + 1;
        for (int ring = 0; ring < rings; ring++)
        {
            for (int seg = 0; seg < segments; seg++)
            {
                int curr = ring * stride + seg;
                int next = curr + stride;
                tris.Add(curr); tris.Add(next); tris.Add(curr + 1);
                tris.Add(curr + 1); tris.Add(next); tris.Add(next + 1);
            }
        }

        var colors = new Color[verts.Count];
        var tagColor = new Color(swingTag, partId, 0, 1);
        for (int i = 0; i < colors.Length; i++)
            colors[i] = tagColor;

        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetTriangles(tris, 0);
        mesh.colors = colors;
        return mesh;
    }

    /// <summary>
    /// 简单带标记的立方体（用于矛杆等不需要倒角的部件）。
    /// </summary>
    private static Mesh CreateTaggedCube(Vector3 size, float swingTag, float partId)
    {
        var mesh = new Mesh();

        float x = size.x / 2f, y = size.y / 2f, z = size.z / 2f;

        Vector3[] verts =
        {
            new Vector3(-x,-y,-z), new Vector3(-x, y,-z), new Vector3( x, y,-z), new Vector3( x,-y,-z),
            new Vector3( x,-y, z), new Vector3( x, y, z), new Vector3(-x, y, z), new Vector3(-x,-y, z),
            new Vector3(-x, y,-z), new Vector3(-x, y, z), new Vector3( x, y, z), new Vector3( x, y,-z),
            new Vector3(-x,-y, z), new Vector3(-x,-y,-z), new Vector3( x,-y,-z), new Vector3( x,-y, z),
            new Vector3(-x,-y, z), new Vector3(-x, y, z), new Vector3(-x, y,-z), new Vector3(-x,-y,-z),
            new Vector3( x,-y,-z), new Vector3( x, y,-z), new Vector3( x, y, z), new Vector3( x,-y, z),
        };

        int[] tris =
        {
            0,1,2, 0,2,3,   4,5,6, 4,6,7,
            8,9,10, 8,10,11, 12,13,14, 12,14,15,
            16,17,18, 16,18,19, 20,21,22, 20,22,23,
        };

        Vector3[] normals =
        {
            Vector3.back, Vector3.back, Vector3.back, Vector3.back,
            Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward,
            Vector3.up, Vector3.up, Vector3.up, Vector3.up,
            Vector3.down, Vector3.down, Vector3.down, Vector3.down,
            Vector3.left, Vector3.left, Vector3.left, Vector3.left,
            Vector3.right, Vector3.right, Vector3.right, Vector3.right,
        };

        var colors = new Color[verts.Length];
        var tagColor = new Color(swingTag, partId, 0, 1);
        for (int i = 0; i < colors.Length; i++)
            colors[i] = tagColor;

        mesh.vertices = verts;
        mesh.triangles = tris;
        mesh.normals = normals;
        mesh.colors = colors;

        return mesh;
    }
}
