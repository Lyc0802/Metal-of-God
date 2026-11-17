using UnityEngine;

/// <summary>
/// 在物理更新中處理移動、跳躍、牆面滑動，以及金屬能力留下的慣性。
/// </summary>
[RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
[DefaultExecutionOrder(100)]
public class PlayerMovement : MonoBehaviour
{
    public float MovementSpeed = 6, RotationSpeed = 540, JumpForce = 6;
    [Min(0)]
    public float SprintSpeed = 10;
    public bool IsSprinting { get; private set; }
    [Min(0)] public float GroundAcceleration = 22;
    [Range(0,1)] public float AttackMovementMultiplier = .25f;
    [Range(0,.2f)] public float JumpAnticipation = .09f;
    public bool IsPreparingJump => jumpPreparing;
    private bool jumpPreparing;
    private float jumpLaunchTime;
    public float CurrentMovementSpeed => (IsSprinting ? SprintSpeed : MovementSpeed) * (pewter
        ? pewter.CurrentMovementMultiplier
        : 1);
    public float CurrentJumpForce => JumpForce * (pewter ? pewter.CurrentJumpMultiplier : 1);

    public enum MotionMode { Locomotion, Attack, MetalAbility }
    public MotionMode CurrentMotionMode { get; private set; }
    public bool IsMetalDriven { get; private set; }
    // Compatibility accessors for animation authoring tools.
    private PlayerCombat AttackOwner => combat ? combat : (combat = GetComponent<PlayerCombat>());
    public AnimationCurve LocalX { get => AttackOwner.FirstAttack.LocalX; set => AttackOwner.FirstAttack.LocalX = value; }
    public AnimationCurve LocalZ { get => AttackOwner.FirstAttack.LocalZ; set => AttackOwner.FirstAttack.LocalZ = value; }
    public AnimationCurve ReturnLocalX { get => AttackOwner.ReturnAttack.LocalX; set => AttackOwner.ReturnAttack.LocalX = value; }
    public AnimationCurve ReturnLocalZ { get => AttackOwner.ReturnAttack.LocalZ; set => AttackOwner.ReturnAttack.LocalZ = value; }
    public Transform Visual;
    private bool attackMotionActive, originalGravity, releaseBrakePending;
    private float releaseSpeedMultiplier;
    private int attackSequence = -1;
    private Vector3 previousAttackSample;
    private Quaternion attackFacing;
    private PlayerCombat combat;
    private PlayerHealth health;
    private PlayerMetalManipulator manipulation;
    private PlayerTargetLockOn enemyLock;
    private PlayerCharacterAnimation characterAnimation;
    private PlayerPewterAbility pewter;
    [Min(0)]
    public float GroundDeceleration = 45;
    [Min(0)]
    public float AirDeceleration = 8;
    [Min(0)]
    public float MomentumSteering = 8;
    [Min(0)]
    public float AirReverseDeceleration = 15;
    private bool preservingMomentum;
    public float GroundCheckDistance = .15f;
    public LayerMask GroundMask = ~0;
    public PhysicsMaterial MovementMaterial;
    [Min(0)]
    public float WallSkin = .03f;
    private readonly System.Collections.Generic.List<Vector3> wallNormals = new System.Collections.Generic.List<Vector3>();
    public bool IsGrounded { get; private set; }
    public Vector3 DesiredMoveDirection => desired;

    public event System.Action Jumped;
    private Rigidbody body;
    private CapsuleCollider capsule;
    private Vector3 desired;
    private bool jump;
    private PhysicsMaterial runtimeMaterial, originalMaterial;
    private void Awake()
    {
        pewter = GetComponent<PlayerPewterAbility>();
        combat = GetComponent<PlayerCombat>();
        health = GetComponent<PlayerHealth>();
        manipulation = GetComponent<PlayerMetalManipulator>();
        enemyLock = GetComponent<PlayerTargetLockOn>();
        characterAnimation = GetComponent<PlayerCharacterAnimation>();
        body = GetComponent<Rigidbody>();
        capsule = GetComponent<CapsuleCollider>();
        body.constraints |= RigidbodyConstraints.FreezeRotation;
        originalMaterial = capsule.sharedMaterial;
        if (!MovementMaterial)
        {
            runtimeMaterial = new PhysicsMaterial("Player No Friction")
            {
                staticFriction = 0,
                dynamicFriction = 0,
                bounciness = 0,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };
        }

        capsule.sharedMaterial = MovementMaterial ? MovementMaterial : runtimeMaterial;
    }

    private void OnDestroy()
    {
        if (runtimeMaterial)
        {
            if (capsule)
            {
                capsule.sharedMaterial = originalMaterial;
            }

            Destroy(runtimeMaterial);
        }
    }

    public void ToggleSprint()
    {
        IsSprinting = !IsSprinting;
    }

    public void PreserveExternalMomentum()
    {
        preservingMomentum = true;
    }

    public void ReadInput(Vector2 move, bool jumpPressed, Camera view)
    {
        Vector3 forward = enemyLock && enemyLock.IsLocked
            ? Vector3.ProjectOnPlane(
            enemyLock.Target.TargetPoint - body.worldCenterOfMass,
            Vector3.up).normalized
            : Vector3.ProjectOnPlane(
            view.transform.forward,
            Vector3.up).normalized;
        if (forward.sqrMagnitude < .01f)
        {
            forward = Vector3.forward;
        }

        desired = forward * move.y + Vector3.Cross(Vector3.up, forward) * move.x;
        desired = Vector3.ClampMagnitude(desired, 1);
        jump |= jumpPressed;
    }

    private void OnCollisionEnter(Collision collision)
    {
        RecordWalls(collision);
    }

    private void OnCollisionStay(Collision collision)
    {
        RecordWalls(collision);
    }

    private void RecordWalls(Collision collision)
    {
        for (int i = 0; i < collision.contactCount; i++)
        {
            var normal = collision.GetContact(i).normal;
            if (Mathf.Abs(normal.y) < .55f)
            {
                wallNormals.Add(normal);
            }
        }
    }

    private static Vector3 Slide(Vector3 velocity, Vector3 normal)
    {
        // Remove only the horizontal component into a wall; gravity/jumping are untouched.
        normal = Vector3.ProjectOnPlane(normal, Vector3.up).normalized;
        float into = Vector3.Dot(velocity, normal);
        return into < 0 ? velocity - normal * into : velocity;
    }

    private Vector3 WallSlide(Vector3 velocity)
    {
        foreach (var normal in wallNormals)
        {
            velocity = Slide(velocity, normal);
        }

        var scale = transform.lossyScale;
        int axis = capsule.direction;
        int firstRadialAxis = (axis + 1) % 3;
        int secondRadialAxis = (axis + 2) % 3;
        float radius = capsule.radius * Mathf.Max(Mathf.Abs(scale[firstRadialAxis]), Mathf.Abs(scale[secondRadialAxis]));
        float half = Mathf.Max(0, capsule.height * Mathf.Abs(scale[axis]) * .5f - radius);
        var center = transform.TransformPoint(capsule.center);
        var direction = transform.TransformDirection(axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward);
        for (int i = 0; i < 3 && velocity.sqrMagnitude > .0001f; i++)
        {
            var before = velocity;
            foreach (var hit in Physics.CapsuleCastAll(
                center + direction * half,
                center - direction * half,
                radius * .98f,
                velocity.normalized,
                velocity.magnitude * Time.fixedDeltaTime + WallSkin,
                ~0,
                QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.transform.IsChildOf(transform) || hit.distance <= .0001f
                    || Mathf.Abs(hit.normal.y) >= .55f)
                {
                    continue;
                }

                velocity = Slide(velocity, hit.normal);
            }

            if ((velocity - before).sqrMagnitude < .0001f)
            {
                break;
            }
        }

        return velocity;
    }

    private void FixedUpdate()
    {
        UpdateGroundedState();
        if (health && health.IsDead)
        {
            if (manipulation) manipulation.Release();
            ClearPendingMotion();
            return;
        }

        if (releaseBrakePending)
        {
            releaseBrakePending = false;
            body.linearVelocity *= releaseSpeedMultiplier;
        }

        // Exactly one owner chooses player motion during this physics step.
        if (manipulation && manipulation.isActiveAndEnabled && manipulation.TickPhysics(this))
        {
            CurrentMotionMode = MotionMode.MetalAbility;
            ClearPendingMotion();
            return;
        }
        ReleaseMetalMotion();
        bool attacking = combat && combat.IsAttacking;
        CurrentMotionMode = attacking ? MotionMode.Attack : MotionMode.Locomotion;
        if (!attacking) attackMotionActive = false;
        if (enemyLock && enemyLock.isActiveAndEnabled && enemyLock.TryGetFacingRotation(out var facing))
            body.MoveRotation(facing);
        if (attacking && IsGrounded)
        {
            body.linearVelocity = new Vector3(0, body.linearVelocity.y, 0);
            jump = jumpPreparing = false;
            wallNormals.Clear();
            ApplyAttackMotion();
            Debug.Log("attack");
            return;
        }
        float abilityMovement = characterAnimation ? characterAnimation.MetalAbilityMovementMultiplier : 1;
        bool abilityAnimating = characterAnimation && characterAnimation.IsPlayingMetalAbility;
        if (!attacking && abilityAnimating) CurrentMotionMode = MotionMode.MetalAbility;
        var moveDirection = desired * (attacking ? AttackMovementMultiplier : abilityMovement);
        if (abilityAnimating) jump = jumpPreparing = false;

        if (attacking)
        {
            jump = false;
        }

        var localDirection = transform.InverseTransformDirection(moveDirection.normalized);
        float directionSpeed = enemyLock && enemyLock.IsLocked
            ? Mathf.Lerp(1,.65f,Mathf.Abs(localDirection.x)) * (localDirection.z<0 ? .85f : 1) : 1;
        UpdateHorizontalVelocity(moveDirection, CurrentMovementSpeed * directionSpeed);
        wallNormals.Clear();
        if (moveDirection.sqrMagnitude > .001f && !(enemyLock && enemyLock.IsLocked))
        {
            body.MoveRotation(Quaternion.RotateTowards(body.rotation, Quaternion.LookRotation(moveDirection), RotationSpeed * Time.fixedDeltaTime));
        }

        if (jump && IsGrounded && !attacking && !jumpPreparing)
        {
            jumpPreparing=true;
            jumpLaunchTime=Time.time+JumpAnticipation;
        }
        if (jumpPreparing && (!IsGrounded || attacking)) jumpPreparing=false;
        if (jumpPreparing && Time.time>=jumpLaunchTime)
        {
            jumpPreparing=false;
            body.AddForce(Vector3.up * CurrentJumpForce, ForceMode.VelocityChange);
            Jumped?.Invoke();
        }

        jump = false;
        if (attacking) ApplyAttackMotion();
    }

    // Called only by the metal solver inside this component's FixedUpdate.
    public void ApplyMetalMotion(Vector3 velocity, Quaternion? rotation)
    {
        if (!IsMetalDriven)
        {
            originalGravity = body.useGravity;
            body.angularVelocity = Vector3.zero;
            IsMetalDriven = true;
        }
        body.useGravity = false;
        body.linearVelocity = velocity;
        if (rotation.HasValue) body.MoveRotation(rotation.Value);
    }

    public void ReleaseMetalMotion()
    {
        if (!IsMetalDriven) return;
        if (body) body.useGravity = originalGravity;
        IsMetalDriven = false;
        PreserveExternalMomentum();
    }

    public void QueueMetalReleaseBrake(float multiplier)
    {
        releaseSpeedMultiplier = Mathf.Clamp01(multiplier);
        releaseBrakePending = true;
    }

    private void ClearPendingMotion()
    {
        wallNormals.Clear();
        jump = jumpPreparing = false;
        attackMotionActive = false;
    }

    private void OnDisable()
    {
        ReleaseMetalMotion();
        ClearPendingMotion();
        releaseBrakePending = false;
        desired = Vector3.zero;
    }

    private void ApplyAttackMotion()
    {
        var x = combat.ActiveAttack.LocalX;
        var z = combat.ActiveAttack.LocalZ;
        if (x == null || z == null) return;
        if (!attackMotionActive || attackSequence != combat.SwingSequence)
        {
            attackMotionActive = true;
            attackSequence = combat.SwingSequence;
            previousAttackSample = new Vector3(x.Evaluate(0), 0, z.Evaluate(0));
            attackFacing = body.rotation;
        }
        var sample = new Vector3(x.Evaluate(combat.SwingProgress), 0, z.Evaluate(combat.SwingProgress));
        var delta = attackFacing * ((sample - previousAttackSample) * (Visual ? Visual.lossyScale.y : 1));
        previousAttackSample = sample;
        ApplyAnimationDisplacement(delta);
    }

    // Shared collision-limited displacement entry point for future ability motion.
    // Invoke from the physics step; visual animation must not move the Rigidbody directly.
    private void ApplyAnimationDisplacement(Vector3 delta)
    {
        float distance = delta.magnitude;
        if (distance < .00001f) return;
        foreach (var hit in body.SweepTestAll(delta.normalized, distance + .015f, QueryTriggerInteraction.Ignore))
        {
            if (!hit.collider.transform.IsChildOf(transform) && Mathf.Abs(hit.normal.y) < .8f)
                distance = Mathf.Min(distance, Mathf.Max(0, hit.distance - .015f));
        }
        body.MovePosition(body.position + delta.normalized * distance);
    }

    private void UpdateGroundedState()
    {
        var bounds = capsule.bounds;
        IsGrounded = false;
        foreach (var hit in Physics.SphereCastAll(
            new Vector3(bounds.center.x, bounds.min.y + .2f, bounds.center.z),
            .15f,
            Vector3.down,
            .05f + GroundCheckDistance,
            GroundMask,
            QueryTriggerInteraction.Ignore))
        {
            if (!hit.collider.transform.IsChildOf(transform) && hit.normal.y > .55f)
            {
                IsGrounded = true;
            }
        }
    }

    private void UpdateHorizontalVelocity(Vector3 moveDirection, float moveSpeed)
    {
        var current = body.linearVelocity;
        var horizontal = new Vector3(current.x, 0, current.z);
        if (preservingMomentum)
        {
            if (IsGrounded)
            {
                // Landed momentum must converge even when a movement key stays held.
                var target = moveDirection * moveSpeed;
                horizontal = Vector3.MoveTowards(horizontal, target, GroundDeceleration * Time.fixedDeltaTime);
                if ((horizontal - target).sqrMagnitude < .0001f)
                {
                    preservingMomentum = false;
                }
            }
            else if (moveDirection.sqrMagnitude > .001f)
            {
                var direction = moveDirection.normalized;
                float along = Vector3.Dot(horizontal, direction);
                if (along < 0)
                {
                    // Opposite input brakes only the opposing component, without instant reversal.
                    horizontal += direction * Mathf.Min(
                        -along,
                        AirReverseDeceleration * moveDirection.magnitude * Time.fixedDeltaTime);
                }
                else
                {
                    float missing = moveSpeed * moveDirection.magnitude - along;
                    if (missing > 0)
                    {
                        horizontal += direction * Mathf.Min(missing, MomentumSteering * Time.fixedDeltaTime);
                    }
                }
            }
            else
            {
                // Apply air drag to released metal momentum too, without changing vertical fall speed.
                horizontal = Vector3.MoveTowards(horizontal, Vector3.zero, AirDeceleration * Time.fixedDeltaTime);
            }

            horizontal = WallSlide(horizontal);
            body.linearVelocity = new Vector3(horizontal.x, current.y, horizontal.z);
        }
        else if (moveDirection.sqrMagnitude > .001f)
        {
            var target = moveDirection * moveSpeed;
            var velocity = WallSlide(IsGrounded ? Vector3.MoveTowards(horizontal,target,GroundAcceleration*Time.fixedDeltaTime) : target);
            velocity.y = body.linearVelocity.y;
            body.linearVelocity = velocity;
        }
        else
        {
            var velocity = body.linearVelocity;
            var planar = Vector3.MoveTowards(
                new Vector3(velocity.x, 0, velocity.z),
                Vector3.zero,
                (IsGrounded ? GroundDeceleration : AirDeceleration) * Time.fixedDeltaTime);
            body.linearVelocity = new Vector3(planar.x, velocity.y, planar.z);
        }
    }
}
