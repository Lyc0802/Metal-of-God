using UnityEngine;

/// <summary>
/// 撞擊點的小粉塵特效。粒子系統與貼圖在第一次使用時由程式建立，不需要任何資產或場景設定。
/// </summary>
public static class DustPuff
{
    private static GameObject template;
    private static Material material;
    private static Texture2D softCircle;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        template = null;
        material = null;
        softCircle = null;
    }

    private static Texture2D SoftCircle()
    {
        if (softCircle)
        {
            return softCircle;
        }

        const int size = 32;
        softCircle = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x + .5f) / size * 2 - 1, dy = (y + .5f) / size * 2 - 1;
                float a = Mathf.Clamp01(1 - Mathf.Sqrt(dx * dx + dy * dy));
                pixels[y * size + x] = new Color(1, 1, 1, a * a);
            }
        }

        softCircle.SetPixels(pixels);
        softCircle.Apply();
        return softCircle;
    }

    private static GameObject Template()
    {
        if (template)
        {
            return template;
        }

        var shader = Shader.Find("Sprites/Default");
        if (!shader)
        {
            shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        }

        material = new Material(shader) { mainTexture = SoftCircle() };
        template = new GameObject("Dust Puff Template");
        template.SetActive(false);
        Object.DontDestroyOnLoad(template);
        var ps = template.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = 1;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(.45f, .9f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(.8f, 2.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(.18f, .4f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
        main.startColor = new Color(.78f, .74f, .68f, .6f);
        main.gravityModifier = -.03f;
        main.maxParticles = 64;

        var emission = ps.emission;
        emission.enabled = false;
        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 40;
        shape.radius = .12f;

        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(new Keyframe(0, .5f), new Keyframe(1, 1.6f)));

        var color = ps.colorOverLifetime;
        color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
            new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .12f), new GradientAlphaKey(0, 1) });
        color.color = gradient;

        var renderer = template.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return template;
    }

    /// <summary>在撞擊點噴出小粉塵，沿撞擊面的法線方向擴散。speed 越大粉塵越多。</summary>
    public static void Play(Vector3 point, Vector3 normal, float speed)
    {
        var puff = Object.Instantiate(Template());
        puff.name = "Dust Puff";
        Vector3 n = normal.sqrMagnitude > .01f ? normal.normalized : Vector3.up;
        puff.transform.SetPositionAndRotation(point + n * .05f, Quaternion.LookRotation(n));
        puff.SetActive(true);
        var ps = puff.GetComponent<ParticleSystem>();
        ps.Play();
        ps.Emit(Mathf.Clamp(Mathf.RoundToInt(5 + speed * .5f), 6, 18));
        Object.Destroy(puff, 1.5f);
    }
}
