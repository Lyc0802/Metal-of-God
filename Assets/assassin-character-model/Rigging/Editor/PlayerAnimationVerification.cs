using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// Repeatable checks against the real assets and gameplay scene, without saved test objects.
/// <summary>
/// 驗證角色動畫資產與遊戲整合；驗證與影像輸出入口分開呼叫。
/// </summary>
public static class PlayerAnimationVerification
{
    private const string Output = "Temp/AnimationRework";
    private static IEnumerator routine;
    private static readonly List<string> Results = new List<string>();
    private static PlayerController controller;
    private static Enemy dummy;
    private static float originalTimeScale;
    private static int lastFrame;
    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }

        Results.Add("PASS " + message);
    }

    private static AnimationClip Clip(string name) => AssetDatabase.LoadAssetAtPath<AnimationClip>(AssassinMotionAuthoring.Folder + "/Animations/" + name + ".anim");
    private static GameObject Visual() => UnityEngine.Object.Instantiate(UnityEngine.Object.FindFirstObjectByType<PlayerCharacterAnimation>().CharacterAnimator.gameObject);
    public static string VerifyAssets()
    {
        var report = new List<string>();
        foreach (string name in new[]
        {
            "Idle",
            "Walk",
            "Run",
            "Attack"
        }

        )
        {
            var clip = Clip(name);
            var bindings = AnimationUtility.GetCurveBindings(clip);
            float seam = 0, slope = 0;
            foreach (var binding in bindings)
            {
                if (!binding.path.StartsWith("Rig/"))
                {
                    throw new InvalidOperationException("Root motion binding: " + binding.path);
                }

                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                var keys = curve.keys;
                foreach (var k in keys)
                {
                    if (float.IsNaN(k.value) || float.IsInfinity(k.value))
                    {
                        throw new InvalidOperationException("Invalid curve");
                    }
                }

                seam = Mathf.Max(seam, Mathf.Abs(keys[0].value - keys[keys.Length - 1].value));
                slope = Mathf.Max(slope, Mathf.Abs(keys[0].outTangent - keys[keys.Length - 1].inTangent));
            }

            if (name != "Attack" && (seam > .00001f || slope > .00001f))
            {
                throw new InvalidOperationException("Loop seam: " + name);
            }

            report.Add(name + ": " + clip.length + "s, " + bindings.Length + " finite visual-only curves; seam=" + seam + ", tangent seam=" + slope);
        }

        var model = Visual();
        try
        {
            model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            model.transform.localScale = Vector3.one;
            model.GetComponent<Animator>().enabled = false;
            var foot = model.GetComponentsInChildren<Transform>().First(t => t.name == "LeftFoot");
            foreach (string name in new[]
            {
                "Walk",
                "Run"
            }

            )
            {
                string path = "Assets/assassin-character-model/" + (name == "Walk" ? "Standard Walk" : "Fast Run") + ".fbx";
                var reference = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c => !c.name.StartsWith("__"));
                if (Mathf.Abs(Clip(name).length - reference.length) > .001f)
                {
                    throw new InvalidOperationException(name + " reference duration changed");
                }

                report.Add(name + " retains reference cycle duration: " + reference.length.ToString("F3") + "s");
            }

            var bones = model.GetComponentsInChildren<Transform>().ToDictionary(t => t.name);
            float walkKnee = 0, runKnee = 0, walkLean = 0, runLean = 0, runFlight = 0;
            foreach (string name in new[]
            {
                "Walk",
                "Run"
            }

            )
            {
                for (int frame = 0; frame < 120; frame++)
                {
                    Clip(name).SampleAnimation(model, Clip(name).length * frame / 120f);
                    float knee = Vector3.Angle(
                        bones["LeftLowerLeg"].position - bones["LeftUpperLeg"].position,
                        bones["LeftFoot"].position - bones["LeftLowerLeg"].position);
                    var up = bones["UpperChest"].up;
                    float lean = Mathf.Atan2(up.z, up.y) * Mathf.Rad2Deg;
                    if (name == "Walk")
                    {
                        walkKnee = Mathf.Max(walkKnee, knee);
                        walkLean += lean / 120;
                    }
                    else
                    {
                        runKnee = Mathf.Max(runKnee, knee);
                        runLean += lean / 120;
                        float left = (bones["LeftFoot"].position + bones["LeftFoot"].rotation * new Vector3(
                            0,
                            -.062f,
                            0)).y;
                        float right = (bones["RightFoot"].position + bones["RightFoot"].rotation * new Vector3(
                            0,
                            -.062f,
                            0)).y;
                        runFlight = Mathf.Max(runFlight, Mathf.Min(left, right));
                    }
                }
            }

            if (runKnee < walkKnee + 15 || runLean < walkLean + 8 || runFlight < .005f)
            {
                throw new InvalidOperationException("Run does not retain the reference's distinct knee flexion, lean and flight");
            }

            report.Add("Reference gait differences: peak knee flex Walk=" + walkKnee.ToString("F1") + ", Run=" + runKnee.ToString("F1") + " degrees; mean trunk lean Walk=" + walkLean.ToString("F1") + ", Run=" + runLean.ToString("F1") + "; Run flight=" + (runFlight * 287).ToString("F1") + " cm.");
            foreach (string clipName in new[]
            {
                "Idle",
                "Walk",
                "Run",
                "Attack"
            }

            )
            {
                var clip = Clip(clipName);
                float minFlex = 180, maxFlex = 0, maxWrist = 0, maxHingeError = 0, maxUpperStep = 0;
                var previous = new Quaternion[2];
                for (int frame = 0; frame <= 120; frame++)
                {
                    clip.SampleAnimation(model, clip.length * frame / 120);
                    for (int s = 0; s < 2; s++)
                    {
                        string side = s == 0 ? "Left" : "Right";
                        var upper = bones[side + "UpperArm"];
                        var lower = bones[side + "LowerArm"];
                        var hand = bones[side + "Hand"];
                        float flex = Vector3.Angle(lower.position - upper.position, hand.position - lower.position);
                        minFlex = Mathf.Min(minFlex, flex);
                        maxFlex = Mathf.Max(maxFlex, flex);
                        maxWrist = Mathf.Max(maxWrist, Quaternion.Angle(Quaternion.identity, hand.localRotation));
                        float angle;
                        Vector3 axis;
                        lower.localRotation.ToAngleAxis(out angle, out axis);
                        var normal = Vector3.Cross(lower.localPosition, hand.localPosition).normalized;
                        if (Mathf.Min(angle, 360 - angle) > 1)
                        {
                            maxHingeError = Mathf.Max(maxHingeError, 1 - Mathf.Abs(Vector3.Dot(normal, axis)));
                        }

                        if (frame > 0)
                        {
                            maxUpperStep = Mathf.Max(maxUpperStep, Quaternion.Angle(previous[s], upper.localRotation));
                        }

                        previous[s] = upper.localRotation;
                    }
                }

                bool locomotion = clipName == "Walk" || clipName == "Run";
                if (minFlex < (locomotion ? 2 : 13) || maxFlex > (locomotion ? 133 : 114) || maxWrist > 26
                    || maxHingeError > .002f
                    || maxUpperStep > 35)
                {
                    throw new InvalidOperationException(clipName + " arm constraints: flex=" + minFlex + ".." + maxFlex + " wrist=" + maxWrist + " hinge=" + maxHingeError + " upper step=" + maxUpperStep);
                }

                report.Add(clipName + " arms: elbow flex=" + minFlex.ToString("F1") + ".." + maxFlex.ToString("F1") + " degrees; wrist max=" + maxWrist.ToString("F1") + "; hinge deviation=" + maxHingeError.ToString("F5") + "; maximum upper-arm frame step=" + maxUpperStep.ToString("F1"));
            }

            float minY = 10, maxY = -10;
            for (int frame = 0; frame <= 120; frame++)
            {
                Clip("Walk").SampleAnimation(model, Clip("Walk").length * frame / 120f);
                minY = Mathf.Min(minY, bones["Hips"].position.y);
                maxY = Mathf.Max(maxY, bones["Hips"].position.y);
            }

            if (maxY - minY < .003f)
            {
                throw new InvalidOperationException("Walk lacks reference body bounce");
            }

            report.Add("Walk pelvis vertical excursion=" + (maxY - minY).ToString("F4") + " model units (about " + ((maxY - minY) * 2.87f * 100).ToString("F1") + " cm on the player).");
            for (int s = 0; s < 2; s++)
            {
                string side = s == 0 ? "Left" : "Right";
                float minSwing = 180, maxSwing = -180, minShoulder = 180, maxShoulder = -180, minHand = 10, maxHand = -10;
                for (int frame = 0; frame <= 120; frame++)
                {
                    Clip("Walk").SampleAnimation(model, Clip("Walk").length * frame / 120f);
                    var chest = bones["UpperChest"];
                    var upper = bones[side + "UpperArm"];
                    var lower = bones[side + "LowerArm"];
                    var direction = chest.InverseTransformDirection(lower.position - upper.position);
                    float angle = Mathf.Atan2(direction.z, -direction.y) * Mathf.Rad2Deg;
                    minSwing = Mathf.Min(minSwing, angle);
                    maxSwing = Mathf.Max(maxSwing, angle);
                    angle = Mathf.DeltaAngle(0, bones[side + "Shoulder"].localEulerAngles.x);
                    minShoulder = Mathf.Min(minShoulder, angle);
                    maxShoulder = Mathf.Max(maxShoulder, angle);
                    float hand = chest.InverseTransformPoint(bones[side + "Hand"].position).z;
                    minHand = Mathf.Min(minHand, hand);
                    maxHand = Mathf.Max(maxHand, hand);
                }

                if (maxSwing - minSwing < 10 || maxHand - minHand < .04f)
                {
                    throw new InvalidOperationException(side + " arm lacks a visible shoulder-led swing");
                }

                Clip("Walk").SampleAnimation(model, 0);
                float first = bones["UpperChest"].InverseTransformPoint(bones[side + "Hand"].position).z;
                Clip("Walk").SampleAnimation(model, Clip("Walk").length * .5f);
                float second = bones["UpperChest"].InverseTransformPoint(bones[side + "Hand"].position).z;
                if ((s == 0 && first >= second) || (s == 1 && first <= second))
                {
                    throw new InvalidOperationException(side + " arm is not opposite the stepping leg");
                }

                report.Add(side + " Walk: upper-arm swing=" + (maxSwing - minSwing).ToString("F1") + " degrees; clavicle pitch=" + (maxShoulder - minShoulder).ToString("F1") + "; hand travel=" + ((maxHand - minHand) * 2.87f * 100).ToString("F1") + " cm; opposite-leg phase verified.");
            }

            VerifyClothDeformation(model, bones, report);
            using (var torso = new TorsoSurfaceProbe(model.transform))
            {
                for (int frame = 0; frame <= 32; frame++)
                {
                    Clip("Walk").SampleAnimation(model, Clip("Walk").length * frame / 32f);
                    torso.Sample();
                }

                torso.Validate();
                report.Add(torso.Report("Authored torso surface"));
            }

            var combat = UnityEngine.Object.FindFirstObjectByType<PlayerCombat>();
            if (combat.AnimateSwordPivot)
            {
                throw new InvalidOperationException("Two sword animation owners");
            }

            report.Add("Combat owns attack clock; sword is attached to the animated hand; root motion disabled.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(model);
        }

        Directory.CreateDirectory(Output);
        File.WriteAllLines(Output + "/asset-checks.txt", report);
        return string.Join("\n", report);
    }

    // Measures the visible torso surface, not just bone transforms. The root frame
    // removes gameplay translation, while left/right mesh samples expose twist/roll.
    sealed class TorsoSurfaceProbe : IDisposable
    {
        private readonly Transform root;
        private readonly SkinnedMeshRenderer skin;
        private readonly Mesh baked = new Mesh();
        private readonly int[] left, right;
        private Vector3 min = Vector3.one * 100, max = Vector3.one * -100;
        private float minYaw = 360, maxYaw = -360, minRoll = 360, maxRoll = -360;
        private int samples;
        public TorsoSurfaceProbe(Transform visual)
        {
            root = visual;
            skin = root.GetComponentInChildren<SkinnedMeshRenderer>();
            var vertices = skin.sharedMesh.vertices;
            var weights = skin.sharedMesh.boneWeights;
            var core = new HashSet<int>(Enumerable.Range(0, skin.bones.Length).Where(i => new[] { "Spine", "Chest", "UpperChest" }.Contains(skin.bones[i].name)));
            var selected = Enumerable.Range(0, vertices.Length).Where(i =>
            {
                var p = vertices[i];
                if (p.y < .46f || p.y > .575f || Mathf.Abs(p.x) < .015f || Mathf.Abs(p.x) > .085f)
                {
                    return false;
                }

                var w = weights[i];
                return (core.Contains(w.boneIndex0) ? w.weight0 : 0) + (core.Contains(w.boneIndex1) ? w.weight1 : 0) + (core.Contains(w.boneIndex2) ? w.weight2 : 0) + (core.Contains(w.boneIndex3) ? w.weight3 : 0) > .8f;
            }).ToArray();
            left = selected.Where(i => vertices[i].x > 0).ToArray();
            right = selected.Where(i => vertices[i].x < 0).ToArray();
            if (left.Length < 10 || right.Length < 10)
            {
                throw new InvalidOperationException("Torso mesh samples are missing");
            }
        }

        private Vector3 Average(Vector3[] vertices, int[] selected)
        {
            Vector3 center = Vector3.zero;
            foreach (int i in selected)
            {
                center += root.InverseTransformPoint(skin.transform.TransformPoint(vertices[i]));
            }

            return center / selected.Length;
        }

        public void Sample()
        {
            // In this rig/version, useScale=true yields unscaled renderer-space vertices
            // (verified against the source bounds), suitable for TransformPoint below.
            skin.BakeMesh(baked, true);
            var vertices = baked.vertices;
            var a = Average(vertices, left);
            var b = Average(vertices, right);
            var center = (a + b) * .5f;
            min = Vector3.Min(min, center);
            max = Vector3.Max(max, center);
            var across = a - b;
            float yaw = Mathf.Atan2(-across.z, across.x) * Mathf.Rad2Deg;
            float roll = Mathf.Atan2(across.y, new Vector2(across.x, across.z).magnitude) * Mathf.Rad2Deg;
            minYaw = Mathf.Min(minYaw, yaw);
            maxYaw = Mathf.Max(maxYaw, yaw);
            minRoll = Mathf.Min(minRoll, roll);
            maxRoll = Mathf.Max(maxRoll, roll);
            samples++;
        }

        public void Validate()
        {
            if (samples < 10 || max.x - min.x < .004f || maxYaw - minYaw < 3 || maxRoll - minRoll < 1)
            {
                throw new InvalidOperationException(Report("Insufficient visible torso motion"));
            }
        }

        public string Report(string label) => label + ": samples=" + samples + ", torso mesh lateral travel=" + ((max.x - min.x) * root.lossyScale.x * 100).ToString("F1") + " cm, twist=" + (maxYaw - minYaw).ToString("F1") + " deg, side tilt=" + (maxRoll - minRoll).ToString("F1") + " deg";
        public void Dispose()
        {
            UnityEngine.Object.DestroyImmediate(baked);
        }
    }

    private static void VerifyClothDeformation(
        GameObject model,
        Dictionary<string, Transform> bones,
        List<string> report)
    {
        var skin = model.GetComponentInChildren<SkinnedMeshRenderer>();
        var mesh = skin.sharedMesh;
        var weights = mesh.boneWeights;
        var vertices = mesh.vertices;
        var baked = new Mesh();
        try
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
                var indices = new HashSet<int>(Enumerable.Range(0, skin.bones.Length).Where(i => skin.bones[i].name.StartsWith(chain)));
                var selected = Enumerable.Range(0, weights.Length).Where(i =>
                {
                    var w = weights[i];
                    float amount = (indices.Contains(w.boneIndex0) ? w.weight0 : 0) + (indices.Contains(w.boneIndex1) ? w.weight1 : 0) + (indices.Contains(w.boneIndex2) ? w.weight2 : 0) + (indices.Contains(w.boneIndex3) ? w.weight3 : 0);
                    return amount > .8f && vertices[i].y < .23f;
                }).ToArray();
                if (selected.Length == 0)
                {
                    throw new InvalidOperationException(chain + " has no weighted hem vertices");
                }

                var min = Enumerable.Repeat(Vector3.one * 100, selected.Length).ToArray();
                var max = Enumerable.Repeat(Vector3.one * -100, selected.Length).ToArray();
                float maxLagAngle = 0;
                Vector3 anchor = bones[chain].localPosition;
                for (int frame = 0; frame <= 32; frame++)
                {
                    Clip("Walk").SampleAnimation(model, Clip("Walk").length * frame / 32f);
                    skin.BakeMesh(baked);
                    var points = baked.vertices;
                    for (int i = 0; i < selected.Length; i++)
                    {
                        var point = bones["Hips"].InverseTransformPoint(skin.transform.TransformPoint(points[selected[i]]));
                        min[i] = Vector3.Min(min[i], point);
                        max[i] = Vector3.Max(max[i], point);
                    }

                    if (Vector3.Distance(anchor, bones[chain].localPosition) > .00001f)
                    {
                        throw new InvalidOperationException(chain + " waist anchor moved");
                    }

                    maxLagAngle = Mathf.Max(
                        maxLagAngle,
                        Quaternion.Angle(bones[chain].localRotation, bones[chain + "Tip"].localRotation));
                }

                float excursion = Enumerable.Range(0, selected.Length).Max(i => (max[i] - min[i]).magnitude);
                if (excursion < .025f || maxLagAngle < 6)
                {
                    throw new InvalidOperationException(chain + " cloth deformation is too small");
                }

                report.Add(chain + ": " + selected.Length + " hem vertices, motion relative to pelvis=" + (excursion * 2.87f * 100).ToString("F1") + " cm; segment variation=" + maxLagAngle.ToString("F1") + " degrees; waist anchored.");
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(baked);
        }
    }

    public static string RenderPoses(bool sideView = false, bool renderWalkFrames = false)
    {
        Directory.CreateDirectory(Output);
        var preview = new PreviewRenderUtility();
        var model = Visual();
        preview.AddSingleGO(model);
        model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        model.transform.localScale = Vector3.one * 2.87f;
        model.GetComponent<Animator>().enabled = false;
        foreach (var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            renderer.updateWhenOffscreen = true;
        }

        // Static previews do not advance Animator's skinning pass. Bake each sampled pose
        // explicitly so the contact sheet shows the same deformation as gameplay.
        var skin = model.GetComponentInChildren<SkinnedMeshRenderer>();
        var baked = new Mesh();
        var visible = new GameObject("Sampled body");
        visible.transform.SetParent(skin.transform, false);
        visible.transform.localScale = new Vector3(
            1 / skin.transform.lossyScale.x,
            1 / skin.transform.lossyScale.y,
            1 / skin.transform.lossyScale.z);
        visible.AddComponent<MeshFilter>().sharedMesh = baked;
        visible.AddComponent<MeshRenderer>().sharedMaterials = skin.sharedMaterials;
        skin.enabled = false;
        preview.camera.orthographic = true;
        preview.camera.orthographicSize = 1.32f;
        preview.camera.transform.position = sideView ? new Vector3(-4.8f, 1.65f, 1.3f) : new Vector3(3.2f, 1.9f, 4.3f);
        preview.camera.transform.LookAt(new Vector3(0, .95f, 0));
        preview.camera.nearClipPlane = .01f;
        preview.camera.farClipPlane = 20;
        preview.camera.clearFlags = CameraClearFlags.SolidColor;
        preview.camera.backgroundColor = new Color(.16f, .19f, .23f);
        preview.ambientColor = new Color(.55f, .55f, .55f);
        preview.lights[0].intensity = 1.5f;
        preview.lights[0].transform.rotation = Quaternion.Euler(35, 135, 0);
        preview.lights[1].intensity = 1;
        preview.lights[1].transform.rotation = Quaternion.Euler(20, -35, 0);
        const int width = 300, height = 340;
        var sheet = new Texture2D(width * 6, height * 3, TextureFormat.RGB24, false);
        try
        {
            string[] names =
            {
                "Idle",
                "Walk",
                "Attack"
            };
            float[][] times =
            {
                new[]
                {
                    0f,
                    .17f,
                    .34f,
                    .5f,
                    .67f,
                    .84f
                },
                new[]
                {
                    0f,
                    .17f,
                    .34f,
                    .5f,
                    .67f,
                    .84f
                },
                new[]
                {
                    0f,
                    .28f,
                    .40f,
                    .50f,
                    .66f,
                    1f
                }
            };
            for (int row = 0; row < 3; row++)
            {
                for (int col = 0; col < 6; col++)
                {
                    var clip = Clip(names[row]);
                    clip.SampleAnimation(model, times[row][col] * clip.length);
                    skin.BakeMesh(baked);
                    preview.BeginStaticPreview(new Rect(0, 0, width, height));
                    preview.Render(true);
                    var frame = preview.EndStaticPreview();
                    sheet.SetPixels(col * width, (2 - row) * height, width, height, frame.GetPixels());
                    UnityEngine.Object.DestroyImmediate(frame);
                }
            }

            sheet.Apply();
            File.WriteAllBytes(Output + (sideView ? "/poses-side.png" : "/poses.png"), sheet.EncodeToPNG());
            if (renderWalkFrames)
            {
                for (int frameIndex = 0; frameIndex < 40; frameIndex++)
                {
                    Clip("Walk").SampleAnimation(model, frameIndex / 40f);
                    skin.BakeMesh(baked);
                    preview.BeginStaticPreview(new Rect(0, 0, width, height));
                    preview.Render(true);
                    var frame = preview.EndStaticPreview();
                    File.WriteAllBytes(
                        Output + "/walk-" + (sideView ? "side-" : "front-") + frameIndex.ToString("D2") + ".png",
                        frame.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(frame);
                }
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(sheet);
            UnityEngine.Object.DestroyImmediate(baked);
            preview.Cleanup();
        }

        return Output + (sideView ? "/poses-side.png" : "/poses.png") + " (rows: Idle, Walk, Attack; six sequential samples each)";
    }

    public static string BeginPlaySmoke()
    {
        if (!Application.isPlaying)
        {
            throw new InvalidOperationException("Enter Play Mode first.");
        }

        if (routine != null)
        {
            throw new InvalidOperationException("Smoke check already running");
        }

        Results.Clear();
        originalTimeScale = Time.timeScale;
        lastFrame = -1;
        routine = Smoke();
        EditorApplication.update += Tick;
        return "Gameplay checks started; result will be written to Temp/AnimationRework/play-checks.txt";
    }

    private static void Tick()
    {
        if (!Application.isPlaying)
        {
            Finish("FAIL Play Mode stopped before completion");
            return;
        }

        if (lastFrame == Time.frameCount)
        {
            return;
        }

        lastFrame = Time.frameCount;
        try
        {
            if (!routine.MoveNext())
            {
                Finish("COMPLETE");
            }
        }
        catch (Exception e)
        {
            Finish("FAIL " + e);
        }
    }

    private static void Finish(string message)
    {
        EditorApplication.update -= Tick;
        routine = null;
        Time.timeScale = originalTimeScale;
        if (Keyboard.current != null)
        {
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
        }

        if (Mouse.current != null)
        {
            InputSystem.QueueStateEvent(Mouse.current, new MouseState());
        }

        if (dummy)
        {
            UnityEngine.Object.Destroy(dummy.gameObject);
        }

        if (controller)
        {
            controller.enabled = true;
        }

        Results.Add(message);
        Directory.CreateDirectory(Output);
        File.WriteAllLines(Output + "/play-checks.txt", Results);
        Debug.Log("Player animation verification: " + message);
    }

    private static IEnumerator Smoke()
    {
        var animation = UnityEngine.Object.FindFirstObjectByType<PlayerCharacterAnimation>();
        var player = animation.gameObject;
        var movement = player.GetComponent<PlayerMovement>();
        var combat = player.GetComponent<PlayerCombat>();
        var body = player.GetComponent<Rigidbody>();
        var animator = animation.CharacterAnimator;
        controller = player.GetComponent<PlayerController>();
        // Move to an empty part of the existing test floor; these are Play Mode changes only.
        body.position = new Vector3(30, 1.05f, 15);
        body.linearVelocity = Vector3.zero;
        body.rotation = Quaternion.identity;
        controller.enabled = false;
        movement.ReadInput(Vector2.zero, false, controller.ViewCamera);
        float until = Time.time + .4f;
        while (Time.time < until)
        {
            yield return null;
        }

        Check(movement.IsGrounded, "Player grounded on test floor");
        Check(animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"), "Idle selected while stationary");
        Check(!animator.applyRootMotion, "Animator root motion disabled");
        movement.ReadInput(Vector2.up, false, controller.ViewCamera);
        using (var torso = new TorsoSurfaceProbe(animator.transform))
        {
            float nextSample = Time.time;
            until = Time.time + 1.35f;
            while (Time.time < until)
            {
                if (Time.time >= nextSample && animator.GetCurrentAnimatorStateInfo(0).IsName("Walk"))
                {
                    torso.Sample();
                    nextSample = Time.time + .04f;
                }

                yield return null;
            }

            torso.Validate();
            Results.Add("PASS " + torso.Report("Live Animator torso surface"));
        }

        Check(animator.GetCurrentAnimatorStateInfo(0).IsName("Walk"), "Movement switches Idle to Walk");
        float actual = new Vector2(body.linearVelocity.x, body.linearVelocity.z).magnitude;
        Check(
            Mathf.Abs(animator.GetFloat("WalkRate") - animation.TargetWalkRate) < .05f
            && animator.GetFloat("WalkRate") <= Mathf.Max(animation.MaximumWalkRate, animation.MaximumRunRate),
            "Locomotion cadence follows actual velocity within its caps");
        Check(
            Mathf.Abs(animator.GetFloat("RunBlend") - animation.TargetRunBlend) < .05f,
            "Walk/Run blend follows actual planar velocity");
        bool sprintAtStart = movement.IsSprinting;
        if (!sprintAtStart)
        {
            movement.ToggleSprint();
        }

        until = Time.time + .5f;
        while (Time.time < until)
        {
            yield return null;
        }

        Check(
            animator.GetFloat("RunBlend") > .95f
            && animator.GetCurrentAnimatorClipInfo(0).Any(c => c.clip.name == "Run"
            && c.weight > .9f),
            "High velocity selects the new Run clip");
        if (!sprintAtStart)
        {
            movement.ToggleSprint();
        }

        until = Time.time + .35f;
        while (Time.time < until)
        {
            yield return null;
        }

        var savedVelocity = body.linearVelocity;
        var savedPosition = body.position;
        body.linearVelocity = Vector3.forward * 100;
        Check(
            animation.TargetWalkRate <= Mathf.Max(animation.MaximumWalkRate, animation.MaximumRunRate),
            "High ability/sprint velocity cannot accelerate the gait beyond its cap");
        body.linearVelocity = Vector3.forward * animation.WalkReferenceSpeed;
        Check(animation.TargetRunBlend < .001f, "Walk reference velocity selects Walk independently of input");
        body.linearVelocity = Vector3.forward * animation.RunReferenceSpeed;
        Check(animation.TargetRunBlend > .999f, "Run reference velocity selects Run independently of input");
        body.linearVelocity = savedVelocity;
        animation.SendMessage("Update");
        animation.SendMessage("LateUpdate");
        Check(
            body.position == savedPosition && body.linearVelocity == savedVelocity,
            "Animation update preserves Rigidbody position and velocity");
        movement.ReadInput(Vector2.zero, false, controller.ViewCamera);
        until = Time.time + 1.25f;
        while (Time.time < until && !animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"))
        {
            yield return null;
        }

        Check(animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"), "Release movement returns to Idle");
        var target = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        target.name = "Temporary animation hit target";
        target.transform.position = body.position + player.transform.forward * 1.7f;
        dummy = target.AddComponent<Enemy>();
        float health = dummy.CurrentHealth;
        int effects = 0;
        Action effect = () => effects++;
        combat.SwingEffectTriggered += effect;
        Check(combat.TryAttack(), "Existing combat accepts attack");
        until = Time.time + combat.SwingDuration * .24f;
        while (Time.time < until)
        {
            yield return null;
        }

        Check(dummy.CurrentHealth == health && effects == 0, "Windup does not damage or emit slash effects");
        Check(animator.GetCurrentAnimatorStateInfo(0).IsName("Attack"), "Attack entry crossfade completes");
        Check(
            Mathf.Abs(animator.GetFloat("AttackProgress") - combat.SwingProgress) < .08f,
            "Attack pose follows Combat clock");
        while (combat.IsAttacking)
        {
            yield return null;
        }

        Check(dummy.CurrentHealth == health - combat.AttackDamage, "Strike damages target exactly once");
        Check(effects == 1, "Slash effect fires once at strike start");
        combat.SwingEffectTriggered -= effect;
        UnityEngine.Object.Destroy(dummy.gameObject);
        dummy = null;
        until = Time.time + .25f;
        while (Time.time < until)
        {
            yield return null;
        }

        Check(animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"), "Completed attack blends back to Idle");
        movement.ReadInput(Vector2.up, false, controller.ViewCamera);
        until = Time.time + .3f;
        while (Time.time < until)
        {
            yield return null;
        }

        Check(combat.TryAttack(), "Attack can interrupt Walk");
        while (combat.IsAttacking)
        {
            yield return null;
        }

        until = Time.time + .35f;
        while (Time.time < until)
        {
            yield return null;
        }

        Check(animator.GetCurrentAnimatorStateInfo(0).IsName("Walk"), "Completed attack resumes held movement");
        movement.ReadInput(Vector2.zero, false, controller.ViewCamera);
        until = Time.time + .4f;
        while (Time.time < until)
        {
            yield return null;
        }

        Check(combat.TryAttack(), "Cancellation test attack starts");
        yield return null;
        combat.CancelAttack();
        until = Time.time + .25f;
        while (Time.time < until)
        {
            yield return null;
        }

        Check(
            !combat.IsAttacking && animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"),
            "Cancelled attack returns smoothly to locomotion");
        Check(
            player.GetComponent<PlayerTargetLockOn>().enabled && player.GetComponent<PlayerMetalManipulator>().enabled
            && player.GetComponent<PlayerPewterAbility>().enabled
            && player.GetComponent<PlayerTinAbility>().enabled,
            "Lock-On and metal ability components stay enabled");
        // Exercise the actual InputAction binding through PlayerController.
        controller.enabled = true;
        Cursor.lockState = CursorLockMode.Locked;
        // Cancelling preserves Combat's cooldown; wait until the next attack is legal.
        until = Time.time + combat.SwingDuration + .1f;
        while (Time.time < until)
        {
            yield return null;
        }

        InputSystem.QueueStateEvent(Mouse.current, new MouseState());
        InputSystem.Update();
        InputSystem.QueueStateEvent(Mouse.current, new MouseState().WithButton(MouseButton.Left));
        InputSystem.Update();
        Cursor.lockState = CursorLockMode.Locked;
        controller.SendMessage("Update");
        Check(combat.IsAttacking, "Left Mouse InputAction triggers attack through existing PlayerController");
        InputSystem.QueueStateEvent(Mouse.current, new MouseState());
        while (combat.IsAttacking)
        {
            yield return null;
        }

        // Test the real camera update with genuine movement and deceleration.
        controller.enabled = false;
        body.position = new Vector3(30, 1.05f, 15);
        body.linearVelocity = Vector3.zero;
        movement.ReadInput(Vector2.zero, false, controller.ViewCamera);
        float baseFov = controller.ViewCamera.fieldOfView;
        var previousVelocity = body.linearVelocity;
        body.linearVelocity = Vector3.up * 20;
        Check(
            Mathf.Abs(controller.TargetFieldOfView - (baseFov + controller.MaximumSpeedFovBoost)) < .1f,
            "Speed FOV also responds to external/airborne momentum");
        body.linearVelocity = previousVelocity;
        bool sprintBefore = movement.IsSprinting;
        if (!sprintBefore)
        {
            movement.ToggleSprint();
        }

        movement.ReadInput(Vector2.up, false, controller.ViewCamera);
        bool intermediate = false;
        until = Time.time + .9f;
        while (Time.time < until)
        {
            controller.SendMessage("UpdateCamera");
            float fov = controller.ViewCamera.fieldOfView;
            if (fov > baseFov + .1f && fov < baseFov + controller.MaximumSpeedFovBoost - 1)
            {
                intermediate = true;
            }

            yield return null;
        }

        float fastFov = controller.ViewCamera.fieldOfView;
        Check(
            intermediate && fastFov > baseFov + 5 && fastFov <= baseFov + controller.MaximumSpeedFovBoost + .1f,
            "High-speed movement widens FOV smoothly within its cap");
        controller.SendMessage("UpdateCamera");
        float once = controller.ViewCamera.fieldOfView;
        controller.SendMessage("UpdateCamera");
        Check(
            Mathf.Abs(controller.ViewCamera.fieldOfView - once) < .00001f,
            "Update and LateUpdate cannot advance FOV twice in one frame");
        movement.ReadInput(Vector2.zero, false, controller.ViewCamera);
        until = Time.time + 1.8f;
        while (Time.time < until)
        {
            controller.SendMessage("UpdateCamera");
            yield return null;
        }

        Check(
            Mathf.Abs(controller.ViewCamera.fieldOfView - baseFov) < .35f,
            "Deceleration restores the original FOV smoothly");
        Results.Add("FOV measured: baseline=" + baseFov.ToString("F1") + ", fast=" + fastFov.ToString("F1") + ", stopped=" + controller.ViewCamera.fieldOfView.ToString("F1"));
        controller.enabled = true;
        controller.ViewCamera.fieldOfView = baseFov + 5;
        controller.enabled = false;
        Check(
            Mathf.Abs(controller.ViewCamera.fieldOfView - baseFov) < .001f,
            "Disabling the controller restores the camera baseline");
        if (!sprintBefore)
        {
            movement.ToggleSprint();
        }
    }
}
