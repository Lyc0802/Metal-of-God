using UnityEngine;
[DefaultExecutionOrder(-100)]
[RequireComponent(typeof(PlayerMovement), typeof(PlayerCombat), typeof(Rigidbody))]
public sealed class PlayerCharacterAnimation : MonoBehaviour
{
    public const float AuthoredAttackDuration = .64f, AuthoredWalkStride = .60f;
    public Animator CharacterAnimator;
    public float MovementBlendTime = .12f, WalkReferenceSpeed = 1.9f, RunReferenceSpeed = 5.1f;
    public float MaximumWalkRate = 2.5f, MaximumRunRate = 2.5f;
    public float ReturnBlendTime = .15f;
    private PlayerCombat AttackOwner => combat ? combat : (combat = GetComponent<PlayerCombat>());
    public float AttackBlendTime { get => AttackOwner.FirstAttack.AttackBlendTime; set { AttackOwner.FirstAttack.AttackBlendTime = value; AttackOwner.ReturnAttack.AttackBlendTime = value; } }
    public float ComboBlendTime { get => AttackOwner.FirstAttack.ComboBlendTime; set { AttackOwner.FirstAttack.ComboBlendTime = value; AttackOwner.ReturnAttack.ComboBlendTime = value; } }
    [System.Serializable]
    public sealed class MetalAbilityAnimation
    {
        public MetalType Metal;
        [Tooltip("Optional state on Animator layer 0. Empty leaves current animation unchanged.")]
        public string StateName;
        [Min(.01f)] public float Duration = .6f;
        [Min(0)] public float BlendTime = .1f;
        [Range(0, 1)] public float MovementMultiplier = 1;
    }
    [Header("Optional metal ability animations")]
    public MetalAbilityAnimation[] MetalAnimations = new MetalAbilityAnimation[0];
    public event System.Action<MetalType> MetalAbilityRequested;
    private MetalAbilityAnimation activeMetalAnimation;
    private float metalAnimationEnd;
    private PlayerMetalManipulator metalManipulator;
    public MetalType CurrentAnimationMetal => IsPlayingMetalAbility ? activeMetalAnimation.Metal : MetalType.None;
    public bool IsPlayingMetalAbility => isActiveAndEnabled && activeMetalAnimation != null
        && Time.time < metalAnimationEnd && !(health && health.IsDead) && !(combat && combat.IsAttacking)
        && ((activeMetalAnimation.Metal != MetalType.Iron && activeMetalAnimation.Metal != MetalType.Steel)
            || (metalManipulator && metalManipulator.IsLocked));
    public float MetalAbilityMovementMultiplier => IsPlayingMetalAbility
        ? Mathf.Clamp01(activeMetalAnimation.MovementMultiplier) : 1;

    public bool PlayMetalAbility(MetalType metal)
    {
        MetalAbilityRequested?.Invoke(metal);
        if (!isActiveAndEnabled || !CharacterAnimator || (health && health.IsDead) || MetalAnimations == null)
            return false;
        foreach (var animation in MetalAnimations)
        {
            if (animation == null || animation.Metal != metal || string.IsNullOrWhiteSpace(animation.StateName))
                continue;
            if (!CharacterAnimator.HasState(0, Animator.StringToHash(animation.StateName))) return false;
            activeMetalAnimation = animation;
            metalAnimationEnd = Time.time + Mathf.Max(.01f, animation.Duration);
            CharacterAnimator.CrossFadeInFixedTime(animation.StateName, Mathf.Max(0, animation.BlendTime), 0, 0);
            state = animation.StateName;
            return true;
        }
        return false;
    }

    public void StopMetalAbility()
    {
        activeMetalAnimation = null;
        metalAnimationEnd = 0;
    }

    private PlayerMovement movement;
    private PlayerCombat combat;
    private PlayerHealth health;
    private Rigidbody body;
    private bool wasGrounded;
    private string state;
    private float landingUntil, previousVertical;
    public float PlanarSpeed => body ? Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up).magnitude : 0;
    public float TargetRunBlend => movement && movement.IsSprinting ? 1 : Mathf.InverseLerp(WalkReferenceSpeed * 1.6f, RunReferenceSpeed, PlanarSpeed);
    public float TargetWalkRate => Mathf.Clamp(PlanarSpeed / Mathf.Lerp(WalkReferenceSpeed, RunReferenceSpeed, TargetRunBlend), .35f, Mathf.Lerp(MaximumWalkRate, MaximumRunRate, TargetRunBlend));
    public Vector2 LocalMotion { get; private set; }
    public string VisualState => state;
    private void Awake()
    {
        movement=GetComponent<PlayerMovement>(); combat=GetComponent<PlayerCombat>();
        metalManipulator = GetComponent<PlayerMetalManipulator>();
        body=GetComponent<Rigidbody>(); health=GetComponent<PlayerHealth>();
        if (!CharacterAnimator) CharacterAnimator=GetComponentInChildren<Animator>();
        if (CharacterAnimator) { CharacterAnimator.applyRootMotion=false; CharacterAnimator.cullingMode=AnimatorCullingMode.AlwaysAnimate; }
    }
    private void Update()
    {
        if (!CharacterAnimator) return;
        CharacterAnimator.speed=health && health.IsDead ? 0 : 1;
        if (!IsPlayingMetalAbility) StopMetalAbility();
        var local=transform.InverseTransformDirection(Vector3.ProjectOnPlane(body.linearVelocity,Vector3.up));
        LocalMotion=PlanarSpeed>.05f ? new Vector2(local.x,local.z).normalized : Vector2.zero;
        var a=CharacterAnimator;
        a.SetFloat("MoveX",LocalMotion.x,MovementBlendTime,Time.deltaTime);
        a.SetFloat("MoveY",LocalMotion.y,MovementBlendTime,Time.deltaTime);
        a.SetFloat("Speed",Mathf.Clamp01(PlanarSpeed / Mathf.Max(.1f,RunReferenceSpeed)),MovementBlendTime,Time.deltaTime);
        a.SetFloat("Moving",Mathf.InverseLerp(.03f,.35f,PlanarSpeed),MovementBlendTime,Time.deltaTime);
        a.SetFloat("RunBlend",TargetRunBlend,.18f,Time.deltaTime);
        float directionalStride=Mathf.Lerp(1,.58f,Mathf.Abs(LocalMotion.x));
        a.SetFloat("WalkRate",Mathf.Clamp(TargetWalkRate/directionalStride,.35f,2.5f),.12f,Time.deltaTime);
        bool grounded=movement.IsGrounded && body.linearVelocity.y<.8f;
        a.SetBool("IsGrounded",grounded); a.SetFloat("VerticalVelocity",body.linearVelocity.y,.06f,Time.deltaTime);
        a.SetBool("IsAttacking",combat.IsAttacking);
        if (grounded && !wasGrounded && previousVertical < -1.5f) landingUntil=Time.time+.26f;
        string next=combat.IsAttacking ? combat.ActiveAttack.StateName : IsPlayingMetalAbility ? activeMetalAnimation.StateName : movement.IsPreparingJump ? "Jump Anticipation"
            : !grounded ? (body.linearVelocity.y > .15f ? "Jump Ascending" : "Fall Descending")
            : Time.time<landingUntil ? "Landing" : "Locomotion";
        if (combat.IsAttacking) ApplyAttackPose();
        else if (next!=state)
        {
            a.CrossFadeInFixedTime(next,next=="Landing" ? .055f : ReturnBlendTime,0,0);
            state=next;
        }
        wasGrounded=grounded; previousVertical=body.linearVelocity.y;
    }
    private void LateUpdate()
    {
        // Combat advances after Update; trails must see the same sampled hand pose.
        if (CharacterAnimator && combat.IsAttacking)
        {
            ApplyAttackPose();
            CharacterAnimator.Update(0);
        }
    }
    private void ApplyAttackPose()
    {
        bool returning=combat.AttackIndex==1;
        var attack = combat.ActiveAttack;
        string next = attack.StateName;
        // Each state has its own clock. Resetting a shared clock to zero would
        // rewind the outgoing attack during the crossfade, exposing its ready pose.
        CharacterAnimator.SetFloat((returning ? combat.FirstAttack : combat.ReturnAttack).ProgressParameter,1);
        CharacterAnimator.SetFloat(attack.ProgressParameter,combat.SwingProgress);
        if(state!=next)
        {
            bool linking=state==combat.FirstAttack.StateName || state==combat.ReturnAttack.StateName;
            CharacterAnimator.CrossFadeInFixedTime(next,linking?attack.ComboBlendTime:attack.AttackBlendTime,0,0);
            state=next;
        }
    }
    private void OnDisable() { state=null; StopMetalAbility(); }
}

