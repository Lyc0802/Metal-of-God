using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 會影響潛行的光源（火把、火盆、吊燈蠟燭）。玩家站在越亮的地方，敵人越容易看見他；
/// 把火盆推倒、把吊燈砸下來，光源熄滅後那一帶就會變暗。
/// （檔名必須與類別名一致，Unity 才找得到這個元件的腳本。）
/// </summary>
[RequireComponent(typeof(Light))]
public class StealthLight : MonoBehaviour
{
    public static readonly List<StealthLight> Active = new List<StealthLight>();
    [Min(0)] public float Strength = 1;
    public Light Source { get; private set; }

    private void Awake()
    {
        Source = GetComponent<Light>();
    }

    private void OnEnable()
    {
        Active.Add(this);
    }

    private void OnDisable()
    {
        Active.Remove(this);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Active.Clear();
    }
}
