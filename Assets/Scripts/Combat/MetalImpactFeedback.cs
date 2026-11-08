using System.Collections.Generic;
using UnityEngine;

// Added only to a MetalDisplayer's physical body, never to arbitrary rigidbodies.
[DisallowMultipleComponent, RequireComponent(typeof(Rigidbody))]
public class MetalImpactFeedback : MonoBehaviour
{
    [Min(.01f)] public float MinimumImpactSpeed = 2.5f;
    [Min(.01f)] public float PerEnemyCooldown = .3f;
    [Min(.1f)] public float RestResetDelay = .75f;
    public bool IsPlayerDriven { get; private set; }
    private Rigidbody body;
    private float resting;
    private readonly Dictionary<Enemy, float> nextHit = new Dictionary<Enemy, float>();

    private void Awake() => body = GetComponent<Rigidbody>();

    public static void MarkDriven(Rigidbody target, MetalDisplayer metal)
    {
        if (!target || !metal || target.isKinematic) return;
        var feedback = target.GetComponent<MetalImpactFeedback>();
        if (!feedback) feedback = target.gameObject.AddComponent<MetalImpactFeedback>();
        feedback.IsPlayerDriven = true;
        feedback.resting = 0;
    }

    private void FixedUpdate()
    {
        if (!IsPlayerDriven) return;
        resting = body.linearVelocity.sqrMagnitude < .04f ? resting + Time.fixedDeltaTime : 0;
        if (resting >= RestResetDelay) IsPlayerDriven = false;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!IsPlayerDriven || collision.contactCount == 0) return;
        var enemy = collision.collider.GetComponentInParent<Enemy>();
        float speed = collision.relativeVelocity.magnitude;
        if (!enemy || !enemy.isActiveAndEnabled || enemy.IsDead || speed < MinimumImpactSpeed) return;
        if (nextHit.TryGetValue(enemy, out float next) && Time.time < next) return;
        // OnCollisionStay deliberately does not emit: resting contact cannot retrigger.
        nextHit[enemy] = Time.time + PerEnemyCooldown;
        var contact = collision.GetContact(0);
        CombatFeedbackEvents.PublishMetalImpact(contact.point, contact.normal, speed);
    }

    private void OnDisable()
    {
        IsPlayerDriven = false;
        nextHit.Clear();
    }
}
