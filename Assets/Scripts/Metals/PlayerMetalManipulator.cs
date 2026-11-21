using UnityEngine;

/// <summary>
/// 處理 Iron 拉與 Steel 推的物理作用、重量差異及釋放後的慣性。
/// </summary>
[RequireComponent(typeof(Rigidbody), typeof(PlayerMetalInventory), typeof(PlayerMovement))]
public class PlayerMetalManipulator : MonoBehaviour
{
    [Min(.01f)]
    public float Weight = 80;

    [Min(0)]
    public float PushSpeed = 20, PullSpeed = 20;
    [Range(0, 1)]
    public float ReleaseSpeedMultiplier = .5f;
    [Min(.01f)]
    public float ArrivalDistance = .12f;
    [Min(.01f)]
    public float SlowdownDistance = 2;
    [Min(0)]
    public float WallFacingSpeed = 540;
    [Min(.01f)]
    public float WallTurnSmoothTime = .3f;
    [Min(0)]
    public float WallTurnDistance = 5;
    private float wallTurnVelocity;
    public bool IsLocked { get; private set; }
    /// <summary>拉到抵達點、正停在目標表面上（例如黏在牆上）。</summary>
    public bool IsHoldingSurface { get; private set; }
    /// <summary>
    /// 鎖定瞬間決定的牆面轉身方向（+1 = 向右 / 順時針，-1 = 向左，0 = 不確定時走最短路徑）。
    /// 接近 180° 時最短路徑不唯一，固定方向才能讓轉身與伸手的那隻手一致。
    /// </summary>
    public float PlannedWallTurnSign { get; private set; }
    public bool IsPull { get; private set; }
    public MetalSurface Target { get; private set; }

    private Rigidbody body;
    private PlayerMetalInventory inventory;
    private PlayerMetalSelector selector;
    private PlayerMovement movement;
    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        movement = GetComponent<PlayerMovement>();
        inventory = GetComponent<PlayerMetalInventory>();
        selector = GetComponent<PlayerMetalSelector>();
    }

    public void HandleInput(bool held, float scroll, MetalSurface selected)
    {
        if (!held)
        {
            if (movement && movement.IsMetalDriven)
            {
                movement.QueueMetalReleaseBrake(ReleaseSpeedMultiplier);
            }

            Release();
            return;
        }

        if (IsLocked || scroll == 0 || !selected.Valid || !InRange(selected))
        {
            return;
        }

        bool pull = scroll < 0;
        if (!inventory.CanUse(pull ? MetalType.Iron : MetalType.Steel))
        {
            return;
        }

        Target = selected;
        IsPull = pull;
        IsLocked = true;
        float turn = WallTurnYaw;
        PlannedWallTurnSign = Mathf.Abs(turn) > 1 ? Mathf.Sign(turn) : 0;
    }

    private bool InRange(MetalSurface surface)
    {
        return selector
            ? selector.IsPointInRange(surface.Point)
            : (surface.Point - body.worldCenterOfMass).sqrMagnitude <= 30f * 30f;
    }

    public void Release()
    {
        if (movement) movement.ReleaseMetalMotion();
        wallTurnVelocity = 0;
        IsLocked = false;
        IsHoldingSurface = false;
        PlannedWallTurnSign = 0;
        Target = default;
    }

    private void OnDisable()
    {
        Release();
    }

    // Account for the player volume: its center cannot reach a solid wall's surface.
    private float PlayerExtent(Vector3 direction)
    {
        var collider = GetComponent<Collider>();
        if (!collider)
        {
            return 0;
        }

        var e = collider.bounds.extents;
        return Mathf.Abs(direction.x) * e.x + Mathf.Abs(direction.y) * e.y + Mathf.Abs(direction.z) * e.z;
    }

    private Vector3 Velocity(Vector3 direction, float remaining)
    {
        float speed = IsPull ? PullSpeed : PushSpeed;
        if (IsPull)
        {
            speed = Mathf.Min(
                speed * Mathf.Clamp01(remaining / Mathf.Max(.01f, SlowdownDistance)),
                Mathf.Max(0, remaining) / Time.fixedDeltaTime);
        }

        return direction * speed;
    }

    /// <summary>
    /// 鎖定的拉力會移動玩家（金屬比玩家重）。只有這種情況下，接近牆面時玩家會轉身背對牆。
    /// </summary>
    public bool MovesPlayer => IsLocked && Target.Valid && Target.Metal.weight > Weight
        && !Mathf.Approximately(Weight, Target.Metal.weight);

    /// <summary>接近牆面時要轉向的朝向：沿目標表面法線朝外。</summary>
    public Quaternion WallFacingRotation
    {
        get
        {
            var forward = Target.Normal;
            var up = Mathf.Abs(Vector3.Dot(forward, Vector3.up)) > .99f ? Vector3.forward : Vector3.up;
            return Quaternion.LookRotation(forward, up);
        }
    }

    /// <summary>
    /// 以目前身體朝向計算，接近牆面時的轉身方向（水平角度，正 = 向右 / 順時針）。
    /// 與 TickPhysics 的 RotateTowards 使用同一個目標朝向（取最短路徑）。
    /// </summary>
    public float WallTurnYaw
    {
        get
        {
            var from = Vector3.ProjectOnPlane(body.rotation * Vector3.forward, Vector3.up);
            var to = Vector3.ProjectOnPlane(WallFacingRotation * Vector3.forward, Vector3.up);
            if (from.sqrMagnitude < 1e-6f || to.sqrMagnitude < 1e-6f) return 0;
            return Vector3.SignedAngle(from, to, Vector3.up);
        }
    }

    public bool TickPhysics(PlayerMovement motor)
    {
        IsHoldingSurface = false;
        if (!IsLocked)
        {
            return false;
        }

        if (!Target.Valid || !InRange(Target) || !inventory.CanUse(IsPull ? MetalType.Iron : MetalType.Steel))
        {
            Release();
            return false;
        }

        float metalWeight = Target.Metal.weight;
        float playerWeight = Weight;
        if (Mathf.Approximately(playerWeight, metalWeight))
        {
            motor.ReleaseMetalMotion();
            return false;
        }

        Vector3 delta = Target.Point - body.worldCenterOfMass;
        Vector3 toward = delta.sqrMagnitude > .000001f ? delta.normalized : -Target.Normal;
        float remaining = delta.magnitude - PlayerExtent(toward) - ArrivalDistance;
        if (playerWeight > metalWeight)
        {
            motor.ReleaseMetalMotion();
            var metalBody = Target.Collider.attachedRigidbody;
            if (!metalBody)
            {
                metalBody = Target.Metal.GetComponentInParent<Rigidbody>();
            }

            if (metalBody && !metalBody.isKinematic)
            {
                metalBody.linearVelocity = Velocity(IsPull ? -toward : toward, remaining);
                if (metalBody.linearVelocity.sqrMagnitude > .001f)
                {
                    MetalImpactFeedback.MarkDriven(metalBody, Target.Metal);
                }
            }
        }
        else
        {
            Quaternion? rotation = null;
            // While locked, hold the arrival position through zero velocity and natural collision.
            bool arrived = IsPull && remaining <= ArrivalDistance;
            IsHoldingSurface = arrived;
            var velocity = arrived ? Vector3.zero : Velocity(IsPull ? toward : -toward, remaining);
            if (IsPull && remaining <= WallTurnDistance)
            {
                // Face outward along the selected surface normal.
                var targetRotation = WallFacingRotation;
                float angle = Quaternion.Angle(body.rotation, targetRotation);
                // A damped angular response accelerates into the turn and eases into alignment.
                float nextAngle = Mathf.SmoothDamp(
                    angle,
                    0,
                    ref wallTurnVelocity,
                    WallTurnSmoothTime,
                    WallFacingSpeed,
                    Time.fixedDeltaTime);
                float turnStep = Mathf.Max(0, angle - nextAngle);
                float yaw = WallTurnYaw;
                // Near 180° the shortest way is ambiguous; keep turning the planned way
                // (about vertical) until the remaining turn is unambiguous.
                if (PlannedWallTurnSign != 0 && Mathf.Abs(yaw) > 90 && Mathf.Sign(yaw) != PlannedWallTurnSign)
                    rotation = Quaternion.AngleAxis(PlannedWallTurnSign * turnStep, Vector3.up) * body.rotation;
                else
                    rotation = Quaternion.RotateTowards(body.rotation, targetRotation, turnStep);
            }
            else
            {
                wallTurnVelocity = 0;
            }
            motor.ApplyMetalMotion(velocity, rotation);
            return true;
        }
        return false;
    }
}
