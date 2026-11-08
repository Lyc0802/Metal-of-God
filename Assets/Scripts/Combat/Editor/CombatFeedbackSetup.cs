using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class CombatFeedbackSetup
{
    public const string Root = "Assets/CombatFeedback";
    static CombatFeedbackSetup() { EditorApplication.update += Poll; }
    private static void Poll()
    {
        if (!EditorApplication.isCompiling && !EditorApplication.isPlayingOrWillChangePlaymode && File.Exists("Temp/CombatFeedback/preview.request"))
        {
            File.Delete("Temp/CombatFeedback/preview.request");
            try { RenderPreviews(); } catch (Exception e) { Debug.LogException(e); }
        }
        const string request = "Temp/CombatFeedback/setup.request";
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(request)) return;
        File.Delete(request);
        try { Build(); File.WriteAllText("Temp/CombatFeedback/setup.txt", "PASS Integrated prefabs, audio, scene; compilationFailed=" + EditorUtility.scriptCompilationFailed); }
        catch (Exception e) { File.WriteAllText("Temp/CombatFeedback/setup.txt", "FAIL " + e); Debug.LogException(e); }
    }

    [MenuItem("Tools/Combat Feedback/Build and Integrate")]
    public static void Build()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
        Directory.CreateDirectory(Root + "/Materials");
        Directory.CreateDirectory(Root + "/Prefabs");
        AssetDatabase.Refresh();
        var spark = MakeMaterial("Spark", false, false);
        var ring = MakeMaterial("Ring", true, false);
        var slash = MakeMaterial("Slash", false, true);
        string dustPath = Root + "/Materials/Dust.mat";
        var dust = AssetDatabase.LoadAssetAtPath<Material>(dustPath);
        if (!dust) { dust = new Material(spark); AssetDatabase.CreateAsset(dust, dustPath); }
        dust.SetFloat("_Blend", 0);
        dust.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        EditorUtility.SetDirty(dust);

        var metal = new GameObject("MetalImpactVFX");
        Burst(metal, "Hot sparks", spark, new Color(3, 1.15f, .06f), 32, .12f, .42f, 5, 10, .035f, .085f, true);
        var debris = Burst(metal, "Grey debris", dust, new Color(.38f, .38f, .38f, .65f), 8, .2f, .5f, .6f, 2, .08f, .18f);
        var dm = debris.main; dm.gravityModifier = .6f;
        Ring(metal, "Impact shockwave", ring, new Color(2, .7f, .08f, .6f), .32f, 1.8f);
        Save(metal);

        var sword = new GameObject("SwordHitVFX");
        var flash = Burst(sword, "White slash", slash, new Color(4, 4, 4), 2, .18f, .23f, 0, 0, 1.1f, 1.4f);
        var fm = flash.main; fm.startRotation = new ParticleSystem.MinMaxCurve(-.6f, .6f);
        Burst(sword, "Crimson rays", spark, new Color(2.6f, .015f, .025f), 21, .12f, .3f, 3, 7, .035f, .07f, true);
        var trails = Burst(sword, "Short cutting trails", slash, new Color(2, .28f, .3f), 4, .14f, .32f, .3f, 1, .35f, .7f);
        var tm = trails.main; tm.startRotation = new ParticleSystem.MinMaxCurve(-1, 1);
        Save(sword);

        var emotion = new GameObject("EmotionalInfluenceVFX");
        Ring(emotion, "Expanding energy ring", ring, Color.white, .68f, 3.2f);
        var motes = Burst(emotion, "Rising motes", spark, Color.white, 18, .4f, .7f, .2f, .8f, .04f, .09f);
        var shape = motes.shape; shape.shapeType = ParticleSystemShapeType.Circle; shape.radius = .65f;
        var velocity = motes.velocityOverLifetime; velocity.enabled = true; velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = 0; velocity.y = .9f; velocity.z = 0;
        Save(emotion);

        var manager = Object.FindFirstObjectByType<CombatVFXManager>();
        if (!manager) manager = new GameObject("CombatVFXManager").AddComponent<CombatVFXManager>();
        Assign(manager.MetalImpact, "MetalImpactVFX", "MetalImpact");
        Assign(manager.SwordHit, "SwordHitVFX", "SwordHit");
        Assign(manager.EmotionalInfluence, "EmotionalInfluenceVFX", "EmotionalInfluence");
        manager.EmotionalInfluence.Volume = .65f;
        foreach (var metalObject in Object.FindObjectsByType<MetalDisplayer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var body = metalObject.GetComponentInParent<Rigidbody>();
            if (body && !body.isKinematic && !body.GetComponent<MetalImpactFeedback>()) body.gameObject.AddComponent<MetalImpactFeedback>();
        }
        EditorUtility.SetDirty(manager);
        EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
        EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();
    }

    [MenuItem("Tools/Combat Feedback/Render Previews")]
    public static void RenderPreviews()
    {
        Directory.CreateDirectory("Temp/CombatFeedback/Previews");
        var manager = Object.FindFirstObjectByType<CombatVFXManager>();
        for (int i = 0; i < 4; i++)
        {
            var utility = new PreviewRenderUtility();
            try
            {
                string name = i == 0 ? "MetalImpact" : i == 1 ? "SwordHit" : i == 2 ? "Zinc" : "Brass";
                var settings = i == 0 ? manager.MetalImpact : i == 1 ? manager.SwordHit : manager.EmotionalInfluence;
                var go = Object.Instantiate(settings.Prefab);
                go.hideFlags = HideFlags.HideAndDontSave;
                utility.AddSingleGO(go);
                if (i >= 2) go.transform.rotation = Quaternion.FromToRotation(Vector3.forward, Vector3.up);
                foreach (var ps in go.GetComponentsInChildren<ParticleSystem>())
                {
                    if (i >= 2) { var main = ps.main; main.startColor = i == 2 ? manager.ZincColor : manager.BrassColor; }
                    ps.useAutoRandomSeed = false; ps.randomSeed = 711;
                    ps.Simulate(i < 2 ? .12f : .28f, false, true, false);
                }
                utility.camera.transform.position = new Vector3(0, 2, -5);
                utility.camera.transform.LookAt(Vector3.zero);
                utility.camera.orthographic = true; utility.camera.orthographicSize = 1.7f;
                utility.camera.nearClipPlane = .1f; utility.camera.farClipPlane = 20;
                utility.camera.clearFlags = CameraClearFlags.SolidColor;
                utility.camera.backgroundColor = new Color(.025f, .035f, .055f, 1);
                utility.BeginStaticPreview(new Rect(0, 0, 480, 400));
                utility.Render(true, false);
                var result = utility.EndStaticPreview();
                File.WriteAllBytes("Temp/CombatFeedback/Previews/" + name + ".png", result.EncodeToPNG());
                Object.DestroyImmediate(result);
            }
            finally { utility.Cleanup(); }
        }
    }
    private static void Assign(CombatEffectSettings settings, string prefab, string audio)
    {
        settings.Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/" + prefab + ".prefab");
        settings.Clip = AssetDatabase.LoadAssetAtPath<AudioClip>(Root + "/Audio/" + audio + ".wav");
        if (!settings.Clip) throw new InvalidOperationException("Missing clip: " + audio);
    }
    private static Material MakeMaterial(string name, bool ring, bool slash)
    {
        var texture = new Texture2D(128, 128, TextureFormat.RGBA32, false);
        for (int y = 0; y < 128; y++) for (int x = 0; x < 128; x++)
        {
            float u = (x + .5f - 64) / 64, v = (y + .5f - 64) / 64;
            float radius = Mathf.Sqrt(u*u + v*v);
            float alpha = ring ? Mathf.Exp(-Mathf.Pow((radius - .78f) / .045f, 2))
                : slash ? Mathf.Exp(-Mathf.Pow((v - .3f*u*u) / .055f, 2)) * Mathf.Clamp01((1-Mathf.Abs(u))*5)
                : Mathf.Pow(Mathf.Clamp01(1-radius), 2);
            texture.SetPixel(x, y, new Color(1, 1, 1, alpha));
        }
        texture.Apply();
        string texturePath = Root + "/Materials/" + name + ".png";
        File.WriteAllBytes(texturePath, texture.EncodeToPNG()); Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(texturePath);
        var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
        importer.alphaIsTransparency = true; importer.wrapMode = TextureWrapMode.Clamp; importer.mipmapEnabled = false; importer.SaveAndReimport();
        var source = AssetDatabase.LoadAssetAtPath<Material>("Assets/PlayerFeedback/SwordSpark.mat");
        if (!source || !source.shader.isSupported) throw new InvalidOperationException("URP particle material missing or unsupported.");
        string path = Root + "/Materials/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material) { material = new Material(source); AssetDatabase.CreateAsset(material, path); }
        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
        material.SetColor("_BaseColor", Color.white);
        EditorUtility.SetDirty(material);
        return material;
    }
    private static ParticleSystem Burst(GameObject root, string name, Material material, Color color, int count,
        float lifeMin, float lifeMax, float speedMin, float speedMax, float sizeMin, float sizeMax, bool stretched = false)
    {
        var go = new GameObject(name); go.transform.SetParent(root.transform, false);
        var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main; main.loop = false; main.playOnAwake = false; main.duration = .05f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
        main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax); main.startColor = color;
        main.maxParticles = 128; main.simulationSpace = ParticleSystemSimulationSpace.World; main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        var emission = ps.emission; emission.rateOverTime = 0; emission.SetBursts(new[] { new ParticleSystem.Burst(0, (short)count) });
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = .04f;
        var fade = ps.colorOverLifetime; fade.enabled = true;
        var gradient = new Gradient(); gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},
            new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(.8f,.25f),new GradientAlphaKey(0,1)}); fade.color = gradient;
        var renderer = ps.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = material;
        renderer.renderMode = stretched ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
        renderer.lengthScale = 2.5f; renderer.velocityScale = .04f; renderer.maxParticleSize = 2;
        renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        return ps;
    }
    private static void Ring(GameObject root, string name, Material material, Color color, float lifetime, float diameter)
    {
        var ps = Burst(root, name, material, color, 1, lifetime, lifetime, 0, 0, diameter, diameter);
        var shape = ps.shape; shape.enabled = false;
        var size = ps.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.EaseInOut(0,.05f,1,1));
        ps.GetComponent<ParticleSystemRenderer>().alignment = ParticleSystemRenderSpace.Local;
    }
    private static void Save(GameObject root)
    {
        PrefabUtility.SaveAsPrefabAsset(root, Root + "/Prefabs/" + root.name + ".prefab");
        Object.DestroyImmediate(root);
    }
}
