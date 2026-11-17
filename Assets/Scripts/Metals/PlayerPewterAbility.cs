using UnityEngine;

/// <summary>
/// 將 Pewter 的啟用狀態轉成移動、跳躍、力量與重量倍率。
/// </summary>
[RequireComponent(typeof(PlayerMetalInventory), typeof(PlayerHealth))]
public class PlayerPewterAbility : MonoBehaviour
{
    [Min(1)]
    public float MovementMultiplier = 1.5f, JumpMultiplier = 1.4f;
    [Min(0)]
    public float WeightBonus = 20;
    [Min(1)]
    public float StrengthMultiplier = 1.5f;
    [Min(0)]
    public float HealthPerSecond = 3;
    private PlayerMetalInventory inventory;
    private PlayerHealth health;
    private void Awake()
    {
        inventory = GetComponent<PlayerMetalInventory>();
        health = GetComponent<PlayerHealth>();
    }

    public bool IsActive => isActiveAndEnabled && inventory && health && !health.IsDead
        && inventory.CanUse(MetalType.Pewter);
    public float CurrentMovementMultiplier => IsActive ? Mathf.Max(1, MovementMultiplier) : 1;
    public float CurrentJumpMultiplier => IsActive ? Mathf.Max(1, JumpMultiplier) : 1;
    public float CurrentStrengthMultiplier => IsActive ? Mathf.Max(1, StrengthMultiplier) : 1;

    private void Update()
    {
        if (IsActive && health.CurrentHealth < health.MaxHealth)
        {
            health.Heal(Mathf.Max(0, HealthPerSecond) * Time.deltaTime);
        }
    }
}
