using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 畫面上方的 Boss 血條。直接讀取 Enemy 的生命值，因此玩家的普攻與推回的金屬造成的傷害都會反映出來：
/// 紅色為現有血量，後方的黃色殘影延遲後緩慢追上，用來顯示剛受到的傷害。
/// UI 由程式在執行時建立，不需要場景設定。
/// </summary>
[RequireComponent(typeof(Enemy))]
public class BossHealthBar : MonoBehaviour
{
    public string BossName = "STEEL INQUISITOR";
    [Min(0)] public float TrailDelay = .5f;
    [Min(.01f)] public float TrailSpeed = .6f;

    private Enemy enemy;
    private GameObject canvasObject;
    private RectTransform fill, trail;
    private Image fillImage;
    private Text valueText;
    private float shownHealth, trailHealth, lastHealth, trailTimer, flash, hideTimer;
    private static readonly Color FillColor = new Color(.8f, .1f, .1f);

    private void Awake()
    {
        enemy = GetComponent<Enemy>();
    }

    private void Start()
    {
        Build();
        lastHealth = shownHealth = trailHealth = Ratio();
    }

    private float Ratio() => enemy.MaxHealth > 0 ? Mathf.Clamp01(enemy.CurrentHealth / enemy.MaxHealth) : 0;

    private static RectTransform Bar(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = color;
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return rt;
    }

    private static Text Label(string name, Transform parent, int size, TextAnchor anchor)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var text = go.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = size;
        text.alignment = anchor;
        text.color = Color.white;
        text.raycastTarget = false;
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return text;
    }

    private void Build()
    {
        canvasObject = new GameObject("Boss Health Bar", typeof(Canvas), typeof(CanvasScaler));
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 20;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = .5f;

        var panel = new GameObject("Panel", typeof(RectTransform)).GetComponent<RectTransform>();
        panel.SetParent(canvasObject.transform, false);
        panel.anchorMin = panel.anchorMax = new Vector2(.5f, 1);
        panel.pivot = new Vector2(.5f, 1);
        panel.anchoredPosition = new Vector2(0, -34);
        panel.sizeDelta = new Vector2(820, 78);

        var nameText = Label("Name", panel, 26, TextAnchor.UpperCenter);
        nameText.text = BossName;
        nameText.fontStyle = FontStyle.Bold;

        var bar = new GameObject("Bar", typeof(RectTransform)).GetComponent<RectTransform>();
        bar.SetParent(panel, false);
        bar.anchorMin = bar.anchorMax = new Vector2(.5f, 0);
        bar.pivot = new Vector2(.5f, 0);
        bar.anchoredPosition = Vector2.zero;
        bar.sizeDelta = new Vector2(820, 30);

        Bar("Border", bar, new Color(.85f, .85f, .85f, .9f));
        var back = Bar("Back", bar, new Color(.08f, .04f, .04f, .95f));
        back.offsetMin = new Vector2(3, 3);
        back.offsetMax = new Vector2(-3, -3);
        trail = Bar("Trail", back, new Color(1f, .78f, .2f, .95f));
        fill = Bar("Fill", back, FillColor);
        fillImage = fill.GetComponent<Image>();
        // 二階段門檻標記（血量一半）。
        var boss = GetComponent<SteelInquisitorBoss>();
        float marker = boss ? boss.Phase2Threshold : .5f;
        var tick = Bar("Phase Tick", back, new Color(1, 1, 1, .85f));
        tick.anchorMin = new Vector2(marker, 0);
        tick.anchorMax = new Vector2(marker, 1);
        tick.sizeDelta = new Vector2(3, 0);
        valueText = Label("Value", back, 18, TextAnchor.MiddleCenter);
    }

    private void SetWidth(RectTransform rt, float ratio)
    {
        rt.anchorMax = new Vector2(Mathf.Clamp01(ratio), 1);
    }

    private void Update()
    {
        if (!canvasObject)
        {
            return;
        }

        float target = Ratio();
        if (target < lastHealth - .0001f)
        {
            trailTimer = TrailDelay;
            flash = .12f;
        }

        lastHealth = target;
        shownHealth = Mathf.MoveTowards(shownHealth, target, 3f * Time.unscaledDeltaTime);
        if (target > trailHealth)
        {
            trailHealth = target;
        }
        else if (trailTimer > 0)
        {
            trailTimer -= Time.unscaledDeltaTime;
        }
        else
        {
            trailHealth = Mathf.MoveTowards(trailHealth, target, TrailSpeed * Time.unscaledDeltaTime);
        }

        flash = Mathf.Max(0, flash - Time.unscaledDeltaTime);
        fillImage.color = flash > 0 ? Color.white : FillColor;
        SetWidth(fill, shownHealth);
        SetWidth(trail, trailHealth);
        valueText.text = Mathf.CeilToInt(enemy.CurrentHealth) + " / " + Mathf.CeilToInt(enemy.MaxHealth);
        if (enemy.IsDead)
        {
            hideTimer += Time.unscaledDeltaTime;
            if (hideTimer > 2)
            {
                canvasObject.SetActive(false);
            }
        }
    }

    private void OnDestroy()
    {
        if (canvasObject)
        {
            Destroy(canvasObject);
        }
    }
}
