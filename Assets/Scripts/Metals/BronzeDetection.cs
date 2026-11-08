using System;
using System.Collections.Generic;
using UnityEngine;
using SimpleOutline;

// Shared by Player and Enemy. No raycasts, collision changes, damage or AI state writes.
/// <summary>
/// 查詢附近活躍金屬訊號並維護偵測事件與 X-Ray Outline，不直接改寫 AI 狀態。
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(100)]
public sealed class BronzeDetection : MonoBehaviour, IEnemyDetectionSource
{
    [Min(0)]
    public float DetectionRadius = 60;
    [Header("Player camera outline (independent of metal-object outlines)")]
    public bool ShowOutlines;
    public Camera DetectionCamera;
    public Color OutlineColor = new Color(1, .62f, .15f, 1);
    [Range(1, 6)]
    public float OutlinePixels = 3;
    private IMetalAbilityStatus status;
    private Enemy enemy;
    private readonly HashSet<IMetalAbilityStatus> detected = new HashSet<IMetalAbilityStatus>();
    private readonly List<IMetalAbilityStatus> lost = new List<IMetalAbilityStatus>();
    private readonly Dictionary<IMetalAbilityStatus, BronzeOutlineEntry> outlines = new Dictionary<IMetalAbilityStatus, BronzeOutlineEntry>();
    public event Action<IMetalAbilityStatus> TargetDetected, TargetLost;
    public IMetalAbilityStatus Status => status ?? (status = GetComponent<IMetalAbilityStatus>());
    public bool IsActive => this && isActiveAndEnabled && Status != null && Status.IsMetalEnabled(MetalType.Bronze);
    public IReadOnlyCollection<IMetalAbilityStatus> DetectedActors => detected;

    public Camera ObserverCamera
    {
        get
        {
            if (DetectionCamera)
            {
                return DetectionCamera;
            }

            var controller = GetComponent<PlayerController>();
            return controller && controller.ViewCamera ? controller.ViewCamera : Camera.main;
        }
    }

    private void OnEnable()
    {
        enemy = GetComponent<Enemy>();
        if (enemy)
        {
            enemy.RegisterDetectionSource(this);
        }
    }

    private void OnDisable()
    {
        if (enemy)
        {
            enemy.UnregisterDetectionSource(this);
        }

        ClearDetections();
    }

    public bool CanDetect(IMetalAbilityStatus target)
    {
        if (!IsActive || target == null || !target.IsMetalStatusActive || target.MetalOwner == Status.MetalOwner
            || !target.IsUsingAnyMetal)
        {
            return false;
        }

        float radius = Mathf.Max(0, DetectionRadius);
        return (target.MetalSignalPosition - Status.MetalSignalPosition).sqrMagnitude <= radius * radius
            && !CopperSuppression.IsSuppressed(target);
    }

    public void RefreshDetections()
    {
        if (!IsActive)
        {
            ClearDetections();
            return;
        }

        lost.Clear();
        foreach (var actor in detected)
        {
            if (!CanDetect(actor))
            {
                lost.Add(actor);
            }
        }

        foreach (var actor in lost)
        {
            detected.Remove(actor);
            RemoveOutline(actor);
            TargetLost?.Invoke(actor);
        }

        foreach (var actor in MetalAbilitySignals.Actors)
        {
            if (!CanDetect(actor))
            {
                continue;
            }

            if (detected.Add(actor))
            {
                TargetDetected?.Invoke(actor);
            }

            if (ShowOutlines && !outlines.ContainsKey(actor))
            {
                var entry = new BronzeOutlineEntry(this, actor);
                outlines.Add(actor, entry);
                BronzeOutlineRegistry.Add(entry);
            }
        }

        if (!ShowOutlines)
        {
            ClearOutlines();
        }
    }

    private void RemoveOutline(IMetalAbilityStatus actor)
    {
        if (outlines.TryGetValue(actor, out var entry))
        {
            BronzeOutlineRegistry.Remove(entry);
            outlines.Remove(actor);
        }
    }

    private void ClearOutlines()
    {
        foreach (var entry in outlines.Values)
        {
            BronzeOutlineRegistry.Remove(entry);
        }

        outlines.Clear();
    }

    private void ClearDetections()
    {
        ClearOutlines();
        foreach (var actor in detected)
        {
            TargetLost?.Invoke(actor);
        }

        detected.Clear();
    }

    private void LateUpdate()
    {
        RefreshDetections();
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1, .6f, .1f, .5f);
        Gizmos.DrawWireSphere(transform.position, Mathf.Max(0, DetectionRadius));
    }
}
