using System;
using UnityEngine;

[Serializable]
public class CombatEffectSettings
{
    public GameObject Prefab;
    public AudioClip Clip;
    [Range(0, 1)] public float Volume = .8f;
    [Range(.1f, 3)] public float Pitch = 1;
    [Min(1)] public float MaxDistance = 30;
    [Min(.01f)] public float Size = 1;
}

public enum CombatEffectKind { MetalImpact, SwordHit, EmotionalInfluence }

[DisallowMultipleComponent]
public class CombatVFXManager : MonoBehaviour
{
    public CombatEffectSettings MetalImpact = new CombatEffectSettings();
    public CombatEffectSettings SwordHit = new CombatEffectSettings();
    public CombatEffectSettings EmotionalInfluence = new CombatEffectSettings();
    [ColorUsage(true, true)] public Color ZincColor = new Color(2, .18f, .035f, 1);
    [ColorUsage(true, true)] public Color BrassColor = new Color(.03f, .8f, 2, 1);
    [Range(.5f, 2)] public float ZincSpeed = 1.2f, BrassSpeed = .85f;
    [Range(.5f, 2)] public float ZincPitch = 1.12f, BrassPitch = .88f;
    // Also useful to diagnostics without coupling tests to a particular particle layout.
    public event Action<CombatEffectKind, GameObject> EffectPlayed;
    public int MetalPlayCount { get; private set; }
    public int SwordPlayCount { get; private set; }
    public int EmotionalPlayCount { get; private set; }

    private void OnEnable()
    {
        CombatFeedbackEvents.MetalImpact += OnMetal;
        CombatFeedbackEvents.SwordHit += OnSword;
        CombatFeedbackEvents.EmotionalInfluence += OnEmotion;
    }
    private void OnDisable()
    {
        CombatFeedbackEvents.MetalImpact -= OnMetal;
        CombatFeedbackEvents.SwordHit -= OnSword;
        CombatFeedbackEvents.EmotionalInfluence -= OnEmotion;
    }
    private void OnMetal(Vector3 position, Vector3 normal, float speed)
    {
        float intensity = Mathf.InverseLerp(2.5f, 22, speed);
        Play(MetalImpact, CombatEffectKind.MetalImpact, position, normal,
            Mathf.Lerp(.75f, 1.6f, intensity), Mathf.Lerp(.92f, 1.08f, intensity), Mathf.Lerp(.65f, 1, intensity));
        MetalPlayCount++;
    }
    private void OnSword(Vector3 position, Vector3 normal)
    {
        Play(SwordHit, CombatEffectKind.SwordHit, position, normal);
        SwordPlayCount++;
    }
    private void OnEmotion(Vector3 position, EnemyActionType type)
    {
        bool zinc = type == EnemyActionType.Zinc;
        Play(EmotionalInfluence, CombatEffectKind.EmotionalInfluence, position, Vector3.up,
            1, zinc ? ZincPitch : BrassPitch, 1, zinc ? ZincColor : BrassColor, zinc ? ZincSpeed : BrassSpeed);
        EmotionalPlayCount++;
    }
    private void Play(CombatEffectSettings settings, CombatEffectKind kind, Vector3 position, Vector3 normal,
        float scale = 1, float pitch = 1, float volume = 1, Color? tint = null, float speed = 1)
    {
        if (!settings.Prefab) return;
        var instance = Instantiate(settings.Prefab, position,
            Quaternion.FromToRotation(Vector3.forward, normal.sqrMagnitude > .001f ? normal : Vector3.forward));
        instance.transform.localScale *= settings.Size * scale;
        float lifetime = .1f;
        foreach (var ps in instance.GetComponentsInChildren<ParticleSystem>())
        {
            var main = ps.main;
            if (tint.HasValue) main.startColor = tint.Value;
            main.simulationSpeed *= speed;
            lifetime = Mathf.Max(lifetime, (main.startDelay.constantMax + main.duration + main.startLifetime.constantMax) / Mathf.Max(.01f, main.simulationSpeed));
        }
        var source = instance.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 1;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = 1.5f;
        source.maxDistance = settings.MaxDistance;
        source.dopplerLevel = 0;
        source.pitch = Mathf.Clamp(settings.Pitch * pitch, .1f, 3);
        // Participate in the existing Tin hearing/SoundEmitter system too.
        var emitter = instance.AddComponent<SoundEmitter>();
        emitter.Source = source;
        emitter.LocalOffset = Vector3.zero;
        emitter.Play(settings.Clip, settings.Volume * volume);
        if (settings.Clip) lifetime = Mathf.Max(lifetime, settings.Clip.length / source.pitch);
        foreach (var ps in instance.GetComponentsInChildren<ParticleSystem>()) ps.Play(false);
        Destroy(instance, lifetime + .15f);
        EffectPlayed?.Invoke(kind, instance);
    }
}
