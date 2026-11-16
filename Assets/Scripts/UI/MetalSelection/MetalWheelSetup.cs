using System;
using UnityEngine;
using UnityEngine.UI;

/// 遊戲開始時建立金屬輪盤：一個 Canvas + 金屬存量轉接（MetalReserves）+ 輪盤（MetalBurningWheel）。
public class MetalWheelSetup : MonoBehaviour
{
    [SerializeField] int sortingOrder = 20;   // 要比組員其他 UI 的 Canvas 高，輪盤才會蓋在上面

    void Start()
    {
        var go = new GameObject("MetalWheelCanvas", typeof(Canvas), typeof(CanvasScaler));
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        var reserves = go.AddComponent<MetalReserves>();
        var selector = MetalBurningWheel.Create(go.transform, reserves);

        // 玩家死亡時不能開輪盤
        var player = GameObject.FindGameObjectWithTag("Player");
        var health = player ? player.GetComponent<PlayerHealth>() : null;
        selector.canAct = () => health == null || !health.IsDead;
    }
}