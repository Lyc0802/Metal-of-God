using UnityEngine;

/// <summary>以場景中所有 StealthLight 計算某個位置被光照到的程度（0~1）。</summary>
public static class StealthLighting
{
    /// <summary>環境光的基本曝光（完全黑暗的地方也不是 0）。</summary>
    public const float Ambient = .1f;

    public static float Exposure(Vector3 point)
    {
        float exposure = Ambient;
        foreach (var light in StealthLight.Active)
        {
            if (!light || !light.Source || !light.Source.enabled || light.Source.intensity <= 0)
            {
                continue;
            }

            float range = light.Source.range;
            float distance = Vector3.Distance(point, light.Source.transform.position);
            if (distance >= range)
            {
                continue;
            }

            float k = 1f - distance / range;
            exposure += k * k * light.Strength * (light.Source.intensity / 3f);
        }

        return Mathf.Clamp01(exposure);
    }
}
