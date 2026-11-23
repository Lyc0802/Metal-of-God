using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 鋼鐵審判者 Boss。開局 Boss 靜止準備（玩家可自由行動），之後緩慢逼近玩家。
/// 近距離：旋轉斬（圓形、後擊退）、直劈（直線、側擊退）。
/// 遠距離：踩地召喚金屬雨（扇形落下後往外射出）、跳砸（地面噴出金屬 + 全場落石）。
/// 視覺結構：Boss > Spinner(旋轉/上升) > Body(蹲下縮放)；Spinner > Axe Pivot(斧頭骨架)。
/// 斧頭本體 AxeRoot 獨立於 Boss 階層外（否則會被選取系統當成敵人而不是金屬），每幀跟隨 Axe Pivot。
/// </summary>
[RequireComponent(typeof(Enemy))]
public partial class SteelInquisitorBoss : MonoBehaviour
{
    public PlayerHealth Player;

    [Header("Rig")]
    public Transform Spinner;
    public Transform Body;
    public Transform AxePivot;
    public GameObject SpinIndicator;
    public GameObject SlashIndicator;
    [Tooltip("近戰（旋轉斬、直劈）前搖時是否在地上顯示紅色預警範圍。")]
    public bool ShowMeleeTelegraph = false;
    [Tooltip("可推拉的斧頭（獨立根物件）。每幀跟隨 AxePivot。")]
    public Transform AxeRoot;
    public GameObject SlamIndicator;

    [Header("Projectiles")]
    public BossProjectile MetalPrefab;
    public BossProjectile StonePrefab;
    public Material MarkerMaterial;

    [Header("Behaviour")]
    [Tooltip("戰鬥開始後，Boss 完全不動的準備時間（秒）。玩家不受影響。")]
    [Min(0)] public float PrepareTime = 5;
    [Min(0)] public float MoveSpeed = 2.2f;
    [Min(0)] public float TurnSpeed = 120;
    [Tooltip("玩家進入此距離後使用近戰攻擊。")]
    [Min(1)] public float TriggerRange = 8;
    [Tooltip("玩家超過此距離時才會使用遠程技能。")]
    [Min(1)] public float FarRange = 15;
    [Min(0)] public float Cooldown = 2.5f;
    [Min(0)] public float RangedCooldown = 3;
    [Min(1)] public float ArenaRadius = 26;
    [Tooltip("玩家中心高於此高度（相對 Boss）時不會被近戰命中。斧頭很大，跳起來也躲不掉。")]
    public float HitHeight = 8;
    [Tooltip("命中判定時，額外加上的玩家半徑（以玩家中心點判定的補償）。")]
    public float PlayerHitMargin = .5f;

    [Header("Attack 1 - Spin (circle)")]
    [Min(1)] public float SpinRadius = 10;
    [Min(.1f)] public float SpinWindup = .95f;
    [Min(.1f)] public float SpinStrike = .5f;
    public float SpinDamage = 25;
    public float SpinKnockback = 22, SpinKnockbackUp = 2;

    [Header("Attack 2 - Slash (line)")]
    [Min(1)] public float SlashLength = 14;
    [Min(.5f)] public float SlashHalfWidth = 2.5f;
    [Min(.1f)] public float SlashWindup = 1.2f;
    [Tooltip("直劈只打中腳底高度低於此值（相對 Boss）的玩家。")]
    public float SlashHitHeight = 3;
    [Min(.05f)] public float SlashStrike = .25f;
    public float SlashDamage = 30;
    public float SlashKnockback = 18, SlashKnockbackUp = 1.5f;
    [Tooltip("直劈前搖時上半身後仰的角度，與揮出時前傾的角度。")]
    public float SlashLeanBack = 25, SlashLeanForward = 30;

    [Header("Ranged 1 - Stomp Rain")]
    [Min(1)] public int StompCount = 7;
    [Min(.1f)] public float StompWindup = .9f;
    public float StompDropHeight = 14;
    public float StompMinDistance = 5, StompMaxDistance = 9, StompArc = 50;
    [Min(1)] public float StompLaunchSpeed = 36;
    [Tooltip("踩地後蹲著，射出時圓形揮舞戰斧的時間。")]
    [Min(.1f)] public float StompSwing = .5f;
    [Tooltip("金屬下落到此高度時（仍在空中）就被全數往外射出。")]
    public float StompLaunchHeight = 1.6f;

    [Header("Ranged 2 - Jump Slam")]
    [Min(.1f)] public float SlamWindup = .9f;
    public float JumpHeight = 10;
    [Min(1)] public float SlamRadius = 8;
    public float SlamShockRadius = 5, SlamShockDamage = 15, SlamShockKnockback = 20;
    [Min(1)] public int EruptCount = 10;
    public float EruptStartRadius = 3, EruptMinRange = 7.5f, EruptMaxRange = 10;
    public float EruptMinApex = 3.2f, EruptMaxApex = 4.8f;
    [Min(1)] public int RainCount = 18;
    public float RainHeight = 16, RainRadius = 24;

    public bool IsPreparing { get; private set; } = true;
    public string CurrentAttack { get; private set; } = "";

    private Enemy enemy;
    private int lastAttack = -1, lastRanged = -1;
    private float nextRanged;
    private bool axeIgnoresPlayer;
    private static readonly Quaternion AxeRest = Quaternion.Euler(35, 0, 0);
    // 圓形揮舞：斧頭指向右側（yaw 90）、Roll -90 使斧刃（本地 -Y）朝向前進方向；
    // Spinner 以負 yaw 逆時針（從上往下看）旋轉，右手側的斧頭由後往前橫掃過身前。
    private static readonly Quaternion WindAxe = Quaternion.Euler(10, 100, -90);
    private static readonly Quaternion SwingAxe = Quaternion.Euler(0, 90, -90);
    private static float SpinYaw(float k) => -360 * (1 - (1 - k) * (1 - k));
    // 斧頭握把位置（Body 座標）；蹲下時 Y 隨 Body 一起縮放。
    private const float GripX = 1.9f, GripY = 3.6f, GripZ = .9f, CrouchAmount = .28f;
    // 斧頭從握把到矛尖的長度（BossModelBuilder 的斧頭網格：矛尖在 z = 6.25）。
    private const float AxeTipLength = 6.25f;
    /// <summary>旋轉斬時斧頭尖端離 Boss 中心軸的水平距離：斧指向側面，握把在 (GripX, GripZ)。</summary>
    private static float SpinReach => Mathf.Sqrt((GripX + AxeTipLength) * (GripX + AxeTipLength) + GripZ * GripZ);

    private void Awake()
    {
        enemy = GetComponent<Enemy>();
    }

    private void OnEnable()
    {
        enemy.Died += OnDeath;
    }

    private void OnDisable()
    {
        enemy.Died -= OnDeath;
    }

    private void Start()
    {
        ShowIndicators(false, false);
        if (SlamIndicator)
        {
            SlamIndicator.SetActive(false);
        }

        SetPose(0, AxeRest, 0);
        StartCoroutine(Brain());
    }

    private void LateUpdate()
    {
        if (AxeRoot && AxePivot && !axeDetached)
        {
            AxeRoot.SetPositionAndRotation(AxePivot.position, AxePivot.rotation);
        }
    }

    private void OnDeath(Enemy dead)
    {
        StopAllCoroutines();
        CleanupTemporaries();
        ShowIndicators(false, false);
        if (SlamIndicator)
        {
            SlamIndicator.SetActive(false);
        }

        var p = transform.position;
        transform.position = new Vector3(p.x, 0, p.z);
        if (AxeRoot)
        {
            Destroy(AxeRoot.gameObject, enemy.DestroyDelay);
        }

        enabled = false;
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

        if (Player && !axeIgnoresPlayer && AxeRoot)
        {
            // 斧頭保留可被選取的碰撞器，但不與玩家發生實體碰撞（玩家被拉到斧頭上不會被彈開）。
            axeIgnoresPlayer = true;
            foreach (var a in AxeRoot.GetComponentsInChildren<Collider>())
            {
                foreach (var b in Player.GetComponentsInChildren<Collider>())
                {
                    Physics.IgnoreCollision(a, b, true);
                }
            }
        }

        return Player && !Player.IsDead;
    }

    private Vector3 PlayerPoint()
    {
        var c = Player.GetComponent<Collider>();
        return c ? c.bounds.center : Player.transform.position;
    }

    private Vector3 ToPlayerFlat()
    {
        var d = Player.transform.position - transform.position;
        d.y = 0;
        return d;
    }

    private void Face(Vector3 flatDirection, float degreesPerSecond)
    {
        if (flatDirection.sqrMagnitude > .001f)
        {
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                Quaternion.LookRotation(flatDirection),
                degreesPerSecond * Time.deltaTime);
        }
    }

    /// <param name="lean">上半身傾角（度）：正值前傾，負值後仰（以腳底為軸）。</param>
    private void SetPose(float crouch, Quaternion axeRotation, float spinYaw, float lift = 0, float lean = 0)
    {
        float scaleY = 1 - CrouchAmount * crouch;
        Quaternion leanRotation = Quaternion.Euler(lean, 0, 0);
        if (Body)
        {
            Body.localScale = new Vector3(1, scaleY, 1);
            Body.localRotation = leanRotation;
        }

        if (Spinner)
        {
            Spinner.localRotation = Quaternion.Euler(0, spinYaw, 0);
            Spinner.localPosition = new Vector3(0, lift, 0);
        }

        if (AxePivot)
        {
            // 握把跟著上半身一起傾斜。
            AxePivot.localPosition = leanRotation * new Vector3(GripX, GripY * scaleY, GripZ);
            AxePivot.localRotation = axeRotation;
        }
    }

    private void ShowIndicators(bool spin, bool slash)
    {
        // 近戰不顯示紅色預警範圍（ShowMeleeTelegraph 預設關閉）。
        spin &= ShowMeleeTelegraph;
        slash &= ShowMeleeTelegraph;
        if (SpinIndicator)
        {
            SpinIndicator.SetActive(spin);
        }

        if (SlashIndicator)
        {
            SlashIndicator.SetActive(slash);
        }
    }

    // ------------------------------------------------------------------ brain

    private IEnumerator Brain()
    {
        IsPreparing = true;
        yield return new WaitForSeconds(PrepareTime);
        IsPreparing = false;
        nextRanged = Time.time;
        while (!enemy.IsDead)
        {
            if (!FindPlayer())
            {
                yield return null;
                continue;
            }

            // 血量降到一半以下：進入二階段（只演出一次）。
            if (!IsPhase2 && enemy.CurrentHealth <= enemy.MaxHealth * Phase2Threshold)
            {
                yield return StartCoroutine(PhaseTransition());
                continue;
            }

            Vector3 toPlayer = ToPlayerFlat();
            float distance = toPlayer.magnitude;
            Face(toPlayer, TurnSpeed);
            if (distance > TriggerRange)
            {
                // 後躍拉開距離之後，不論實際距離都直接選遠程技能。
                int skill = Time.time >= nextRanged
                    ? PickFarSkill(forceFarSkill ? Mathf.Max(distance, FarRange + .1f) : distance)
                    : -1;
                if (skill >= 0)
                {
                    lastRanged = skill;
                    forceFarSkill = false;
                    meleeRounds = 0;
                    yield return StartCoroutine(RunFarSkill(skill));
                    CurrentAttack = "";
                    nextRanged = Time.time + RangedCooldown * CooldownScale;
                    // 飛撲拿回斧頭後立刻接下一個攻擊，不進入冷卻。
                    if (skipCooldown)
                    {
                        skipCooldown = false;
                        nextRanged = Time.time;
                    }
                    else
                    {
                        yield return new WaitForSeconds(Cooldown * CooldownScale);
                    }

                    continue;
                }

                var next = transform.position + toPlayer.normalized * MoveSpeed * Time.deltaTime;
                var flat = new Vector3(next.x, 0, next.z);
                if (flat.magnitude > ArenaRadius)
                {
                    next = flat.normalized * ArenaRadius;
                }

                next.y = transform.position.y;
                transform.position = next;
                yield return null;
                continue;
            }

            // 近戰：二階段連續使用兩招（第二招前只有短暫停頓）。
            int combo = IsPhase2 ? Mathf.Max(1, Phase2MeleeCombo) : 1;
            if (meleeRounds == 0)
            {
                meleeStreakHealth = enemy.CurrentHealth;
            }

            for (int i = 0; i < combo && FindPlayer(); i++)
            {
                if (i > 0)
                {
                    float d2 = ToPlayerFlat().magnitude;
                    if (d2 > TriggerRange * 1.4f)
                    {
                        break;
                    }

                    Face(ToPlayerFlat(), TurnSpeed * 3);
                }

                // 近距離偏好旋轉斬，較遠偏好直劈；避免連續兩次相同招式。
                float dist = ToPlayerFlat().magnitude;
                int attack = Random.value < (dist < SpinRadius * .8f ? .65f : .3f) ? 0 : 1;
                if (attack == lastAttack && Random.value < .6f)
                {
                    attack = 1 - attack;
                }

                lastAttack = attack;
                yield return StartCoroutine(attack == 0 ? SpinAttack() : SlashAttack());
                CurrentAttack = "";
                if (i < combo - 1)
                {
                    yield return new WaitForSeconds(.3f);
                }
            }

            // 玩家一直貼身近戰：後躍拉開距離，接著用遠程技能。
            meleeRounds++;
            if (ShouldDisengage())
            {
                yield return new WaitForSeconds(.15f);
                yield return StartCoroutine(Disengage());
                continue;
            }

            yield return new WaitForSeconds(Cooldown * CooldownScale);
        }
    }

    // ------------------------------------------------------------------ melee

    /// <param name="windupOverride">大於 0 時取代預設前搖時間（鋼拉鉤連段用）。</param>
    private IEnumerator SpinAttack(float windupOverride = -1, int rotations = 1)
    {
        rotations = Mathf.Max(1, rotations);
        CurrentAttack = "Spin";
        ShowIndicators(true, false);
        Quaternion windAxe = WindAxe;
        float windup = windupOverride > 0 ? windupOverride : SpinWindup;
        for (float t = 0; t < windup; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0, 1, t / windup);
            SetPose(k, Quaternion.Slerp(AxeRest, windAxe, k), 0);
            yield return null;
        }

        // 多圈時整體時間拉長（每圈約 0.75 倍的單圈時間），每一圈各判定一次。
        var hitTurns = new bool[rotations];
        float strike = SpinStrike * (rotations > 1 ? rotations * .75f : 1f);
        Quaternion swingAxe = SwingAxe;
        for (float t = 0; t < strike; t += Time.deltaTime)
        {
            float k = t / strike;
            float yaw = SpinYaw(k) * rotations;
            SetPose(1 - k * .5f, swingAxe, yaw);
            int turn = Mathf.Clamp((int)(Mathf.Abs(yaw) / 360f), 0, rotations - 1);
            // 命中判定持續整段揮擊（不是單一瞬間），範圍內的玩家（包含跳起來的）都會被打中。
            if (!hitTurns[turn] && k >= .05f && FindPlayer())
            {
                var d = PlayerPoint() - transform.position;
                d.y = 0;
                // 橫向範圍 = 斧頭實際能掃到的距離（握把位置 + 斧長）；縱向仍用 HitHeight。
                if (d.magnitude <= SpinReach + PlayerHitMargin && PlayerPoint().y - transform.position.y < HitHeight)
                {
                    hitTurns[turn] = true;
                    BossHit.DamageAndKnockback(Player, SpinDamage, d, SpinKnockback, SpinKnockbackUp);
                }
            }

            yield return null;
        }

        ShowIndicators(false, false);
        yield return Recover(.5f, 1 - .5f, swingAxe);
    }

    private bool TrySlashHit()
    {
        if (!FindPlayer())
        {
            return false;
        }

        Vector3 local = transform.InverseTransformPoint(PlayerPoint());
        if (local.z > -1 && local.z <= SlashLength && Mathf.Abs(local.x) <= SlashHalfWidth
            && PlayerPoint().y - transform.position.y < SlashHitHeight)
        {
            // 往玩家所在的那一側擊退（正好在中線時往右）。
            Vector3 side = transform.right * (local.x < 0 ? -1 : 1);
            BossHit.DamageAndKnockback(Player, SlashDamage, side, SlashKnockback, SlashKnockbackUp);
            return true;
        }

        return false;
    }

    private IEnumerator SlashAttack()
    {
        CurrentAttack = "Slash";
        ShowIndicators(false, true);
        Quaternion raised = Quaternion.Euler(-125, 0, 0);
        for (float t = 0; t < SlashWindup; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0, 1, t / SlashWindup);
            // 前搖最後 0.3 秒鎖定方向，讓玩家有機會閃避。
            if (t < SlashWindup - .3f && FindPlayer())
            {
                Face(ToPlayerFlat(), TurnSpeed * 1.5f);
            }

            // 後仰蓄力：斧頭舉到後上方，上半身同時向後仰。
            SetPose(0, Quaternion.Euler(Mathf.Lerp(35f, -125f, k), 0, 0), 0, 0, Mathf.Lerp(0, -SlashLeanBack, k));
            yield return null;
        }

        bool hit = false;
        Quaternion down = Quaternion.Euler(60, 0, 0);
        for (float t = 0; t < SlashStrike; t += Time.deltaTime)
        {
            float k = t / SlashStrike;
            // 以角度插值：-125° → 60° 相差 185°，用 Slerp 會走反方向（由下往上轉）。
            // 揮出：上半身由後仰迅速前傾。
            SetPose(0, Quaternion.Euler(Mathf.Lerp(-125f, 60f, k * k), 0, 0), 0, 0, Mathf.Lerp(-SlashLeanBack, SlashLeanForward, k * k));
            // 直劈的判定：揮到一半時單次判定（範圍內才會被打中）。
            if (!hit && k >= .5f)
            {
                hit = true;
                TrySlashHit();
            }

            yield return null;
        }

        yield return new WaitForSeconds(.2f);
        ShowIndicators(false, false);
        yield return Recover(.5f, 0, down, SlashLeanForward);
    }

    private IEnumerator Recover(float duration, float fromCrouch, Quaternion fromAxe, float fromLean = 0)
    {
        for (float t = 0; t < duration; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0, 1, t / duration);
            SetPose(Mathf.Lerp(fromCrouch, 0, k), Quaternion.Slerp(fromAxe, AxeRest, k), 0, 0, Mathf.Lerp(fromLean, 0, k));
            yield return null;
        }

        SetPose(0, AxeRest, 0);
    }

    // ------------------------------------------------------------------ ranged helpers

    private BossProjectile Spawn(bool metal, Vector3 position, float lifetime)
    {
        var prefab = metal ? MetalPrefab : StonePrefab;
        if (!prefab)
        {
            return null;
        }

        var shot = Instantiate(prefab, position, Random.rotation);
        shot.Lifetime = lifetime;
        // 揮舞中的斧頭不應擋下或擊落投射物。
        if (AxeRoot)
        {
            foreach (var a in AxeRoot.GetComponentsInChildren<Collider>())
            {
                foreach (var b in shot.GetComponentsInChildren<Collider>())
                {
                    Physics.IgnoreCollision(a, b, true);
                }
            }
        }

        // 石頭大小略有變化，看起來不單調。
        if (!metal)
        {
            shot.transform.localScale *= Random.Range(.85f, 1.35f);
        }

        return shot;
    }

    private void Marker(Vector3 position, float radius, float life)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        g.name = "Landing Marker";
        Destroy(g.GetComponent<Collider>());
        g.transform.position = new Vector3(position.x, .05f, position.z);
        g.transform.localScale = new Vector3(radius * 2, .005f, radius * 2);
        if (MarkerMaterial)
        {
            g.GetComponent<Renderer>().sharedMaterial = MarkerMaterial;
        }

        Destroy(g, life);
    }

    /// <summary>從高度 height 以初速 downSpeed 向下拋出，落地所需時間。</summary>
    private static float FallTime(float height, float downSpeed)
    {
        float g = Mathf.Abs(Physics.gravity.y);
        return (-downSpeed + Mathf.Sqrt(downSpeed * downSpeed + 2 * g * Mathf.Max(0, height))) / g;
    }

    private Vector3 ClampToArena(Vector3 p, float radius)
    {
        var flat = new Vector3(p.x, 0, p.z);
        if (flat.magnitude > radius)
        {
            flat = flat.normalized * radius;
        }

        return new Vector3(flat.x, 0, flat.z);
    }

    // ------------------------------------------------------------------ ranged 1: stomp rain

    private IEnumerator StompRain()
    {
        CurrentAttack = "Stomp";
        Quaternion raised = Quaternion.Euler(-30, 0, 0);
        for (float t = 0; t < StompWindup; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0, 1, t / StompWindup);
            if (FindPlayer())
            {
                Face(ToPlayerFlat(), TurnSpeed * 1.5f);
            }

            SetPose(0, Quaternion.Slerp(AxeRest, raised, k), 0, 1.2f * k);
            yield return null;
        }

        // 猛踩地面。
        for (float t = 0; t < .12f; t += Time.deltaTime)
        {
            float k = t / .12f;
            SetPose(k, raised, 0, 1.2f * (1 - k));
            yield return null;
        }

        SetPose(1, raised, 0, 0);
        ScreenShake.Play(.3f, .5f);

        var drops = new List<BossProjectile>();
        // 只落到發射高度（仍在空中）就射出，不等落地。
        float fall = FallTime(StompDropHeight - StompLaunchHeight, 6);
        Vector3 origin = transform.position;
        // 二階段（狂暴）金屬數量加倍、扇形更寬、射出速度更快。
        int count = Mathf.RoundToInt(StompCount * (IsPhase2 ? Phase2StompMultiplier : 1f));
        float arc = StompArc * (IsPhase2 ? 1.4f : 1f);
        float launchSpeed = IsPhase2 ? Phase2LaunchSpeed : StompLaunchSpeed;
        for (int i = 0; i < count; i++)
        {
            float u = count == 1 ? .5f : i / (float)(count - 1);
            float angle = Mathf.Lerp(-arc, arc, u) + Random.Range(-4f, 4f);
            float distance = Random.Range(StompMinDistance, StompMaxDistance);
            Vector3 dir = Quaternion.AngleAxis(angle, Vector3.up) * transform.forward;
            Vector3 spot = ClampToArena(origin + dir * distance, ArenaRadius + 3);
            Marker(spot, 1f, fall + .3f);
            // 二階段：有機會掉下普通石頭（不能被推拉，也不會被 Boss 奪回）。
            bool metal = !(IsPhase2 && Random.value < Phase2StoneChance);
            var shot = Spawn(metal, new Vector3(spot.x, StompDropHeight, spot.z), metal ? 12 : 6);
            if (shot)
            {
                shot.Launch(Vector3.down * 6, null, true);
                drops.Add(shot);
            }
        }

        // 保持蹲姿，把斧頭拉到側後方蓄力；金屬降到發射高度時，揮斧正好到「射出」的瞬間。
        float launchAt = StompSwing * .25f;
        float windTime = Mathf.Max(.15f, fall - launchAt);
        for (float t = 0; t < windTime; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0, 1, t / windTime);
            SetPose(1, Quaternion.Slerp(raised, WindAxe, k), 0);
            yield return null;
        }

        // 圓形揮舞戰斧，同時把金屬全數往外射出（方向依各自位置，從 Boss 向外輻射）。
        bool launched = false;
        List<Vector3> directions = null;
        for (float t = 0; t < StompSwing; t += Time.deltaTime)
        {
            float k = t / StompSwing;
            SetPose(1 - k * .3f, SwingAxe, SpinYaw(k));
            if (!launched && t >= launchAt)
            {
                launched = true;
                directions = LaunchOutward(drops, origin, launchSpeed);
            }

            yield return null;
        }

        if (!launched)
        {
            directions = LaunchOutward(drops, origin, launchSpeed);
        }

        // 二階段：射出短暫時間後，Boss 把其中的金屬全部奪回，再朝原方向射出（石頭不受影響）。
        if (IsPhase2)
        {
            var metals = new List<BossProjectile>();
            var metalDirections = new List<Vector3>();
            for (int i = 0; i < drops.Count; i++)
            {
                if (drops[i] && drops[i].Kind == BossProjectileKind.Metal)
                {
                    metals.Add(drops[i]);
                    metalDirections.Add(directions[i]);
                }
            }

            if (metals.Count > 0)
            {
                // 讓金屬先飛出去一小段時間再奪回。
                for (float t = 0; t < StompReclaimWait; t += Time.deltaTime)
                {
                    SetPose(.7f, SwingAxe, 0);
                    yield return null;
                }

                yield return StartCoroutine(ReclaimShots(metals, metalDirections, launchSpeed, .7f, SwingAxe,
                    ReclaimLaunch.ScatterAtPlayer));
            }
        }

        yield return Recover(.6f, .7f, SwingAxe);
    }

    /// <summary>把金屬全數往外射出，並回傳各自的方向（與 shots 一一對應）。</summary>
    private List<Vector3> LaunchOutward(List<BossProjectile> shots, Vector3 origin, float speed)
    {
        var directions = new List<Vector3>();
        foreach (var shot in shots)
        {
            Vector3 outward = Vector3.forward;
            if (shot)
            {
                outward = shot.transform.position - origin;
                outward.y = 0;
                outward = outward.sqrMagnitude > .001f ? outward.normalized : transform.forward;
                // 玩家在落下途中已對它發動推拉的金屬，不再由 Boss 射出。
                if (!shot.IsPlayerControlled)
                {
                    shot.Launch(outward * speed, null, false);
                }
            }

            directions.Add(outward);
        }

        return directions;
    }

    // ------------------------------------------------------------------ ranged 2: jump slam

    private IEnumerator JumpSlam()
    {
        CurrentAttack = "JumpSlam";
        Vector3 start = transform.position;
        Vector3 target = start;
        if (FindPlayer())
        {
            Vector3 toPlayer = ToPlayerFlat();
            target = ClampToArena(start + toPlayer * .55f, ArenaRadius);
        }

        if (SlamIndicator)
        {
            SlamIndicator.transform.position = new Vector3(target.x, .04f, target.z);
            SlamIndicator.transform.localScale = new Vector3(SlamRadius * 2, .005f, SlamRadius * 2);
            SlamIndicator.SetActive(true);
        }

        Quaternion raised = Quaternion.Euler(-60, 0, 0);
        for (float t = 0; t < SlamWindup; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0, 1, t / SlamWindup);
            if (FindPlayer())
            {
                Face(target - transform.position, TurnSpeed * 1.5f);
            }

            SetPose(k, Quaternion.Slerp(AxeRest, raised, k), 0);
            yield return null;
        }

        // 起跳 + 滯空（水平移動到目標上空）。
        const float rise = .6f, hang = .25f, drop = .18f;
        for (float t = 0; t < rise + hang; t += Time.deltaTime)
        {
            float h = t < rise ? Mathf.Sin(t / rise * Mathf.PI * .5f) : 1;
            float xz = Mathf.SmoothStep(0, 1, t / (rise + hang));
            Vector3 p = Vector3.Lerp(start, target, xz);
            transform.position = new Vector3(p.x, JumpHeight * h, p.z);
            SetPose(1 - h, Quaternion.Slerp(raised, AxeRest, h * .5f), 0);
            yield return null;
        }

        // 猛砸。
        for (float t = 0; t < drop; t += Time.deltaTime)
        {
            float k = t / drop;
            transform.position = new Vector3(target.x, JumpHeight * (1 - k * k), target.z);
            yield return null;
        }

        transform.position = target;
        SetPose(.6f, AxeRest, 0);
        if (SlamIndicator)
        {
            SlamIndicator.SetActive(false);
        }

        ScreenShake.Play(.6f, 1.6f);
        if (FindPlayer())
        {
            Vector3 d = PlayerPoint() - target;
            d.y = 0;
            if (d.magnitude <= SlamShockRadius && PlayerPoint().y < HitHeight)
            {
                BossHit.DamageAndKnockback(Player, SlamShockDamage, d, SlamShockKnockback, 2);
            }
        }

        // 地面噴出短距離拋物線的可推金屬。
        float g = Mathf.Abs(Physics.gravity.y);
        for (int i = 0; i < EruptCount; i++)
        {
            float angle = i * 360f / EruptCount + Random.Range(-10f, 10f);
            Vector3 dir = Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward;
            float range = Random.Range(EruptMinRange, EruptMaxRange) - EruptStartRadius;
            float apex = Random.Range(EruptMinApex, EruptMaxApex);
            float vy = Mathf.Sqrt(2 * g * apex);
            float airTime = 2 * vy / g;
            var shot = Spawn(true, target + dir * EruptStartRadius + Vector3.up * .4f, 12);
            if (shot)
            {
                shot.Launch(dir * (range / airTime) + Vector3.up * vy, null, true);
            }
        }

        // 站起，場地搖晃一陣子。
        yield return Recover(.5f, .6f, AxeRest);
        yield return new WaitForSeconds(.5f);

        // 全場隨機落下金屬與非金屬，落點先標示。
        float longest = 0;
        var rainMetals = new List<BossProjectile>();
        for (int i = 0; i < RainCount; i++)
        {
            Vector3 spot;
            if (i < 5 && FindPlayer())
            {
                // 部分落點落在玩家附近，逼玩家移動。
                var offset = Random.insideUnitCircle * 6;
                spot = ClampToArena(Player.transform.position + new Vector3(offset.x, 0, offset.y), RainRadius);
            }
            else
            {
                var c = Random.insideUnitCircle * RainRadius;
                spot = new Vector3(c.x, 0, c.y);
            }

            // 二階段：金屬同時落下（方便 Boss 在它們快落地時一併奪回），石頭仍隨機錯開。
            bool metal = Random.value < .5f;
            bool reclaim = IsPhase2 && metal;
            float delay = reclaim ? 0 : Random.Range(0f, .7f);
            float fall = FallTime(RainHeight, 5);
            longest = Mathf.Max(longest, delay + fall);
            StartCoroutine(RainOne(spot, metal, delay, fall, reclaim ? rainMetals : null));
        }

        if (IsPhase2)
        {
            // 金屬由天花板落下，即將命中地板前：Boss 奪回金屬並朝玩家射出。
            float trigger = Mathf.Max(0, FallTime(RainHeight - RainReclaimHeight, 5) - ReclaimDelay);
            yield return new WaitForSeconds(trigger);
            if (rainMetals.Count > 0)
            {
                yield return StartCoroutine(ReclaimShots(rainMetals, null, Phase2LaunchSpeed, 0, AxeRest, ReclaimLaunch.ScatterAtPlayer));
                yield return Recover(.5f, 0, SwingAxe);
            }

            yield return new WaitForSeconds(.6f);
        }
        else
        {
            yield return new WaitForSeconds(longest + .4f);
        }
    }

    private IEnumerator RainOne(Vector3 spot, bool metal, float delay, float fall, List<BossProjectile> collect)
    {
        Marker(spot, 1.3f, delay + fall + .15f);
        yield return new WaitForSeconds(delay);
        var shot = Spawn(metal, new Vector3(spot.x, RainHeight, spot.z), metal ? 12 : 5);
        if (shot)
        {
            shot.Launch(Vector3.down * 5, null, true);
            collect?.Add(shot);
        }
    }
}
