using System.Collections.Generic;
using UnityEngine;

/// 
/// 敵人的情緒核心（全部由這一個元件管理）：
///   每隔 scanInterval 秒找出場景裡所有組員的 Enemy，還沒有核心的就建立一個，之後生成的敵人也會有。
///   核心 = 胸口的發光小球 + 三顆繞著轉的光點，玩家燒鋅或黃銅時才看得到。
///   情緒值 0（平靜，黃）~ 1（憤怒，紅橘）：被煽動（鋅）→ 1；被安撫（黃銅）→ 0；追擊 / 攻擊中 → 0.75；閒晃 → 0.1
///   越生氣心跳越快、光點轉越快；越遠越透明。敵人死亡或消失時核心一起移除。
/// 只讀敵人的狀態，不修改敵人的程式、場景或 prefab。
/// 用法：掛在場景裡任一物件上（例如跟 TargetingSetup 同一個物件）。
/// 
public class EmotionCoreSetup : MonoBehaviour
{
    [SerializeField] float scanInterval = 1f;
    [SerializeField] float heightOffset = 0f;        // 核心太低（例如在肚子）時往上調

    static readonly Color Calm = new Color(1f, 0.85f, 0.2f);
    static readonly Color Angry = new Color(1f, 0.35f, 0.05f);

    /// 一個敵人的核心（普通類別，不是元件）
    class Core
    {
        public Enemy enemy;
        public TestEnemy ai;                // 不是 TestEnemy 的敵人：讀不到情緒狀態，維持平靜的顏色
        public Transform root;
        public Material mat;
        public readonly List<Transform> orbiters = new List<Transform>();
        public float anger = 0.1f;
    }

    readonly List<Core> cores = new List<Core>();
    readonly HashSet<Enemy> known = new HashSet<Enemy>();
    PlayerMetalInventory inventory;
    Transform player;
    float nextScan;

    void Start()
    {
        var p = GameObject.FindGameObjectWithTag("Player");
        inventory = p ? p.GetComponent<PlayerMetalInventory>() : null;
        player = p ? p.transform : null;
        if (!inventory) Debug.LogError("[EmotionCoreSetup] 找不到 Player 身上的 PlayerMetalInventory（記得設 Player tag）", this);
    }

    void Update()
    {
        if (!inventory || Time.time < nextScan) return;
        nextScan = Time.time + scanInterval;

        foreach (var enemy in FindObjectsByType<Enemy>(FindObjectsSortMode.None))
            if (!enemy.IsDead && known.Add(enemy)) cores.Add(Create(enemy));
    }

    void LateUpdate()
    {
        bool burning = inventory && (inventory.CanUse(MetalType.Zinc) || inventory.CanUse(MetalType.Brass));

        for (int i = cores.Count - 1; i >= 0; i--)
        {
            var c = cores[i];
            if (!c.enemy)                       // 敵人物件被刪掉了：核心也刪掉
            {
                Remove(i);
                continue;
            }

            bool visible = burning && !c.enemy.IsDead;
            if (c.root.gameObject.activeSelf != visible) c.root.gameObject.SetActive(visible);

            // 情緒值即使看不到也持續變化，打開鋅 / 黃銅時才不會突然跳色
            float target = 0.1f;
            if (c.ai)
            {
                if (c.ai.IsZincActive) target = 1f;
                else if (c.ai.IsBrassActive) target = 0f;
                else if (c.ai.State == EnemyState.Chase || c.ai.State == EnemyState.Attack) target = 0.75f;
            }
            c.anger = Mathf.MoveTowards(c.anger, target, Time.deltaTime * 0.8f);
            if (!visible) continue;

            c.root.position = c.enemy.TargetPoint + Vector3.up * heightOffset;

            // 心跳：越生氣跳越快；|sin|^8 做出「咚」的尖峰
            float beat = Mathf.Pow(Mathf.Abs(Mathf.Sin(Time.time * Mathf.Lerp(2.5f, 7f, c.anger))), 8f);
            c.root.localScale = Vector3.one * (1f + 0.5f * beat + 0.4f * c.anger);

            var col = Color.Lerp(Calm, Angry, c.anger);
            float dist = player ? Vector3.Distance(player.position, c.root.position) : 0f;
            col.a = Mathf.Lerp(0.5f, 0.25f, Mathf.Clamp01(dist / 35f));   // 近 50% → 遠 25%
            c.mat.color = col;

            // 三顆光點繞著核心轉
            for (int k = 0; k < c.orbiters.Count; k++)
            {
                float a = Time.time * Mathf.Lerp(2f, 6f, c.anger) + k * 2.094f;   // 2.094 = 120 度
                c.orbiters[k].localPosition = new Vector3(Mathf.Cos(a), Mathf.Sin(a * 1.3f) * 0.4f, Mathf.Sin(a)) * 0.22f;
            }
        }
    }

    Core Create(Enemy enemy)
    {
        var c = new Core
        {
            enemy = enemy,
            ai = enemy.GetComponent<TestEnemy>(),
            // 放在這個物件底下、每幀跟到敵人的 TargetPoint：不受敵人模型的縮放和動畫影響
            root = new GameObject($"EmotionCore_{enemy.name}").transform,
            mat = new Material(Shader.Find("Sprites/Default")) { color = Calm },   // 每個敵人一份，顏色才能各自變化
        };
        c.root.SetParent(transform, false);
        Sphere(c, "Core", 0.22f);
        for (int i = 0; i < 3; i++) c.orbiters.Add(Sphere(c, "Spark", 0.06f));
        c.root.gameObject.SetActive(false);
        return c;
    }

    static Transform Sphere(Core c, string name, float size)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = name;
        DestroyImmediate(go.GetComponent<Collider>());     // 不能擋住推拉的射線或碰撞
        go.transform.SetParent(c.root, false);
        go.transform.localScale = Vector3.one * size;
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = c.mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return go.transform;
    }

    void Remove(int i)
    {
        var c = cores[i];
        if (c.root) Destroy(c.root.gameObject);
        if (c.mat) Destroy(c.mat);
        known.Remove(c.enemy);
        cores.RemoveAt(i);
    }

    void OnDestroy()
    {
        for (int i = cores.Count - 1; i >= 0; i--) Remove(i);
    }
}