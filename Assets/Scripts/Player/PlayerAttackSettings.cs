using UnityEngine;

/// <summary>Shared settings for one attack, consumed by combat, movement, animation and effects.</summary>
[System.Serializable]
public sealed class PlayerAttackSettings
{
    [Header("Combat")]
    public float AttackDamage = 25;
    public float AttackRange = 2.5f;
    [Range(1, 180)]
    public float AttackAngle = 65;
    public float AttackCooldown = .4f;
    [Min(.1f)]
    public float SwingDuration = .64f;
    [Range(20, 160)]
    public float LeftSwingAngle = 105;
    [Range(.3f, .85f)]
    public float StrikeEnd = .60f;
    [Range(0, 1)]
    public float HitStart = .34f;
    [Range(0, 1)]
    public float HitEnd = .58f;
    [Min(0)]
    public float KnockbackSpeed = 8;
    [Min(0)]
    public float KnockbackDuration = .3f;
    [Header("Animation blending")]
    public float AttackBlendTime = .065f;
    public float ComboBlendTime = .045f;
    [Header("Swing effects")]
    public float SlashDuration = .28f;
    [Range(.5f, 2)]
    public float SwordPitch = 1.25f;
    [Range(0, .2f)]
    public float SwordPitchVariation = .06f;
    [Header("Animator state and clock")]
    public string StateName = "Sword Attack";
    public string ProgressParameter = "AttackProgress";
    [Header("Displacement in model space")]
    public AnimationCurve LocalX = AnimationCurve.Constant(0, 1, 0);
    public AnimationCurve LocalZ = AnimationCurve.Constant(0, 1, 0);

    public static PlayerAttackSettings CreateReturnAttack() => new PlayerAttackSettings
    {
        StateName = "Sword Attack Return", ProgressParameter = "ReturnAttackProgress",
        HitStart = .5f, HitEnd = .83f
    };
}
