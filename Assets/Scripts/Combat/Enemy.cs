using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public enum EnemyActionType
{
    Zinc,
    Brass
}

/// <summary>
/// 共用的敵人生命、金屬狀態與反應事件；AI 狀態由個別敵人腳本維護。
/// </summary>
public class Enemy : MonoBehaviour, IMetalAbilityStatus
{
    public static readonly HashSet<Enemy> ActiveEnemies = new HashSet<Enemy>();
    public MetalType UsingMetal = MetalType.None;
    [Tooltip("Multiple enabled metal reserves. An entry overrides the legacy UsingMetal value for that metal.")]
    public MetalReserve[] MetalAbilities = Array.Empty<MetalReserve>();
    [Header("Metal Reactions")]
    [Tooltip("Called when affected by Zinc. Add objects and public methods like a Button On Click event.")]
    public UnityEvent OnZincAffected = new UnityEvent();
    [Tooltip("Called when affected by Brass. Add objects and public methods like a Button On Click event.")]
    public UnityEvent OnBrassAffected = new UnityEvent();
    private readonly List<IEnemyDetectionSource> detectionSources = new List<IEnemyDetectionSource>();
    public Transform MetalOwner => this ? transform : null;
    public Vector3 MetalSignalPosition => this ? TargetPoint : Vector3.zero;
    public bool IsMetalStatusActive => this && isActiveAndEnabled && !IsDead;

    public MetalReserve GetMetal(MetalType metal)
    {
        if (MetalAbilities != null)
        {
            foreach (var reserve in MetalAbilities)
            {
                if (reserve != null && reserve.Type == metal)
                {
                    return reserve;
                }
            }
        }

        return null;
    }

    public bool IsMetalEnabled(MetalType metal)
    {
        if (!IsMetalStatusActive || !MetalAbilitySignals.IsRealMetal(metal))
        {
            return false;
        }

        var reserve = GetMetal(metal);
        return reserve != null ? reserve.IsEnabled && reserve.CurrentAmount > 0 : UsingMetal == metal;
    }

    public bool IsUsingAnyMetal
    {
        get
        {
            for (int i = 0; i < 8; i++)
            {
                if (IsMetalEnabled((MetalType)i))
                {
                    return true;
                }
            }

            return false;
        }
    }

    public void RegisterDetectionSource(IEnemyDetectionSource source)
    {
        if (source != null && !detectionSources.Contains(source))
        {
            detectionSources.Add(source);
        }
    }

    public void UnregisterDetectionSource(IEnemyDetectionSource source)
    {
        detectionSources.Remove(source);
    }

    public bool HasAbilityDetection(Transform target)
    {
        if (!IsMetalStatusActive || !target)
        {
            return false;
        }

        var status = target.GetComponent<IMetalAbilityStatus>();
        if (status == null)
        {
            return false;
        }

        foreach (var source in detectionSources)
        {
            if (source is Behaviour behaviour && (!behaviour || !behaviour.isActiveAndEnabled))
            {
                continue;
            }

            if (source.CanDetect(status))
            {
                return true;
            }
        }

        return false;
    }

    [Min(1)]
    public float MaxHealth = 100;
    [Min(0)]
    public float CurrentHealth = 100;
    [Min(0)]
    public float DestroyDelay = 2;
    public bool IsDead { get; private set; }

    public event Action<Enemy> Died;
    // Legacy void callbacks remain supported. A callback that can reject an action
    // must report its actual outcome (TestEnemy does so below).
    private bool? reportedActionResult;
    public void ReportActionResult(bool succeeded)
    {
        reportedActionResult = (reportedActionResult ?? false) || succeeded;
    }
    private readonly Dictionary<EnemyActionType, Action> actions = new Dictionary<EnemyActionType, Action>();
    public void RegisterAction(EnemyActionType type, Action handler)
    {
        if (handler == null || IsDead)
        {
            return;
        }

        actions.TryGetValue(type, out var current);
        if (current != null && Array.IndexOf(current.GetInvocationList(), handler) >= 0)
        {
            return;
        }

        actions[type] = current + handler;
    }

    public void UnregisterAction(EnemyActionType type, Action handler)
    {
        if (handler == null || !actions.TryGetValue(type, out var current))
        {
            return;
        }

        current -= handler;
        if (current == null)
        {
            actions.Remove(type);
        }
        else
        {
            actions[type] = current;
        }
    }

    public bool TryExecuteAction(EnemyActionType type)
    {
        if (IsDead || !isActiveAndEnabled)
        {
            return false;
        }

        bool executed = false;
        actions.TryGetValue(type, out var handlers);
        // Delegate snapshots permit handlers to safely unregister themselves during dispatch.
        if (handlers != null)
        {
            foreach (Action handler in handlers.GetInvocationList())
            {
                if (IsDead)
                {
                    break;
                }

                if (handler.Target is UnityEngine.Object owner && !owner)
                {
                    UnregisterAction(type, handler);
                    continue;
                }

                if (handler.Target is Behaviour behaviour && !behaviour.isActiveAndEnabled)
                {
                    continue;
                }

                reportedActionResult = null;
                handler();
                executed |= reportedActionResult ?? true;
            }
        }

        var reaction = type == EnemyActionType.Zinc
            ? OnZincAffected
            : type == EnemyActionType.Brass
            ? OnBrassAffected
            : null;
        if (!IsDead && isActiveAndEnabled && HasInspectorReaction(reaction))
        {
            reportedActionResult = null;
            reaction.Invoke();
            executed |= reportedActionResult ?? true;
        }

        return executed;
    }

    private static bool HasInspectorReaction(UnityEvent reaction)
    {
        if (reaction == null)
        {
            return false;
        }

        for (int i = 0; i < reaction.GetPersistentEventCount(); i++)
        {
            var target = reaction.GetPersistentTarget(i);
            if (reaction.GetPersistentListenerState(i) != UnityEventCallState.Off && target
                && (!(target is Behaviour behaviour) || behaviour.isActiveAndEnabled)
                && UnityEventBase.GetValidMethodInfo(target, reaction.GetPersistentMethodName(i), Type.EmptyTypes) != null)
            {
                return true;
            }
        }

        return false;
    }

    private void OnDestroy()
    {
        actions.Clear();
    }

    public Vector3 TargetPoint
    {
        get
        {
            var c = GetComponent<Collider>();
            return c ? c.bounds.center : transform.position + Vector3.up;
        }
    }

    private void Awake()
    {
        Clamp();
        MetalAbilitySignals.EnsureServices(this, false);
        if (CurrentHealth <= 0)
        {
            Die();
        }
    }

    private void OnEnable()
    {
        ActiveEnemies.Add(this);
        MetalAbilitySignals.Register(this);
    }

    private void OnDisable()
    {
        ActiveEnemies.Remove(this);
        MetalAbilitySignals.Unregister(this);
    }

    private void Clamp()
    {
        MaxHealth = Mathf.Max(1, MaxHealth);
        CurrentHealth = Mathf.Clamp(CurrentHealth, 0, MaxHealth);
    }

    private void OnValidate()
    {
        Clamp();
        if (MetalAbilities != null)
        {
            foreach (var reserve in MetalAbilities)
            {
                reserve?.Clamp();
            }
        }
    }

    public void TakeDamage(float damage)
    {
        if (IsDead || damage <= 0)
        {
            return;
        }

        CurrentHealth = Mathf.Clamp(CurrentHealth - damage, 0, MaxHealth);
        if (CurrentHealth <= 0)
        {
            Die();
        }
    }

    public void Heal(float amount)
    {
        if (IsDead || amount <= 0)
        {
            return;
        }

        CurrentHealth = Mathf.Clamp(CurrentHealth + amount, 0, MaxHealth);
    }

    public void Die()
    {
        if (IsDead)
        {
            return;
        }

        IsDead = true;
        CurrentHealth = 0;
        Died?.Invoke(this);
        actions.Clear();
        Destroy(gameObject, DestroyDelay);
    }
}
