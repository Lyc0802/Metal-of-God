using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SimpleOutline;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

// Project-local Editor test entry point. The request file permits running the same
// checks when the MCP editor session is temporarily unavailable; no UI automation.
/// <summary>
/// 在實際場景與 Play Mode 驗證金屬偵測、Outline、AI 及 Inspector 事件整合。
/// </summary>
public static class CopperBronzeVerification
{
    private const string Folder = "Temp/CopperBronze";
    private const string StageKey = "CopperBronzeVerification.Stage";
    private static IEnumerator routine;
    private static readonly List<string> report = new List<string>();
    private static readonly List<Object> temporary = new List<Object>();
    private static int lastFrame = -1;
    private static bool originalBackground;
    [InitializeOnLoadMethod]
    private static void Initialize()
    {
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    [MenuItem("Tools/Metal Abilities/Integrate Copper and Bronze")]
    public static void Integrate()
    {
        if (Application.isPlaying)
        {
            throw new InvalidOperationException("Exit Play Mode before integrating.");
        }

        int actors = 0;
        foreach (var inventory in Object.FindObjectsByType<PlayerMetalInventory>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None))
        {
            if (!inventory.gameObject.scene.IsValid() || !inventory.gameObject.scene.isLoaded)
            {
                continue;
            }

            Configure(inventory.gameObject, true);
            EditorSceneManager.MarkSceneDirty(inventory.gameObject.scene);
            actors++;
        }

        foreach (var enemy in Object.FindObjectsByType<Enemy>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!enemy.gameObject.scene.IsValid() || !enemy.gameObject.scene.isLoaded)
            {
                continue;
            }

            Configure(enemy.gameObject, false);
            EditorSceneManager.MarkSceneDirty(enemy.gameObject.scene);
            actors++;
        }

        const string path = "Assets/Combat/TestEnemy.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path))
        {
            var prefab = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Configure(prefab, false);
                PrefabUtility.SaveAsPrefabAsset(prefab, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefab);
            }
        }

        EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();
        Directory.CreateDirectory(Folder);
        File.WriteAllText(
            Folder + "/integration.txt",
            "Integrated " + actors + " scene actors and TestEnemy prefab. Configured component radii, existing reserves and legacy UsingMetal preserved.");
    }

    private static void Configure(GameObject go, bool player)
    {
        if (!go.GetComponent<CopperSuppression>())
        {
            Undo.AddComponent<CopperSuppression>(go);
        }

        var detector = go.GetComponent<BronzeDetection>();
        if (!detector)
        {
            detector = Undo.AddComponent<BronzeDetection>(go);
        }

        if (player)
        {
            Undo.RecordObject(detector, "Player Bronze camera outline");
            detector.ShowOutlines = true;
            var controller = go.GetComponent<PlayerController>();
            if (controller)
            {
                detector.DetectionCamera = controller.ViewCamera;
            }

            EditorUtility.SetDirty(detector);
        }
    }

    [MenuItem("Tools/Metal Abilities/Run Copper Bronze Checks")]
    public static void RequestTests()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Folder + "/request.txt", DateTime.UtcNow.Ticks.ToString());
    }

    private static void Tick()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            return;
        }

        if (routine != null)
        {
            if (!Application.isPlaying)
            {
                Finish("FAIL Play Mode ended before completion");
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

            return;
        }

        string stage = SessionState.GetString(StageKey, "");
        if (stage == "play")
        {
            if (!Application.isPlaying || EditorApplication.isPaused)
            {
                return;
            }

            originalBackground = Application.runInBackground;
            Application.runInBackground = true;
            report.Clear();
            temporary.Clear();
            lastFrame = -1;
            routine = Tests();
            WriteStatus("Running Play Mode tests");
            return;
        }

        if (stage == "exit")
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            SessionState.SetString(StageKey, "");
            WriteStatus("Finished; scriptCompilationFailed=" + EditorUtility.scriptCompilationFailed);
            return;
        }

        string request = Folder + "/request.txt";
        if (Application.isPlaying || !File.Exists(request) || string.IsNullOrWhiteSpace(File.ReadAllText(request)))
        {
            return;
        }

        File.WriteAllText(request, "");
        try
        {
            WriteStatus("Integrating scene and prefab");
            Integrate();
            if (EditorUtility.scriptCompilationFailed)
            {
                throw new InvalidOperationException("Unity reports script compilation failure");
            }

            var shader = Shader.Find("Hidden/SimpleOutline");
            if (!shader || ShaderUtil.GetShaderMessages(shader).Any(m => m.severity.ToString() == "Error"))
            {
                throw new InvalidOperationException("Outline shader errors");
            }

            SessionState.SetString(StageKey, "play");
            WriteStatus("Entering Play Mode");
            EditorApplication.isPlaying = true;
        }
        catch (Exception e)
        {
            report.Clear();
            report.Add("FAIL " + e);
            Directory.CreateDirectory(Folder);
            File.WriteAllLines(Folder + "/results.txt", report);
            WriteStatus("Failed during setup");
        }
    }

    private static void WriteStatus(string text)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Folder + "/status.txt", DateTime.Now.ToString("O") + " " + text);
    }

    private static void Check(bool success, string message)
    {
        if (!success)
        {
            throw new InvalidOperationException(message);
        }

        report.Add("PASS " + message);
    }

    private static void Finish(string result)
    {
        (routine as IDisposable)?.Dispose();
        routine = null;
        foreach (var obj in temporary)
        {
            if (obj)
            {
                Object.DestroyImmediate(obj);
            }
        }

        temporary.Clear();
        Application.runInBackground = originalBackground;
        report.Add(result);
        Directory.CreateDirectory(Folder);
        File.WriteAllLines(Folder + "/results.txt", report);
        SessionState.SetString(StageKey, "exit");
        WriteStatus(result);
        EditorApplication.isPlaying = false;
    }

    private static T Keep<T>(T obj)
        where T : Object
    {
        temporary.Add(obj);
        return obj;
    }

    private static void Enable(PlayerMetalInventory inventory, MetalType metal, bool enabled = true, float amount = 100)
    {
        var reserve = inventory.Get(metal);
        reserve.IsEnabled = enabled;
        reserve.CurrentAmount = amount;
    }

    private static void Empty(PlayerMetalInventory inventory)
    {
        foreach (var reserve in inventory.Metals)
        {
            reserve.IsEnabled = false;
            reserve.CurrentAmount = 100;
        }
    }

    private static void Metals(Enemy enemy, params MetalType[] metals)
    {
        enemy.UsingMetal = MetalType.None;
        enemy.MetalAbilities = metals.Select(m => new MetalReserve { Type = m, IsEnabled = true, CurrentAmount = 100 }).ToArray();
    }

    private static Material Gray()
    {
        var material = Keep(new Material(Shader.Find("Universal Render Pipeline/Unlit")));
        material.SetColor("_BaseColor", new Color(.16f, .16f, .16f, 1));
        return material;
    }

    private static GameObject Shape(string name, PrimitiveType type, Vector3 position, Vector3 scale)
    {
        var go = Keep(GameObject.CreatePrimitive(type));
        go.name = name;
        go.transform.position = position;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = Gray();
        return go;
    }

    private static PlayerMetalInventory Player(string name, Vector3 position)
    {
        var go = Keep(new GameObject(name));
        go.transform.position = position;
        go.AddComponent<PlayerHealth>();
        var inventory = go.AddComponent<PlayerMetalInventory>();
        Empty(inventory);
        return inventory;
    }

    private static Enemy Actor(string name, Vector3 position, params MetalType[] metals)
    {
        var go = Shape(name, PrimitiveType.Capsule, position, Vector3.one);
        var actor = go.AddComponent<Enemy>();
        Metals(actor, metals);
        return actor;
    }

    private static Color32[] Render(Camera camera, RenderTexture texture)
    {
        camera.Render();
        var prior = RenderTexture.active;
        RenderTexture.active = texture;
        var pixels = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
        try
        {
            pixels.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
            pixels.Apply();
            return pixels.GetPixels32();
        }
        finally
        {
            RenderTexture.active = prior;
            Object.DestroyImmediate(pixels);
        }
    }

    private static int Gold(Color32[] pixels) => pixels.Count(c => c.r > 135 && c.g > 45 && c.b < 100
        && c.r > c.g * 1.3f);
    private static int Cyan(Color32[] pixels) => pixels.Count(c => c.g > 100 && c.b > 100 && c.r < c.g * .65f);
    private static IEnumerator Tests()
    {
        var observer = Player("Bronze test observer", new Vector3(40, 1, 28));
        Enable(observer, MetalType.Bronze);
        var detector = observer.GetComponent<BronzeDetection>();
        detector.DetectionRadius = 20;
        detector.OutlineColor = new Color(1, .5f, 0, 1);
        var cameraGo = Keep(new GameObject("Bronze verification camera"));
        var camera = cameraGo.AddComponent<Camera>();
        camera.enabled = false;
        camera.transform.position = observer.transform.position;
        camera.transform.rotation = Quaternion.identity;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.fieldOfView = 50;
        camera.nearClipPlane = .1f;
        camera.farClipPlane = 40;
        var texture = Keep(new RenderTexture(384, 384, 24, RenderTextureFormat.ARGB32));
        texture.Create();
        camera.targetTexture = texture;
        detector.ShowOutlines = true;
        detector.DetectionCamera = camera;
        var target = Actor("Bronze test target", new Vector3(40, 1, 36), MetalType.Pewter);
        var wall = Shape("Bronze opaque test wall", PrimitiveType.Cube, new Vector3(40, 1, 32), new Vector3(5, 5, .5f));
        var metalGo = Shape(
            "Independent metal outline",
            PrimitiveType.Cube,
            new Vector3(40.7f, 1, 30),
            Vector3.one * .3f);
        var metal = metalGo.AddComponent<MetalDisplayer>();
        metal.UseWeightOutlineColors = false;
        metal.SetOutlineColor(Color.cyan);
        typeof(MetalDisplayer).GetField(
            "player",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(
            metal,
            observer.transform);
        typeof(MetalDisplayer).GetMethod(
            "UpdateRegistration",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(
            metal,
            null);
        metal.Display();
        Physics.SyncTransforms();
        yield return null;
        // The scene PlayerController owns the normal metal selection display and
        // may Undisplay every ActiveMetal during the yielded frame. Request the
        // fixture's independent normal outline after that input update.
        metal.Display();
        detector.RefreshDetections();
        Check(
            !CombatSight.Clear(observer.MetalSignalPosition, target.TargetPoint, observer.transform, target.transform),
            "Opaque wall blocks ordinary sight in the render test");
        var on = Render(camera, texture);
        int gold = Gold(on), cyan = Cyan(on);
        Check(
            detector.CanDetect(target) && gold > 15,
            "1 Bronze ON: active Enemy has an actual X-Ray outline behind the opaque wall (gold pixels=" + gold + ")");
        Enable(observer, MetalType.Bronze, false);
        Check(
            Gold(Render(camera, texture)) == 0,
            "2 Bronze OFF removes the outline immediately, before the next detection tick");
        detector.RefreshDetections();
        Check(detector.DetectedActors.Count == 0, "Bronze OFF clears cached detections");
        int cyanAfter = Cyan(Render(camera, texture));
        Check(
            metal.Outlined && cyanAfter > 10 && cyan > 10,
            "11 Original visible metal outline survives Bronze OFF (outlined=" + metal.Outlined + ", cyan before=" + cyan + ", after=" + cyanAfter + ")");
        Enable(observer, MetalType.Bronze);
        detector.RefreshDetections();
        Metals(target);
        Check(
            !detector.CanDetect(target) && Gold(Render(camera, texture)) == 0,
            "3 Enemy with no enabled metal has no Bronze outline");
        Metals(target, MetalType.Pewter);
        target.GetMetal(MetalType.Pewter).CurrentAmount = 0;
        Check(!detector.CanDetect(target), "Enabled metal with an empty reserve emits no signal");
        Metals(target, MetalType.Copper, MetalType.Pewter);
        Check(
            !detector.CanDetect(target) && Gold(Render(camera, texture)) == 0,
            "4 Enemy Copper hides all of its own metal signals");
        target.GetMetal(MetalType.Copper).CurrentAmount = 0;
        Check(detector.CanDetect(target), "Empty Copper reserve cannot suppress another active metal");
        Metals(target, MetalType.Pewter);
        var cloud = Actor("Copper protector A", new Vector3(41, 1, 36), MetalType.Copper);
        cloud.GetComponent<CopperSuppression>().CopperRadius = 3;
        Physics.SyncTransforms();
        bool hidden = !detector.CanDetect(target);
        int hiddenGold = Gold(Render(camera, texture));
        Check(
            hidden && hiddenGold == 0,
            "5 Another Copper user protects the Enemy through the shared radius query (hidden=" + hidden + ", gold=" + hiddenGold + ", copper active=" + cloud.GetComponent<CopperSuppression>().IsActive + ", positions=" + cloud.MetalSignalPosition + " / " + target.MetalSignalPosition + ")");
        cloud.GetMetal(MetalType.Copper).IsEnabled = false;
        Check(detector.CanDetect(target), "Copper OFF immediately restores detection");
        var second = Actor("Copper protector B", new Vector3(39, 1, 36), MetalType.Copper);
        second.GetComponent<CopperSuppression>().CopperRadius = 3;
        Physics.SyncTransforms();
        cloud.GetMetal(MetalType.Copper).IsEnabled = true;
        cloud.GetMetal(MetalType.Copper).IsEnabled = false;
        Check(
            !detector.CanDetect(target),
            "Overlapping Copper clouds form a union; disabling one does not expose the target");
        second.gameObject.SetActive(false);
        Check(detector.CanDetect(target), "Disabling the last Copper user restores detection");
        target.transform.position += Vector3.forward * 22;
        Physics.SyncTransforms();
        Check(
            !detector.CanDetect(target) && Gold(Render(camera, texture)) == 0,
            "Bronze radius excludes an out-of-range active actor");
        target.transform.position -= Vector3.forward * 22;
        Physics.SyncTransforms();
        detector.RefreshDetections();
        Check(Gold(Render(camera, texture)) > 15, "Returning to Bronze radius restores the X-Ray outline");
        foreach (var reserve in observer.Metals)
        {
            reserve.IsEnabled = false;
        }

        Enable(observer, MetalType.Iron);
        Check(
            observer.IsUsingAnyMetal && observer.CanUse(MetalType.Iron),
            "Shared Player metal status preserves the inventory CanUse API");
        Enable(observer, MetalType.Bronze);
        cloud.GetMetal(MetalType.Copper).IsEnabled = true;
        float copperAmount = cloud.GetMetal(MetalType.Copper).CurrentAmount, bronzeAmount = observer.Get(MetalType.Bronze).CurrentAmount;
        yield return null;
        Check(
            cloud.GetMetal(MetalType.Copper).CurrentAmount == copperAmount
            && observer.Get(MetalType.Bronze).CurrentAmount == bronzeAmount,
            "Copper and Bronze do not consume reserves");
        Check(
            NavMesh.SamplePosition(new Vector3(10, 0, -10), out var nav, 20, NavMesh.AllAreas),
            "Test scene supplies a baked NavMesh");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Combat/TestEnemy.prefab");
        var aiGo = Keep(Object.Instantiate(prefab));
        var ai = aiGo.GetComponent<TestEnemy>();
        var aiEnemy = aiGo.GetComponent<Enemy>();
        var agent = aiGo.GetComponent<NavMeshAgent>();
        agent.Warp(nav.position);
        ai.Player = observer.GetComponent<PlayerHealth>();
        Metals(aiEnemy, MetalType.Bronze);
        var enemyBronze = aiGo.GetComponent<BronzeDetection>();
        enemyBronze.DetectionRadius = 25;
        Empty(observer);
        Enable(observer, MetalType.Iron);
        bool clear = false;
        for (int candidate = 0; candidate < 8; candidate++)
        {
            float angle = candidate * Mathf.PI / 4;
            var direction = new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle));
            if (!NavMesh.SamplePosition(nav.position + direction * 6, out var playerNav, 3, NavMesh.AllAreas))
            {
                continue;
            }

            observer.transform.position = playerNav.position + Vector3.up;
            Physics.SyncTransforms();
            if (Vector3.Distance(aiEnemy.TargetPoint, observer.MetalSignalPosition) > 3
                && CombatSight.Clear(
                aiEnemy.TargetPoint,
                observer.MetalSignalPosition,
                aiEnemy.transform,
                observer.transform))
            {
                clear = true;
                break;
            }
        }

        Check(clear, "AI test uses a NavMesh pair with an initially clear ordinary sight line");
        wall.transform.position = (aiEnemy.TargetPoint + observer.MetalSignalPosition) * .5f;
        wall.transform.localScale = new Vector3(5, 5, .5f);
        wall.transform.rotation = Quaternion.LookRotation(observer.MetalSignalPosition - aiEnemy.TargetPoint);
        Physics.SyncTransforms();
        yield return null;
        Check(
            !CombatSight.Clear(aiEnemy.TargetPoint, observer.MetalSignalPosition, aiEnemy.transform, observer.transform),
            "AI test starts behind a sight-blocking wall");
        ai.TickAI();
        Check(
            aiEnemy.HasAbilityDetection(observer.transform) && ai.State == EnemyState.Chase,
            "6 Bronze Enemy detects an active Player through walls and starts Chase through the detection API");
        Enable(observer, MetalType.Copper);
        ai.TickAI();
        Check(
            !aiEnemy.HasAbilityDetection(observer.transform) && ai.State == EnemyState.Wander,
            "7 Player Copper immediately removes an ability-only Chase behind the wall");
        Enable(observer, MetalType.Copper, false);
        cloud.transform.position = observer.transform.position + Vector3.right;
        cloud.GetMetal(MetalType.Copper).IsEnabled = true;
        Physics.SyncTransforms();
        ai.TickAI();
        Check(
            !aiEnemy.HasAbilityDetection(observer.transform) && ai.State == EnemyState.Wander,
            "8 Other Copper user protects Player and clears Bronze tracking");
        cloud.transform.position += Vector3.right * 8;
        Physics.SyncTransforms();
        ai.TickAI();
        Check(
            aiEnemy.HasAbilityDetection(observer.transform) && ai.State == EnemyState.Chase,
            "9 Leaving another user's Copper radius restores Bronze Chase");
        aiEnemy.GetMetal(MetalType.Bronze).CurrentAmount = 0;
        ai.TickAI();
        Check(
            !aiEnemy.HasAbilityDetection(observer.transform) && ai.State == EnemyState.Wander,
            "Enemy Bronze requires a positive reserve");
        aiEnemy.GetMetal(MetalType.Bronze).CurrentAmount = 100;
        ai.TickAI();
        Check(aiEnemy.HasAbilityDetection(observer.transform), "Refilling an enabled Bronze reserve restores detection");
        aiEnemy.GetMetal(MetalType.Bronze).IsEnabled = false;
        ai.TickAI();
        Check(
            !aiEnemy.HasAbilityDetection(observer.transform) && ai.State == EnemyState.Wander,
            "Enemy Bronze OFF disables its special detection and clears an ability-only track");
        wall.SetActive(false);
        Enable(observer, MetalType.Copper);
        Physics.SyncTransforms();
        ai.TickAI();
        Check(ai.State == EnemyState.Chase, "10 Ordinary visual detection still sees a Copper-protected Player");
        observer.transform.position = aiEnemy.transform.position + Vector3.forward * .9f;
        Physics.SyncTransforms();
        float health = ai.Player.CurrentHealth;
        ai.TickAI();
        Check(
            ai.IsAttacking || ai.State == EnemyState.Attack,
            "10 Existing ordinary Attack still starts with clear sight");
        float until = Time.time + .5f;
        while (Time.time < until)
        {
            yield return null;
        }

        Check(
            ai.Player.CurrentHealth < health,
            "10 Existing attack damage still lands on a Copper-protected visible Player");
        Check(
            aiEnemy.OnBrassAffected.GetPersistentEventCount() == 1
            && aiEnemy.OnBrassAffected.GetPersistentTarget(0) == ai
            && aiEnemy.OnBrassAffected.GetPersistentMethodName(0) == nameof(TestEnemy.OnBrass),
            "Brass Inspector event retains its serialized prefab target and public method");
        Check(
            aiEnemy.OnZincAffected.GetPersistentEventCount() == 1
            && aiEnemy.OnZincAffected.GetPersistentTarget(0) == ai
            && aiEnemy.OnZincAffected.GetPersistentMethodName(0) == nameof(TestEnemy.OnZinc),
            "Zinc Inspector event retains its serialized prefab target and public method");
        aiEnemy.OnZincAffected.SetPersistentListenerState(0, UnityEngine.Events.UnityEventCallState.Off);
        Check(
            !aiEnemy.TryExecuteAction(EnemyActionType.Zinc),
            "Turning off the Inspector event leaves no hidden TestEnemy code registration");
        aiEnemy.OnZincAffected.SetPersistentListenerState(0, UnityEngine.Events.UnityEventCallState.RuntimeOnly);
        Check(
            aiEnemy.TryExecuteAction(EnemyActionType.Brass) && ai.IsBrassActive,
            "11 Brass action API invokes the Inspector reaction");
        Check(
            aiEnemy.TryExecuteAction(EnemyActionType.Zinc) && ai.IsZincActive,
            "11 Zinc action API invokes the Inspector reaction through Copper");
        int registeredCalls = 0;
        Action extraReaction = () => registeredCalls++;
        aiEnemy.RegisterAction(EnemyActionType.Zinc, extraReaction);
        Check(
            aiEnemy.TryExecuteAction(EnemyActionType.Zinc) && registeredCalls == 1 && ai.IsZincActive,
            "Programmatic registrations still work alongside Inspector reactions");
        aiEnemy.UnregisterAction(EnemyActionType.Zinc, extraReaction);
        Check(
            aiEnemy.TryExecuteAction(EnemyActionType.Zinc) && registeredCalls == 1,
            "Unregistering a code reaction preserves the Inspector reaction");
        foreach (var metalType in new[]
        {
            MetalType.Iron,
            MetalType.Steel,
            MetalType.Tin,
            MetalType.Pewter,
            MetalType.Zinc,
            MetalType.Brass
        }

        )
        {
            Enable(observer, metalType);
            Check(observer.CanUse(metalType), "11 Original inventory status remains usable: " + metalType);
        }

        var pewter = observer.gameObject.AddComponent<PlayerPewterAbility>();
        Check(
            pewter.IsActive && pewter.CurrentStrengthMultiplier > 1,
            "11 Pewter still reads the same inventory and applies its strength modifier");
        Check(!EditorUtility.scriptCompilationFailed, "Unity script compilation reports no errors");
    }
}
