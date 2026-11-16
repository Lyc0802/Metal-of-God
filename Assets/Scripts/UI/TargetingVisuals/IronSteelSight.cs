using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 燃燒鐵或鋼時：從玩家胸口拉出藍線，指向視野範圍內每個金屬物件（組員的 MetalDisplayer）。
///   「視野範圍內」= 距離不超過 range，而且在鏡頭畫面裡
///   顏色：比玩家重（或固定不動）→ 深藍；比玩家輕 → 淺藍；正在瞄準或推拉中的那條 → 接近白色、變粗
///   粗細：越重越粗、越遠越細，並微微閃爍
/// 只負責畫線；金屬清單、重量、瞄準目標都讀組員的程式。由 TargetingSetup 加到 Player 上。
/// </summary>
public class IronSteelSight : MonoBehaviour
{
    [Tooltip("線的起點；留空會自動找胸口的骨頭")]
    public Transform chest;
    public float range = 35f;

    PlayerController controller;
    PlayerMetalInventory inventory;
    PlayerMetalSelector selector;
    PlayerMetalManipulator manipulator;

    readonly List<LineRenderer> lines = new List<LineRenderer>();   // 物件池，不夠才新增
    Material heavyMat, lightMat, targetMat;
    Transform lineRoot;

    void Start()
    {
        controller = GetComponent<PlayerController>();
        inventory = GetComponent<PlayerMetalInventory>();
        selector = GetComponent<PlayerMetalSelector>();
        manipulator = GetComponent<PlayerMetalManipulator>();
        if (!chest) chest = FindChest();

        heavyMat = Glow(new Color(0.12f, 0.28f, 0.85f));   // 深藍
        lightMat = Glow(new Color(0.55f, 0.85f, 1f));      // 淺藍
        targetMat = Glow(new Color(0.85f, 0.95f, 1f));     // 被瞄準：接近白色

        // 線放在場景最外層，不跟著玩家的 Transform 縮放或旋轉
        lineRoot = new GameObject("IronSteelSightLines").transform;
    }

    void OnDestroy()
    {
        if (lineRoot) Destroy(lineRoot.gameObject);
    }

    void LateUpdate()
    {
        int used = 0;
        var view = controller && controller.ViewCamera ? controller.ViewCamera : Camera.main;
        bool active = inventory && view
                   && (inventory.CanUse(MetalType.Iron) || inventory.CanUse(MetalType.Steel));

        if (active)
        {
            Vector3 from = ChestPosition();
            MetalDisplayer aimed = AimedMetal();
            float playerWeight = manipulator ? manipulator.Weight : 80f;

            foreach (var metal in MetalDisplayer.ActiveMetals)
            {
                if (!metal || !metal.isActiveAndEnabled) continue;
                Vector3 to = Center(metal);
                float dist = Vector3.Distance(from, to);
                if (dist > range || dist < 0.5f) continue;

                // 在鏡頭畫面內才畫（z > 0 代表在鏡頭前方）
                var vp = view.WorldToViewportPoint(to);
                if (vp.z <= 0f || vp.x < 0f || vp.x > 1f || vp.y < 0f || vp.y > 1f) continue;

                var body = metal.GetComponent<Rigidbody>();
                bool heavier = body == null || body.isKinematic || metal.weight > playerWeight;
                bool isAimed = metal == aimed;

                var line = GetLine(used++);
                line.sharedMaterial = isAimed ? targetMat : heavier ? heavyMat : lightMat;
                float width = (0.008f + 0.025f * Mathf.Log(1f + Mathf.Max(0f, metal.weight))) * Mathf.Lerp(1f, 0.4f, dist / range);
                if (isAimed) width *= 2f;
                width *= 0.85f + 0.15f * Mathf.Sin(Time.time * 25f + used);   // 微微閃爍
                line.startWidth = width;
                line.endWidth = width * 0.6f;
                line.SetPosition(0, from);
                line.SetPosition(1, to);

                float alpha = Mathf.Lerp(0.5f, 0.25f, dist / range);           // 近 50% → 遠 25%
                line.startColor = line.endColor = new Color(1f, 1f, 1f, isAimed ? 0.9f : alpha);
            }
        }

        for (int i = used; i < lines.Count; i++) lines[i].enabled = false;
    }

    /// 推拉中的目標，或正在瞄準的金屬（都沒有就是 null）
    MetalDisplayer AimedMetal()
    {
        if (manipulator && manipulator.IsLocked && manipulator.Target.Valid) return manipulator.Target.Metal;
        if (controller && controller.IsSelectingMetal && selector && selector.Current.Valid) return selector.Current.Metal;
        return null;
    }

    static Vector3 Center(MetalDisplayer metal)
    {
        var col = metal.GetComponentInChildren<Collider>();
        if (col) return col.bounds.center;
        var r = metal.GetComponentInChildren<Renderer>();
        return r ? r.bounds.center : metal.transform.position;
    }

    Vector3 ChestPosition()
    {
        if (chest) return chest.position;
        // 找不到胸口骨頭：用玩家碰撞體的上半部
        var col = GetComponent<Collider>();
        if (col) return col.bounds.center + Vector3.up * col.bounds.extents.y * 0.4f;
        return transform.position + Vector3.up * 1.3f;
    }

    /// Humanoid 骨架直接問 Animator；否則找名字裡有 chest / spine 的骨頭（取最高的那根）
    Transform FindChest()
    {
        var anim = GetComponentInChildren<Animator>();
        if (anim && anim.isHuman)
        {
            var t = anim.GetBoneTransform(HumanBodyBones.UpperChest);
            if (!t) t = anim.GetBoneTransform(HumanBodyBones.Chest);
            if (t) return t;
        }
        Transform best = null;
        foreach (var t in GetComponentsInChildren<Transform>())
        {
            string n = t.name.ToLowerInvariant();
            if (n.Contains("chest")) return t;
            if (n.Contains("spine") && (best == null || t.position.y > best.position.y)) best = t;
        }
        return best;
    }

    LineRenderer GetLine(int i)
    {
        if (i == lines.Count)
        {
            var go = new GameObject("SteelLine");
            go.transform.SetParent(lineRoot, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.useWorldSpace = true;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lines.Add(lr);
        }
        lines[i].enabled = true;
        return lines[i];
    }

    static Material Glow(Color c)
    {
        // Sprites/Default：半透明、不受光照、在 URP 也能用
        return new Material(Shader.Find("Sprites/Default")) { color = c };
    }
}