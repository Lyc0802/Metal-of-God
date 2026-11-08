using UnityEngine;
[DefaultExecutionOrder(-100)]
[RequireComponent(typeof(PlayerMovement), typeof(PlayerCombat), typeof(Rigidbody))]
public sealed class PlayerCharacterAnimation : MonoBehaviour
{
    public const float AuthoredAttackDuration = .64f, AuthoredWalkStride = .60f;
    public Animator CharacterAnimator;
    public float MovementBlendTime = .12f, WalkReferenceSpeed = 1.9f, RunReferenceSpeed = 5.1f;
    public float MaximumWalkRate = 2.5f, MaximumRunRate = 2.5f;
    public float ReturnBlendTime = .15f, AttackBlendTime = .065f;
    public float ComboBlendTime = .045f;
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
        body=GetComponent<Rigidbody>(); health=GetComponent<PlayerHealth>();
        if (!CharacterAnimator) CharacterAnimator=GetComponentInChildren<Animator>();
        if (CharacterAnimator) { CharacterAnimator.applyRootMotion=false; CharacterAnimator.cullingMode=AnimatorCullingMode.AlwaysAnimate; }
    }
    private void Update()
    {
        if (!CharacterAnimator) return;
        CharacterAnimator.speed=health && health.IsDead ? 0 : 1;
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
        string next=combat.IsAttacking ? (combat.AttackIndex==1 ? "Sword Attack Return" : "Sword Attack") : movement.IsPreparingJump ? "Jump Anticipation"
            : !grounded ? (body.linearVelocity.y > .15f ? "Jump Ascending" : "Fall Descending")
            : Time.time<landingUntil ? "Landing" : "Locomotion";
        if (combat.IsAttacking) ApplyAttackPose();
        else if (next!=state)
        {
            a.CrossFadeInFixedTime(next,combat.IsAttacking ? AttackBlendTime : next=="Landing" ? .055f : ReturnBlendTime,0,0);
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
        string next=returning?"Sword Attack Return":"Sword Attack";
        // Each state has its own clock. Resetting a shared clock to zero would
        // rewind the outgoing attack during the crossfade, exposing its ready pose.
        CharacterAnimator.SetFloat(returning?"AttackProgress":"ReturnAttackProgress",1);
        CharacterAnimator.SetFloat(returning?"ReturnAttackProgress":"AttackProgress",combat.SwingProgress);
        if(state!=next)
        {
            bool linking=state=="Sword Attack" || state=="Sword Attack Return";
            CharacterAnimator.CrossFadeInFixedTime(next,linking?ComboBlendTime:AttackBlendTime,0,0);
            state=next;
        }
    }
    private void OnDisable() { state=null; }
}

