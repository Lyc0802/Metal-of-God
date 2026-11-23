#if UNITY_EDITOR
using System.IO;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

/// <summary>
/// 城堡入口（第三區）：通往 Boss 房的全黑單向走廊。
/// 選單 Castle/Build Entrance Corridor 會重建整個場景（可重複執行）：材質、敵人 Prefab、走廊、燈光、NavMesh、敵人配置。
/// </summary>
public static partial class CastleEntranceBuilder
{
    private const string Root = "Assets/Castle";
    private const string ScenePath = "Assets/Scenes/CastleEntrance.unity";
    private const float H = 9f;           // 天花板高度
    private const float DoorZ = 94f;      // 大門位置
    private const float EndZ = 104f;      // 走廊盡頭（Boss 房入口前）

    private static Material stone, floorMat, trim, steel, darkSteel, wood, flame, doorMat, glowGold, gold, barrelMat;
    private static Transform structure, props, lights, interactives;

    [MenuItem("Castle/Build Entrance Corridor")]
    public static void Build()
    {
        EnsureFolder(Root);
        EnsureFolder(Root + "/Materials");
        EnsureFolder(Root + "/Prefabs");
        CreateMaterials();
        tintedCache.Clear();
        var controller = BuildEnemyAnimator();
        var ambusher = BuildAmbusherPrefab(controller);
        var guard = BuildGuardPrefab();
        var thrower = BuildThrowerPrefab(controller);
        BuildScene(ambusher, guard, thrower);
        AssetDatabase.SaveAssets();
        Debug.Log("Castle Entrance built: " + ScenePath);
    }

    // ------------------------------------------------------------------ assets

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
        {
            return;
        }

        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    private static Material Mat(string name, Color color, float smoothness, float metallic, Color? emission = null)
    {
        string path = Root + "/Materials/" + name + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!mat)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, path);
        }

        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Smoothness", smoothness);
        mat.SetFloat("_Metallic", metallic);
        if (emission.HasValue)
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", emission.Value);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }

        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static void CreateMaterials()
    {
        stone = Mat("CastleStone", new Color(.3f, .3f, .33f), .1f, 0);
        floorMat = Mat("CastleFloor", new Color(.24f, .24f, .27f), .3f, 0);
        trim = Mat("CastleTrim", new Color(.38f, .37f, .4f), .15f, 0);
        steel = Mat("CastleSteel", new Color(.5f, .54f, .6f), .5f, .45f);
        darkSteel = Mat("CastleDarkSteel", new Color(.28f, .3f, .35f), .45f, .35f);
        wood = Mat("CastleWood", new Color(.32f, .21f, .13f), .1f, 0);
        flame = Mat("CastleFlame", new Color(1f, .5f, .15f), .2f, 0, new Color(3.2f, 1.3f, .35f));
        doorMat = Mat("CastleDoor", new Color(.34f, .34f, .4f), .45f, .4f);
        gold = Mat("CastleGold", new Color(.85f, .66f, .2f), .5f, .4f);
        barrelMat = Mat("CastleBarrel", new Color(.34f, .32f, .38f), .4f, .4f);
        glowGold = Mat("CastleDoorGlow", new Color(.9f, .7f, .3f), .3f, .5f, new Color(1.6f, 1.05f, .4f));
    }

    // ------------------------------------------------------------------ enemy prefabs

    private static Transform Deep(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == name)
            {
                return t;
            }
        }

        return null;
    }

    private static void SetMetals(Enemy enemy, params MetalType[] metals)
    {
        enemy.UsingMetal = MetalType.None;
        enemy.MetalAbilities = new MetalReserve[metals.Length];
        for (int i = 0; i < metals.Length; i++)
        {
            enemy.MetalAbilities[i] = new MetalReserve { Type = metals[i], IsEnabled = true, CurrentAmount = 100, MaxAmount = 100 };
        }
    }

    private const string ModelPath = "Assets/EnemyModel/RFA/URP/Models/RFA_Model.fbx";
    private const string ClipDir = "Assets/EnemyModel/RFA/Common/Animations/";

    private static AnimationClip Clip(string name)
    {
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(ClipDir + "RFA_" + name + ".fbx"))
        {
            var clip = o as AnimationClip;
            if (clip && !clip.name.StartsWith("__preview"))
            {
                return clip;
            }
        }

        return null;
    }

    /// <summary>敵人共用的 Animator：待機 / 跑 / 攻擊 / 受傷 / 死亡，由 EnemyAnimationDriver 驅動。</summary>
    private static RuntimeAnimatorController BuildEnemyAnimator()
    {
        string path = Root + "/EnemyAnimator.controller";
        AssetDatabase.DeleteAsset(path);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("RunRate", AnimatorControllerParameterType.Float);
        controller.AddParameter("AttackSpeed", AnimatorControllerParameterType.Float);
        controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Hurt", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Dead", AnimatorControllerParameterType.Bool);
        var parameters = controller.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].name == "RunRate" || parameters[i].name == "AttackSpeed")
            {
                parameters[i].defaultFloat = 1;
            }
        }

        controller.parameters = parameters;

        var machine = controller.layers[0].stateMachine;
        var idle = machine.AddState("Idle");
        idle.motion = Clip("CombatIdle");
        var run = machine.AddState("Run");
        run.motion = Clip("RunForward");
        run.speedParameterActive = true;
        run.speedParameter = "RunRate";
        var attack = machine.AddState("Attack");
        attack.motion = Clip("Attack");
        attack.speedParameterActive = true;
        attack.speedParameter = "AttackSpeed";
        var hurt = machine.AddState("Hurt");
        hurt.motion = Clip("TakingDamage");
        var death = machine.AddState("Death");
        death.motion = Clip("Death");
        machine.defaultState = idle;

        var toRun = idle.AddTransition(run);
        toRun.hasExitTime = false;
        toRun.duration = .15f;
        toRun.AddCondition(AnimatorConditionMode.Greater, .4f, "Speed");
        var toIdle = run.AddTransition(idle);
        toIdle.hasExitTime = false;
        toIdle.duration = .15f;
        toIdle.AddCondition(AnimatorConditionMode.Less, .25f, "Speed");

        AnyTo(machine, attack, "Attack", .08f);
        AnyTo(machine, hurt, "Hurt", .05f);
        foreach (var state in new[] { attack, hurt })
        {
            var back = state.AddTransition(idle);
            back.hasExitTime = true;
            back.exitTime = .92f;
            back.duration = .15f;
        }

        var die = machine.AddAnyStateTransition(death);
        die.hasExitTime = false;
        die.duration = .1f;
        die.canTransitionToSelf = false;
        die.AddCondition(AnimatorConditionMode.If, 0, "Dead");
        return controller;
    }

    private static void AnyTo(AnimatorStateMachine machine, AnimatorState target, string trigger, float duration)
    {
        var t = machine.AddAnyStateTransition(target);
        t.hasExitTime = false;
        t.duration = duration;
        t.canTransitionToSelf = false;
        t.AddCondition(AnimatorConditionMode.If, 0, trigger);
        t.AddCondition(AnimatorConditionMode.IfNot, 0, "Dead");
    }

    private static readonly System.Collections.Generic.Dictionary<string, Material> tintedCache
        = new System.Collections.Generic.Dictionary<string, Material>();

    /// <summary>
    /// 每種敵人 + 每個原始材質只建立一份染色材質。Cloth 與 Scarf、Dagger 與 Sword 共用同一個原始材質，
    /// 如果每次都刪除重建，先建好的那份會被刪掉，渲染器就變成「遺失材質」的洋紅色。
    /// </summary>
    private static Material Tinted(Material source, string suffix, Color tint)
    {
        string path = Root + "/Materials/Enemy_" + suffix + "_" + source.name + ".mat";
        if (tintedCache.TryGetValue(path, out var cached) && cached)
        {
            return cached;
        }

        AssetDatabase.DeleteAsset(path);
        var mat = new Material(source);
        AssetDatabase.CreateAsset(mat, path);
        if (source.HasProperty("_BaseColor"))
        {
            mat.SetColor("_BaseColor", source.GetColor("_BaseColor") * tint);
        }

        tintedCache[path] = mat;
        return mat;
    }

    /// <summary>以 TestEnemy 為底（保留 Enemy / AI / NavMeshAgent / Bronze 等元件），移除舊的方塊身體。</summary>
    private static GameObject NewEnemyBase()
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Combat/TestEnemy.prefab");
        var go = (GameObject)PrefabUtility.InstantiatePrefab(source);
        PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        var old = Deep(go.transform, "Enemy Model");
        if (old)
        {
            Object.DestroyImmediate(old.gameObject);
        }

        go.GetComponent<TestEnemy>().AttackPivot = null;
        // 聽覺 / 警報 / 光線感知（所有敵人共用）。
        var senses = go.AddComponent<EnemySenses>();
        // 對金屬撞擊聲等的警覺性高：聽覺範圍大、更遠的埋伏者也會被吵醒、到了聲源會多留一會兒。
        senses.HearingScale = 1.7f;
        senses.AmbushHearRadius = 9f;
        senses.InvestigateWait = 6f;
        return go;
    }

    /// <summary>建立以腳底為原點的 "Enemy Model" 容器，並放入模型實例（Visual）。</summary>
    private static Transform AddModel(GameObject go, string modelPath, float scale, float yaw, out GameObject visual)
    {
        var container = new GameObject("Enemy Model").transform;
        container.SetParent(go.transform, false);
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        visual = (GameObject)PrefabUtility.InstantiatePrefab(asset, container);
        visual.name = "Visual";
        visual.transform.localPosition = Vector3.zero;
        // 保留網格本身的根旋轉（Z 軸朝上的模型靠它轉正），再疊加轉向。
        visual.transform.localRotation = Quaternion.Euler(0, yaw, 0) * asset.transform.localRotation;
        visual.transform.localScale = Vector3.one * scale;
        return container;
    }

    /// <summary>RFA 女刺客（埋伏者）：有完整動畫。</summary>
    private static GameObject NewEnemyRoot(string suffix, Color tint, float modelScale, RuntimeAnimatorController controller, out Transform model)
    {
        var go = NewEnemyBase();
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(fbx, go.transform);
        instance.name = "Enemy Model";
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one * modelScale;
        foreach (var r in instance.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i])
                {
                    mats[i] = Tinted(mats[i], suffix, tint);
                }
            }

            r.sharedMaterials = mats;
        }

        var animator = instance.GetComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        // 攻擊動畫帶有 SetAttackStart / SetAttackEnd 事件，需要同一個物件上有接收者。
        instance.AddComponent<EnemyAnimationEvents>();
        go.AddComponent<EnemyAnimationDriver>().Animator = animator;
        model = instance.transform;
        return go;
    }

    private static void SetBody(GameObject go, float height, float radius)
    {
        var capsule = go.GetComponent<CapsuleCollider>();
        capsule.height = height;
        capsule.radius = radius;
        capsule.center = new Vector3(0, height * .5f, 0);
        var agent = go.GetComponent<NavMeshAgent>();
        agent.height = height;
        agent.radius = radius;
    }

    // --- Iron Juggernaut（重甲衛兵）：Generic 骨架，只有待機 / 走 / 跑，攻擊的手臂揮動用程式補。

    private const string JuggernautMesh = "Assets/EnemyModel/IronJuggernaut/Meshes/LowPoly.fbx";
    private const string JuggernautClips = "Assets/EnemyModel/IronJuggernaut/Animations/";
    // 模型實例繞 Y 軸的轉向，讓臉朝向敵人的前方（+Z）。
    private const float JuggernautYaw = 0f;
    private const float JuggernautScale = 1.25f;
    // 原始動畫的位移（模型單位）：走路 6.56 公尺 / 2.73 秒、跑步 10.10 公尺 / 1.90 秒，再乘上模型縮放得到實際速度。
    private const float JuggernautWalkSpeed = 6.56f / 2.73f * JuggernautScale;
    private const float JuggernautRunSpeed = 10.10f / 1.90f * JuggernautScale;

    private static AnimationClip FirstClip(string path)
    {
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            var clip = o as AnimationClip;
            if (clip && !clip.name.StartsWith("__preview"))
            {
                return clip;
            }
        }

        return null;
    }

    /// <summary>
    /// 原始的走 / 跑動畫把「root」骨頭一路往前移（帶位移），沒開 Root Motion 時模型會滑離敵人本體再瞬間跳回。
    /// 複製成原地版本：刪掉 root 的位移曲線並設為循環。
    /// </summary>
    private static AnimationClip InPlaceClip(string source, string name)
    {
        string path = Root + "/Clips/Juggernaut_" + name + ".anim";
        AssetDatabase.DeleteAsset(path);
        var original = FirstClip(JuggernautClips + source);
        var copy = Object.Instantiate(original);
        copy.name = "Juggernaut_" + name;
        foreach (var binding in AnimationUtility.GetCurveBindings(original))
        {
            if (binding.path == "root" && binding.propertyName.StartsWith("m_LocalPosition"))
            {
                AnimationUtility.SetEditorCurve(copy, binding, null);
            }
        }

        var settings = AnimationUtility.GetAnimationClipSettings(copy);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(copy, settings);
        AssetDatabase.CreateAsset(copy, path);
        return copy;
    }

    private static RuntimeAnimatorController BuildJuggernautAnimator()
    {
        EnsureFolder(Root + "/Clips");
        string path = Root + "/JuggernautAnimator.controller";
        AssetDatabase.DeleteAsset(path);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("WalkRate", AnimatorControllerParameterType.Float);
        controller.AddParameter("RunRate", AnimatorControllerParameterType.Float);
        var parameters = controller.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].type == AnimatorControllerParameterType.Float && parameters[i].name != "Speed")
            {
                parameters[i].defaultFloat = 1;
            }
        }

        controller.parameters = parameters;
        var machine = controller.layers[0].stateMachine;
        var idle = machine.AddState("Idle");
        idle.motion = InPlaceClip("MM_Idle.FBX", "Idle");
        var walk = machine.AddState("Walk");
        walk.motion = InPlaceClip("MM_Walk_Fwd.FBX", "Walk");
        walk.speedParameterActive = true;
        walk.speedParameter = "WalkRate";
        var run = machine.AddState("Run");
        run.motion = InPlaceClip("MM_Run_Fwd.FBX", "Run");
        run.speedParameterActive = true;
        run.speedParameter = "RunRate";
        machine.defaultState = idle;
        Transition(idle, walk, AnimatorConditionMode.Greater, .3f);
        Transition(walk, idle, AnimatorConditionMode.Less, .2f);
        Transition(walk, run, AnimatorConditionMode.Greater, 4.5f);
        Transition(run, walk, AnimatorConditionMode.Less, 4f);
        return controller;
    }

    private static void Transition(AnimatorState from, AnimatorState to, AnimatorConditionMode mode, float threshold)
    {
        var t = from.AddTransition(to);
        t.hasExitTime = false;
        t.duration = .2f;
        t.AddCondition(mode, threshold, "Speed");
    }

    private static EnemyProceduralMotion AddProcedural(Transform container, bool bob, bool breathe, float windup, float strike)
    {
        var motion = container.gameObject.AddComponent<EnemyProceduralMotion>();
        motion.Pivot = container;
        motion.BobWhileMoving = bob;
        motion.Breathe = breathe;
        motion.WindupLean = windup;
        motion.StrikeLean = strike;
        return motion;
    }

    /// <summary>埋伏者：Pewter + Tin 使用者。蹲在暗處，靠近或被打到時爆發衝刺。</summary>
    private static GameObject BuildAmbusherPrefab(RuntimeAnimatorController controller)
    {
        var go = NewEnemyRoot("Ambusher", new Color(.42f, .45f, .6f), 1f, controller, out var model);
        SetBody(go, 1.9f, .4f);
        var enemy = go.GetComponent<Enemy>();
        enemy.MaxHealth = 70;
        enemy.CurrentHealth = 70;
        SetMetals(enemy, MetalType.Pewter, MetalType.Tin);
        var ai = go.GetComponent<TestEnemy>();
        ai.WanderRadius = 0;
        ai.DetectionRange = 14;
        ai.ChaseSpeed = 3.4f;
        ai.AttackDamage = 10;
        ai.AttackRange = 1.9f;
        ai.AttackCooldown = 1f;
        ai.AttackDuration = 1f;
        ai.AttackHitTime = .45f;
        ai.AttackWhileMoving = true;
        var ambush = go.AddComponent<EnemyAmbush>();
        ambush.Model = model;
        // 走廊寬 12：站在中間走道距離壁龕 7.5，所以觸發距離要大於它，貼著對面那排柱子走才能避開。
        ambush.TriggerRange = 8.5f;
        ambush.LurkHeight = 1f;
        ambush.ModelAnimator = model.GetComponent<Animator>();
        ambush.GlowRenderers = new Renderer[0];
        return SaveStandalone(go, "Ambusher");
    }

    /// <summary>重甲衛兵：Iron 使用者。體型大、血厚、慢，攻擊前搖長但很痛。</summary>
    private static GameObject BuildGuardPrefab()
    {
        var controller = BuildJuggernautAnimator();
        var go = NewEnemyBase();
        var container = AddModel(go, JuggernautMesh, JuggernautScale, JuggernautYaw, out var visual);
        var animator = visual.GetComponent<Animator>();
        if (!animator)
        {
            animator = visual.AddComponent<Animator>();
        }

        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        // 攻擊：雙手舉過頭頂再砸下（手臂骨頭的 Z 軸是前後揮動），身體只做輕微前傾。
        var motion = AddProcedural(container, false, false, -6f, 12f);
        var arms = new System.Collections.Generic.List<Transform>();
        foreach (string boneName in new[] { "upperarm_l", "upperarm_r" })
        {
            var bone = Deep(visual.transform, boneName);
            if (bone)
            {
                arms.Add(bone);
            }
        }

        motion.ArmBones = arms.ToArray();
        motion.ArmAxis = Vector3.forward;
        motion.ArmWindup = 150f;
        motion.ArmStrike = 55f;
        var driver = go.AddComponent<EnemyAnimationDriver>();
        driver.Animator = animator;
        driver.Procedural = motion;
        driver.WalkReferenceSpeed = JuggernautWalkSpeed;
        driver.RunReferenceSpeed = JuggernautRunSpeed;
        SetBody(go, 2.5f, .6f);
        var enemy = go.GetComponent<Enemy>();
        enemy.MaxHealth = 160;
        enemy.CurrentHealth = 160;
        SetMetals(enemy, MetalType.Iron);
        var ai = go.GetComponent<TestEnemy>();
        ai.WanderRadius = 2.5f;
        ai.WanderSpeed = 1.1f;
        ai.DetectionRange = 13;
        ai.ChaseSpeed = 2.8f;
        ai.AttackRange = 2.6f;
        ai.AttackDamage = 18;
        ai.AttackCooldown = 2.2f;
        ai.AttackDuration = 1.3f;
        ai.AttackHitTime = .5f;
        return SaveStandalone(go, "Guard");
    }

    /// <summary>投擲兵：Steel 使用者（與埋伏者同一套 RFA 模型，只有顏色不同）。保持距離，把金屬碎片射向玩家。</summary>
    private static GameObject BuildThrowerPrefab(RuntimeAnimatorController controller)
    {
        var go = NewEnemyRoot("Thrower", new Color(.55f, .72f, 1f), 1f, controller, out _);
        SetBody(go, 1.9f, .4f);
        var enemy = go.GetComponent<Enemy>();
        enemy.MaxHealth = 55;
        enemy.CurrentHealth = 55;
        SetMetals(enemy, MetalType.Steel);
        // 近戰 AI 停用，改由 EnemyShooter 控制。
        go.GetComponent<TestEnemy>().enabled = false;
        var shooter = go.AddComponent<EnemyShooter>();
        shooter.Projectile = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Boss/Prefabs/Boss Projectile Metal.prefab").GetComponent<BossProjectile>();
        return SaveStandalone(go, "Thrower");
    }

    private static GameObject SaveStandalone(GameObject instance, string name)
    {
        string path = Root + "/Prefabs/" + name + ".prefab";
        AssetDatabase.DeleteAsset(path);
        var prefab = PrefabUtility.SaveAsPrefabAsset(instance, path);
        Object.DestroyImmediate(instance);
        return prefab;
    }

    // ------------------------------------------------------------------ scene

    private static void BuildScene(GameObject ambusher, GameObject guard, GameObject thrower)
    {
        EditorSceneManager.SaveOpenScenes();
        if (!File.Exists(ScenePath))
        {
            AssetDatabase.CopyAsset("Assets/Scenes/BossArena.unity", ScenePath);
        }

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.name == "Boss Arena" || root.name == "Inquisitor Axe" || root.name == "Castle Corridor"
                || root.name == "Castle Enemies" || root.name == "Castle Interactives" || root.name == "Aim Slow Motion"
                || root.name == "Patrol Routes")
            {
                Object.DestroyImmediate(root);
            }
        }

        var corridor = new GameObject("Castle Corridor").transform;
        structure = new GameObject("Structure").transform;
        props = new GameObject("Props").transform;
        lights = new GameObject("Lights").transform;
        structure.SetParent(corridor, false);
        props.SetParent(corridor, false);
        lights.SetParent(corridor, false);
        // 可互動道具（吊燈、火盆、桶、金幣…）放在走廊之外：它們會移動，不該被烘進 NavMesh。
        interactives = new GameObject("Castle Interactives").transform;

        routes = null;
        BuildStructure();
        BuildVestibule();
        BuildCourtyard();
        BuildWestWing();
        BuildEastWing();
        BuildCorridor();
        BuildDoor();
        ConfigureAtmosphere(scene);
        BakeNavMesh(corridor.gameObject);
        PlaceEnemies(ambusher, guard, thrower);
        PlacePlayer(scene);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    // --- building blocks

    private static GameObject Box(string name, Transform parent, Vector3 center, Vector3 size, Material material, bool collider = true, float yaw = 0)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.position = center;
        go.transform.localScale = size;
        go.transform.rotation = Quaternion.Euler(0, yaw, 0);
        go.GetComponent<Renderer>().sharedMaterial = material;
        if (!collider)
        {
            Object.DestroyImmediate(go.GetComponent<Collider>());
        }

        return go;
    }

    private static GameObject Cylinder(string name, Transform parent, Vector3 center, float radius, float height, Material material, bool collider = true)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.position = center;
        go.transform.localScale = new Vector3(radius * 2, height * .5f, radius * 2);
        go.GetComponent<Renderer>().sharedMaterial = material;
        if (!collider)
        {
            Object.DestroyImmediate(go.GetComponent<Collider>());
        }

        return go;
    }

    /// <summary>加上金屬能力：靜態（很重，玩家會被拉過去）或動態（輕，會被推拉移動）。</summary>
    private static void MakeMetal(GameObject go, float weight, bool dynamic)
    {
        var body = go.GetComponent<Rigidbody>();
        if (!body)
        {
            body = go.AddComponent<Rigidbody>();
        }

        body.mass = dynamic ? weight : 1;
        body.isKinematic = !dynamic;
        body.useGravity = dynamic;
        if (dynamic)
        {
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }

        go.AddComponent<MetalDisplayer>().weight = weight;
        if (dynamic)
        {
            go.AddComponent<MetalImpactFeedback>();
        }
    }

    private static Light PointLight(string name, Vector3 position, Color color, float intensity, float range, bool flicker, Transform parent = null)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent ? parent : lights, false);
        go.transform.position = position;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = color;
        l.intensity = intensity;
        l.range = range;
        l.shadows = LightShadows.None;
        if (flicker)
        {
            go.AddComponent<TorchFlicker>();
        }

        // 所有光源都會影響潛行：玩家站在亮處比較容易被發現。
        go.AddComponent<StealthLight>();
        return l;
    }

    /// <summary>沒有網格的容器根物件（只為了滿足 MetalDisplayer 需要 Renderer；實際外觀由子物件提供）。</summary>
    private static GameObject Holder(string name, Vector3 position)
    {
        var go = new GameObject(name);
        go.transform.SetParent(interactives, false);
        go.transform.position = position;
        // MetalDisplayer 要求有 Renderer，所以根物件需要 MeshRenderer；但 Outline 會讀同物件的 MeshFilter，
        // 沒有 MeshFilter 就丟 MissingComponentException。給一個空網格的 MeshFilter（Outline 會略過）。
        go.AddComponent<MeshFilter>();
        go.AddComponent<MeshRenderer>().sharedMaterial = darkSteel;
        return go;
    }

    private static GameObject Part(PrimitiveType type, string name, Transform parent, Vector3 localPosition, Vector3 localScale, Material material, bool collider)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localScale = localScale;
        go.GetComponent<Renderer>().sharedMaterial = material;
        if (!collider)
        {
            Object.DestroyImmediate(go.GetComponent<Collider>());
        }
        else if (type == PrimitiveType.Cylinder)
        {
            FixCylinderCollider(go);
        }

        return go;
    }

    /// <summary>
    /// 圓柱基本體預設是膠囊碰撞器：壓扁的圓盤（火盆底座、吊燈環、金幣）會變成一顆圓球，站不穩、會亂滾。
    /// 換成凸多邊形網格碰撞器，形狀才會跟外觀一致。
    /// </summary>
    private static void FixCylinderCollider(GameObject go)
    {
        var capsule = go.GetComponent<CapsuleCollider>();
        if (capsule)
        {
            Object.DestroyImmediate(capsule);
        }

        var mesh = go.AddComponent<MeshCollider>();
        mesh.sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
        mesh.convex = true;
    }

    private static Rigidbody DynamicBody(GameObject go, float mass)
    {
        var body = go.GetComponent<Rigidbody>();
        if (!body)
        {
            body = go.AddComponent<Rigidbody>();
        }

        body.mass = mass;
        body.useGravity = true;
        body.isKinematic = false;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        return body;
    }

    private static LooseMetalProp MakeProp(GameObject go, float weight, float damage, float noiseRadius, float volume, float pitch, float playerDamage = 0)
    {
        go.AddComponent<MetalDisplayer>().weight = weight;
        go.AddComponent<MetalImpactFeedback>();
        var prop = go.AddComponent<LooseMetalProp>();
        prop.Damage = damage;
        prop.NoiseRadius = noiseRadius;
        prop.Volume = volume;
        prop.Pitch = pitch;
        prop.PlayerDamage = playerDamage;
        prop.MinNoiseSpeed = 1.5f;
        return prop;
    }

    /// <summary>
    /// 讓敵人尋路時繞過這個可移動的物體：NavMeshObstacle 只在物體靜止時雕刻 NavMesh（被推動中不會一直重算）。
    /// radius / height 是物件本地座標（會乘上物件的縮放）。
    /// </summary>
    private static void AddNavObstacle(GameObject go, float radius, float height)
    {
        var obstacle = go.AddComponent<NavMeshObstacle>();
        obstacle.shape = NavMeshObstacleShape.Capsule;
        obstacle.radius = radius;
        obstacle.height = height;
        obstacle.carving = true;
        obstacle.carveOnlyStationary = true;
        obstacle.carvingMoveThreshold = .25f;
        obstacle.carvingTimeToStationary = .6f;
    }

    /// <summary>金幣袋：很輕，Steel 推出去撞到牆會發出清脆聲響，把敵人引開。</summary>
    private static void CoinPurse(Vector3 position)
    {
        var go = Part(PrimitiveType.Cylinder, "Coin Purse", interactives, Vector3.zero, new Vector3(.44f, .08f, .44f), gold, true);
        go.transform.position = position;
        DynamicBody(go, 3);
        MakeProp(go, 3, 0, 20, .8f, 1.7f);
    }

    /// <summary>鐵箍桶：中等重量，推出去撞到敵人會造成傷害與擊退，聲響很大；撞到玩家也會受傷。</summary>
    private static void Barrel(Vector3 position)
    {
        var go = Part(PrimitiveType.Cylinder, "Iron Barrel", interactives, Vector3.zero, new Vector3(.9f, .5f, .9f), barrelMat, true);
        go.transform.position = position;
        DynamicBody(go, 45);
        var prop = MakeProp(go, 45, 32, 24, .95f, .55f, 18);
        prop.KnockbackSpeed = 10;
        // 敵人尋路會繞過靜止的鐵桶（被推動時不雕刻，停下來才會）。
        AddNavObstacle(go, .5f, 2f);
    }

    /// <summary>
    /// 牆上的擺飾刀（一塊木板 + 兩把匕首）：平時固定在牆上，玩家用 Iron 拉它就會脫離牆面飛向自己；
    /// 站在牆前的敵人若剛好在刀與玩家之間，會被從背後刺中（傷害 ×3，一擊斃命）。
    /// side：-1 左牆、+1 右牆；half：牆的內側 |x|；z：沿走道的位置；y：高度。
    /// </summary>
    private static void BladeDisplay(float side, float half, float z, float y = 2f)
    {
        // 圓形木盾底板（圓柱的軸轉成朝向房間）。
        var plaque = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        plaque.name = "Blade Plaque";
        Object.DestroyImmediate(plaque.GetComponent<Collider>());
        plaque.transform.SetParent(props, false);
        plaque.transform.position = new Vector3(side * (half - .05f), y, z);
        plaque.transform.rotation = Quaternion.Euler(0, 0, 90);
        plaque.transform.localScale = new Vector3(1.7f, .04f, 1.7f);
        plaque.GetComponent<Renderer>().sharedMaterial = wood;
        // 兩把交叉的匕首（±40°），刀尖朝向走道，像交叉掛在盾牌上的裝飾刀。
        for (int i = 0; i < 2; i++)
        {
            float tilt = i == 0 ? 40f : -40f;
            // 兩把刀在牆面法線方向錯開一點，交叉處碰撞箱才不會互相重疊。
            var dagger = Dagger(new Vector3(side * (half - (i == 0 ? .14f : .24f)), y, z), tilt);
            var body = DynamicBody(dagger, 5);
            body.isKinematic = true;
            body.useGravity = false;
            var prop = MakeProp(dagger, 5, 55, 16, .7f, 1.5f, 12);
            prop.BackstabMultiplier = 3;
            // 被玩家攜帶後投擲（滾輪射出）：傷害 ×2.5。
            prop.ThrownDamageMultiplier = 2.5f;
            prop.KnockbackSpeed = 6;
            prop.MinDamageSpeed = 4;
            dagger.AddComponent<WallBlade>();
        }
    }

    private static Mesh daggerBlade;

    /// <summary>匕首：兩面開刃的尖刀身 + 護手 + 刀柄 + 圓柄頭。長軸為 +Z（刀尖朝 +Z），平面貼著牆（法線為 X）。</summary>
    private static GameObject Dagger(Vector3 position, float tiltDegrees)
    {
        if (!daggerBlade)
        {
            daggerBlade = MakeDaggerBladeMesh();
        }

        var root = new GameObject("Wall Dagger");
        root.transform.SetParent(interactives, false);
        // 繞 X（牆的法線）轉：刀在牆面的平面裡傾斜，刀尖朝 +Z 方向抬起 / 壓低。
        root.transform.rotation = Quaternion.Euler(-tiltDegrees, 0, 0);
        root.transform.localScale = Vector3.one * 1.2f;
        // position 是整把刀的中心（兩把交叉的交點）；刀的原點在護手，所以往後退到中心位置。
        root.transform.position = position - root.transform.rotation * new Vector3(0, 0, .26f * 1.2f);
        root.AddComponent<MeshFilter>().sharedMesh = daggerBlade;
        root.AddComponent<MeshRenderer>().sharedMaterial = steel;
        var box = root.AddComponent<BoxCollider>();
        box.center = new Vector3(0, 0, .28f);
        box.size = new Vector3(.07f, .14f, 1.1f);

        // 護手（金色橫桿，與刀身垂直）。
        var guard = GameObject.CreatePrimitive(PrimitiveType.Cube);
        guard.name = "Dagger Guard";
        Object.DestroyImmediate(guard.GetComponent<Collider>());
        guard.transform.SetParent(root.transform, false);
        guard.transform.localPosition = new Vector3(0, 0, -.01f);
        guard.transform.localScale = new Vector3(.05f, .3f, .05f);
        guard.GetComponent<Renderer>().sharedMaterial = gold;
        // 刀柄（木）。
        var grip = GameObject.CreatePrimitive(PrimitiveType.Cube);
        grip.name = "Dagger Grip";
        Object.DestroyImmediate(grip.GetComponent<Collider>());
        grip.transform.SetParent(root.transform, false);
        grip.transform.localPosition = new Vector3(0, 0, -.14f);
        grip.transform.localScale = new Vector3(.055f, .055f, .26f);
        grip.GetComponent<Renderer>().sharedMaterial = wood;
        // 柄頭（金色圓球）。
        var pommel = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        pommel.name = "Dagger Pommel";
        Object.DestroyImmediate(pommel.GetComponent<Collider>());
        pommel.transform.SetParent(root.transform, false);
        pommel.transform.localPosition = new Vector3(0, 0, -.29f);
        pommel.transform.localScale = Vector3.one * .1f;
        pommel.GetComponent<Renderer>().sharedMaterial = gold;
        return root;
    }

    /// <summary>刀身網格：寬（Y 方向）漸縮成尖，厚（X 方向）很薄、中間有稜，平直著色。</summary>
    private static Mesh MakeDaggerBladeMesh()
    {
        const float length = .86f, shoulder = .22f, halfWidth = .07f, halfThick = .012f;
        // 剖面是菱形：(厚 ±, 0)、(0, 寬 ±)。環 0 在護手處，環 1 是最寬處之後，最後收成尖端。
        Vector3[] ring0 =
        {
            new Vector3(0, halfWidth, 0), new Vector3(halfThick, 0, 0), new Vector3(0, -halfWidth, 0), new Vector3(-halfThick, 0, 0)
        };
        Vector3[] ring1 =
        {
            new Vector3(0, halfWidth, shoulder), new Vector3(halfThick, 0, shoulder), new Vector3(0, -halfWidth, shoulder), new Vector3(-halfThick, 0, shoulder)
        };
        var tip = new Vector3(0, 0, length);
        var vertices = new System.Collections.Generic.List<Vector3>();
        var triangles = new System.Collections.Generic.List<int>();
        System.Action<Vector3, Vector3, Vector3> tri = (a, b, c) =>
        {
            int i = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            triangles.Add(i);
            triangles.Add(i + 1);
            triangles.Add(i + 2);
        };
        for (int k = 0; k < 4; k++)
        {
            int n = (k + 1) % 4;
            // 環 0 → 環 1 的四個側面（直的一段），環 1 → 尖端的四個斜面。
            tri(ring0[k], ring1[k], ring1[n]);
            tri(ring0[k], ring1[n], ring0[n]);
            tri(ring1[k], tip, ring1[n]);
        }

        // 護手側的底面。
        tri(ring0[0], ring0[1], ring0[2]);
        tri(ring0[0], ring0[2], ring0[3]);
        var mesh = new Mesh { name = "WallDaggerBlade" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        EnsureFolder(Root + "/Meshes");
        string path = Root + "/Meshes/WallDaggerBlade.asset";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(mesh, path);
        return AssetDatabase.LoadAssetAtPath<Mesh>(path);
    }

    private static void Torch(Vector3 position, float side)
    {
        // 壁掛火把：托架 + 火焰 + 暖色閃爍燈光。side = +1 掛在右牆（面向 -X），-1 掛在左牆。
        Box("Torch Bracket", props, position + new Vector3(side * .15f, 0, 0), new Vector3(.3f, .12f, .12f), darkSteel, false);
        Box("Torch Handle", props, position + new Vector3(0, .45f, 0), new Vector3(.1f, .9f, .1f), wood, false);
        var fire = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        fire.name = "Torch Flame";
        Object.DestroyImmediate(fire.GetComponent<Collider>());
        fire.transform.SetParent(props, false);
        fire.transform.position = position + new Vector3(0, 1f, 0);
        fire.transform.localScale = new Vector3(.28f, .4f, .28f);
        fire.GetComponent<Renderer>().sharedMaterial = flame;
        PointLight("Torch Light", position + new Vector3(-side * .5f, 1.1f, 0), new Color(1f, .55f, .25f), 3.2f, 11f, true);
    }

    /// <summary>金屬火盆：玩家可以推倒（Steel）或拉倒（Iron），火熄滅後那一帶變暗，敵人更難發現你。</summary>
    private static void MetalBrazier(Vector3 floorPosition)
    {
        var root = Holder("Metal Brazier", floorPosition);
        var body = DynamicBody(root, 70);
        body.centerOfMass = new Vector3(0, .35f, 0);
        Part(PrimitiveType.Cylinder, "Brazier Base", root.transform, new Vector3(0, .06f, 0), new Vector3(1.1f, .06f, 1.1f), darkSteel, true);
        Part(PrimitiveType.Cylinder, "Brazier Stand", root.transform, new Vector3(0, .55f, 0), new Vector3(.24f, .45f, .24f), darkSteel, false);
        Part(PrimitiveType.Cylinder, "Brazier Bowl", root.transform, new Vector3(0, 1.05f, 0), new Vector3(1.1f, .15f, 1.1f), darkSteel, true);
        var fire = Part(PrimitiveType.Sphere, "Brazier Fire", root.transform, new Vector3(0, 1.4f, 0), new Vector3(.7f, .75f, .7f), flame, false);
        var light = PointLight("Brazier Light", floorPosition + new Vector3(0, 2f, 0), new Color(1f, .45f, .2f), 4.5f, 13f, true, root.transform);
        var prop = MakeProp(root, 70, 12, 18, .9f, .7f, 15);
        prop.KnockbackSpeed = 8;
        AddNavObstacle(root, .65f, 2f);
        var topple = root.AddComponent<ToppleBrazier>();
        topple.FireLight = light;
        topple.Flames = new[] { fire };
    }

    /// <summary>
    /// 金屬柱子：整根（柱基、柱身、柱頭）是一個很重的金屬目標。玩家用 Iron 拉它，會被吸向柱子（很重，不會被拉動）；
    /// Steel 推它則把自己推開。是中庭裡拉著自己快速移動、翻越敵人視線的錨點。
    /// </summary>
    private static void Pillar(Vector3 floorPosition, float width = 1.4f)
    {
        float shaft = H - 1.2f;
        // 根物件：MetalDisplayer 要求有 Renderer，空網格的 MeshFilter 讓 Outline 略過它（見 Holder）。
        var root = new GameObject("Metal Pillar");
        root.transform.SetParent(structure, false);
        root.transform.position = floorPosition;
        root.AddComponent<MeshFilter>();
        root.AddComponent<MeshRenderer>().sharedMaterial = stone;
        Box("Pillar Base", root.transform, floorPosition + new Vector3(0, .3f, 0), new Vector3(width + .5f, .6f, width + .5f), trim);
        Box("Pillar Shaft", root.transform, floorPosition + new Vector3(0, .6f + shaft * .5f - .3f, 0), new Vector3(width, shaft, width), stone);
        Box("Pillar Capital", root.transform, floorPosition + new Vector3(0, H - .3f, 0), new Vector3(width + .5f, .6f, width + .5f), trim);
        MakeMetal(root, 10000, false);
    }

    /// <summary>
    /// 吊燈：對它發動推 / 拉，鎖鏈會斷、吊燈垂直砸下，範圍傷害 + 巨大聲響 + 蠟燭熄滅。
    /// 把它掛在敵人巡邏 / 站崗的路徑上方就是陷阱。
    /// </summary>
    private static void Chandelier(Vector3 center, float areaRadius)
    {
        var root = Holder("Chandelier", center);
        var body = root.AddComponent<Rigidbody>();
        body.mass = 60;
        body.isKinematic = true;
        body.useGravity = false;
        Part(PrimitiveType.Cylinder, "Chandelier Ring", root.transform, Vector3.zero, new Vector3(3f, .125f, 3f), darkSteel, true);
        for (int i = 0; i < 6; i++)
        {
            float a = i / 6f * Mathf.PI * 2f;
            var p = new Vector3(Mathf.Cos(a) * 1.3f, .35f, Mathf.Sin(a) * 1.3f);
            Part(PrimitiveType.Cylinder, "Candle " + i, root.transform, p, new Vector3(.16f, .2f, .16f), trim, false);
            Part(PrimitiveType.Sphere, "Candle Flame", root.transform, p + new Vector3(0, .3f, 0), new Vector3(.14f, .2f, .14f), flame, false);
        }

        // 四條鎖鏈連到天花板（斷裂時隱藏）。
        float chainLength = H - center.y;
        var chains = new GameObject[4];
        for (int i = 0; i < 4; i++)
        {
            float a = i / 4f * Mathf.PI * 2f + Mathf.PI * .25f;
            chains[i] = Part(PrimitiveType.Cube, "Chain", root.transform, new Vector3(Mathf.Cos(a) * 1.1f, chainLength * .5f, Mathf.Sin(a) * 1.1f), new Vector3(.08f, chainLength, .08f), darkSteel, false);
        }

        var light = PointLight("Chandelier Light", center + new Vector3(0, .6f, 0), new Color(1f, .6f, .3f), 2.6f, 10f, true, root.transform);
        // 砸落是秒殺：範圍內的敵人無論血量都會死（玩家站在底下也會受重傷）。
        var prop = MakeProp(root, 60, 9999, 36, 1f, .6f, 40);
        prop.AreaRadius = areaRadius;
        prop.MinDamageSpeed = 3;
        prop.KnockbackSpeed = 12;
        var drop = root.AddComponent<DroppableChandelier>();
        drop.Chains = chains;
        drop.CandleLights = new[] { light };
    }

    private static void Statue(Vector3 floorPosition, float yaw)
    {
        var root = new GameObject("Inquisitor Statue").transform;
        root.SetParent(props, false);
        root.position = floorPosition;
        root.rotation = Quaternion.Euler(0, yaw, 0);
        Box("Pedestal", root, floorPosition + new Vector3(0, .5f, 0), new Vector3(1.6f, 1f, 1.6f), trim).transform.SetParent(root, true);
        Box("Statue Robe", root, floorPosition + new Vector3(0, 2.3f, 0), new Vector3(1f, 2.6f, .8f), darkSteel).transform.SetParent(root, true);
        Box("Statue Shoulders", root, floorPosition + new Vector3(0, 3.4f, 0), new Vector3(1.5f, .4f, .8f), darkSteel).transform.SetParent(root, true);
        var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        head.name = "Statue Head";
        head.transform.SetParent(root, true);
        head.transform.position = floorPosition + new Vector3(0, 3.95f, 0);
        head.transform.localScale = new Vector3(.6f, .7f, .6f);
        head.GetComponent<Renderer>().sharedMaterial = darkSteel;
        // 手持的大斧（致敬審判者）。
        var axe = Box("Statue Axe Shaft", root, floorPosition + new Vector3(.8f, 2.4f, .2f), new Vector3(.12f, 3.6f, .12f), darkSteel);
        axe.transform.SetParent(root, true);
        Box("Statue Axe Head", root, floorPosition + new Vector3(.8f, 4.0f, .2f), new Vector3(.12f, 1.0f, .9f), darkSteel).transform.SetParent(root, true);
    }


    private static void BuildDoor()
    {
        // 大門：需要 Pewter 強化的劍擊破壞（BreakableObstacle）。門後是通往 Boss 房的前室。
        var door = new GameObject("Castle Door");
        door.transform.SetParent(props, false);
        door.transform.position = new Vector3(0, 0, DoorZ);
        var col = door.AddComponent<BoxCollider>();
        col.center = new Vector3(0, 4f, 0);
        col.size = new Vector3(8f, 8f, 1.2f);
        var breakable = door.AddComponent<BreakableObstacle>();
        breakable.MaxHealth = 100;
        breakable.CurrentHealth = 100;
        // 敲門有聲響；打破的瞬間全場警報。
        door.AddComponent<BreakableNoise>();
        var navObstacle = door.AddComponent<NavMeshObstacle>();
        navObstacle.carving = true;
        navObstacle.shape = NavMeshObstacleShape.Box;
        navObstacle.center = new Vector3(0, 1f, 0);
        navObstacle.size = new Vector3(8f, 2f, 1.2f);

        // 兩扇門板各自掛在外側的門軸上（x = ±4），玩家攻擊大門時會繞門軸往門後（+Z）打開（見 DoorOpener）。
        var hinges = new Transform[2];
        for (int h = 0; h < 2; h++)
        {
            float s = h == 0 ? -1f : 1f;   // -1 左扇、+1 右扇
            var hinge = new GameObject(h == 0 ? "Door Hinge L" : "Door Hinge R").transform;
            hinge.SetParent(door.transform, false);
            hinge.position = new Vector3(s * 4f, 0, DoorZ);
            hinges[h] = hinge;
            Box(h == 0 ? "Door Leaf L" : "Door Leaf R", hinge, new Vector3(s * 2f, 4f, DoorZ), new Vector3(3.9f, 8f, .8f), doorMat, false);
            for (int i = 0; i < 4; i++)
            {
                Box("Door Band", hinge, new Vector3(s * 2f, 1f + i * 2.1f, DoorZ - .45f), new Vector3(3.9f, .3f, .1f), darkSteel, false);
            }

            Box("Door Seam Glow", hinge, new Vector3(s * .06f, 4f, DoorZ - .43f), new Vector3(.06f, 7.6f, .06f), glowGold, false);
            foreach (float x in new[] { 1.2f, 3.4f })
            {
                for (int i = 0; i < 4; i++)
                {
                    Box("Door Stud", hinge, new Vector3(s * x, 1.8f + i * 1.7f, DoorZ - .47f), new Vector3(.25f, .25f, .08f), steel, false);
                }
            }
        }

        // 被打破後門不消失，而是兩扇門板整個打開；每次攻擊也會讓門縫開得更大。
        breakable.DestroyOnBreak = false;
        var opener = door.AddComponent<DoorOpener>();
        opener.LeftLeaf = hinges[0];
        opener.RightLeaf = hinges[1];

        // 門框。
        Box("Door Frame L", structure, new Vector3(-4.6f, 4f, DoorZ), new Vector3(1.2f, 8f, 1.6f), trim);
        Box("Door Frame R", structure, new Vector3(4.6f, 4f, DoorZ), new Vector3(1.2f, 8f, 1.6f), trim);
        Box("Door Lintel", structure, new Vector3(0, 8.5f, DoorZ), new Vector3(10, 1f, 1.6f), trim);

        // 門後的前室：明亮的金色光（Boss 房就在前面）。
        PointLight("Antechamber Light", new Vector3(0, 5f, DoorZ + 5), new Color(1f, .85f, .55f), 5f, 16f, false);
        Box("Antechamber Pedestal", props, new Vector3(0, .3f, EndZ - 3), new Vector3(3, .6f, 3), trim);

        // 前室盡頭的觸發區：走進去就切換到 Boss 房。
        AddBossRoomTrigger();
    }

    /// <summary>在前室盡頭放切換場景的觸發區，並確保兩個場景都在 Build Settings 中。</summary>
    public static void AddBossRoomTrigger()
    {
        var existing = GameObject.Find("Boss Room Trigger");
        if (existing)
        {
            Object.DestroyImmediate(existing);
        }

        var trigger = new GameObject("Boss Room Trigger");
        trigger.transform.position = new Vector3(0, 3f, EndZ - 1.5f);
        var box = trigger.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(9f, 6f, 2f);
        var load = trigger.AddComponent<SceneLoadTrigger>();
        load.SceneName = "BossArena";

        var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        foreach (string path in new[] { ScenePath, "Assets/Scenes/BossArena.unity" })
        {
            if (!scenes.Exists(s => s.path == path))
            {
                scenes.Add(new EditorBuildSettingsScene(path, true));
            }
        }

        EditorBuildSettings.scenes = scenes.ToArray();
    }

    // --- atmosphere, navigation, placement

    private static void ConfigureAtmosphere(UnityEngine.SceneManagement.Scene scene)
    {
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(.03f, .032f, .042f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(.012f, .012f, .018f);
        RenderSettings.fogDensity = .014f;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
        Light sun = null;
        foreach (var root in scene.GetRootGameObjects())
        {
            var light = root.GetComponent<Light>();
            if (light && light.type == LightType.Directional)
            {
                // 實際幾乎全黑（亮度 0.03）。顏色和方向則是 Tin 夜視的「參考光」：
                // 夜視會依這盞光的顏色與方向補亮表面，不會用到它的實際亮度。
                light.intensity = .03f;
                light.color = new Color(.85f, .9f, 1f);
                light.shadows = LightShadows.None;
                sun = light;
            }

            foreach (var cam in root.GetComponentsInChildren<Camera>(true))
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
            }
        }

        // 明確指定夜視的參考光，不依賴 RenderSettings.sun 的自動挑選。
        if (sun)
        {
            RenderSettings.sun = sun;
            foreach (var root in scene.GetRootGameObjects())
            {
                var tin = root.GetComponent<PlayerTinAbility>();
                if (tin)
                {
                    tin.ReferenceLight = sun;
                }
            }
        }
    }

    private static void BakeNavMesh(GameObject corridor)
    {
        var surface = corridor.GetComponent<NavMeshSurface>();
        if (!surface)
        {
            surface = corridor.AddComponent<NavMeshSurface>();
        }

        surface.collectObjects = CollectObjects.Children;
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surface.BuildNavMesh();
        string path = "Assets/Scenes/CastleEntrance_NavMesh.asset";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(surface.navMeshData, path);
        surface.navMeshData = AssetDatabase.LoadAssetAtPath<NavMeshData>(path);
    }

    private static GameObject Spawn(GameObject prefab, Transform parent, string name, Vector3 position, float yaw, float wanderRadius = -1, bool stoic = false)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.name = name;
        go.transform.position = position;
        go.transform.rotation = Quaternion.Euler(0, yaw, 0);
        var melee = go.GetComponent<TestEnemy>();
        if (wanderRadius >= 0 && melee)
        {
            melee.WanderRadius = wanderRadius;
        }

        if (stoic)
        {
            go.GetComponent<EnemySenses>().Stoic = true;
        }

        // 尋路時敵人彼此也會繞開（高品質避障 + 隨機優先權，避免兩個人互相卡住）。
        var agent = go.GetComponent<NavMeshAgent>();
        if (agent)
        {
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
            agent.avoidancePriority = Random.Range(30, 70);
        }

        return go;
    }


    private static Transform[] PatrolOf(GameObject enemy, Transform[] route, float speed = 1.5f)
    {
        var melee = enemy.GetComponent<TestEnemy>();
        melee.PatrolPoints = route;
        melee.WanderSpeed = speed;
        melee.WanderWait = 1.2f;
        return route;
    }

    /// <summary>
    /// 敵人配置（17 個）。多條路線、各有不同的難題；任何一個敵人親眼發現玩家，附近的敵人都會一起圍上來（警報）：
    /// - 中庭（樞紐）：兩組守衛各沿自己的路線繞圈巡邏（內圈繞光池、外圈沿著暗處外牆），巡邏週期不同，要抓空檔；四個埋伏者守在各出口兩側的暗處。
    /// - 西翼兵器庫：兩個 Stoic 哨兵（不理聲響）站牆邊，投擲兵守側門。解法：Zinc 哨兵引到吊燈下、或拉他們背後牆上的飾刀。
    /// - 東翼迴廊：高台上的投擲兵俯瞰全場、兩個埋伏者、一組巡邏守衛。解法：躲高台視線、金幣引開、吊燈。
    /// - 中央走道：最短但最危險——前半段有巡邏守衛，後半段是典獄長 + 兩個投擲兵。翼樓側門（z=70）可以繞過前半段。
    /// </summary>
    private static void PlaceEnemies(GameObject ambusher, GameObject guard, GameObject thrower)
    {
        var enemies = new GameObject("Castle Enemies").transform;

        // ── 中庭：內圈（繞中央光池）與外圈（沿外牆暗處）各一組巡邏守衛。
        var inner = Spawn(guard, enemies, "Guard Patrol A (inner)", new Vector3(-9f, 0, 24), 90);
        PatrolOf(inner, Route("Patrol A", new Vector3(-9f, 0, 24), new Vector3(9f, 0, 24), new Vector3(9f, 0, 37), new Vector3(-9f, 0, 37)), 1.5f);
        var outer = Spawn(guard, enemies, "Guard Patrol B (outer)", new Vector3(19f, 0, 16), 0);
        PatrolOf(outer, Route("Patrol B", new Vector3(-19f, 0, 16), new Vector3(19f, 0, 16), new Vector3(19f, 0, 44), new Vector3(-19f, 0, 44)), 1.3f);
        // 埋伏者：西 / 東通道兩側各一個，中央大門兩側各一個（躲在暗處等你經過）。
        Spawn(ambusher, enemies, "Ambusher W (west passage)", new Vector3(-17.8f, 0, 45.6f), 0);
        Spawn(ambusher, enemies, "Ambusher E (east passage)", new Vector3(17.8f, 0, 45.6f), 0);
        Spawn(ambusher, enemies, "Ambusher C1 (center gate)", new Vector3(-5.4f, 0, 45.6f), 0);
        Spawn(ambusher, enemies, "Ambusher C2 (center gate)", new Vector3(5.4f, 0, 45.6f), 0);

        // ── 西翼兵器庫：兩個 Stoic 哨兵站牆邊（背後是擺飾刀，聲響引不開）、投擲兵守側門。
        Spawn(guard, enemies, "Sentinel 1 (west)", new Vector3(-18f, 0, 58), 90, .8f, true);
        Spawn(guard, enemies, "Sentinel 2 (west)", new Vector3(-18f, 0, 66), 90, .8f, true);
        Spawn(thrower, enemies, "Thrower W (side door)", new Vector3(-10f, 0, 72f), 180);

        // ── 東翼迴廊：高台投擲兵、兩個埋伏者（側門旁 / 通道旁）、一組巡邏守衛繞著柱子走。
        Spawn(thrower, enemies, "Thrower E (ledge)", new Vector3(20.25f, 2.4f, 66), 180);
        Spawn(ambusher, enemies, "Ambusher E1 (gallery)", new Vector3(7.5f, 0, 53), 0);
        Spawn(ambusher, enemies, "Ambusher E2 (side door)", new Vector3(7.5f, 0, 74.5f), 180);
        var gallery = Spawn(guard, enemies, "Guard Patrol C (gallery)", new Vector3(9f, 0, 54), 0);
        PatrolOf(gallery, Route("Patrol C", new Vector3(9f, 0, 54), new Vector3(17f, 0, 54), new Vector3(17f, 0, 72), new Vector3(9f, 0, 72)), 1.4f);

        // ── 中央走道：前半段一個守衛來回巡邏（z 52~66）；後半段典獄長 + 兩個投擲兵。
        var corridorGuard = Spawn(guard, enemies, "Guard Patrol D (corridor)", new Vector3(0, 0, 52), 0);
        PatrolOf(corridorGuard, Route("Patrol D", new Vector3(0, 0, 52), new Vector3(0, 0, 66)), 1.3f);
        // 典獄長（血 320、攻擊很重、Stoic）堵在大門前。
        // 解法一：Zinc 他，讓他沿著紅地毯衝過來，經過地毯上方的吊燈時砸下去。
        // 解法二：Brass 他（6 秒），趁機走到門前把門劈開（約 3 秒），進去就是 Boss 房。
        var warden = Spawn(guard, enemies, "Warden (door)", new Vector3(0, 0, 88.5f), 180, .6f, true);
        var wardenEnemy = warden.GetComponent<Enemy>();
        wardenEnemy.MaxHealth = 320;
        wardenEnemy.CurrentHealth = 320;
        var wardenAi = warden.GetComponent<TestEnemy>();
        wardenAi.AttackDamage = 26;
        wardenAi.DetectionRange = 17;
        wardenAi.ChaseSpeed = 3.1f;
        Spawn(thrower, enemies, "Thrower 3 (door left)", new Vector3(-3.6f, 0, 87.5f), 180);
        Spawn(thrower, enemies, "Thrower 4 (door right)", new Vector3(3.6f, 0, 87.5f), 180);
    }

    private static void PlacePlayer(UnityEngine.SceneManagement.Scene scene)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.name == "Player")
            {
                root.transform.position = new Vector3(0, 1.1f, 4);
                root.transform.rotation = Quaternion.identity;
                // 進入這個場景時，所有金屬能力預設關閉（要在金屬輪盤 / 面板自己開啟）。
                var inventory = root.GetComponent<PlayerMetalInventory>();
                if (inventory && inventory.Metals != null)
                {
                    foreach (var reserve in inventory.Metals)
                    {
                        if (reserve != null)
                        {
                            reserve.IsEnabled = false;
                        }
                    }

                    EditorUtility.SetDirty(inventory);
                }

                // 死亡後：淡出成全黑 → 重新載入本關 → 淡入（取代突然變暗又突然變亮）。
                if (!root.GetComponent<DeathReload>())
                {
                    root.AddComponent<DeathReload>();
                }
            }
        }
    }
}
#endif
