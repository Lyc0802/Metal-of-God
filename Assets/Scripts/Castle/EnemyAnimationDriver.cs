using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 依敵人的實際狀態驅動模型：移動速度（待機 / 跑）、攻擊或投擲、受傷、死亡。
/// 有骨架動畫的模型走 Animator；缺少的動作（或沒有骨架的模型）由 EnemyProceduralMotion 補上。
/// 不改動 AI；只讀 TestEnemy / EnemyShooter / Enemy 的狀態。
/// </summary>
[RequireComponent(typeof(Enemy))]
public class EnemyAnimationDriver : MonoBehaviour
{
    public Animator Animator;
    public EnemyProceduralMotion Procedural;
    [Tooltip("走路動畫對應的移動速度（公尺 / 秒）；實際速度與此值的比例決定走路動畫的播放速率，避免腳滑。")]
    [Min(.5f)] public float WalkReferenceSpeed = 3f;
    [Tooltip("跑步動畫對應的移動速度（公尺 / 秒）；實際速度與此值的比例決定跑步動畫的播放速率。")]
    [Min(.5f)] public float RunReferenceSpeed = 4.5f;

    private static readonly int SpeedId = Animator.StringToHash("Speed");
    private static readonly int WalkRateId = Animator.StringToHash("WalkRate");
    private static readonly int RunRateId = Animator.StringToHash("RunRate");
    private static readonly int AttackSpeedId = Animator.StringToHash("AttackSpeed");
    private static readonly int AttackId = Animator.StringToHash("Attack");
    private static readonly int HurtId = Animator.StringToHash("Hurt");
    private static readonly int DeadId = Animator.StringToHash("Dead");

    private Enemy enemy;
    private TestEnemy melee;
    private EnemyShooter shooter;
    private readonly HashSet<int> parameters = new HashSet<int>();
    private Vector3 lastPosition;
    private float lastHealth, smoothedSpeed;
    private bool wasAttacking, dead;

    private void Awake()
    {
        enemy = GetComponent<Enemy>();
        melee = GetComponent<TestEnemy>();
        shooter = GetComponent<EnemyShooter>();
        lastPosition = transform.position;
        lastHealth = enemy.CurrentHealth;
        if (Animator && Animator.runtimeAnimatorController)
        {
            foreach (var p in Animator.parameters)
            {
                parameters.Add(p.nameHash);
            }
        }
    }

    // 控制器沒有的參數就略過（不同模型的 Animator 不一定有攻擊 / 受傷 / 死亡）。
    private void SetFloat(int id, float value)
    {
        if (Animator && parameters.Contains(id))
        {
            Animator.SetFloat(id, value);
        }
    }

    private void SetTrigger(int id)
    {
        if (Animator && parameters.Contains(id))
        {
            Animator.SetTrigger(id);
        }
    }

    private void Update()
    {
        if (!Animator && !Procedural)
        {
            return;
        }

        if (enemy.IsDead)
        {
            if (!dead)
            {
                dead = true;
                if (Animator)
                {
                    Animator.speed = 1;
                    if (parameters.Contains(DeadId))
                    {
                        Animator.SetBool(DeadId, true);
                    }
                }

                if (Procedural)
                {
                    Procedural.PlayDeath();
                }
            }

            return;
        }

        // 用實際位移估計速度（擊退、NavMeshAgent.Move 也算進去）。
        Vector3 delta = transform.position - lastPosition;
        delta.y = 0;
        lastPosition = transform.position;
        float speed = delta.magnitude / Mathf.Max(Time.deltaTime, .0001f);
        smoothedSpeed = Mathf.Lerp(smoothedSpeed, speed, Mathf.Clamp01(10f * Time.deltaTime));
        SetFloat(SpeedId, smoothedSpeed);
        SetFloat(RunRateId, Mathf.Clamp(smoothedSpeed / RunReferenceSpeed, .5f, 1.6f));
        SetFloat(WalkRateId, Mathf.Clamp(smoothedSpeed / WalkReferenceSpeed, .3f, 1.8f));
        if (Procedural)
        {
            Procedural.SetSpeed(smoothedSpeed);
        }

        bool attacking = (melee && melee.isActiveAndEnabled && melee.IsAttacking) || (shooter && shooter.IsThrowing);
        if (attacking && !wasAttacking)
        {
            bool meleeAttack = melee && melee.isActiveAndEnabled;
            float duration = meleeAttack ? melee.AttackDuration : shooter.AttackAnimationTime;
            float hit = meleeAttack ? melee.AttackHitTime : shooter.FireFraction;
            // Attack 動畫長 1 秒：依 AI 的攻擊時間縮放播放速度，讓揮擊與傷害判定同步。
            SetFloat(AttackSpeedId, 1f / Mathf.Max(.2f, duration));
            SetTrigger(AttackId);
            if (Procedural)
            {
                Procedural.PlayAttack(duration, hit);
            }
        }

        wasAttacking = attacking;
        if (enemy.CurrentHealth < lastHealth - .01f && !attacking)
        {
            SetTrigger(HurtId);
            if (Procedural)
            {
                Procedural.PlayHurt();
            }
        }

        lastHealth = enemy.CurrentHealth;
    }
}
