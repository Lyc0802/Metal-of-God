using System.Collections.Generic;
using UnityEngine;

public class SpeedFloorEffect : BaseFloorEffect
{
    [Min(0)]
    [Tooltip("Speed multiplier while touching this floor: 2 = double, 0.3 = slow, 1 = unchanged.")]
    public float SpeedMultiplier = 2f;

    private sealed class SpeedState
    {
        public float WalkSpeed;
        public float SprintSpeed;
        public readonly Dictionary<SpeedFloorEffect, float> Multipliers = new Dictionary<SpeedFloorEffect, float>();
    }

    private static readonly Dictionary<PlayerMovement, SpeedState> states = new Dictionary<PlayerMovement, SpeedState>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStates() => states.Clear();

    protected override void OnPlayerEnter(PlayerMovement player)
    {
        if (!states.TryGetValue(player, out var state))
        {
            state = new SpeedState { WalkSpeed = player.MovementSpeed, SprintSpeed = player.SprintSpeed };
            states.Add(player, state);
        }

        state.Multipliers[this] = Mathf.Max(0, SpeedMultiplier);
        ApplySpeed(player, state);
    }

    protected override void OnPlayerExit(PlayerMovement player)
    {
        if (!states.TryGetValue(player, out var state)) return;
        state.Multipliers.Remove(this);
        ApplySpeed(player, state);
        if (state.Multipliers.Count == 0) states.Remove(player);
    }

    private static void ApplySpeed(PlayerMovement player, SpeedState state)
    {
        // Recompute from the original speeds so overlaps and zero multipliers restore correctly.
        float multiplier = 1f;
        foreach (float value in state.Multipliers.Values) multiplier *= value;
        player.MovementSpeed = state.WalkSpeed * multiplier;
        player.SprintSpeed = state.SprintSpeed * multiplier;
    }
}
