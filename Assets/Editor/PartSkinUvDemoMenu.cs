using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class PartSkinUvDemoMenu
{
    const string OnehandDir = "Assets/texture/onehand";
    const string PartexDir = "Assets/texture/partex";

    [MenuItem("MassNorth/Part Skin Demo/Fix Texture Imports (recommended)")]
    static void FixTextureImports()
    {
        if (!EditorUtility.DisplayDialog("Part Skin Demo",
                "将 onehand 设为「线性（非 sRGB）」以正确读取 RG 中的 UV 数据（AssetStudio 导出的 PNG 在 Unity 里常被误判为 sRGB，这是错位/发糊的常见原因）；partex 保持 sRGB。\n" +
                "同时：无 mipmap、Point、无压缩、max size 8192。\n\n是否继续？",
                "继续", "取消"))
            return;

        int n = FixFolderImports(OnehandDir, linearData: true);
        n += FixFolderImports(PartexDir, linearData: false);
        Debug.Log($"PartSkinUvDemo: 已重新导入 {n} 张纹理。请再运行场景查看 UV 与皮肤是否对齐。");
    }

    static int FixFolderImports(string folder, bool linearData)
    {
        if (!AssetDatabase.IsValidFolder(folder))
            return 0;

        AssetDatabase.StartAssetEditing();
        int count = 0;
        try
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { folder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti == null)
                    continue;

                ti.sRGBTexture = !linearData;
                ti.filterMode = FilterMode.Point;
                ti.mipmapEnabled = false;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.npotScale = TextureImporterNPOTScale.None;
                ti.maxTextureSize = 8192;
                // 不设置 alphaSource：各版本 API 不一致，且 PNG 默认会保留输入的 Alpha
                ti.SaveAndReimport();
                count++;
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }

        return count;
    }

    [MenuItem("MassNorth/Part Skin Demo/Populate Textures On Selected")]
    static void PopulateSelected()
    {
        var demo = Selection.activeGameObject != null
            ? Selection.activeGameObject.GetComponent<PartSkinUvDemo>()
            : null;
        if (demo == null)
        {
            EditorUtility.DisplayDialog("Part Skin Demo",
                "请先选中挂有 PartSkinUvDemo 组件的 GameObject。", "OK");
            return;
        }

        PopulateDemo(demo);
    }

    [MenuItem("MassNorth/Part Skin Demo/Populate Textures On Selected", true)]
    static bool ValidatePopulateSelected()
    {
        return Selection.activeGameObject != null
               && Selection.activeGameObject.GetComponent<PartSkinUvDemo>() != null;
    }

    [MenuItem("MassNorth/Part Skin Demo/Populate On Scene Demos")]
    static void PopulateAllInScene()
    {
        var demos = Object.FindObjectsByType<PartSkinUvDemo>(FindObjectsSortMode.None);
        if (demos.Length == 0)
        {
            EditorUtility.DisplayDialog("Part Skin Demo",
                "当前场景中没有 PartSkinUvDemo 组件。", "OK");
            return;
        }

        foreach (var d in demos)
            PopulateDemo(d);
    }

    static void PopulateDemo(PartSkinUvDemo demo)
    {
        var frames = LoadTextures(OnehandDir,
            n => n.StartsWith("Onehanded", System.StringComparison.OrdinalIgnoreCase));
        var skinList = LoadTextures(PartexDir, _ => true);

        Undo.RecordObject(demo, "Populate Part Skin Demo Textures");
        demo.SetTextureArrays(frames, skinList);
        EditorUtility.SetDirty(demo);
        Debug.Log($"PartSkinUvDemo: {frames.Length} UV frames, {skinList.Length} skins.");
    }

    static Texture2D[] LoadTextures(string assetFolder, System.Func<string, bool> filterFileNameWithoutExt)
    {
        if (!AssetDatabase.IsValidFolder(assetFolder))
        {
            Debug.LogWarning($"PartSkinUvDemo: 文件夹不存在: {assetFolder}");
            return System.Array.Empty<Texture2D>();
        }

        var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { assetFolder });
        var list = new List<Texture2D>();
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase))
                continue;
            var nameNoExt = Path.GetFileNameWithoutExtension(path);
            if (!filterFileNameWithoutExt(nameNoExt))
                continue;
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex != null)
                list.Add(tex);
        }

        return list.OrderBy(t => t.name, System.StringComparer.Ordinal).ToArray();
    }
}
