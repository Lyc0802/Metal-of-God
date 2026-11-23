using UnityEngine;

/// <summary>
/// 大門被敲擊會發出聲響（附近守衛過來查看）；被打破的瞬間全場警報，所有敵人一起衝來。
/// </summary>
[RequireComponent(typeof(BreakableObstacle))]
public class BreakableNoise : MonoBehaviour
{
    [Min(0)] public float HitNoiseRadius = 24;
    [Min(0)] public float BreakAlertRadius = 60;

    private BreakableObstacle obstacle;
    private float lastHealth;
    private bool broken;
    private Vector3 position;

    private void Awake()
    {
        obstacle = GetComponent<BreakableObstacle>();
        lastHealth = obstacle.CurrentHealth;
        position = transform.position;
    }

    private void Update()
    {
        if (broken)
        {
            return;
        }

        if (obstacle.CurrentHealth < lastHealth - .01f)
        {
            lastHealth = obstacle.CurrentHealth;
            NoiseSystem.Emit(transform.position, HitNoiseRadius, 1f);
        }

        if (obstacle.IsBroken)
        {
            Broke();
        }
    }

    private void Broke()
    {
        broken = true;
        NoiseSystem.Emit(position, BreakAlertRadius, 1f);
        AlertNetwork.Raise(position, BreakAlertRadius);
    }

    private void OnDestroy()
    {
        // BreakableObstacle 打破時會直接 Destroy 物件，Update 可能來不及跑；
        // 這裡只讀純 C# 屬性，不碰已被銷毀的 Unity 物件。
        if (!broken && Application.isPlaying && (object)obstacle != null && obstacle.IsBroken)
        {
            Broke();
        }
    }
}
