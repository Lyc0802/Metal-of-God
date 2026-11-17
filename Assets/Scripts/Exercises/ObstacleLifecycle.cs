using UnityEngine;

[RequireComponent(typeof(Rigidbody), typeof(BoxCollider))]
public sealed class ObstacleLifecycle : MonoBehaviour
{
    [HideInInspector] public ObstacleSpawner Owner;
    public bool IsHit { get; private set; }
    public float HitTime { get; private set; }

    private void OnCollisionEnter(Collision collision)
    {
        var other = collision.rigidbody
            ? collision.rigidbody.GetComponent<ObstacleLifecycle>() : null;
        bool player = collision.rigidbody && collision.rigidbody.GetComponent<PlayerMovement>();
        if (!player && !(other && other.IsHit)) return;
        if (!IsHit)
        {
            IsHit = true;
            HitTime = Time.time;
        }
    }

    private void Update()
    {
        if (IsHit && Time.time - HitTime >= 2f && Owner) Owner.Replace(this);
    }
}
