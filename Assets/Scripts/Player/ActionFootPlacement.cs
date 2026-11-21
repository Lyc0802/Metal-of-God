using System.Linq;
using UnityEngine;

/// <summary>
/// 地形高度適應（Generic rig，動畫之後執行；不寫入 root）。
/// 只依「腳下地面」與「身體下方地面」的高度差調整每隻腳的高度，需要時骨盆下沉讓腳搆到地，
/// 腳掌順著斜坡傾斜。水平位置完全保留動畫、不鎖腳，因此不會出現拉扯或瞬移；
/// 防滑步由動畫播放速度（WalkRate）與移動速度的匹配處理。
/// </summary>
[DefaultExecutionOrder(20)]
public sealed class ActionFootPlacement : MonoBehaviour
{
    public Animator Animator;
    [Range(0, 1)] public float Weight = 1;
    [Tooltip("腳最多往上 / 往下調整的高度（世界單位）。")]
    [Min(0)] public float MaxStepUp = .35f, MaxStepDown = .35f;
    [Tooltip("腳高度修正的平滑時間（秒）。")]
    [Min(.001f)] public float FootSmoothTime = .06f;
    [Tooltip("骨盆下沉的平滑時間（秒）。")]
    [Min(.001f)] public float PelvisSmoothTime = .1f;
    [Tooltip("腳掌順著斜坡傾斜的程度。")]
    [Range(0, 1)] public float SlopeAlignWeight = .7f;
    [Range(0, 45)] public float MaxSlopeAngle = 25;
    [Tooltip("跳躍、攻擊等狀態切換時的淡入淡出時間（秒）。")]
    [Min(.01f)] public float BlendTime = .15f;

    private PlayerMovement movement;
    private PlayerCombat combat;
    private PlayerCharacterAnimation characterAnimation;
    private Collider body;
    private Transform hips;
    private readonly Transform[,] bones = new Transform[2, 3];
    private readonly float[] offset = new float[2], offsetVelocity = new float[2];
    private readonly Vector3[] normal = { Vector3.up, Vector3.up };
    private float pelvis, pelvisVelocity, blend;

    private void OnEnable()
    {
        movement = GetComponent<PlayerMovement>();
        combat = GetComponent<PlayerCombat>();
        characterAnimation = GetComponent<PlayerCharacterAnimation>();
        body = GetComponent<Collider>();
        if (!Animator) Animator = GetComponentInChildren<Animator>();
        if (!Animator) { enabled = false; return; }
        var all = Animator.GetComponentsInChildren<Transform>();
        hips = all.FirstOrDefault(t => t.name == "Hips");
        for (int i = 0; i < 2; i++)
            for (int j = 0; j < 3; j++)
                bones[i, j] = all.First(t => t.name == (i == 0 ? "Left" : "Right") + new[] { "UpperLeg", "LowerLeg", "Foot" }[j]);
        offset[0] = offset[1] = offsetVelocity[0] = offsetVelocity[1] = 0;
        normal[0] = normal[1] = Vector3.up;
        pelvis = pelvisVelocity = blend = 0;
    }

    private void LateUpdate()
    {
        if (!Animator) return;
        float dt = Time.deltaTime;
        bool active = movement.IsGrounded && !(combat && combat.IsAttacking) && !movement.IsPreparingJump
            && !(characterAnimation && characterAnimation.IsPlayingMetalAbility);
        // Never switch on/off in one frame: that snapped the legs.
        blend = Mathf.MoveTowards(blend, active ? Weight : 0, dt / BlendTime);

        // Ground under the body is the reference the animation was authored on.
        float baseY = body ? body.bounds.min.y : transform.position.y;
        var centre = body ? body.bounds.center : transform.position;
        bool rootFound = Ground(centre, baseY, out float rootY, out _);

        for (int i = 0; i < 2; i++)
        {
            float want = 0;
            var n = Vector3.up;
            if (blend > 0 && rootFound && Ground(bones[i, 2].position, baseY, out float y, out var hitNormal))
            {
                want = Mathf.Clamp(y - rootY, -MaxStepDown, MaxStepUp);
                n = hitNormal;
            }
            offset[i] = Mathf.SmoothDamp(offset[i], want, ref offsetVelocity[i], FootSmoothTime, Mathf.Infinity, dt);
            normal[i] = Vector3.Slerp(normal[i], n, 1 - Mathf.Exp(-dt / FootSmoothTime));
        }

        // Sink the pelvis so the lower foot can reach; never raise it above the animation.
        float wantPelvis = Mathf.Min(0, Mathf.Min(offset[0], offset[1]));
        pelvis = Mathf.SmoothDamp(pelvis, wantPelvis, ref pelvisVelocity, PelvisSmoothTime, Mathf.Infinity, dt);
        if (blend <= 0 && Mathf.Abs(offset[0]) < 1e-4f && Mathf.Abs(offset[1]) < 1e-4f && Mathf.Abs(pelvis) < 1e-4f) return;

        // Foot targets come from the animated pose, taken before the pelvis moves.
        var targets = new Vector3[2];
        var rotations = new Quaternion[2];
        for (int i = 0; i < 2; i++)
        {
            targets[i] = bones[i, 2].position + Vector3.up * offset[i] * blend;
            rotations[i] = bones[i, 2].rotation;
        }

        if (hips) hips.position += Vector3.up * pelvis * blend;
        for (int i = 0; i < 2; i++)
        {
            Solve(bones[i, 0], bones[i, 1], bones[i, 2], targets[i]);
            // Tilt the sole with the slope (capped), on top of the animated foot rotation.
            var tilt = Quaternion.FromToRotation(Vector3.up, normal[i]);
            float angle = Quaternion.Angle(Quaternion.identity, tilt);
            if (angle > MaxSlopeAngle) tilt = Quaternion.Slerp(Quaternion.identity, tilt, MaxSlopeAngle / angle);
            bones[i, 2].rotation = Quaternion.Slerp(Quaternion.identity, tilt, SlopeAlignWeight * blend) * rotations[i];
        }
    }

    // Ground height below 'point' (XZ), searched from MaxStepUp above to MaxStepDown below baseY.
    private bool Ground(Vector3 point, float baseY, out float height, out Vector3 groundNormal)
    {
        height = 0;
        groundNormal = Vector3.up;
        var origin = new Vector3(point.x, baseY + MaxStepUp, point.z);
        float best = float.PositiveInfinity;
        foreach (var hit in Physics.RaycastAll(origin, Vector3.down, MaxStepUp + MaxStepDown, movement.GroundMask, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.transform.IsChildOf(transform) || hit.normal.y <= .55f || hit.distance >= best) continue;
            best = hit.distance;
            height = hit.point.y;
            groundNormal = hit.normal;
        }

        return best < float.PositiveInfinity;
    }

    // Two-bone solve that keeps the knee in its animated plane and the foot's world rotation.
    private static void Solve(Transform upper, Transform lower, Transform end, Vector3 target)
    {
        var rotation = end.rotation;
        float a = Vector3.Distance(upper.position, lower.position), b = Vector3.Distance(lower.position, end.position);
        var axis = (target - upper.position).normalized;
        float d = Mathf.Clamp(Vector3.Distance(target, upper.position), .001f, a + b - .0001f);
        var bend = Vector3.ProjectOnPlane(lower.position - upper.position, axis).normalized;
        float along = (a * a + d * d - b * b) / (2 * d), height = Mathf.Sqrt(Mathf.Max(0, a * a - along * along));
        var knee = upper.position + axis * along + bend * height;
        upper.rotation = Quaternion.FromToRotation(lower.position - upper.position, knee - upper.position) * upper.rotation;
        lower.rotation = Quaternion.FromToRotation(end.position - lower.position, target - lower.position) * lower.rotation;
        end.rotation = rotation;
    }
}
