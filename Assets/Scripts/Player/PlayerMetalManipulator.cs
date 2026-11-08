using UnityEngine;

/// <summary>
/// 處理 Iron 拉與 Steel 推的物理作用、重量差異及釋放後的慣性。
/// </summary>
[RequireComponent(typeof(Rigidbody), typeof(PlayerMetalInventory))]
public class PlayerMetalManipulator : MonoBehaviour
{
    [Min(.01f)]
    public float Weight = 80;

    [Min(0)]
    public float PushSpeed = 20, PullSpeed = 20;
    [Range(0, 1)]
    public float ReleaseSpeedMultiplier = .5f;
    private bool releaseBrakePending;
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
    public bool IsPull { get; private set; }
    public MetalSurface Target { get; private set; }

    private Rigidbody body;
    private PlayerMetalInventory inventory;
    private PlayerMetalSelector selector;
    private bool controllingPlayer, originalGravity;
    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        inventory = GetComponent<PlayerMetalInventory>();
        selector = GetComponent<PlayerMetalSelector>();
    }

    public void HandleInput(bool held, float scroll, MetalSurface selected)
    {
        if (!held)
        {
            if (controllingPlayer)
            {
                releaseBrakePending = true;
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
    }

    private bool InRange(MetalSurface surface)
    {
        return selector
            ? selector.IsPointInRange(surface.Point)
            : (surface.Point - body.worldCenterOfMass).sqrMagnitude <= 30f * 30f;
    }

    private void RestoreGravity()
    {
        if (controllingPlayer && body)
        {
            body.useGravity = originalGravity;
            var movement = GetComponent<PlayerMovement>();
            if (movement)
            {
                movement.PreserveExternalMomentum();
            }
        }

        controllingPlayer = false;
    }

    public void Release()
    {
        RestoreGravity();
        wallTurnVelocity = 0;
        IsLocked = false;
        Target = default;
    }

    private void OnDisable()
    {
        Release();
        releaseBrakePending = false;
    }

    private // Account for the player volume: its center cannot reach a solid wall's surface.
 float PlayerExtent(Vector3 direction)
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

    private void FixedUpdate()
    {
        if (releaseBrakePending)
        {
            releaseBrakePending = false;
            body.linearVelocity *= Mathf.Clamp01(ReleaseSpeedMultiplier);
        }

        if (!IsLocked)
        {
            return;
        }

        if (!Target.Valid || !InRange(Target) || !inventory.CanUse(IsPull ? MetalType.Iron : MetalType.Steel))
        {
            Release();
            return;
        }

        float metalWeight = Target.Metal.weight;
        float playerWeight = Weight;
        if (Mathf.Approximately(playerWeight, metalWeight))
        {
            RestoreGravity();
            return;
        }

        Vector3 delta = Target.Point - body.worldCenterOfMass;
        Vector3 toward = delta.sqrMagnitude > .000001f ? delta.normalized : -Target.Normal;
        float remaining = delta.magnitude - PlayerExtent(toward) - ArrivalDistance;
        if (playerWeight > metalWeight)
        {
            RestoreGravity();
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
            if (!controllingPlayer)
            {
                originalGravity = body.useGravity;
                body.angularVelocity = Vector3.zero;
                controllingPlayer = true;
            }

            body.useGravity = false;
            // While locked, hold the arrival position through zero velocity and natural collision.
            bool arrived = IsPull && remaining <= ArrivalDistance;
            body.linearVelocity = arrived ? Vector3.zero : Velocity(IsPull ? toward : -toward, remaining);
            if (IsPull && remaining <= WallTurnDistance)
            {
                // Face outward along the selected surface normal.
                var forward = Target.Normal;
                var up = Mathf.Abs(Vector3.Dot(forward, Vector3.up)) > .99f ? Vector3.forward : Vector3.up;
                var targetRotation = Quaternion.LookRotation(forward, up);
                float angle = Quaternion.Angle(body.rotation, targetRotation);
                // A damped angular response accelerates into the turn and eases into alignment.
                float nextAngle = Mathf.SmoothDamp(
                    angle,
                    0,
                    ref wallTurnVelocity,
                    WallTurnSmoothTime,
                    WallFacingSpeed,
                    Time.fixedDeltaTime);
                body.MoveRotation(Quaternion.RotateTowards(body.rotation, targetRotation, Mathf.Max(0, angle - nextAngle)));
            }
            else
            {
                wallTurnVelocity = 0;
            }
        }
    }
}
