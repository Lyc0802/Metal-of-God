using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 敵人的感知與團隊行為（不改動 TestEnemy / EnemyShooter 本身的 AI，只透過它們的公開介面）：
/// - 聽覺：金屬撞擊、吊燈砸落等聲響會讓閒晃中的敵人過去查看；埋伏者只有聲響很近才會被吵醒。
/// - 警報：有敵人親眼發現玩家、或同伴被打時，附近的敵人會一起追擊（所以直接衝進去會被圍攻）。
/// - 光線：玩家站在黑暗中時，敵人的視覺偵測範圍縮小；站在火光下則完整。
/// 吸引仇恨 = 製造聲響把敵人引開；降低仇恨 = 躲進黑暗、用 Brass 安撫。
/// </summary>
[RequireComponent(typeof(Enemy))]
public class EnemySenses : MonoBehaviour
{
    [Header("Hearing")]
    [Tooltip("訓練有素的哨兵：不會被聲響引開（金幣、撞擊聲都沒用），只能用 Zinc 把他激怒引出來，或用 Brass 讓他冷靜。")]
    public bool Stoic;
    [Tooltip("聽覺範圍倍率（對金屬撞擊聲等的警覺性）。")]
    [Min(0)] public float HearingScale = 1.7f;
    [Tooltip("埋伏者只有聲響在這個距離內才會被吵醒。")]
    [Min(0)] public float AmbushHearRadius = 9;
    [Tooltip("到達聲響來源後，停留多久才回去（秒）。")]
    [Min(0)] public float InvestigateWait = 6;

    [Header("Alert")]
    [Tooltip("發現玩家時，警報傳播的半徑。")]
    [Min(0)] public float ShoutRadius = 20;
    [Tooltip("自己被打到時，戰鬥聲響傳播的半徑（讓同伴過去查看，不是警報）。")]
    [Min(0)] public float FightNoiseRadius = 12;
    [Tooltip("收到警報後持續追擊的時間（秒）。")]
    [Min(0)] public float AlertSeconds = 8;

    [Header("Light")]
    [Tooltip("玩家完全在黑暗中時，視覺偵測範圍縮小到這個比例。")]
    [Range(.1f, 1)] public float DarkVisionScale = .4f;

    private Enemy enemy;
    private TestEnemy melee;
    private EnemyShooter shooter;
    private EnemyAmbush ambush;
    private NavMeshAgent agent;
    private PlayerHealth player;
    private float baseMeleeRange, baseShooterRange, baseTriggerRange;
    private float lastHealth, nextShout, investigateUntil, arrivedAt = -1;
    private bool investigating;

    private void Awake()
    {
        enemy = GetComponent<Enemy>();
        melee = GetComponent<TestEnemy>();
        shooter = GetComponent<EnemyShooter>();
        ambush = GetComponent<EnemyAmbush>();
        agent = GetComponent<NavMeshAgent>();
    }

    private void Start()
    {
        if (melee)
        {
            baseMeleeRange = melee.DetectionRange;
        }

        if (shooter)
        {
            baseShooterRange = shooter.DetectionRange;
        }

        if (ambush)
        {
            baseTriggerRange = ambush.TriggerRange;
        }

        lastHealth = enemy.CurrentHealth;
    }

    private void OnEnable()
    {
        NoiseSystem.Heard += OnNoise;
        AlertNetwork.Raised += OnAlert;
    }

    private void OnDisable()
    {
        NoiseSystem.Heard -= OnNoise;
        AlertNetwork.Raised -= OnAlert;
    }

    private bool FindPlayer()
    {
        if (!player)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p)
            {
                player = p.GetComponent<PlayerHealth>();
            }
        }

        return player && !player.IsDead;
    }

    private void Update()
    {
        if (enemy.IsDead)
        {
            if (shooter)
            {
                shooter.Investigating = false;
            }

            enabled = false;
            return;
        }

        // 被打到：戰鬥聲響會讓附近閒晃的同伴過去查看（只是聲響，不是警報；沉睡的埋伏者只有離得很近才會醒）。
        if (enemy.CurrentHealth < lastHealth - .01f)
        {
            NoiseSystem.Emit(transform.position, FightNoiseRadius, .8f);
        }

        lastHealth = enemy.CurrentHealth;
        DetectBumps();
        if (!FindPlayer())
        {
            return;
        }

        UpdateVision();
        UpdateShout();
        UpdateInvestigation();
    }

    // --- 自己走路撞到道具（鐵桶、火盆、武器…）：會發出撞擊聲，其他敵人聽到會過來查看（自己不理會自己撞出的聲音）。
    private readonly Collider[] bumpBuffer = new Collider[8];
    private float nextBump;
    private bool selfNoise;

    private void DetectBumps()
    {
        if (Time.time < nextBump || !agent || !agent.enabled || !agent.isOnNavMesh || agent.velocity.sqrMagnitude < .09f)
        {
            return;
        }

        Vector3 heading = agent.velocity.normalized;
        Vector3 probe = transform.position + Vector3.up * .8f + heading * (agent.radius * .6f);
        int count = Physics.OverlapSphereNonAlloc(probe, agent.radius + .1f, bumpBuffer, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var prop = bumpBuffer[i].GetComponentInParent<LooseMetalProp>();
            // 鐵桶 / 火盆這種有導航障礙物的重物，敵人尋路本來就會繞開，擦到邊不算撞擊。
            if (!prop || prop.GetComponent<UnityEngine.AI.NavMeshObstacle>())
            {
                continue;
            }

            nextBump = Time.time + 2f;
            selfNoise = true;
            prop.Bump(Mathf.Max(2.5f, agent.velocity.magnitude + 1f));
            selfNoise = false;
            return;
        }
    }

    // --- 光線：黑暗中視覺範圍縮小（已經在追擊的敵人不受影響，免得一進暗處就瞬間丟失目標）。
    private void UpdateVision()
    {
        var collider = player.GetComponent<Collider>();
        Vector3 point = collider ? collider.bounds.center : player.transform.position;
        float scale = Mathf.Lerp(DarkVisionScale, 1f, StealthLighting.Exposure(point));
        if (melee && baseMeleeRange > 0)
        {
            bool engaged = melee.isActiveAndEnabled && melee.State != EnemyState.Wander;
            melee.DetectionRange = baseMeleeRange * (engaged ? 1f : scale);
        }

        if (shooter && baseShooterRange > 0)
        {
            shooter.DetectionRange = baseShooterRange * (shooter.SeesPlayer ? 1f : scale);
        }

        if (ambush && baseTriggerRange > 0 && !ambush.IsAwake)
        {
            ambush.TriggerRange = baseTriggerRange * scale;
        }
    }

    // --- 發現玩家：自己看到（不是別人警報或玩家 Zinc 造成的追擊）才發出警報。
    private void UpdateShout()
    {
        if (Time.time < nextShout)
        {
            return;
        }

        bool sawPlayer = false;
        if (melee && melee.isActiveAndEnabled)
        {
            sawPlayer = melee.State != EnemyState.Wander && !melee.IsZincActive && !melee.IsBrassActive;
        }
        else if (shooter && shooter.isActiveAndEnabled)
        {
            sawPlayer = shooter.SeesPlayer;
        }

        if (sawPlayer)
        {
            nextShout = Time.time + 2f;
            AlertNetwork.Raise(transform.position, ShoutRadius);
        }
    }

    private void UpdateInvestigation()
    {
        if (!investigating)
        {
            return;
        }

        // 開始追擊 / 戰鬥就不再查看聲響。
        bool engaged = melee && melee.isActiveAndEnabled && melee.State != EnemyState.Wander;
        bool arrived = agent && agent.enabled && agent.isOnNavMesh && !agent.pathPending && agent.remainingDistance <= .6f;
        if (arrived && arrivedAt < 0)
        {
            arrivedAt = Time.time;
        }

        if (engaged || Time.time >= investigateUntil || (arrivedAt >= 0 && Time.time >= arrivedAt + InvestigateWait))
        {
            StopInvestigating();
        }
    }

    private void StopInvestigating()
    {
        if (investigating)
        {
            noInvestigateUntil = Time.time + 10f;
        }

        investigating = false;
        arrivedAt = -1;
        if (shooter)
        {
            shooter.Investigating = false;
        }
    }

    // 同一個地方的聲響不重複引人過去：正在往那邊走、或剛查看完沒多久，就不再理會附近的新聲響
    // （否則一群人擠在聲源旁互相碰撞，又製造更多聲響，永遠散不開）。
    private Vector3 lastInvestigatePosition;
    private float noInvestigateUntil;
    private const float SameSpotRadius = 8f;

    private void Investigate(Vector3 position)
    {
        if (!agent || !agent.enabled || !agent.isOnNavMesh)
        {
            return;
        }

        bool nearLast = (position - lastInvestigatePosition).sqrMagnitude < SameSpotRadius * SameSpotRadius;
        if (nearLast && (investigating || Time.time < noInvestigateUntil))
        {
            return;
        }

        lastInvestigatePosition = position;
        if (!NavMesh.SamplePosition(position, out var hit, 4f, agent.areaMask))
        {
            return;
        }

        agent.isStopped = false;
        agent.SetDestination(hit.position);
        investigating = true;
        arrivedAt = -1;
        investigateUntil = Time.time + 12f;
        if (shooter)
        {
            shooter.Investigating = true;
        }
    }

    private void OnNoise(Vector3 position, float radius, float loudness)
    {
        if (!isActiveAndEnabled || enemy.IsDead || selfNoise)
        {
            return;
        }

        float distance = Vector3.Distance(transform.position, position);
        if (ambush && !ambush.IsAwake)
        {
            // 埋伏者：只有聲響非常近才會被吵醒，遠處的聲響不影響它。
            if (distance <= AmbushHearRadius)
            {
                ambush.Wake();
            }

            return;
        }

        if (Stoic || distance > radius * HearingScale)
        {
            return;
        }

        if (melee && melee.isActiveAndEnabled)
        {
            if (melee.State == EnemyState.Wander && !melee.IsBrassActive && !melee.IsZincActive)
            {
                Investigate(position);
            }
        }
        else if (shooter && shooter.isActiveAndEnabled && !shooter.SeesPlayer && !shooter.IsCalmed)
        {
            Investigate(position);
        }
    }

    private void OnAlert(Vector3 position, float radius)
    {
        if (!isActiveAndEnabled || enemy.IsDead || Vector3.Distance(transform.position, position) > radius)
        {
            return;
        }

        if (ambush && !ambush.IsAwake)
        {
            ambush.Wake();
            return;
        }

        if (melee && melee.isActiveAndEnabled && !melee.IsBrassActive)
        {
            StopInvestigating();
            melee.OnZinc();
        }

        if (shooter && shooter.isActiveAndEnabled)
        {
            StopInvestigating();
            shooter.Alert(AlertSeconds);
        }
    }
}
