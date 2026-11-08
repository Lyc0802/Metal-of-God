using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Player 與 Enemy 共用的金屬訊號查詢契約。
/// </summary>
public interface IMetalAbilityStatus
{
    Transform MetalOwner { get; }

    Vector3 MetalSignalPosition { get; }

    bool IsMetalStatusActive { get; }

    bool IsMetalEnabled(MetalType metal);
    bool IsUsingAnyMetal { get; }
}

// Detection providers report a signal; the AI retains ownership of its state.
/// <summary>
/// 能力偵測只回報能否感知目標，讓敵人 AI 自行決定後續行為。
/// </summary>
public interface IEnemyDetectionSource
{
    bool CanDetect(IMetalAbilityStatus target);
}

/// <summary>
/// 維護啟用角色的訊號登錄，並確保角色具備 Copper/Bronze 共用服務。
/// </summary>
public static class MetalAbilitySignals
{
    private static readonly HashSet<IMetalAbilityStatus> actors = new HashSet<IMetalAbilityStatus>();
    public static IEnumerable<IMetalAbilityStatus> Actors => actors;

    public static bool IsRealMetal(MetalType metal) => metal >= MetalType.Iron && metal <= MetalType.Bronze;
    public static void Register(IMetalAbilityStatus actor)
    {
        actors.Add(actor);
    }

    public static void Unregister(IMetalAbilityStatus actor)
    {
        actors.Remove(actor);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        actors.Clear();
    }

    public static void EnsureServices(Component owner, bool player)
    {
        if (!owner.GetComponent<CopperSuppression>())
        {
            owner.gameObject.AddComponent<CopperSuppression>();
        }

        if (!owner.GetComponent<BronzeDetection>())
        {
            var bronze = owner.gameObject.AddComponent<BronzeDetection>();
            bronze.ShowOutlines = player;
        }
    }
}
