using System.Collections.Generic;
using UnityEngine;

/// <summary>Attach to a floor with a Collider. Supports solid floors and trigger areas.</summary>
public abstract class BaseFloorEffect : MonoBehaviour
{
    private readonly Dictionary<Collider, PlayerMovement> contacts = new Dictionary<Collider, PlayerMovement>();

    protected void OnCollisionEnter(Collision collision) => Enter(collision.collider);
    protected void OnCollisionStay(Collision collision) => Enter(collision.collider);
    protected void OnCollisionExit(Collision collision) => Exit(collision.collider);
    protected void OnTriggerEnter(Collider other) => Enter(other);
    protected void OnTriggerStay(Collider other) => Enter(other);
    protected void OnTriggerExit(Collider other) => Exit(other);

    private void Enter(Collider other)
    {
        // Unity can deliver collision messages to disabled behaviours.
        if (!isActiveAndEnabled || contacts.ContainsKey(other)) return;

        var player = other.attachedRigidbody
            ? other.attachedRigidbody.GetComponent<PlayerMovement>()
            : other.GetComponentInParent<PlayerMovement>();
        if (!player) return;

        bool alreadyTouching = contacts.ContainsValue(player);
        contacts.Add(other, player);
        if (!alreadyTouching) OnPlayerEnter(player);
    }

    private void Exit(Collider other)
    {
        if (!contacts.TryGetValue(other, out var player)) return;
        contacts.Remove(other);
        if (player && !contacts.ContainsValue(player)) OnPlayerExit(player);
    }

    protected virtual void OnDisable()
    {
        // Restore effects even when the floor is disabled or destroyed during contact.
        var players = new HashSet<PlayerMovement>(contacts.Values);
        contacts.Clear();
        foreach (var player in players)
        {
            if (player) OnPlayerExit(player);
        }
    }

    protected abstract void OnPlayerEnter(PlayerMovement player);
    protected virtual void OnPlayerExit(PlayerMovement player) { }
}
