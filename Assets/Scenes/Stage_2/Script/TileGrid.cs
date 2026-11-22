using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// 
/// 用迴圈把地磚排成整齊的格子。
/// 用法：掛在空物件上 → 拖入地磚 prefab → 填行列數 → 在 Inspector 按 Generate（編輯模式即可，產生的地磚會存進場景）。
/// 地磚的大小會從模型的外框自動量出來，不用自己算間距。
/// 
public class TileGrid : MonoBehaviour
{
    [Tooltip("地磚 prefab（從 Project 視窗拖入）")]
    public GameObject tilePrefab;

    [Header("地磚大小")]
    [Tooltip("每塊地磚的寬（X）和長（Z），單位公尺；設 0 就用模型原本的大小")]
    public Vector2 tileSize = new Vector2(0.5f, 0.5f);
    [Tooltip("勾選：厚度（Y）跟著等比例縮放；取消：只縮放寬和長，厚度維持原樣")]
    public bool scaleThickness = true;

    [Header("格子")]
    [Min(1)] public int columns = 20;          // X 方向幾塊
    [Min(1)] public int rows = 20;             // Z 方向幾塊
    [Tooltip("地磚之間的縫（公尺）")]
    public float gap = 0f;
    [Tooltip("以這個物件為中心排列；取消勾選就從這個物件的位置往 +X、+Z 排")]
    public bool centered = true;

    [Header("變化")]
    [Tooltip("每塊隨機轉 0 / 90 / 180 / 270 度，看起來比較不像複製貼上（正方形地磚才建議用）")]
    public bool randomRotate90 = false;
    public int seed = 1;

    [Header("碰撞")]
    [Tooltip("在整片地磚下面放一個大的 Box Collider，取代每塊各自的碰撞體（效能較好，也不會在接縫卡腳）")]
    public bool oneBigCollider = true;

    const string ContainerName = "_Tiles";

    public void Generate()
    {
        Clear();
        if (!tilePrefab) { Debug.LogWarning("[TileGrid] 請先指定 Tile Prefab", this); return; }

        Vector3 raw = MeasureSize(tilePrefab);
        Vector3 scale = TileScale(raw);
        Vector3 size = Vector3.Scale(raw, scale);
        float stepX = size.x + gap, stepZ = size.z + gap;
        Vector3 origin = centered
            ? new Vector3(-(columns - 1) * stepX / 2f, 0f, -(rows - 1) * stepZ / 2f)
            : new Vector3(size.x / 2f, 0f, size.z / 2f);

        var container = new GameObject(ContainerName).transform;
        container.SetParent(transform, false);
        var rng = new System.Random(seed);

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                Vector3 local = origin + new Vector3(c * stepX, 0f, r * stepZ);
                Quaternion rot = randomRotate90 ? Quaternion.Euler(0f, 90f * rng.Next(4), 0f) : Quaternion.identity;
#if UNITY_EDITOR
                // 編輯模式用 PrefabUtility：產生的地磚仍然連著 prefab，之後改 prefab 會一起更新
                var tile = Application.isPlaying
                    ? Instantiate(tilePrefab, container)
                    : (GameObject)PrefabUtility.InstantiatePrefab(tilePrefab, container);
#else
                var tile = Instantiate(tilePrefab, container);
#endif
                tile.transform.localPosition = local;
                tile.transform.localRotation = rot * tilePrefab.transform.localRotation;
                tile.transform.localScale = Vector3.Scale(tilePrefab.transform.localScale, scale);
                tile.name = $"Tile_{r:00}_{c:00}";
                if (oneBigCollider)
                    foreach (var col in tile.GetComponentsInChildren<Collider>()) col.enabled = false;
                if (!Application.isPlaying) tile.isStatic = true;   // 靜態批次合併，大量地磚也不會拖慢
            }
        }

        if (oneBigCollider)
        {
            var box = container.gameObject.AddComponent<BoxCollider>();
            float w = columns * stepX - gap, d = rows * stepZ - gap;
            Vector3 center = centered ? Vector3.zero : new Vector3(w / 2f, 0f, d / 2f);
            box.size = new Vector3(w, Mathf.Max(size.y, 0.1f), d);
            float top = TopOffset(tilePrefab) * scale.y;
            box.center = center + new Vector3(0f, size.y > 0 ? -size.y / 2f + top : -0.05f, 0f);
        }

        Debug.Log($"[TileGrid] 產生了 {rows * columns} 塊地磚（每塊 {size.x:0.##} × {size.z:0.##} m）", this);
    }

    public void Clear()
    {
        var old = transform.Find(ContainerName);
        if (old) DestroyImmediate(old.gameObject);
    }

    /// 量出 prefab 的實際大小（含縮放與子物件）
    static Vector3 MeasureSize(GameObject prefab)
    {
        var tmp = Instantiate(prefab);
        tmp.transform.SetPositionAndRotation(Vector3.zero, prefab.transform.rotation);
        var renderers = tmp.GetComponentsInChildren<Renderer>();
        Bounds b = renderers.Length > 0 ? renderers[0].bounds : new Bounds(Vector3.zero, Vector3.one);
        foreach (var r in renderers) b.Encapsulate(r.bounds);
        DestroyImmediate(tmp);
        return b.size;
    }

    /// 把模型原本的大小縮放成 tileSize 需要的倍率（0 = 不縮放）
    Vector3 TileScale(Vector3 raw)
    {
        float sx = tileSize.x > 0f && raw.x > 0f ? tileSize.x / raw.x : 1f;
        float sz = tileSize.y > 0f && raw.z > 0f ? tileSize.y / raw.z : 1f;
        if (tileSize.x <= 0f && tileSize.y > 0f) sx = sz;      // 只填了一邊：另一邊等比例
        if (tileSize.y <= 0f && tileSize.x > 0f) sz = sx;
        float sy = scaleThickness ? (sx + sz) / 2f : 1f;
        return new Vector3(sx, sy, sz);
    }

    /// 地磚表面相對原點的高度（讓大碰撞體的頂面剛好在地磚表面）
    static float TopOffset(GameObject prefab)
    {
        var tmp = Instantiate(prefab);
        tmp.transform.SetPositionAndRotation(Vector3.zero, prefab.transform.rotation);
        float top = float.MinValue;
        foreach (var r in tmp.GetComponentsInChildren<Renderer>()) top = Mathf.Max(top, r.bounds.max.y);
        DestroyImmediate(tmp);
        return top == float.MinValue ? 0f : top;
    }

    void OnDrawGizmosSelected()
    {
        if (!tilePrefab) return;
        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.8f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Vector2 t = new Vector2(tileSize.x > 0 ? tileSize.x : 1f, tileSize.y > 0 ? tileSize.y : 1f);
        var size = new Vector3(columns * (t.x + gap), 0.05f, rows * (t.y + gap));   // 範圍提示（tileSize 為 0 時以 1 m 估計）
        Gizmos.DrawWireCube(centered ? Vector3.zero : size / 2f, size);
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(TileGrid))]
public class TileGridEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var grid = (TileGrid)target;
        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Generate", GUILayout.Height(28)))
            {
                grid.Generate();
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(grid.gameObject.scene);
            }
            if (GUILayout.Button("Clear", GUILayout.Height(28)))
            {
                grid.Clear();
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(grid.gameObject.scene);
            }
        }
    }
}
#endif