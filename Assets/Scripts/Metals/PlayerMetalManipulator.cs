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

    [Header("Fire Pulse (visual only)")]
    [Tooltip("推 / 拉的線特效播放時間（秒）。只是視覺回饋，施力不會等它。")]
    [Min(.01f)]
    public float PulseVisualTime = .08f;
    /// <summary>發出推 / 拉時觸發：目標、是否為拉、線特效播放時間（秒）。施力與此事件同時開始。</summary>
    public event System.Action<MetalSurface, bool, float> Fired;

    [Header("Carry & Launch")]
    [Tooltip("拉過來的輕金屬離玩家表面這麼近，就算「已經接觸」。")]
    [Min(0)]
    public float CarryContactDistance = 1.1f;
    [Tooltip("按住右鍵、物體持續接觸玩家這麼久之後，就進入攜帶狀態；此時往前滾輪（推）會把它沿瞄準方向射出。")]
    [Min(0)]
    public float CarryHoldTime = .2f;
    [Min(0)]
    public float LaunchSpeed = 38;
    /// <summary>瞄準用的相機（由 PlayerController 每幀設定）；沒設定就用主相機。</summary>
    public Camera AimCamera;
    /// <summary>累計射出次數（給動畫 / 音效判斷是否剛射出）。</summary>
    public int LaunchCount { get; private set; }

    [Tooltip("攜帶時物體懸浮的位置（相對玩家中心；x = 右、y = 上、z = 前，方向跟著鏡頭的水平朝向）。")]
    public Vector3 CarryShoulderOffset = new Vector3(.6f, .75f, .1f);
    private float carryTime;
    private Rigidbody carriedBody;
    private bool carryIgnoring;

    /// <summary>攜帶時物體懸浮的世界座標：玩家右肩上方，隨鏡頭水平朝向轉動。</summary>
    public Vector3 CarryPoint
    {
        get
        {
            var cam = AimCamera ? AimCamera : Camera.main;
            Vector3 right = cam ? Vector3.ProjectOnPlane(cam.transform.right, Vector3.up) : transform.right;
            Vector3 forward = cam ? Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up) : transform.forward;
            right = right.sqrMagnitude > .0001f ? right.normalized : transform.right;
            forward = forward.sqrMagnitude > .0001f ? forward.normalized : transform.forward;
            Vector3 center = body ? body.worldCenterOfMass : transform.position;
            return center + right * CarryShoulderOffset.x + Vector3.up * CarryShoulderOffset.y + forward * CarryShoulderOffset.z;
        }
    }

    /// <summary>攜帶期間忽略物體與玩家自己的碰撞（它懸浮在肩上、射出時也從那裡出發，不該撞到玩家）。</summary>
    private void SetCarryCollisionIgnored(Rigidbody metalBody, bool ignored)
    {
        if (ignored == carryIgnoring && carriedBody == metalBody)
        {
            return;
        }

        if (!ignored && !carriedBody)
        {
            carryIgnoring = false;
            return;
        }

        var target = ignored ? metalBody : carriedBody;
        if (target && !ignored && !target.GetComponent<WallBlade>())
        {
            // 攜帶時關掉了重力；結束後還給物體（飾刀由 WallBlade 自己管理飛行 / 重力）。
            target.useGravity = true;
        }

        if (target)
        {
            var mine = GetComponentsInChildren<Collider>();
            foreach (var a in target.GetComponentsInChildren<Collider>())
            {
                foreach (var b in mine)
                {
                    if (a && b && a != b)
                    {
                        Physics.IgnoreCollision(a, b, ignored);
                    }
                }
            }
        }

        carryIgnoring = ignored;
        carriedBody = ignored ? metalBody : null;
    }
    /// <summary>按住右鍵拉著輕金屬、並且它已經貼在身上一段時間：可以用推（滾輪向前）把它射出去。</summary>
    public bool IsCarrying => IsLocked && IsPull && Target.Valid && carryTime >= CarryHoldTime && Target.Metal.weight < Weight;

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

        // 攜帶中（按住右鍵、物體貼在身上一段時間）再往前滾輪（推）：把它沿瞄準方向射出去。
        if (IsLocked)
        {
            if (scroll > 0 && IsCarrying && inventory.CanUse(MetalType.Steel))
            {
                Launch();
            }

            return;
        }

        if (scroll == 0 || !selected.Valid || !InRange(selected))
        {
            return;
        }

        bool pull = scroll < 0;
        if (!inventory.CanUse(pull ? MetalType.Iron : MetalType.Steel))
        {
            return;
        }

        // 立刻施力，沒有任何延遲；線的特效只是同時播放的視覺回饋。
        Target = selected;
        IsPull = pull;
        IsLocked = true;
        carryTime = 0;
        float turn = WallTurnYaw;
        PlannedWallTurnSign = Mathf.Abs(turn) > 1 ? Mathf.Sign(turn) : 0;
        Fired?.Invoke(selected, pull, PulseVisualTime);
    }

    private bool InRange(MetalSurface surface)
    {
        return selector
            ? selector.IsPointInRange(surface.Point)
            : (surface.Point - body.worldCenterOfMass).sqrMagnitude <= 30f * 30f;
    }

    /// <summary>從 from 朝準心瞄準點的方向（準心射線打到的第一個東西；忽略玩家自己與被攜帶的金屬）。</summary>
    public Vector3 AimDirection(Vector3 from)
    {
        var cam = AimCamera ? AimCamera : Camera.main;
        if (!cam)
        {
            return transform.forward;
        }

        var ray = cam.ViewportPointToRay(new Vector3(.5f, .5f));
        float skip = Vector3.Distance(ray.origin, body ? body.worldCenterOfMass : transform.position);
        Vector3 point = ray.GetPoint(80f);
        var hits = Physics.RaycastAll(ray, 80f, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            // 相機到玩家之間的東西（背後的牆等）不算；玩家自己與被攜帶的金屬也不算。
            if (hit.distance < skip || hit.collider.transform.IsChildOf(transform)
                || (Target.Valid && hit.collider.transform.IsChildOf(Target.Metal.transform)))
            {
                continue;
            }

            point = hit.point;
            break;
        }

        Vector3 d = point - from;
        return d.sqrMagnitude > .01f ? d.normalized : ray.direction;
    }

    private void Launch()
    {
        var metal = Target.Metal;
        var metalBody = Target.Collider.attachedRigidbody;
        if (!metalBody)
        {
            metalBody = metal.GetComponentInParent<Rigidbody>();
        }

        if (!metalBody || metalBody.isKinematic)
        {
            return;
        }

        Vector3 velocity = AimDirection(metalBody.worldCenterOfMass) * LaunchSpeed;
        var blade = metalBody.GetComponent<WallBlade>();
        if (blade)
        {
            blade.Launch(velocity);
        }
        else
        {
            metalBody.WakeUp();
            metalBody.linearVelocity = velocity;
        }

        MetalImpactFeedback.MarkDriven(metalBody, metal);
        var prop = metalBody.GetComponent<LooseMetalProp>();
        if (prop)
        {
            // 剛射出時還貼在玩家身邊，不算撞到玩家。
            prop.ResetPlayerGrace();
            // 攜帶後投擲：傷害加成（飾刀特別高）。
            prop.MarkThrown();
        }

        LaunchCount++;
        Release();
    }

    public void Release()
    {
        carryTime = 0;
        if (carryIgnoring)
        {
            SetCarryCollisionIgnored(carriedBody, false);
        }

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

            bool dynamicBody = metalBody && !metalBody.isKinematic;
            if (IsCarrying && dynamicBody)
            {
                // 攜帶中：懸浮在玩家的右肩上（跟著鏡頭轉），不再貼在拉過來的方向；與玩家自己的碰撞暫時忽略。
                SetCarryCollisionIgnored(metalBody, true);
                Vector3 toShoulder = CarryPoint - metalBody.worldCenterOfMass;
                if (toShoulder.magnitude > 3f)
                {
                    // 離肩膀太遠（被擋住 / 被拉開）：放棄攜帶，重新拉過來。
                    carryTime = 0;
                    SetCarryCollisionIgnored(metalBody, false);
                }
                else
                {
                    metalBody.useGravity = false;
                    metalBody.linearVelocity = Vector3.ClampMagnitude(toShoulder / Time.fixedDeltaTime, 45f);
                    MetalImpactFeedback.MarkDriven(metalBody, Target.Metal);
                }
            }
            else
            {
                carryTime = IsPull && remaining <= CarryContactDistance ? carryTime + Time.fixedDeltaTime : 0;
                if (dynamicBody)
                {
                    metalBody.linearVelocity = Velocity(IsPull ? -toward : toward, remaining);
                    if (metalBody.linearVelocity.sqrMagnitude > .001f)
                    {
                        MetalImpactFeedback.MarkDriven(metalBody, Target.Metal);
                    }
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
