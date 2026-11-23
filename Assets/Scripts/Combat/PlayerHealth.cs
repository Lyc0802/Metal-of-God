using UnityEngine;

/// <summary>
/// 管理玩家生命值與死亡事件，供戰鬥及其他能力查詢。
/// </summary>
public class PlayerHealth : MonoBehaviour
{
    [Min(1)]
    public float MaxHealth = 100;
    [Min(0)]
    public float CurrentHealth = 100;
    public bool IsDead { get; private set; }
    public event System.Action Died;
    /// <summary>玩家實際受到傷害時觸發，參數為本次傷害量。</summary>
    public event System.Action<float> Damaged;

    private void Awake()
    {
        Clamp();
        if (CurrentHealth <= 0)
        {
            Die();
        }
    }

    private void OnValidate()
    {
        Clamp();
    }

    private void Clamp()
    {
        MaxHealth = Mathf.Max(1, MaxHealth);
        CurrentHealth = Mathf.Clamp(CurrentHealth, 0, MaxHealth);
    }

    public void TakeDamage(float damage)
    {
        if (IsDead || damage <= 0)
        {
            return;
        }

        CurrentHealth = Mathf.Clamp(CurrentHealth - damage, 0, MaxHealth);
        Damaged?.Invoke(damage);
        if (CurrentHealth <= 0)
        {
            Die();
        }
    }

    public void Heal(float amount)
    {
        if (!IsDead && amount > 0)
        {
            CurrentHealth = Mathf.Clamp(CurrentHealth + amount, 0, MaxHealth);
        }
    }

    public void Die()
    {
        if (IsDead)
        {
            return;
        }

        IsDead = true;
        CurrentHealth = 0;
        Died?.Invoke();
    }
}
