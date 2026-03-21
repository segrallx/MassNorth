using UnityEngine;

/// <summary>
/// 跟随方阵中心的相机。挂到 Main Camera 上，拖入 KnightSquad 即可。
/// </summary>
public class SquadCamera : MonoBehaviour
{
    [Tooltip("跟随的方阵")]
    public KnightSquad squad;

    [Header("跟随参数")]
    public float smoothSpeed = 5f;

    private Vector3 _initialOffset;

    private void Start()
    {
        if (squad != null)
            _initialOffset = transform.position - squad.FormationCenter;
    }

    private void LateUpdate()
    {
        if (squad == null) return;

        Vector3 desired = squad.FormationCenter + _initialOffset;
        transform.position = Vector3.Lerp(transform.position, desired, smoothSpeed * Time.deltaTime);
    }
}
