using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

/// <summary>
/// Knight 方阵：原生 Animator 骨骼动画 + 点击移动。
/// 默认 Idle 持剑站立，点击地板后全体跑向目标位置，到达后回 Idle。
/// </summary>
public class KnightSquad : MonoBehaviour, IFormationRenderer
{
    [Header("方阵设置")]
    public GameObject knightPrefab;
    [Tooltip("剑的 FBX（可选，不设则不装备）")]
    public GameObject swordPrefab;
    public int rows = 5;
    public int cols = 8;
    public float spacing = 0.8f;

    [Header("颜色（按材质部件）")]
    public Color armorColor = new Color(0.12f, 0.12f, 0.12f, 1f);
    public Color skinColor = new Color(0.6f, 0.45f, 0.26f, 1f);
    public Color bootsColor = new Color(0.025f, 0.01f, 0.006f, 1f);

    [Header("剑挂载调整")]
    public Vector3 swordOffset = Vector3.zero;
    public Vector3 swordRotation = new Vector3(-90f, 0f, 0f);

    [Header("移动")]
    public float moveSpeed = 2.0f;
    public LayerMask groundLayer = ~0;

    private enum SquadState { Idle, Moving }

    private struct KnightInstance
    {
        public GameObject go;
        public Animation anim;
        public SkinnedMeshRenderer smr;
        public Vector3 targetPos;
    }

    private readonly List<KnightInstance> _instances = new List<KnightInstance>();
    private SquadState _state = SquadState.Idle;
    public Vector3 FormationCenter
    {
        get
        {
            if (_instances.Count == 0) return transform.position;
            Vector3 sum = Vector3.zero;
            int count = 0;
            foreach (var inst in _instances)
            {
                if (inst.go != null) { sum += inst.go.transform.position; count++; }
            }
            return count > 0 ? sum / count : transform.position;
        }
    }
    private Vector3 _formationCenter;

    // Legacy animation clips (cloned)
    private AnimationClip _idleClip;
    private AnimationClip _runClip;

    private const string IdleAnim = "Idle";
    private const string RunAnim = "Run";

    // 目标标记
    private LineRenderer _marker;
    private const int MarkerSegments = 32;

    private void Start()
    {
        if (knightPrefab == null)
        {
            Debug.LogError("knightPrefab 未设置！请拖入 KnightCharacter FBX。");
            enabled = false;
            return;
        }

        CreateMarker();
        LoadClips();
        SpawnFormation();
    }

    private void LoadClips()
    {
        #if UNITY_EDITOR
        string path = UnityEditor.AssetDatabase.GetAssetPath(knightPrefab);
        if (string.IsNullOrEmpty(path)) return;

        var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path);
        foreach (var asset in assets)
        {
            if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__"))
            {
                if (clip.name.Contains("Idle_swordRight") && _idleClip == null)
                    _idleClip = CloneLegacy(clip);
                else if (clip.name.Contains("Run_swordRight") && _runClip == null)
                    _runClip = CloneLegacy(clip);
            }
        }

        // Fallback: 如果没找到 sword 变体，用普通版
        if (_idleClip == null || _runClip == null)
        {
            foreach (var asset in assets)
            {
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                {
                    if (_idleClip == null && clip.name == "Idle")
                        _idleClip = CloneLegacy(clip);
                    if (_runClip == null && clip.name == "Run")
                        _runClip = CloneLegacy(clip);
                }
            }
        }

        Debug.Log($"动画: Idle={_idleClip?.name ?? "MISSING"}, Run={_runClip?.name ?? "MISSING"}");
        #endif
    }

    private static AnimationClip CloneLegacy(AnimationClip source)
    {
        var clone = new AnimationClip();
        clone.name = source.name;
        clone.legacy = true;
        clone.wrapMode = WrapMode.Loop;

        #if UNITY_EDITOR
        var bindings = UnityEditor.AnimationUtility.GetCurveBindings(source);
        foreach (var binding in bindings)
        {
            var curve = UnityEditor.AnimationUtility.GetEditorCurve(source, binding);
            clone.SetCurve(binding.path, binding.type, binding.propertyName, curve);
        }
        #endif

        return clone;
    }

    private void SpawnFormation()
    {
        ClearFormation();

        spacing = CalcSpacing();
        _formationCenter = transform.position;

        var positions = CalcFormationPositions(_formationCenter);

        for (int i = 0; i < positions.Length; i++)
        {
            float scale = Random.Range(0.9f, 1.1f);
            var go = Instantiate(knightPrefab, positions[i], Quaternion.identity, transform);
            go.transform.localScale = Vector3.one * scale;

            // 移除 Animator，用 Legacy Animation
            var existingAnimator = go.GetComponentInChildren<Animator>();
            if (existingAnimator != null)
                DestroyImmediate(existingAnimator);

            var animComp = go.AddComponent<Animation>();

            if (_idleClip != null)
                animComp.AddClip(_idleClip, IdleAnim);
            if (_runClip != null)
                animComp.AddClip(_runClip, RunAnim);

            // 默认播放 Idle，随机相位
            if (_idleClip != null)
            {
                animComp.clip = _idleClip;
                animComp.Play(IdleAnim);
                var state = animComp[IdleAnim];
                if (state != null)
                {
                    state.time = Random.Range(0f, state.length);
                    state.speed = Random.Range(0.9f, 1.1f);
                }
                animComp.Sample();
                animComp.Play(IdleAnim);
            }

            // 装备剑
            AttachSword(go);

            var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
            ApplyColors(smr);

            _instances.Add(new KnightInstance
            {
                go = go,
                anim = animComp,
                smr = smr,
                targetPos = positions[i]
            });
        }
    }

    private void AttachSword(GameObject knight)
    {
        if (swordPrefab == null) return;

        // 找右手骨骼
        Transform handBone = FindBoneRecursive(knight.transform, "Hand.R");
        if (handBone == null)
            handBone = FindBoneRecursive(knight.transform, "HandR");
        if (handBone == null)
            handBone = FindBoneRecursive(knight.transform, "hand.R");
        if (handBone == null)
        {
            Debug.LogWarning("找不到右手骨骼，跳过装备剑");
            return;
        }

        var sword = Instantiate(swordPrefab, handBone);
        // 剑的 Y 轴是长度方向（柄在底部约 Y=0）。
        // 挂到手骨后需要：旋转使剑指向前方，偏移使剑柄在手心。
        sword.transform.localPosition = swordOffset;
        sword.transform.localRotation = Quaternion.Euler(swordRotation);
        sword.transform.localScale = Vector3.one;

        // 移除剑上可能存在的 Animator
        var swordAnimator = sword.GetComponentInChildren<Animator>();
        if (swordAnimator != null)
            DestroyImmediate(swordAnimator);
    }

    private static Transform FindBoneRecursive(Transform root, string boneName)
    {
        if (root.name.Contains(boneName))
            return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var found = FindBoneRecursive(root.GetChild(i), boneName);
            if (found != null) return found;
        }
        return null;
    }

    private void Update()
    {
        HandleInput();
        HandleMovement();
    }

    private void HandleInput()
    {
        var mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;

        var ray = Camera.main.ScreenPointToRay(mouse.position.ReadValue());
        if (!Physics.Raycast(ray, out var hit, 500f, groundLayer)) return;

        Vector3 clickPos = hit.point;
        clickPos.y = 0; // 保持在地面

        _formationCenter = clickPos;
        ShowMarker(clickPos);
        var targets = CalcFormationPositions(_formationCenter);

        for (int i = 0; i < _instances.Count && i < targets.Length; i++)
        {
            var inst = _instances[i];
            inst.targetPos = targets[i];
            _instances[i] = inst;
        }

        if (_state != SquadState.Moving)
        {
            _state = SquadState.Moving;
            foreach (var inst in _instances)
            {
                if (inst.anim != null && _runClip != null)
                    inst.anim.CrossFade(RunAnim, 0.2f);
            }
        }
    }

    private void HandleMovement()
    {
        if (_state != SquadState.Moving) return;

        bool allArrived = true;
        float dt = Time.deltaTime;

        for (int i = 0; i < _instances.Count; i++)
        {
            var inst = _instances[i];
            if (inst.go == null) continue;

            Vector3 current = inst.go.transform.position;
            Vector3 target = inst.targetPos;
            float dist = Vector3.Distance(current, target);

            if (dist > 0.05f)
            {
                allArrived = false;
                inst.go.transform.position = Vector3.MoveTowards(current, target, moveSpeed * dt);

                // 面向移动方向
                Vector3 dir = target - current;
                dir.y = 0;
                if (dir.sqrMagnitude > 0.001f)
                    inst.go.transform.rotation = Quaternion.LookRotation(dir);
            }
        }

        if (allArrived)
        {
            _state = SquadState.Idle;
            if (_marker != null) _marker.enabled = false;
            foreach (var inst in _instances)
            {
                if (inst.anim != null && _idleClip != null)
                    inst.anim.CrossFade(IdleAnim, 0.2f);
            }
        }
    }

    // ---- 目标标记 ----

    private void CreateMarker()
    {
        var markerGo = new GameObject("MoveMarker");
        markerGo.transform.SetParent(transform);
        _marker = markerGo.AddComponent<LineRenderer>();
        _marker.useWorldSpace = true;
        _marker.loop = true;
        _marker.positionCount = MarkerSegments;
        _marker.startWidth = 0.08f;
        _marker.endWidth = 0.08f;
        _marker.startColor = Color.green;
        _marker.endColor = Color.green;
        _marker.material = new Material(Shader.Find("Sprites/Default"));
        _marker.material.color = Color.green;
        _marker.enabled = false;
    }

    private void ShowMarker(Vector3 center)
    {
        if (_marker == null) return;
        // 圆圈半径 = 方阵对角线的一半
        float rx = (cols - 1) * spacing * 0.5f;
        float rz = (rows - 1) * spacing * 0.5f;
        float radius = Mathf.Sqrt(rx * rx + rz * rz) + spacing * 0.5f;

        for (int i = 0; i < MarkerSegments; i++)
        {
            float angle = i * Mathf.PI * 2f / MarkerSegments;
            _marker.SetPosition(i, center + new Vector3(Mathf.Cos(angle) * radius, 0.02f, Mathf.Sin(angle) * radius));
        }
        _marker.enabled = true;
    }

    // ---- IFormationRenderer ----

    public void Init(int newRows, int newCols, GameObject prefab)
    {
        rows = newRows;
        cols = newCols;
        knightPrefab = prefab;
        if (Application.isPlaying)
        {
            LoadClips();
            SpawnFormation();
        }
    }

    public void SetAnimState(int state)
    {
        // 0 = idle, 1 = run
        if (state == 0)
        {
            _state = SquadState.Idle;
            foreach (var inst in _instances)
                if (inst.anim != null) inst.anim.CrossFade(IdleAnim, 0.2f);
        }
    }

    public void SetTeamColor(Color color)
    {
        armorColor = color;
        foreach (var inst in _instances)
            ApplyColors(inst.smr);
    }

    // ---- Internal ----

    private Vector3[] CalcFormationPositions(Vector3 center)
    {
        int count = rows * cols;
        var positions = new Vector3[count];
        float offsetX = (cols - 1) * spacing * 0.5f;
        float offsetZ = (rows - 1) * spacing * 0.5f;

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                int idx = r * cols + c;
                positions[idx] = center + new Vector3(
                    c * spacing - offsetX,
                    0,
                    r * spacing - offsetZ
                );
            }
        }
        return positions;
    }

    private float CalcSpacing()
    {
        var probe = Instantiate(knightPrefab);
        var renderers = probe.GetComponentsInChildren<Renderer>();
        var bounds = new Bounds(probe.transform.position, Vector3.zero);
        foreach (var r in renderers)
            bounds.Encapsulate(r.bounds);
        DestroyImmediate(probe);

        float bodySize = Mathf.Max(bounds.size.x, bounds.size.z);
        return bodySize * 1.8f;
    }

    private void ApplyColors(SkinnedMeshRenderer smr)
    {
        if (smr == null) return;
        var materials = smr.sharedMaterials;

        for (int i = 0; i < materials.Length; i++)
        {
            string matName = materials[i] != null ? materials[i].name : "";
            Color col;
            if (matName.Contains("Skin"))
                col = skinColor;
            else if (matName.Contains("Boot"))
                col = bootsColor;
            else
                col = armorColor;

            var block = new MaterialPropertyBlock();
            smr.GetPropertyBlock(block, i);
            block.SetColor("_Color", col);
            block.SetColor("_BaseColor", col);
            smr.SetPropertyBlock(block, i);
        }
    }

    private void ClearFormation()
    {
        foreach (var inst in _instances)
        {
            if (inst.go != null)
                Destroy(inst.go);
        }
        _instances.Clear();
    }

    private void OnValidate()
    {
        if (Application.isPlaying && _instances.Count > 0)
        {
            foreach (var inst in _instances)
                ApplyColors(inst.smr);
        }
    }

    private void OnDestroy()
    {
        ClearFormation();
    }
}
