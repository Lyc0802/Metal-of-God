using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 投擲型敵人（Steel 使用者）：與玩家保持距離，蓄力後把金屬碎片射向玩家。
/// 碎片是可被玩家推 / 拉的金屬：玩家把它推回去就能傷害敵人。
/// Zinc 讓它連續快速射擊，Brass 讓它安靜下來不再攻擊。
/// </summary>
[RequireComponent(typeof(Enemy))]
public class EnemyShooter : MonoBehaviour
{
    public PlayerHealth Player;
    public BossProjectile Projectile;
    [Tooltip("投擲時舉起 / 揮出的手臂樞紐（視覺用）。")]
    public Transform ThrowPivot;
    [Min(1)] public float DetectionRange = 26;
    [Tooltip("玩家比這個距離近時，會往後拉開距離。")]
    public float MinDistance = 9;
    [Tooltip("玩家比這個距離遠時，會往前靠近。")]
    public float MaxDistance = 17;
    public float MoveSpeed = 3.2f, TurnSpeed = 300;
    public float ThrowSpeed = 24, ThrowDamage = 10, ThrowKnockback = 9;
    [Min(.1f)] public float Windup = .8f;
    public float Cooldown = 2.8f;
    [Min(0)] public float ZincDuration = 8, BrassDuration = 6;

    [Header("Steel push (enemy metal ability)")]
    [Tooltip("投擲兵是 Steel 使用者：除了射碎片，也會把場景上的金屬道具（鐵桶、火盆、武器…）推向玩家。")]
    public bool CanPushMetal = true;
    [Tooltip("能推動的道具離自己多遠。")]
    [Min(1)] public float PushRange = 15;
    [Min(1)] public float PushSpeed = 22;
    [Tooltip("推動 / 投擲前，道具先浮到自己腳底上方這個高度（蓄力期間）。")]
    [Min(.5f)] public float LevitateHeight = 2.6f;
    [Tooltip("兩次推道具之間的冷卻（秒）；兩次之間照常射碎片。")]
    [Min(0)] public float PushCooldown = 5.5f;
    [Tooltip("能推動的最大重量（比這重的推不動）。")]
    [Min(1)] public float PushMaxWeight = 75;

    private LooseMetalProp pushTarget;
    private float nextPush;
    private LineRenderer pushLine;
    private Material pushLineMaterial;

    private Enemy enemy;
    private NavMeshAgent agent;
    private Quaternion pivotRest = Quaternion.identity;
    private bool throwing;
    private float nextThrow, nextPath, effectEndsAt;
    private EnemyActionType? effect;

    private const float FollowThrough = .25f;

    /// <summary>親眼看到玩家（有視線、在偵測範圍內）。</summary>
    public bool SeesPlayer { get; private set; }
    /// <summary>被 Brass 安撫中。</summary>
    public bool IsCalmed => IsBrass;
    /// <summary>正在走向聲響來源（由 EnemySenses 設定）；此時不會把路徑清掉。</summary>
    public bool Investigating { get; set; }
    private float alertedUntil;

    /// <summary>同伴發出警報：這段時間內視為發現玩家。</summary>
    public void Alert(float seconds)
    {
        alertedUntil = Mathf.Max(alertedUntil, Time.time + seconds);
    }

    /// <summary>正在蓄力 / 投擲中（給動畫用）。</summary>
    public bool IsThrowing => throwing;
    /// <summary>整段投擲動作的時間（蓄力 + 收招）。</summary>
    public float AttackAnimationTime => Windup + FollowThrough;
    /// <summary>碎片射出的時間點佔整段動作的比例。</summary>
    public float FireFraction => Windup / (Windup + FollowThrough);

    private bool IsZinc => effect == EnemyActionType.Zinc && Time.time < effectEndsAt;
    private bool IsBrass => effect == EnemyActionType.Brass && Time.time < effectEndsAt;

    private void Awake()
    {
        enemy = GetComponent<Enemy>();
        agent = GetComponent<NavMeshAgent>();
        if (agent)
        {
            agent.updateRotation = false;
            agent.speed = MoveSpeed;
            agent.stoppingDistance = 0;
        }

        if (ThrowPivot)
        {
            pivotRest = ThrowPivot.localRotation;
        }
    }

    private void OnEnable()
    {
        enemy = GetComponent<Enemy>();
        enemy.RegisterAction(EnemyActionType.Zinc, OnZinc);
        enemy.RegisterAction(EnemyActionType.Brass, OnBrass);
    }

    private void OnDisable()
    {
        if (enemy)
        {
            enemy.UnregisterAction(EnemyActionType.Zinc, OnZinc);
            enemy.UnregisterAction(EnemyActionType.Brass, OnBrass);
        }
    }

    private void OnZinc()
    {
        if (ZincDuration <= 0)
        {
            enemy.ReportActionResult(false);
            return;
        }

        effect = EnemyActionType.Zinc;
        effectEndsAt = Time.time + ZincDuration;
        enemy.ReportActionResult(true);
    }

    private void OnBrass()
    {
        if (BrassDuration <= 0)
        {
            enemy.ReportActionResult(false);
            return;
        }

        effect = EnemyActionType.Brass;
        effectEndsAt = Time.time + BrassDuration;
        enemy.ReportActionResult(true);
    }

    private bool FindPlayer()
    {
        if (!Player)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p)
            {
                Player = p.GetComponent<PlayerHealth>();
            }
        }

        return Player && !Player.IsDead;
    }

    private bool OnMesh => agent && agent.enabled && agent.isOnNavMesh;

    private void Stop()
    {
        if (OnMesh)
        {
            agent.isStopped = true;
            agent.ResetPath();
        }
    }

    private void Face(Vector3 direction)
    {
        direction.y = 0;
        if (direction.sqrMagnitude > .001f)
        {
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), TurnSpeed * Time.deltaTime);
        }
    }

    private void Update()
    {
        if (enemy.IsDead)
        {
            StopAllCoroutines();
            HidePushLine();
            if (pushTarget)
            {
                pushTarget.EndLevitate();
                pushTarget = null;
            }

            DropHeldShard();
            Stop();
            if (agent)
            {
                agent.enabled = false;
            }

            enabled = false;
            return;
        }

        if (!FindPlayer())
        {
            Stop();
            return;
        }

        var collider = Player.GetComponent<Collider>();
        Vector3 playerPoint = collider ? collider.bounds.center : Player.transform.position;
        Vector3 toPlayer = Player.transform.position - transform.position;
        toPlayer.y = 0;
        float distance = toPlayer.magnitude;
        bool sight = CombatSight.Clear(enemy.TargetPoint, playerPoint, transform, Player.transform);
        SeesPlayer = !IsBrass && distance <= DetectionRange && sight;
        // 同伴發出警報時（Alert），就算沒親眼看到玩家也會進入戰鬥並靠近。
        bool aware = !IsBrass && (IsZinc || Time.time < alertedUntil || SeesPlayer);
        if (!aware)
        {
            if (!Investigating)
            {
                Stop();
            }

            return;
        }

        Investigating = false;

        Face(toPlayer);
        if (throwing)
        {
            Stop();
            return;
        }

        Move(toPlayer, distance, sight);
        if (sight && distance <= MaxDistance + 4 && Time.time >= nextThrow)
        {
            // 優先用 Steel 推場景上的金屬道具砸向玩家（有冷卻，兩次之間照常射碎片）。
            pushTarget = null;
            if (CanPushMetal && Time.time >= nextPush)
            {
                pushTarget = FindPushTarget(playerPoint);
                if (pushTarget)
                {
                    nextPush = Time.time + PushCooldown * (IsZinc ? .5f : 1f);
                }
            }

            StartCoroutine(Throw());
        }
    }

    /// <summary>找一個可以推向玩家的金屬道具：夠輕、離自己夠近、能看到、道具到玩家之間沒有東西擋住、撞到會傷人。</summary>
    private LooseMetalProp FindPushTarget(Vector3 playerPoint)
    {
        LooseMetalProp best = null;
        float bestDistance = float.PositiveInfinity;
        foreach (var prop in LooseMetalProp.All)
        {
            if (!prop || !prop.isActiveAndEnabled || prop.PlayerDamage <= 0)
            {
                continue;
            }

            var body = prop.GetComponent<Rigidbody>();
            var metal = prop.GetComponent<MetalDisplayer>();
            if (!body || body.isKinematic || body.linearVelocity.sqrMagnitude > 4f || (metal && metal.weight > PushMaxWeight))
            {
                continue;
            }

            Vector3 center = body.worldCenterOfMass;
            if (Vector3.Distance(transform.position, center) > PushRange)
            {
                continue;
            }

            float toPlayer = Vector3.Distance(center, playerPoint);
            if (toPlayer < 4f || toPlayer > 24f || toPlayer >= bestDistance)
            {
                continue;
            }

            if (!CombatSight.Clear(enemy.TargetPoint, center, transform, prop.transform)
                || !CombatSight.Clear(center, playerPoint, prop.transform, Player.transform))
            {
                continue;
            }

            best = prop;
            bestDistance = toPlayer;
        }

        return best;
    }

    /// <summary>道具已經浮在空中：直接瞄準玩家砸過去（直線飛行，撞到東西才下墜），標記成敵人推的：只傷玩家，不傷敵人。</summary>
    private void PushProp(LooseMetalProp prop)
    {
        if (!prop || !FindPlayer())
        {
            return;
        }

        var body = prop.GetComponent<Rigidbody>();
        if (!body || body.isKinematic)
        {
            return;
        }

        var collider = Player.GetComponent<Collider>();
        Vector3 target = collider ? collider.bounds.center : Player.transform.position;
        Vector3 delta = target - body.worldCenterOfMass;
        float time = Mathf.Max(.2f, delta.magnitude / PushSpeed);
        prop.Hurl(delta.normalized * PushSpeed, time + 1.5f);
        prop.Arm(time + 2.5f, true);
    }

    /// <summary>浮在頭頂的碎片：蓄力時從手升到頭上，射出時直接瞄準玩家。</summary>
    private BossProjectile heldShard;

    private void DropHeldShard()
    {
        if (heldShard)
        {
            Destroy(heldShard.gameObject);
            heldShard = null;
        }
    }

    /// <summary>蓄力時從手到道具的藍色細線（Steel 的視覺提示）。</summary>
    private void ShowPushLine(Vector3 hand, Vector3 propPosition)
    {
        if (!pushLine)
        {
            var go = new GameObject("Enemy Steel Line");
            go.transform.SetParent(transform, false);
            pushLine = go.AddComponent<LineRenderer>();
            pushLine.positionCount = 2;
            pushLine.startWidth = .05f;
            pushLine.endWidth = .02f;
            pushLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            pushLine.receiveShadows = false;
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader)
            {
                pushLineMaterial = new Material(shader);
                pushLineMaterial.SetColor("_BaseColor", new Color(.35f, .6f, 1f));
                pushLine.sharedMaterial = pushLineMaterial;
            }
        }

        pushLine.enabled = true;
        pushLine.SetPosition(0, hand);
        pushLine.SetPosition(1, propPosition);
    }

    private void HidePushLine()
    {
        if (pushLine)
        {
            pushLine.enabled = false;
        }
    }

    private void OnDestroy()
    {
        if (pushLineMaterial)
        {
            Destroy(pushLineMaterial);
        }
    }

    private void Move(Vector3 toPlayer, float distance, bool sight)
    {
        if (!OnMesh)
        {
            return;
        }

        if (Time.time < nextPath)
        {
            return;
        }

        nextPath = Time.time + .3f;
        agent.speed = MoveSpeed;
        if (distance < MinDistance)
        {
            // 往遠離玩家的方向退，找不到可走的點就原地站著射。
            Vector3 away = transform.position - toPlayer.normalized * 6f;
            if (NavMesh.SamplePosition(away, out var hit, 3f, agent.areaMask))
            {
                agent.isStopped = false;
                agent.SetDestination(hit.position);
                return;
            }

            Stop();
        }
        else if (distance > MaxDistance || !sight)
        {
            agent.isStopped = false;
            agent.SetDestination(Player.transform.position);
        }
        else
        {
            Stop();
        }
    }

    private IEnumerator Throw()
    {
        throwing = true;
        nextThrow = Time.time + Windup + Cooldown * (IsZinc ? .5f : 1f);
        // 沒有要推的道具時，先在手邊生出一片金屬碎片，蓄力期間讓它升到頭頂懸停。
        Vector3 shardStart = Vector3.zero;
        if (!pushTarget && Projectile && FindPlayer())
        {
            shardStart = (ThrowPivot ? ThrowPivot.position : enemy.TargetPoint) + transform.forward * .4f;
            heldShard = Instantiate(Projectile, shardStart, Quaternion.LookRotation(transform.forward));
            heldShard.Damage = ThrowDamage;
            heldShard.Knockback = ThrowKnockback;
            heldShard.Lifetime = 8;
            var shardBody = heldShard.GetComponent<Rigidbody>();
            shardBody.useGravity = false;
            shardBody.isKinematic = true;
            foreach (var a in heldShard.GetComponentsInChildren<Collider>())
            {
                foreach (var b in GetComponentsInChildren<Collider>())
                {
                    Physics.IgnoreCollision(a, b, true);
                }
            }
        }

        for (float t = 0; t < Windup; t += Time.deltaTime)
        {
            if (enemy.IsDead || IsBrass)
            {
                ResetPivot();
                HidePushLine();
                if (pushTarget)
                {
                    pushTarget.EndLevitate();
                }

                DropHeldShard();
                pushTarget = null;
                throwing = false;
                yield break;
            }

            if (heldShard)
            {
                Vector3 hover = transform.position + Vector3.up * LevitateHeight + transform.forward * .3f;
                heldShard.transform.position = Vector3.Lerp(shardStart, hover, Mathf.SmoothStep(0, 1, t / Windup));
            }

            if (ThrowPivot)
            {
                float k = Mathf.SmoothStep(0, 1, t / Windup);
                ThrowPivot.localRotation = pivotRest * Quaternion.Euler(Mathf.Lerp(0, -130f, k), 0, 0);
            }

            // 蓄力時用藍線連到要推的道具，並把它往上浮空到頭頂的高度；道具被別人拿走（水平被拉動）就放棄。
            if (pushTarget)
            {
                var pushBody = pushTarget.GetComponent<Rigidbody>();
                Vector3 pv = pushBody ? pushBody.linearVelocity : Vector3.zero;
                if (pushBody && !pushBody.isKinematic && pv.x * pv.x + pv.z * pv.z < 9f)
                {
                    // 浮空的同時，長條形的道具（飾刀、劍）刀尖轉向玩家，之後砸出去的方向就是這個方向。
                    Vector3? aimPoint = null;
                    if (FindPlayer())
                    {
                        var aimCollider = Player.GetComponent<Collider>();
                        aimPoint = aimCollider ? aimCollider.bounds.center : Player.transform.position;
                    }

                    pushTarget.LevitateStep(transform.position.y + LevitateHeight, aimPoint);
                    ShowPushLine((ThrowPivot ? ThrowPivot.position : enemy.TargetPoint), pushBody.worldCenterOfMass);
                }
                else
                {
                    pushTarget.EndLevitate();
                    pushTarget = null;
                    HidePushLine();
                }
            }

            if (FindPlayer())
            {
                Face(Player.transform.position - transform.position);
            }

            yield return null;
        }

        HidePushLine();
        if (pushTarget)
        {
            PushProp(pushTarget);
            pushTarget = null;
        }
        else
        {
            Fire();
        }

        for (float t = 0; t < FollowThrough; t += Time.deltaTime)
        {
            if (ThrowPivot)
            {
                float k = Mathf.SmoothStep(0, 1, t / FollowThrough);
                ThrowPivot.localRotation = pivotRest * Quaternion.Euler(Mathf.Lerp(-130f, 25f, k), 0, 0);
            }

            yield return null;
        }

        ResetPivot();
        throwing = false;
    }

    private void ResetPivot()
    {
        if (ThrowPivot)
        {
            ThrowPivot.localRotation = pivotRest;
        }
    }

    private void Fire()
    {
        if (!Projectile || !FindPlayer() || enemy.IsDead || IsBrass)
        {
            DropHeldShard();
            return;
        }

        var collider = Player.GetComponent<Collider>();
        Vector3 target = collider ? collider.bounds.center : Player.transform.position;
        // 蓄力時浮在頭頂的碎片：從那裡直接瞄準玩家射出。
        var shot = heldShard;
        heldShard = null;
        Vector3 origin = shot ? shot.transform.position : (ThrowPivot ? ThrowPivot.position : enemy.TargetPoint) + transform.forward * .6f;
        Vector3 direction = (target - origin).normalized;
        if (shot)
        {
            shot.GetComponent<Rigidbody>().isKinematic = false;
            shot.transform.rotation = Quaternion.LookRotation(direction);
        }
        else
        {
            shot = Instantiate(Projectile, origin, Quaternion.LookRotation(direction));
            shot.Damage = ThrowDamage;
            shot.Knockback = ThrowKnockback;
            shot.Lifetime = 6;
        }

        shot.Launch(direction * ThrowSpeed, GetComponentsInChildren<Collider>(), false);
    }
}
