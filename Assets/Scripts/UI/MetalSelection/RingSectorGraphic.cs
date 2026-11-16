using UnityEngine;
using UnityEngine.UI;

/// 
/// 自己產生網格（mesh）的 uGUI 元件：畫「圓環的一段」。
/// 整個 UI 的圖形都是它畫的，不需要任何圖片：
///   - 實心扇形（金屬存量、小地圖的半圓遮罩）
///   - 扇形外框（平常的細框、燃燒時的粗框）
///   - 柔和光暈（外框兩側透明度漸淡）
///   - 有點歪扭的圓環（中央「胃」的形狀）
///
/// 角度用數學慣例：0° = 右，90° = 上；sweep 為負代表順時針。
/// 
[RequireComponent(typeof(CanvasRenderer))]
public class RingSectorGraphic : MaskableGraphic
{
    public enum Mode { Fill, Outline }

    public Mode mode = Mode.Fill;
    public float innerRadius = 50f;
    public float outerRadius = 100f;
    public float startAngle = 0f;
    public float sweep = 360f;
    [Tooltip("外框模式的線寬（像素）")]
    public float thickness = 2f;
    [Tooltip("邊緣漸淡：做光暈用，線的中間最亮、兩側變透明")]
    public bool soft;
    [Tooltip("半徑隨角度起伏的幅度（0 = 正圓），用來畫胃的形狀")]
    public float wobble;
    public float wobbleFrequency = 3f;
    [Tooltip("每 360 度切幾段，越多越圓")]
    public int segmentsPer360 = 120;
    [Tooltip("只讓內側邊緣變透明（往外漸漸變不透明），用來做畫面周圍變暗")]
    public bool fadeInner;

    /// 一次設定好幾何參數並重畫。
    public void SetShape(float inner, float outer, float start, float sweepDegrees)
    {
        innerRadius = inner;
        outerRadius = outer;
        startAngle = start;
        sweep = sweepDegrees;
        SetVerticesDirty();
    }

    public void SetOuterRadius(float outer)
    {
        if (Mathf.Approximately(outer, outerRadius)) return;
        outerRadius = outer;
        SetVerticesDirty();
    }

    public void SetThickness(float t)
    {
        if (Mathf.Approximately(t, thickness)) return;
        thickness = t;
        SetVerticesDirty();
    }

    // ------------------------------------------------------------------
    // 產生網格
    // ------------------------------------------------------------------

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        int steps = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(sweep) / 360f * segmentsPer360));

        if (mode == Mode.Fill)
        {
            // 實心扇形 = 沿著「中間半徑」的一條粗線，粗細剛好是外半徑減內半徑
            float mid = (innerRadius + outerRadius) / 2f;
            float half = (outerRadius - innerRadius) / 2f;
            AddArc(vh, mid, half, steps);
        }
        else
        {
            // 外框 = 外圈弧線 + 內圈弧線 + 兩條半徑方向的邊
            float half = thickness / 2f;
            AddArc(vh, outerRadius, half, steps);
            if (innerRadius > 0.5f) AddArc(vh, innerRadius, half, steps);
            if (Mathf.Abs(sweep) < 359.9f)
            {
                AddRadialEdge(vh, startAngle, half);
                AddRadialEdge(vh, startAngle + sweep, half);
            }
        }
    }

    /// 半徑 r 會隨角度起伏（wobble），畫出不規則的胃形。
    float RadiusAt(float r, float degrees)
    {
        if (wobble == 0f) return r;
        float a = degrees * Mathf.Deg2Rad;
        return r * (1f + wobble * Mathf.Sin(wobbleFrequency * a) + wobble * 0.5f * Mathf.Sin(2f * a + 1f));
    }

    /// 
    /// 沿著圓弧畫一條寬 2 * halfWidth 的帶子。
    /// 每一段有三排頂點：內側、中線、外側；soft 時內外側的透明度為 0，就變成光暈。
    /// 
    void AddArc(VertexHelper vh, float radius, float halfWidth, int steps)
    {
        var center = (Vector2)rectTransform.rect.center;
        int first = vh.currentVertCount;
        for (int i = 0; i <= steps; i++)
        {
            float deg = startAngle + sweep * i / steps;
            var dir = MetalInfo.Direction(deg);
            // 三個半徑各自套用起伏，實心的胃和它的外框才會是同一個形狀
            AddVert(vh, center + dir * RadiusAt(Mathf.Max(0f, radius - halfWidth), deg), (soft || fadeInner) ? 0f : 1f);
            AddVert(vh, center + dir * RadiusAt(radius, deg), 1f);
            AddVert(vh, center + dir * RadiusAt(radius + halfWidth, deg), soft ? 0f : 1f);
        }
        for (int i = 0; i < steps; i++)
        {
            int a = first + i * 3, b = a + 3;          // 這一段和下一段的「內側」頂點
            Quad(vh, a, a + 1, b + 1, b);              // 內側 ~ 中線
            Quad(vh, a + 1, a + 2, b + 2, b + 1);      // 中線 ~ 外側
        }
    }

    /// 扇形兩側的直邊：從內半徑畫到外半徑。
    void AddRadialEdge(VertexHelper vh, float degrees, float halfWidth)
    {
        var center = (Vector2)rectTransform.rect.center;
        var dir = MetalInfo.Direction(degrees);
        var side = new Vector2(-dir.y, dir.x) * halfWidth;   // 垂直於半徑的方向
        var p0 = center + dir * RadiusAt(innerRadius, degrees);
        var p1 = center + dir * RadiusAt(outerRadius, degrees);
        int first = vh.currentVertCount;
        foreach (var p in new[] { p0, p1 })
        {
            AddVert(vh, p - side, soft ? 0f : 1f);
            AddVert(vh, p, 1f);
            AddVert(vh, p + side, soft ? 0f : 1f);
        }
        Quad(vh, first, first + 1, first + 4, first + 3);
        Quad(vh, first + 1, first + 2, first + 5, first + 4);
    }

    void AddVert(VertexHelper vh, Vector2 pos, float alphaScale)
    {
        var v = UIVertex.simpleVert;
        v.position = pos;
        var c = color;
        c.a *= alphaScale;
        v.color = c;
        vh.AddVert(v);
    }

    static void Quad(VertexHelper vh, int a, int b, int c, int d)
    {
        vh.AddTriangle(a, b, c);
        vh.AddTriangle(c, d, a);
    }

    // ------------------------------------------------------------------
    // 建立的小工具
    // ------------------------------------------------------------------

    public static RingSectorGraphic Create(string name, Transform parent, Mode mode, Color color,
                                            float inner, float outer, float start, float sweep)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = Vector2.zero;            // 用半徑畫圖，不依賴 RectTransform 大小
        rt.anchoredPosition = Vector2.zero;
        var g = go.AddComponent<RingSectorGraphic>();
        g.mode = mode;
        g.color = color;
        g.raycastTarget = false;
        g.SetShape(inner, outer, start, sweep);
        return g;
    }
}
