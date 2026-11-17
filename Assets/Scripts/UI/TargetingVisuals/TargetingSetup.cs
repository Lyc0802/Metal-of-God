using UnityEngine;
using UnityEngine.UI;

/// 
/// 遊戲開始時建立瞄準介面：一個 Canvas + TargetUI，並在 Player 身上加上 TargetingBridge。
/// 推拉本身完全由組員的程式負責，這裡只負責畫面。
/// 用法：場景裡任一物件掛上這個元件即可（例如跟 MetalWheelSetup 放在同一個物件上）。
/// 
public class TargetingSetup : MonoBehaviour
{
    [SerializeField] int sortingOrder = 15;   // 比輪盤（20）低：開輪盤時輪盤蓋在瞄準介面上面

    void Start()
    {
        var player = GameObject.FindGameObjectWithTag("Player");
        var controller = player ? player.GetComponent<PlayerController>() : null;
        if (!controller)
        {
            Debug.LogError("[TargetingSetup] 找不到有 PlayerController 的 Player（記得設 Player tag）", this);
            return;
        }

        var bridge = player.GetComponent<TargetingBridge>();
        if (!bridge) bridge = player.AddComponent<TargetingBridge>();
        if (!player.GetComponent<IronSteelSight>()) player.AddComponent<IronSteelSight>();   // 燒鐵或鋼時的藍線

        var go = new GameObject("TargetingCanvas", typeof(Canvas), typeof(CanvasScaler));
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        // 不加 GraphicRaycaster：介面不接收滑鼠，也不會讓組員的 IsPointerOverUI 誤判

        var view = controller.ViewCamera ? controller.ViewCamera : Camera.main;
        TargetUI.Create(canvas, bridge, view);
    }
}