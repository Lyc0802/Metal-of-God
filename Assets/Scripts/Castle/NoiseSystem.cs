using System;
using UnityEngine;

/// <summary>
/// 場景中的「聲響」：金屬碰撞、吊燈砸落、敲門等會讓附近的敵人過去查看（EnemySenses 負責接收）。
/// </summary>
public static class NoiseSystem
{
    /// <summary>(位置, 傳播半徑, 響度)</summary>
    public static event Action<Vector3, float, float> Heard;

    public static void Emit(Vector3 position, float radius, float loudness = 1f)
    {
        if (radius > 0)
        {
            Heard?.Invoke(position, radius, loudness);
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        Heard = null;
    }
}

/// <summary>
/// 警報網路：有敵人發現玩家（或同伴被打）時，附近的敵人會一起進入追擊。
/// 這是「直接衝進去會被圍攻」的來源。
/// </summary>
public static class AlertNetwork
{
    /// <summary>(位置, 傳播半徑)</summary>
    public static event Action<Vector3, float> Raised;

    public static void Raise(Vector3 position, float radius)
    {
        if (radius > 0)
        {
            Raised?.Invoke(position, radius);
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        Raised = null;
    }
}
