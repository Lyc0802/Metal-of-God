using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Offline retargeting from the two user-provided FBXs. The gameplay avatar, skin,
// bind poses and bone lengths are preserved. Only ordinary transform clips are baked.
/// <summary>
/// Editor 工具：產生角色動畫曲線，並將參考步行與跑步重定向到既有骨架。
/// </summary>
public static partial class AssassinMotionAuthoring
{
    private const string WalkSource = "Assets/assassin-character-model/Standard Walk.fbx";
    private const string RunSource = "Assets/assassin-character-model/Fast Run.fbx";
    private static Vector3 Mirror(Vector3 p) => new Vector3(-p.x, p.y, p.z);
    private static Quaternion Mirror(Quaternion q) => new Quaternion(q.x, -q.y, -q.z, q.w);
    sealed class ReferenceMotion : IDisposable
    {
        public GameObject Model;
        public AnimationClip Clip;
        public Dictionary<string, Transform> Bones;
        public Dictionary<string, Vector3> HingeAxes = new Dictionary<string, Vector3>();
        public float PhaseOffset, Ground, LegLength;
        public Vector3 MeanHips;
        public ReferenceMotion(string path)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!asset)
            {
                throw new InvalidOperationException("Missing locomotion reference: " + path);
            }

            Model = UnityEngine.Object.Instantiate(asset);
            Model.hideFlags = HideFlags.HideAndDontSave;
            Model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Model.transform.localScale = Vector3.one;
            foreach (var renderer in Model.GetComponentsInChildren<Renderer>())
            {
                renderer.enabled = false;
            }

            foreach (var animator in Model.GetComponentsInChildren<Animator>())
            {
                animator.applyRootMotion = false;
                animator.enabled = false;
            }

            Clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c => !c.name.StartsWith("__"));
            Bones = Model.GetComponentsInChildren<Transform>().Where(t => t.name.Contains(":")).ToDictionary(t => t.name.Substring(t.name.IndexOf(':') + 1));
            LegLength = Vector3.Distance(Bones["LeftUpLeg"].position, Bones["LeftLeg"].position) + Vector3.Distance(
                Bones["LeftLeg"].position,
                Bones["LeftFoot"].position);
            Ground = float.PositiveInfinity;
            float ankleGround = float.PositiveInfinity;
            var largestFlex = new Dictionary<string, float>();
            for (int i = 0; i < 120; i++)
            {
                Clip.SampleAnimation(Model, Clip.length * i / 120);
                MeanHips += Bones["Hips"].position / 120;
                Ground = Mathf.Min(Ground, LowestPoint());
                ankleGround = Mathf.Min(ankleGround, Bones["LeftFoot"].position.y);
                foreach (string side in new[]
                {
                    "Left",
                    "Right"
                }

                )
                {
                    foreach (bool arm in new[]
                    {
                        true,
                        false
                    }

                    )
                    {
                        string a = side + (arm ? "Arm" : "UpLeg"), b = side + (arm ? "ForeArm" : "Leg"), c = side + (arm
                            ? "Hand"
                            : "Foot");
                        var u = Bones[b].position - Bones[a].position;
                        var l = Bones[c].position - Bones[b].position;
                        float angle = Vector3.Angle(u, l);
                        if (!largestFlex.ContainsKey(a) || angle > largestFlex[a])
                        {
                            largestFlex[a] = angle;
                            HingeAxes[a] = Bones[a].InverseTransformDirection(Vector3.Cross(u, l).normalized);
                        }
                    }
                }
            }

            // Align both cycles to the same left-foot forward contact for phase-safe blending.
            float best = float.NegativeInfinity;
            for (int i = 0; i < 120; i++)
            {
                Clip.SampleAnimation(Model, Clip.length * i / 120);
                var foot = Bones["LeftFoot"].position;
                if (foot.y > ankleGround + .09f)
                {
                    continue;
                }

                float score = foot.z - Bones["Hips"].position.z;
                if (score > best)
                {
                    best = score;
                    PhaseOffset = i / 120f;
                }
            }
        }

        public float LowestPoint() => Mathf.Min(
            Bones["LeftToeBase"].position.y,
            Bones["RightToeBase"].position.y,
            Bones["LeftFoot"].position.y,
            Bones["RightFoot"].position.y);
        public void Sample(float phase)
        {
            Clip.SampleAnimation(Model, Mathf.Repeat(phase + PhaseOffset, 1) * Clip.length);
        }

        public void Dispose()
        {
            if (Model)
            {
                UnityEngine.Object.DestroyImmediate(Model);
            }
        }
    }

    private static void ReferenceLimb(Rig r, ReferenceMotion source, string side, bool arm)
    {
        string upperName = side + (arm ? "UpperArm" : "UpperLeg"), lowerName = side + (arm ? "LowerArm" : "LowerLeg"), endName = side + (arm
            ? "Hand"
            : "Foot");
        string a = side + (arm ? "Arm" : "UpLeg"), b = side + (arm ? "ForeArm" : "Leg"), c = side + (arm
            ? "Hand"
            : "Foot");
        var upper = r.B(upperName);
        var lower = r.B(lowerName);
        var end = r.B(endName);
        var bindUpper = r.Positions[Array.IndexOf(r.Bones, lower)];
        var bindLower = r.Positions[Array.IndexOf(r.Bones, end)];
        var bindNormal = Vector3.Cross(bindUpper, bindLower).normalized;
        var u = Mirror(source.Bones[b].position - source.Bones[a].position).normalized;
        var l = Mirror(source.Bones[c].position - source.Bones[b].position).normalized;
        // Calibrate the hinge in the source bone's local frame at a bent pose.
        // Following its authored rotation avoids an unstable cross product when a
        // running knee/elbow becomes almost straight, without suppressing source twist.
        var normal = -Mirror(source.Bones[a].TransformDirection(source.HingeAxes[a]));
        normal = Vector3.ProjectOnPlane(normal, u).normalized;
        upper.rotation = Quaternion.LookRotation(u, normal) * Quaternion.Inverse(Quaternion.LookRotation(bindUpper, bindNormal));
        float flex = Mathf.Clamp(Vector3.Angle(u, l), arm ? 5 : .5f, arm ? 130 : 155);
        lower.localRotation = Quaternion.AngleAxis(flex - Vector3.Angle(bindUpper, bindLower), bindNormal);
        if (arm)
        {
            end.localRotation = Quaternion.identity;
        }
        else
        {
            var toe = r.B(side + "Toes");
            var forward = Mirror(source.Bones[side + "ToeBase"].position - source.Bones[c].position).normalized;
            // A lateral foot axis stays well-defined when Run points the toes almost
            // vertically; world-up would introduce a 180-degree ankle bank flip.
            var lateral = -Mirror(source.Bones["Hips"].right);
            end.rotation = Quaternion.LookRotation(forward, lateral) * Quaternion.Inverse(Quaternion.LookRotation(toe.localPosition, Vector3.right));
            var toeForward = Mirror(source.Bones[side + "Toe_End"].position - source.Bones[side + "ToeBase"].position).normalized;
            float footPitch = Mathf.Atan2(-forward.y, new Vector2(forward.x, forward.z).magnitude) * Mathf.Rad2Deg;
            float toePitch = Mathf.Atan2(-toeForward.y, new Vector2(toeForward.x, toeForward.z).magnitude) * Mathf.Rad2Deg;
            toe.localRotation = Quaternion.Euler(Mathf.Clamp(toePitch - footPitch, -35, 35), 0, 0);
        }
    }

    private static float SoleHeight(Rig r, string side)
    {
        var foot = r.B(side + "Foot");
        var toes = r.B(side + "Toes");
        return Mathf.Min(
            (foot.position + foot.rotation * V(0, -.062f, 0)).y,
            (toes.position + toes.rotation * V(0, -.025f, 0)).y);
    }

    private static void ReferencePose(Rig r, ReferenceMotion source, bool running, float time)
    {
        float phase = time / source.Clip.length;
        source.Sample(phase);
        float legScale = (r.B("LeftLowerLeg").localPosition.magnitude + r.B("LeftFoot").localPosition.magnitude) / source.LegLength;
        var srcHips = source.Bones["Hips"].position;
        r.B("Hips").localPosition = V(
            -(srcHips.x - source.MeanHips.x) * legScale,
            srcHips.y * legScale,
            0);
        foreach (var pair in new[]
        {
            new[]
            {
                "Hips",
                "Hips"
            },
            new[]
            {
                "Spine",
                "Spine"
            },
            new[]
            {
                "Chest",
                "Spine1"
            },
            new[]
            {
                "UpperChest",
                "Spine2"
            },
            new[]
            {
                "Neck",
                "Neck"
            },
            new[]
            {
                "Head",
                "Head"
            }
        }

        )
        {
            r.B(pair[0]).rotation = Mirror(source.Bones[pair[1]].rotation);
        }

        foreach (string side in new[]
        {
            "Left",
            "Right"
        }

        )
        {
            var shoulder = r.B(side + "Shoulder");
            var clavicle = Mirror(source.Bones[side + "Arm"].position - source.Bones[side + "Shoulder"].position);
            shoulder.rotation = Quaternion.LookRotation(clavicle, Mirror(source.Bones["Spine2"].up)) * Quaternion.Inverse(Quaternion.LookRotation(r.B(side + "UpperArm").localPosition, Vector3.up));
            ReferenceLimb(r, source, side, true);
            ReferenceLimb(r, source, side, false);
        }

        // Strip all source horizontal travel. Match sole contact on this model's
        // proportions, retaining the reference's airborne phase in Run.
        float flight = running ? Mathf.Max(0, source.LowestPoint() - source.Ground) * legScale : 0;
        float floor = Mathf.Min(SoleHeight(r, "Left"), SoleHeight(r, "Right"));
        r.B("Hips").position += Vector3.up * (flight - floor);
        WalkingCloth(r, phase);
        if (running)
        {
            foreach (var bone in r.Bones.Where(b => b.name.Contains("Cloth")))
            {
                float depth = bone.name.EndsWith("Tip") ? 1.5f : bone.name.EndsWith("Lower") ? 1 : .5f;
                bone.localRotation *= Quaternion.Euler(4 * depth, 2 * depth * Wave(phase - .12f), 0);
            }
        }
    }

    private static void BakeReferenceLocomotion(Rig r)
    {
        using (var walk = new ReferenceMotion(WalkSource))
        {
            using (var run = new ReferenceMotion(RunSource))
            {
                Bake(r, "Walk", walk.Clip.length, true, (rig, t) => ReferencePose(rig, walk, false, t));
                Bake(r, "Run", run.Clip.length, true, (rig, t) => ReferencePose(rig, run, true, t));
                Debug.Log("Locomotion retargeted from Standard Walk (" + walk.Clip.length.ToString("F3") + "s) and Fast Run (" + run.Clip.length.ToString("F3") + "s); contact phases=" + walk.PhaseOffset + ", " + run.PhaseOffset);
            }
        }
    }

    public static void ConfigureReferenceController()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(Folder + "/Assassin.controller");
        if (!controller.parameters.Any(p => p.name == "RunBlend"))
        {
            controller.AddParameter("RunBlend", AnimatorControllerParameterType.Float);
        }

        var state = controller.layers[0].stateMachine.states.First(s => s.state.name == "Walk").state;
        var tree = state.motion as BlendTree;
        if (!tree)
        {
            tree = new BlendTree
            {
                name = "Velocity Walk Run"
            };
            AssetDatabase.AddObjectToAsset(tree, controller);
        }

        tree.blendType = BlendTreeType.Simple1D;
        tree.blendParameter = "RunBlend";
        tree.useAutomaticThresholds = false;
        tree.children = new[]
        {
            new ChildMotion
            {
                motion = AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + "/Animations/Walk.anim"),
                threshold = 0,
                timeScale = 1
            },
            new ChildMotion
            {
                motion = AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + "/Animations/Run.anim"),
                threshold = 1,
                timeScale = 1
            }
        };
        state.motion = tree;
        state.speedParameter = "WalkRate";
        state.speedParameterActive = true;
        EditorUtility.SetDirty(tree);
        EditorUtility.SetDirty(controller);
    }
}
