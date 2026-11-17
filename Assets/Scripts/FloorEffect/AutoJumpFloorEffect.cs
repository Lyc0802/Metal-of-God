using UnityEngine;

public class AutoJumpFloorEffect : BaseFloorEffect
{
    [Min(1.01f)]
    [Tooltip("Upward launch speed relative to the player's current normal jump, including Pewter.")]
    public float JumpMultiplier = 1.5f;

    protected override void OnPlayerEnter(PlayerMovement player)
    {
        var body = player.GetComponent<Rigidbody>();
        if (!body || body.isKinematic) return;

        var velocity = body.linearVelocity;
        // Replace downward speed so falling onto the pad still produces a full launch.
        // Preserve horizontal movement and any upward speed already exceeding this launch.
        velocity.y = Mathf.Max(velocity.y, player.CurrentJumpForce * Mathf.Max(1.01f, JumpMultiplier));
        body.linearVelocity = velocity;
    }
}
