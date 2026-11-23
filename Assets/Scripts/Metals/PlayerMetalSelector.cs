using System;
using UnityEngine;

/// <summary>
/// 以碰撞器的局部座標保存接觸面，讓目標移動或旋轉後仍能還原作用點。
/// </summary>
[Serializable]
public struct MetalSurface
{
    public MetalDisplayer Metal;
    public Collider Collider;
    public Vector3 LocalPoint, LocalNormal;
    public bool Valid => Metal && Metal.isActiveAndEnabled && Collider && Collider.enabled
        && Collider.gameObject.activeInHierarchy;
    public Vector3 Point => Collider.transform.TransformPoint(LocalPoint);
    public Vector3 Normal => Collider.transform.worldToLocalMatrix.transpose.MultiplyVector(LocalNormal).normalized;

    public static MetalSurface FromHit(RaycastHit h, MetalDisplayer metal) => new MetalSurface
    {
        Metal = metal,
        Collider = h.collider,
        LocalPoint = h.collider.transform.InverseTransformPoint(h.point),
        LocalNormal = h.collider.transform.localToWorldMatrix.transpose.MultiplyVector(h.normal).normalized
    };
}

/// <summary>
/// 從鏡頭射線取得第一個可見目標，另以玩家位置檢查能力距離。
/// </summary>
public class PlayerMetalSelector : MonoBehaviour
{
    public float SelectionDistance = 30;
    public LayerMask SelectionMask = ~0;
    public MetalSurface Current { get; private set; }
    public Enemy CurrentEnemy { get; private set; }
    public RaycastHit EnemyHit { get; private set; }

    public void ClearSelection()
    {
        Current = default;
        CurrentEnemy = null;
        EnemyHit = default;
    }

    public bool TryGetFocusBounds(out Bounds bounds)
    {
        Transform root;
        if (CurrentEnemy && !CurrentEnemy.IsDead && CurrentEnemy.isActiveAndEnabled && EnemyHit.collider)
        {
            bounds = EnemyHit.collider.bounds;
            root = CurrentEnemy.transform;
        }
        else if (Current.Valid)
        {
            bounds = Current.Collider.bounds;
            root = Current.Metal.transform;
        }
        else
        {
            bounds = default;
            return false;
        }

        foreach (var renderer in root.GetComponentsInChildren<Renderer>())
        {
            if (renderer.enabled)
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return true;
    }

    private Rigidbody body;
    private void Awake()
    {
        body = GetComponent<Rigidbody>();
    }

    public Vector3 RangeOrigin => body ? body.worldCenterOfMass : transform.position;

    public bool IsPointInRange(Vector3 point)
    {
        float range = Mathf.Max(0, SelectionDistance);
        return (point - RangeOrigin).sqrMagnitude <= range * range;
    }

    public void Select(Camera view)
    {
        ClearSelection();
        // Camera distance is not ability range. Cast far enough to reach the whole
        // player-centered sphere, then test the actual surface point against it.
        var ray = view.ViewportPointToRay(new Vector3(.5f, .5f));
        float rayDistance = Mathf.Max(0, SelectionDistance) + Vector3.Distance(ray.origin, RangeOrigin);
        var hits = Physics.RaycastAll(ray, rayDistance, ~0, QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        bool enemyInFront = false;
        foreach (var hit in hits)
        {
            if (hit.collider.transform.IsChildOf(transform))
            {
                continue;
            }

            // 敵人身後牆上的飾刀（WallBlade）可以穿過敵人選取；其他東西仍然會擋住。
            if (enemyInFront)
            {
                var blade = hit.collider.GetComponentInParent<WallBlade>();
                var bladeMetal = blade ? blade.GetComponent<MetalDisplayer>() : null;
                if (bladeMetal && bladeMetal.isActiveAndEnabled && IsPointInRange(hit.point))
                {
                    Current = MetalSurface.FromHit(hit, bladeMetal);
                    break;
                }

                if (blade)
                {
                    break;
                }

                // 同一個敵人的其他碰撞器、或再後面的敵人不算遮擋物。
                if (hit.collider.GetComponentInParent<Enemy>())
                {
                    continue;
                }

                break;
            }

            // The first non-player collider occludes everything behind it, regardless of selection mask.
            if ((SelectionMask.value & (1 << hit.collider.gameObject.layer)) != 0 && IsPointInRange(hit.point))
            {
                var enemy = hit.collider.GetComponentInParent<Enemy>();
                if (enemy)
                {
                    if (!enemy.IsDead && enemy.isActiveAndEnabled)
                    {
                        CurrentEnemy = enemy;
                        EnemyHit = hit;
                        // 繼續往後找：牆上的飾刀可以穿過這個敵人選取。
                        enemyInFront = true;
                        continue;
                    }
                }
                else
                {
                    var m = hit.collider.GetComponentInParent<MetalDisplayer>();
                    if (m && m.isActiveAndEnabled)
                    {
                        Current = MetalSurface.FromHit(hit, m);
                    }
                }
            }

            break;
        }
    }
}
