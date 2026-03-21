using UnityEngine;

/// <summary>
/// 用 Graphics.DrawMeshInstanced 批量渲染 Knight 方阵。
/// 使用 VAT (Vertex Animation Texture) 驱动骨骼动画。
/// </summary>
public class KnightFormationRenderer : MonoBehaviour
{
    [Header("方阵设置")]
    public int rows = 5;
    public int cols = 8;
    public float spacing = 0.8f;

    [Header("渲染资源")]
    [Tooltip("VATBaker 生成的 bind pose Mesh")]
    public Mesh knightMesh;
    [Tooltip("配好 InstancedKnight shader 和 VAT 纹理的材质")]
    public Material knightMaterial;
    [Tooltip("VATBaker 生成的动画元数据")]
    public KnightVATData vatData;
    [Tooltip("VATBaker 生成的 VAT 纹理")]
    public Texture2D vatTexture;

    [Header("阵营")]
    public Color teamColor = new Color(0.3f, 0.5f, 1.0f, 1f);

    [Header("动画")]
    [Tooltip("0 = Walking, 1 = Attack")]
    [Range(0, 1)]
    public int animState = 0;

    private Matrix4x4[] _matrices;
    private MaterialPropertyBlock _propBlock;
    private float[] _animPhases;
    private float[] _animSpeeds;
    private float[] _animStates;
    private Vector4[] _tintColors;

    private const int MaxInstancesPerBatch = 1023;

    private void Start()
    {
        _propBlock = new MaterialPropertyBlock();

        // 自动从烘焙输出路径加载资源（如果 Inspector 中未手动指定）
        #if UNITY_EDITOR
        AutoLoadBakedAssets();
        #endif

        if (knightMesh == null)
        {
            Debug.LogError("knightMesh 未设置！请先运行 MassNorth > Bake Knight VAT，然后拖入生成的 KnightBindPose Mesh。");
            enabled = false;
            return;
        }

        // 自动创建材质
        if (knightMaterial == null)
        {
            var shader = Shader.Find("MassNorth/InstancedKnight");
            if (shader == null)
            {
                Debug.LogError("找不到 MassNorth/InstancedKnight shader！");
                enabled = false;
                return;
            }
            knightMaterial = new Material(shader);
            knightMaterial.enableInstancing = true;
        }

        // 自动配置材质的 VAT 参数
        if (vatData != null)
        {
            knightMaterial.SetFloat("_TotalFrames", vatData.totalFrames);
            knightMaterial.SetFloat("_FPS", vatData.fps);
            knightMaterial.SetFloat("_WalkStart", vatData.walkStartFrame);
            knightMaterial.SetFloat("_WalkCount", vatData.walkFrameCount);
            knightMaterial.SetFloat("_AttackStart", vatData.attackStartFrame);
            knightMaterial.SetFloat("_AttackCount", vatData.attackFrameCount);
        }

        if (vatTexture != null)
        {
            knightMaterial.SetTexture("_VATTex", vatTexture);
        }

        RebuildFormation();
    }

    #if UNITY_EDITOR
    private void AutoLoadBakedAssets()
    {
        const string dir = "Assets/Models/Knight";

        if (knightMesh == null)
            knightMesh = UnityEditor.AssetDatabase.LoadAssetAtPath<Mesh>($"{dir}/KnightBindPose.asset");

        if (vatData == null)
            vatData = UnityEditor.AssetDatabase.LoadAssetAtPath<KnightVATData>($"{dir}/KnightVATData.asset");

        if (vatTexture == null)
            vatTexture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/KnightVAT.exr");
    }
    #endif

    private void RebuildFormation()
    {
        int count = rows * cols;
        _matrices = new Matrix4x4[count];
        _animPhases = new float[count];
        _animSpeeds = new float[count];
        _animStates = new float[count];
        _tintColors = new Vector4[count];

        Vector3 origin = transform.position;
        float offsetX = (cols - 1) * spacing * 0.5f;
        float offsetZ = (rows - 1) * spacing * 0.5f;

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                int idx = r * cols + c;
                Vector3 pos = origin + new Vector3(
                    c * spacing - offsetX,
                    0,
                    r * spacing - offsetZ
                );
                pos.x += Random.Range(-0.05f, 0.05f);
                pos.z += Random.Range(-0.05f, 0.05f);

                float scale = Random.Range(0.9f, 1.1f);
                _matrices[idx] = Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one * scale);

                _animPhases[idx] = Random.Range(0f, Mathf.PI * 2f);
                _animSpeeds[idx] = Random.Range(0.9f, 1.1f);
                _animStates[idx] = animState;
                _tintColors[idx] = teamColor;
            }
        }
    }

    /// <summary>
    /// 切换全阵动画状态。0 = Walking, 1 = Attack。
    /// </summary>
    public void SetAnimState(int state)
    {
        animState = state;
        if (_animStates == null) return;
        for (int i = 0; i < _animStates.Length; i++)
            _animStates[i] = state;
    }

    private void Update()
    {
        if (knightMesh == null || knightMaterial == null) return;

        // 同步 animState 变更
        if (_animStates != null && _animStates.Length > 0 && _animStates[0] != animState)
            SetAnimState(animState);

        int total = _matrices.Length;

        for (int offset = 0; offset < total; offset += MaxInstancesPerBatch)
        {
            int batchSize = Mathf.Min(MaxInstancesPerBatch, total - offset);

            var batchMatrices = new Matrix4x4[batchSize];
            var batchPhases = new float[batchSize];
            var batchSpeeds = new float[batchSize];
            var batchStates = new float[batchSize];
            var batchTints = new Vector4[batchSize];

            System.Array.Copy(_matrices, offset, batchMatrices, 0, batchSize);
            System.Array.Copy(_animPhases, offset, batchPhases, 0, batchSize);
            System.Array.Copy(_animSpeeds, offset, batchSpeeds, 0, batchSize);
            System.Array.Copy(_animStates, offset, batchStates, 0, batchSize);
            System.Array.Copy(_tintColors, offset, batchTints, 0, batchSize);

            _propBlock.SetFloatArray("_AnimPhase", batchPhases);
            _propBlock.SetFloatArray("_AnimSpeed", batchSpeeds);
            _propBlock.SetFloatArray("_AnimState", batchStates);
            _propBlock.SetVectorArray("_TintColor", batchTints);

            Graphics.DrawMeshInstanced(
                knightMesh,
                0,
                knightMaterial,
                batchMatrices,
                batchSize,
                _propBlock
            );
        }
    }

    private void OnValidate()
    {
        if (Application.isPlaying && _matrices != null)
            RebuildFormation();
    }
}
