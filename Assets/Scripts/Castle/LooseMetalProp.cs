using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 可被推拉的金屬道具（金幣袋、武器、盾牌、鐵桶、吊燈…）。
/// 撞到東西會發出聲響（吸引敵人過來查看，Tin 也看得到方向）；被玩家推拉過（或剛被砸下）時撞到敵人會造成傷害與擊退。
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class LooseMetalProp : MonoBehaviour
{
    [Header("Damage (only while driven by the player / freshly dropped)")]
    [Min(0)] public float Damage = 0;
    [Tooltip("撞擊速度至少要這麼快才會造成傷害。")]
    [Min(0)] public float MinDamageSpeed = 4;
    public float KnockbackSpeed = 9;
    [Tooltip("大於 0：第一次重擊會傷害這個半徑內所有敵人（吊燈砸落用）。")]
    [Min(0)] public float AreaRadius = 0;
    [Tooltip("從敵人背後（飛行方向與敵人面向大致相同）命中時的傷害倍率。")]
    [Min(1)] public float BackstabMultiplier = 1;
    [Tooltip("被玩家攜帶後投擲（滾輪射出）時的傷害倍率。")]
    [Min(1)] public float ThrownDamageMultiplier = 1;

    [Header("Damage to the player (any ability-driven / dropped prop is dangerous)")]
    [Tooltip("撞到玩家的傷害（依速度縮放）。0 = 不會傷到玩家。")]
    [Min(0)] public float PlayerDamage = 0;
    [Tooltip("撞到玩家的速度至少要這麼快才會受傷。")]
    [Min(0)] public float PlayerMinSpeed = 5;
    [Tooltip("開始有殺傷力後的這段時間內不會傷到玩家（剛被推出去時還貼在玩家身邊）。")]
    [Min(0)] public float PlayerGrace = .3f;

    [Header("Noise")]
    [Tooltip("聲響傳播半徑（依撞擊速度縮放）。")]
    [Min(0)] public float NoiseRadius = 14;
    [Min(0)] public float MinNoiseSpeed = 1.5f;
    [Range(0, 1)] public float Volume = .8f;
    public float Pitch = 1;

    private Rigidbody body;
    private MetalImpactFeedback feedback;
    private AudioSource audioSource;
    private SoundEmitter soundEmitter;
    private float armedUntil, nextNoise, nextArea, nextPlayerHit, lethalSince = float.PositiveInfinity;
    private bool wasLethal;
    private Vector3 lastVelocity;
    private readonly Dictionary<Enemy, float> nextHit = new Dictionary<Enemy, float>();
    private static AudioClip clangClip;

    /// <summary>被敵人走路撞到（NavMeshAgent 不會產生真正的碰撞速度）：發出撞擊聲並傳播給其他敵人。</summary>
    private PlayerMetalManipulator manipulator;
    private MetalDisplayer metalDisplayer;

    private bool IsHeldByPlayer
    {
        get
        {
            if (!manipulator)
            {
                manipulator = FindFirstObjectByType<PlayerMetalManipulator>();
            }

            if (!metalDisplayer)
            {
                metalDisplayer = GetComponent<MetalDisplayer>();
            }

            return manipulator && manipulator.IsLocked && manipulator.Target.Valid && manipulator.Target.Metal == metalDisplayer;
        }
    }

    /// <summary>正被玩家攜帶（懸浮在肩上）。</summary>
    public bool IsCarried => IsHeldByPlayer && manipulator.IsCarrying;

    private float nextBumpNoise;

    public void Bump(float speed)
    {
        // 走路擦撞只是輕微的聲響：範圍小、冷卻長，否則一個敵人碰到東西全場的敵人都會被吸過來。
        if (Time.time < nextBumpNoise || IsCarried)
        {
            return;
        }

        nextBumpNoise = Time.time + 2.5f;
        nextNoise = Mathf.Max(nextNoise, Time.time + .25f);
        Clang(speed * .6f);
        NoiseSystem.Emit(transform.position, NoiseRadius * .35f * Mathf.Clamp(speed / 6f, .8f, 1.5f), Mathf.Clamp01(speed / 14f));
    }

    private float thrownUntil, hurledUntil;
    private bool hurled;

    /// <summary>被玩家攜帶後投擲出去：接下來這段時間內命中敵人享有 ThrownDamageMultiplier 傷害加成。</summary>
    public void MarkThrown(float seconds = 4f)
    {
        thrownUntil = Time.time + seconds;
    }

    /// <summary>敵人的金屬能力：把道具往上浮空（關掉重力）並懸停在指定高度，準備砸向玩家。</summary>
    [Tooltip("刀、劍這類長條形的道具：被敵人浮空 / 砸出去時，長軸（+Z）永遠對準前進方向。")]
    public bool AlignToTravel;
    private WallBlade wallBlade;

    private bool Aligns => AlignToTravel || GetWallBlade();

    private WallBlade GetWallBlade()
    {
        if (!wallBlade)
        {
            wallBlade = GetComponent<WallBlade>();
        }

        return wallBlade;
    }

    /// <summary>aim：浮空時，長條形道具的刀尖對準這個點（玩家），之後砸出去的方向就是這個方向。</summary>
    public void LevitateStep(float targetY, Vector3? aim = null)
    {
        if (!body)
        {
            body = GetComponent<Rigidbody>();
        }

        body.useGravity = false;
        Vector3 v = body.linearVelocity;
        float up = Mathf.Clamp((targetY - body.worldCenterOfMass.y) * 5f, -3f, 5f);
        body.linearVelocity = new Vector3(v.x * .8f, up, v.z * .8f);
        if (Aligns && aim.HasValue)
        {
            Vector3 look = aim.Value - body.worldCenterOfMass;
            if (look.sqrMagnitude > .01f)
            {
                body.constraints = RigidbodyConstraints.FreezeRotation;
                body.angularVelocity = Vector3.zero;
                body.MoveRotation(Quaternion.LookRotation(look.normalized, Vector3.up));
            }
        }
        else
        {
            body.angularVelocity *= .9f;
        }
    }

    /// <summary>浮空被取消（蓄力被打斷）：還給道具重力。</summary>
    public void EndLevitate()
    {
        if (body && !hurled)
        {
            body.useGravity = true;
            if (Aligns)
            {
                body.constraints = RigidbodyConstraints.None;
            }
        }
    }

    /// <summary>直接以指定速度砸出去：飛行中不受重力（直線），撞到任何東西或過了幾秒才恢復重力。</summary>
    public void Hurl(Vector3 velocity, float maxSeconds = 4f)
    {
        if (!body)
        {
            body = GetComponent<Rigidbody>();
        }

        // 飾刀：由 WallBlade 負責直線飛行（無重力、刀尖朝向前進方向、命中後損壞）。
        var blade = GetWallBlade();
        if (blade)
        {
            hurled = false;
            blade.Launch(velocity);
            return;
        }

        hurled = true;
        hurledUntil = Time.time + maxSeconds;
        body.useGravity = false;
        body.WakeUp();
        body.linearVelocity = velocity;
        if (AlignToTravel && velocity.sqrMagnitude > .01f)
        {
            // 長條形道具（劍）：長軸對準前進方向，飛行中不讓物理自己亂轉。
            body.constraints = RigidbodyConstraints.FreezeRotation;
            body.angularVelocity = Vector3.zero;
            body.rotation = Quaternion.LookRotation(velocity.normalized, Vector3.up);
        }
        else
        {
            body.angularVelocity = Random.onUnitSphere * 4f;
        }
    }

    /// <summary>重新計算「剛開始有殺傷力」的寬限時間（玩家把攜帶的道具射出去時用，避免剛離手就撞到自己）。</summary>
    public void ResetPlayerGrace()
    {
        lethalSince = Time.time;
        nextPlayerHit = Mathf.Max(nextPlayerHit, Time.time);
    }

    /// <summary>場景上所有可被推拉的道具（敵人的金屬能力用它找可以推的東西）。</summary>
    public static readonly List<LooseMetalProp> All = new List<LooseMetalProp>();
    private float enemyThrownUntil;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        All.Clear();
    }

    private void OnEnable()
    {
        All.Add(this);
    }

    private void OnDisable()
    {
        All.Remove(this);
    }

    /// <summary>被砸下（不是被玩家推拉）時，在這段時間內也算有殺傷力。enemyThrown：敵人推出來的，只會傷到玩家（不傷敵人）。</summary>
    public void Arm(float seconds, bool enemyThrown = false)
    {
        armedUntil = Mathf.Max(armedUntil, Time.time + seconds);
        if (enemyThrown)
        {
            enemyThrownUntil = Mathf.Max(enemyThrownUntil, Time.time + seconds);
        }
    }

    /// <summary>目前是敵人用能力砸出來的（還沒被玩家接手推拉）。</summary>
    public bool IsEnemyThrown => Time.time < enemyThrownUntil && !HarmsEnemies;

    /// <summary>敵人推出來的道具飛行中不會傷到敵人；但被玩家推拉過（推回去）就會。</summary>
    private bool HarmsEnemies
    {
        get
        {
            if (Time.time >= enemyThrownUntil)
            {
                return true;
            }

            if (!feedback)
            {
                feedback = GetComponent<MetalImpactFeedback>();
            }

            return feedback && feedback.IsPlayerDriven;
        }
    }

    public bool IsLethal
    {
        get
        {
            if (Time.time < armedUntil)
            {
                return true;
            }

            if (!feedback)
            {
                feedback = GetComponent<MetalImpactFeedback>();
            }

            return feedback && feedback.IsPlayerDriven;
        }
    }

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        soundEmitter = GetComponent<SoundEmitter>();
        if (!soundEmitter)
        {
            soundEmitter = gameObject.AddComponent<SoundEmitter>();
        }

        audioSource = GetComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 1;
        audioSource.minDistance = 4;
        audioSource.maxDistance = 40;
        soundEmitter.Source = audioSource;
    }

    private void FixedUpdate()
    {
        // 碰撞發生在物理模擬之後，所以先記下撞擊前的速度（判斷是不是從背後命中用）。
        lastVelocity = body.linearVelocity;
        if (hurled)
        {
            if (Time.time >= hurledUntil)
            {
                hurled = false;
                body.useGravity = true;
                body.constraints = RigidbodyConstraints.None;
            }
            else if (Aligns && !GetWallBlade() && lastVelocity.sqrMagnitude > 1f)
            {
                // 長條形道具飛行中：長軸一直對準前進方向。
                body.angularVelocity = Vector3.zero;
                body.MoveRotation(Quaternion.LookRotation(lastVelocity.normalized, Vector3.up));
            }
        }

        bool lethal = IsLethal;
        if (lethal && !wasLethal)
        {
            lethalSince = Time.time;
        }

        wasLethal = lethal;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.contactCount == 0)
        {
            return;
        }

        // 被敵人砸出來的道具：撞到任何東西後才恢復重力（與自由旋轉）。
        if (hurled)
        {
            hurled = false;
            body.useGravity = true;
            body.constraints = RigidbodyConstraints.None;
        }

        float speed = collision.relativeVelocity.magnitude;
        var contact = collision.GetContact(0);
        // 玩家攜帶中（或被玩家拉著靠到身上）：不發出碰撞聲響。
        bool quiet = IsCarried || (IsHeldByPlayer && collision.collider.GetComponentInParent<PlayerHealth>());
        if (!quiet && speed >= MinNoiseSpeed && Time.time >= nextNoise)
        {
            nextNoise = Time.time + .25f;
            Clang(speed);
            NoiseSystem.Emit(contact.point, NoiseRadius * Mathf.Clamp(speed / 6f, .8f, 2f), Mathf.Clamp01(speed / 10f));
        }

        if (!IsLethal)
        {
            return;
        }

        // 被能力移動中 / 砸落中的物件也會傷到玩家。
        var player = collision.collider.GetComponentInParent<PlayerHealth>();
        if (player && !player.IsDead)
        {
            if (PlayerDamage > 0 && speed >= PlayerMinSpeed && Time.time >= nextPlayerHit && Time.time - lethalSince >= PlayerGrace)
            {
                nextPlayerHit = Time.time + 1f;
                HurtPlayer(player, speed, player.transform.position - contact.point);
            }
        }

        if (speed < MinDamageSpeed || Damage <= 0)
        {
            return;
        }

        var enemy = collision.collider.GetComponentInParent<Enemy>();
        if (enemy && !HarmsEnemies)
        {
            return;
        }

        if (enemy && !enemy.IsDead && (!nextHit.TryGetValue(enemy, out float next) || Time.time >= next))
        {
            nextHit[enemy] = Time.time + .5f;
            float amount = Damage * Mathf.Clamp(speed / 10f, .5f, 2f);
            if (IsBackstab(enemy))
            {
                amount *= BackstabMultiplier;
            }

            if (Time.time < thrownUntil)
            {
                amount *= ThrownDamageMultiplier;
            }

            Hurt(enemy, amount, enemy.transform.position - transform.position);
        }

        if (AreaRadius > 0 && Time.time >= nextArea)
        {
            nextArea = Time.time + 1.5f;
            Blast(contact.point);
        }
    }

    /// <summary>飛行方向與敵人面向大致相同 = 從背後打中他。</summary>
    private bool IsBackstab(Enemy enemy)
    {
        if (BackstabMultiplier <= 1)
        {
            return false;
        }

        Vector3 flight = lastVelocity;
        flight.y = 0;
        Vector3 facing = enemy.transform.forward;
        facing.y = 0;
        return flight.sqrMagnitude > .25f && facing.sqrMagnitude > .01f && Vector3.Dot(flight.normalized, facing.normalized) > .25f;
    }

    /// <summary>玩家用能力推拉的物體不會傷到玩家自己（只有敵人推出來的、或砸落的吊燈才會）。</summary>
    private bool HarmsPlayer
    {
        get
        {
            if (!feedback)
            {
                feedback = GetComponent<MetalImpactFeedback>();
            }

            return !(feedback && feedback.IsPlayerDriven);
        }
    }

    private void HurtPlayer(PlayerHealth player, float speed, Vector3 direction)
    {
        if (!HarmsPlayer)
        {
            return;
        }

        BossHit.DamageAndKnockback(player, PlayerDamage * Mathf.Clamp(speed / 10f, .6f, 1.8f), direction, 7f, 2f);
    }

    private void Blast(Vector3 point)
    {
        var done = new HashSet<Enemy>();
        PlayerHealth hurtPlayer = null;
        foreach (var hit in Physics.OverlapSphere(point, AreaRadius, ~0, QueryTriggerInteraction.Ignore))
        {
            var player = hit.GetComponentInParent<PlayerHealth>();
            if (player && !hurtPlayer && PlayerDamage > 0 && Time.time - lethalSince >= PlayerGrace)
            {
                hurtPlayer = player;
                float falloff = 1f - Mathf.Clamp01(Vector3.Distance(point, player.transform.position) / AreaRadius) * .5f;
                HurtPlayer(player, 10f * falloff, player.transform.position - point);
            }

            var enemy = hit.GetComponentInParent<Enemy>();
            if (!enemy || !HarmsEnemies || enemy.IsDead || !done.Add(enemy))
            {
                continue;
            }

            float distance = Vector3.Distance(point, enemy.transform.position);
            float scale = 1f - Mathf.Clamp01(distance / AreaRadius) * .5f;
            Hurt(enemy, Damage * scale, enemy.transform.position - point);
        }
    }

    private void Hurt(Enemy enemy, float damage, Vector3 direction)
    {
        enemy.TakeDamage(damage);
        var melee = enemy.GetComponent<TestEnemy>();
        if (melee && melee.isActiveAndEnabled && !enemy.IsDead)
        {
            melee.ApplyKnockback(direction, KnockbackSpeed, .35f);
        }

        CombatFeedbackEvents.PublishMetalImpact(enemy.TargetPoint, Vector3.up, KnockbackSpeed);
    }

    private void Clang(float speed)
    {
        if (!audioSource || !soundEmitter)
        {
            return;
        }

        if (!clangClip)
        {
            clangClip = MakeClang();
        }

        audioSource.pitch = Pitch * Random.Range(.88f, 1.15f);
        soundEmitter.Play(clangClip, Volume * Mathf.Clamp(.35f + speed / 12f, .35f, 1f));
    }

    /// <summary>程式合成的金屬撞擊聲：幾個不和諧的正弦波 + 開頭的一小段雜訊，指數衰減。</summary>
    private static AudioClip MakeClang()
    {
        const int rate = 22050;
        int count = (int)(rate * .6f);
        var data = new float[count];
        float[] frequencies = { 640f, 1040f, 1760f, 2710f };
        float[] amplitudes = { 1f, .6f, .4f, .25f };
        var random = new System.Random(7);
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)rate;
            float tone = 0;
            for (int k = 0; k < frequencies.Length; k++)
            {
                tone += amplitudes[k] * Mathf.Sin(2f * Mathf.PI * frequencies[k] * t) * Mathf.Exp(-t * (6f + k * 3f));
            }

            float noise = ((float)random.NextDouble() * 2f - 1f) * Mathf.Exp(-t * 70f);
            data[i] = Mathf.Clamp(tone * .35f + noise * .5f, -1f, 1f);
        }

        var clip = AudioClip.Create("Metal Clang", count, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
