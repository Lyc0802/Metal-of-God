using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;

// One-time, repeatable editor authoring tool for this model's A-pose and proportions.
// Weights, bind poses and animation curves are baked assets; no runtime procedural skinning.
/// <summary>
/// Editor 工具：建立角色骨架、蒙皮與動畫整合所需的視覺階層。
/// </summary>
public static class AssassinRigBuilder
{
    private const string Source = "Assets/assassin-character-model/Assassin Model.prefab";
    private const string Folder = "Assets/assassin-character-model/Rigging";
    private static readonly List<Transform> Bones = new List<Transform>();
    private static readonly Dictionary<string, int> Id = new Dictionary<string, int>();
    private static Transform Root;
    private static float Smooth(float a, float b, float value)
    {
        return Mathf.SmoothStep(0, 1, Mathf.InverseLerp(a, b, value));
    }

    private static Transform Bone(string name, Transform parent, Vector3 position)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.position = Root.TransformPoint(position);
        Id[name] = Bones.Count;
        Bones.Add(t);
        return t;
    }

    private static Transform B(string name)
    {
        return Bones[Id[name]];
    }

    private static void Add(Dictionary<int, float> weights, string name, float weight)
    {
        if (weight <= .00001f)
        {
            return;
        }

        int i = Id[name];
        if (weights.ContainsKey(i))
        {
            weights[i] += weight;
        }
        else
        {
            weights.Add(i, weight);
        }
    }

    private static void Pair(Dictionary<int, float> weights, string a, string b, float t, float amount = 1)
    {
        Add(weights, a, (1 - t) * amount);
        Add(weights, b, t * amount);
    }

    private static void Torso(Dictionary<int, float> weights, Vector3 p, float amount)
    {
        if (p.y < .445f)
        {
            Pair(weights, "Hips", "Spine", Smooth(.385f, .445f, p.y), amount);
        }
        else if (p.y < .515f)
        {
            Pair(weights, "Spine", "Chest", Smooth(.445f, .515f, p.y), amount);
        }
        else if (p.y < .555f)
        {
            Pair(weights, "Chest", "UpperChest", Smooth(.515f, .555f, p.y), amount);
        }
        else if (p.y < .59f)
        {
            Pair(weights, "UpperChest", "Neck", Smooth(.555f, .59f, p.y), amount);
        }
        else
        {
            Pair(weights, "Neck", "Head", Smooth(.59f, .615f, p.y), amount);
        }
    }

    private static void Leg(Dictionary<int, float> weights, Vector3 p, float amount)
    {
        string side = p.x >= 0 ? "Left" : "Right";
        // The shared crotch seam follows the pelvis; it must not jump between opposite thighs.
        float pelvis = (1 - Smooth(.012f, .045f, Mathf.Abs(p.x))) * Smooth(.28f, .32f, p.y);
        Add(weights, "Hips", amount * pelvis);
        amount *= 1 - pelvis;
        if (p.y > .335f)
        {
            Pair(weights, side + "UpperLeg", "Hips", Smooth(.335f, .385f, p.y), amount);
        }
        else if (p.y > .18f)
        {
            Pair(weights, side + "LowerLeg", side + "UpperLeg", Smooth(.18f, .245f, p.y), amount);
        }
        else if (p.y > .04f)
        {
            Pair(weights, side + "Foot", side + "LowerLeg", Smooth(.045f, .085f, p.y), amount);
        }
        else
        {
            Pair(weights, side + "Foot", side + "Toes", Smooth(.025f, .065f, p.z), amount);
        }
    }

    private static Dictionary<int, float> BodyWeights(Vector3 p)
    {
        var weights = new Dictionary<int, float>();
        float ax = Mathf.Abs(p.x);
        if (p.y < .395f && ax < .16f)
        {
            Leg(weights, p, 1);
            return weights;
        }

        float arm = p.y > .395f ? Smooth(.055f, .145f, ax) : 0;
        Torso(weights, p, 1 - arm);
        if (arm > 0)
        {
            string side = p.x >= 0 ? "Left" : "Right";
            if (ax < .153f)
            {
                Add(weights, side + "UpperArm", arm);
            }
            else if (ax < .218f)
            {
                Pair(weights, side + "UpperArm", side + "LowerArm", Smooth(.163f, .205f, ax), arm);
            }
            else
            {
                Pair(weights, side + "LowerArm", side + "Hand", Smooth(.222f, .252f, ax), arm);
            }
        }

        return weights;
    }

    private static string ClothChain(string part)
    {
        switch (part)
        {
            case "cloth3":
                return "FrontCloth";
            case "cloth5":
                return "BackLeftCloth";
            case "cloth9":
                return "BackRightCloth";
            case "cloth10":
                return "RightCloth";
            case "cloth12":
                return "LeftCloth";
            default:
                return null;
        }
    }

    private static BoneWeight Weight(Vector3 p, string part, int rigid)
    {
        if (rigid >= 0)
        {
            return new BoneWeight
            {
                boneIndex0 = rigid,
                weight0 = 1
            };
        }

        Dictionary<int, float> weights;
        var cloth = ClothChain(part);
        if (cloth != null)
        {
            weights = new Dictionary<int, float>();
            if (p.y > .22f)
            {
                Pair(weights, cloth + "Lower", cloth, Smooth(.22f, .375f, p.y));
            }
            else
            {
                Pair(weights, cloth + "Tip", cloth + "Lower", Smooth(.065f, .22f, p.y));
            }
        }
        else if (part == "cloth2")
        {
            // Hood/cape is one continuous panel, including vertices behind the shoulders.
            // Keep its back attached to the torso instead of stretching with nearby arms.
            weights = new Dictionary<int, float>();
            Torso(weights, p, 1);
        }
        else
        {
            weights = BodyWeights(p);
        }

        var best = weights.OrderByDescending(w => w.Value).Take(4).ToArray();
        float total = best.Sum(w => w.Value);
        if (total <= 0)
        {
            throw new InvalidOperationException("Unweighted vertex: " + part + " " + p);
        }

        var w = new BoneWeight();
        if (best.Length > 0)
        {
            w.boneIndex0 = best[0].Key;
            w.weight0 = best[0].Value / total;
        }

        if (best.Length > 1)
        {
            w.boneIndex1 = best[1].Key;
            w.weight1 = best[1].Value / total;
        }

        if (best.Length > 2)
        {
            w.boneIndex2 = best[2].Key;
            w.weight2 = best[2].Value / total;
        }

        if (best.Length > 3)
        {
            w.boneIndex3 = best[3].Key;
            w.weight3 = best[3].Value / total;
        }

        return w;
    }

    private static void Skeleton()
    {
        var rig = new GameObject("Rig").transform;
        rig.SetParent(Root, false);
        var hips = Bone("Hips", rig, new Vector3(0, .385f, 0));
        var spine = Bone("Spine", hips, new Vector3(0, .445f, 0));
        var chest = Bone("Chest", spine, new Vector3(0, .515f, -.005f));
        var upper = Bone("UpperChest", chest, new Vector3(0, .555f, -.005f));
        var neck = Bone("Neck", upper, new Vector3(0, .59f, .005f));
        var head = Bone("Head", neck, new Vector3(0, .635f, .007f));
        Bone("HeadEnd", head, new Vector3(0, .695f, .007f));
        foreach (string side in new[]
        {
            "Left",
            "Right"
        }

        )
        {
            float sign = side == "Left" ? 1 : -1;
            var shoulder = Bone(side + "Shoulder", upper, new Vector3(sign * .073f, .553f, -.007f));
            var arm = Bone(side + "UpperArm", shoulder, new Vector3(sign * .098f, .544f, -.007f));
            var forearm = Bone(side + "LowerArm", arm, new Vector3(sign * .187f, .484f, -.006f));
            var hand = Bone(side + "Hand", forearm, new Vector3(sign * .243f, .435f, .027f));
            Bone(side + "HandEnd", hand, new Vector3(sign * .287f, .407f, .035f));
            var thigh = Bone(side + "UpperLeg", hips, new Vector3(sign * .047f, .375f, -.005f));
            var shin = Bone(side + "LowerLeg", thigh, new Vector3(sign * .057f, .215f, .008f));
            var foot = Bone(side + "Foot", shin, new Vector3(sign * .063f, .062f, -.008f));
            Bone(side + "Toes", foot, new Vector3(sign * .063f, .025f, .05f));
        }

        foreach (var entry in new[]
        {
            new Vector3(0, .375f, .048f),
            new Vector3(-.035f, .375f, -.05f),
            new Vector3(.035f, .375f, -.05f),
            new Vector3(.065f, .375f, -.015f),
            new Vector3(-.065f, .375f, -.015f)
        }

        )
        {
            string[] names =
            {
                "FrontCloth",
                "BackLeftCloth",
                "BackRightCloth",
                "RightCloth",
                "LeftCloth"
            };
            string name = names[(Bones.Count - 25) / 3];
            var top = Bone(name, hips, entry);
            var lower = Bone(name + "Lower", top, new Vector3(entry.x, .22f, entry.z));
            Bone(name + "Tip", lower, new Vector3(entry.x, .065f, entry.z));
        }
    }

    private static Mesh Skin(GameObject source, out Material[] materials)
    {
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var tangents = new List<Vector4>();
        var uv = new List<Vector2>();
        var weights = new List<BoneWeight>();
        var colors = new List<Color>();
        var mats = new List<Material>();
        var indices = new List<List<int>>();
        foreach (var mf in source.GetComponentsInChildren<MeshFilter>(true))
        {
            var renderer = mf.GetComponent<MeshRenderer>();
            if (!renderer || !renderer.enabled || !mf.gameObject.activeInHierarchy)
            {
                continue;
            }

            var mesh = mf.sharedMesh;
            var matrix = Root.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            var normalMatrix = matrix.inverse.transpose;
            bool mirrored = matrix.determinant < 0;
            var v = mesh.vertices;
            var n = mesh.normals;
            var tan = mesh.tangents;
            var tex = mesh.uv;
            var col = mesh.colors;
            int offset = vertices.Count;
            var center = matrix.MultiplyPoint3x4(mesh.bounds.center);
            int rigid = -1;
            // Small metal/leather buckles and hard gauntlet pieces retain their shape.
            if (mf.name.StartsWith("Accessories") || mf.name.StartsWith("Arm_Accessories"))
            {
                var candidate = BodyWeights(center);
                rigid = candidate.OrderByDescending(w => w.Value).First().Key;
            }

            for (int i = 0; i < v.Length; i++)
            {
                var point = matrix.MultiplyPoint3x4(v[i]);
                vertices.Add(point);
                normals.Add(n.Length == v.Length ? normalMatrix.MultiplyVector(n[i]).normalized : Vector3.up);
                var t = tan.Length == v.Length ? tan[i] : new Vector4(1, 0, 0, 1);
                var direction = matrix.MultiplyVector(new Vector3(t.x, t.y, t.z)).normalized;
                tangents.Add(new Vector4(direction.x, direction.y, direction.z, t.w * (mirrored ? -1 : 1)));
                uv.Add(tex.Length == v.Length ? tex[i] : Vector2.zero);
                colors.Add(col.Length == v.Length ? col[i] : Color.white);
                weights.Add(Weight(point, mf.name, rigid));
            }

            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                var mat = renderer.sharedMaterials[Mathf.Min(sub, renderer.sharedMaterials.Length - 1)];
                int index = mats.IndexOf(mat);
                if (index < 0)
                {
                    index = mats.Count;
                    mats.Add(mat);
                    indices.Add(new List<int>());
                }

                var triangles = mesh.GetTriangles(sub);
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    indices[index].Add(offset + triangles[i]);
                    indices[index].Add(offset + triangles[i + (mirrored ? 2 : 1)]);
                    indices[index].Add(offset + triangles[i + (mirrored ? 1 : 2)]);
                }
            }
        }

        var result = new Mesh
        {
            name = "Assassin Skinned Mesh",
            indexFormat = IndexFormat.UInt32
        };
        result.SetVertices(vertices);
        result.SetNormals(normals);
        result.SetTangents(tangents);
        result.SetUVs(0, uv);
        result.SetColors(colors);
        result.boneWeights = weights.ToArray();
        result.bindposes = Bones.Select(b => b.worldToLocalMatrix * Root.localToWorldMatrix).ToArray();
        result.subMeshCount = mats.Count;
        for (int i = 0; i < mats.Count; i++)
        {
            result.SetTriangles(indices[i], i);
        }

        result.RecalculateBounds();
        materials = mats.ToArray();
        return result;
    }

    private static void Curve(
        AnimationClip clip,
        Transform bone,
        string property,
        Func<float, float> value,
        float duration)
    {
        int frames = Mathf.RoundToInt(duration * 60);
        var keys = new Keyframe[frames + 1];
        for (int i = 0; i <= frames; i++)
        {
            float t = duration * i / frames;
            keys[i] = new Keyframe(t, value(t));
        }

        var curve = new AnimationCurve(keys);
        for (int i = 0; i < keys.Length; i++)
        {
            AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
            AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
        }

        AnimationUtility.SetEditorCurve(
            clip,
            EditorCurveBinding.FloatCurve(AnimationUtility.CalculateTransformPath(bone, Root), typeof(Transform), property),
            curve);
    }

    private static void Rotation(AnimationClip clip, string bone, Func<float, Vector3> euler, float duration)
    {
        var transform = B(bone);
        Curve(clip, transform, "localRotation.x", t => Quaternion.Euler(euler(t)).x, duration);
        Curve(clip, transform, "localRotation.y", t => Quaternion.Euler(euler(t)).y, duration);
        Curve(clip, transform, "localRotation.z", t => Quaternion.Euler(euler(t)).z, duration);
        Curve(clip, transform, "localRotation.w", t => Quaternion.Euler(euler(t)).w, duration);
    }

    private static Vector3 Idle(string name, float t)
    {
        float wave = Mathf.Sin(t * Mathf.PI), breath = Mathf.Sin(t * Mathf.PI + .4f);
        switch (name)
        {
            case "Spine":
                return new Vector3(.7f * wave, 0, .4f * breath);
            case "Chest":
                return new Vector3(1.2f * wave, 0, 0);
            case "Neck":
                return new Vector3(-.5f * wave, 0, 0);
            case "Head":
                return new Vector3(-.7f * wave, .8f * breath, 0);
            case "LeftUpperArm":
                return new Vector3(-5, 0, -43 + wave);
            case "RightUpperArm":
                return new Vector3(-5, 0, 43 - wave);
            case "LeftLowerArm":
            case "RightLowerArm":
                return new Vector3(-10, 0, 0);
            default:
                return Vector3.zero;
        }
    }

    private static Vector3 WalkLeg(string name, float t)
    {
        string side = name.StartsWith("Left") ? "Left" : "Right";
        float phase = t * 2 * Mathf.PI + (side == "Left" ? 0 : Mathf.PI);
        var hip = Root.InverseTransformPoint(B(side + "UpperLeg").position);
        var knee = Root.InverseTransformPoint(B(side + "LowerLeg").position);
        var ankle = Root.InverseTransformPoint(B(side + "Foot").position);
        Vector2 upper = new Vector2(knee.z - hip.z, hip.y - knee.y), lower = new Vector2(
            ankle.z - knee.z,
            knee.y - ankle.y);
        float a = upper.magnitude, b = lower.magnitude;
        float bob = -.009f + .002f * Mathf.Cos(phase * 2);
        float lift = .028f * Mathf.Pow(Mathf.Max(0, Mathf.Sin(phase)), 1.25f);
        float dz = (ankle.z - hip.z) + .046f * Mathf.Cos(phase), dy = hip.y + bob - ankle.y - lift;
        float d = Mathf.Min(Mathf.Sqrt(dz * dz + dy * dy), a + b - .0001f);
        float direction = Mathf.Atan2(dz, dy);
        float theta1 = direction + Mathf.Acos(Mathf.Clamp((a * a + d * d - b * b) / (2 * a * d), -1, 1));
        float theta2 = direction - Mathf.Acos(Mathf.Clamp((b * b + d * d - a * a) / (2 * b * d), -1, 1));
        float rx1 = -(theta1 - Mathf.Atan2(upper.x, upper.y)) * Mathf.Rad2Deg;
        float rx2 = -(theta2 - Mathf.Atan2(lower.x, lower.y)) * Mathf.Rad2Deg - rx1;
        if (name.EndsWith("UpperLeg"))
        {
            return new Vector3(rx1, 0, 0);
        }

        if (name.EndsWith("LowerLeg"))
        {
            return new Vector3(rx2, 0, 0);
        }

        return new Vector3(-rx1 - rx2, 0, 0);
    }

    private static Vector3 Walk(string name, float t)
    {
        float phase = t * 2 * Mathf.PI, wave = Mathf.Sin(phase);
        if (name.EndsWith("UpperLeg") || name.EndsWith("LowerLeg") || name.EndsWith("Foot"))
        {
            return WalkLeg(name, t);
        }

        if (name == "Hips")
        {
            return new Vector3(0, 2 * wave, 1.5f * Mathf.Cos(phase));
        }

        if (name == "Chest")
        {
            return new Vector3(2, -4 * wave, 0);
        }

        if (name == "Head")
        {
            return new Vector3(-2, 2 * wave, 0);
        }

        if (name == "LeftUpperArm")
        {
            return new Vector3(-5 + 19 * Mathf.Cos(phase), 0, -43);
        }

        if (name == "RightUpperArm")
        {
            return new Vector3(-5 - 19 * Mathf.Cos(phase), 0, 43);
        }

        if (name.EndsWith("LowerArm"))
        {
            return new Vector3(-12, 0, 0);
        }

        if (name.Contains("Cloth"))
        {
            float amount = name.StartsWith("Front") ? -1 : 1;
            return new Vector3(4 * amount + 4 * Mathf.Sin(phase + .5f), 0, 1.5f * wave);
        }

        return Vector3.zero;
    }

    private static float AttackValue(float t, float[] values)
    {
        float[] times =
        {
            0,
            .12f,
            .24f,
            .36f,
            .58f
        };
        for (int i = 1; i < times.Length; i++)
        {
            if (t <= times[i])
            {
                return Mathf.Lerp(values[i - 1], values[i], Smooth(times[i - 1], times[i], t));
            }
        }

        return values[values.Length - 1];
    }

    private static Vector3 Attack(string name, float t)
    {
        if (name == "Hips")
        {
            return new Vector3(0, AttackValue(t, new float[] { 0, -10, 20, 24, 0 }), 0);
        }

        if (name == "Spine")
        {
            return new Vector3(AttackValue(t, new float[] { 0, -3, 8, 5, 0 }), 0, 0);
        }

        if (name == "Chest")
        {
            return new Vector3(0, AttackValue(t, new float[] { 0, -16, 16, 12, 0 }), 0);
        }

        if (name == "Head")
        {
            return new Vector3(0, AttackValue(t, new float[] { 0, 12, -15, -10, 0 }), 0);
        }

        if (name == "RightUpperArm")
        {
            return new Vector3(
                AttackValue(t, new float[] { -5, -32, -86, -75, -5 }),
                AttackValue(t, new float[] { 0, -10, 5, 35, 0 }),
                AttackValue(t, new float[] { 43, 70, 18, 12, 43 }));
        }

        if (name == "RightLowerArm")
        {
            return new Vector3(AttackValue(t, new float[] { -10, -65, 12, 8, -10 }), 0, 0);
        }

        if (name == "LeftUpperArm")
        {
            return new Vector3(AttackValue(t, new float[] { -5, -25, -35, -25, -5 }), 0, -43);
        }

        if (name == "LeftLowerArm")
        {
            return new Vector3(AttackValue(t, new float[] { -10, -75, -85, -65, -10 }), 0, 0);
        }

        if (name.Contains("Cloth"))
        {
            return new Vector3(4 * Mathf.Sin(t / .58f * Mathf.PI), 0, 3 * Mathf.Sin(t / .58f * Mathf.PI));
        }

        return Idle(name, 0);
    }

    private static AnimationClip Clip(string name, float duration, bool loop, Func<string, float, Vector3> pose)
    {
        var clip = new AnimationClip
        {
            name = name,
            frameRate = 60
        };
        foreach (var bone in Bones)
        {
            Rotation(clip, bone.name, t => pose(bone.name, t), duration);
        }

        if (name == "Walk")
        {
            Curve(
                clip,
                B("Hips"),
                "m_LocalPosition.y",
                t => .385f - .009f + .002f * Mathf.Cos(t * 4 * Mathf.PI),
                duration);
        }
        else
        {
            Curve(clip, B("Hips"), "m_LocalPosition.y", t => .385f, duration);
        }

        clip.EnsureQuaternionContinuity();
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop;
        settings.loopBlend = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        return SaveAsset(clip, Folder + "/Animations/" + name + ".anim");
    }

    private static T SaveAsset<T>(T asset, string path)
        where T : UnityEngine.Object
    {
        var existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if (existing)
        {
            EditorUtility.CopySerialized(asset, existing);
            EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(asset);
            return existing;
        }

        AssetDatabase.CreateAsset(asset, path);
        return asset;
    }

    private static AnimatorController Controller(AnimationClip idle, AnimationClip walk, AnimationClip attack)
    {
        string path = Folder + "/Assassin.controller";
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        if (!controller)
        {
            controller = AnimatorController.CreateAnimatorControllerAtPath(path);
        }
        else
        {
            var old = controller.layers[0].stateMachine;
            foreach (var state in old.states)
            {
                old.RemoveState(state.state);
            }

            foreach (var transition in old.anyStateTransitions)
            {
                old.RemoveAnyStateTransition(transition);
            }

            controller.parameters = new AnimatorControllerParameter[0];
        }

        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
        var machine = controller.layers[0].stateMachine;
        var a = machine.AddState("Idle");
        a.motion = idle;
        var b = machine.AddState("Walk");
        b.motion = walk;
        var c = machine.AddState("Attack");
        c.motion = attack;
        machine.defaultState = a;
        var ab = a.AddTransition(b);
        ab.hasExitTime = false;
        ab.duration = .14f;
        ab.AddCondition(AnimatorConditionMode.Greater, .1f, "Speed");
        var ba = b.AddTransition(a);
        ba.hasExitTime = false;
        ba.duration = .14f;
        ba.AddCondition(AnimatorConditionMode.Less, .1f, "Speed");
        var hit = machine.AddAnyStateTransition(c);
        hit.hasExitTime = false;
        hit.duration = .065f;
        hit.canTransitionToSelf = false;
        hit.AddCondition(AnimatorConditionMode.If, 0, "Attack");
        var end = c.AddTransition(a);
        end.hasExitTime = true;
        end.exitTime = 1;
        end.duration = .1f;
        return controller;
    }

    [MenuItem("Tools/Assassin/Build Rig and Animations")]
    private static void BuildMenu()
    {
        Debug.Log(Build());
    }

    public static string Build()
    {
        if (Application.isPlaying)
        {
            throw new InvalidOperationException("Exit Play Mode before building the rig.");
        }

        System.IO.Directory.CreateDirectory(Folder + "/Animations");
        AssetDatabase.Refresh();
        Bones.Clear();
        Id.Clear();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/Source/Assassin Unrigged.prefab");
        var source = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        source.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        source.transform.localScale = Vector3.one;
        var model = new GameObject("Assassin Model");
        Root = model.transform;
        try
        {
            Skeleton();
            Material[] materials;
            var mesh = Skin(source, out materials);
            SaveAsset(mesh, Folder + "/AssassinSkinnedMesh.asset");
            mesh = AssetDatabase.LoadAssetAtPath<Mesh>(Folder + "/AssassinSkinnedMesh.asset");
            var body = new GameObject("Assassin Body");
            body.transform.SetParent(Root, false);
            var skinned = body.AddComponent<SkinnedMeshRenderer>();
            skinned.sharedMesh = mesh;
            skinned.sharedMaterials = materials;
            skinned.bones = Bones.ToArray();
            skinned.rootBone = B("Hips");
            skinned.quality = SkinQuality.Bone4;
            skinned.localBounds = new Bounds(new Vector3(0, .36f, 0), new Vector3(1.1f, .95f, .8f));
            skinned.shadowCastingMode = ShadowCastingMode.On;
            skinned.receiveShadows = true;
            var idle = Clip("Idle", 2, true, Idle);
            var walk = Clip("Walk", 1, true, Walk);
            var attack = Clip("Attack", .58f, false, Attack);
            var controller = Controller(idle, walk, attack);
            // Rebuilding the mesh must also preserve the improved motion authoring.
            AssassinMotionAuthoring.Rebuild(Root);
            var avatar = AvatarBuilder.BuildGenericAvatar(model, "Hips");
            avatar.name = "Assassin Generic Avatar";
            SaveAsset(avatar, Folder + "/AssassinAvatar.asset");
            avatar = AssetDatabase.LoadAssetAtPath<Avatar>(Folder + "/AssassinAvatar.asset");
            var animator = model.AddComponent<Animator>();
            animator.avatar = avatar;
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            PrefabUtility.SaveAsPrefabAsset(model, Source);
            AssetDatabase.SaveAssets();
            return "Rigged " + mesh.vertexCount + " vertices, " + Bones.Count + " bones, " + materials.Length + " materials; Idle/Walk/Attack generated.";
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(source);
            UnityEngine.Object.DestroyImmediate(model);
            Root = null;
        }
    }
}
