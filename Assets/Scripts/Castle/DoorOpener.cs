using UnityEngine;

/// <summary>
/// 大門的開啟動畫：兩扇門板繞外側門軸往門後（+Z）轉開。
/// 玩家每攻擊一次（門的血量下降），門縫就開得更大一點（最多 CrackAngle）；被打破時整個打開（FullAngle）。
/// 搭配 BreakableObstacle（DestroyOnBreak = false）：打破後碰撞與導航障礙被關掉，玩家就能走過去。
/// </summary>
[RequireComponent(typeof(BreakableObstacle))]
public class DoorOpener : MonoBehaviour
{
    [Tooltip("左 / 右門板的門軸（門板是它的子物件，繞它的 Y 軸轉）。")]
    public Transform LeftLeaf, RightLeaf;
    [Tooltip("門還沒被打破時，血量歸零前最多開到這個角度（門縫）。")]
    [Range(0, 60)] public float CrackAngle = 22;
    [Tooltip("被打破後完全打開的角度。")]
    [Range(30, 130)] public float FullAngle = 105;
    [Tooltip("開門速度（度 / 秒）。")]
    [Min(1)] public float Speed = 70;
    [Tooltip("被打時的震動幅度（度）。")]
    [Min(0)] public float ShakeAngle = 2.5f;

    private BreakableObstacle obstacle;
    private float angle, lastHealth, shake;

    private void Awake()
    {
        obstacle = GetComponent<BreakableObstacle>();
        lastHealth = obstacle.CurrentHealth;
    }

    private void Update()
    {
        if (obstacle.CurrentHealth < lastHealth - .01f)
        {
            lastHealth = obstacle.CurrentHealth;
            shake = 1f;
        }

        float damaged = 1f - Mathf.Clamp01(obstacle.CurrentHealth / Mathf.Max(1f, obstacle.MaxHealth));
        float target = obstacle.IsBroken ? FullAngle : CrackAngle * damaged;
        float speed = obstacle.IsBroken ? Speed : Speed * 1.5f;
        angle = Mathf.MoveTowards(angle, target, speed * Time.deltaTime);
        shake = Mathf.MoveTowards(shake, 0, Time.deltaTime * 4f);
        float jitter = Mathf.Sin(Time.time * 60f) * ShakeAngle * shake;
        // 左扇：往 -Y 轉，右扇：往 +Y 轉（兩扇都朝 +Z 方向開）。
        if (LeftLeaf)
        {
            LeftLeaf.localRotation = Quaternion.Euler(0, -(angle + jitter), 0);
        }

        if (RightLeaf)
        {
            RightLeaf.localRotation = Quaternion.Euler(0, angle + jitter, 0);
        }
    }
}
