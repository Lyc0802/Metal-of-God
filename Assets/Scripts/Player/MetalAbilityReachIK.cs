using System.Collections.Generic;
using System.Linq;
using MetalOfGod.IK;
using UnityEngine;

/// <summary>
/// 使用指向性金屬能力（Iron/Steel 推拉、Zinc/Brass 情緒操控）時，
/// 上半身轉向目標（有角度上限），並以兩節 IK（Scripts/IK/TwoBoneIK）讓手伸向目標點。
/// Auto 模式在施放瞬間依目標在身體哪一側選手；每隻手只在自己那一側活動（可略微跨到身前，不會跨到背後）。
/// 動畫之後執行；只疊加在既有動畫上，能力結束後平滑淡出。
/// </summary>
[DefaultExecutionOrder(30)]
[RequireComponent(typeof(PlayerCharacterAnimation))]
public sealed class MetalAbilityReachIK : MonoBehaviour
{
    // Explicit values keep previously serialized Left/Right settings unchanged.
    public enum ReachHand { Left = 0, Right = 1, Auto = 2 }

    [Tooltip("Auto：依目標在身體左 / 右側自動選手。Left / Right：固定用角色自己的左 / 右手。")]
    public ReachHand HandMode = ReachHand.Auto; // new field name: old saved "Hand" values are ignored
    [Tooltip("手臂舉起 / 放下的過渡時間。")]
    [Min(.01f)] public float HandSwitchTime = .2f;
    [Tooltip("在 Console 印出每次施放 / 換手時使用的是哪隻手。")]
    public bool LogHandChoice = true;
    [Tooltip("能力結束後這段時間內，對同一個目標再次施放會沿用上一次的手（避免重新施放時換手）。")]
    [Min(0)] public float HandMemoryTime = 1.5f;

    [Header("Blend")]
    [Min(.01f)] public float BlendInTime = .12f;
    [Min(.01f)] public float BlendOutTime = .25f;
    [Tooltip("能力結束後維持伸手姿勢的秒數。")]
    [Min(0)] public float LingerTime = .25f;

    [Header("Upper body (degrees, relative to body forward)")]
    [Range(0, 90)] public float MaxTorsoYaw = 60;
    [Range(0, 60)] public float MaxTorsoPitch = 20;
    [Range(0, 60)] public float MaxHeadYaw = 35;
    [Range(0, 60)] public float MaxHeadPitch = 30;

    [Header("Arm")]
    [Tooltip("手伸出的長度（手臂全長的比例）。1 = 完全伸直。")]
    [Range(.5f, 1)] public float ReachExtension = .99f;
    [Tooltip("手臂方向相對身體正前方的最大角度。")]
    [Range(30, 180)] public float MaxReachAngle = 150;
    [Tooltip("手臂方向相對水平面的最大上下角度，避免指向正上 / 正下時手臂折疊。")]
    [Range(10, 89)] public float MaxReachElevation = 70;

    [Tooltip("手臂最多可跨過身體中線、伸到另一側身前幾度（不會跨到背後）。")]
    [Range(0, 90)] public float CrossBodyReach = 45;

    [Header("Stability")]
    [Tooltip("目標在正後方時，軀幹 / 頭偏向使用中那隻手一側的角度（決定往哪邊轉身）。")]
    [Range(0, 90)] public float HandSideBias = 30;
    [Tooltip("軀幹 / 頭 / 手臂方向每秒最多轉幾度。")]
    [Min(30)] public float MaxTurnSpeed = 360;
    [Tooltip("在 Console 印出每次能力結束（手開始收回）的原因。")]
    public bool LogActivation = true;

    [Header("Pull arrival")]
    [Tooltip("關閉（預設）：黏在牆上時手持續指向目標（手掌朝牆）。開啟：接近牆面時逐漸收手。")]
    public bool FadeOnPullArrival = false;
    [Tooltip("FadeOnPullArrival 開啟時，距離小於 WallTurnDistance × 此比例就完全收手。")]
    [Range(0, 1)] public float PullReleaseFraction = .4f;

    [Header("Wall hang breathing")]
    [Tooltip("黏在牆上時的輕微喘氣。0 = 關閉。")]
    [Range(0, 1)] public float BreathWeight = 1;
    [Tooltip("每秒呼吸次數（喘氣略快於平常呼吸）。")]
    [Range(.1f, 2)] public float BreathRate = .75f;
    [Tooltip("吸氣時胸口抬起的角度。")]
    [Range(0, 10)] public float ChestBreath = 2.5f;
    [Tooltip("吸氣時肩膀聳起的角度。")]
    [Range(0, 10)] public float ShoulderBreath = 2;
    [Min(.01f)] public float BreathBlendTime = .5f;

    [Header("Hand")]
    [Tooltip("手掌面向目標的程度。")]
    [Range(0, 1)] public float PalmFacingWeight = 1;
    [Tooltip("手腕為了讓手掌面向目標，最多額外轉幾度。")]
    [Range(0, 180)] public float MaxWristAngle = 90;
    [Tooltip("手掌轉向時，前臂沿自身軸向最多扭轉幾度（旋前 / 旋後）。")]
    [Range(0, 180)] public float MaxForearmTwist = 110;
    [Tooltip("每秒在 Console 印一次手掌與目標的角度誤差。")]
    public bool LogPalm = true;
    [Range(0, 1)] public float OpenHandWeight = .8f;

    public float Weight { get; private set; }
    public bool HasTarget { get; private set; }
    public Vector3 TargetPoint { get; private set; }
    /// <summary>目前主要伸出的手（角色自己的左右）。</summary>
    public ReachHand ActiveHand => rightActive ? ReachHand.Right : ReachHand.Left;

    private static readonly string[] TorsoChain = { "Spine", "Chest", "UpperChest" };
    private static readonly float[] TorsoShare = { .3f, .35f, .35f };
    private static readonly string[] FingerNames = { "Thumb", "Index", "Middle", "Ring", "Little" };

    // One reaching arm. Index 0 = character's left, 1 = character's right.
    private sealed class Arm
    {
        public Transform Shoulder, Upper, Fore, Hand, Knuckle, Tip;
        // Palm normal and finger direction in hand-local space, measured from the animated curl.
        public Vector3 PalmLocal, FingerLocal;
        public bool PalmReady;
        public TwoBoneIK Limb;
        public readonly List<Transform> Fingers = new List<Transform>();
        public readonly List<Quaternion> OpenFingers = new List<Quaternion>();
        public float Blend, YawState, ElevationState;
        public bool Ready, AngleReady;
    }

    private PlayerCharacterAnimation characterAnimation;
    private PlayerMetalManipulator metalManipulator;
    private PlayerEnemyManipulator enemyManipulator;
    private PlayerCombat combat;
    private PlayerHealth health;
    private PlayerMetalInventory inventory;
    private PlayerMetalSelector selector;

    private Transform visual, chest, neck, head;
    private Transform[] torso;
    private readonly Arm[] arms = new Arm[2];
    private float lingerUntil, reachStrength, nextPalmLog, breathBlend, breathPhase;
    private bool valid, rightActive;
    // Rate-limited angles (degrees) for continuity.
    private float torsoYawState, torsoPitchState, headYawState, headPitchState;
    private bool stateReady, targetSeen;
    // What is being targeted (metal collider / enemy) and how, cached while the ability is live,
    // because the manipulators clear their state the moment the ability ends.
    private Object currentTargetKey, lastTargetKey;
    private MetalType currentMetal = MetalType.None;
    private bool lastHandRight;
    private float lastEndTime = float.NegativeInfinity;
    private string handRule = "";
    private float HandSign => rightActive ? 1 : -1;

    private void Awake()
    {
        characterAnimation = GetComponent<PlayerCharacterAnimation>();
        metalManipulator = GetComponent<PlayerMetalManipulator>();
        enemyManipulator = GetComponent<PlayerEnemyManipulator>();
        combat = GetComponent<PlayerCombat>();
        health = GetComponent<PlayerHealth>();
        inventory = GetComponent<PlayerMetalInventory>();
        selector = GetComponent<PlayerMetalSelector>();
        var animator = characterAnimation.CharacterAnimator ? characterAnimation.CharacterAnimator : GetComponentInChildren<Animator>();
        if (!animator) return;
        visual = animator.transform;
        var bones = visual.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
        bones.TryGetValue("UpperChest", out chest);
        bones.TryGetValue("Neck", out neck);
        bones.TryGetValue("Head", out head);
        torso = TorsoChain.Where(bones.ContainsKey).Select(n => bones[n]).ToArray();
        valid = chest && head && torso.Length == TorsoChain.Length && BuildArms(bones);
        if (!valid) Debug.LogWarning("MetalAbilityReachIK: required bones not found under " + visual.name, this);
        rightActive = HandMode != ReachHand.Left;
    }

    // This rig's bone names are mirrored, so each arm is classified by its real side of the body.
    // Awake runs before the Animator's first write, so finger rotations here are the bind (open) pose.
    private bool BuildArms(Dictionary<string, Transform> bones)
    {
        if (!bones.TryGetValue("LeftUpperArm", out var a) || !bones.TryGetValue("RightUpperArm", out var b)) return false;
        bool namedLeftIsRight = Vector3.Dot(a.position - b.position, visual.right) > 0;
        for (int i = 0; i < 2; i++)
        {
            bool right = i == 1;
            string prefix = right == namedLeftIsRight ? "Left" : "Right";
            var arm = new Arm();
            bones.TryGetValue(prefix + "Shoulder", out arm.Shoulder);
            bones.TryGetValue(prefix + "UpperArm", out arm.Upper);
            bones.TryGetValue(prefix + "LowerArm", out arm.Fore);
            bones.TryGetValue(prefix + "Hand", out arm.Hand);
            bones.TryGetValue(prefix + "MiddleProximal", out arm.Knuckle);
            bones.TryGetValue(prefix + "MiddleTip", out arm.Tip);
            if (!arm.Upper || !arm.Fore || !arm.Hand) return false;
            foreach (var t in arm.Hand.GetComponentsInChildren<Transform>(true))
            {
                if (t == arm.Hand || t.name.EndsWith("Tip") || !FingerNames.Any(t.name.Contains)) continue;
                arm.Fingers.Add(t);
                arm.OpenFingers.Add(t.localRotation);
            }

            // The bend goal decides the elbow plane; the hand keeps its local rotation.
            arm.Limb = new TwoBoneIK();
            arm.Limb.SetChain(arm.Upper, arm.Fore, arm.Hand);
            arms[i] = arm;
        }

        return true;
    }

    // strength: 1 = full reach. A pull fades out on arrival, where the manipulator turns the
    // player to face away from the surface and holds them there (the target is then behind).
    private bool TryGetAbilityTarget(out Vector3 point, out float strength)
    {
        strength = 1;
        if (metalManipulator && metalManipulator.IsLocked && metalManipulator.Target.Valid)
        {
            point = metalManipulator.Target.Point;
            currentTargetKey = metalManipulator.Target.Collider;
            currentMetal = metalManipulator.IsPull ? MetalType.Iron : MetalType.Steel;
            if (metalManipulator.IsPull && FadeOnPullArrival)
            {
                float far = Mathf.Max(.01f, metalManipulator.WallTurnDistance);
                float distance = Vector3.Distance(point, chest.position);
                strength = Mathf.InverseLerp(far * PullReleaseFraction, far, distance);
            }
            return true;
        }

        if (enemyManipulator && enemyManipulator.HasActivatedThisHold && enemyManipulator.Target
            && !enemyManipulator.Target.IsDead)
        {
            point = enemyManipulator.Target.TargetPoint;
            currentTargetKey = enemyManipulator.Target;
            currentMetal = MetalType.None;
            return true;
        }

        point = default;
        return false;
    }

    // Palm side = where the animated, slightly curled middle finger bends toward.
    private static void CalibratePalm(Arm arm)
    {
        if (arm.PalmReady || !arm.Knuckle || !arm.Tip) return;
        var finger = arm.Knuckle.position - arm.Hand.position;
        var curl = arm.Tip.position - arm.Knuckle.position;
        if (finger.sqrMagnitude < 1e-10f || curl.sqrMagnitude < 1e-10f) return;
        finger.Normalize();
        var palm = curl - finger * Vector3.Dot(curl, finger);
        if (palm.magnitude < curl.magnitude * .35f) return; // needs a clear curl (baked clips: relaxed hands)
        var inverse = Quaternion.Inverse(arm.Hand.rotation);
        arm.PalmLocal = inverse * palm.normalized;
        arm.FingerLocal = inverse * finger;
        arm.PalmReady = true;
    }

    private void LateUpdate()
    {
        if (!valid) return;
        // Measure the palm side on the animated pose, before this script changes anything.
        foreach (var arm in arms) CalibratePalm(arm);
        Breathe();

        bool blocked = (combat && combat.IsAttacking) || (health && health.IsDead);
        bool found = TryGetAbilityTarget(out var point, out float strength);
        if (!blocked && found)
        {
            TargetPoint = point;
            HasTarget = true;
            reachStrength = strength;
            lingerUntil = Time.time + LingerTime;
        }

        if (LogActivation && targetSeen && (blocked || !found))
            Debug.Log($"[MetalAbilityReachIK] ability ended → hand lowering ({EndReason(blocked)})", this);
        targetSeen = !blocked && found;

        bool active = !blocked && HasTarget && Time.time < lingerUntil;
        float goal = active ? reachStrength : 0;
        Weight = Mathf.MoveTowards(Weight, goal, Time.deltaTime / (goal > Weight ? BlendInTime : BlendOutTime));
        if (Weight <= 0)
        {
            HasTarget = false;
            if (stateReady)
            {
                // Remember which hand served this target, for a quick re-activation.
                lastHandRight = rightActive;
                lastTargetKey = currentTargetKey;
                lastEndTime = Time.time;
            }
            stateReady = false;
            foreach (var arm in arms) { arm.Blend = 0; arm.AngleReady = false; }
            return;
        }

        float w = Smooth(Weight);
        float step = MaxTurnSpeed * Time.deltaTime;
        var up = Vector3.up;
        var forward = Vector3.ProjectOnPlane(visual.forward, up).normalized;

        // 1) Torso: yaw toward the target (clamped), small lean up/down, spread over the spine.
        Angles(chest.position, forward, out float yaw, out float pitch);
        ChooseHand(yaw);
        // Wrap around a centre biased to the active hand: a target straight behind turns the
        // torso toward that hand (so it can reach back), never toward the other side.
        yaw = Unwrap(yaw, HandSign * HandSideBias);
        float wantTorsoYaw = Mathf.Clamp(yaw, -MaxTorsoYaw, MaxTorsoYaw);
        float wantTorsoPitch = Mathf.Clamp(pitch, -MaxTorsoPitch, MaxTorsoPitch);
        if (!stateReady)
        {
            torsoYawState = wantTorsoYaw;
            torsoPitchState = wantTorsoPitch;
        }
        torsoYawState = Mathf.MoveTowards(torsoYawState, wantTorsoYaw, step);
        torsoPitchState = Mathf.MoveTowards(torsoPitchState, wantTorsoPitch, step);
        float torsoYaw = torsoYawState * w;
        float torsoPitch = torsoPitchState * w;
        var facing = Quaternion.AngleAxis(torsoYaw, up) * forward;
        var pitchAxis = Vector3.Cross(up, facing);
        for (int i = 0; i < torso.Length; i++)
        {
            torso[i].rotation = Quaternion.AngleAxis(torsoYaw * TorsoShare[i], up) * torso[i].rotation;
            // Positive rotation about the facing-right axis tips forward (down): pitch up = negative.
            torso[i].rotation = Quaternion.AngleAxis(-torsoPitch * TorsoShare[i], pitchAxis) * torso[i].rotation;
        }

        // 2) Head looks the rest of the way (clamped), split neck / head.
        Angles(head.position, facing, out float headYaw, out float headPitch);
        headYaw = Unwrap(headYaw, HandSign * HandSideBias);
        float wantHeadYaw = Mathf.Clamp(headYaw, -MaxHeadYaw, MaxHeadYaw);
        float wantHeadPitch = Mathf.Clamp(headPitch - torsoPitch, -MaxHeadPitch, MaxHeadPitch);
        if (!stateReady)
        {
            headYawState = wantHeadYaw;
            headPitchState = wantHeadPitch;
        }
        headYawState = Mathf.MoveTowards(headYawState, wantHeadYaw, step);
        headPitchState = Mathf.MoveTowards(headPitchState, wantHeadPitch, step);
        headYaw = headYawState * w;
        headPitch = headPitchState * w;
        var headAxis = Vector3.Cross(up, Quaternion.AngleAxis(headYaw, up) * facing);
        foreach (var (bone, share) in new[] { (neck, .4f), (head, .6f) })
        {
            if (!bone) continue;
            bone.rotation = Quaternion.AngleAxis(headYaw * share, up) * bone.rotation;
            bone.rotation = Quaternion.AngleAxis(-headPitch * share, headAxis) * bone.rotation;
        }
        stateReady = true;

        // 3) Arms: the chosen hand reaches; on a switch the other one lowers smoothly.
        float handStep = Time.deltaTime / HandSwitchTime;
        for (int i = 0; i < 2; i++)
        {
            var arm = arms[i];
            bool chosen = (i == 1) == rightActive;
            arm.Blend = Mathf.MoveTowards(arm.Blend, chosen ? 1 : 0, handStep);
            float armWeight = w * Smooth(arm.Blend);
            if (armWeight <= 0) { arm.AngleReady = false; continue; }
            if (!SolveArm(arm, armWeight, forward, step)) { valid = false; return; }
        }
    }

    // Subtle panting while held on a wall: the held jump pose is otherwise completely still.
    // Applied before the reach, so the arm IK starts from the breathing shoulders.
    private void Breathe()
    {
        bool hanging = metalManipulator && metalManipulator.IsHoldingSurface && !(health && health.IsDead);
        breathBlend = Mathf.MoveTowards(breathBlend, hanging ? BreathWeight : 0, Time.deltaTime / BreathBlendTime);
        if (breathBlend <= 0) { breathPhase = 0; return; }

        breathPhase = Mathf.Repeat(breathPhase + Time.deltaTime * BreathRate, 1);
        // Quicker inhale, slower exhale: 0 at rest, 1 at full inhale.
        float inhale = breathPhase < .4f ? Smooth(breathPhase / .4f) : 1 - Smooth((breathPhase - .4f) / .6f);
        float amount = inhale * Smooth(breathBlend);

        var up = Vector3.up;
        var forward = Vector3.ProjectOnPlane(visual.forward, up).normalized;
        var right = Vector3.Cross(up, forward); // positive rotation about it tips forward
        // Chest lifts and opens (tips back), shared over Chest / UpperChest; the head stays level.
        float chestAngle = -ChestBreath * amount;
        torso[1].rotation = Quaternion.AngleAxis(chestAngle * .4f, right) * torso[1].rotation;
        torso[2].rotation = Quaternion.AngleAxis(chestAngle * .6f, right) * torso[2].rotation;
        if (neck) neck.rotation = Quaternion.AngleAxis(-chestAngle * .7f, right) * neck.rotation;

        // Shoulders rise: rotate each about the forward axis toward up on its own side.
        foreach (var arm in arms)
        {
            if (!arm.Shoulder) continue;
            float outward = Mathf.Sign(Vector3.Dot(arm.Upper.position - chest.position, visual.right));
            if (outward == 0) continue;
            arm.Shoulder.rotation = Quaternion.AngleAxis(ShoulderBreath * amount, forward * outward) * arm.Shoulder.rotation;
        }
    }

    private void ChooseHand(float yaw)
    {
        bool before = rightActive;
        PickHand(yaw);
        if (LogHandChoice && (!stateReady || before != rightActive))
        {
            var arm = arms[rightActive ? 1 : 0];
            Debug.Log($"[MetalAbilityReachIK] {(stateReady ? "Switch to" : "Use")} {(rightActive ? "RIGHT" : "LEFT")} hand " +
                      $"(bone {arm.Hand.name}, mode {HandMode}, rule {handRule}, target yaw {yaw:F0}°)", this);
        }
    }

    private void PickHand(float yaw)
    {
        handRule = "fixed";
        if (HandMode != ReachHand.Auto) { rightActive = HandMode == ReachHand.Right; return; }
        // The hand is decided once, at the moment of activation, and kept until it ends.
        if (stateReady) return;

        // Re-activating on the same target shortly after it ended keeps the same hand.
        if (currentTargetKey && currentTargetKey == lastTargetKey && Time.time - lastEndTime <= HandMemoryTime)
        {
            rightActive = lastHandRight;
            handRule = $"kept from previous activation ({Time.time - lastEndTime:F2}s ago)";
            return;
        }

        // A pull that moves the player ends with the player turning to face away from the
        // wall (PlayerMetalManipulator, same rotation and shortest-way turn). Turning right
        // sweeps the wall past the left side to behind-left, so the left hand reaches it.
        if (metalManipulator && metalManipulator.IsPull && metalManipulator.MovesPlayer)
        {
            // Same value the manipulator locked in for its turn, so hand and turn always agree.
            float turnSign = metalManipulator.PlannedWallTurnSign;
            if (turnSign != 0)
            {
                rightActive = turnSign < 0;
                handRule = $"wall turn {(turnSign > 0 ? "right" : "left")} ({metalManipulator.WallTurnYaw:F0}°)";
                return;
            }
        }

        rightActive = yaw >= 0;
        handRule = "target side";
    }

    private bool SolveArm(Arm arm, float weight, Vector3 forward, float step)
    {
        if (!arm.Ready)
        {
            arm.Ready = arm.Limb.Initiated;
            if (!arm.Ready) return false;
        }

        var up = Vector3.up;
        var shoulder = arm.Upper.position;
        float armLength = Vector3.Distance(arm.Upper.position, arm.Fore.position) + Vector3.Distance(arm.Fore.position, arm.Hand.position);
        ReachAngles(arm == arms[1] ? 1 : -1, (TargetPoint - shoulder).normalized, forward, out float wantYaw, out float wantElevation);
        if (!arm.AngleReady) { arm.YawState = wantYaw; arm.ElevationState = wantElevation; arm.AngleReady = true; }
        // Scalar smoothing: a large change sweeps through the front of the body, never the back.
        arm.YawState = Mathf.MoveTowards(arm.YawState, wantYaw, step);
        arm.ElevationState = Mathf.MoveTowards(arm.ElevationState, wantElevation, step);
        var direction = Direction(forward, arm.YawState, arm.ElevationState);
        float outwardSign = Mathf.Sign(Vector3.Dot(shoulder - chest.position, visual.right));
        var outward = visual.right * (outwardSign == 0 ? 1 : outwardSign);

        // Elbow goal is always perpendicular to the reach, preferring out and down, so it
        // can never line up with the arm (which flipped/folded the elbow on high/low targets).
        var bend = Vector3.ProjectOnPlane(outward - up * .5f - forward * .2f, direction);
        if (bend.sqrMagnitude < 1e-4f) bend = Vector3.ProjectOnPlane(outward, direction);
        arm.Limb.IKPosition = shoulder + direction * armLength * ReachExtension;
        arm.Limb.IKPositionWeight = weight;
        arm.Limb.SetBendGoalPosition(shoulder + direction * armLength * .5f + bend.normalized * armLength, 1);
        arm.Limb.Update();

        // Palm faces the target, fingers pointing up (a "push / grab" hand), wrist turn capped.
        if (arm.PalmReady && PalmFacingWeight > 0)
        {
            var toTarget = TargetPoint - arm.Hand.position;
            if (toTarget.sqrMagnitude > 1e-8f)
            {
                toTarget.Normalize();
                var fingersUp = Vector3.ProjectOnPlane(up, toTarget);
                if (fingersUp.sqrMagnitude < 1e-4f) fingersUp = Vector3.ProjectOnPlane(forward, toTarget);
                var localFrame = Quaternion.LookRotation(arm.PalmLocal, Vector3.ProjectOnPlane(arm.FingerLocal, arm.PalmLocal));
                var wanted = Quaternion.LookRotation(toTarget, fingersUp) * Quaternion.Inverse(localFrame);
                float amount = weight * PalmFacingWeight;

                // a) Twist about the forearm's long axis goes to the forearm (pronation /
                //    supination). Rolling about its own axis keeps the elbow and hand in place.
                var axis = (arm.Hand.position - arm.Fore.position).normalized;
                var delta = wanted * Quaternion.Inverse(arm.Hand.rotation);
                if (delta.w < 0) delta = new Quaternion(-delta.x, -delta.y, -delta.z, -delta.w);
                var along = axis * Vector3.Dot(new Vector3(delta.x, delta.y, delta.z), axis);
                var twist = new Quaternion(along.x, along.y, along.z, delta.w);
                float twistLength = Mathf.Sqrt(twist.x * twist.x + twist.y * twist.y + twist.z * twist.z + twist.w * twist.w);
                if (twistLength > 1e-6f)
                {
                    twist = new Quaternion(twist.x / twistLength, twist.y / twistLength, twist.z / twistLength, twist.w / twistLength);
                    twist.ToAngleAxis(out float twistAngle, out var twistAxis);
                    if (twistAngle > 180) twistAngle -= 360;
                    twistAngle = Mathf.Clamp(twistAngle, -MaxForearmTwist, MaxForearmTwist) * amount;
                    arm.Fore.rotation = Quaternion.AngleAxis(twistAngle, twistAxis) * arm.Fore.rotation;
                }

                // b) What remains is wrist bend, capped.
                float angle = Quaternion.Angle(arm.Hand.rotation, wanted);
                float limit = angle > MaxWristAngle ? MaxWristAngle / angle : 1;
                arm.Hand.rotation = Quaternion.Slerp(arm.Hand.rotation, wanted, amount * limit);

                if (LogPalm && arm.Blend >= 1 && Time.time >= nextPalmLog)
                {
                    nextPalmLog = Time.time + 1;
                    float error = Vector3.Angle(arm.Hand.rotation * arm.PalmLocal, toTarget);
                    Debug.Log($"[MetalAbilityReachIK] {(arm == arms[1] ? "RIGHT" : "LEFT")} hand (bone {arm.Hand.name}) palm→target error {error:F0}° " +
                              $"(needed wrist {angle:F0}°, cap {MaxWristAngle:F0}°, weight {amount:F2})", this);
                }
            }
        }
        else if (LogPalm && PalmFacingWeight > 0 && Time.time >= nextPalmLog)
        {
            nextPalmLog = Time.time + 1;
            Debug.LogWarning($"[MetalAbilityReachIK] {(arm == arms[1] ? "RIGHT" : "LEFT")} hand (bone {arm.Hand.name}): palm side not measured yet " +
                             $"(Tip bone {(arm.Tip ? "found" : "MISSING")}); palm facing skipped.", this);
        }

        float open = weight * OpenHandWeight;
        for (int i = 0; i < arm.Fingers.Count; i++)
            arm.Fingers[i].localRotation = Quaternion.Slerp(arm.Fingers[i].localRotation, arm.OpenFingers[i], open);
        return true;
    }

    // Signed yaw (right = +) and pitch (up = +) from 'origin' to the target, relative to 'forward'.
    private void Angles(Vector3 origin, Vector3 forward, out float yaw, out float pitch)
    {
        var to = TargetPoint - origin;
        var flat = Vector3.ProjectOnPlane(to, Vector3.up);
        yaw = flat.sqrMagnitude > 1e-6f ? Vector3.SignedAngle(forward, flat, Vector3.up) : 0;
        pitch = Mathf.Atan2(to.y, Mathf.Max(flat.magnitude, 1e-4f)) * Mathf.Rad2Deg;
    }

    // Express yaw in the 360° window centred on 'centre' (centre ± 180).
    private static float Unwrap(float yaw, float centre) => centre + Mathf.DeltaAngle(centre, yaw);

    // Each arm works on its own side: from MaxReachAngle on its side (reaching back) to
    // CrossBodyReach across the front. A target outside goes to the anatomically nearer limit,
    // e.g. a target behind the opposite shoulder is reached behind on the arm's own side.
    private void ReachAngles(float side, Vector3 direction, Vector3 forward, out float yaw, out float elevation)
    {
        var flat = Vector3.ProjectOnPlane(direction, Vector3.up);
        if (flat.sqrMagnitude < 1e-6f) flat = forward;
        float raw = Vector3.SignedAngle(forward, flat, Vector3.up);
        float own = side * MaxReachAngle, cross = -side * CrossBodyReach;
        float low = Mathf.Min(own, cross), high = Mathf.Max(own, cross);
        yaw = Mathf.Clamp(Unwrap(raw, (low + high) * .5f), low, high);
        elevation = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(direction.y, -1, 1)) * Mathf.Rad2Deg, -MaxReachElevation, MaxReachElevation);
    }

    private static Vector3 Direction(Vector3 forward, float yaw, float elevation)
    {
        var horizontal = Quaternion.AngleAxis(yaw, Vector3.up) * forward;
        return (horizontal * Mathf.Cos(elevation * Mathf.Deg2Rad) + Vector3.up * Mathf.Sin(elevation * Mathf.Deg2Rad)).normalized;
    }

    private string EndReason(bool blocked)
    {
        if (combat && combat.IsAttacking) return "⑤ attacking";
        if (health && health.IsDead) return "⑥ dead";
        if (currentMetal != MetalType.None && inventory && !inventory.CanUse(currentMetal)) return $"① out of {currentMetal} (or disabled)";
        if (selector && !selector.IsPointInRange(TargetPoint)) return "② target out of range";
        if (!currentTargetKey) return "③ target destroyed";
        if (currentTargetKey is Collider c && (!c.enabled || !c.gameObject.activeInHierarchy)) return "③ target disabled";
        if (currentTargetKey is Enemy e && e.IsDead) return "⑥ enemy died";
        return "④ ability button released (also: metal wheel opened / mouse uncaptured / focus lost)";
    }

    private static float Smooth(float t) => t * t * (3 - 2 * t);

    private void OnDisable()
    {
        Weight = 0;
        HasTarget = false;
        stateReady = false;
    }
}
