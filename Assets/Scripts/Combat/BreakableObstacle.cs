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

    public Vector3 TargetPoint
    {
        get
        {
            var c = GetComponent<Collider>();
            return c ? c.bounds.center : transform.position;
        }
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

        Destroy(gameObject);
    }
}
