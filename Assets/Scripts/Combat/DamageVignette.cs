using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 玩家受傷時，畫面邊緣泛紅（中央透明、四周紅色的暈影），依傷害量決定強度並快速淡出。
/// 血量偏低時邊緣會保留微弱的紅暈。UI 由程式在執行時建立。
/// </summary>
[RequireComponent(typeof(PlayerHealth))]
public class DamageVignette : MonoBehaviour
{
    public Color Color = new Color(.85f, .02f, .02f, 1);
    [Tooltip("達到最大強度所需的傷害量。")]
    [Min(1)] public float FullDamage = 30;
    [Range(0, 1)] public float MinimumIntensity = .35f, MaximumIntensity = .95f;
    [Min(.1f)] public float FadeTime = .8f;
    [Tooltip("血量低於此比例時，邊緣保留微弱紅暈。")]
    [Range(0, 1)] public float LowHealthRatio = .3f;
    [Range(0, .6f)] public float LowHealthIntensity = .25f;

    private PlayerHealth health;
    private GameObject canvasObject;
    private Image image;
    private Texture2D texture;
    private float intensity;

    private void Awake()
    {
        health = GetComponent<PlayerHealth>();
    }

    private void OnEnable()
    {
        health.Damaged += OnDamaged;
    }

    private void OnDisable()
    {
        health.Damaged -= OnDamaged;
    }

    private void Start()
    {
        BuildTexture();
        canvasObject = new GameObject("Damage Vignette", typeof(Canvas));
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 15;
        var go = new GameObject("Image", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(canvasObject.transform, false);
        image = go.GetComponent<Image>();
        image.sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f));
        image.raycastTarget = false;
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        image.color = new Color(Color.r, Color.g, Color.b, 0);
    }

    /// <summary>中央完全透明、愈靠邊緣愈紅的暈影貼圖。</summary>
    private void BuildTexture()
    {
        const int size = 256;
        texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x + .5f) / size * 2 - 1, dy = (y + .5f) / size * 2 - 1;
                float d = Mathf.Sqrt(dx * dx + dy * dy) / 1.4142f;
                float a = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.35f, 1f, d));
                pixels[y * size + x] = new Color(1, 1, 1, a);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();
    }

    private void OnDamaged(float damage)
    {
        float hit = Mathf.Lerp(MinimumIntensity, MaximumIntensity, Mathf.Clamp01(damage / FullDamage));
        intensity = Mathf.Max(intensity, hit);
    }

    private void Update()
    {
        if (!image)
        {
            return;
        }

        intensity = Mathf.MoveTowards(intensity, 0, Time.unscaledDeltaTime / FadeTime);
        float ratio = health.MaxHealth > 0 ? health.CurrentHealth / health.MaxHealth : 1;
        float low = health.IsDead || ratio >= LowHealthRatio ? 0 : LowHealthIntensity * (1 - ratio / LowHealthRatio);
        float alpha = Mathf.Max(intensity, low);
        image.color = new Color(Color.r, Color.g, Color.b, alpha);
    }

    private void OnDestroy()
    {
        if (canvasObject)
        {
            Destroy(canvasObject);
        }

        if (texture)
        {
            Destroy(texture);
        }
    }
}
