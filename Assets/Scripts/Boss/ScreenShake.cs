using UnityEngine;

/// <summary>
/// 鏡頭震動。在 PlayerController 更新鏡頭之後套用偏移，下一幀開頭還原；用 ScreenShake.Play 呼叫。
/// 玩家瞄準金屬時，幅度與頻率都大幅降低，避免影響瞄準。
/// </summary>
[DefaultExecutionOrder(1000)]
public class ScreenShake : MonoBehaviour
{
    private const float NormalFrequency = 30, AimFrequency = 4, AimAmplitude = .12f;
    private float magnitude, duration, elapsed, nextSample;
    private Vector3 applied, target, current;
    private PlayerController player;

    public static void Play(float magnitude, float duration)
    {
        var cam = Camera.main;
        if (!cam)
        {
            return;
        }

        var shake = cam.GetComponent<ScreenShake>();
        if (!shake)
        {
            shake = cam.gameObject.AddComponent<ScreenShake>();
        }

        // 較強的震動優先，不會被較弱的覆蓋。
        if (magnitude >= shake.CurrentMagnitude)
        {
            shake.magnitude = magnitude;
            shake.duration = Mathf.Max(.05f, duration);
            shake.elapsed = 0;
        }
    }

    private float CurrentMagnitude => elapsed < duration ? magnitude * (1 - elapsed / duration) : 0;

    private void Update()
    {
        transform.position -= applied;
        applied = Vector3.zero;
    }

    private void LateUpdate()
    {
        if (elapsed >= duration)
        {
            current = target = Vector3.zero;
            return;
        }

        if (!player)
        {
            player = FindFirstObjectByType<PlayerController>();
        }

        bool aiming = player && player.IsSelectingMetal;
        float frequency = aiming ? AimFrequency : NormalFrequency;
        float amplitude = CurrentMagnitude * (aiming ? AimAmplitude : 1);
        elapsed += Time.unscaledDeltaTime;
        // 以固定頻率取新的隨機目標，並平滑趨近：頻率低時是緩慢的晃動而非抖動。
        if (Time.unscaledTime >= nextSample)
        {
            nextSample = Time.unscaledTime + 1f / frequency;
            target = Random.insideUnitSphere;
        }

        current = Vector3.MoveTowards(current, target, frequency * 2 * Time.unscaledDeltaTime);
        applied = current * amplitude;
        transform.position += applied;
    }
}
