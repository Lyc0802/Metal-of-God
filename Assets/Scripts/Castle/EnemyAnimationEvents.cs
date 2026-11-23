using UnityEngine;

/// <summary>
/// 接收 RFA 攻擊動畫裡的 Animation Event（SetAttackStart / SetAttackEnd）。
/// 敵人的傷害判定由 TestEnemy / EnemyShooter 的計時負責，所以這裡只記錄揮擊區間，避免事件找不到接收者而報錯。
/// </summary>
public class EnemyAnimationEvents : MonoBehaviour
{
    /// <summary>目前是否在攻擊動畫的揮擊區間內。</summary>
    public bool AttackActive { get; private set; }

    public void SetAttackStart()
    {
        AttackActive = true;
    }

    public void SetAttackEnd()
    {
        AttackActive = false;
    }
}
