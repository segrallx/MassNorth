using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 编辑器工具：将 Knight FBX 的骨骼动画烘焙为 Vertex Animation Texture (VAT)。
/// 生成：VAT 纹理 (EXR) + bind pose 静态 Mesh + 动画元数据 ScriptableObject。
/// </summary>
public static class VATBaker
{
    private const float BakeFPS = 30f;
    private const string OutputDir = "Assets/Models/Knight";
    private const string FBXPath = "Assets/Models/Knight/KnightCharacter.fbx";

    // 需要烘焙的动画 clip 名（FBX 中的 Take 名）
    private static readonly string[] WalkClipNames = { "Walking" };
    private static readonly string[] AttackClipNames = { "Run_swordAttack" };

    [MenuItem("MassNorth/Bake Knight VAT")]
    public static void Bake()
    {
        // 1. 加载 FBX
        var fbxAsset = AssetDatabase.LoadAssetAtPath<GameObject>(FBXPath);
        if (fbxAsset == null)
        {
            Debug.LogError($"找不到 FBX: {FBXPath}");
            return;
        }

        // 2. 实例化
        var go = Object.Instantiate(fbxAsset);
        go.name = "VATBaker_Temp";

        var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
        if (smr == null)
        {
            Debug.LogError("FBX 中找不到 SkinnedMeshRenderer");
            Object.DestroyImmediate(go);
            return;
        }

        // 3. 获取所有动画 clip
        var allClips = AssetDatabase.LoadAllAssetsAtPath(FBXPath)
            .OfType<AnimationClip>()
            .Where(c => !c.name.StartsWith("__preview__"))
            .ToList();

        Debug.Log($"找到 {allClips.Count} 个动画 clip: {string.Join(", ", allClips.Select(c => c.name))}");

        var walkClip = allClips.FirstOrDefault(c => WalkClipNames.Any(n => c.name.Contains(n)));
        var attackClip = allClips.FirstOrDefault(c => AttackClipNames.Any(n => c.name.Contains(n)));

        if (walkClip == null || attackClip == null)
        {
            Debug.LogError($"找不到所需动画 clip。Walk: {walkClip?.name ?? "MISSING"}, Attack: {attackClip?.name ?? "MISSING"}");
            Debug.LogError($"可用 clips: {string.Join(", ", allClips.Select(c => c.name))}");
            Object.DestroyImmediate(go);
            return;
        }

        // 4. 获取 bind pose
        // BakeMesh 返回的是 mesh 本地空间顶点，不含 SMR 父级变换。
        // Blender FBX 通常需要 -90° X 旋转来修正 Z-up → Y-up。
        // 从 SMR 的 transform 链中获取修正矩阵。
        Matrix4x4 rootCorrection = smr.transform.localToWorldMatrix * go.transform.worldToLocalMatrix;
        // 只取旋转部分（忽略平移和缩放），保持 mesh 在原点
        Quaternion correctionRot = rootCorrection.rotation;
        Debug.Log($"SMR 修正旋转: {correctionRot.eulerAngles}");

        var bindPoseMesh = new Mesh();
        smr.BakeMesh(bindPoseMesh, true);
        Vector3[] bindVertsRaw = bindPoseMesh.vertices;
        int vertexCount = bindVertsRaw.Length;

        // 对 bind pose 顶点应用旋转修正
        Vector3[] bindVerts = new Vector3[vertexCount];
        for (int i = 0; i < vertexCount; i++)
            bindVerts[i] = correctionRot * bindVertsRaw[i];

        Debug.Log($"Bind pose 顶点数: {vertexCount}");

        // 5. 采样动画帧
        int walkFrames = Mathf.Max(1, Mathf.RoundToInt(walkClip.length * BakeFPS));
        int attackFrames = Mathf.Max(1, Mathf.RoundToInt(attackClip.length * BakeFPS));
        int totalFrames = walkFrames + attackFrames;

        Debug.Log($"Walking: {walkFrames} frames ({walkClip.length}s), Attack: {attackFrames} frames ({attackClip.length}s), Total: {totalFrames}");

        // VAT 纹理: width = vertexCount, height = totalFrames
        var vatTex = new Texture2D(vertexCount, totalFrames, TextureFormat.RGBAHalf, false);
        vatTex.filterMode = FilterMode.Point; // 精确采样
        vatTex.wrapMode = TextureWrapMode.Clamp;

        var tempMesh = new Mesh();
        int frameRow = 0;

        // 烘焙 Walking
        BakeClipFrames(go, smr, walkClip, bindVerts, vatTex, tempMesh, ref frameRow, walkFrames, vertexCount, correctionRot);
        int walkStart = 0;

        // 烘焙 Attack
        int attackStart = frameRow;
        BakeClipFrames(go, smr, attackClip, bindVerts, vatTex, tempMesh, ref frameRow, attackFrames, vertexCount, correctionRot);

        vatTex.Apply();

        // 6. 保存 VAT 纹理为 EXR
        byte[] exrData = vatTex.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat);
        string exrPath = $"{OutputDir}/KnightVAT.exr";
        System.IO.File.WriteAllBytes(exrPath, exrData);
        AssetDatabase.ImportAsset(exrPath);

        // 设置纹理导入设置
        var texImporter = AssetImporter.GetAtPath(exrPath) as TextureImporter;
        if (texImporter != null)
        {
            texImporter.sRGBTexture = false; // 线性数据
            texImporter.filterMode = FilterMode.Point;
            texImporter.mipmapEnabled = false;
            texImporter.textureCompression = TextureImporterCompression.Uncompressed;
            texImporter.npotScale = TextureImporterNPOTScale.None;
            texImporter.maxTextureSize = Mathf.Max(vertexCount, totalFrames);

            var platformSettings = texImporter.GetDefaultPlatformTextureSettings();
            platformSettings.format = TextureImporterFormat.RGBAHalf;
            platformSettings.overridden = true;
            texImporter.SetPlatformTextureSettings(platformSettings);

            texImporter.SaveAndReimport();
        }

        Debug.Log($"VAT 纹理已保存: {exrPath} ({vertexCount}x{totalFrames})");

        // 7. 创建静态 bind pose Mesh（传入已修正的 bindVerts）
        var staticMesh = CreateStaticMesh(smr, bindPoseMesh, bindVerts, vertexCount, correctionRot);
        string meshPath = $"{OutputDir}/KnightBindPose.asset";
        AssetDatabase.CreateAsset(staticMesh, meshPath);
        Debug.Log($"Bind pose Mesh 已保存: {meshPath}");

        // 8. 创建元数据
        var vatData = ScriptableObject.CreateInstance<KnightVATData>();
        vatData.vertexCount = vertexCount;
        vatData.totalFrames = totalFrames;
        vatData.fps = BakeFPS;
        vatData.walkStartFrame = walkStart;
        vatData.walkFrameCount = walkFrames;
        vatData.attackStartFrame = attackStart;
        vatData.attackFrameCount = attackFrames;

        string dataPath = $"{OutputDir}/KnightVATData.asset";
        AssetDatabase.CreateAsset(vatData, dataPath);
        Debug.Log($"VAT 元数据已保存: {dataPath}");

        // 清理
        Object.DestroyImmediate(go);
        Object.DestroyImmediate(bindPoseMesh);
        Object.DestroyImmediate(tempMesh);
        Object.DestroyImmediate(vatTex);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("=== VAT 烘焙完成 ===");
    }

    private static void BakeClipFrames(GameObject go, SkinnedMeshRenderer smr,
        AnimationClip clip, Vector3[] bindVerts, Texture2D vatTex,
        Mesh tempMesh, ref int frameRow, int frameCount, int vertexCount,
        Quaternion correctionRot)
    {
        for (int f = 0; f < frameCount; f++)
        {
            float time = (float)f / (frameCount - 1) * clip.length;
            if (frameCount == 1) time = 0;

            clip.SampleAnimation(go, time);
            smr.BakeMesh(tempMesh);

            Vector3[] frameVerts = tempMesh.vertices;

            for (int v = 0; v < vertexCount; v++)
            {
                // 对帧顶点也应用旋转修正后再算 delta
                Vector3 correctedVert = correctionRot * frameVerts[v];
                Vector3 delta = correctedVert - bindVerts[v];
                vatTex.SetPixel(v, frameRow, new Color(delta.x, delta.y, delta.z, 1f));
            }
            frameRow++;
        }
    }

    private static Mesh CreateStaticMesh(SkinnedMeshRenderer smr, Mesh bindPoseMesh,
        Vector3[] correctedVerts, int vertexCount, Quaternion correctionRot)
    {
        var sharedMesh = smr.sharedMesh;
        var mesh = new Mesh();
        mesh.name = "KnightBindPose";

        // 使用已旋转修正的顶点
        mesh.vertices = correctedVerts;

        // 法线也需要旋转修正
        Vector3[] rawNormals = bindPoseMesh.normals;
        if (rawNormals != null && rawNormals.Length == vertexCount)
        {
            Vector3[] correctedNormals = new Vector3[vertexCount];
            for (int i = 0; i < vertexCount; i++)
                correctedNormals[i] = correctionRot * rawNormals[i];
            mesh.normals = correctedNormals;
        }

        mesh.tangents = bindPoseMesh.tangents;

        // 合并所有子网格的三角形
        var allTris = new List<int>();
        var vertColors = new Color[vertexCount];

        // 材质名到 partId 的映射
        var materials = smr.sharedMaterials;
        float[] partIds = new float[materials.Length];
        for (int m = 0; m < materials.Length; m++)
        {
            string matName = materials[m] != null ? materials[m].name : "";
            if (matName.Contains("Armor"))
                partIds[m] = 0.0f;
            else if (matName.Contains("Skin"))
                partIds[m] = 0.33f;
            else if (matName.Contains("Boot"))
                partIds[m] = 0.66f;
            else
                partIds[m] = 0.0f;

            Debug.Log($"子网格 {m}: {matName} -> partId {partIds[m]}");
        }

        for (int sub = 0; sub < sharedMesh.subMeshCount; sub++)
        {
            var subTris = sharedMesh.GetTriangles(sub);
            allTris.AddRange(subTris);

            float pid = sub < partIds.Length ? partIds[sub] : 0f;
            foreach (int vi in subTris)
            {
                vertColors[vi] = new Color(pid, 0, 0, 1);
            }
        }

        mesh.triangles = allTris.ToArray();
        mesh.colors = vertColors;

        // UV2: 顶点索引归一化，用于 VAT 采样
        var uv2 = new Vector2[vertexCount];
        for (int i = 0; i < vertexCount; i++)
        {
            uv2[i] = new Vector2((i + 0.5f) / vertexCount, 0);
        }
        mesh.uv2 = uv2;

        // 保留原始 UV
        if (sharedMesh.uv != null && sharedMesh.uv.Length == vertexCount)
            mesh.uv = sharedMesh.uv;

        mesh.RecalculateBounds();

        return mesh;
    }
}
