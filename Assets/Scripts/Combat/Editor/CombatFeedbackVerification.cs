using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

// Runs real PlayerCombat swings, manipulator input and PhysX contacts in the
// integrated test scene. All fixtures and runtime changes vanish on exiting Play.
[InitializeOnLoad]
public static class CombatFeedbackVerification
{
    private const string Folder = "Temp/CombatFeedback";
    private const string Key = "CombatFeedbackVerification.Run";
    private static IEnumerator routine;
    private static int lastFrame = -1;
    private static readonly List<string> report = new List<string>();
    private static readonly List<GameObject> spawnedEffects = new List<GameObject>();
    private static GameObject lastEffect;
    private static bool background;
    static CombatFeedbackVerification() { EditorApplication.update += Tick; }

    [MenuItem("Tools/Combat Feedback/Run Interaction Checks")]
    public static void Request()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Folder + "/test.request", "run");
    }
    private static void Tick()
    {
        if (EditorApplication.isCompiling) return;
        if (routine != null)
        {
            if (!Application.isPlaying || EditorApplication.isPaused || Time.frameCount == lastFrame) return;
            lastFrame = Time.frameCount;
            try { if (!routine.MoveNext()) Finish("COMPLETE"); }
            catch (Exception e) { Finish("FAIL " + e); }
            return;
        }
        if (SessionState.GetBool(Key, false) && Application.isPlaying)
        {
            SessionState.SetBool(Key, false);
            report.Clear(); spawnedEffects.Clear();
            background = Application.runInBackground; Application.runInBackground = true;
            routine = Run(); return;
        }
        if (!EditorApplication.isPlayingOrWillChangePlaymode && File.Exists(Folder + "/test.request"))
        {
            File.Delete(Folder + "/test.request");
            SessionState.SetBool(Key, true);
            EditorApplication.isPlaying = true;
        }
    }
    private static void Finish(string result)
    {
        routine = null;
        report.Add(result); File.WriteAllLines(Folder + "/results.txt", report);
        Application.runInBackground = background;
        EditorApplication.isPlaying = false;
    }
    private static void Check(bool value, string description)
    {
        if (!value) throw new InvalidOperationException(description);
        report.Add("PASS " + description);
        File.WriteAllLines(Folder + "/results.txt", report);
    }
    private static GameObject Actor(string name, Vector3 point, PrimitiveType type)
    {
        var go = GameObject.CreatePrimitive(type); go.name = name; go.transform.position = point; return go;
    }
    private static bool HasParticles(GameObject effect) => effect && effect.GetComponentsInChildren<ParticleSystem>().Any(p => p.particleCount > 0);
    private static void CheckPlayback(string label)
    {
        Check(HasParticles(lastEffect), label + " real particles emitted");
        var source = lastEffect.GetComponent<AudioSource>();
        Check(source && source.isPlaying && source.spatialBlend == 1 && source.transform.position == lastEffect.transform.position,
            label + " synchronized 3D PlayOneShot audio at effect position");
    }
    private static IEnumerator Run()
    {
        yield return null; yield return null;
        var manager = Object.FindFirstObjectByType<CombatVFXManager>();
        Check(manager && manager.MetalImpact.Prefab && manager.SwordHit.Prefab && manager.EmotionalInfluence.Prefab,
            "scene manager has all three prefab references");
        Check(manager.MetalImpact.Clip && manager.SwordHit.Clip && manager.EmotionalInfluence.Clip
            && manager.MetalImpact.Clip != manager.SwordHit.Clip && manager.SwordHit.Clip != manager.EmotionalInfluence.Clip,
            "three distinct nonempty AudioClips assigned");
        manager.EffectPlayed += (kind, effect) => { lastEffect = effect; spawnedEffects.Add(effect); };
        foreach (var controller in Object.FindObjectsByType<PlayerController>(FindObjectsSortMode.None)) controller.enabled = false;
        var realAI = Object.FindFirstObjectByType<TestEnemy>();
        Check(realAI, "existing TestEnemy available for registered Inspector actions");
        foreach (var ai in Object.FindObjectsByType<TestEnemy>(FindObjectsSortMode.None))
        {
            var agent = ai.GetComponent<NavMeshAgent>(); if (agent) agent.enabled = false;
        }
        var origin = new Vector3(100, 2, 100);
        var player = Actor("Feedback Test Player", origin, PrimitiveType.Capsule);
        var body = player.AddComponent<Rigidbody>(); body.useGravity = false; body.constraints = RigidbodyConstraints.FreezeAll;
        var inventory = player.AddComponent<PlayerMetalInventory>();
        var selector = player.AddComponent<PlayerMetalSelector>(); selector.SelectionDistance = 10000;
        var health = player.AddComponent<PlayerHealth>();
        var manipulator = player.AddComponent<PlayerMetalManipulator>();
        var emotions = player.AddComponent<PlayerEnemyManipulator>();
        var swordPivot = new GameObject("Sword test pivot").transform; swordPivot.SetParent(player.transform, false);
        var combat = player.AddComponent<PlayerCombat>(); combat.SwordPivot = swordPivot;
        combat.SwingDuration = .3f; combat.AttackCooldown = 0; combat.AttackDamage = 10; combat.KnockbackSpeed = 0;
        var enemyObject = Actor("Feedback Target", origin + Vector3.forward * 1.8f, PrimitiveType.Capsule);
        var enemy = enemyObject.AddComponent<Enemy>(); enemy.MaxHealth = enemy.CurrentHealth = 1000;
        Physics.SyncTransforms();

        int count = manager.SwordPlayCount;
        Check(combat.TryAttack(), "existing sword attack started");
        for (float t = Time.time + .5f; Time.time < t;) yield return null;
        Check(manager.SwordPlayCount == count + 1 && enemy.CurrentHealth == 990,
            "successful swing damages enemy and emits exactly once");
        // A second collider must still deduplicate by the Enemy, not the collider.
        enemyObject.AddComponent<BoxCollider>();
        count = manager.SwordPlayCount;
        Check(combat.TryAttack(), "second sword attack started");
        for (float t = Time.time + .14f; Time.time < t;) yield return null;
        CheckPlayback("Sword hit");
        for (float t = Time.time + .4f; Time.time < t;) yield return null;
        Check(manager.SwordPlayCount == count + 1 && enemy.CurrentHealth == 980, "multi-collider enemy emits once per swing");
        player.transform.rotation = Quaternion.Euler(0,180,0); Physics.SyncTransforms();
        count = manager.SwordPlayCount; combat.TryAttack();
        for (float t = Time.time + .5f; Time.time < t;) yield return null;
        Check(manager.SwordPlayCount == count, "swing miss emits no hit particles or sound");
        player.transform.rotation = Quaternion.identity; combat.AttackDamage = 0; combat.TryAttack();
        for (float t = Time.time + .5f; Time.time < t;) yield return null;
        Check(manager.SwordPlayCount == count, "zero damage emits no sword feedback");

        enemyObject.transform.position = origin + Vector3.forward * 4;
        var metalObject = Actor("Driven Metal", origin + Vector3.forward * 2, PrimitiveType.Cube);
        metalObject.transform.localScale = Vector3.one * .5f;
        var metalBody = metalObject.AddComponent<Rigidbody>(); metalBody.useGravity = false;
        metalBody.constraints = RigidbodyConstraints.FreezeRotation;
        metalBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        var metal = metalObject.AddComponent<MetalDisplayer>(); metal.weight = 1;
        var feedback = metalObject.AddComponent<MetalImpactFeedback>(); feedback.MinimumImpactSpeed = 2.5f;
        var surface = new MetalSurface { Metal = metal, Collider = metalObject.GetComponent<Collider>(), LocalNormal = Vector3.back };
        Physics.SyncTransforms();
        count = manager.MetalPlayCount;
        // Ordinary motion, including motion of a real metal, is insufficient.
        metalBody.linearVelocity = Vector3.forward * 10;
        for (float t = Time.time + .35f; Time.time < t;) yield return null;
        Check(manager.MetalPlayCount == count, "unmanipulated metal collision emits nothing");
        metalBody.position = origin + Vector3.forward * 2; metalBody.linearVelocity = Vector3.zero; Physics.SyncTransforms();
        yield return null; yield return null;
        manipulator.PushSpeed = 10;
        manipulator.HandleInput(true, 1, surface);
        for (float t = Time.time + .06f; Time.time < t;) yield return null;
        Check(feedback.IsPlayerDriven, "Steel actually drives and arms only the selected metal body");
        Check(manager.MetalPlayCount == count, "metal has not yet reached enemy when Steel is released");
        manipulator.Release();
        for (float t = Time.time + .5f; Time.time < t && manager.MetalPlayCount == count;) yield return null;
        yield return null;
        Check(manager.MetalPlayCount == count + 1, "released Steel inertia hits enemy via a real PhysX contact");
        CheckPlayback("Metal impact");
        Check(Vector3.Distance(lastEffect.transform.position, enemy.TargetPoint) < 1.5f, "metal feedback uses contact position");
        for (float t = Time.time + .65f; Time.time < t;) yield return null;
        Check(manager.MetalPlayCount == count + 1, "continued contact does not repeat effects");
        // Iron pulls the same body back through an enemy between it and the player.
        metalBody.position = origin + Vector3.forward * 6; metalBody.linearVelocity = Vector3.zero; Physics.SyncTransforms();
        yield return null; yield return null;
        feedback.PerEnemyCooldown = 1;
        manipulator.HandleInput(true, -1, surface);
        for (float t = Time.time + .18f; Time.time < t;) yield return null;
        manipulator.Release();
        Check(manager.MetalPlayCount == count + 2, "Iron pull collision emits metal feedback");
        metalBody.position = origin + Vector3.forward * 6; metalBody.linearVelocity = Vector3.back * 20;
        Physics.SyncTransforms();
        for (float t = Time.time + .15f; Time.time < t;) yield return null;
        Check(manager.MetalPlayCount == count + 2, "rapid repeated contact with same enemy is suppressed by cooldown");
        // Let the cooldown expire so the next assertion isolates the speed gate.
        for (float t = Time.time + 1.05f; Time.time < t;) yield return null;
        metalBody.position = origin + Vector3.forward * 3.12f; metalBody.linearVelocity = Vector3.zero;
        Physics.SyncTransforms(); yield return null; yield return null;
        manipulator.PushSpeed = 1; manipulator.HandleInput(true, 1, surface);
        for (float t = Time.time + .5f; Time.time < t;) yield return null;
        manipulator.Release();
        Check(manager.MetalPlayCount == count + 2, "sub-threshold player-driven collision emits nothing");
        Check(enemy.CurrentHealth == 980, "metal feedback adds no new damage system");

        realAI.Player = health;
        var emotionalEnemy = realAI.GetComponent<Enemy>();
        count = manager.EmotionalPlayCount;
        emotions.Release();
        Check(emotions.HandleInput(true, 1, emotionalEnemy, emotionalEnemy.TargetPoint), "Zinc registered Action succeeds");
        yield return null;
        Check(manager.EmotionalPlayCount == count + 1 && realAI.IsZincActive, "successful Zinc emits feedback and AI effect");
        CheckPlayback("Zinc");
        var zincColor = lastEffect.GetComponentInChildren<ParticleSystem>().main.startColor.color;
        float zincPitch = lastEffect.GetComponent<AudioSource>().pitch;
        Check(zincColor.r > zincColor.b && zincPitch > 1, "Zinc uses red/orange and higher pitch");
        emotions.Release(); yield return null;
        Check(realAI.IsZincActive && realAI.EffectTimeRemaining > 1, "releasing right mouse preserves Zinc AI duration");
        Check(emotions.HandleInput(true, -1, emotionalEnemy, emotionalEnemy.TargetPoint), "Brass registered Action succeeds");
        yield return null; CheckPlayback("Brass");
        var brassColor = lastEffect.GetComponentInChildren<ParticleSystem>().main.startColor.color;
        Check(brassColor.b > brassColor.r && lastEffect.GetComponent<AudioSource>().pitch < zincPitch && realAI.IsBrassActive,
            "same emotional prefab uses blue/cyan, slower speed, lower pitch for Brass");
        emotions.Release(); Check(realAI.IsBrassActive, "right mouse release preserves Brass AI duration");
        count = manager.EmotionalPlayCount;
        inventory.Get(MetalType.Zinc).IsEnabled = false;
        Check(!emotions.HandleInput(true, 1, emotionalEnemy, emotionalEnemy.TargetPoint), "disabled Zinc rejected");
        inventory.Get(MetalType.Zinc).IsEnabled = true; inventory.Get(MetalType.Zinc).CurrentAmount = 0;
        Check(!emotions.HandleInput(true, 1, emotionalEnemy, emotionalEnemy.TargetPoint), "empty Zinc rejected");
        inventory.Get(MetalType.Zinc).CurrentAmount = 100;
        inventory.Get(MetalType.Brass).IsEnabled = false;
        Check(!emotions.HandleInput(true, -1, emotionalEnemy, emotionalEnemy.TargetPoint), "disabled Brass rejected");
        inventory.Get(MetalType.Brass).IsEnabled = true; inventory.Get(MetalType.Brass).CurrentAmount = 0;
        Check(!emotions.HandleInput(true, -1, emotionalEnemy, emotionalEnemy.TargetPoint), "empty Brass rejected");
        inventory.Get(MetalType.Brass).CurrentAmount = 100;
        Check(!emotions.HandleInput(true, 1, enemy, enemy.TargetPoint), "enemy with no Action rejects influence");
        realAI.ZincDuration = 0;
        Check(!emotions.HandleInput(true, 1, emotionalEnemy, emotionalEnemy.TargetPoint), "registered but ineffective AI Action reports failure");
        realAI.enabled = false;
        Check(!emotions.HandleInput(true, -1, emotionalEnemy, emotionalEnemy.TargetPoint), "disabled AI listener is not a successful Action");
        Check(manager.EmotionalPlayCount == count, "all failure cases emit neither particles nor audio");
        for (float t = Time.time + 2; Time.time < t;) yield return null;
        Check(spawnedEffects.All(e => !e), "all transient VFX/audio instances automatically destroyed");
        Check(!EditorUtility.scriptCompilationFailed, "Unity compilation succeeds");
    }
}
