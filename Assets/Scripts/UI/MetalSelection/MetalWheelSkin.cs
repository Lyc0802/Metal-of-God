using UnityEngine;

/// <summary>
/// 3D 輪盤的外觀設定：在 Inspector 拖入材質與圖片。
/// 放在場景裡任一物件上（建議跟 MetalWheelSetup 同一個物件），MetalWheel3D 建立時會自動找到它。
/// 沒有指定的欄位會用預設的純色材質，方便先看形狀。
/// </summary>
public class MetalWheelSkin : MonoBehaviour
{
    [Header("材質（URP Lit 等 3D 材質都可以）")]
    [Tooltip("每一塊的外圈（例如皮革）")]
    public Material outerMaterial;
    [Tooltip("每一塊的內圈（比較黑的材質）。它的長度代表存量，燃燒時會發光")]
    public Material innerMaterial;
    [Tooltip("正中央的圓（胃）")]
    public Material centerMaterial;
    [Tooltip("最底下的大圓（鐵輪盤底座）")]
    public Material backgroundMaterial;

    [Header("每一塊自己的材質（順序：Iron, Steel, Zinc, Brass, Tin, Pewter, Copper, Bronze）")]
    [Tooltip("有填的那一塊用這裡的外圈材質；空著的用上面共用的 Outer Material")]
    public Material[] outerMaterials = new Material[8];
    [Tooltip("有填的那一塊用這裡的內圈材質；空著的用上面共用的 Inner Material")]
    public Material[] innerMaterials = new Material[8];

    [Header("每一塊中間的圖片（順序：Iron, Steel, Zinc, Brass, Tin, Pewter, Copper, Bronze）")]
    public Texture2D[] icons = new Texture2D[8];
    [Range(0.1f, 0.6f), Tooltip("圖片大小，相對於扇形的寬度")]
    public float iconSize = 0.32f;

    [Header("形狀（以輪盤半徑 = 1 為單位）")]
    [Range(0.8f, 1.4f)] public float backgroundRadius = 1.12f;
    [Range(0.01f, 0.2f)] public float outerThickness = 0.06f;
    [Range(0.01f, 0.2f), Tooltip("要比 Outer Thickness 薄：兩者的差就是內圈凹進外框的深度")]
    public float innerThickness = 0.03f;
    [Range(0f, 0.15f), Tooltip("外框的寬度：內圈嵌在外框中間的凹槽裡")]
    public float innerInset = 0.04f;
    [Range(0f, 10f), Tooltip("扇形之間的縫隙（度）")]
    public float gapDegrees = 1.5f;

    [Header("貼圖重複次數（材質的紋理太大或太小時調整）")]
    public float textureTiling = 1f;
}