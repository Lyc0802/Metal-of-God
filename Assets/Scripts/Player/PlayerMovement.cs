using UnityEngine;

/// <summary>
/// 在物理更新中處理移動、跳躍、牆面滑動，以及金屬能力留下的慣性。
/// </summary>
[RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
[DefaultExecutionOrder(100)]
public class PlayerMovement : MonoBehaviour
{
    public float MovementSpeed = 6, RotationSpeed = 540, JumpForce = 6;
    [Tooltip("Degrees per second the body turns back upright after a metal pull tilted it.")]
    [Min(0)] public float UprightRecoverySpeed = 720;
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
    [Header("Step Climbing")]
    [Tooltip("Maximum obstacle height the player walks onto without jumping (like CharacterController Step Offset).")]
    [Min(0)] public float StepHeight = .35f;
    [Tooltip("Distance ahead of the capsule swept for steps.")]
    [Min(0)] public float StepCheckDistance = .2f;
    [Tooltip("Maximum upward speed while climbing a step.")]
    [Min(.01f)] public float StepClimbSpeed = 4;
    // Sweeps use a capsule shrunk by StepSweepSkin so they do not start overlapping the ground;
    // steps lower than the skin are left to the rounded capsule bottom.
    private const float StepMinHeight = .005f, StepContactTolerance = .03f;
    private const float StepSweepSkin = .01f, StepSweepBackOff = .05f;
    private readonly RaycastHit[] stepHits = new RaycastHit[16];
    private bool stepClimbing;
    private float stepRiseSpeed;
    // Wall contacts at or below this height belong to the step being climbed and must not block motion.
    private float stepIgnoreBelowY = float.NegativeInfinity;
    private readonly System.Collections.Generic.List<(Vector3 normal, float height)> wallNormals =
        new System.Collections.Generic.List<(Vector3 normal, float height)>();
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
        // Dynamic bodies must receive the player's contact impulse from the solver.
        if (collision.rigidbody && !collision.rigidbody.isKinematic) return;
        for (int i = 0; i < collision.contactCount; i++)
        {
            var contact = collision.GetContact(i);
            if (Mathf.Abs(contact.normal.y) < .55f)
            {
                wallNormals.Add((contact.normal, contact.point.y));
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
        foreach (var wall in wallNormals)
        {
            if (wall.height > stepIgnoreBelowY)
            {
                velocity = Slide(velocity, wall.normal);
            }
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
                    || (hit.rigidbody && !hit.rigidbody.isKinematic)
                    || Mathf.Abs(hit.normal.y) >= .55f
                    || hit.point.y <= stepIgnoreBelowY)
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
        RecoverUpright();
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
            EndStepClimb();
            ApplyAttackMotion();
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
        float moveSpeed = CurrentMovementSpeed * directionSpeed;
        // Detect before wall sliding so the step face does not cancel forward velocity.
        bool stepping = TryFindStep(moveDirection, out float stepRemaining, out float stepEdgeDistance);
        UpdateHorizontalVelocity(moveDirection, moveSpeed);
        wallNormals.Clear();
        if (stepping)
        {
            ApplyStepRise(stepRemaining, stepEdgeDistance, moveSpeed * moveDirection.magnitude);
        }
        else
        {
            EndStepClimb();
        }
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

    // Pulling onto a ceiling/wall tilts the body to face the surface normal; once released,
    // turn back upright (keeping the heading) so the player does not fall face-down.
    private void RecoverUpright()
    {
        var rotation = body.rotation;
        if (Vector3.Angle(rotation * Vector3.up, Vector3.up) < .1f) return;
        var heading = Vector3.ProjectOnPlane(rotation * Vector3.forward, Vector3.up);
        // Facing straight down/up: the head direction gives the horizontal heading instead.
        if (heading.sqrMagnitude < .01f) heading = Vector3.ProjectOnPlane(rotation * Vector3.up, Vector3.up);
        if (heading.sqrMagnitude < 1e-6f) heading = Vector3.forward;
        var upright = Quaternion.LookRotation(heading, Vector3.up);
        body.angularVelocity = Vector3.zero;
        body.MoveRotation(Quaternion.RotateTowards(rotation, upright, UprightRecoverySpeed * Time.fixedDeltaTime));
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
        stepClimbing = false;
        stepRiseSpeed = 0;
        stepIgnoreBelowY = float.NegativeInfinity;
    }

    // Capsule-sweep step detection, like CharacterController's Step Offset. Volume sweeps cannot slip
    // through mesh seams or cracks and do not need the step to have a side face:
    // 1) forward at the current height finds a low obstacle, 2) up finds headroom,
    // 3) forward at the raised height checks for a wall, 4) down finds the step top.
    private bool TryFindStep(Vector3 moveDirection, out float remaining, out float edgeDistance)
    {
        remaining = edgeDistance = 0;
        stepIgnoreBelowY = float.NegativeInfinity;
        if (StepHeight <= StepMinHeight || moveDirection.sqrMagnitude < .001f
            || !(IsGrounded || stepClimbing)
            || body.linearVelocity.y > stepRiseSpeed + .1f)
        {
            return false;
        }

        var direction = Vector3.ProjectOnPlane(moveDirection, Vector3.up).normalized;
        float foot = capsule.bounds.min.y;
        GetStepCapsule(out var upper, out var lower, out float radius);
        float reach = StepCheckDistance + CurrentMovementSpeed * Time.fixedDeltaTime;

        // 1) Only a low obstacle ahead can be a step.
        if (!SweepStep(upper, lower, radius, direction, reach, true, out var front)
            || front.point.y > foot + StepHeight + StepContactTolerance)
        {
            return false;
        }

        // 2) Headroom above the player limits how far it may rise.
        float lift = StepHeight + StepSweepSkin;
        if (SweepStep(upper, lower, radius, Vector3.up, lift, false, out var ceiling))
        {
            lift = ceiling.distance;
        }

        if (lift <= StepMinHeight)
        {
            return false;
        }

        // 3) Raised, the capsule must move past the edge; hitting something at the edge means a wall.
        var raise = Vector3.up * lift;
        float advance = front.distance + radius * .5f;
        if (SweepStep(upper + raise, lower + raise, radius, direction, advance, true, out var wall))
        {
            if (wall.distance <= front.distance + StepMinHeight)
            {
                return false;
            }

            advance = wall.distance;
        }

        // 4) Drop back down onto the step top.
        var shift = raise + direction * advance;
        if (!SweepStep(upper + shift, lower + shift, radius, Vector3.down, lift, false, out var landing)
            || landing.normal.y < .55f)
        {
            return false;
        }

        float height = landing.point.y - foot;
        if (height <= StepMinHeight || height > StepHeight + .001f)
        {
            return false;
        }

        // Reject a slope the capsule already stands on: that plane touches its bottom sphere.
        if (Mathf.Abs(Vector3.Dot(landing.normal, lower - landing.point) - (radius + StepSweepSkin)) < .008f)
        {
            return false;
        }

        float bestTop = landing.point.y;
        remaining = height;
        // The sweep already measured how far the capsule can move before touching the edge.
        edgeDistance = front.distance;
        stepIgnoreBelowY = bestTop + StepContactTolerance;
        return true;
    }

    private void ApplyStepRise(float remaining, float edgeDistance, float horizontalSpeed)
    {
        // Rise so the feet reach the top about when the capsule reaches the edge, never overshooting.
        float dt = Time.fixedDeltaTime;
        float rise = Mathf.Min(
            StepClimbSpeed,
            remaining * Mathf.Max(horizontalSpeed, .5f) / Mathf.Max(edgeDistance, .05f),
            remaining / dt);
        // Lift by position, not upward velocity: animation treats vertical velocity as a jump,
        // while stepping up is still walking. Horizontal velocity is kept as is.
        var velocity = body.linearVelocity;
        velocity.y = 0;
        body.linearVelocity = velocity;
        // The solver still integrates gravity this step; add back that drop so the net rise is `rise`.
        float gravityDrop = body.useGravity ? -Physics.gravity.y * dt * dt : 0;
        body.MovePosition(body.position + Vector3.up * (rise * dt + gravityDrop));
        stepClimbing = true;
        stepRiseSpeed = rise;
    }

    private void EndStepClimb()
    {
        if (stepClimbing)
        {
            // Remove leftover climb velocity so finishing a step never becomes a small hop.
            var velocity = body.linearVelocity;
            if (velocity.y > 0 && velocity.y <= stepRiseSpeed + .1f)
            {
                velocity.y = 0;
                body.linearVelocity = velocity;
            }
        }

        stepClimbing = false;
        stepRiseSpeed = 0;
        stepIgnoreBelowY = float.NegativeInfinity;
    }

    // World-space sphere centers of the player capsule, with the radius shrunk by StepSweepSkin.
    private void GetStepCapsule(out Vector3 upper, out Vector3 lower, out float radius)
    {
        var scale = transform.lossyScale;
        int axis = capsule.direction;
        radius = capsule.radius * Mathf.Max(Mathf.Abs(scale[(axis + 1) % 3]), Mathf.Abs(scale[(axis + 2) % 3]));
        float half = Mathf.Max(0, capsule.height * Mathf.Abs(scale[axis]) * .5f - radius);
        var center = transform.TransformPoint(capsule.center);
        var up = transform.TransformDirection(axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward);
        if (up.y < 0) up = -up;
        upper = center + up * half;
        lower = center - up * half;
        radius = Mathf.Max(.01f, radius - StepSweepSkin);
    }

    private bool SweepStep(Vector3 upper, Vector3 lower, float radius, Vector3 direction, float distance,
        bool backOff, out RaycastHit result)
    {
        result = default;
        // Starting slightly behind keeps an edge the player is already pressed against detectable.
        float offset = backOff ? StepSweepBackOff : 0;
        var start = -direction * offset;
        int count = Physics.CapsuleCastNonAlloc(upper + start, lower + start, radius, direction, stepHits,
            distance + offset, GroundMask, QueryTriggerInteraction.Ignore);
        float best = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            var hit = stepHits[i];
            // Skip initial overlaps; dynamic bodies are pushed by the solver rather than climbed.
            if (hit.collider.transform.IsChildOf(transform) || hit.distance >= best
                || (hit.distance <= 0 && hit.point == Vector3.zero)
                || (hit.rigidbody && !hit.rigidbody.isKinematic))
            {
                continue;
            }

            best = hit.distance;
            result = hit;
        }

        if (float.IsPositiveInfinity(best))
        {
            return false;
        }

        result.distance = Mathf.Max(0, best - offset);
        return true;
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

        // Climbing a step is walking; keep grounded so animation and foot placement stay on the ground.
        if (stepClimbing)
        {
            IsGrounded = true;
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
