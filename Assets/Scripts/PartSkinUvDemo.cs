using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// 使用 onehand 序列（RG 存皮肤 UV）+ partex 皮肤图，在 UI 上预览换肤与帧动画。
/// Bad North / AssetStudio 注意：导出 PNG 会丢失工程里的「线性贴图」标记，onehand 若被当成 sRGB 导入，RG 会被错误伽马解码，身体和贴图会对不齐或发糊——请用菜单 Fix Texture Imports，或勾选 Decode SRGB Misimported UV。
/// 若仍差一点，可微调 UV Scale / Offset（游戏内可能对图集做了整体缩放/裁边）。AssetStudio 从块贴图（ASTC 等）解压成 PNG 也会带来轻微柔化。
/// 若未绑定 RawImage/Dropdown，会在运行时搭建简易 Canvas。
/// </summary>
[DisallowMultipleComponent]
public class PartSkinUvDemo : MonoBehaviour
{
    [SerializeField] Material remapMaterialTemplate;
    [SerializeField] Texture2D[] uvFrames;
    [SerializeField] Texture2D[] skins;
    [SerializeField] float frameRate = 12f;
    [Tooltip("每个源像素在屏幕上占多少格（整数）。只放大显示，不能比原图更细；源图很小时可调到 10～16。糊多半是原图分辨率低或见下方 Canvas 模式。")]
    [SerializeField] [Min(1)] int previewPixelScale = 10;
    [Tooltip("开启后 Canvas 使用 Constant Pixel Size，避免按参考分辨率缩放带来的非整数像素发糊（推荐）")]
    [SerializeField] bool constantPixelSizeCanvas = true;
    [Tooltip("Constant Pixel Size 下的全局倍数，整体再放大/缩小 UI")]
    [SerializeField] [Min(0.25f)] float constantCanvasScaleFactor = 1f;
    [SerializeField] bool vFlip;
    [Tooltip("若左右/上下与身体部位错位，可勾选尝试交换 RG 对应的 UV 轴")]
    [SerializeField] bool swapRG;
    [Tooltip("将采样点对齐到皮肤纹理像素中心，一般应开启；若边缘更怪可关掉对比")]
    [SerializeField] bool texelCenterSkin = true;
    [Tooltip("onehand 在 Unity 里若仍勾了 sRGB，勾选此项在 Shader 里把 RG 从「误解码的线性」拉回近似原始字节比例")]
    [SerializeField] bool decodeSrgbMisimportedUV;
    [Tooltip("对皮肤图集 UV 的整体缩放（游戏内 atlas 边距等与 1:1 不一致时可调）")]
    [SerializeField] Vector2 uvScale = Vector2.one;
    [Tooltip("对皮肤图集 UV 的平移")]
    [SerializeField] Vector2 uvOffset = Vector2.zero;
    [SerializeField] RawImage previewImage;
    [SerializeField] Dropdown skinDropdown;
    [Tooltip("未绑定 UI 引用时是否在运行时创建 Canvas / Dropdown / EventSystem")]
    [SerializeField] bool buildUiIfNeeded = true;
    [Tooltip("运行时按 [ 上一套、] 下一套皮肤（循环）")]
    [SerializeField] bool cycleSkinsWithBracketKeys = true;

    Material _remapInstance;
    int _frameIndex;
    float _frameTimer;
    int _skinIndex;

    static readonly Regex s_FrameNum = new Regex(@"(\d+)", RegexOptions.Compiled);

    void Awake()
    {
        if (remapMaterialTemplate == null)
        {
            Debug.LogError("PartSkinUvDemo: 请在 Inspector 指定 remapMaterialTemplate（PartTexUVRemap.mat）。");
            enabled = false;
            return;
        }

        _remapInstance = new Material(remapMaterialTemplate);
        ApplyRemapMaterialParams();

        if (buildUiIfNeeded && previewImage == null)
            BuildRuntimeUi();

        if (previewImage != null)
        {
            previewImage.material = _remapInstance;
            previewImage.texture = Texture2D.whiteTexture;
            previewImage.color = Color.white;
            previewImage.raycastTarget = false;
            ConfigurePreviewForCrispPixels(previewImage);
        }

        EnsureEventSystem();

        if (skinDropdown != null)
        {
            skinDropdown.onValueChanged.RemoveAllListeners();
            skinDropdown.ClearOptions();
            var opts = new List<Dropdown.OptionData>();
            foreach (var s in skins)
                opts.Add(new Dropdown.OptionData(s != null ? s.name : "(null)"));
            skinDropdown.AddOptions(opts);
            skinDropdown.onValueChanged.AddListener(OnSkinDropdown);
            skinDropdown.SetValueWithoutNotify(Mathf.Clamp(_skinIndex, 0, Mathf.Max(0, skins.Length - 1)));
        }

        SortUvFramesIfNeeded();
        PointFilterAllLoadedTextures();
        ApplySkinToMaterial();
        ShowFrame(0);
    }

    void PointFilterAllLoadedTextures()
    {
        if (uvFrames != null)
        {
            foreach (var t in uvFrames)
                EnsurePointFilter(t);
        }
        if (skins != null)
        {
            foreach (var t in skins)
                EnsurePointFilter(t);
        }
    }

    void OnDestroy()
    {
        if (_remapInstance != null)
            Destroy(_remapInstance);
    }

    void Update()
    {
        if (cycleSkinsWithBracketKeys && skins != null && skins.Length > 1 && Keyboard.current != null)
        {
            if (Keyboard.current.leftBracketKey.wasPressedThisFrame)
                SetSkinIndex(_skinIndex - 1);
            if (Keyboard.current.rightBracketKey.wasPressedThisFrame)
                SetSkinIndex(_skinIndex + 1);
        }

        if (uvFrames == null || uvFrames.Length == 0 || _remapInstance == null)
            return;

        _frameTimer += Time.deltaTime;
        float step = frameRate > 0.01f ? 1f / frameRate : 0.1f;
        while (_frameTimer >= step)
        {
            _frameTimer -= step;
            _frameIndex = (_frameIndex + 1) % uvFrames.Length;
            ShowFrame(_frameIndex);
        }
    }

    /// <summary>切换到指定皮肤索引（0 .. skins.Length-1），可与 UI 下拉或其它脚本联动。</summary>
    public void SetSkinIndex(int index)
    {
        if (skins == null || skins.Length == 0)
            return;
        int n = skins.Length;
        _skinIndex = (index % n + n) % n;
        if (skinDropdown != null)
            skinDropdown.SetValueWithoutNotify(_skinIndex);
        ApplySkinToMaterial();
    }

    void ApplyRemapMaterialParams()
    {
        if (_remapInstance == null) return;
        _remapInstance.SetFloat("_VFlip", vFlip ? 1f : 0f);
        _remapInstance.SetFloat("_SwapRG", swapRG ? 1f : 0f);
        _remapInstance.SetFloat("_TexelCenterSkin", texelCenterSkin ? 1f : 0f);
        _remapInstance.SetFloat("_DecodeSrgbUV", decodeSrgbMisimportedUV ? 1f : 0f);
        _remapInstance.SetVector("_UVScale", new Vector4(uvScale.x, uvScale.y, 0f, 0f));
        _remapInstance.SetVector("_UVOffset", new Vector4(uvOffset.x, uvOffset.y, 0f, 0f));
    }

    void OnSkinDropdown(int index)
    {
        _skinIndex = index;
        ApplySkinToMaterial();
    }

    void ApplySkinToMaterial()
    {
        if (_remapInstance == null || skins == null || skins.Length == 0)
            return;
        _skinIndex = Mathf.Clamp(_skinIndex, 0, skins.Length - 1);
        var skin = skins[_skinIndex];
        if (skin != null)
        {
            EnsurePointFilter(skin);
            _remapInstance.SetTexture("_SkinTex", skin);
            _remapInstance.SetVector("_SkinAtlasDim", new Vector4(skin.width, skin.height, 0f, 0f));
        }
    }

    static void EnsurePointFilter(Texture2D tex)
    {
        if (tex == null) return;
        tex.filterMode = FilterMode.Point;
        tex.anisoLevel = 0;
        tex.wrapMode = TextureWrapMode.Clamp;
    }

    static void ConfigurePreviewForCrispPixels(RawImage raw)
    {
        if (raw == null) return;
        raw.raycastTarget = false;
    }

    void ShowFrame(int index)
    {
        if (uvFrames == null || uvFrames.Length == 0 || previewImage == null)
            return;
        index = Mathf.Clamp(index, 0, uvFrames.Length - 1);
        var uv = uvFrames[index];
        if (uv == null) return;

        EnsurePointFilter(uv);
        previewImage.texture = uv;
        _remapInstance.SetTexture("_UVTex", uv);
        previewImage.SetNativeSize();
        int s = Mathf.Max(1, previewPixelScale);
        var rt = previewImage.rectTransform;
        rt.sizeDelta = new Vector2(uv.width * s, uv.height * s);
        SnapRectToPixels(rt);
    }

    static void SnapRectToPixels(RectTransform rt)
    {
        if (rt == null) return;
        var p = rt.anchoredPosition;
        rt.anchoredPosition = new Vector2(Mathf.Round(p.x), Mathf.Round(p.y));
    }

    void EnsureEventSystem()
    {
        if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() != null)
            return;

        var es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        es.AddComponent<InputSystemUIInputModule>();
    }

    void BuildRuntimeUi()
    {
        var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.pixelPerfect = true;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        if (constantPixelSizeCanvas)
        {
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = constantCanvasScaleFactor;
        }
        else
        {
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            scaler.referencePixelsPerUnit = 100f;
        }

        var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(canvasGo.transform, false);
        var panelRt = panel.GetComponent<RectTransform>();
        panelRt.anchorMin = Vector2.zero;
        panelRt.anchorMax = Vector2.one;
        panelRt.offsetMin = Vector2.zero;
        panelRt.offsetMax = Vector2.zero;
        panel.GetComponent<Image>().color = new Color(0.12f, 0.12f, 0.14f, 1f);

        var rawGo = new GameObject("UVPreview", typeof(RectTransform), typeof(RawImage));
        rawGo.transform.SetParent(canvasGo.transform, false);
        var rawRt = rawGo.GetComponent<RectTransform>();
        rawRt.anchorMin = new Vector2(0.5f, 0.5f);
        rawRt.anchorMax = new Vector2(0.5f, 0.5f);
        rawRt.pivot = new Vector2(0.5f, 0.5f);
        rawRt.anchoredPosition = new Vector2(0f, 40f);
        previewImage = rawGo.GetComponent<RawImage>();
        ConfigurePreviewForCrispPixels(previewImage);

        Sprite uiSprite = Sprite.Create(Texture2D.whiteTexture,
            new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f);
        var res = new DefaultControls.Resources
        {
            standard = uiSprite,
            background = uiSprite,
            inputField = uiSprite,
            knob = uiSprite,
            checkmark = uiSprite,
            dropdown = uiSprite,
            mask = uiSprite
        };
        var dropGo = DefaultControls.CreateDropdown(res);
        dropGo.name = "SkinDropdown";
        dropGo.transform.SetParent(canvasGo.transform, false);
        var dropRt = dropGo.GetComponent<RectTransform>();
        dropRt.anchorMin = new Vector2(0.5f, 1f);
        dropRt.anchorMax = new Vector2(0.5f, 1f);
        dropRt.pivot = new Vector2(0.5f, 1f);
        dropRt.anchoredPosition = new Vector2(0f, -48f);
        dropRt.sizeDelta = new Vector2(360f, 36f);
        skinDropdown = dropGo.GetComponent<Dropdown>();
    }

    void SortUvFramesIfNeeded()
    {
        if (uvFrames == null || uvFrames.Length <= 1)
            return;
        var list = new List<Texture2D>(uvFrames);
        list.Sort(CompareUvFrame);
        uvFrames = list.ToArray();
    }

    static int CompareUvFrame(Texture2D a, Texture2D b)
    {
        if (a == null && b == null) return 0;
        if (a == null) return 1;
        if (b == null) return -1;
        int na = ExtractTrailingNumber(a.name);
        int nb = ExtractTrailingNumber(b.name);
        if (na != nb) return na.CompareTo(nb);
        return string.Compare(a.name, b.name, StringComparison.Ordinal);
    }

    static int ExtractTrailingNumber(string name)
    {
        if (string.IsNullOrEmpty(name)) return 0;
        var matches = s_FrameNum.Matches(name);
        if (matches.Count == 0) return 0;
        int best = int.MaxValue;
        for (int i = 0; i < matches.Count; i++)
        {
            if (int.TryParse(matches[i].Groups[1].Value, out int v))
                best = Mathf.Min(best, v);
        }
        return best == int.MaxValue ? 0 : best;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (_remapInstance != null)
            ApplyRemapMaterialParams();
    }
#endif

    public void SetTextureArrays(Texture2D[] frames, Texture2D[] skinTextures)
    {
        uvFrames = frames;
        skins = skinTextures;
        SortUvFramesIfNeeded();
        if (skinDropdown != null)
        {
            skinDropdown.onValueChanged.RemoveAllListeners();
            skinDropdown.ClearOptions();
            var opts = new List<Dropdown.OptionData>();
            foreach (var s in skins)
                opts.Add(new Dropdown.OptionData(s != null ? s.name : "(null)"));
            skinDropdown.AddOptions(opts);
            skinDropdown.onValueChanged.AddListener(OnSkinDropdown);
            skinDropdown.SetValueWithoutNotify(Mathf.Clamp(_skinIndex, 0, Mathf.Max(0, skins.Length - 1)));
        }
        PointFilterAllLoadedTextures();
        ApplySkinToMaterial();
        _frameIndex = 0;
        ShowFrame(0);
    }
}
