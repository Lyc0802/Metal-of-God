using System;
using UnityEngine;

/// 
/// 八種金屬。順序就是輪盤上的順序：從正左方 (-1, 0) 開始，順時針排列。
/// 
public enum Metal { Iron, Steel, Zinc, Brass, Tin, Pewter, Copper, Bronze }

/// 
/// 金屬的固定資料：名稱、顏色、在輪盤上的角度。
/// 
public static class MetalInfo
{
    public const int Count = 8;
    public const float SectorAngle = 360f / Count;   // 每種金屬佔 45 度

    public static readonly string[] Names =
        { "Iron", "Steel", "Zinc", "Brass", "Tin", "Pewter", "Copper", "Bronze" };

    public static readonly string[] Chinese =
        { "鐵", "鋼", "鋅", "黃銅", "錫", "白鑞", "紅銅", "青銅" };

    public static readonly Color[] Colors =
    {
        new Color(0.62f, 0.10f, 0.14f),   // 鐵：暗紅（鐵鏽）
        new Color(0.78f, 0.80f, 0.86f),   // 鋼：亮灰帶藍
        new Color(0.70f, 0.66f, 0.96f),   // 鋅：淡紫
        new Color(0.98f, 0.84f, 0.16f),   // 黃銅：黃
        new Color(0.86f, 0.86f, 0.84f),   // 錫：淺灰
        new Color(0.58f, 0.60f, 0.64f),   // 白鑞：深灰
        new Color(0.96f, 0.52f, 0.16f),   // 紅銅：橘
        new Color(0.80f, 0.46f, 0.22f),   // 青銅：古銅
    };

    /// 
    /// 第 i 個扇形的起始角度（數學角度：0° = 右，90° = 上，逆時針為正）。
    /// 鐵從 180°（左）開始，每個往順時針走 45°，所以起始角度是 180 - 45 * i。
    /// 
    public static float StartAngle(int i) => 180f - SectorAngle * i;

    /// 扇形中線的角度，用來放文字或往外推。
    public static float MidAngle(int i) => StartAngle(i) - SectorAngle / 2f;

    /// 
    /// 反過來：給一個方向（例如滑鼠拖曳的方向），算出它落在哪個扇形。
    /// atan2 回傳 -180..180 度；「從 180° 順時針走了幾度」= 180 - angle，再除以 45 取整數。
    /// 
    public static int SectorFromDirection(Vector2 dir)
    {
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        float clockwiseFromLeft = Mathf.Repeat(180f - angle, 360f);
        return Mathf.Clamp((int)(clockwiseFromLeft / SectorAngle), 0, Count - 1);
    }

    public static Vector2 Direction(float degrees)
    {
        float r = degrees * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(r), Mathf.Sin(r));
    }
}

/// 
/// 玩家體內的金屬存量與燃燒狀態。
///   amount[i]  ：0（用完）到 1（滿）
///   burning[i] ：是否正在燃燒；燃燒時存量會一直減少，用完自動熄滅
/// UI 只讀這裡的資料來畫圖，所以遊戲邏輯和畫面是分開的。
/// 
public class MetalReserves : MonoBehaviour
{
    public readonly float[] amount = new float[MetalInfo.Count];
    public readonly bool[] burning = new bool[MetalInfo.Count];
    
    static public readonly MetalType[] ToGame =
    {
        MetalType.Iron, MetalType.Steel, MetalType.Zinc,   MetalType.Brass,
        MetalType.Tin,  MetalType.Pewter, MetalType.Copper, MetalType.Bronze,
    };

    [Tooltip("滿存量可以連續燒幾秒")]
    public float secondsPerFullReserve = 45f;

    /// (金屬, 是否開始燃燒) — 給音效與粒子用。
    public event Action<Metal, bool> Toggled;
    /// 想燒但已經沒有存量。
    public event Action<Metal> Empty;
    public PlayerMetalInventory inventory;

    void Awake()
    {

        var player = GameObject.FindGameObjectWithTag("Player");
        if (player) inventory = player.GetComponent<PlayerMetalInventory>();
        if (!inventory) Debug.LogError("[MetalReserves] 找不到 Player 身上的 PlayerMetalInventory");

        // 一開始每種金屬給不同的存量，畫面比較好辨認
        for (int i = 0; i < MetalInfo.Count; i++)
            amount[i] = 0.55f + 0.45f * ((i * 37) % 10) / 9f;
    }

    public bool IsBurning(Metal m) => burning[(int)m];

    public void Toggle(Metal m)
    {
        int i = (int)m;
        var r = inventory ? inventory.Get(ToGame[i]) : null;
        if (r == null) return;
        if (!r.IsEnabled && r.CurrentAmount <= 0) { Empty?.Invoke(m); return; }
        r.IsEnabled = !r.IsEnabled;
        burning[i] = r.IsEnabled && r.CurrentAmount > 0;
        Toggled?.Invoke(m, burning[i]);
    }

    public void Refill(Metal m, float add)
    {
        var r = inventory ? inventory.Get(ToGame[(int)m]) : null;
        if (r != null) inventory.Add(ToGame[(int)m], add * r.MaxAmount);
    }

    void Update()
    {
        if (!inventory) return;
        for (int i = 0; i < MetalInfo.Count; i++)
        {
            var r = inventory.Get(ToGame[i]);
            bool wasBurning = burning[i];
            amount[i]  = r != null && r.MaxAmount > 0 ? r.CurrentAmount / r.MaxAmount : 0f;
            burning[i] = r != null && r.IsEnabled && r.CurrentAmount > 0;
            if (wasBurning && !burning[i]) Toggled?.Invoke((Metal)i, false);   // 燒完了，輪盤播熄滅音效
        }
    }
}