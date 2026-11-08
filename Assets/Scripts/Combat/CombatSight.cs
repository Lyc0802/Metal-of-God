using System;
using UnityEngine;

/// <summary>
/// 共用的戰鬥視線檢查，略過攻擊者及目標自身的碰撞器。
/// </summary>
public static class CombatSight
{
    public static bool Clear(Vector3 origin, Vector3 point, Transform source, Transform target)
    {
        var delta = point - origin;
        if (delta.sqrMagnitude < .0001f)
        {
            return true;
        }

        var hits = Physics.RaycastAll(origin, delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            if (hit.collider.transform.IsChildOf(source))
            {
                continue;
            }

            return hit.collider.transform.IsChildOf(target);
        }

        return true;
    }
}
