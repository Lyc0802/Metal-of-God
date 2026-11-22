using System;
using UnityEngine;
using UnityEngine.InputSystem;

public enum TargetKind { None, Metal, Emotion }

/// <summary>
/// 把組員的瞄準與推拉狀態，整理成 TargetUI 需要的資料。只讀不寫，不會影響推拉本身。
///   選目標：PlayerMetalSelector（按住右鍵時，畫面中央射線打到的金屬或敵人）
///   推拉金屬：PlayerMetalManipulator（滾輪往上 = 鋼推、往下 = 鐵拉）
///   操控敵人：PlayerEnemyManipulator（滾輪往上 = 鋅、往下 = 黃銅）
/// 掛在 Player 上（TargetingSetup 會自動加上）。
/// </summary>
[DefaultExecutionOrder(100)]   // 在 PlayerController 之後執行，讀到的是這一幀已更新的狀態
public class TargetingBridge : MonoBehaviour
{
    PlayerController controller;
    PlayerMetalSelector selector;
    PlayerMetalManipulator manipulator;
    PlayerEnemyManipulator enemyManipulator;
    PlayerMetalInventory inventory;

    bool wasLocked, wasActivated;

    // ---- TargetUI 讀的資料 ----
    public bool Aiming { get; private set; }
    public TargetKind Kind { get; private set; }
    public Vector3 TargetPoint { get; private set; }

    /// 上箭頭：金屬 = 鋼推，敵人 = 鋅；下箭頭：金屬 = 鐵拉，敵人 = 黃銅
    public Metal UpMetal => Kind == TargetKind.Emotion ? Metal.Zinc : Metal.Steel;
    public Metal DownMetal => Kind == TargetKind.Emotion ? Metal.Brass : Metal.Iron;
    public bool UpUnlocked { get; private set; }
    public bool DownUnlocked { get; private set; }

    /// 目標比玩家重（或固定不動）：圓圈用深藍色
    public bool TargetHeavier { get; private set; }
    /// 目標下方的說明文字
    public string InfoText { get; private set; } = "";

    /// (是不是上箭頭, 成功或失敗)：成功時箭頭放大變色，失敗（金屬沒在燒）時抖動
    public event Action<bool, bool> Used;

    void Awake()
    {
        controller = GetComponent<PlayerController>();
        selector = GetComponent<PlayerMetalSelector>();
        manipulator = GetComponent<PlayerMetalManipulator>();
        enemyManipulator = GetComponent<PlayerEnemyManipulator>();
        inventory = GetComponent<PlayerMetalInventory>();
    }

    void Update()
    {
        if (!controller || !selector || !manipulator || !inventory)
        {
            Aiming = false;
            Kind = TargetKind.None;
            return;
        }

        bool locked = manipulator.IsLocked;
        bool activated = enemyManipulator && enemyManipulator.HasActivatedThisHold;
        // Aiming = controller.IsSelectingMetal || locked || activated;
        Aiming = controller.IsSelectingMetal;

        // ---- 目前的目標 ----
        // 推拉鎖定後，組員會清掉選取，所以改從 manipulator 拿正在推拉的目標
        Enemy enemy = null;
        MetalSurface surface = default;
        if (locked) surface = manipulator.Target;
        else if (activated) enemy = enemyManipulator.Target;
        else if (controller.IsSelectingMetal)
        {
            enemy = selector.CurrentEnemy;
            if (!enemy) surface = selector.Current;
        }

        if (enemy && !enemy.IsDead)
        {
            Kind = TargetKind.Emotion;
            TargetPoint = enemy.TargetPoint;
            TargetHeavier = false;
            InfoText = EnemyInfo(enemy);
        }
        else if (surface.Valid)
        {
            Kind = TargetKind.Metal;
            TargetPoint = surface.Point;
            MetalInfoText(surface.Metal);
        }
        else
        {
            Kind = TargetKind.None;
            InfoText = "";
        }

        UpUnlocked = inventory.CanUse(Kind == TargetKind.Emotion ? MetalType.Zinc : MetalType.Steel);
        DownUnlocked = inventory.CanUse(Kind == TargetKind.Emotion ? MetalType.Brass : MetalType.Iron);

        // ---- 成功：推拉剛鎖定、或剛對敵人施放 ----
        if (locked && !wasLocked) Used?.Invoke(!manipulator.IsPull, true);
        if (activated && !wasActivated) Used?.Invoke(ScrollThisFrame() > 0, true);

        // ---- 失敗：有目標、滾了滾輪，但對應的金屬沒在燒 ----
        float scroll = ScrollThisFrame();
        if (Aiming && !locked && !activated && Kind != TargetKind.None && scroll != 0)
        {
            bool up = scroll > 0;
            if (up ? !UpUnlocked : !DownUnlocked) Used?.Invoke(up, false);
        }

        wasLocked = locked;
        wasActivated = activated;
    }

    static float ScrollThisFrame() => Mouse.current != null ? Mouse.current.scroll.ReadValue().y : 0f;

    void MetalInfoText(MetalDisplayer metal)
    {
        var body = metal.GetComponent<Rigidbody>();
        bool anchored = body == null || body.isKinematic;
        float playerWeight = manipulator.Weight;
        TargetHeavier = anchored || metal.weight > playerWeight;
        string weight = anchored ? "anchored" : $"weight {metal.weight:0.#}";
        string compare = anchored ? "" : Mathf.Approximately(metal.weight, playerWeight) ? "  (same as you)"
                       : metal.weight > playerWeight ? "  (heavier than you)" : "  (lighter than you)";
        InfoText = $"{metal.gameObject.name}   {weight}{compare}";
    }

    static string EnemyInfo(Enemy enemy)
    {
        var test = enemy.GetComponent<TestEnemy>();
        if (test && test.IsZincActive) return "Enemy  —  enraged";
        if (test && test.IsBrassActive) return "Enemy  —  soothed";
        return "Enemy  —  emotional core";
    }
}
