using System;
using UnityEngine;

// Interaction code publishes facts; presentation is owned by CombatVFXManager.
public static class CombatFeedbackEvents
{
    public static event Action<Vector3, Vector3, float> MetalImpact;
    public static event Action<Vector3, Vector3> SwordHit;
    public static event Action<Vector3, EnemyActionType> EmotionalInfluence;

    public static void PublishMetalImpact(Vector3 point, Vector3 normal, float speed) => MetalImpact?.Invoke(point, normal, speed);
    public static void PublishSwordHit(Vector3 point, Vector3 normal) => SwordHit?.Invoke(point, normal);
    public static void PublishEmotionalInfluence(Vector3 point, EnemyActionType type) => EmotionalInfluence?.Invoke(point, type);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetEvents()
    {
        MetalImpact = null;
        SwordHit = null;
        EmotionalInfluence = null;
    }
}
