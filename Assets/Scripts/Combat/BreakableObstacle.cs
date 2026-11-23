using UnityEngine;

/// <summary>
/// 處理可破壞障礙物的生命與破壞反應，劍擊破壞須符合 Pewter 條件。
/// </summary>
public class BreakableObstacle : MonoBehaviour
{
    [Min(1)]
    public float MaxHealth = 60;
    [Min(0)]
    public float CurrentHealth = 60;
    public bool IsBroken { get; private set; }
    [Tooltip("打破後是否直接把物件銷毀。關閉時物件保留（碰撞與導航障礙會關掉），給大門等有開啟動畫的物件使用。")]
    public bool DestroyOnBreak = true;
    /// <summary>被打破的瞬間觸發（物件保留時才有意義）。</summary>
    public event System.Action Broken;

    public Vector3 TargetPoint
    {
        get
        {
            var c = GetComponent<Collider>();
            return c ? c.bounds.center : transform.position;
        }
    }

    /// <summary>
    /// 距離 from 最近的表面點。大型障礙（整面門）的碰撞箱中心離玩家很遠，
    /// 用中心點算揮劍距離會永遠打不到，所以近戰判定要用表面最近點。
    /// </summary>
    public Vector3 ClosestPointTo(Vector3 from)
    {
        bool found = false;
        Vector3 best = TargetPoint;
        float bestDistance = float.PositiveInfinity;
        foreach (var c in GetComponentsInChildren<Collider>())
        {
            if (!c.enabled || c.isTrigger)
            {
                continue;
            }

            Vector3 p = c.ClosestPoint(from);
            float d = (p - from).sqrMagnitude;
            if (d < bestDistance)
            {
                bestDistance = d;
                best = p;
                found = true;
            }
        }

        return found ? best : TargetPoint;
    }

    private void Awake()
    {
        Clamp();
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

    public void TakeSwordDamage(float damage, PlayerPewterAbility source)
    {
        if (IsBroken || !source || !source.IsActive || damage <= 0)
        {
            return;
        }

        CurrentHealth = Mathf.Clamp(CurrentHealth - damage, 0, MaxHealth);
        if (CurrentHealth > 0)
        {
            return;
        }

        IsBroken = true;
        foreach (var collider in GetComponentsInChildren<Collider>())
        {
            collider.enabled = false;
        }

        var obstacle = GetComponent<UnityEngine.AI.NavMeshObstacle>();
        if (obstacle)
        {
            obstacle.enabled = false;
        }

        Broken?.Invoke();
        if (DestroyOnBreak)
        {
            Destroy(gameObject);
        }
    }
}
