using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// 
/// 金屬選擇輪盤的操作：
///   1. 按住滑鼠中鍵（滾輪按下去）或鍵盤 Tab → 畫面變暗、時間變慢，大輪盤出現在畫面正中央
///   2. 移動滑鼠 → 一個看不見的「指標」從中心往外移動，指到哪個扇形就選哪個
///   3. 放開中鍵 / Tab → 指到的金屬切換燃燒 / 熄滅，輪盤關閉，回到遊戲
///   按住中鍵 / Tab 時按左鍵：直接切換指到的金屬、輪盤不關，可以一次開關好幾種
///   （右鍵留給「瞄準」，見 AllomanticTargeting）
///   數字鍵 1–8：不開輪盤，直接切換對應的金屬
/// 
public class MetalBurningWheel : MonoBehaviour
{
    public MetalReserves reserves;
    public float slowMotion = 0.25f;           // 選擇時的時間倍率
    public bool IsOpen { get; private set; }
    public static MetalBurningWheel Current { get; private set; }
    public static bool WheelOpen => Current != null && Current.IsOpen;

    public WheelConstruction wheel;
    public UISparks sparks;
    public CanvasGroup group;
    public RectTransform pointerDot;
    public Vector2 pointer;                           // 指標位置（輪盤的區域座標，單位是 UI 像素）
    bool toggledDuringHold, openedByTab;
    float openAmount;                          // 0 = 關閉，1 = 完全打開（做淡入淡出用）
    float defaultFixedDelta;
    public System.Func<bool> canAct;   // 由外部決定能不能開輪盤（例如玩家死亡時不行）

    public static MetalBurningWheel Create(Transform canvas, MetalReserves reserves)
    {
        var root = new GameObject("MetalBurningWheel", typeof(RectTransform), typeof(CanvasGroup));
        root.transform.SetParent(canvas, false);
        var rt = (RectTransform)root.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;

        var sel = root.AddComponent<MetalBurningWheel>();
        Current = sel;
        sel.reserves = reserves;
        sel.group = root.GetComponent<CanvasGroup>();
        sel.group.alpha = 0f;
        sel.group.blocksRaycasts = false;

        // 半透明黑色背景，讓輪盤更清楚
        var dim = new GameObject("Dim", typeof(RectTransform), typeof(CanvasRenderer)).AddComponent<Image>();
        dim.transform.SetParent(root.transform, false);
        dim.rectTransform.anchorMin = Vector2.zero;
        dim.rectTransform.anchorMax = Vector2.one;
        dim.rectTransform.offsetMin = dim.rectTransform.offsetMax = Vector2.zero;
        dim.color = new Color(0, 0, 0, 0.45f);
        dim.raycastTarget = false;

        sel.wheel = WheelConstruction.Create(root.transform, reserves, 260f);
        sel.sparks = UISparks.Create(sel.wheel.transform);

        // 指標：一個小圓點
        var dot = RingSectorGraphic.Create("Pointer", sel.wheel.transform, RingSectorGraphic.Mode.Fill,
                                            Color.white, 0f, 9f, 0f, 360f);
        sel.pointerDot = dot.rectTransform;

        sel.defaultFixedDelta = Time.fixedDeltaTime;
        reserves.Toggled += sel.OnToggled;
        reserves.Empty += m => SoundFx.Play(SoundFx.Kind.Empty);
        return sel;
    }

    void OnDestroy()
    {
        if (reserves != null) reserves.Toggled -= OnToggled;
    }

    void OnToggled(Metal m, bool burning)
    {
        SoundFx.Play(burning ? SoundFx.Kind.Burn : SoundFx.Kind.Extinguish);
        if (burning && IsOpen) sparks.Burst(wheel.SectorPoint((int)m), MetalInfo.Colors[(int)m]);
    }

    void Update()
    {
        var mouse = Mouse.current;
        var kb = Keyboard.current;
        if (mouse == null) return;

        // 滑鼠中鍵或鍵盤 Tab 都可以開輪盤；用哪個開，就由哪個的放開來關閉。
        bool middlePressed = mouse.middleButton.wasPressedThisFrame;
        bool tabPressed = kb != null && kb.tabKey.wasPressedThisFrame;
        if (!IsOpen && (middlePressed || tabPressed))
        {
            openedByTab = !middlePressed && tabPressed;
            Open();
        }

        if (IsOpen)
        {
            // 滑鼠位移（螢幕像素）換算成 Canvas 的單位（參考解析度 1080 高）
            float toCanvas = 1080f / Mathf.Max(1, Screen.height);
            pointer += mouse.delta.ReadValue() * toCanvas;
            pointer = Vector2.ClampMagnitude(pointer, wheel.Radius);

            // 離中心夠遠才算選到；在胃的範圍內代表「不選」
            int now = pointer.magnitude > wheel.Radius * 0.3f ? MetalInfo.SectorFromDirection(pointer) : -1;
            if (now != wheel.hovered && now >= 0) SoundFx.Play(SoundFx.Kind.Tick, 0.6f);
            wheel.hovered = now;
            pointerDot.anchoredPosition = pointer;

            if (mouse.leftButton.wasPressedThisFrame && wheel.hovered >= 0)
            {
                reserves.Toggle((Metal)wheel.hovered);
                toggledDuringHold = true;
            }

            bool released = openedByTab
                ? kb != null && kb.tabKey.wasReleasedThisFrame
                : mouse.middleButton.wasReleasedThisFrame;
            if (released)
            {
                // 如果按住期間已經用左鍵切換過，放開時就不再切換（避免切兩次又變回去）
                if (wheel.hovered >= 0 && !toggledDuringHold) reserves.Toggle((Metal)wheel.hovered);
                Close();
            }
        }
        else if (kb != null)
        {
            // 快捷鍵 1..8
            var keys = new[] { kb.digit1Key, kb.digit2Key, kb.digit3Key, kb.digit4Key,
                                kb.digit5Key, kb.digit6Key, kb.digit7Key, kb.digit8Key };
            for (int i = 0; i < keys.Length; i++)
                if (keys[i].wasPressedThisFrame) reserves.Toggle((Metal)i);
        }

        // 淡入淡出 + 輪盤從 85% 放大到 100%
        openAmount = Mathf.MoveTowards(openAmount, IsOpen ? 1f : 0f, Time.unscaledDeltaTime * 8f);
        group.alpha = openAmount;
        float s = Mathf.Lerp(0.85f, 1f, openAmount);
        wheel.Rect.localScale = new Vector3(s, s, 1f);
    }

    void Open()
    {
        IsOpen = true;
        pointer = Vector2.zero;
        wheel.hovered = -1;
        toggledDuringHold = false;
        Time.timeScale = slowMotion;                              // 慢動作，方便選擇
        Time.fixedDeltaTime = defaultFixedDelta * slowMotion;     // 物理也跟著變慢，才不會卡頓
        SoundFx.Play(SoundFx.Kind.Open);
    }

    void Close()
    {
        IsOpen = false;
        wheel.hovered = -1;
        Time.timeScale = 1f;
        Time.fixedDeltaTime = defaultFixedDelta;
        SoundFx.Play(SoundFx.Kind.Close);
    }
}

