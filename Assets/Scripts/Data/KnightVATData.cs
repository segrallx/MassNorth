using UnityEngine;

/// <summary>
/// 存储 VAT 烘焙的动画元数据。
/// </summary>
[CreateAssetMenu(fileName = "KnightVATData", menuName = "MassNorth/Knight VAT Data")]
public class KnightVATData : ScriptableObject
{
    public int vertexCount;
    public int totalFrames;
    public float fps = 30f;

    public int walkStartFrame;
    public int walkFrameCount;

    public int attackStartFrame;
    public int attackFrameCount;
}
