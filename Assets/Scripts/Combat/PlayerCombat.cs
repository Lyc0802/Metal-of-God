using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 管理揮劍時序、命中窗口與每次攻擊的去重；命中仍須通過視線檢查。
/// </summary>
public class PlayerCombat : MonoBehaviour
{
    public Transform SwordPivot;
    [Tooltip("Disable when a skinned character animation drives the hand-held sword.")]
    public bool AnimateSwordPivot = true;
    public float AttackDamage = 25, AttackRange = 2.5f;
    [Range(1, 180)]
    public float AttackAngle = 65;
    public float AttackCooldown = .4f, SwingDuration = .64f;
    [Range(20, 160)]
    public float LeftSwingAngle = 105;
    [Range(.3f, .85f)]
    public float StrikeEnd = .60f;
    [Range(0, 1)]
    public float HitStart = .34f, HitEnd = .58f;
    [Min(0)]
    public float KnockbackSpeed = 8, KnockbackDuration = .3f;
    public LayerMask EnemyMask = ~0;
    public bool IsAttacking { get; private set; }
    public float SwingProgress { get; private set; }
    public bool AlternateAttacks;
    [Min(0)] public float ComboResetDelay = .65f;
    [Range(0,1)] public float ReturnHitStart = .5f, ReturnHitEnd = .83f;
    public int AttackIndex { get; private set; }
    public int SwingSequence { get; private set; }
    private bool queuedAttack;
    private int nextAttackIndex;
    private float lastAttackEnd = float.NegativeInfinity;
    private float ActiveHitStart => AttackIndex == 1 ? ReturnHitStart : HitStart;
    private float ActiveHitEnd => AttackIndex == 1 ? ReturnHitEnd : HitEnd;
    public bool IsBladeSwinging => IsAttacking && SwingProgress >= ActiveHitStart
        && SwingProgress < Mathf.Clamp(
        StrikeEnd,
        .3f,
        .85f);

    public event System.Action SwingEffectTriggered;
    public event System.Action SwingCancelled;
    private PlayerHealth health;
    private PlayerMetalManipulator metal;
    private PlayerController controller;
    private PlayerPewterAbility pewter;
    private readonly HashSet<BreakableObstacle> hitObstacles = new HashSet<BreakableObstacle>();
    private Quaternion rest;
    private float nextAttack;
    private readonly HashSet<Enemy> hitEnemies = new HashSet<Enemy>();
    private void Awake()
    {
        pewter = GetComponent<PlayerPewterAbility>();
        health = GetComponent<PlayerHealth>();
        metal = GetComponent<PlayerMetalManipulator>();
        controller = GetComponent<PlayerController>();
        if (SwordPivot)
        {
            rest = SwordPivot.localRotation;
        }
    }

    public bool TryAttack()
    {
        if (!CanAttack()) return false;
        if (IsAttacking)
        {
            if (!AlternateAttacks || queuedAttack) return false;
            queuedAttack = true;
            return true;
        }
        if (Time.time < nextAttack) return false;
        if (Time.time-lastAttackEnd > ComboResetDelay) nextAttackIndex=0;
        StartCoroutine(Swing());
        return true;
    }

    private bool CanAttack()
    {
        if (!SwordPivot || (health && health.IsDead)
            || (metal
            && metal.IsLocked)
            || (controller
            && controller.IsSelectingMetal))
        {
            return false;
        }

        return true;
    }

    private IEnumerator Swing()
    {
        do
        {
        queuedAttack = false;
        AttackIndex = AlternateAttacks ? nextAttackIndex : 0;
        nextAttackIndex = 1-AttackIndex;
        SwingSequence++;
        nextAttack = Time.time + Mathf.Max(AttackCooldown, SwingDuration);
        IsAttacking = true;
        SwingProgress = 0;
        hitEnemies.Clear();
        hitObstacles.Clear();
        bool effectPlayed = false;
        float duration = Mathf.Max(.1f, SwingDuration);
        float previous = 0;
        for (float elapsed = 0; elapsed < duration; elapsed += Time.deltaTime)
        {
            float t = elapsed / duration;
            SwingProgress = t;
            // One right-to-left cut, then a non-attacking return to the resting pose.
            float cutEnd = Mathf.Clamp(StrikeEnd, .3f, .85f), left = -Mathf.Abs(LeftSwingAngle);
            float angle = t < cutEnd
                ? Mathf.Lerp(0, left, Mathf.SmoothStep(0, 1, t / cutEnd))
                : Mathf.Lerp(
                left,
                0,
                Mathf.SmoothStep(0, 1, (t - cutEnd) / (1 - cutEnd)));
            if (AnimateSwordPivot)
            {
                SwordPivot.localRotation = rest * Quaternion.Euler(0, angle, 0);
            }

            if (!effectPlayed && t >= ActiveHitStart)
            {
                effectPlayed = true;
                SwingEffectTriggered?.Invoke();
            }

            // Check the swept time interval so a slow frame cannot skip a short hit window.
            if (t >= ActiveHitStart && previous <= ActiveHitEnd)
            {
                ApplyHit();
            }

            previous = t;
            yield return null;
        }

        if (previous <= ActiveHitEnd)
        {
            if (!effectPlayed)
            {
                SwingEffectTriggered?.Invoke();
            }

            ApplyHit();
        }

        if (AnimateSwordPivot && SwordPivot)
        {
            SwordPivot.localRotation = rest;
        }

        SwingProgress = 1;
        lastAttackEnd = Time.time;
        // Do not hold IsAttacking after an unqueued strike: movement must unlock
        // immediately. Late presses still use nextAttackIndex and ComboResetDelay.
        // At most one follow-up is buffered; holding the button does not auto-combo.
        while (queuedAttack && Time.time < nextAttack && CanAttack()) yield return null;
        } while (queuedAttack && CanAttack());
        queuedAttack = false;
        IsAttacking = false;
    }

    private void ApplyHit()
    {
        var own = GetComponent<Collider>();
        var origin = own ? own.bounds.center : transform.position;
        foreach (var collider in Physics.OverlapSphere(origin, AttackRange, EnemyMask, QueryTriggerInteraction.Ignore))
        {
            var obstacle = collider.GetComponentInParent<BreakableObstacle>();
            if (obstacle)
            {
                if (pewter && pewter.IsActive && !obstacle.IsBroken && !hitObstacles.Contains(obstacle))
                {
                    var point = obstacle.TargetPoint;
                    var offset = point - origin;
                    if (offset.magnitude <= AttackRange && Vector3.Angle(transform.forward, offset) <= AttackAngle
                        && CombatSight.Clear(
                        origin,
                        point,
                        transform,
                        obstacle.transform))
                    {
                        hitObstacles.Add(obstacle);
                        obstacle.TakeSwordDamage(AttackDamage * pewter.CurrentStrengthMultiplier, pewter);
                    }
                }

                continue;
            }

            var enemy = collider.GetComponentInParent<Enemy>();
            if (!enemy || enemy.IsDead || hitEnemies.Contains(enemy))
            {
                continue;
            }

            var delta = enemy.TargetPoint - origin;
            if (delta.magnitude > AttackRange || Vector3.Angle(transform.forward, delta) > AttackAngle
                || !CombatSight.Clear(
                origin,
                enemy.TargetPoint,
                transform,
                enemy.transform))
            {
                continue;
            }

            hitEnemies.Add(enemy);
            float previousHealth = enemy.CurrentHealth;
            var hitPosition = collider.ClosestPoint(SwordPivot ? SwordPivot.position : origin);
            enemy.TakeDamage(AttackDamage);
            if (enemy.CurrentHealth < previousHealth)
            {
                CombatFeedbackEvents.PublishSwordHit(hitPosition, -delta.normalized);
            }
            if (!enemy.IsDead)
            {
                var direction = Vector3.ProjectOnPlane(delta, Vector3.up).normalized;
                if (direction.sqrMagnitude < .001f)
                {
                    direction = transform.forward;
                }

                var ai = enemy.GetComponent<TestEnemy>();
                if (ai)
                {
                    ai.ApplyKnockback(direction, KnockbackSpeed, KnockbackDuration);
                }
                else
                {
                    var rb = enemy.GetComponent<Rigidbody>();
                    if (rb && !rb.isKinematic)
                    {
                        rb.AddForce(direction * KnockbackSpeed, ForceMode.VelocityChange);
                    }
                }
            }
        }
    }

    public void CancelAttack()
    {
        if (IsAttacking)
        {
            SwingCancelled?.Invoke();
        }

        StopAllCoroutines();
        queuedAttack=false;nextAttackIndex=0;lastAttackEnd=float.NegativeInfinity;
        SwingProgress = 0;
        IsAttacking = false;
        if (AnimateSwordPivot && SwordPivot)
        {
            SwordPivot.localRotation = rest;
        }
    }

    private void OnDisable()
    {
        CancelAttack();
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, AttackRange);
    }
}
