using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Offline upgrade of the authored Generic rig. Existing animation paths and bone indices stay intact.
public static class AssassinFingerRig
{
    private const string Folder = "Assets/assassin-character-model/Rigging";
    private const string PrefabPath = "Assets/assassin-character-model/Assassin Model.prefab";
    private static readonly string[] Names = { "Thumb", "Index", "Middle", "Ring", "Little" };
    private static readonly string[] Joints = { "Proximal", "Intermediate", "Distal", "Tip" };

    // Joint centres measured from the existing mesh in its model-space A-pose.
    private static readonly Vector3[][] Centres =
    {
        new[] { V(.240f,.430f,.035f), V(.237f,.421f,.045f), V(.2355f,.415f,.052f), V(.2353f,.4115f,.0585f) },
        new[] { V(.2635f,.4174f,.0467f), V(.2758f,.4092f,.0503f), V(.2831f,.4025f,.0535f), V(.2879f,.4000f,.0544f) },
        new[] { V(.2643f,.4182f,.0382f), V(.2770f,.4098f,.0417f), V(.2852f,.4028f,.0447f), V(.2912f,.3979f,.0464f) },
        new[] { V(.2649f,.4195f,.0288f), V(.2772f,.4110f,.0324f), V(.2850f,.4045f,.0352f), V(.2902f,.4009f,.0364f) },
        new[] { V(.2655f,.4205f,.0202f), V(.2775f,.4125f,.0235f), V(.2840f,.4072f,.0256f), V(.2882f,.4030f,.0270f) }
    };

    // Proximal phalanx length relative to the measured mesh centres. The Intermediate joint is pulled
    // toward Proximal; Distal and Tip stay on the mesh.
    private const float ProximalLengthScale = .775f;

    private static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);

    private static Vector3[] JointPoints(int finger, float sign)
    {
        var points = Centres[finger].Select(p => V(sign * p.x, p.y, p.z)).ToArray();
        points[1] = Vector3.Lerp(points[0], points[1], ProximalLengthScale);
        return points;
    }

    private sealed class Finger
    {
        public Vector3[] Points;
        public int[] Indices;
        public string Name;
    }

    private static float Distance(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 d = b - a;
        return (p - (a + d * Mathf.Clamp01(Vector3.Dot(p - a, d) / d.sqrMagnitude))).sqrMagnitude;
    }

    private static void Add(Dictionary<int, float> weights, int index, float amount)
    {
        if (amount <= 0) return;
        float old;
        weights.TryGetValue(index, out old);
        weights[index] = old + amount;
    }

    private static BoneWeight SkinWeight(Vector3 point, BoneWeight original, List<Finger> fingers)
    {
        Finger nearest = null;
        float best = .009f * .009f;
        foreach (var finger in fingers)
            for (int j = 0; j < 3; j++)
            {
                float distance = Distance(point, finger.Points[j], finger.Points[j + 1]);
                if (distance < best) { best = distance; nearest = finger; }
            }
        if (nearest == null) return original;

        var p = nearest.Points;
        float along = Vector3.Dot(point - p[0], (p[1] - p[0]).normalized);
        float influence = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-.004f, .004f, along));
        if (influence <= 0) return original;

        // Blend adjacent phalanges only near each joint, keeping each finger independent.
        float middle = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-.002f, .002f,
            Vector3.Dot(point - p[1], (p[2] - p[0]).normalized)));
        float distal = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-.0015f, .0015f,
            Vector3.Dot(point - p[2], (p[3] - p[1]).normalized)));
        var weights = new Dictionary<int, float>();
        Add(weights, original.boneIndex0, original.weight0 * (1 - influence));
        Add(weights, original.boneIndex1, original.weight1 * (1 - influence));
        Add(weights, original.boneIndex2, original.weight2 * (1 - influence));
        Add(weights, original.boneIndex3, original.weight3 * (1 - influence));
        Add(weights, nearest.Indices[0], influence * (1 - middle));
        Add(weights, nearest.Indices[1], influence * middle * (1 - distal));
        Add(weights, nearest.Indices[2], influence * middle * distal);
        var ordered = weights.OrderByDescending(w => w.Value).Take(4).ToArray();
        float sum = ordered.Sum(w => w.Value);
        var result = new BoneWeight();
        for (int i = 0; i < ordered.Length; i++)
        {
            int index = ordered[i].Key;
            float weight = ordered[i].Value / sum;
            switch (i)
            {
                case 0: result.boneIndex0 = index; result.weight0 = weight; break;
                case 1: result.boneIndex1 = index; result.weight1 = weight; break;
                case 2: result.boneIndex2 = index; result.weight2 = weight; break;
                case 3: result.boneIndex3 = index; result.weight3 = weight; break;
            }
        }
        return result;
    }

    private static Mesh PrepareMesh(Mesh source, Matrix4x4[] bindposes, List<Finger> fingers)
    {
        // The source author's Face_And_Chest material occupies submesh 3 in both baked meshes.
        // Restrict to its skin vertices: gauntlet plates and buckles must keep their old weights.
        if (source == null || source.subMeshCount != 6 || source.bindposes.Length < 40)
            throw new InvalidOperationException("Unexpected Assassin mesh layout; refusing to replace its skin weights.");
        var mesh = UnityEngine.Object.Instantiate(source);
        mesh.name = source.name;
        var vertices = mesh.vertices;
        var weights = mesh.boneWeights;
        foreach (int i in mesh.GetTriangles(3).Distinct())
        {
            Vector3 p = vertices[i];
            if (Mathf.Abs(p.x) < .22f || Mathf.Abs(p.x) > .305f || p.y < .385f || p.y > .45f) continue;
            weights[i] = SkinWeight(p, weights[i], fingers);
        }
        mesh.boneWeights = weights;
        mesh.bindposes = bindposes;
        // Structural authoring guard, without entering Play Mode or changing any poses.
        var used = new HashSet<int>();
        foreach (var w in weights)
        {
            if (Mathf.Abs(w.weight0 + w.weight1 + w.weight2 + w.weight3 - 1) > .0001f)
                throw new InvalidOperationException("Non-normalized skin weights.");
            if (w.weight0 > .001f) used.Add(w.boneIndex0);
            if (w.weight1 > .001f) used.Add(w.boneIndex1);
            if (w.weight2 > .001f) used.Add(w.boneIndex2);
            if (w.weight3 > .001f) used.Add(w.boneIndex3);
        }
        foreach (var finger in fingers)
            for (int j = 0; j < 3; j++)
                if (!used.Contains(finger.Indices[j]))
                    throw new InvalidOperationException("No skin weights for " + finger.Name + Joints[j]);
        return mesh;
    }

    // Used by the original full builder too, so a later rebuild retains the fingers.
    public static void Configure(GameObject model) => Configure(model, false);

    private static void Configure(GameObject model, bool refit)
    {
        var body = model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(s => s.name == "Assassin Body");
        var bones = body.bones.ToList();
        if (bones.Count != 40 && bones.Count != 80)
            throw new InvalidOperationException("Expected the original 40 bones or the upgraded 80-bone rig.");
        var fingers = new List<Finger>();
        foreach (string side in new[] { "Left", "Right" })
        {
            float sign = side == "Left" ? 1 : -1;
            var hand = bones.Single(b => b.name == side + "Hand");
            for (int f = 0; f < Names.Length; f++)
            {
                var points = JointPoints(f, sign);
                var finger = new Finger { Points = points, Indices = new int[4], Name = side + Names[f] };
                var parent = hand;
                for (int j = 0; j < 4; j++)
                {
                    string name = finger.Name + Joints[j];
                    var joint = parent.Find(name);
                    if (!joint || refit)
                    {
                        if (!joint)
                        {
                            joint = new GameObject(name).transform;
                            joint.SetParent(parent, false);
                        }
                        joint.position = model.transform.TransformPoint(points[j]);
                        Vector3 direction = points[Mathf.Min(j + 1, 3)] - points[j == 3 ? 2 : j];
                        Vector3 normal = Vector3.Cross(direction, V(0, 0, sign)).normalized;
                        joint.rotation = model.transform.rotation * Quaternion.LookRotation(normal, direction.normalized);
                    }
                    if (!bones.Contains(joint)) bones.Add(joint);
                    finger.Indices[j] = bones.IndexOf(joint);
                    parent = joint;
                }
                fingers.Add(finger);
            }
        }
        // Preserve the original bind poses exactly; append only the new joints.
        var poses = body.sharedMesh.bindposes.Take(refit ? 40 : int.MaxValue).ToList();
        for (int i = poses.Count; i < bones.Count; i++)
            poses.Add(bones[i].worldToLocalMatrix * body.transform.localToWorldMatrix);
        var meshes = new List<Mesh> { body.sharedMesh };
        var original = AssetDatabase.LoadAssetAtPath<Mesh>(Folder + "/AssassinSkinnedMesh.asset");
        if (original && !meshes.Contains(original)) meshes.Add(original);
        var prepared = new List<Mesh>();
        try
        {
            foreach (var mesh in meshes) prepared.Add(PrepareMesh(mesh, poses.ToArray(), fingers));
            for (int i = 0; i < meshes.Count; i++)
            {
                EditorUtility.CopySerialized(prepared[i], meshes[i]);
                EditorUtility.SetDirty(meshes[i]);
            }
            body.bones = bones.ToArray();
            EditorUtility.SetDirty(body);
        }
        finally
        {
            foreach (var mesh in prepared) UnityEngine.Object.DestroyImmediate(mesh);
        }
    }

    [MenuItem("Tools/Assassin/Add Finger Rig")]
    public static void Upgrade() => Upgrade(false);

    // Moves the existing finger joints to the current JointPoints and rebinds/reweights the finger skin.
    [MenuItem("Tools/Assassin/Refit Finger Joints")]
    public static void Refit() => Upgrade(true);

    private static void Upgrade(bool refit)
    {
        if (Application.isPlaying) throw new InvalidOperationException("Exit Play Mode before editing the rig.");
        var model = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            Configure(model, refit);
            var animator = model.GetComponent<Animator>();
            var avatar = AvatarBuilder.BuildGenericAvatar(model, "Hips");
            if (!avatar.isValid) { UnityEngine.Object.DestroyImmediate(avatar); throw new InvalidOperationException("Invalid rebuilt Generic Avatar."); }
            avatar.name = animator.avatar.name;
            EditorUtility.CopySerialized(avatar, animator.avatar);
            EditorUtility.SetDirty(animator.avatar);
            UnityEngine.Object.DestroyImmediate(avatar);
            PrefabUtility.SaveAsPrefabAsset(model, PrefabPath);
            AssetDatabase.SaveAssets();
        }
        finally { PrefabUtility.UnloadPrefabContents(model); }

        // Keep the open scene and all of its existing prefab overrides; do not reload it.
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.path == "Assets/Scenes/MetalPushPullTest.unity") EditorSceneManager.SaveScene(scene);
        Debug.Log("Assassin finger rig saved: 30 weighted finger joints + 10 tips; 80 body bones. Generic Avatar rebuilt; animations and cape weights preserved.");
    }
}
