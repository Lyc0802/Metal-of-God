using System;
using UnityEngine;

public enum MetalType
{
    Iron,
    Tin,
    Steel,
    Pewter,
    Zinc,
    Copper,
    Brass,
    Bronze,
    None,
    All
}

/// <summary>
/// 單一金屬的開關、剩餘量與容量；Clamp 用於維持有效範圍。
/// </summary>
[Serializable]
public class MetalReserve
{
    public MetalType Type;
    public bool IsEnabled = true;
    [Min(0)]
    public float CurrentAmount = 100;
    [Min(0)]
    public float MaxAmount = 100;
    public void Clamp()
    {
        MaxAmount = Mathf.Max(0, MaxAmount);
        CurrentAmount = Mathf.Clamp(CurrentAmount, 0, MaxAmount);
    }
}

/// <summary>
/// 保存玩家金屬存量與開關，並提供所有能力共用的使用狀態查詢。
/// </summary>
public class PlayerMetalInventory : MonoBehaviour, IMetalAbilityStatus
{
    public MetalReserve[] Metals = CreateDefaults();
    private PlayerHealth health;
    public Transform MetalOwner => this ? transform : null;

    public Vector3 MetalSignalPosition
    {
        get
        {
            var ownerCollider = this ? GetComponent<Collider>() : null;
            return ownerCollider ? ownerCollider.bounds.center : this ? transform.position : Vector3.zero;
        }
    }

    public bool IsMetalStatusActive
    {
        get
        {
            if (!this || !isActiveAndEnabled)
            {
                return false;
            }

            if (!health)
            {
                health = GetComponent<PlayerHealth>();
            }

            return !health || !health.IsDead;
        }
    }

    public bool IsMetalEnabled(MetalType metal) => IsMetalStatusActive && MetalAbilitySignals.IsRealMetal(metal)
        && CanUse(metal);
    public bool IsUsingAnyMetal
    {
        get
        {
            if (!IsMetalStatusActive || Metals == null)
            {
                return false;
            }

            foreach (var reserve in Metals)
            {
                if (reserve != null && MetalAbilitySignals.IsRealMetal(reserve.Type) && reserve.IsEnabled
                    && reserve.CurrentAmount > 0)
                {
                    return true;
                }
            }

            return false;
        }
    }

    private void Awake()
    {
        MetalAbilitySignals.EnsureServices(this, true);
    }

    private void OnEnable()
    {
        MetalAbilitySignals.Register(this);
    }

    private void OnDisable()
    {
        MetalAbilitySignals.Unregister(this);
    }

    public bool CanUse(MetalType type)
    {
        var reserve = Get(type);
        return reserve != null && reserve.IsEnabled && reserve.CurrentAmount > 0;
    }

    public void Add(MetalType type, float amount)
    {
        var reserve = Get(type);
        if (reserve != null)
        {
            reserve.CurrentAmount += Mathf.Max(0, amount);
            reserve.Clamp();
        }
    }

    public bool Consume(MetalType type, float amount)
    {
        var reserve = Get(type);
        if (amount < 0 || !CanUse(type) || reserve.CurrentAmount < amount)
        {
            return false;
        }

        reserve.CurrentAmount -= amount;
        return true;
    }

    private void OnValidate()
    {
        if (Metals == null)
        {
            Metals = CreateDefaults();
        }

        foreach (var reserve in Metals)
        {
            reserve?.Clamp();
        }
    }

    private static MetalReserve[] CreateDefaults()
    {
        var reserves = new MetalReserve[8];
        for (int i = 0; i < 8; i++)
        {
            reserves[i] = new MetalReserve
            {
                Type = (MetalType)i
            };
        }

        return reserves;
    }

    public MetalReserve Get(MetalType type)
    {
        if (Metals == null)
        {
            return null;
        }

        foreach (var reserve in Metals)
        {
            if (reserve != null && reserve.Type == type)
            {
                reserve.Clamp();
                return reserve;
            }
        }

        return null;
    }
}
