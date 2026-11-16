using UnityEngine;
using UnityEngine.UI;

/// 
/// 自己產生網格的箭頭（朝上）。要朝下就把 RectTransform 轉 180 度。
/// 形狀（以 size 為單位，原點在中心）：
///
///            (0, 1)            ← 箭頭尖
///           /      \
///   (-0.55, 0.2)  (0.55, 0.2)  ← 箭頭底邊
///       |-0.22  0.22|          ← 箭身
///       |           |
///   (-0.22, -1) (0.22, -1)
/// 
[RequireComponent(typeof(CanvasRenderer))]
public class ArrowGraphic : MaskableGraphic
{
    public float size = 40f;           // 箭頭半高（像素）

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        // 箭頭的三角形
        Add(vh, -0.55f, 0.2f);
        Add(vh, 0f, 1f);
        Add(vh, 0.55f, 0.2f);
        vh.AddTriangle(0, 1, 2);
        // 箭身的長方形（兩個三角形）
        Add(vh, -0.22f, -1f);
        Add(vh, -0.22f, 0.2f);
        Add(vh, 0.22f, 0.2f);
        Add(vh, 0.22f, -1f);
        vh.AddTriangle(3, 4, 5);
        vh.AddTriangle(5, 6, 3);
    }

    void Add(VertexHelper vh, float x, float y)
    {
        var v = UIVertex.simpleVert;
        v.position = new Vector2(x, y) * size;
        v.color = color;
        vh.AddVert(v);
    }

    public static ArrowGraphic Create(string name, Transform parent, float size, bool pointUp)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = Vector2.zero;
        rt.localRotation = Quaternion.Euler(0, 0, pointUp ? 0f : 180f);
        var a = go.AddComponent<ArrowGraphic>();
        a.size = size;
        a.raycastTarget = false;
        return a;
    }
}
