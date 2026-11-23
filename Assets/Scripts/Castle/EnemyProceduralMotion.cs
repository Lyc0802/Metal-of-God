using UnityEngine;

/// <summary>
/// 沒有攻擊 / 受傷 / 死亡動畫的模型用的程式動作：以腳底為軸整個身體前傾後仰、彈跳、搖擺。
/// 沒有骨架的靜態模型連走路 / 待機也由這裡做（上下彈跳 + 左右搖擺 + 呼吸）。
/// 掛在以腳底為原點的容器上，Pivot 指到它自己（或其他以腳底為軸的物件）。
/// </summary>
public class EnemyProceduralMotion : MonoBehaviour
{
    public Transform Pivot;
    [Tooltip("移動時上下彈跳 / 左右搖擺（沒有走路動畫的靜態模型才需要）。")]
    public bool BobWhileMoving = true;
    [Tooltip("待機時的呼吸起伏。")]
    public bool Breathe = true;
    [Min(0)] public float BobHeight = .09f, SwayDegrees = 5f;
    [Tooltip("一步的距離（公尺）；步頻 = 移動速度 / 這個值。")]
    [Min(.2f)] public float StrideLength = 1.4f;
    [Tooltip("攻擊時蓄力後仰、揮出前傾的角度。")]
    public float WindupLean = -20, StrikeLean = 32;
    [Header("Arm swing (skeleton models)")]
    [Tooltip("攻擊時額外揮動的手臂骨頭（左右對稱，套用同樣的旋轉）。")]
    public Transform[] ArmBones;
    [Tooltip("手臂旋轉軸（骨頭本地座標）。")]
    public Vector3 ArmAxis = Vector3.forward;
    [Tooltip("蓄力時舉到頭頂後的角度，與命中瞬間砸到身前的角度。")]
    public float ArmWindup = 150, ArmStrike = 55;

    private Vector3 basePosition, baseScale;
    private Quaternion baseRotation;
    private float speed, stepPhase;
    private float attackTime = -1, attackDuration = 1, attackHit = .5f;
    private float hurtTime = -1;
    private float deathTime = -1;

    private const float HurtDuration = .35f, DeathDuration = .9f;

    private void Awake()
    {
        if (!Pivot)
        {
            Pivot = transform;
        }

        basePosition = Pivot.localPosition;
        baseRotation = Pivot.localRotation;
        baseScale = Pivot.localScale;
    }

    public void SetSpeed(float metersPerSecond)
    {
        speed = metersPerSecond;
    }

    /// <param name="hitFraction">揮擊命中點佔整段攻擊時間的比例（0~1）。</param>
    public void PlayAttack(float duration, float hitFraction)
    {
        if (deathTime >= 0)
        {
            return;
        }

        attackDuration = Mathf.Max(.2f, duration);
        attackHit = Mathf.Clamp(hitFraction, .2f, .9f);
        attackTime = 0;
    }

    public void PlayHurt()
    {
        if (deathTime < 0)
        {
            hurtTime = 0;
        }
    }

    public void PlayDeath()
    {
        if (deathTime < 0)
        {
            deathTime = 0;
        }
    }

    private void LateUpdate()
    {
        if (!Pivot)
        {
            return;
        }

        float dt = Time.deltaTime;
        float pitch = 0, roll = 0, lift = 0, scaleY = 1, arm = 0;

        if (deathTime >= 0)
        {
            deathTime += dt;
            float k = Mathf.SmoothStep(0, 1, Mathf.Clamp01(deathTime / DeathDuration));
            pitch = Mathf.Lerp(0, -88f, k);
            lift = Mathf.Lerp(0, .15f, k);
        }
        else
        {
            if (BobWhileMoving && speed > .3f)
            {
                stepPhase += dt * Mathf.PI * 2f * (speed / StrideLength);
                float strength = Mathf.Clamp01(speed / 2.5f);
                lift = Mathf.Abs(Mathf.Sin(stepPhase)) * BobHeight * strength;
                roll = Mathf.Sin(stepPhase) * SwayDegrees * strength;
                pitch += 4f * strength;
            }
            else if (Breathe)
            {
                scaleY = 1f + Mathf.Sin(Time.time * 1.7f) * .012f;
            }

            if (attackTime >= 0)
            {
                attackTime += dt;
                float u = attackTime / attackDuration;
                if (u >= 1)
                {
                    attackTime = -1;
                }
                else
                {
                    float windupEnd = Mathf.Max(.1f, attackHit - .12f);
                    float strikeEnd = Mathf.Min(.98f, attackHit + .06f);
                    float lean;
                    if (u < windupEnd)
                    {
                        float k = Mathf.SmoothStep(0, 1, u / windupEnd);
                        lean = Mathf.Lerp(0, WindupLean, k);
                        arm = Mathf.Lerp(0, ArmWindup, k);
                    }
                    else if (u < strikeEnd)
                    {
                        float s = (u - windupEnd) / (strikeEnd - windupEnd);
                        float k = 1 - (1 - s) * (1 - s);
                        lean = Mathf.Lerp(WindupLean, StrikeLean, k);
                        arm = Mathf.Lerp(ArmWindup, ArmStrike, k);
                    }
                    else
                    {
                        float k = Mathf.SmoothStep(0, 1, (u - strikeEnd) / (1 - strikeEnd));
                        lean = Mathf.Lerp(StrikeLean, 0, k);
                        arm = Mathf.Lerp(ArmStrike, 0, k);
                    }

                    pitch += lean;
                }
            }

            if (hurtTime >= 0)
            {
                hurtTime += dt;
                float u = hurtTime / HurtDuration;
                if (u >= 1)
                {
                    hurtTime = -1;
                }
                else
                {
                    pitch -= Mathf.Sin(u * Mathf.PI) * 14f;
                }
            }
        }

        // Animator 已經在 Update 階段算完骨架姿勢，這裡在它之上疊加手臂的揮動。
        if (ArmBones != null && Mathf.Abs(arm) > .01f)
        {
            var swing = Quaternion.AngleAxis(arm, ArmAxis);
            foreach (var bone in ArmBones)
            {
                if (bone)
                {
                    bone.localRotation = bone.localRotation * swing;
                }
            }
        }

        Pivot.localPosition = basePosition + Vector3.up * lift;
        Pivot.localRotation = baseRotation * Quaternion.Euler(pitch, 0, roll);
        Pivot.localScale = new Vector3(baseScale.x, baseScale.y * scaleY, baseScale.z);
    }
}
