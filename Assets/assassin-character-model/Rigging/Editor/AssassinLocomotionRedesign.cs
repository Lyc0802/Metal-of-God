using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Redesigned 8-way walk/run. Every direction is derived from ONE forward capture per
// gait (Standard Walk 1 / Running), so all clips share cadence and foot-contact timing
// and blend cleanly in the FreeformDirectional2D trees of ActionLocomotion.controller.
public static partial class AssassinMotionAuthoring
{
    private const string RedesignWalkSource = "Assets/assassin-character-model/Standard Walk 1.fbx";
    private const string RedesignRunSource = "Assets/assassin-character-model/Running.fbx";
    private static readonly string[] GaitSides = { "Left", "Right" };
    private static readonly string[] TargetFingers = { "Thumb", "Index", "Middle", "Ring", "Little" };
    private static readonly string[] SourceFingers = { "Thumb", "Index", "Middle", "Ring", "Pinky" };
    private static readonly string[] TargetFingerJoints = { "Proximal", "Intermediate", "Distal", "Tip" };

    sealed class GaitStats
    {
        public Quaternion[] Neutral;
        public readonly Dictionary<string, Vector3> FootMean = new Dictionary<string, Vector3>();
        public readonly Dictionary<string, Vector2> AnkleRange = new Dictionary<string, Vector2>();
        public readonly Dictionary<string, float> BindPitch = new Dictionary<string, float>();
        public float StanceSpeed;
        // Per foot: swing window as (lift-off phase, swing length), both in cycle fractions.
        public readonly Dictionary<string, Vector2> Swing = new Dictionary<string, Vector2>();
        // Cycle-average orientation (relative to the root) of the stabilised bones.
        public readonly Dictionary<string, Quaternion> SteadyRotation = new Dictionary<string, Quaternion>();
    }

    private static readonly string[] SteadyBones = { "Neck", "Head" };

    private static float ToePitch(Rig r, string side)
    {
        var d = r.Root.InverseTransformDirection(r.B(side + "Toes").position - r.B(side + "Foot").position);
        return Mathf.Atan2(-d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
    }

    private static GaitStats AnalyseGait(Rig r, ReferenceMotion source, bool run)
    {
        const int samples = 60;
        var stats = new GaitStats();
        r.Reset();
        foreach (string side in GaitSides)
        {
            stats.BindPitch[side] = ToePitch(r, side);
            stats.FootMean[side] = Vector3.zero;
            stats.AnkleRange[side] = new Vector2(float.PositiveInfinity, float.NegativeInfinity);
        }

        var sum = new Vector4[r.Bones.Length];
        var first = new Quaternion[r.Bones.Length];
        var z = new Dictionary<string, float[]> { ["Left"] = new float[samples], ["Right"] = new float[samples] };
        var grounded = new Dictionary<string, bool[]> { ["Left"] = new bool[samples], ["Right"] = new bool[samples] };
        var steadySum = SteadyBones.ToDictionary(n => n, n => Vector4.zero);
        var steadyFirst = new Dictionary<string, Quaternion>();
        for (int i = 0; i < samples; i++)
        {
            r.Reset();
            ReferencePose(r, source, run, source.Clip.length * i / samples, KneeBend(run), ElbowBend(run));
            foreach (string name in SteadyBones)
            {
                var q = Quaternion.Inverse(r.Root.rotation) * r.B(name).rotation;
                if (i == 0) steadyFirst[name] = q;
                if (Quaternion.Dot(steadyFirst[name], q) < 0) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                steadySum[name] += new Vector4(q.x, q.y, q.z, q.w);
            }

            for (int b = 0; b < r.Bones.Length; b++)
            {
                var q = r.Bones[b].localRotation;
                if (i == 0) first[b] = q;
                if (Quaternion.Dot(first[b], q) < 0) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                sum[b] += new Vector4(q.x, q.y, q.z, q.w);
            }

            foreach (string side in GaitSides)
            {
                var ankle = r.Root.InverseTransformPoint(r.B(side + "Foot").position);
                stats.FootMean[side] += ankle / samples;
                var range = stats.AnkleRange[side];
                stats.AnkleRange[side] = new Vector2(Mathf.Min(range.x, ankle.y), Mathf.Max(range.y, ankle.y));
                z[side][i] = ankle.z;
                grounded[side][i] = SoleHeight(r, side) < .015f;
            }
        }

        stats.Neutral = sum.Select(v => new Quaternion(v.x, v.y, v.z, v.w)).Select(q =>
        {
            float m = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            return new Quaternion(q.x / m, q.y / m, q.z / m, q.w / m);
        }).ToArray();
        foreach (string name in SteadyBones)
        {
            var v = steadySum[name].normalized;
            stats.SteadyRotation[name] = new Quaternion(v.x, v.y, v.z, v.w);
        }

        // Swing window per foot: the longest circular run of airborne samples.
        foreach (string side in GaitSides)
        {
            int bestStart = 0, bestLength = 0;
            for (int i = 0; i < samples; i++)
            {
                if (grounded[side][i] || !grounded[side][(i - 1 + samples) % samples]) continue;
                int length = 0;
                while (length < samples && !grounded[side][(i + length) % samples]) length++;
                if (length > bestLength) { bestLength = length; bestStart = i; }
            }
            stats.Swing[side] = new Vector2((float)bestStart / samples, (float)bestLength / samples);
        }

        // Speed of a planted foot relative to the hips = ground speed that avoids foot sliding.
        float dt = source.Clip.length / samples;
        var speeds = new List<float>();
        foreach (string side in GaitSides)
        {
            for (int i = 0; i < samples; i++)
            {
                int j = (i + 1) % samples;
                if (grounded[side][i] && grounded[side][j])
                {
                    float v = -(z[side][j] - z[side][i]) / dt;
                    if (v > 0) speeds.Add(v);
                }
            }
        }

        speeds.Sort();
        stats.StanceSpeed = speeds.Count > 0 ? speeds[speeds.Count / 2] : 0;
        r.Reset();
        return stats;
    }

    private static void AimBone(Transform bone, Vector3 current, Vector3 desired)
    {
        if (current.sqrMagnitude < 1e-8f || desired.sqrMagnitude < 1e-8f) return;
        bone.rotation = Quaternion.FromToRotation(current, desired) * bone.rotation;
    }

    // Palm frame from wrist->middle knuckle and the index/little knuckle spread.
    private static bool PalmFrame(Vector3 hand, Vector3 middle, Vector3 index, Vector3 little, out Quaternion frame)
    {
        var forward = middle - hand;
        var normal = Vector3.Cross(index - hand, little - hand);
        frame = Quaternion.identity;
        if (forward.sqrMagnitude < 1e-10f || normal.sqrMagnitude < 1e-12f) return false;
        frame = Quaternion.LookRotation(forward, normal);
        return true;
    }

    // Fingers follow the capture's currently sampled frame instead of staying at bind pose.
    // Directions are transferred relative to the palm, so the curl is correct even when this
    // clip's wrist orientation differs from the capture's (jump, sword, idle).
    private static void MatchFingers(Rig r, ReferenceMotion source)
    {
        foreach (string side in GaitSides)
        {
            var src = source.Bones;
            if (!src.TryGetValue(side + "Hand", out var sHand) || !src.TryGetValue(side + "HandMiddle1", out var sMiddle) ||
                !src.TryGetValue(side + "HandIndex1", out var sIndex) || !src.TryGetValue(side + "HandPinky1", out var sLittle) ||
                !r.Map.TryGetValue(side + "MiddleProximal", out var tMiddle) || !r.Map.TryGetValue(side + "IndexProximal", out var tIndex) ||
                !r.Map.TryGetValue(side + "LittleProximal", out var tLittle))
                continue;
            var tHand = r.B(side + "Hand");
            if (!PalmFrame(Mirror(sHand.position), Mirror(sMiddle.position), Mirror(sIndex.position), Mirror(sLittle.position), out var sourcePalm) ||
                !PalmFrame(tHand.position, tMiddle.position, tIndex.position, tLittle.position, out var targetPalm))
                continue;
            var palmDelta = targetPalm * Quaternion.Inverse(sourcePalm);
            for (int f = 0; f < TargetFingers.Length; f++)
            {
                for (int j = f == 0 ? 1 : 0; j < 3; j++)
                {
                    if (!r.Map.TryGetValue(side + TargetFingers[f] + TargetFingerJoints[j], out var joint) ||
                        !r.Map.TryGetValue(side + TargetFingers[f] + TargetFingerJoints[j + 1], out var child) ||
                        !source.Bones.TryGetValue(side + "Hand" + SourceFingers[f] + (j + 1), out var a) ||
                        !source.Bones.TryGetValue(side + "Hand" + SourceFingers[f] + (j + 2), out var b))
                        break;
                    AimBone(joint, child.position - joint.position, palmDelta * Mirror(b.position - a.position));
                }
            }
        }
    }

    private static void RedesignedGait(Rig r, ReferenceMotion source, GaitStats stats, bool run, Vector2 direction, float phase)
    {
        // Wrist keeps the stable retargeted pose; fingers carry the capture's curl (palm-relative).
        ReferencePose(r, source, run, Mathf.Repeat(phase, 1) * source.Clip.length, KneeBend(run), ElbowBend(run));
        float angle = Mathf.Atan2(direction.x, direction.y);
        float s = Mathf.Sin(angle), c = Mathf.Cos(angle), lateral = Mathf.Abs(s);
        // Walking always goes through the leg IK (forward included) so its foot lift can be raised.
        // For straight-forward travel the rest of this block is an identity (no turn, full swing).
        if (lateral > .001f || c < 0 || !run)
        {
            // Capture the planted/swing leg state of the forward capture before touching the torso.
            var ankle = new Vector3[2];
            var knee = new Vector3[2];
            var footRotation = new Quaternion[2];
            var sole = new float[2];
            var pitch = new float[2];
            for (int k = 0; k < 2; k++)
            {
                string side = GaitSides[k];
                var foot = r.B(side + "Foot");
                ankle[k] = r.Root.InverseTransformPoint(foot.position);
                knee[k] = r.Root.InverseTransformPoint(r.B(side + "LowerLeg").position);
                footRotation[k] = Quaternion.Inverse(r.Root.rotation) * foot.rotation;
                sole[k] = SoleHeight(r, side);
                pitch[k] = ToePitch(r, side) - stats.BindPitch[side];
            }

            // Torso keeps its captured motion, with less pelvis/shoulder twist when strafing.
            float torso = Mathf.Lerp(1, .55f, lateral);
            foreach (string name in new[] { "Hips", "Spine", "Chest", "UpperChest", "Neck", "Head" })
                BlendFromNeutral(r, stats, name, torso);
            r.B("Spine").localRotation *= Quaternion.Euler(0, 0, -3 * s);

            // Arm swing follows the forward component of travel: full forward, small while
            // strafing, reversed when backing up so arms stay opposite to the legs.
            float armSwing = Mathf.Lerp(c, .3f, lateral);
            foreach (string side in GaitSides)
            {
                BlendFromNeutral(r, stats, side + "Shoulder", armSwing);
                BlendFromNeutral(r, stats, side + "UpperArm", armSwing);
                BlendFromNeutral(r, stats, side + "LowerArm", Mathf.Abs(armSwing));
            }

            // Rotate each foot's path around its own lane. The swing foot passes slightly in
            // front during side steps so the legs never clip through each other.
            // Walks take longer strides: same ground speed at a slower, calmer cadence.
            float stride = Mathf.Lerp(1, PlayerCharacterAnimation.LateralStrideScale, lateral) * (run ? 1 : WalkStrideScale);
            float footRoll = Mathf.Lerp(c, .35f, lateral);
            // Side runs: swing height and sole clearance scale down together.
            float lift = run ? Mathf.Lerp(1, RunSideLift, lateral) : 1;
            var targets = new Vector3[2];
            var rotations = new Quaternion[2];
            var soleTargets = new float[2];
            for (int k = 0; k < 2; k++)
            {
                string side = GaitSides[k];
                var mean = stats.FootMean[side];
                var range = stats.AnkleRange[side];
                float along = (ankle[k].z - mean.z) * stride;
                // Swing amount from sole clearance, which stays 0 through heel-off. Using ankle
                // height made the planted foot slide forward during heel-off on side steps.
                float swing = Mathf.InverseLerp(.005f, .04f, sole[k]);
                float outward = Mathf.Sign(mean.x);
                targets[k] = new Vector3(
                    ankle[k].x + s * along + outward * .03f * lateral,
                    range.x + (ankle[k].y - range.x) * lift + WalkArc(stats, side, phase, run),
                    mean.z + c * along + .08f * lateral * swing);
                // Never cross the body's midline: keep each foot at least half a gap on its own
                // side (soft clamp, so no kink). Feet may slide a little there; that is accepted.
                float midline = r.Root.InverseTransformPoint(r.B("Hips").position).x;
                float fromMid = outward * (targets[k].x - midline);
                targets[k].x = midline + outward * SoftMax(fromMid, SideStepGap * .5f * lateral, .015f * lateral);
                rotations[k] = Quaternion.AngleAxis(10 * s, Vector3.up) * Quaternion.AngleAxis(pitch[k] * (footRoll - 1), Vector3.right) * footRotation[k];
                soleTargets[k] = sole[k] * lift + WalkArc(stats, side, phase, run);
            }

            // Never let a leg lock straight: if a foot target is beyond reach (knee bend
            // below MinKneeFlex), sink the hips instead. Max of two smooth curves = smooth.
            float drop = 0;
            for (int k = 0; k < 2; k++)
            {
                string side = GaitSides[k];
                var hip = r.Root.InverseTransformPoint(r.B(side + "UpperLeg").position);
                float a = r.Positions[System.Array.IndexOf(r.Bones, r.B(side + "LowerLeg"))].magnitude;
                float b = r.Positions[System.Array.IndexOf(r.Bones, r.B(side + "Foot"))].magnitude;
                float reach = Mathf.Sqrt(a * a + b * b + 2 * a * b * Mathf.Cos(MinKneeFlex * Mathf.Deg2Rad));
                float horizontal = new Vector2(targets[k].x - hip.x, targets[k].z - hip.z).magnitude;
                if (horizontal >= reach) continue;
                drop = Mathf.Max(drop, (hip.y - targets[k].y) - Mathf.Sqrt(reach * reach - horizontal * horizontal));
            }
            if (drop > 0) r.B("Hips").position -= r.Root.up * drop;

            for (int k = 0; k < 2; k++)
            {
                string side = GaitSides[k];
                var target = targets[k];
                // Knee always bends forward (slightly outward): aim the hint ahead of the
                // hip-foot midpoint. Shifting the captured knee along with a mirrored foot
                // path put the hint behind the leg when backing up, flipping the knee.
                var hip = r.Root.InverseTransformPoint(r.B(side + "UpperLeg").position);
                var hint = (hip + target) * .5f + Vector3.forward * .25f + Vector3.right * Mathf.Sign(stats.FootMean[side].x) * .03f;
                Limb(r, side + "UpperLeg", side + "LowerLeg", side + "Foot", target, hint, rotations[k]);
                // Preserve the capture's sole clearance (planted stays planted).
                float fix = soleTargets[k] - SoleHeight(r, side);
                if (Mathf.Abs(fix) > .0005f)
                    Limb(r, side + "UpperLeg", side + "LowerLeg", side + "Foot", target + Vector3.up * fix, hint + Vector3.up * fix, rotations[k]);
            }
        }

        // Combat readiness: slight forward lean and a tighter, guarded arm swing.
        CombatLean(r, run ? RunCombatLean : WalkCombatLean);
        foreach (string side in GaitSides)
        {
            BlendFromNeutral(r, stats, side + "Shoulder", CombatArmSwing);
            BlendFromNeutral(r, stats, side + "UpperArm", CombatArmSwing);
        }

        if (run)
        {
            foreach (string side in GaitSides) TuckArm(r, side, RunArmTuckUpper, RunArmTuckFore);
        }

        // Steady gaze: pull neck and head toward the cycle-average orientation so the
        // pelvis/spine sway is not passed on as head wobble. Neck first, head last.
        r.B("Neck").rotation = Quaternion.Slerp(r.B("Neck").rotation, r.Root.rotation * stats.SteadyRotation["Neck"], NeckSteady);
        r.B("Head").rotation = Quaternion.Slerp(r.B("Head").rotation, r.Root.rotation * stats.SteadyRotation["Head"], HeadSteady);

        foreach (var bone in r.Bones.Where(b => b.name.Contains("Cloth"))) bone.localRotation = Quaternion.identity;
    }

    // Side-run foot lift (1 = captured height) and the smallest knee bend IK may reach (degrees).
    private const float RunSideLift = .5f, MinKneeFlex = 15;
    // Minimum foot spacing (model units) across the midline during side steps.
    private const float SideStepGap = .05f;

    // Smooth max(a, b): equals the larger value away from the crossover, rounded within ~k.
    private static float SoftMax(float a, float b, float k)
    {
        if (k <= 1e-6f) return Mathf.Max(a, b);
        return .5f * (a + b + Mathf.Sqrt((a - b) * (a - b) + k * k));
    }
    // Walks: extra foot lift at mid-swing (model units; the capture's own peak is ~0.06) and
    // stride length scale (>1 = slower cadence at the same ground speed, no foot sliding).
    private const float WalkExtraLift = .06f, WalkStrideScale = 1.12f;

    // Extra swing height: a smooth arc that is exactly 0 at lift-off and touch-down (so contact
    // and heel-off are untouched) and peaks mid-swing. Additive, so it never amplifies heel-off.
    private static float WalkArc(GaitStats stats, string side, float phase, bool run)
    {
        if (run || WalkExtraLift <= 0 || !stats.Swing.TryGetValue(side, out var swing) || swing.y <= 0) return 0;
        float u = Mathf.Repeat(phase - swing.x, 1) / swing.y;
        if (u >= 1) return 0;
        return WalkExtraLift * Mathf.Pow(Mathf.Sin(u * Mathf.PI), 1.5f);
    }

    // Combat feel: torso lean in degrees, and arm swing kept (1 = full captured swing).
    private const float WalkCombatLean = 4, RunCombatLean = 6, CombatArmSwing = 1;

    // Lean the torso forward from the waist. The direction is verified geometrically
    // (the head must move forward), so no rotation-sign assumption about the rig.
    private static void CombatLean(Rig r, float degrees)
    {
        var spine = r.B("Spine");
        var head = r.B("Head");
        var before = head.position;
        var axis = r.Root.right;
        spine.rotation = Quaternion.AngleAxis(degrees, axis) * spine.rotation;
        if (Vector3.Dot(head.position - before, r.Root.forward) < 0)
            spine.rotation = Quaternion.AngleAxis(-2 * degrees, axis) * spine.rotation;
    }

    // 0 = captured head motion, 1 = completely locked orientation.
    private const float NeckSteady = .3f, HeadSteady = .65f;

    // Run arms: remove this fraction of the outward (away-from-body) component of the
    // upper arm and forearm directions. Purely geometric, so no axis-sign assumptions.
    private const float RunArmTuckUpper = .5f, RunArmTuckFore = .7f;

    private static void TuckArm(Rig r, string side, float upperAmount, float foreAmount)
    {
        var upper = r.B(side + "UpperArm");
        var lower = r.B(side + "LowerArm");
        var hand = r.B(side + "Hand");
        var outward = r.Root.right * Mathf.Sign(r.Root.InverseTransformPoint(upper.position).x);
        TuckSegment(upper, lower.position - upper.position, outward, upperAmount);

        // On the forward swing, drive the elbow toward the body's midline as well. Scales
        // smoothly with how far the upper arm points forward, so the back swing is unchanged.
        var arm = lower.position - upper.position;
        float forward = Mathf.SmoothStep(0, 1, Vector3.Dot(arm.normalized, r.Root.forward) / .5f);
        if (forward > 0)
        {
            var inward = arm - outward * arm.magnitude * RunElbowInward * forward;
            upper.rotation = Quaternion.FromToRotation(arm, inward) * upper.rotation;
        }

        TuckSegment(lower, hand.position - lower.position, outward, foreAmount);
    }

    // Extra elbow pull toward the midline at full forward swing (fraction of upper-arm length, ~14 degrees).
    private const float RunElbowInward = .4f;

    private static void TuckSegment(Transform bone, Vector3 direction, Vector3 outward, float amount)
    {
        float spread = Vector3.Dot(direction, outward);
        if (spread <= 0) return; // already pointing inward
        var tucked = direction - outward * spread * amount;
        bone.rotation = Quaternion.FromToRotation(direction, tucked) * bone.rotation;
    }

    // Extra joint bend (degrees) added on top of the capture for walk/run only.
    private const float WalkKneeBend = 16, RunKneeBend = 19, WalkElbowBend = 0, RunElbowBend = 0;
    private static float KneeBend(bool run) => run ? RunKneeBend : WalkKneeBend;
    private static float ElbowBend(bool run) => run ? RunElbowBend : WalkElbowBend;

    private static void BlendFromNeutral(Rig r, GaitStats stats, string name, float amount)
    {
        var bone = r.B(name);
        bone.localRotation = Quaternion.SlerpUnclamped(stats.Neutral[System.Array.IndexOf(r.Bones, bone)], bone.localRotation, amount);
    }

    // ---------------- Hands: relaxed finger curl + wrist follow-through ----------------
    // Fingers blend between two real poses of the same bones (bind = straight, a captured
    // fist), so the curl always follows the true finger hinge axes. The wrist lags the forearm
    // swing (forearm rotation a moment earlier), so no wrist axis is ever assumed.
    private const string HandFistSource = "Assets/assassin-character-model/Jumping.fbx";
    private const float WalkFingerCurl = .4f, RunFingerCurl = .5f, IdleFingerCurl = .42f, FingerCurlSwing = .06f;
    private const float WristLagSeconds = .07f, WristFollow = .6f, MaxWristLag = 20;

    sealed class HandPoseSet
    {
        public int[] Bones;
        public Quaternion[] Open, Fist;
    }

    private static HandPoseSet relaxedHands;

    private static float CurlRatio(Rig r, string side)
    {
        if (!r.Map.TryGetValue(side + "MiddleProximal", out var knuckle) || !r.Map.TryGetValue(side + "MiddleTip", out var tip)) return 0;
        var finger = (knuckle.position - r.B(side + "Hand").position).normalized;
        var curl = tip.position - knuckle.position;
        if (curl.sqrMagnitude < 1e-12f) return 0;
        return (curl - finger * Vector3.Dot(curl, finger)).magnitude / curl.magnitude;
    }

    private static HandPoseSet CaptureHandPoses(Rig r)
    {
        var set = new HandPoseSet();
        set.Bones = Enumerable.Range(0, r.Bones.Length)
            .Where(i => !r.Bones[i].name.EndsWith("Tip") && TargetFingers.Any(f => r.Bones[i].name.Contains(f)) &&
                        (r.Bones[i].name.StartsWith("Left") || r.Bones[i].name.StartsWith("Right")))
            .ToArray();
        set.Open = set.Bones.Select(i => r.Rotations[i]).ToArray();
        set.Fist = (Quaternion[])set.Open.Clone();
        using (var source = new ReferenceMotion(HandFistSource))
        {
            source.PhaseOffset = 0;
            float best = -1;
            for (int s = 0; s <= 60; s++)
            {
                r.Reset();
                ReferencePose(r, source, false, source.Clip.length * s / 60 * .999f);
                float curl = CurlRatio(r, "Left") + CurlRatio(r, "Right");
                if (curl > best)
                {
                    best = curl;
                    set.Fist = set.Bones.Select(i => r.Bones[i].localRotation).ToArray();
                }
            }

            Debug.Log("Captured fist pose from " + HandFistSource + " (curl " + (best * .5f).ToString("F2") + ")");
        }

        r.Reset();
        return set;
    }

    private static void ApplyRelaxedHands(Rig r, float leftCurl, float rightCurl)
    {
        if (relaxedHands == null) return;
        for (int k = 0; k < relaxedHands.Bones.Length; k++)
        {
            var bone = r.Bones[relaxedHands.Bones[k]];
            float amount = bone.name.StartsWith("Left") ? leftCurl : rightCurl;
            bone.localRotation = Quaternion.Slerp(relaxedHands.Open[k], relaxedHands.Fist[k], amount);
        }
    }

    // Full locomotion pose with hands: evaluates the gait a moment earlier to get the forearm
    // swing, then lets each hand trail it (capped) and applies the relaxed, breathing curl.
    private static void GaitWithHands(Rig r, ReferenceMotion source, GaitStats stats, bool run, Vector2 direction, float phase)
    {
        float lag = WristLagSeconds / Mathf.Max(.01f, source.Clip.length);
        RedesignedGait(r, source, stats, run, direction, phase - lag);
        var earlier = GaitSides.Select(s => r.B(s + "LowerArm").rotation).ToArray();
        r.Reset();
        RedesignedGait(r, source, stats, run, direction, phase);
        for (int k = 0; k < 2; k++)
        {
            var hand = r.B(GaitSides[k] + "Hand");
            var now = hand.rotation;
            var trailing = Quaternion.Slerp(now, earlier[k] * hand.localRotation, WristFollow);
            float angle = Quaternion.Angle(now, trailing);
            hand.rotation = angle > MaxWristLag ? Quaternion.Slerp(now, trailing, MaxWristLag / angle) : trailing;
        }

        float curl = run ? RunFingerCurl : WalkFingerCurl;
        float swing = Mathf.Sin(phase * 2 * Mathf.PI) * FingerCurlSwing;
        ApplyRelaxedHands(r, curl + swing, curl - swing);
    }

    [MenuItem("Tools/Assassin/Rebuild All Player Animations (Redesigned + Fingers)")]
    public static void RebuildRedesignedLocomotion()
    {
        if (Application.isPlaying) throw new System.InvalidOperationException("Exit Play Mode first.");
        var model = PrefabUtility.LoadPrefabContents("Assets/assassin-character-model/Assassin Model.prefab");
        try
        {
            var r = new Rig(model.transform);
            using (var walk = new ReferenceMotion(RedesignWalkSource))
            using (var run = new ReferenceMotion(RedesignRunSource))
            {
                relaxedHands = CaptureHandPoses(r);
                var walkStats = AnalyseGait(r, walk, false);
                var runStats = AnalyseGait(r, run, true);
                float walkLength = walk.Clip.length, runLength = run.Clip.length;
                for (int i = 0; i < 8; i++)
                {
                    var direction = Directions[i];
                    Bake(r, "Walk" + DirectionNames[i], walkLength, true, (rig, t) => GaitWithHands(rig, walk, walkStats, false, direction, t / walkLength));
                    Bake(r, "Run" + DirectionNames[i], runLength, true, (rig, t) => GaitWithHands(rig, run, runStats, true, direction, t / runLength));
                }

                bool plausible = walkStats.StanceSpeed > .3f && runStats.StanceSpeed > walkStats.StanceSpeed && runStats.StanceSpeed < 12;
                if (plausible)
                {
                    foreach (var bridge in Object.FindObjectsByType<PlayerCharacterAnimation>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    {
                        float scale = bridge.CharacterAnimator ? bridge.CharacterAnimator.transform.lossyScale.y : 1;
                        bridge.WalkReferenceSpeed = walkStats.StanceSpeed * WalkStrideScale * scale;
                        bridge.RunReferenceSpeed = runStats.StanceSpeed * scale;
                        EditorUtility.SetDirty(bridge);
                        EditorSceneManager.MarkSceneDirty(bridge.gameObject.scene);
                    }
                }
                else
                {
                    Debug.LogWarning("Measured gait speeds look wrong; WalkReferenceSpeed/RunReferenceSpeed left unchanged.");
                }

                // Procedural idle: relaxed fingers that slowly breathe with the idle cycle.
                Bake(r, "ActionIdle", IdleDuration, true, (rig, t) =>
                {
                    Idle(rig, t);
                    foreach (var bone in rig.Bones.Where(b => b.name.Contains("Cloth"))) bone.localRotation = Quaternion.identity;
                    float breathe = Mathf.Sin(t / IdleDuration * 2 * Mathf.PI) * .03f;
                    ApplyRelaxedHands(rig, IdleFingerCurl + breathe, IdleFingerCurl - breathe);
                });

                Debug.Log("Redesigned locomotion baked: walk " + walkLength.ToString("F3") + "s @ " + (walkStats.StanceSpeed * WalkStrideScale).ToString("F2") +
                          " m/s, run " + runLength.ToString("F3") + "s @ " + runStats.StanceSpeed.ToString("F2") + " m/s (model space).");
            }

            // Jump phases, landing and both sword cuts, re-retargeted so they carry fingers too.
            RetargetJumpAndAttack(r);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveOpenScenes();
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(model);
        }
    }
}
