using UnityEngine;

/// <summary>
/// 用 Graphics.DrawMeshInstanced 批量渲染一个方阵的所有小兵。
/// 挂到空 GameObject 上，运行即可看到一整块方阵。
/// </summary>
public class FormationRenderer : MonoBehaviour
{
    [Header("方阵设置")]
    [Tooltip("行数")]
    public int rows = 5;
    [Tooltip("列数")]
    public int cols = 8;
    [Tooltip("兵与兵之间的间距")]
    public float spacing = 0.6f;

    [Header("渲染设置")]
    public Material soldierMaterial;
    [Tooltip("阵营颜色")]
    public Color teamColor = new Color(0.2f, 0.4f, 0.9f, 1f);

    [Header("动画")]
    [Tooltip("是否播放行走动画")]
    public bool animateWalk = true;

    private Mesh _soldierMesh;
    private Matrix4x4[] _matrices;
    private MaterialPropertyBlock _propBlock;
    private float[] _animPhases;
    private float[] _animSpeeds;
    private Vector4[] _armorColors;
    private Vector4[] _skinColors;
    private Vector4[] _clothColors;

    // DrawMeshInstanced 每次最多 1023 个实例
    private const int MaxInstancesPerBatch = 1023;

    // 预设肤色
    private static readonly Color[] SkinPresets = {
        new Color(0.93f, 0.78f, 0.63f, 1f),
        new Color(0.87f, 0.72f, 0.55f, 1f),
        new Color(0.80f, 0.65f, 0.50f, 1f),
        new Color(0.96f, 0.84f, 0.70f, 1f),
        new Color(0.75f, 0.58f, 0.45f, 1f),
    };

    private void Start()
    {
        _soldierMesh = SoldierMeshGenerator.Generate();
        _propBlock = new MaterialPropertyBlock();

        if (soldierMaterial == null)
        {
            var shader = Shader.Find("MassNorth/InstancedSoldier");
            if (shader == null)
            {
                Debug.LogError("找不到 MassNorth/InstancedSoldier shader！请确保 shader 已编译。");
                enabled = false;
                return;
            }
            soldierMaterial = new Material(shader);
            soldierMaterial.enableInstancing = true;
        }

        RebuildFormation();
    }

    private void RebuildFormation()
    {
        int count = rows * cols;
        _matrices = new Matrix4x4[count];
        _animPhases = new float[count];
        _animSpeeds = new float[count];
        _armorColors = new Vector4[count];
        _skinColors = new Vector4[count];
        _clothColors = new Vector4[count];

        Vector3 origin = transform.position;
        float offsetX = (cols - 1) * spacing * 0.5f;
        float offsetZ = (rows - 1) * spacing * 0.5f;

        // teamColor 暗色变体作为衣物色
        Color clothBase = teamColor * 0.6f;
        clothBase.a = 1f;

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

                // 0.9~1.1 随机缩放
                float scale = Random.Range(0.9f, 1.1f);
                _matrices[idx] = Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one * scale);

                _animPhases[idx] = Random.Range(0f, Mathf.PI * 2f);
                _animSpeeds[idx] = Random.Range(0.9f, 1.1f);

                _armorColors[idx] = teamColor;
                _skinColors[idx] = SkinPresets[Random.Range(0, SkinPresets.Length)];
                _clothColors[idx] = clothBase;
            }
        }
    }

    private void Update()
    {
        if (_soldierMesh == null || soldierMaterial == null) return;

        int total = _matrices.Length;

        for (int offset = 0; offset < total; offset += MaxInstancesPerBatch)
        {
            int batchSize = Mathf.Min(MaxInstancesPerBatch, total - offset);

            var batchMatrices = new Matrix4x4[batchSize];
            var batchPhases = new float[batchSize];
            var batchSpeeds = new float[batchSize];
            var batchArmor = new Vector4[batchSize];
            var batchSkin = new Vector4[batchSize];
            var batchCloth = new Vector4[batchSize];

            System.Array.Copy(_matrices, offset, batchMatrices, 0, batchSize);
            System.Array.Copy(_animPhases, offset, batchPhases, 0, batchSize);
            System.Array.Copy(_animSpeeds, offset, batchSpeeds, 0, batchSize);
            System.Array.Copy(_armorColors, offset, batchArmor, 0, batchSize);
            System.Array.Copy(_skinColors, offset, batchSkin, 0, batchSize);
            System.Array.Copy(_clothColors, offset, batchCloth, 0, batchSize);

            _propBlock.SetFloatArray("_AnimPhase", batchPhases);
            _propBlock.SetFloatArray("_AnimSpeed", batchSpeeds);
            _propBlock.SetVectorArray("_ArmorColor", batchArmor);
            _propBlock.SetVectorArray("_SkinColor", batchSkin);
            _propBlock.SetVectorArray("_ClothColor", batchCloth);

            Graphics.DrawMeshInstanced(
                _soldierMesh,
                0,
                soldierMaterial,
                batchMatrices,
                batchSize,
                _propBlock
            );
        }
    }

    private void OnValidate()
    {
        if (Application.isPlaying && _soldierMesh != null)
            RebuildFormation();
    }

    private void OnDestroy()
    {
        if (_soldierMesh != null)
            Destroy(_soldierMesh);
    }
}
