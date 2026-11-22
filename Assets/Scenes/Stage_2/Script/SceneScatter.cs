using System.Collections.Generic;
using UnityEngine;
using Unity.AI.Navigation;   // NavMeshSurface（AI Navigation 套件）

namespace MP3_Save
{
    /// 用迴圈在範圍內隨機擺放多個模型：
    /// - 從上往下打射線，放在地形表面上
    /// - 彼此保持最小距離、避開保留區（例如角色出生點）
    /// - 固定 seed：每次產生的位置都一樣，存讀檔時場景不會變
    /// 用法：掛在空物件上，填好 prefabs，Play 時自動產生；Rebuild Nav Mesh 拖入地面的 NavMesh Surface。
    public class SceneScatter : MonoBehaviour
    {
        [System.Serializable]
        public class Entry
        {
            public GameObject prefab;
            public int count = 10;
            public Vector2 scaleRange = new Vector2(0.8f, 1.2f);
            public float spacing = 5f;            // 跟其他物件至少隔多遠
            public bool alignToGround = true;    // 是否順著坡度傾斜（樹建議 false，石頭可以 true）
            public bool addCollider = true;      // 沒有碰撞體的模型，自動加上貼合外形的 Box Collider
            public bool pickOneChild = true;     // 模型是一整組（例如 4 顆寶石）時，每次只留隨機一顆，並把它的底部貼到地面
        }

        [SerializeField] List<Entry> entries = new List<Entry>();

        [Header("範圍（以這個物件為中心）")]
        [SerializeField] Vector2 areaSize = new Vector2(60f, 60f);
        [SerializeField] float rayHeight = 100f;
        [SerializeField] LayerMask groundMask = ~0;
        [SerializeField] float maxSlope = 35f;            // 太陡的地方不放
        [SerializeField] float surfaceOffset = 0f;        // 放好後往上抬多少（模型原點不在底部時用）

        [Header("保留區（不放東西）")]
        [SerializeField] Transform[] keepClear;           // 例如 Player、兩隻龍
        [SerializeField] float keepClearRadius = 4f;

        [Header("其他")]
        [SerializeField] int seed = 12345;
        [SerializeField] bool generateOnStart = true;     // Play 時自動產生
        [SerializeField] NavMeshSurface rebuildNavMesh;    // 拖入地面的 NavMesh Surface：產生完重新烘焙，龍才會繞開
        [SerializeField] int maxTriesPerObject = 30;

        const string ContainerName = "_Scattered";

        void Awake()   // 用 Awake：比角色和龍的 Start 早，大家開始動之前場景就擺好了
        {
            if (!generateOnStart) return;
            Generate();
            if (rebuildNavMesh != null)
            {
                Physics.SyncTransforms();
                rebuildNavMesh.BuildNavMesh();
            }
        }

        [ContextMenu("Generate")]
        public void Generate()
        {
            Clear();
            Physics.SyncTransforms();          // 編輯模式下移動過的碰撞體，先同步到物理世界
            bool oldBackfaces = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true; // Sketchfab 模型常有法線反向的面，也要打得到
            int noHit = 0, steep = 0, close = 0, clear = 0;
            var hitNames = new Dictionary<string, int>();   // 統計射線打到哪些物件，方便除錯
            var container = new GameObject(ContainerName).transform;
            container.SetParent(transform, false);              // 放在 Scatter 底下
            Vector3 parentScale = container.lossyScale;          // Scatter 有縮放的話，子物件要抵銷回來

            var rng = new System.Random(seed);
            var placed = new List<(Vector3 pos, float radius)>();
            int total = 0;

            foreach (var e in entries)
            {
                if (e.prefab == null) continue;
                int made = 0;
                for (int i = 0; i < e.count; i++)                       // 每種模型要放 count 個
                {
                    for (int t = 0; t < maxTriesPerObject; t++)          // 每個最多試幾次找位置
                    {
                        float x = ((float)rng.NextDouble() - 0.5f) * areaSize.x;
                        float z = ((float)rng.NextDouble() - 0.5f) * areaSize.y;
                        Vector3 origin = transform.position + new Vector3(x, rayHeight, z);

                        // 打穿所有碰撞體，取「最高」的那個表面（避免打到地面底下的東西）
                        var hits = Physics.RaycastAll(origin, Vector3.down, rayHeight * 2f,
                                                      groundMask, QueryTriggerInteraction.Ignore);
                        bool found = false;
                        RaycastHit hit = default;
                        foreach (var h in hits)
                        {
                            if (h.transform.IsChildOf(container)) continue;              // 剛放的物件
                            if (h.collider is CharacterController) continue;              // 角色的膠囊
                            if (h.collider.GetComponentInParent<UnityEngine.AI.NavMeshAgent>() != null) continue;  // 龍身上的碰撞體
                            if (!found || h.point.y > hit.point.y) { hit = h; found = true; }
                        }
                        if (!found) { noHit++; continue; }
                        string hn = hit.collider.name;
                        hitNames[hn] = hitNames.TryGetValue(hn, out int c) ? c + 1 : 1;
                        float slope = Vector3.Angle(hit.normal, Vector3.up);
                        if (slope > 90f) slope = 180f - slope;                            // 法線反向的面
                        if (slope > maxSlope) { steep++; continue; }
                        if (TooClose(hit.point, e.spacing, placed)) { close++; continue; }
                        if (InKeepClear(hit.point)) { clear++; continue; }

                        float yaw = (float)rng.NextDouble() * 360f;
                        Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
                        if (e.alignToGround) rot = Quaternion.FromToRotation(Vector3.up, hit.normal) * rot;
                        // Inspector 用 + 新增的項目，欄位預設值會是 0；沒填縮放就當作 1
                        Vector2 sr = (e.scaleRange.x <= 0f && e.scaleRange.y <= 0f) ? Vector2.one : e.scaleRange;
                        float s = Mathf.Lerp(sr.x, sr.y, (float)rng.NextDouble());

                        var go = Instantiate(e.prefab, hit.point + Vector3.up * surfaceOffset, rot, container);
                        Vector3 world = e.prefab.transform.localScale * s;   // 想要的世界大小
                        go.transform.localScale = new Vector3(SafeDiv(world.x, parentScale.x),
                                                              SafeDiv(world.y, parentScale.y),
                                                              SafeDiv(world.z, parentScale.z));
                        go.name = $"{e.prefab.name}_{made:00}";
                        if (e.pickOneChild) KeepOneChild(go, hit.point + Vector3.up * surfaceOffset, rng);
                        if (e.addCollider) AddColliders(go);

                        placed.Add((hit.point, e.spacing * 0.5f));
                        made++;
                        break;
                    }
                }
                if (made < e.count)
                    Debug.LogWarning($"[SceneScatter] {e.prefab.name} 只放了 {made}/{e.count} 個（範圍太小或 spacing 太大）", this);
                total += made;
            }
            Physics.queriesHitBackfaces = oldBackfaces;
            Debug.Log($"[SceneScatter] 共產生 {total} 個物件。失敗的嘗試：射線沒打到地面 {noHit}、太陡 {steep}、太近 {close}、保留區 {clear}", this);
            var summary = new System.Text.StringBuilder("[SceneScatter] 射線打到的表面：");
            foreach (var kv in hitNames) summary.Append($"{kv.Key} ×{kv.Value}  ");
            Debug.Log(summary.ToString(), this);
        }

        [ContextMenu("Clear")]
        public void Clear()
        {
            var old = transform.Find(ContainerName);
            if (old == null) return;
            DestroyImmediate(old.gameObject);   // 立刻刪掉，避免這一幀的射線還打到舊物件
        }

        // 只留下隨機一個有外觀的子物件，再把它移到落點：水平置中、底部貼地
        static void KeepOneChild(GameObject go, Vector3 groundPoint, System.Random rng)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;
            var keep = renderers[rng.Next(renderers.Length)];
            foreach (var r in renderers)
            {
                if (r == null || r == keep || r.gameObject == go || keep.transform.IsChildOf(r.transform)) continue;
                DestroyImmediate(r.gameObject);
            }
            go.name += "_" + keep.name;

            Bounds b = keep.bounds;                                   // 這顆在世界中的外框
            Vector3 shift = new Vector3(groundPoint.x - b.center.x,
                                        groundPoint.y - b.min.y,
                                        groundPoint.z - b.center.z);
            go.transform.position += shift;
        }

        // 每個有外觀的部分加一個 Box Collider；加在有 MeshFilter 的物件上，Unity 會自動貼合網格大小
        static void AddColliders(GameObject go)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (r.GetComponent<Collider>() != null) continue;
                r.gameObject.AddComponent<BoxCollider>();
            }
        }

        static float SafeDiv(float a, float b) => Mathf.Abs(b) < 1e-6f ? a : a / b;

        bool TooClose(Vector3 p, float spacing, List<(Vector3 pos, float radius)> placed)
        {
            foreach (var (pos, r) in placed)
            {
                float need = spacing * 0.5f + r;
                if ((new Vector2(p.x - pos.x, p.z - pos.z)).sqrMagnitude < need * need) return true;
            }
            return false;
        }

        bool InKeepClear(Vector3 p)
        {
            if (keepClear == null) return false;
            foreach (var t in keepClear)
            {
                if (t == null) continue;
                Vector3 d = p - t.position; d.y = 0f;
                if (d.sqrMagnitude < keepClearRadius * keepClearRadius) return true;
            }
            return false;
        }

        void OnDrawGizmosSelected()   // 選取時在 Scene 視窗畫出範圍
        {
            Gizmos.color = new Color(0.2f, 0.8f, 0.3f, 0.8f);
            Gizmos.DrawWireCube(transform.position, new Vector3(areaSize.x, 1f, areaSize.y));
        }
    }
}