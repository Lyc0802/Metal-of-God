using System.Collections;
using UnityEngine;
using UnityEngine.AI;

public enum EnemyState
{
    Wander,
    Chase,
    Attack
}

/// <summary>
/// 以 NavMesh 執行巡邏、追擊與攻擊，並整合視線、Bronze 與 Zinc/Brass 反應。
/// </summary>
[RequireComponent(typeof(Enemy), typeof(NavMeshAgent))]
[RequireComponent(typeof(SoundEmitter))]
public class TestEnemy : MonoBehaviour
{
    public PlayerHealth Player;
    [Header("AudioSource Sounds")]
    public AudioClip FootstepClip, AttackClip;
    [Range(0, 1)]
    public float FootstepVolume = .7f, AttackVolume = 1;
    [Min(.1f)]
    public float FootstepDistance = 1.6f;
    private SoundEmitter soundEmitter;
    private Vector3 lastFootPosition;
    private float footTravel;
    public float WanderRadius = 5, WanderSpeed = 1.5f, WanderWait = 2;
    public float DetectionRange = 12, LoseDistance = 4, ChaseSpeed = 3.5f;
    public float AttackRange = 1.8f, AttackDamage = 8, AttackCooldown = 1.3f, TurnSpeed = 360;
    [Header("Attack Visual")]
    public Transform AttackPivot;
    [Min(.1f)]
    public float AttackDuration = .6f;
    [Range(0, 1)]
    public float AttackHitTime = .5f;
    public bool IsAttacking { get; private set; }

    private Quaternion attackRest = Quaternion.identity;
    [Min(0)]
    public float ZincDuration = 8, BrassDuration = 6;
    private EnemyActionType? activeEffect;
    private float effectEndsAt;
    public bool IsZincActive => activeEffect == EnemyActionType.Zinc && Time.time < effectEndsAt;
    public bool IsBrassActive => activeEffect == EnemyActionType.Brass && Time.time < effectEndsAt;
    public float EffectTimeRemaining => Mathf.Max(0, effectEndsAt - Time.time);
    public EnemyState State { get; private set; }

    private bool trackingByAbility;
    private Enemy enemy;
    private NavMeshAgent agent;
    private Vector3 spawn;
    private float nextWander, nextAttack, nextPath;
    private Vector3 knockbackVelocity;
    private float knockbackRemaining, knockbackDuration;
    public bool IsKnockedBack => knockbackRemaining > 0;

    private void Awake()
    {
        soundEmitter = GetComponent<SoundEmitter>();
        lastFootPosition = transform.position;
        enemy = GetComponent<Enemy>();
        agent = GetComponent<NavMeshAgent>();
        spawn = transform.position;
        agent.updateRotation = false;
        if (AttackPivot)
        {
            attackRest = AttackPivot.localRotation;
        }
    }

    private void OnEnable()
    {
        lastFootPosition = transform.position;
        footTravel = 0;
        if (!enemy)
        {
            return;
        }

        enemy.Died += OnDeath;
    }

    private void OnDisable()
    {
        CancelAttack();
        knockbackRemaining = 0;
        ClearEffect();
        trackingByAbility = false;
        if (enemy)
        {
            enemy.Died -= OnDeath;
        }

        Stop();
    }

    private void FindPlayer()
    {
        if (!Player)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p)
            {
                Player = p.GetComponent<PlayerHealth>();
            }
        }
    }

    private void ClearEffect()
    {
        activeEffect = null;
        effectEndsAt = 0;
    }

    /// <summary>Inspector 的 On Zinc Affected 預設反應：在持續時間內追擊玩家。</summary>
    public void OnZinc()
    {
        if (enemy) enemy.ReportActionResult(false);
        if (!isActiveAndEnabled || !enemy || enemy.IsDead)
        {
            return;
        }

        FindPlayer();
        if (!Player || Player.IsDead || ZincDuration <= 0)
        {
            return;
        }

        activeEffect = EnemyActionType.Zinc;
        effectEndsAt = Time.time + Mathf.Max(0, ZincDuration);
        State = EnemyState.Chase;
        Stop();
        nextPath = 0;
        enemy.ReportActionResult(true);
    }

    /// <summary>Inspector 的 On Brass Affected 預設反應：取消攻擊並暫停追擊。</summary>
    public void OnBrass()
    {
        if (enemy) enemy.ReportActionResult(false);
        if (!isActiveAndEnabled || !enemy || enemy.IsDead)
        {
            return;
        }

        if (BrassDuration <= 0) return;
        CancelAttack();
        activeEffect = EnemyActionType.Brass;
        effectEndsAt = Time.time + Mathf.Max(0, BrassDuration);
        State = EnemyState.Wander;
        Stop();
        nextWander = Time.time;
        enemy.ReportActionResult(true);
    }

    private void Start()
    {
        FindPlayer();
        if (!agent.isOnNavMesh)
        {
            Debug.LogWarning("TestEnemy needs a baked NavMesh beneath its spawn.", this);
        }
    }

    private void Stop()
    {
        if (agent && agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.ResetPath();
        }
    }

    private void OnDeath(Enemy dead)
    {
        ClearEffect();
        Stop();
        agent.enabled = false;
        enabled = false;
    }

    public void ApplyKnockback(Vector3 direction, float speed, float duration)
    {
        if (enemy.IsDead || !agent.enabled || !agent.isOnNavMesh || speed <= 0 || duration <= 0)
        {
            return;
        }

        direction.y = 0;
        if (direction.sqrMagnitude < .001f)
        {
            return;
        }

        CancelAttack();
        Stop();
        knockbackVelocity = direction.normalized * speed;
        knockbackDuration = knockbackRemaining = duration;
        nextAttack = Mathf.Max(nextAttack, Time.time + duration);
    }

    public void TickAI()
    {
        if (enemy.IsDead)
        {
            OnDeath(enemy);
            return;
        }

        if (activeEffect.HasValue && Time.time >= effectEndsAt)
        {
            ClearEffect();
            State = EnemyState.Wander;
            Stop();
            nextWander = Time.time;
            nextPath = 0;
        }

        if (!agent.enabled || !agent.isOnNavMesh)
        {
            return;
        }

        if (knockbackRemaining > 0)
        {
            // Keep the agent on its NavMesh and pause chasing/attacking until recovery.
            Stop();
            agent.Move(knockbackVelocity * (knockbackRemaining / knockbackDuration) * Time.deltaTime);
            knockbackRemaining = Mathf.Max(0, knockbackRemaining - Time.deltaTime);
            return;
        }

        var playerCollider = Player ? Player.GetComponent<Collider>() : null;
        var targetPoint = playerCollider
            ? playerCollider.bounds.center
            : Player
            ? Player.transform.position
            : transform.position;
        float distance = Player && !Player.IsDead
            ? Vector3.Distance(enemy.TargetPoint, targetPoint)
            : float.PositiveInfinity;
        bool hasSight = Player && CombatSight.Clear(enemy.TargetPoint, targetPoint, transform, Player.transform);
        bool detectedByAbility = Player && !Player.IsDead && enemy.HasAbilityDetection(Player.transform);
        bool detectedByVision = Player && !Player.IsDead && distance <= DetectionRange && hasSight;
        if (IsAttacking)
        {
            Stop();
            Face(targetPoint - transform.position);
            return;
        }

        UpdatePerceptionState(distance, hasSight, detectedByAbility, detectedByVision);
        if (State == EnemyState.Attack)
        {
            TickAttack(targetPoint, distance, hasSight);
        }
        else if (State == EnemyState.Chase)
        {
            TickChase();
        }
        else
        {
            TickWander();
        }
    }

    private IEnumerator AttackRoutine()
    {
        IsAttacking = true;
        bool hit = false;
        float duration = Mathf.Max(.1f, AttackDuration);
        for (float elapsed = 0; elapsed < duration; elapsed += Time.deltaTime)
        {
            if (enemy.IsDead || IsBrassActive || IsKnockedBack)
            {
                break;
            }

            float t = elapsed / duration;
            float angle = t < .3f
                ? Mathf.Lerp(0, 35, t / .3f)
                : t < .55f
                ? Mathf.Lerp(
                35,
                -85,
                Mathf.SmoothStep(0, 1, (t - .3f) / .25f))
                : Mathf.Lerp(
                -85,
                0,
                Mathf.SmoothStep(0, 1, (t - .55f) / .45f));
            if (AttackPivot)
            {
                AttackPivot.localRotation = attackRest * Quaternion.Euler(angle, 0, 0);
            }

            if (!hit && t >= AttackHitTime)
            {
                hit = true;
                if (soundEmitter)
                {
                    soundEmitter.Play(AttackClip, AttackVolume);
                }

                DealAttackDamage();
            }

            yield return null;
        }

        if (AttackPivot)
        {
            AttackPivot.localRotation = attackRest;
        }

        IsAttacking = false;
    }

    private void DealAttackDamage()
    {
        if (!isActiveAndEnabled || enemy.IsDead || IsBrassActive || IsKnockedBack || !Player || Player.IsDead)
        {
            return;
        }

        var collider = Player.GetComponent<Collider>();
        var point = collider ? collider.bounds.center : Player.transform.position;
        if (Vector3.Distance(enemy.TargetPoint, point) <= AttackRange
            && CombatSight.Clear(
            enemy.TargetPoint,
            point,
            transform,
            Player.transform))
        {
            Player.TakeDamage(AttackDamage);
        }
    }

    private void CancelAttack()
    {
        StopAllCoroutines();
        IsAttacking = false;
        if (AttackPivot)
        {
            AttackPivot.localRotation = attackRest;
        }
    }

    private void Update()
    {
        TickAI();
        UpdateFootsteps();
    }

    private void UpdateFootsteps()
    {
        var movement = Vector3.ProjectOnPlane(transform.position - lastFootPosition, Vector3.up);
        lastFootPosition = transform.position;
        if (enemy.IsDead || !agent.enabled || !agent.isOnNavMesh || IsAttacking || IsKnockedBack)
        {
            return;
        }

        float step = Mathf.Max(.1f, FootstepDistance);
        if (movement.magnitude > step * 3)
        {
            footTravel = 0;
            return;
        }

        footTravel += movement.magnitude;
        if (footTravel >= step)
        {
            footTravel %= step;
            if (soundEmitter)
            {
                soundEmitter.Play(FootstepClip, FootstepVolume);
            }
        }
    }

    private void Face(Vector3 direction)
    {
        direction.y = 0;
        if (direction.sqrMagnitude > .001f)
        {
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                Quaternion.LookRotation(direction),
                TurnSpeed * Time.deltaTime);
        }
    }

    private void UpdatePerceptionState(float distance, bool hasSight, bool detectedByAbility, bool detectedByVision)
    {
        if (IsBrassActive)
        {
            State = EnemyState.Wander;
        }
        else if (IsZincActive && Player && !Player.IsDead)
        {
            State = distance <= AttackRange && hasSight ? EnemyState.Attack : EnemyState.Chase;
        }
        else
        {
            // Bronze reports through Enemy's detection API. Only this AI chooses a state.
            // Losing an ability-only track behind a wall does not become visual memory.
            if (trackingByAbility && !detectedByAbility && !detectedByVision)
            {
                State = EnemyState.Wander;
                Stop();
                nextWander = Time.time;
                nextPath = 0;
            }

            trackingByAbility = detectedByAbility && !detectedByVision;
            if (State == EnemyState.Wander && (detectedByVision || detectedByAbility))
            {
                State = EnemyState.Chase;
            }
            else if (State != EnemyState.Wander && !detectedByAbility && distance > DetectionRange + LoseDistance)
            {
                State = EnemyState.Wander;
                Stop();
                nextWander = Time.time;
            }

            if (State != EnemyState.Wander)
            {
                State = distance <= AttackRange && hasSight ? EnemyState.Attack : EnemyState.Chase;
            }
        }
    }

    private void TickAttack(Vector3 targetPoint, float distance, bool hasSight)
    {
        Stop();
        Face(targetPoint - transform.position);
        if (Time.time >= nextAttack && Player && !Player.IsDead && distance <= AttackRange && hasSight)
        {
            nextAttack = Time.time + Mathf.Max(AttackDuration, AttackCooldown);
            StartCoroutine(AttackRoutine());
        }
    }

    private void TickChase()
    {
        agent.isStopped = false;
        agent.speed = ChaseSpeed;
        agent.stoppingDistance = AttackRange * .7f;
        if (Time.time >= nextPath)
        {
            agent.SetDestination(Player.transform.position);
            nextPath = Time.time + .2f;
        }

        Face(agent.desiredVelocity);
    }

    private void TickWander()
    {
        agent.speed = WanderSpeed;
        agent.stoppingDistance = .15f;
        if (!agent.pathPending && (!agent.hasPath || agent.remainingDistance <= .3f))
        {
            if (agent.hasPath)
            {
                Stop();
                nextWander = Time.time + WanderWait;
            }

            if (Time.time >= nextWander)
            {
                for (int i = 0; i < 6; i++)
                {
                    var random = Random.insideUnitCircle * WanderRadius;
                    if (!NavMesh.SamplePosition(
                        spawn + new Vector3(random.x, 0, random.y),
                        out var hit,
                        2,
                        agent.areaMask))
                    {
                        continue;
                    }

                    var path = new NavMeshPath();
                    if (agent.CalculatePath(hit.position, path) && path.status == NavMeshPathStatus.PathComplete)
                    {
                        agent.isStopped = false;
                        agent.SetPath(path);
                        break;
                    }
                }

                nextWander = Time.time + WanderWait;
            }
        }

        Face(agent.desiredVelocity);
    }
}
