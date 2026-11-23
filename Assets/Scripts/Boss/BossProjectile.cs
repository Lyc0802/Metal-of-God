using UnityEngine;

public enum BossProjectileKind
{
    /// <summary>帶有 MetalDisplayer，可被 Iron 拉 / Steel 推。</summary>
    Metal,
    /// <summary>沒有 MetalDisplayer（石頭、木頭等），無法被選取或推拉。</summary>
    NonMetal
}

/// <summary>Boss 攻擊共用的玩家傷害與擊退。</summary>
public static class BossHit
{
    public static void DamageAndKnockback(PlayerHealth player, float damage, Vector3 direction, float speed, float up)
    {
        if (!player || player.IsDead)
        {
            return;
        }

        player.TakeDamage(damage);
        var body = player.GetComponent<Rigidbody>();
        if (!body || player.IsDead)
        {
            return;
        }

        var manipulator = player.GetComponent<PlayerMetalManipulator>();
        if (manipulator)
        {
            manipulator.Release();
        }

        // 玩家揮劍中（且在地面）時 PlayerMovement 會把水平速度歸零，擊退會被吃掉，所以先中斷攻擊。
        var combat = player.GetComponent<PlayerCombat>();
        if (combat && combat.IsAttacking)
        {
            combat.CancelAttack();
        }

        direction.y = 0;
        if (direction.sqrMagnitude < .001f)
        {
            direction = -player.transform.forward;
        }

        body.linearVelocity = direction.normalized * speed + Vector3.up * up;
        var movement = player.GetComponent<PlayerMovement>();
        if (movement)
        {
            movement.PreserveExternalMomentum();
        }
    }
}

/// <summary>
/// Boss 發射的投射物。命中玩家造成傷害與擊退；被玩家推拉過的金屬投射物擊中 Boss 時反過來傷害 Boss。
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class BossProjectile : MonoBehaviour
{
    public BossProjectileKind Kind = BossProjectileKind.NonMetal;
    [Min(0)]
    public float Damage = 10;
    [Min(0)]
    public float DamageToBoss = 40;
    [Min(0)]
    public float Knockback = 14;
    public float KnockbackUp = 2;
    [Min(.1f)]
    public float Lifetime = 8;
    [Tooltip("速度低於此值的碰撞不造成傷害，避免靜止接觸傷人。")]
    [Min(0)]
    public float MinimumHitSpeed = 3;

    private Rigidbody body;
    private bool spent, gravityOnImpact;
    private Vector3 lastVelocity;
    private float groundArmTime;
    private const float GroundGrace = .3f, DustMinSpeed = 2.5f, GroundHeight = .8f;
    private float nextDustTime;
    private static PhysicsMaterial bounceMaterial;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        // 撞到牆壁、柱子時依物理彈開：彈性取兩者較大值（牆本身彈性為 0）。
        if (!bounceMaterial)
        {
            bounceMaterial = new PhysicsMaterial("Boss Projectile Bounce")
            {
                bounciness = .55f,
                dynamicFriction = .2f,
                staticFriction = .2f,
                bounceCombine = PhysicsMaterialCombine.Maximum,
                frictionCombine = PhysicsMaterialCombine.Minimum
            };
        }

        foreach (var c in GetComponentsInChildren<Collider>())
        {
            c.sharedMaterial = bounceMaterial;
        }

        groundArmTime = Time.time + GroundGrace;
    }

    private void Start()
    {
        Destroy(gameObject, Lifetime);
    }

    private void FixedUpdate()
    {
        lastVelocity = body.linearVelocity;
        // 持續偵測：玩家一旦發動推拉，就永久脫離 Boss 的控制。
        if (!takenByPlayer && Kind == BossProjectileKind.Metal)
        {
            takenByPlayer = IsPlayerControlled;
        }
    }

    /// <summary>發射。gravity=false 表示直線飛行，撞到牆或其他物體後才開始下墜。</summary>
    public void Launch(Vector3 velocity, Collider[] ignore = null, bool gravity = false)
    {
        if (!body)
        {
            body = GetComponent<Rigidbody>();
        }

        body.useGravity = gravity;
        gravityOnImpact = !gravity;
        groundArmTime = Time.time + GroundGrace;
        body.linearVelocity = velocity;
        lastVelocity = velocity;
        if (ignore == null)
        {
            return;
        }

        foreach (var a in GetComponentsInChildren<Collider>())
        {
            foreach (var b in ignore)
            {
                if (a && b)
                {
                    Physics.IgnoreCollision(a, b, true);
                }
            }
        }
    }

    private int groundBouncesLeft;

    /// <summary>接下來前 count 次碰到地面時反彈（而不是消失）。</summary>
    public void AllowGroundBounces(int count)
    {
        groundBouncesLeft = Mathf.Max(0, count);
    }

    /// <summary>接下來 seconds 秒內，碰到地面不會消失（奪回 / 重新射出金屬時使用）。</summary>
    public void SuppressGroundVanish(float seconds)
    {
        groundArmTime = Time.time + seconds;
    }

    /// <summary>是否正被玩家推拉驅動。</summary>
    public bool IsDrivenByPlayer => IsDriven();

    private bool takenByPlayer;
    private static PlayerMetalManipulator cachedManipulator;

    /// <summary>
    /// 玩家曾對這顆金屬發動推 / 拉（含訊號飛行中、已鎖定、被驅動）後永遠為 true：
    /// Boss 會主動移動金屬的技能（射出、奪回、重新射出）從此不再控制它。
    /// </summary>
    public bool IsPlayerControlled
    {
        get
        {
            if (takenByPlayer)
            {
                return true;
            }

            if (IsDriven() || TargetedByPlayer())
            {
                takenByPlayer = true;
            }

            return takenByPlayer;
        }
    }

    private bool TargetedByPlayer()
    {
        var metal = GetComponent<MetalDisplayer>();
        if (!metal)
        {
            return false;
        }

        if (!cachedManipulator)
        {
            cachedManipulator = FindFirstObjectByType<PlayerMetalManipulator>();
        }

        var m = cachedManipulator;
        return m && m.IsLocked && m.Target.Metal == metal;
    }

    private bool IsDriven()
    {
        var feedback = GetComponent<MetalImpactFeedback>();
        return Kind == BossProjectileKind.Metal && feedback && feedback.IsPlayerDriven;
    }

    private void OnCollisionEnter(Collision collision)
    {
        // 撞到任何東西（地面、牆、柱子、玩家、Boss）時噴出小粉塵；彼此相撞與慢速接觸不算。
        if (collision.contactCount > 0 && Time.time >= nextDustTime
            && collision.relativeVelocity.magnitude >= DustMinSpeed
            && !collision.collider.GetComponentInParent<BossProjectile>())
        {
            nextDustTime = Time.time + .12f;
            var contact = collision.GetContact(0);
            DustPuff.Play(contact.point, contact.normal, collision.relativeVelocity.magnitude);
        }

        // 擊中地面（朝上的表面）的瞬間消失（石頭與金屬皆是）；打到玩家或敵人仍照常結算。
        // 剛生成 / 剛發射的短暫期間不判定，避免從地面噴出時立刻消失。
        // 只有撞到地面（朝上的表面、在地面高度）會消失；牆壁、柱子等則依物理彈開。
        if (Time.time >= groundArmTime && collision.contactCount > 0
            && collision.GetContact(0).normal.y > .7f
            && collision.GetContact(0).point.y < GroundHeight
            && !collision.collider.GetComponentInParent<PlayerHealth>()
            && !collision.collider.GetComponentInParent<Enemy>()
            && !collision.collider.GetComponentInParent<BossProjectile>())
        {
            if (groundBouncesLeft > 0)
            {
                // 奪回後射出的金屬：第一次碰到地面時反彈（之後開始受重力），不消失。
                groundBouncesLeft--;
                body.useGravity = true;
                gravityOnImpact = false;
                return;
            }

            spent = true;
            Destroy(gameObject);
            return;
        }

        if (gravityOnImpact && collision.contactCount > 0 && collision.GetContact(0).normal.y < .7f)
        {
            body.useGravity = true;
            gravityOnImpact = false;
        }

        if (spent || collision.relativeVelocity.magnitude < MinimumHitSpeed)
        {
            return;
        }

        bool driven = IsDriven();
        var player = collision.collider.GetComponentInParent<PlayerHealth>();
        if (player)
        {
            // 玩家自己推拉的投射物不會傷到玩家。
            if (driven)
            {
                return;
            }

            spent = true;
            Vector3 dir = new Vector3(lastVelocity.x, 0, lastVelocity.z);
            if (dir.magnitude < 1.5f)
            {
                dir = player.transform.position - transform.position;
            }

            BossHit.DamageAndKnockback(player, Damage, dir, Knockback, KnockbackUp);
            Destroy(gameObject);
            return;
        }

        // 只有被玩家推拉驅動的金屬投射物才能傷害 Boss。
        var enemy = collision.collider.GetComponentInParent<Enemy>();
        if (enemy && driven)
        {
            spent = true;
            enemy.TakeDamage(DamageToBoss);
            Destroy(gameObject);
        }
    }
}
