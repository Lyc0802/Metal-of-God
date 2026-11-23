using System.Collections;
using UnityEngine;

/// <summary>
/// 鋼鐵審判者的「拉開距離」：玩家一直貼身近戰時，Boss 會後躍到遠處，並立刻接遠程技能。
/// </summary>
public partial class SteelInquisitorBoss
{
    [Header("Disengage (back-leap to open distance)")]
    [Tooltip("連續幾輪近戰（期間沒用遠程技能）後，Boss 會後躍拉開距離。二階段少一輪。")]
    [Min(1)] public int DisengageAfterMelees = 2;
    [Tooltip("這段近戰期間 Boss 損失的血量超過最大血量的此比例，也會立刻後躍（懲罰貼身硬打）。")]
    [Range(0, 1)] public float DisengageDamageFraction = .12f;
    [Tooltip("後躍落點範圍的半徑（要小於柱子所在的圈）。")]
    [Min(5)] public float RetreatArenaRadius = 20;
    [Tooltip("落點與玩家至少要有這麼遠才算有效；找不到時取最遠的。")]
    [Min(1)] public float RetreatMinDistance = 19;
    [Tooltip("單次後躍的最大水平距離。")]
    [Min(5)] public float RetreatMaxJump = 28;
    [Min(.1f)] public float RetreatWindup = .45f;
    [Tooltip("後躍的最高點高度。")]
    public float RetreatHeight = 6;

    private int meleeRounds;
    private float meleeStreakHealth;
    private bool forceFarSkill;

    private bool ShouldDisengage()
    {
        if (enemy.IsDead || !FindPlayer())
        {
            return false;
        }

        // 血量剛好要進二階段時先讓戰吼演出，不後躍。
        if (!IsPhase2 && enemy.CurrentHealth <= enemy.MaxHealth * Phase2Threshold)
        {
            return false;
        }

        int rounds = IsPhase2 ? Mathf.Max(1, DisengageAfterMelees - 1) : DisengageAfterMelees;
        bool tooMany = meleeRounds >= rounds;
        bool hurt = meleeStreakHealth - enemy.CurrentHealth >= enemy.MaxHealth * DisengageDamageFraction;
        return tooMany || hurt;
    }

    private IEnumerator Disengage()
    {
        meleeRounds = 0;
        Vector3 start = transform.position;
        Vector3 landing = PickRetreatSpot();
        float jump = new Vector3(landing.x - start.x, 0, landing.z - start.z).magnitude;
        if (jump < 4)
        {
            // 找不到更遠的落點（例如玩家就在場地邊緣的另一側）：照一般流程進入冷卻。
            yield return new WaitForSeconds(Cooldown * CooldownScale);
            yield break;
        }

        CurrentAttack = "Retreat";
        Quaternion raised = Quaternion.Euler(-30, 0, 0);
        for (float t = 0; t < RetreatWindup; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0, 1, t / RetreatWindup);
            if (FindPlayer())
            {
                Face(ToPlayerFlat(), TurnSpeed * 1.5f);
            }

            SetPose(k, Quaternion.Slerp(AxeRest, raised, k), 0);
            yield return null;
        }

        DustPuff.Play(start + Vector3.up * .1f, Vector3.up, 14f);
        // 面向玩家倒退著跳走，上半身後仰。
        float flight = Mathf.Clamp(jump / 24f, .6f, 1.2f);
        for (float t = 0; t < flight; t += Time.deltaTime)
        {
            float u = t / flight;
            float arc = 4f * RetreatHeight * u * (1 - u);
            Vector3 p = Vector3.Lerp(start, landing, Mathf.SmoothStep(0, 1, u));
            transform.position = new Vector3(p.x, arc, p.z);
            if (FindPlayer())
            {
                Face(ToPlayerFlat(), TurnSpeed * 2f);
            }

            float crouch = Mathf.Clamp01(1 - u * 3f);
            SetPose(crouch, Quaternion.Slerp(raised, AxeRest, u), 0, 0, Mathf.Sin(u * Mathf.PI) * -14f);
            yield return null;
        }

        transform.position = landing;
        DustPuff.Play(landing + Vector3.up * .1f, Vector3.up, 16f);
        ScreenShake.Play(.25f, .4f);
        SetPose(.7f, AxeRest, 0);
        yield return Recover(.3f, .7f, AxeRest);

        // 落地後立刻接遠程技能（不論實際距離），不吃冷卻。
        forceFarSkill = true;
        nextRanged = Time.time;
        CurrentAttack = "";
    }

    /// <summary>在場內挑一個離玩家夠遠、沒有擋住的落點；挑不到時回傳目前位置。</summary>
    private Vector3 PickRetreatSpot()
    {
        Vector3 here = transform.position;
        here.y = 0;
        Vector3 playerPos = Player.transform.position;
        playerPos.y = 0;
        Vector3 best = here;
        float bestScore = float.NegativeInfinity;
        const int Angles = 24;
        float[] radii = { RetreatArenaRadius, RetreatArenaRadius * .72f, RetreatArenaRadius * .45f };
        foreach (float r in radii)
        {
            for (int i = 0; i < Angles; i++)
            {
                float a = (i + Random.value * .5f) * Mathf.PI * 2f / Angles;
                var spot = new Vector3(Mathf.Sin(a) * r, 0, Mathf.Cos(a) * r);
                float jump = (spot - here).magnitude;
                if (jump < 6f || jump > RetreatMaxJump)
                {
                    continue;
                }

                float away = (spot - playerPos).magnitude;
                // 離玩家越遠越好（超過需求距離後不再加分）；偏好短一點的跳躍，並加一點隨機。
                float score = Mathf.Min(away, RetreatMinDistance + 6f) - jump * .25f + Random.value * 2f;
                if (score <= bestScore || !LandingFree(spot))
                {
                    continue;
                }

                bestScore = score;
                best = spot;
            }
        }

        return best;
    }

    private bool LandingFree(Vector3 spot)
    {
        var hits = Physics.OverlapSphere(spot + Vector3.up * 3.8f, 3.4f, ~0, QueryTriggerInteraction.Ignore);
        foreach (var c in hits)
        {
            if (!c || c.transform.IsChildOf(transform) || (AxeRoot && c.transform.IsChildOf(AxeRoot))
                || c.GetComponentInParent<PlayerHealth>() || c.GetComponentInParent<BossProjectile>())
            {
                continue;
            }

            return false;
        }

        return true;
    }
}
