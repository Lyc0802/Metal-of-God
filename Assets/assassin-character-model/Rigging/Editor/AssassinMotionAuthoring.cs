using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

// Offline authoring only: curves are baked into the existing Generic rig.
/// <summary>
/// Editor 工具：產生角色動畫曲線，並將參考步行與跑步重定向到既有骨架。
/// </summary>
public static partial class AssassinMotionAuthoring
{
    public const string Folder = "Assets/assassin-character-model/Rigging";
    public const float IdleDuration = 4, WalkDuration = 1;
    private static Quaternion SwordMount = Quaternion.identity;
    private static readonly Vector3 RestBlade = new Vector3(-.22f, -.72f, .66f);
    sealed class Rig
    {
        public Transform Root;
        public Transform[] Bones;
        public Dictionary<string, Transform> Map;
        public Vector3[] Positions;
        public Quaternion[] Rotations;
        public Rig(Transform root)
        {
            Root = root;
            Bones = root.Find("Rig").GetComponentsInChildren<Transform>().Where(t => t.name != "Rig" && !t.GetComponent<Collider>()).ToArray();
            Map = Bones.ToDictionary(t => t.name);
            Positions = Bones.Select(t => t.localPosition).ToArray();
            Rotations = Bones.Select(t => t.localRotation).ToArray();
        }

        public Transform B(string name) => Map[name];
        public void Reset()
        {
            for (int i = 0; i < Bones.Length; i++)
            {
                Bones[i].localPosition = Positions[i];
                Bones[i].localRotation = Rotations[i];
            }
        }

        public void Turn(string name, Vector3 angles)
        {
            B(name).localRotation = Quaternion.Euler(angles);
        }
    }

    private static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);
    private static float Wave(float phase) => Mathf.Sin(phase * 2 * Mathf.PI);
    private static float Ease(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * t * (t * (t * 6 - 15) + 10);
    }

    private // Non-uniform Hermite paths carry momentum through the intermediate key poses.
 static Vector3 Path(float t, float[] times, Vector3[] values)
    {
        for (int i = 0; i < times.Length - 1; i++)
        {
            if (t > times[i + 1])
            {
                continue;
            }

            float dt = times[i + 1] - times[i], u = Mathf.Clamp01((t - times[i]) / dt);
            var m0 = i == 0 ? Vector3.zero : (values[i + 1] - values[i - 1]) / (times[i + 1] - times[i - 1]);
            var m1 = i + 2 == times.Length ? Vector3.zero : (values[i + 2] - values[i]) / (times[i + 2] - times[i]);
            return (2 * u * u * u - 3 * u * u + 1) * values[i] + (u * u * u - 2 * u * u + u) * dt * m0 + (-2 * u * u * u + 3 * u * u) * values[i + 1] + (u * u * u - u * u) * dt * m1;
        }

        return values[values.Length - 1];
    }

    private // Analytic two-bone solve happens at authoring time, never on the gameplay Rigidbody.
 static void Limb(
     Rig r,
     string upperName,
     string lowerName,
     string endName,
     Vector3 target,
     Vector3 hint,
     Quaternion rotation)
    {
        var upper = r.B(upperName);
        var lower = r.B(lowerName);
        var end = r.B(endName);
        target = r.Root.TransformPoint(target);
        hint = r.Root.TransformPoint(hint);
        float a = Vector3.Distance(upper.position, lower.position), b = Vector3.Distance(lower.position, end.position);
        var axis = target - upper.position;
        float d = Mathf.Clamp(axis.magnitude, Mathf.Abs(a - b) + .00001f, a + b - .00001f);
        axis.Normalize();
        target = upper.position + axis * d;
        var bend = Vector3.ProjectOnPlane(hint - upper.position, axis).normalized;
        if (bend.sqrMagnitude < .001f)
        {
            bend = Vector3.ProjectOnPlane(r.Root.forward, axis).normalized;
        }

        float along = (a * a + d * d - b * b) / (2 * d), height = Mathf.Sqrt(Mathf.Max(0, a * a - along * along));
        var knee = upper.position + axis * along + bend * height;
        upper.rotation = Quaternion.FromToRotation(lower.position - upper.position, knee - upper.position) * upper.rotation;
        lower.rotation = Quaternion.FromToRotation(end.position - lower.position, target - lower.position) * lower.rotation;
        end.rotation = r.Root.rotation * rotation;
    }

    private static void Leg(Rig r, string side, Vector3 ankle, float pitch = 0, float yaw = 0)
    {
        float sign = side == "Left" ? 1 : -1;
        Limb(
            r,
            side + "UpperLeg",
            side + "LowerLeg",
            side + "Foot",
            ankle,
            V(sign * .07f, .22f, .25f),
            Quaternion.Euler(pitch, yaw, 0));
        r.Turn(side + "Toes", V(-Mathf.Max(0, pitch) * .55f, 0, 0));
    }

    private // Both segments share the same anatomical bend plane. The forearm has ONE hinge
 // axis, rather than two unrelated shortest-arc rotations twisting the elbow skin.
    static void Arm(Rig r, string side, Vector3 target, Vector3 elbowHint, Vector3? blade = null)
    {
        var upper = r.B(side + "UpperArm");
        var lower = r.B(side + "LowerArm");
        var hand = r.B(side + "Hand");
        var bindUpper = r.Positions[Array.IndexOf(r.Bones, lower)];
        var bindLower = r.Positions[Array.IndexOf(r.Bones, hand)];
        var bindNormal = Vector3.Cross(bindUpper, bindLower).normalized;
        float a = bindUpper.magnitude, b = bindLower.magnitude, bindFlex = Vector3.Angle(bindUpper, bindLower);
        target = r.Root.TransformPoint(target);
        elbowHint = r.Root.TransformPoint(elbowHint);
        var direction = (target - upper.position).normalized;
        float flex = Mathf.Clamp(
            180 - Mathf.Acos(Mathf.Clamp((a * a + b * b - (target - upper.position).sqrMagnitude) / (2 * a * b), -1, 1)) * Mathf.Rad2Deg,
            15,
            112);
        float d = Mathf.Sqrt(a * a + b * b + 2 * a * b * Mathf.Cos(flex * Mathf.Deg2Rad));
        var bend = Vector3.ProjectOnPlane(elbowHint - upper.position, direction).normalized;
        float along = (a * a + d * d - b * b) / (2 * d), height = Mathf.Sqrt(Mathf.Max(0, a * a - along * along));
        var upperDirection = direction * along + bend * height;
        var lowerDirection = direction * (d - along) - bend * height;
        var normal = Vector3.Cross(upperDirection, lowerDirection).normalized;
        upper.rotation = Quaternion.LookRotation(upperDirection, normal) * Quaternion.Inverse(Quaternion.LookRotation(bindUpper, bindNormal));
        lower.localRotation = Quaternion.AngleAxis(flex - bindFlex, bindNormal);
        // Keep the palm following the forearm. Sword aiming gets at most 25 degrees
        // of wrist correction, never the previous unconstrained world-space rotation.
        hand.localRotation = Quaternion.identity;
        if (blade.HasValue)
        {
            var wanted = r.Root.rotation * Quaternion.LookRotation(blade.Value.normalized, Vector3.up) * Quaternion.Inverse(SwordMount);
            hand.rotation = Quaternion.RotateTowards(hand.rotation, wanted, 25);
        }
    }

    private static void RelaxedArms(Rig r, float swing = 0, float breath = 0, bool aim = true)
    {
        r.Turn("LeftShoulder", V(0, -swing * 12, breath * .3f));
        r.Turn("RightShoulder", V(0, swing * 10, -breath * .25f));
        var chest = r.B("UpperChest");
        foreach (string side in new[]
        {
            "Left",
            "Right"
        }

        )
        {
            float sign = side == "Left" ? 1 : -1;
            var shoulder = r.Root.InverseTransformPoint(r.B(side + "UpperArm").position);
            var orientation = Quaternion.Inverse(r.Root.rotation) * chest.rotation;
            var hand = shoulder + orientation * V(sign * .018f, -.172f, .035f - sign * swing);
            var elbow = shoulder + orientation * V(sign * .025f, -.10f, -.12f);
            Arm(r, side, hand, elbow, side == "Right" && aim ? (Vector3? )RestBlade : null);
        }
    }

    private static void WalkArms(Rig r, float phase)
    {
        float cycle = phase * 2 * Mathf.PI;
        foreach (string side in new[]
        {
            "Left",
            "Right"
        }

        )
        {
            float sign = side == "Left" ? 1 : -1;
            bool weapon = side == "Right";
            // Left foot forward => right arm forward. The clavicle leads the upper
            // arm; the elbow and wrist follow a little later, like a relaxed pendulum.
            float drive = Mathf.Cos(cycle), lead = Mathf.Cos(cycle + .12f);
            r.Turn(
                side + "Shoulder",
                V(sign * (weapon ? 2.6f : 3.2f) * lead, 1.8f * lead, sign * .8f * Wave(phase - .04f)));
            var upper = r.B(side + "UpperArm");
            var lower = r.B(side + "LowerArm");
            var hand = r.B(side + "Hand");
            var bindUpper = r.Positions[Array.IndexOf(r.Bones, lower)];
            var bindLower = r.Positions[Array.IndexOf(r.Bones, hand)];
            var normal = Vector3.Cross(bindUpper, bindLower).normalized;
            // Align the neutral upper arm beside the ribs, with its bend plane
            // pointing forward. Animate the shoulder angle directly, not a hand target.
            var neutral = Quaternion.LookRotation(V(sign * .09f, -1, 0), Vector3.left) * Quaternion.Inverse(Quaternion.LookRotation(bindUpper, normal));
            float angle = sign * (weapon ? 20f : 25f) * drive;
            upper.localRotation = Quaternion.AngleAxis(angle, Vector3.right) * neutral;
            float flex = 24 - 7 * sign * Mathf.Cos(cycle - .22f);
            lower.localRotation = Quaternion.AngleAxis(flex - Vector3.Angle(bindUpper, bindLower), normal);
            hand.localRotation = Quaternion.AngleAxis(-sign * 2.5f * Mathf.Cos(cycle - .45f), normal);
        // No world-space sword aim in Walk: the hand and sword travel with the arm.
        }
    }

    private static void Cloth(Rig r, float phase, float amount, float twist = 0)
    {
        foreach (var t in r.Bones.Where(b => b.name.Contains("Cloth")))
        {
            float lag = t.name.EndsWith("Tip") ? .18f : t.name.EndsWith("Lower") ? .10f : 0;
            float side = t.name.Contains("Left") ? 1 : t.name.Contains("Right") ? -1 : 0, front = t.name.StartsWith("Front")
                ? -1
                : 1;
            r.Turn(
                t.name,
                V(front * (1.2f + amount * (.6f + Wave(phase - lag))), twist * (.3f + lag), side * amount * .25f));
        }
    }

    private static void WalkingCloth(Rig r, float phase)
    {
        foreach (string chain in new[]
        {
            "FrontCloth",
            "BackLeftCloth",
            "BackRightCloth",
            "RightCloth",
            "LeftCloth"
        }

        )
        {
            bool front = chain == "FrontCloth", back = chain.StartsWith("Back");
            float side = Mathf.Sign(r.B(chain).localPosition.x);
            float panelPhase = front ? 0 : back ? (side < 0 ? .18f : .38f) : (side < 0 ? .58f : .78f);
            for (int segment = 0; segment < 3; segment++)
            {
                string name = chain + (segment == 0 ? "" : segment == 1 ? "Lower" : "Tip");
                // The waist stays anchored. Each successive joint responds later and
                // bends farther, giving the hem a travelling wave instead of a rigid flap.
                float lag = .055f + .085f * segment;
                float wave = Wave(phase + panelPhase - lag);
                float flutter = Wave(2 * phase + panelPhase * .7f - lag * 1.8f);
                float amplitude = segment == 0 ? 5 : segment == 1 ? 8 : 11;
                float flare = (front ? 2 : back ? 5 : 3) + amplitude * (.55f + .55f * wave) + segment * 1.4f * flutter;
                float pitch = (front ? -1 : 1) * flare;
                float yaw = (2 + segment * 2) * Wave(phase + panelPhase - lag - .14f);
                float roll = side * ((back ? 2 : 4) + amplitude * .35f * Wave(phase + panelPhase - lag - .08f));
                if (front)
                {
                    roll = (2 + segment) * Wave(phase - lag - .12f);
                }

                r.Turn(name, V(pitch, yaw, roll));
            }
        }
    }

    private static void Idle(Rig r, float t)
    {
        float p = t / IdleDuration, b = Wave(p), shift = Wave(p + .16f);
        r.B("Hips").localPosition += V(.0035f * shift, -.006f + .0018f * b, .0015f * Wave(p + .35f));
        r.Turn("Hips", V(0, .7f * shift, -.7f * shift));
        r.Turn("Spine", V(.65f * b, -.35f * shift, .45f * shift));
        r.Turn("Chest", V(1.1f * b, .5f * Wave(p + .1f), .25f * shift));
        r.Turn("UpperChest", V(.45f * b, 0, 0));
        r.Turn("Neck", V(-.4f * b, -.4f * Wave(p + .1f), 0));
        r.Turn("Head", V(-.6f * b, .8f * Wave(p + .23f), .25f * Wave(p + .35f)));
        RelaxedArms(r, .002f * Wave(p + .1f), b);
        Leg(r, "Left", V(.063f, .062f, .002f), 0, 3);
        Leg(r, "Right", V(-.063f, .062f, -.018f), 0, -3);
        Cloth(r, p, .45f);
    }

    private static void Walk(Rig r, float t)
    {
        float p = t / WalkDuration, w = Wave(p), doubleStep = Mathf.Cos(p * 4 * Mathf.PI);
        r.B("Hips").localPosition += V(
            .022f * Mathf.Cos(p * 2 * Mathf.PI),
            -.052f - .016f * doubleStep,
            .006f * Wave(p * 2 - .08f));
        float stride = Mathf.Cos(p * 2 * Mathf.PI);
        r.Turn("Hips", V(2.5f + 2 * Wave(p * 2 - .06f), -6 * stride, -4.5f * stride));
        // Preserve visible trunk roll and twist instead of cancelling the pelvis
        // almost completely across Spine/Chest. The gaze is stabilized at the neck.
        r.B("Spine").localPosition += V(.008f * stride, 0, 0);
        r.Turn("Spine", V(2.5f + 2.2f * Wave(p * 2 - .12f), 5 * stride, -1.5f * stride));
        r.Turn("Chest", V(2 + 1.8f * Wave(p * 2 - .18f), 12 * stride, 1 * stride));
        r.Turn("UpperChest", V(1.2f * Wave(p * 2 - .22f), 3 * stride, .5f * stride));
        r.Turn("Neck", V(-3 - 1.5f * Wave(p * 2 - .15f), -5 * stride, 2.5f * stride));
        r.Turn("Head", V(-2, -9 * stride, 2 * stride));
        WalkArms(r, p);
        foreach (string side in new[]
        {
            "Left",
            "Right"
        }

        )
        {
            float s = side == "Left" ? 1 : -1, phase = Mathf.Repeat(p + (s < 0 ? .5f : 0), 1);
            const float stance = .56f;
            float halfTravel = PlayerCharacterAnimation.AuthoredWalkStride * stance * .5f, z, lift, pitch;
            if (phase < stance)
            {
                z = Mathf.Lerp(halfTravel, -halfTravel, phase / stance);
                lift = 0;
                pitch = -10 * (1 - Ease(phase / .10f)) + 18 * Ease((phase - .44f) / .12f);
            }
            else
            {
                float u = (phase - stance) / (1 - stance), tangent = -PlayerCharacterAnimation.AuthoredWalkStride * (1 - stance);
                z = (2 * u * u * u - 3 * u * u + 1) * (-halfTravel) + (u * u * u - 2 * u * u + u) * tangent + (-2 * u * u * u + 3 * u * u) * halfTravel + (u * u * u - u * u) * tangent;
                lift = .048f * Mathf.Pow(Mathf.Sin(u * Mathf.PI), 1.5f);
                pitch = Mathf.Lerp(18, -10, Ease(u));
            }

            // Roll over the heel/toe without pushing the sole through the floor.
            float radians = pitch * Mathf.Deg2Rad;
            float rollLift = Mathf.Max(
                0,
                .062f * (Mathf.Cos(radians) - 1) + (pitch > 0 ? .118f : -.04f) * Mathf.Sin(radians));
            Leg(r, side, V(s * .063f, .062f + lift + rollLift, -.008f + z), pitch, s * 2);
        }

        WalkingCloth(r, p);
    }

    private static readonly float[] AttackTimes =
    {
        0,
        .28f,
        .34f,
        .46f,
        .58f,
        .72f,
        1
    };
    private static readonly Vector3[] HandPath =
    {
        new Vector3(-.12f, .368f, .048f),
        new Vector3(-.16f, .565f, -.025f),
        new Vector3(-.17f, .565f, .055f),
        new Vector3(-.025f, .49f, .19f),
        new Vector3(.135f, .40f, .095f),
        new Vector3(.12f, .365f, .035f),
        new Vector3(-.12f, .368f, .048f)
    };
    private static readonly Vector3[] BladePath =
    {
        new Vector3(-.22f, -.72f, .66f),
        new Vector3(-.55f, .77f, -.32f),
        new Vector3(-.68f, .51f, .5f),
        new Vector3(-.05f, .12f, 1),
        new Vector3(.88f, -.32f, .34f),
        new Vector3(.72f, -.67f, .18f),
        new Vector3(-.22f, -.72f, .66f)
    };
    private static readonly Vector3[] BodyPath =
    {
        Vector3.zero,
        new Vector3(-3, -18, -2),
        new Vector3(-2, -12, -1),
        new Vector3(4, 12, 2),
        new Vector3(5, 25, 3),
        new Vector3(3, 18, 2),
        Vector3.zero
    };
    private static void Attack(Rig r, float time)
    {
        float t = time / PlayerCharacterAnimation.AuthoredAttackDuration;
        var turn = Path(t, AttackTimes, BodyPath);
        float weight = Ease(t / .32f) * (1 - Ease((t - .72f) / .28f));
        r.B("Hips").localPosition += V(.008f * turn.y / 25, -.006f - .012f * weight, .006f * weight);
        r.Turn("Hips", turn * .55f);
        r.Turn("Spine", V(turn.x * .4f, turn.y * .22f, turn.z * .2f));
        r.Turn("Chest", V(turn.x * .7f, turn.y * .8f, turn.z * .45f));
        r.Turn("UpperChest", V(0, turn.y * .2f, 0));
        r.Turn("Neck", V(-turn.x * .3f, -turn.y * .35f, 0));
        r.Turn("Head", V(-turn.x * .25f, -turn.y * .4f, -turn.z * .2f));
        RelaxedArms(r);
        var orientation = Quaternion.Inverse(r.Root.rotation) * r.B("UpperChest").rotation;
        var shoulder = r.Root.InverseTransformPoint(r.B("RightUpperArm").position);
        Arm(
            r,
            "Right",
            Path(t, AttackTimes, HandPath),
            shoulder + orientation * V(-.035f, -.08f, -.14f),
            Path(t, AttackTimes, BladePath));
        shoulder = r.Root.InverseTransformPoint(r.B("LeftUpperArm").position);
        Arm(
            r,
            "Left",
            shoulder + orientation * Vector3.Lerp(V(.018f, -.172f, .035f), V(.025f, -.11f, .10f), weight),
            shoulder + orientation * V(.035f, -.09f, -.13f));
        Leg(r, "Left", V(.063f, .062f, .002f), 0, 3);
        Leg(r, "Right", V(-.063f, .062f, -.018f), 0, -3 + turn.y * .4f);
        Cloth(r, t * 1.3f, 2.7f * weight, turn.y * .35f);
    }

    private static void SetCurve(AnimationClip clip, string path, string property, Keyframe[] keys, bool loop)
    {
        if (keys.All(k => Mathf.Abs(k.value - keys[0].value) < .000001f))
        {
            keys = new[]
            {
                keys[0],
                keys[keys.Length - 1]
            };
        }

        var curve = new AnimationCurve(keys);
        for (int i = 0; i < keys.Length; i++)
        {
            float tangent;
            if (loop && (i == 0 || i == keys.Length - 1))
            {
                tangent = (keys[1].value - keys[keys.Length - 2].value) / (2 * keys[1].time);
            }
            else if (i == 0 || i == keys.Length - 1)
            {
                tangent = 0;
            }
            else
            {
                tangent = (keys[i + 1].value - keys[i - 1].value) / (keys[i + 1].time - keys[i - 1].time);
            }

            var key = curve[i];
            key.inTangent = key.outTangent = tangent;
            curve.MoveKey(i, key);
        }

        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), property), curve);
    }

    private static AnimationClip Bake(Rig r, string name, float duration, bool loop, Action<Rig, float> pose)
    {
        var clip = new AnimationClip
        {
            name = name,
            frameRate = 60
        };
        int count = Mathf.CeilToInt(duration * (name == "Idle" ? 30 : 60));
        var rotations = new Quaternion[r.Bones.Length, count + 1];
        var positions = new Vector3[r.Bones.Length, count + 1];
        for (int f = 0; f <= count; f++)
        {
            r.Reset();
            pose(r, loop && f == count ? 0 : duration * f / count);
            for (int b = 0; b < r.Bones.Length; b++)
            {
                var q = r.Bones[b].localRotation;
                if (f > 0 && Quaternion.Dot(rotations[b, f - 1], q) < 0)
                {
                    q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                }

                rotations[b, f] = q;
                positions[b, f] = r.Bones[b].localPosition;
            }
        }

        for (int b = 0; b < r.Bones.Length; b++)
        {
            string path = AnimationUtility.CalculateTransformPath(r.Bones[b], r.Root);
            for (int axis = 0; axis < 4; axis++)
            {
                var keys = new Keyframe[count + 1];
                for (int f = 0; f <= count; f++)
                {
                    keys[f] = new Keyframe(duration * f / count, rotations[b, f][axis]);
                }

                SetCurve(clip, path, "m_LocalRotation." + "xyzw"[axis], keys, loop);
            }

            for (int axis = 0; axis < 3; axis++)
            {
                var keys = new Keyframe[count + 1];
                for (int f = 0; f <= count; f++)
                {
                    keys[f] = new Keyframe(duration * f / count, positions[b, f][axis]);
                }

                SetCurve(clip, path, "m_LocalPosition." + "xyz"[axis], keys, loop);
            }
        }

        clip.EnsureQuaternionContinuity();
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop;
        settings.loopBlend = loop;
        settings.keepOriginalPositionXZ = true;
        settings.keepOriginalPositionY = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + "/Animations/" + name + ".anim");
        if (existing)
        {
            EditorUtility.CopySerialized(clip, existing);
            UnityEngine.Object.DestroyImmediate(clip);
            clip = existing;
        }
        else
        {
            AssetDatabase.CreateAsset(clip, Folder + "/Animations/" + name + ".anim");
        }

        EditorUtility.SetDirty(clip);
        r.Reset();
        return clip;
    }

    public static void ConfigureController(AnimatorController controller)
    {
        foreach (var p in new[]
        {
            "WalkRate",
            "AttackProgress",
            "AttackActive"
        }

        )
        {
            if (!controller.parameters.Any(v => v.name == p))
            {
                controller.AddParameter(
                    p,
                    p == "AttackActive" ? AnimatorControllerParameterType.Bool : AnimatorControllerParameterType.Float);
            }
        }

        var parameters = controller.parameters;
        foreach (var p in parameters)
        {
            if (p.name == "WalkRate")
            {
                p.defaultFloat = 1;
            }
        }

        controller.parameters = parameters;
        var machine = controller.layers[0].stateMachine;
        var walk = machine.states.First(s => s.state.name == "Walk").state;
        walk.speedParameter = "WalkRate";
        walk.speedParameterActive = true;
        var attack = machine.states.First(s => s.state.name == "Attack").state;
        attack.timeParameter = "AttackProgress";
        attack.timeParameterActive = true;
        foreach (var transition in machine.anyStateTransitions)
        {
            transition.duration = .075f;
            transition.hasFixedDuration = true;
        }

        foreach (var s in machine.states)
        {
            foreach (var transition in s.state.transitions)
            {
                transition.hasFixedDuration = true;
                if (s.state == attack)
                {
                    continue;
                }

                transition.duration = .16f;
                transition.interruptionSource = TransitionInterruptionSource.SourceThenDestination;
            }
        }

        foreach (var transition in attack.transitions)
        {
            attack.RemoveTransition(transition);
        }

        foreach (string name in new[]
        {
            "Idle",
            "Walk"
        }

        )
        {
            var dest = machine.states.First(s => s.state.name == name).state;
            var end = attack.AddTransition(dest);
            end.hasExitTime = false;
            end.duration = .12f;
            end.hasFixedDuration = true;
            end.AddCondition(AnimatorConditionMode.IfNot, 0, "AttackActive");
            end.AddCondition(name == "Walk" ? AnimatorConditionMode.Greater : AnimatorConditionMode.Less, .1f, "Speed");
        }

        EditorUtility.SetDirty(controller);
        ConfigureReferenceController();
    }

    public static string Rebuild(Transform root)
    {
        var r = new Rig(root);
        try
        {
            r.Reset();
            RelaxedArms(r, 0, 0, false);
            SwordMount = Quaternion.Inverse(r.B("RightHand").rotation) * r.Root.rotation * Quaternion.LookRotation(
                RestBlade,
                Vector3.up);
            r.Reset();
            Bake(r, "Idle", IdleDuration, true, Idle);
            BakeReferenceLocomotion(r);
            Bake(r, "Attack", PlayerCharacterAnimation.AuthoredAttackDuration, false, Attack);
            ConfigureController(AssetDatabase.LoadAssetAtPath<AnimatorController>(Folder + "/Assassin.controller"));
            AssetDatabase.SaveAssets();
            return "Rebuilt Idle, reference Standard Walk / Fast Run, and Attack using the existing rig and controller.";
        }
        finally
        {
            r.Reset();
        }
    }

    public static void AlignGameplaySword(PlayerCombat combat)
    {
        if (!combat || !combat.SwordPivot)
        {
            return;
        }

        Undo.RecordObject(combat.SwordPivot, "Align sword to neutral wrist");
        combat.SwordPivot.localRotation = SwordMount;
        var grip = combat.SwordPivot.Find("Sword Grip");
        if (grip)
        {
            combat.SwordPivot.localPosition = -(SwordMount * Vector3.Scale(
                grip.localPosition,
                combat.SwordPivot.localScale));
        }
    }

    [MenuItem("Tools/Assassin/Rebuild Walk Animation")]
    private static void RebuildWalkMenu()
    {
        Debug.Log(RebuildWalkAssets());
    }

    public static string RebuildWalkAssets()
    {
        if (Application.isPlaying)
        {
            throw new InvalidOperationException("Exit Play Mode before baking animations.");
        }

        var prefab = PrefabUtility.LoadPrefabContents("Assets/assassin-character-model/Assassin Model.prefab");
        var rig = new Rig(prefab.transform);
        try
        {
            BakeReferenceLocomotion(rig);
            ConfigureReferenceController();
            AssetDatabase.SaveAssets();
            return "Retargeted Standard Walk and Fast Run to the existing Assassin rig.";
        }
        finally
        {
            rig.Reset();
            PrefabUtility.UnloadPrefabContents(prefab);
        }
    }

    [MenuItem("Tools/Assassin/Rebuild Natural Animations")]
    public static void RebuildAssets()
    {
        if (Application.isPlaying)
        {
            throw new InvalidOperationException("Exit Play Mode before baking animations.");
        }

        var prefab = PrefabUtility.LoadPrefabContents("Assets/assassin-character-model/Assassin Model.prefab");
        try
        {
            Debug.Log(Rebuild(prefab.transform));
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefab);
        }
    }
}
