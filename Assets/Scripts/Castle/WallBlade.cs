using UnityEngine;

/// <summary>
/// 掛在牆上的擺飾刀：平時固定在牆上，玩家對它發動推 / 拉的瞬間它就脫離牆面。
/// 脫離後在撞到任何東西之前不受重力影響（直線飛行），刀尖永遠朝著飛行方向；撞到東西或速度掉到很低才開始下墜。
/// 用 Iron 把它拉向自己，飛行路徑上的敵人會被刺中；從背後命中（敵人面向玩家、刀從他身後的牆飛來）傷害倍增。
/// PlayerMetalSelector 允許選取被敵人擋在前面的飾刀（穿過敵人瞄準牆上的刀）。
/// </summary>
[RequireComponent(typeof(Rigidbody), typeof(MetalDisplayer), typeof(LooseMetalProp))]
public class WallBlade : MonoBehaviour
{
    [Tooltip("飛行中速度低於這個值就視為飛行結束（開始受重力）。")]
    [Min(0)] public float MinFlightSpeed = 2.5f;
    [Tooltip("脫離牆面後這段時間內的碰撞不算（刀剛離開牆面時可能還貼著牆 / 旁邊的刀）。")]
    [Min(0)] public float ImpactGrace = .15f;

    public bool IsFree { get; private set; }
    /// <summary>脫離牆面後、還沒撞到東西：直線飛行中。</summary>
    public bool IsFlying { get; private set; }

    private MetalDisplayer metal;
    private Rigidbody body;
    private PlayerMetalManipulator manipulator;
    private float freedAt;

    private void Awake()
    {
        metal = GetComponent<MetalDisplayer>();
        body = GetComponent<Rigidbody>();
    }

    private bool carried;

    [Header("Durability")]
    [Tooltip("脫離牆面後，以這個相對速度以上命中敵人或玩家就會損壞（碎掉）。")]
    [Min(0)] public float BreakOnHitSpeed = 4f;
    [Tooltip("一次性（Boss 戰用）：被投擲（射出）之後，碰到任何東西——玩家、牆壁、敵人、地板——都會損壞。")]
    public bool SingleUse;
    private bool broken, thrown;
    private float thrownAt;

    private void Update()
    {
        if (!manipulator)
        {
            manipulator = FindFirstObjectByType<PlayerMetalManipulator>();
        }

        if (IsFree)
        {
            return;
        }

        if (manipulator && manipulator.IsLocked && manipulator.Target.Valid && manipulator.Target.Metal == metal)
        {
            Free();
        }
    }

    private void LateUpdate()
    {
        // 被玩家重新鎖定（拉回來）就不再算「投擲中」。
        if (thrown && manipulator && manipulator.IsLocked && manipulator.Target.Valid && manipulator.Target.Metal == metal)
        {
            thrown = false;
        }
    }

    /// <summary>刀損壞：碎成幾片金屬碎片四散，發出聲響後消失。</summary>
    public void Break()
    {
        if (broken)
        {
            return;
        }

        broken = true;
        var rend = GetComponent<Renderer>();
        var material = rend ? rend.sharedMaterial : null;
        for (int i = 0; i < 5; i++)
        {
            var shard = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shard.name = "Dagger Shard";
            shard.transform.position = transform.position + Random.insideUnitSphere * .2f;
            shard.transform.rotation = Random.rotation;
            shard.transform.localScale = new Vector3(.04f, .06f, Random.Range(.12f, .3f));
            if (material)
            {
                shard.GetComponent<Renderer>().sharedMaterial = material;
            }

            var shardBody = shard.AddComponent<Rigidbody>();
            shardBody.mass = .05f;
            shardBody.linearVelocity = body.linearVelocity * .3f + Random.onUnitSphere * 3f + Vector3.up * 2f;
            shardBody.angularVelocity = Random.onUnitSphere * 12f;
            Destroy(shard, 3f);
        }

        var prop = GetComponent<LooseMetalProp>();
        if (prop)
        {
            prop.Bump(8f);
        }

        Destroy(gameObject);
    }

    /// <summary>玩家把攜帶的刀射出去（或任何時候以指定速度直線射出）：直線飛行，撞到東西之前不受重力，刀尖朝飛行方向。</summary>
    public void Launch(Vector3 velocity)
    {
        IsFree = true;
        IsFlying = true;
        carried = false;
        freedAt = Time.time;
        thrown = true;
        thrownAt = Time.time;
        body.isKinematic = false;
        body.useGravity = false;
        body.constraints = RigidbodyConstraints.FreezeRotation;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.WakeUp();
        body.linearVelocity = velocity;
        if (velocity.sqrMagnitude > .01f)
        {
            body.rotation = Quaternion.LookRotation(velocity.normalized, Vector3.up);
        }
    }

    private void FixedUpdate()
    {
        // 玩家攜帶著它（按住右鍵、已貼在身上）：刀尖朝準心方向，像是拿在手上。
        if (IsFree && manipulator && manipulator.IsCarrying && manipulator.Target.Valid && manipulator.Target.Metal == metal)
        {
            carried = true;
            body.constraints = RigidbodyConstraints.FreezeRotation;
            body.angularVelocity = Vector3.zero;
            body.MoveRotation(Quaternion.LookRotation(manipulator.AimDirection(body.worldCenterOfMass), Vector3.up));
            return;
        }

        if (carried)
        {
            carried = false;
            if (!IsFlying)
            {
                body.constraints = RigidbodyConstraints.None;
                body.useGravity = true;
            }
        }

        if (!IsFlying)
        {
            return;
        }

        Vector3 velocity = body.linearVelocity;
        if (velocity.sqrMagnitude >= MinFlightSpeed * MinFlightSpeed)
        {
            // 刀尖（+Z）朝向飛行方向；不讓物理自己亂轉。
            body.angularVelocity = Vector3.zero;
            body.MoveRotation(Quaternion.LookRotation(velocity.normalized, Vector3.up));
        }
        else if (Time.time - freedAt > ImpactGrace + .2f)
        {
            EndFlight();
        }
    }

    /// <summary>脫離牆面，變成可推拉的金屬；撞到東西之前直線飛行。</summary>
    public void Free()
    {
        if (IsFree)
        {
            return;
        }

        IsFree = true;
        IsFlying = true;
        thrown = false;
        freedAt = Time.time;
        body.isKinematic = false;
        body.useGravity = false;
        body.constraints = RigidbodyConstraints.FreezeRotation;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        NoiseSystem.Emit(transform.position, 7f, .3f);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!IsFree || collision.collider.GetComponentInParent<WallBlade>())
        {
            return;
        }

        // 一次性的刀：投擲出去之後碰到任何東西都損壞（傷害在同一次碰撞中由 LooseMetalProp 結算）。
        if (SingleUse && thrown && Time.time - thrownAt >= .05f)
        {
            Break();
            return;
        }

        // 命中敵人或玩家：刀損壞（傷害由 LooseMetalProp 在同一次碰撞中結算，物件在這一幀結束後才消失）。
        if (collision.relativeVelocity.magnitude >= BreakOnHitSpeed)
        {
            var enemy = collision.collider.GetComponentInParent<Enemy>();
            var player = collision.collider.GetComponentInParent<PlayerHealth>();
            // 不檢查是否已死亡：同一次碰撞裡 LooseMetalProp 可能剛把敵人打死（先後順序不固定）。
            // 碰到玩家只有「敵人砸出來的」才會碎；玩家自己拉過來 / 攜帶時碰到身體不會。
            var prop = GetComponent<LooseMetalProp>();
            bool brokenByEnemyThrow = player && prop && prop.IsEnemyThrown;
            if (enemy || brokenByEnemyThrow)
            {
                Break();
                return;
            }
        }

        // 其他東西（牆、地板…）才受剛脫離牆面的寬限時間影響。
        if (IsFlying && Time.time - freedAt >= ImpactGrace)
        {
            EndFlight();
        }
    }

    private void EndFlight()
    {
        IsFlying = false;
        body.useGravity = true;
        body.constraints = RigidbodyConstraints.None;
    }
}
