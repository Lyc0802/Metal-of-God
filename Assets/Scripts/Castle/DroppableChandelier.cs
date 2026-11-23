using UnityEngine;

/// <summary>
/// 吊燈：玩家對它發動推 / 拉時，鎖鏈會斷掉，吊燈直接往下砸（推拉會立刻被中斷，所以是「垂直砸落」）。
/// 砸到的敵人受到重傷，聲響很大；蠟燭熄滅，那一帶變暗。落地後它就是一般的可推拉金屬，可以再推去撞別人。
/// </summary>
[RequireComponent(typeof(Rigidbody), typeof(MetalDisplayer), typeof(LooseMetalProp))]
public class DroppableChandelier : MonoBehaviour
{
    [Tooltip("鎖鏈（斷裂時隱藏）。")]
    public GameObject[] Chains;
    [Tooltip("蠟燭的燈光（落地後熄滅）。")]
    public Light[] CandleLights;
    [Tooltip("玩家的推 / 拉鎖定在吊燈上這麼久，鎖鏈就斷。")]
    [Min(0)] public float HoldTime = .12f;
    [Min(0)] public float SnapNoiseRadius = 14;

    public bool IsDetached { get; private set; }

    private MetalDisplayer metal;
    private Rigidbody body;
    private LooseMetalProp prop;
    private PlayerMetalManipulator manipulator;
    private float held;
    private bool snuffed;

    private void Awake()
    {
        metal = GetComponent<MetalDisplayer>();
        body = GetComponent<Rigidbody>();
        prop = GetComponent<LooseMetalProp>();
    }

    private void Update()
    {
        if (IsDetached)
        {
            return;
        }

        if (!manipulator)
        {
            manipulator = FindFirstObjectByType<PlayerMetalManipulator>();
        }

        bool targeted = manipulator && manipulator.IsLocked && manipulator.Target.Valid && manipulator.Target.Metal == metal;
        held = targeted ? held + Time.deltaTime : 0;
        if (held >= HoldTime)
        {
            Detach();
        }
    }

    public void Detach()
    {
        if (IsDetached)
        {
            return;
        }

        IsDetached = true;
        body.isKinematic = false;
        body.useGravity = true;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.linearVelocity = Vector3.down * 1.5f;
        if (Chains != null)
        {
            foreach (var chain in Chains)
            {
                if (chain)
                {
                    chain.SetActive(false);
                }
            }
        }

        prop.Arm(6f);
        NoiseSystem.Emit(transform.position, SnapNoiseRadius, .6f);
        // 垂直砸落：中斷這次的推 / 拉，不讓它被拖向玩家。
        if (manipulator)
        {
            manipulator.Release();
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!IsDetached || snuffed || collision.relativeVelocity.magnitude < 3f)
        {
            return;
        }

        snuffed = true;
        if (CandleLights != null)
        {
            foreach (var l in CandleLights)
            {
                if (l)
                {
                    l.enabled = false;
                }
            }
        }
    }
}
