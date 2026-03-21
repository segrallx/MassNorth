using UnityEngine;

/// <summary>
/// 方阵渲染器通用接口。
/// Animator 版 (KnightSquad) 和 VAT 版 (KnightFormationRenderer) 都实现此接口，
/// 上层逻辑可无缝切换渲染方案。
/// </summary>
public interface IFormationRenderer
{
    void Init(int rows, int cols, GameObject prefab);
    void SetAnimState(int state);
    void SetTeamColor(Color color);
}
