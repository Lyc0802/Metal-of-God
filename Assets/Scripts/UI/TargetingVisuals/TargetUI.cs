using UnityEngine;
using UnityEngine.UI;

/// 
/// 瞄準時的介面（照草圖）。資料來自 TargetingBridge（組員的推拉系統），這裡只負責畫：
///   金屬：目標上的圓圈；右上方小的「上箭頭」= 鋼推，下方大的「下箭頭」= 鐵拉
///   情緒核心：上方「上箭頭」= 鋅煽動，下方「下箭頭」= 黃銅安撫
///   已解鎖（對應的金屬正在燒）→ 白色箭頭；未解鎖 → 半透明灰色箭頭
///   使用成功時箭頭放大並染成金屬的顏色，失敗（未解鎖）時左右抖動
/// 介面每幀跟著目標在螢幕上的位置移動。
/// 
public class TargetUI : MonoBehaviour
{
    TargetingBridge targeting;
    Camera view;
    RectTransform canvasRect, root, crosshair;
    RingSectorGraphic reticle, reticleGlow;
    ArrowGraphic upArrow, downArrow, upShadow, downShadow;
    Text upLabel, downLabel, info;
    float upFlash, downFlash, upShake, downShake;     // 1 → 0 的動畫計時
    RectTransform dimRoot;
    const float HoleRadius = 110f;      // 目標周圍不變暗的範圍（UI 像素）

    static readonly Color Unlocked = Color.white;
    static readonly Color Locked = new Color(0.6f, 0.6f, 0.6f, 0.35f);
    static readonly Color HeavyBlue = new Color(0.12f, 0.28f, 0.85f);
    static readonly Color LightBlue = new Color(0.55f, 0.85f, 1f);
    static readonly Color EmotionColor = new Color(1f, 0.7f, 0.15f);

    public static TargetUI Create(Canvas canvas, TargetingBridge targeting, Camera view)
    {
        var ui = new GameObject("TargetUI", typeof(RectTransform)).AddComponent<TargetUI>();
        ui.transform.SetParent(canvas.transform, false);
        ui.targeting = targeting;
        ui.view = view;
        ui.canvasRect = (RectTransform)canvas.transform;
        ui.Build();
        targeting.Used += ui.OnUsed;
        return ui;
    }

    void Build()
    {
        // 周圍變暗：中間挖一個洞（目標所在處），洞的邊緣 45 像素漸漸變暗，外面一整圈 35% 黑色
        dimRoot = new GameObject("Dim", typeof(RectTransform)).GetComponent<RectTransform>();
        dimRoot.SetParent(transform, false);
        var edge = RingSectorGraphic.Create("DimEdge", dimRoot, RingSectorGraphic.Mode.Fill,
                                            new Color(0, 0, 0, 0.35f), HoleRadius, HoleRadius + 90f, 0f, 360f);
        edge.fadeInner = true;
        RingSectorGraphic.Create("DimOuter", dimRoot, RingSectorGraphic.Mode.Fill,
                                new Color(0, 0, 0, 0.35f), HoleRadius + 90f, 2600f, 0f, 360f);
        
        var self = (RectTransform)transform;
        self.anchorMin = self.anchorMax = new Vector2(0.5f, 0.5f);
        self.sizeDelta = Vector2.zero;

        // 畫面中央的準星：小圓圈 + 中心點
        crosshair = new GameObject("Crosshair", typeof(RectTransform)).GetComponent<RectTransform>();
        crosshair.SetParent(transform, false);
        RingSectorGraphic.Create("Ring", crosshair, RingSectorGraphic.Mode.Outline, new Color(1, 1, 1, 0.7f), 0f, 12f, 0f, 360f).SetThickness(2f);
        RingSectorGraphic.Create("Dot", crosshair, RingSectorGraphic.Mode.Fill, new Color(1, 1, 1, 0.9f), 0f, 2.5f, 0f, 360f);

        // 目標上的介面：全部放在 root 底下，每幀把 root 移到目標的螢幕位置
        root = new GameObject("OnTarget", typeof(RectTransform)).GetComponent<RectTransform>();
        root.SetParent(transform, false);
        reticleGlow = RingSectorGraphic.Create("ReticleGlow", root, RingSectorGraphic.Mode.Outline, Color.white, 0f, 26f, 0f, 360f);
        reticleGlow.soft = true;
        reticleGlow.SetThickness(14f);
        reticle = RingSectorGraphic.Create("Reticle", root, RingSectorGraphic.Mode.Outline, Color.white, 0f, 26f, 0f, 360f);
        reticle.SetThickness(3f);

        // 箭頭：後面墊一個稍大的黑色箭頭當外框
        upShadow = ArrowGraphic.Create("UpShadow", root, 1f, true);
        upArrow = ArrowGraphic.Create("Up", root, 1f, true);
        downShadow = ArrowGraphic.Create("DownShadow", root, 1f, false);
        downArrow = ArrowGraphic.Create("Down", root, 1f, false);
        upShadow.color = downShadow.color = new Color(0, 0, 0, 0.5f);

        upLabel = NewText(root, 20, TextAnchor.MiddleLeft);
        downLabel = NewText(root, 20, TextAnchor.MiddleLeft);
        info = NewText(root, 18, TextAnchor.MiddleCenter);
    }

    Text NewText(Transform parent, int size, TextAnchor align)
    {
        var t = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer)).AddComponent<Text>();
        t.transform.SetParent(parent, false);
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = size;
        t.alignment = align;
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.rectTransform.sizeDelta = new Vector2(300, 30);
        t.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0.85f);
        return t;
    }

    void OnUsed(bool up, bool success)
    {
        if (up) { if (success) upFlash = 1f; else upShake = 1f; }
        else { if (success) downFlash = 1f; else downShake = 1f; }
    }

    void LateUpdate()
    {
        float dt = Time.unscaledDeltaTime;
        crosshair.gameObject.SetActive(targeting.Aiming);
        dimRoot.gameObject.SetActive(targeting.Aiming);
        bool show = targeting.Aiming && targeting.Kind != TargetKind.None;
        root.gameObject.SetActive(show);
        dimRoot.anchoredPosition = Vector2.zero;          // 沒有目標時，洞在畫面中央（準星）
        if (!show) return;

        var sp = view.WorldToScreenPoint(targeting.TargetPoint);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, sp, null, out var local);
        root.anchoredPosition = local;
        dimRoot.anchoredPosition = local;                 // 洞跟著目標移動

        bool metal = targeting.Kind == TargetKind.Metal;

        // 圓圈顏色：金屬依重量（深藍 = 比你重、淺藍 = 比你輕），情緒核心用黃橘色
        var ringColor = metal ? (targeting.TargetHeavier ? HeavyBlue : LightBlue) : EmotionColor;
        reticle.color = ringColor;
        var glow = ringColor;
        glow.a = 0.4f + 0.3f * Mathf.Sin(Time.unscaledTime * 6f);
        reticleGlow.color = glow;

        // 版面：照草圖，金屬是右上小箭頭 + 下方大箭頭；情緒是上下各一個一樣大的箭頭
        Vector2 upPos = new(0f, 82f);
        Vector2 downPos = new(0f, -82f);
        float upSize = 32f;
        float downSize = 32f;

        Layout(upArrow, upShadow, upPos, upSize, targeting.UpUnlocked, targeting.UpMetal, ref upFlash, ref upShake, dt);
        Layout(downArrow, downShadow, downPos, downSize, targeting.DownUnlocked, targeting.DownMetal, ref downFlash, ref downShake, dt);

        string upName = metal ? "PUSH  (Steel)" : "RIOT  (Zinc)";
        string downName = metal ? "PULL  (Iron)" : "SOOTHE  (Brass)";
        SetLabel(upLabel, upName + "\nwheel up", upPos + new Vector2(upSize * 0.8f + 10f, 0f), targeting.UpUnlocked);
        SetLabel(downLabel, downName + "\nwheel down", downPos + new Vector2(downSize * 0.8f + 10f, 0f), targeting.DownUnlocked);

        // 目標資訊（文字由 TargetingBridge 依組員的資料組好）
        info.text = targeting.InfoText;
        info.color = metal ? Color.white : EmotionColor;
        info.rectTransform.anchoredPosition = metal ? new Vector2(0f, -190f) : new Vector2(0f, 140f);   // 金屬：大箭頭下方；情緒：上箭頭上方
    }

    void Layout(ArrowGraphic arrow, ArrowGraphic shadow, Vector2 pos, float size, bool unlocked, Metal metal,
                ref float flash, ref float shake, float dt)
    {
        flash = Mathf.MoveTowards(flash, 0f, dt * 3f);
        shake = Mathf.MoveTowards(shake, 0f, dt * 3f);

        // 成功：放大 35% 並染成金屬色；失敗：左右抖動
        float s = size * (1f + 0.35f * flash);
        var offset = new Vector2(Mathf.Sin(Time.unscaledTime * 60f) * 8f * shake, 0f);
        var color = unlocked ? Color.Lerp(Unlocked, MetalInfo.Colors[(int)metal], flash) : Locked;

        arrow.rectTransform.anchoredPosition = pos + offset;
        shadow.rectTransform.anchoredPosition = pos + offset;
        if (!Mathf.Approximately(arrow.size, s))
        {
            arrow.size = s;
            shadow.size = s * 1.15f;
            arrow.SetVerticesDirty();
            shadow.SetVerticesDirty();
        }
        arrow.color = color;
        shadow.enabled = unlocked;      // 未解鎖的箭頭不畫外框，看起來更「淡」
    }

    static void SetLabel(Text t, string s, Vector2 pos, bool unlocked)
    {
        t.text = s;
        t.rectTransform.anchoredPosition = pos + new Vector2(150f, 0f);   // Text 的中心在框中央，往右移半個框寬
        t.color = unlocked ? Color.white : new Color(0.75f, 0.75f, 0.75f, 0.5f);
    }
}
