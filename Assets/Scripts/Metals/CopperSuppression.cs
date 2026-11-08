using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 以所有啟用中的 Copper 範圍聯集，遮蔽角色的 Bronze 能力訊號。
/// </summary>
[DisallowMultipleComponent]
public sealed class CopperSuppression : MonoBehaviour
{
    [Min(0)]
    public float CopperRadius = 8;
    private static readonly HashSet<CopperSuppression> fields = new HashSet<CopperSuppression>();
    private IMetalAbilityStatus status;
    private IMetalAbilityStatus Status => status ?? (status = GetComponent<IMetalAbilityStatus>());
    public bool IsActive => this && isActiveAndEnabled && Status != null && Status.IsMetalEnabled(MetalType.Copper);

    private void OnEnable()
    {
        fields.Add(this);
    }

    private void OnDisable()
    {
        fields.Remove(this);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetFields()
    {
        fields.Clear();
    }

    public static bool IsSuppressed(IMetalAbilityStatus target)
    {
        if (target == null || !target.IsMetalStatusActive)
        {
            return false;
        }

        foreach (var field in fields)
        {
            if (!field || !field.IsActive)
            {
                continue;
            }

            // Self is protected even with a zero-radius cloud. All clouds form a union.
            if (field.Status.MetalOwner == target.MetalOwner)
            {
                return true;
            }

            float radius = Mathf.Max(0, field.CopperRadius);
            if ((field.Status.MetalSignalPosition - target.MetalSignalPosition).sqrMagnitude <= radius * radius)
            {
                return true;
            }
        }

        return false;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(.75f, .4f, .15f, .5f);
        Gizmos.DrawWireSphere(transform.position, Mathf.Max(0, CopperRadius));
    }
}
