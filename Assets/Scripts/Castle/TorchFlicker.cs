using UnityEngine;

/// <summary>火把 / 火盆的燈光閃爍。</summary>
[RequireComponent(typeof(Light))]
public class TorchFlicker : MonoBehaviour
{
    [Range(0, 1)] public float Amount = .25f;
    [Min(.1f)] public float Speed = 6;

    private Light torch;
    private float baseIntensity, seed;

    private void Awake()
    {
        torch = GetComponent<Light>();
        baseIntensity = torch.intensity;
        seed = Random.value * 100f;
    }

    private void Update()
    {
        float n = Mathf.PerlinNoise(seed, Time.time * Speed) * 2f - 1f;
        torch.intensity = baseIntensity * (1f + n * Amount);
    }
}
