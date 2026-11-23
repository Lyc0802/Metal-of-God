using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 瞄準金屬時，從玩家到範圍內每個金屬畫出淺藍色細線（被選取的那條較亮較粗）。
/// 推 / 拉發出時，一道訊號極快地沿著線射向目標，線在訊號經過處變色；
/// 實際施力由 PlayerMetalManipulator 延遲到訊號抵達目標時才開始。
/// </summary>
[RequireComponent(typeof(PlayerController), typeof(PlayerMetalManipulator), typeof(PlayerMetalSelector))]
public class MetalAimLines : MonoBehaviour
{
    [Header("Aim Lines")]
    public Color LineColor = new Color(.55f, .82f, 1f, .4f);
    public Color SelectedColor = new Color(.8f, .95f, 1f, .95f);
    [Min(.001f)] public float Width = .02f, SelectedWidth = .045f;

    [Header("Fire Pulse")]
    public Color PushColor = new Color(1f, .7f, .3f, 1f);
    public Color PullColor = new Color(.45f, 1f, .95f, 1f);
    [Min(.001f)] public float PulseWidth = .06f;
    [Min(0)] public float PulseFade = .25f;
    [Tooltip("撞擊特效的強度（傳給戰鬥金屬撞擊特效的速度參數）。")]
    [Min(0)] public float ImpactSpeed = 8;
    [Min(.1f)] public float RingRadius = 1.4f;
    [Min(.05f)] public float RingTime = .3f;

    private PlayerController controller;
    private PlayerMetalManipulator manipulator;
    private PlayerMetalSelector selector;
    private PlayerMetalInventory inventory;
    private Collider ownCollider;
    private IronSteelSight sight;
    private Material material;
    private readonly List<LineRenderer> pool = new List<LineRenderer>();

    private void Awake()
    {
        controller = GetComponent<PlayerController>();
        manipulator = GetComponent<PlayerMetalManipulator>();
        selector = GetComponent<PlayerMetalSelector>();
        inventory = GetComponent<PlayerMetalInventory>();
        ownCollider = GetComponent<Collider>();
        var shader = Shader.Find("Sprites/Default");
        if (!shader)
        {
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        }

        material = new Material(shader);
    }

    private void OnEnable()
    {
        manipulator.Fired += OnFired;
    }

    private void OnDisable()
    {
        manipulator.Fired -= OnFired;
        foreach (var line in pool)
        {
            if (line)
            {
                line.enabled = false;
            }
        }
    }

    private void OnDestroy()
    {
        foreach (var line in pool)
        {
            if (line)
            {
                Destroy(line.gameObject);
            }
        }

        if (material)
        {
            Destroy(material);
        }
    }

    private Vector3 Origin => ownCollider ? ownCollider.bounds.center : transform.position + Vector3.up;

    private LineRenderer NewLine(string name)
    {
        var go = new GameObject(name);
        var line = go.AddComponent<LineRenderer>();
        line.sharedMaterial = material;
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.numCapVertices = 2;
        line.alignment = LineAlignment.View;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        return line;
    }

    private static Vector3 SurfacePoint(MetalDisplayer metal, Vector3 from)
    {
        var c = metal.GetComponent<Collider>();
        if (!c)
        {
            return metal.transform.position;
        }

        // 非凸面 MeshCollider 不支援 ClosestPoint。
        if (c is MeshCollider mesh && !mesh.convex)
        {
            return c.bounds.center;
        }

        return c.ClosestPoint(from);
    }

    private void LateUpdate()
    {
        // 組員的 IronSteelSight 已經會在瞄準時畫（依重量分深 / 淺藍的）線，有它就不重複畫；
        // 這個元件只負責發射訊號與撞擊特效。
        if (!sight)
        {
            sight = GetComponent<IronSteelSight>();
        }

        bool aiming = !sight && controller.IsSelectingMetal && (inventory.CanUse(MetalType.Iron) || inventory.CanUse(MetalType.Steel));
        int used = 0;
        if (aiming)
        {
            Vector3 origin = Origin;
            float range = Mathf.Max(0, selector.SelectionDistance);
            var current = selector.Current;
            foreach (var metal in MetalDisplayer.ActiveMetals)
            {
                if (!metal || !metal.isActiveAndEnabled || metal.transform.IsChildOf(transform))
                {
                    continue;
                }

                Vector3 point = SurfacePoint(metal, origin);
                if ((point - origin).sqrMagnitude > range * range)
                {
                    continue;
                }

                if (used >= pool.Count)
                {
                    pool.Add(NewLine("Metal Aim Line"));
                }

                bool selected = current.Valid && current.Metal == metal;
                var line = pool[used++];
                line.enabled = true;
                line.SetPosition(0, origin);
                line.SetPosition(1, selected ? current.Point : point);
                line.widthMultiplier = selected ? SelectedWidth : Width;
                Color c = selected ? SelectedColor : LineColor;
                line.startColor = c;
                line.endColor = c;
            }
        }

        for (int i = used; i < pool.Count; i++)
        {
            if (pool[i])
            {
                pool[i].enabled = false;
            }
        }
    }

    private void OnFired(MetalSurface target, bool pull, float delay)
    {
        StartCoroutine(Pulse(target, pull, delay));
    }

    /// <summary>撞擊點的擴散光環（貼在目標表面）。</summary>
    private IEnumerator Ring(Vector3 point, Vector3 normal, Color color)
    {
        var line = NewLine("Metal Impact Ring");
        line.loop = true;
        const int segments = 32;
        line.positionCount = segments;
        Vector3 n = normal.sqrMagnitude > .01f ? normal.normalized : Vector3.up;
        Vector3 u = Vector3.Cross(n, Mathf.Abs(n.y) < .9f ? Vector3.up : Vector3.right).normalized;
        Vector3 v = Vector3.Cross(n, u);
        for (float t = 0; t < RingTime; t += Time.unscaledDeltaTime)
        {
            float k = t / RingTime;
            float radius = Mathf.Lerp(.15f, RingRadius, 1 - (1 - k) * (1 - k));
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2 / segments;
                line.SetPosition(i, point + n * .03f + (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * radius);
            }

            line.widthMultiplier = Mathf.Lerp(.09f, .01f, k);
            Color c = Color.Lerp(Color.white, color, k);
            c.a = 1 - k;
            line.startColor = c;
            line.endColor = c;
            yield return null;
        }

        Destroy(line.gameObject);
    }

    /// <summary>訊號沿線飛向目標：線在訊號經過處變色（亮白頭），抵達後淡出。</summary>
    private IEnumerator Pulse(MetalSurface target, bool pull, float delay)
    {
        var line = NewLine("Metal Fire Pulse");
        line.widthMultiplier = PulseWidth;
        Color fire = pull ? PullColor : PushColor;
        Vector3 end = target.Valid ? target.Point : Origin;
        var gradient = new Gradient();
        float duration = Mathf.Max(.01f, delay);
        for (float t = 0; t < duration; t += Time.unscaledDeltaTime)
        {
            if (target.Valid)
            {
                end = target.Point;
            }

            float head = Mathf.Clamp(t / duration, .01f, .98f);
            line.SetPosition(0, Origin);
            line.SetPosition(1, end);
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(fire, 0),
                    new GradientColorKey(Color.white, head),
                    new GradientColorKey(LineColor, Mathf.Min(1, head + .015f))
                },
                new[]
                {
                    new GradientAlphaKey(1, 0),
                    new GradientAlphaKey(1, head),
                    new GradientAlphaKey(LineColor.a, Mathf.Min(1, head + .015f))
                });
            line.colorGradient = gradient;
            yield return null;
        }

        // 抵達目標：撞擊特效（沿用戰鬥的金屬撞擊特效 + 擴散光環）。
        if (target.Valid)
        {
            CombatFeedbackEvents.PublishMetalImpact(target.Point, target.Normal, ImpactSpeed);
            StartCoroutine(Ring(target.Point, target.Normal, fire));
        }

        // 整條線變成發射色，然後淡出。
        for (float t = 0; t < PulseFade; t += Time.unscaledDeltaTime)
        {
            if (target.Valid)
            {
                end = target.Point;
            }

            float alpha = 1 - t / Mathf.Max(.01f, PulseFade);
            line.SetPosition(0, Origin);
            line.SetPosition(1, end);
            Color c = new Color(fire.r, fire.g, fire.b, alpha);
            line.startColor = c;
            line.endColor = c;
            yield return null;
        }

        Destroy(line.gameObject);
    }
}
