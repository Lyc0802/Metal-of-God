using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 鋼鐵審判者的二階段（血量降到門檻以下）：狂暴強化、鋼拉鉤連段、奪回金屬（跳砸後）、投斧飛撲。
/// </summary>
public partial class SteelInquisitorBoss
{
    [Header("Phase 2 (HP below threshold)")]
    [Range(.05f, .95f)] public float Phase2Threshold = .5f;
    [Tooltip("冷卻時間倍率（0.7 = 縮短 30%）。")]
    [Range(.2f, 1)] public float Phase2CooldownScale = .7f;
    [Tooltip("踩地掉落的金屬數量倍率。")]
    [Min(1)] public float Phase2StompMultiplier = 2;
    [Min(1)] public float Phase2LaunchSpeed = 48;
    [Tooltip("二階段踩地時，每顆掉落物變成普通石頭（不可推拉）的機率。")]
    [Range(0, 1)] public float Phase2StoneChance = .35f;
    [Tooltip("近戰連續使用的招數。")]
    [Min(1)] public int Phase2MeleeCombo = 2;
    [Min(.5f)] public float TransitionTime = 1.8f;
    [Tooltip("戰吼衝擊對玩家的傷害（0 = 只擊退；大於 0 會一併中斷玩家專注）與擊退速度。")]
    public float ShoutDamage = 5, ShoutKnockback = 38, ShoutKnockbackUp = 5;

    [Header("Arms")]
    public Transform LeftArmPivot;
    public GameObject LeftHandOpen;
    public GameObject LeftFist;
    public Transform LeftHandTip;
    public Transform RightArmPivot;

    [Header("Steel Hook Combo")]
    public Color HookColor = new Color(1f, .1f, .08f, 1);
    [Min(.1f)] public float HookWindup = .9f;
    [Min(1)] public float HookPullSpeed = 42;
    [Min(1)] public float HookStopDistance = 3.5f;
    [Min(.1f)] public float HookMaxPullTime = .7f;
    [Min(1)] public float HookRange = 30;
    [Tooltip("拉到面前後，旋轉斬的縮短前搖。")]
    [Min(.1f)] public float HookComboWindup = .4f;

    [Header("Reclaim Metals (after stomp / jump slam rain)")]
    [Tooltip("跳砸的金屬雨降到此高度（即將落地）時，Boss 奪回並朝玩家射出。")]
    [Min(.5f)] public float RainReclaimHeight = 4;
    [Tooltip("踩地射出金屬後，等這麼久才開始奪回。")]
    [Min(0)] public float StompReclaimWait = .2f;
    [Min(0)] public float ReclaimDelay = .12f;
    [Tooltip("奪回（把金屬拉回身邊）的速度。")]
    [Min(1)] public float ReclaimPullSpeed = 65;
    [Tooltip("奪回後朝玩家方向散射的圓錐半角（度）。")]
    [Range(0, 60)] public float ReclaimScatterAngle = 25;
    [Min(.2f)] public float ReclaimTimeout = 1.4f;
    [Tooltip("奪回時左臂的角度（-60 約為水平向前，0 為下垂）。")]
    public float ReclaimArmPitch = -55;

    [Header("Axe Throw")]
    [Min(.1f)] public float ThrowWindup = 1f;
    [Min(1)] public float ThrowSpeed = 40;
    public float ThrowDamage = 35, ThrowKnockback = 24;
    [Min(1)] public float ThrowMinDistance = 10, ThrowMaxDistance = 60;
    [Tooltip("斧頭飛行時中心的高度。")]
    public float ThrowHeight = 2.2f;
    [Header("Axe Throw - Boss dives to the axe")]
    [Tooltip("斧頭插住後，Boss 停多久（秒）就開始飛撲。")]
    [Min(0)] public float DiveHoverTime = .1f;
    [Tooltip("飛撲的最高速度（公尺/秒）。")]
    [Min(1)] public float DiveTopSpeed = 50;
    [Tooltip("接近斧頭時減速，使總飛行時間多出這麼多秒（約 0.3 秒再抓）。")]
    [Min(0)] public float DiveExtraFlight = .3f;
    [Tooltip("身體水平飛行時，從腳底到伸直的手尖的距離（肩高 4.25 + 手臂 1.2）。")]
    [Min(1)] public float DiveHandReach = 5.5f;
    [Range(0, 90)] public float DiveLean = 85;
    public float DiveDamage = 30, DiveKnockback = 26;
    [Min(.5f)] public float DiveHitRadius = 2.2f;
    [Tooltip("手尖與握把端的距離小於此值才算抓到斧頭。")]
    [Min(.05f)] public float DiveGrabTolerance = .2f;
    [Tooltip("撿到斧頭後，Boss 站穩的技能冷卻（秒），之後才會進行下一個攻擊。")]
    [Min(0)] public float DivePickupCooldown = .7f;

    public bool IsPhase2 { get; private set; }

    private bool axeDetached, axeStuck, skipCooldown;
    private readonly List<GameObject> temporaries = new List<GameObject>();
    private static Material lineMaterial;
    private float CooldownScale => IsPhase2 ? Phase2CooldownScale : 1f;
    private const float AxeCenterOffset = 2.3f, AxeHeadOffset = 5.2f;

    // ------------------------------------------------------------------ helpers

    private void CleanupTemporaries()
    {
        foreach (var go in temporaries)
        {
            if (go)
            {
                Destroy(go);
            }
        }

        temporaries.Clear();
    }

    private LineRenderer NewLine(Color color, float width)
    {
        if (!lineMaterial)
        {
            var shader = Shader.Find("Sprites/Default");
            if (!shader)
            {
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }

            lineMaterial = new Material(shader);
        }

        var go = new GameObject("Boss Line");
        temporaries.Add(go);
        var line = go.AddComponent<LineRenderer>();
        line.sharedMaterial = lineMaterial;
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.numCapVertices = 2;
        line.alignment = LineAlignment.View;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.widthMultiplier = width;
        line.startColor = color;
        line.endColor = color;
        return line;
    }

    private void RemoveLine(LineRenderer line)
    {
        if (line)
        {
            temporaries.Remove(line.gameObject);
            Destroy(line.gameObject);
        }
    }

    /// <param name="pitch">左臂抬起角度（0 = 自然下垂，約 -60 = 水平向前，約 -125 = 向前上方舉起）。</param>
    private void SetLeftArm(float pitch, bool fist)
    {
        if (LeftArmPivot)
        {
            LeftArmPivot.localRotation = Quaternion.Euler(pitch, 0, 0);
        }

        if (LeftHandOpen)
        {
            LeftHandOpen.SetActive(!fist);
        }

        if (LeftFist)
        {
            LeftFist.SetActive(fist);
        }
    }

    private void SetRightArm(float pitch)
    {
        if (RightArmPivot)
        {
            RightArmPivot.localRotation = Quaternion.Euler(pitch, 0, 0);
        }
    }

    private IEnumerator LowerLeftArm(float duration, float fromPitch)
    {
        for (float t = 0; t < duration; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0, 1, t / duration);
            SetLeftArm(Mathf.Lerp(fromPitch, 0, k), true);
            yield return null;
        }

        SetLeftArm(0, false);
    }

    private int PickFarSkill(float distance)
    {
        var options = new List<int>();
        if (distance > FarRange)
        {
            options.Add(0);
            options.Add(1);
        }

        if (IsPhase2 && distance > TriggerRange && distance < HookRange && LeftArmPivot && LeftHandTip)
        {
            options.Add(2);
        }

        if (IsPhase2 && distance > ThrowMinDistance && AxeRoot && RightArmPivot)
        {
            options.Add(3);
        }

        if (options.Count == 0)
        {
            return -1;
        }

        if (options.Count > 1)
        {
            options.Remove(lastRanged);
        }

        return options[Random.Range(0, options.Count)];
    }

    private IEnumerator RunFarSkill(int skill)
    {
        switch (skill)
        {
            case 0:
                return StompRain();
            case 1:
                return JumpSlam();
            case 2:
                return HookCombo();
            default:
                return AxeThrow();
        }
    }

    // ------------------------------------------------------------------ phase transition

    private IEnumerator PhaseTransition()
    {
        IsPhase2 = true;
        CurrentAttack = "Phase2";
        // 戰吼期間無敵。
        enemy.Invulnerable = true;
        Quaternion raised = Quaternion.Euler(-60, 0, 0);
        bool shook = false;
        for (float t = 0; t < TransitionTime; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0, 1, t / TransitionTime);
            if (FindPlayer())
            {
                Face(ToPlayerFlat(), TurnSpeed);
            }

            SetPose(.4f * k, Quaternion.Slerp(AxeRest, raised, k), 0, 0, Mathf.Lerp(0, -20, k));
            SetLeftArm(Mathf.Lerp(0, -105, k), false);
            if (!shook && k > .6f)
            {
                shook = true;
                ScreenShake.Play(.5f, 1.2f);
                // 戰吼衝擊：把玩家大幅擊退（遠離 Boss）。
                if (FindPlayer())
                {
                    Vector3 away = Player.transform.position - transform.position;
                    BossHit.DamageAndKnockback(Player, ShoutDamage, away, ShoutKnockback, ShoutKnockbackUp);
                }
            }

            yield return null;
        }

        StartCoroutine(LowerLeftArm(.5f, -105));
        yield return Recover(.5f, .4f, raised, -20);
        enemy.Invulnerable = false;
        CurrentAttack = "";
        nextRanged = Time.time;
    }

    // ------------------------------------------------------------------ steel hook combo

    private bool HookClear()
    {
        Vector3 a = LeftHandTip.position, b = PlayerPoint();
        Vector3 d = b - a;
        var hits = Physics.RaycastAll(a, d.normalized, d.magnitude, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance));
        foreach (var h in hits)
        {
            var c = h.collider;
            if (c.transform.IsChildOf(transform) || (AxeRoot && c.transform.IsChildOf(AxeRoot)) || c.GetComponentInParent<BossProjectile>())
            {
                continue;
            }

            return c.GetComponentInParent<PlayerHealth>() == Player;
        }

        return true;
    }

    private void UpdateHookLine(LineRenderer line, float width, float alpha)
    {
        line.SetPosition(0, LeftHandTip.position);
        line.SetPosition(1, PlayerPoint());
        line.widthMultiplier = width;
        Color c = HookColor;
        c.a = alpha;
        line.startColor = c;
        line.endColor = c;
    }

    /// <summary>
    /// 鋼拉鉤連段：左手舉起、紅線連向玩家（前搖）→ 握拳把玩家拉到面前 → 接縮短前搖的旋轉斬。
    /// 紅線被柱子或牆擋住時拉扯失敗，可以躲在掩體後閃避。
    /// </summary>
    private IEnumerator HookCombo()
    {
        CurrentAttack = "Hook";
        if (!FindPlayer() || !LeftArmPivot || !LeftHandTip)
        {
            yield break;
        }

        var line = NewLine(HookColor, .025f);
        SetLeftArm(0, false);
        for (float t = 0; t < HookWindup; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0, 1, t / HookWindup);
            if (FindPlayer())
            {
                Face(ToPlayerFlat(), TurnSpeed * 1.5f);
                UpdateHookLine(line, Mathf.Lerp(.025f, .07f, k), Mathf.Lerp(.35f, .95f, k));
            }

            SetLeftArm(Mathf.Lerp(0, -125, k), false);
            SetPose(0, AxeRest, 0);
            yield return null;
        }

        // 施放：握拳。
        SetLeftArm(-125, true);
        bool linked = FindPlayer() && HookClear();
        if (linked)
        {
            ScreenShake.Play(.15f, .3f);
            var body = Player.GetComponent<Rigidbody>();
            var manipulator = Player.GetComponent<PlayerMetalManipulator>();
            var movement = Player.GetComponent<PlayerMovement>();
            var combat = Player.GetComponent<PlayerCombat>();
            if (manipulator)
            {
                manipulator.Release();
            }

            for (float t = 0; t < HookMaxPullTime && FindPlayer(); t += Time.deltaTime)
            {
                Vector3 to = transform.position - Player.transform.position;
                to.y = 0;
                if (to.magnitude <= HookStopDistance)
                {
                    break;
                }

                if (combat && combat.IsAttacking)
                {
                    combat.CancelAttack();
                }

                Vector3 v = to.normalized * HookPullSpeed;
                v.y = Mathf.Max(body.linearVelocity.y, 0);
                body.linearVelocity = v;
                if (movement)
                {
                    movement.PreserveExternalMomentum();
                }

                Face(ToPlayerFlat(), TurnSpeed * 3);
                UpdateHookLine(line, .16f, 1);
                yield return null;
            }

            if (Player && !Player.IsDead)
            {
                var vel = body.linearVelocity;
                body.linearVelocity = new Vector3(0, vel.y, 0);
            }
        }
        else
        {
            // 被掩體擋住：紅線短暫閃爍後消失，拉扯失敗。
            for (float t = 0; t < .3f; t += Time.deltaTime)
            {
                if (FindPlayer())
                {
                    UpdateHookLine(line, .04f, Mathf.PingPong(t * 12, 1) * .6f);
                }

                yield return null;
            }
        }

        RemoveLine(line);
        StartCoroutine(LowerLeftArm(.45f, -125));
        if (linked && FindPlayer())
        {
            // 鋼拉鉤後的旋轉斬連打兩圈。
            yield return StartCoroutine(SpinAttack(HookComboWindup, 2));
        }
        else
        {
            yield return new WaitForSeconds(.4f);
        }
    }

    // ------------------------------------------------------------------ reclaim metals

    /// <summary>
    /// 金屬還在空中時，Boss 左手水平伸出，以鋼推拉（紅線）把它們全數奪回身邊，再射出（再揮一次斧頭）。
    /// 射出方式見 ReclaimLaunch。射出的金屬在第一次碰到地面時會反彈（不消失）。
    /// 被玩家推拉接管的金屬不受 Boss 控制。
    /// </summary>
    private enum ReclaimLaunch
    {
        /// <summary>朝各自原本的方向。</summary>
        Original,
        /// <summary>朝玩家的方向散射（不必水平，可斜下），其中一顆必定直接瞄準玩家。</summary>
        ScatterAtPlayer
    }

    private IEnumerator ReclaimShots(List<BossProjectile> shots, List<Vector3> directions, float speed, float crouch,
        Quaternion axeStart, ReclaimLaunch launch = ReclaimLaunch.Original)
    {
        for (float t = 0; t < ReclaimDelay; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0, 1, t / ReclaimDelay);
            SetLeftArm(Mathf.Lerp(0, ReclaimArmPitch, k), false);
            SetPose(crouch, Quaternion.Slerp(axeStart, WindAxe, k), 0);
            yield return null;
        }

        SetLeftArm(ReclaimArmPitch, true);
        Vector3 center = transform.position;
        var bossColliders = GetComponentsInChildren<Collider>();
        var lines = new List<LineRenderer>();
        var arrived = new bool[shots.Count];
        var released = new bool[shots.Count];
        for (int i = 0; i < shots.Count; i++)
        {
            var shot = shots[i];
            LineRenderer line = null;
            if (shot && shot.IsPlayerControlled)
            {
                // 在奪回開始前就被玩家接管的金屬：不控制。
                released[i] = true;
            }
            else if (shot)
            {
                // 回程中不與 Boss 本體碰撞，也不受重力（保持原高度水平飛回）。
                shot.GetComponent<Rigidbody>().useGravity = false;
                shot.SuppressGroundVanish(ReclaimTimeout + 1f);
                foreach (var a in shot.GetComponentsInChildren<Collider>())
                {
                    foreach (var b in bossColliders)
                    {
                        Physics.IgnoreCollision(a, b, true);
                    }
                }

                line = NewLine(new Color(HookColor.r, HookColor.g, HookColor.b, .6f), .03f);
            }

            lines.Add(line);
        }

        // 被奪回的金屬彼此不碰撞（避免全部聚到身邊時互撞）；玩家接管其中一顆後，才恢復那一顆的碰撞。
        for (int i = 0; i < shots.Count; i++)
        {
            for (int j = i + 1; j < shots.Count; j++)
            {
                if (shots[i] && shots[j] && !released[i] && !released[j])
                {
                    SetPairCollision(shots[i], shots[j], true);
                }
            }
        }

        StartCoroutine(WatchShotCollisions(shots, (bool[])released.Clone()));

        for (float elapsed = 0; elapsed < ReclaimTimeout; elapsed += Time.deltaTime)
        {
            bool pending = false;
            for (int i = 0; i < shots.Count; i++)
            {
                var shot = shots[i];
                if (!shot || released[i])
                {
                    if (lines[i])
                    {
                        lines[i].enabled = false;
                    }

                    continue;
                }

                if (shot.IsPlayerControlled)
                {
                    // 玩家對它發動了推拉：從此不再受 Boss 控制（恢復重力與碰撞，交給玩家）。
                    released[i] = true;
                    lines[i].enabled = false;
                    shot.GetComponent<Rigidbody>().useGravity = true;
                    foreach (var a in shot.GetComponentsInChildren<Collider>())
                    {
                        foreach (var b in bossColliders)
                        {
                            Physics.IgnoreCollision(a, b, false);
                        }
                    }

                    continue;
                }

                var rb = shot.GetComponent<Rigidbody>();
                // 保持原高度，水平飛回。
                Vector3 to = center - shot.transform.position;
                to.y = 0;
                if (to.magnitude <= 2.6f)
                {
                    arrived[i] = true;
                    rb.linearVelocity = Vector3.zero;
                }
                else
                {
                    pending = true;
                    // 拉回用 ReclaimPullSpeed（與重新射出的 speed 分開）。
                    rb.linearVelocity = to.normalized * ReclaimPullSpeed;
                }

                lines[i].SetPosition(0, LeftHandTip ? LeftHandTip.position : center);
                lines[i].SetPosition(1, shot.transform.position);
            }

            if (!pending)
            {
                break;
            }

            yield return null;
        }

        foreach (var line in lines)
        {
            RemoveLine(line);
        }

        // 朝原方向再次射出（再揮一次斧頭）。
        SetLeftArm(ReclaimArmPitch, false);
        StartCoroutine(LowerLeftArm(.35f, ReclaimArmPitch));
        float launchAt = StompSwing * .25f;
        bool relaunched = false;
        for (float t = 0; t < StompSwing; t += Time.deltaTime)
        {
            float k = t / StompSwing;
            SetPose(crouch, SwingAxe, SpinYaw(k));
            if (!relaunched && t >= launchAt)
            {
                relaunched = true;
                Relaunch(shots, directions, arrived, released, center, bossColliders, speed, launch);
            }

            yield return null;
        }

        if (!relaunched)
        {
            Relaunch(shots, directions, arrived, released, center, bossColliders, speed, launch);
        }
    }

    private static void SetPairCollision(BossProjectile a, BossProjectile b, bool ignore)
    {
        foreach (var ca in a.GetComponentsInChildren<Collider>())
        {
            foreach (var cb in b.GetComponentsInChildren<Collider>())
            {
                Physics.IgnoreCollision(ca, cb, ignore);
            }
        }
    }

    /// <summary>監看被奪回的金屬：玩家一旦接管其中一顆，就恢復那一顆與其他金屬的碰撞。</summary>
    private IEnumerator WatchShotCollisions(List<BossProjectile> shots, bool[] restored)
    {
        for (float t = 0; t < 20f; t += Time.deltaTime)
        {
            bool any = false;
            for (int i = 0; i < shots.Count; i++)
            {
                if (restored[i])
                {
                    continue;
                }

                if (!shots[i])
                {
                    restored[i] = true;
                    continue;
                }

                if (shots[i].IsPlayerControlled)
                {
                    for (int j = 0; j < shots.Count; j++)
                    {
                        if (j != i && shots[j])
                        {
                            SetPairCollision(shots[i], shots[j], false);
                        }
                    }

                    restored[i] = true;
                    continue;
                }

                any = true;
            }

            if (!any)
            {
                yield break;
            }

            yield return null;
        }
    }

    private void Relaunch(List<BossProjectile> shots, List<Vector3> directions, bool[] arrived, bool[] released,
        Vector3 center, Collider[] bossColliders, float speed, ReclaimLaunch launch)
    {
        bool hasPlayer = FindPlayer();
        Vector3 toPlayer = hasPlayer ? ToPlayerFlat() : transform.forward;
        toPlayer.y = 0;
        toPlayer = toPlayer.sqrMagnitude > .01f ? toPlayer.normalized : transform.forward;
        // 「必定瞄準玩家」的那一顆（隨機挑選）。
        int aimIndex = -1;
        if (launch == ReclaimLaunch.ScatterAtPlayer)
        {
            var valid = new List<int>();
            for (int i = 0; i < shots.Count; i++)
            {
                if (shots[i] && !released[i] && !shots[i].IsPlayerControlled)
                {
                    valid.Add(i);
                }
            }

            if (valid.Count > 0)
            {
                aimIndex = valid[Random.Range(0, valid.Count)];
            }
        }

        for (int i = 0; i < shots.Count; i++)
        {
            var shot = shots[i];
            if (!shot)
            {
                continue;
            }

            foreach (var a in shot.GetComponentsInChildren<Collider>())
            {
                foreach (var b in bossColliders)
                {
                    Physics.IgnoreCollision(a, b, false);
                }
            }

            if (released[i] || shot.IsPlayerControlled)
            {
                continue;
            }

            Vector3 direction = directions != null ? directions[i] : Vector3.forward;
            if (launch != ReclaimLaunch.Original && hasPlayer)
            {
                // 朝玩家胸口的方向散射（不必水平）：圓錐內隨機偏移，其中一顆直接瞄準。
                Vector3 start = center + toPlayer * 2.8f;
                start.y = shot.transform.position.y;
                shot.transform.position = start;
                Vector3 aim = (PlayerPoint() - start).normalized;
                if (i == aimIndex)
                {
                    direction = aim;
                }
                else
                {
                    float spread = ReclaimScatterAngle;
                    direction = Quaternion.LookRotation(aim)
                        * Quaternion.Euler(Random.Range(-spread, spread), Random.Range(-spread, spread), 0) * Vector3.forward;
                }
            }
            else if (arrived[i])
            {
                // 從 Boss 身邊（身體外側）、原本的高度朝原方向射出。
                Vector3 p = center + direction * 2.8f;
                shot.transform.position = new Vector3(p.x, shot.transform.position.y, p.z);
            }

            shot.Launch(direction * speed, null, false);
            // 剛射出的瞬間（還在 Boss 身邊）不判定；第一次碰到地面時反彈而不是消失。
            shot.SuppressGroundVanish(.15f);
            shot.AllowGroundBounces(1);
        }
    }

    // ------------------------------------------------------------------ axe throw

    /// <summary>
    /// 投斧：斧頭直線飛向玩家，擊中牆或柱子後插在那裡。接著 Boss 伸直手臂、整個人直線朝斧頭飛去
    /// （飛行中身體有碰撞傷害），碰到斧頭的瞬間站穩、拔回斧頭並立刻接下一個攻擊。
    /// 斧頭插住期間仍是可拉的金屬，玩家可以把自己拉過去。
    /// </summary>
    private IEnumerator AxeThrow()
    {
        CurrentAttack = "Throw";
        if (!AxeRoot || !FindPlayer())
        {
            yield break;
        }

        for (float t = 0; t < ThrowWindup; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0, 1, t / ThrowWindup);
            if (FindPlayer())
            {
                Face(ToPlayerFlat(), TurnSpeed * 1.5f);
            }

            SetPose(0, Quaternion.Euler(Mathf.Lerp(35f, -125f, k), 0, 0), 0, 0, Mathf.Lerp(0, -SlashLeanBack, k));
            yield return null;
        }

        Vector3 dir = PlayerPoint() - AxeRoot.position;
        dir.y = 0;
        dir = dir.sqrMagnitude > .01f ? dir.normalized : transform.forward;
        axeStuck = false;
        bool released = false;
        const float swing = .22f;
        for (float t = 0; t < swing; t += Time.deltaTime)
        {
            float k = t / swing;
            SetPose(0, Quaternion.Euler(Mathf.Lerp(-125f, 40f, k * k), 0, 0), 0, 0,
                Mathf.Lerp(-SlashLeanBack, SlashLeanForward, k * k));
            if (!released && k >= .6f)
            {
                released = true;
                StartCoroutine(AxeFlight(dir));
            }

            yield return null;
        }

        if (!released)
        {
            StartCoroutine(AxeFlight(dir));
        }

        // 擲出後立刻站直（否則會一直前傾，看起來像要跌倒），等斧頭插住。
        for (float t = 0; t < .25f; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0, 1, t / .25f);
            SetPose(0, Quaternion.Euler(40f, 0, 0), 0, 0, Mathf.Lerp(SlashLeanForward, 0, k));
            yield return null;
        }

        SetPose(0, AxeRest, 0);
        while (!axeStuck)
        {
            yield return null;
        }

        yield return StartCoroutine(DiveToAxe());
        // 撿到斧頭後固定冷卻 0.7 秒（取代一般冷卻）。
        yield return new WaitForSeconds(DivePickupCooldown);
        skipCooldown = true;
    }

    private bool IsObstacle(Collider c)
    {
        return !c.transform.IsChildOf(transform) && !(AxeRoot && c.transform.IsChildOf(AxeRoot))
            && !c.GetComponentInParent<BossProjectile>() && !c.GetComponentInParent<PlayerHealth>()
            && !c.GetComponentInParent<Enemy>();
    }

    private IEnumerator AxeFlight(Vector3 dir)
    {
        axeDetached = true;
        axeStuck = false;
        var axe = AxeRoot;
        // 從 Boss 身前出發（舉斧時斧頭在身後，直接從那裡飛會穿過身體）。
        Vector3 center = AxePivot.position + dir * 2.5f;
        center.y = ThrowHeight;
        Vector3 right = Vector3.Cross(Vector3.up, dir).normalized;
        Quaternion facing = Quaternion.LookRotation(dir);
        float angle = 0, traveled = 0;
        bool hitPlayer = false;
        Vector3 contact = center;
        bool stuck = false;
        while (traveled < ThrowMaxDistance && !stuck)
        {
            float step = ThrowSpeed * Time.deltaTime;
            // 牆 / 柱子：以斧頭中心的球體掃描。
            var hits = Physics.SphereCastAll(center, 1.2f, dir, step + .1f, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance));
            foreach (var h in hits)
            {
                if (IsObstacle(h.collider))
                {
                    stuck = true;
                    contact = h.point;
                    break;
                }
            }

            Vector3 next = center + dir * step;
            if (!stuck)
            {
                var flat = new Vector3(next.x, 0, next.z);
                if (flat.magnitude >= ArenaRadius + 3.5f)
                {
                    stuck = true;
                    flat = flat.normalized * (ArenaRadius + 3.8f);
                    contact = new Vector3(flat.x, next.y, flat.z);
                }
            }

            if (stuck)
            {
                break;
            }

            center = next;
            traveled += step;
            angle += 900 * Time.deltaTime;
            Quaternion rot = Quaternion.AngleAxis(angle, right) * facing;
            axe.SetPositionAndRotation(center - rot * Vector3.forward * AxeCenterOffset, rot);
            if (!hitPlayer && FindPlayer())
            {
                Vector3 p = PlayerPoint();
                var flatOffset = new Vector3(p.x - center.x, 0, p.z - center.z);
                if (flatOffset.magnitude < 2f && Mathf.Abs(p.y - center.y) < 2.8f)
                {
                    hitPlayer = true;
                    BossHit.DamageAndKnockback(Player, ThrowDamage, dir, ThrowKnockback, 3);
                }
            }

            yield return null;
        }

        // 插進牆 / 柱子：劍柄朝飛行方向、斧刃朝下，斧頭尖端剛好在接觸點。
        axe.SetPositionAndRotation(contact - dir * AxeHeadOffset, facing);
        ScreenShake.Play(.35f, .5f);
        CombatFeedbackEvents.PublishMetalImpact(contact, -dir, 12);
        axeStuck = true;
    }

    /// <summary>
    /// Boss 手臂伸直，整個人（身體前傾成水平）沿直線飛向插住的斧頭；碰到斧頭的瞬間站穩並拔回。
    /// 飛行中身體撞到玩家會造成傷害與擊退。
    /// </summary>
    private IEnumerator DiveToAxe()
    {
        Vector3 grip = AxeRoot.position;
        Vector3 start = transform.position;
        Vector3 toAxe = new Vector3(grip.x - start.x, 0, grip.z - start.z);
        Vector3 dir = toAxe.sqrMagnitude > .01f ? toAxe.normalized : transform.forward;
        // 手尖碰到斧頭時，腳底的位置（身體水平時手尖在腳前方 DiveHandReach 處）。
        Vector3 end = new Vector3(grip.x, 0, grip.z) - dir * DiveHandReach;
        // 右手在身體軸線右側約 1.9 公尺：把身體往左偏，讓「手」（而不是軸心）對準握把。
        end -= Vector3.Cross(Vector3.up, dir) * 1.9f;
        float flightHeight = Mathf.Max(0, grip.y - .6f);
        bool hit = false;

        // 斧頭插住後只停一下，立刻面向斧頭、右臂伸直並開始飛撲。
        for (float t = 0; t < DiveHoverTime; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0, 1, t / Mathf.Max(.01f, DiveHoverTime));
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), 1080 * Time.deltaTime);
            SetPose(.3f * k, AxeRest, 0);
            SetRightArm(Mathf.Lerp(0, -36, k));
            yield return null;
        }

        // 飛行：身體前傾成水平，右臂沿飛行方向伸直。
        // 先以最高速度巡航，接近時線性減速到 0，使總飛行時間多出約 DiveExtraFlight 秒。
        float distance = Mathf.Max(.1f, Vector3.Distance(new Vector3(start.x, 0, start.z), end));
        float v0 = Mathf.Max(1, DiveTopSpeed);
        float decelDistance = Mathf.Min(distance * .6f, v0 * Mathf.Max(0, DiveExtraFlight));
        float decelTime = 2 * decelDistance / v0;
        float cruiseDistance = distance - decelDistance;
        float cruiseTime = cruiseDistance / v0;
        float duration = Mathf.Max(.15f, cruiseTime + decelTime);
        for (float t = 0; t < duration; t += Time.deltaTime)
        {
            float travelled = t < cruiseTime
                ? v0 * t
                : cruiseDistance + v0 * ((t - cruiseTime) - (t - cruiseTime) * (t - cruiseTime) / (2 * Mathf.Max(.0001f, decelTime)));
            float k = Mathf.Clamp01(travelled / distance);
            float lean = DiveLean * Mathf.Clamp01(t / .15f);
            transform.rotation = Quaternion.LookRotation(dir);
            Vector3 p = Vector3.Lerp(new Vector3(start.x, 0, start.z), end, k);
            transform.position = new Vector3(p.x, Mathf.Lerp(0, flightHeight, Mathf.Clamp01(t / .15f)), p.z);
            SetPose(.3f * (1 - k), AxeRest, 0, 0, lean);
            SetRightArm(-36 - lean);

            if (!hit && FindPlayer())
            {
                // 身體（腳底到手尖的線段）撞到玩家。
                Vector3 a = transform.position, b = transform.position + dir * DiveHandReach;
                Vector3 pc = PlayerPoint();
                Vector3 ab = b - a;
                float s = Mathf.Clamp01(Vector3.Dot(pc - a, ab) / ab.sqrMagnitude);
                if (Vector3.Distance(pc, a + ab * s) <= DiveHitRadius)
                {
                    hit = true;
                    Vector3 side = Vector3.Cross(Vector3.up, dir);
                    // 沿飛行方向推開，並略往玩家偏向的那一側。
                    Vector3 away = dir + side * Mathf.Sign(Vector3.Dot(pc - a, side)) * .35f;
                    BossHit.DamageAndKnockback(Player, DiveDamage, away, DiveKnockback, 2.5f);
                }
            }

            // 手尖碰到斧頭：立即站穩。
            Vector3 hand = AxePivot.position;
            if (RightArmPivot)
            {
                hand = RightArmPivot.TransformPoint(new Vector3(.45f, -.65f, .9f) * 1.0f);
            }

            // 手要真正碰到握把端才算撿到（不是碰到斧身任何一處）。
            if ((hand - grip).sqrMagnitude <= DiveGrabTolerance * DiveGrabTolerance)
            {
                break;
            }

            yield return null;
        }

        // 站穩：落地、站直，拔回斧頭，立刻接下一個攻擊。
        transform.position = new Vector3(transform.position.x, 0, transform.position.z);
        SetRightArm(0);
        SetPose(0, AxeRest, 0);
        axeDetached = false;
        ScreenShake.Play(.4f, .6f);
        CombatFeedbackEvents.PublishMetalImpact(AxeRoot.position, Vector3.up, 12);
        if (FindPlayer())
        {
            Face(ToPlayerFlat(), 1000);
        }
    }
}
