using UnityEngine;

/// <summary>
/// 金屬火盆：被推倒（傾斜）或被重重撞擊時火就熄滅，那一帶會變暗，敵人更難發現你。
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class ToppleBrazier : MonoBehaviour
{
    public Light FireLight;
    public GameObject[] Flames;
    [Tooltip("火盆的「上方向」的 Y 分量低於這個值就視為倒下。")]
    [Range(0, 1)] public float ExtinguishTilt = .6f;
    [Tooltip("撞擊速度超過這個值也會熄滅。")]
    [Min(0)] public float ExtinguishImpactSpeed = 7;

    public bool IsLit { get; private set; } = true;

    private void Update()
    {
        if (IsLit && transform.up.y < ExtinguishTilt)
        {
            Extinguish();
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (IsLit && collision.relativeVelocity.magnitude >= ExtinguishImpactSpeed)
        {
            Extinguish();
        }
    }

    public void Extinguish()
    {
        if (!IsLit)
        {
            return;
        }

        IsLit = false;
        if (FireLight)
        {
            FireLight.enabled = false;
        }

        if (Flames != null)
        {
            foreach (var flame in Flames)
            {
                if (flame)
                {
                    flame.SetActive(false);
                }
            }
        }
    }
}
