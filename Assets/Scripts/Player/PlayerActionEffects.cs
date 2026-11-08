using UnityEngine;

/// <summary>
/// 依照移動與戰鬥事件播放玩家音效及視覺特效。
/// </summary>
[RequireComponent(typeof(PlayerMovement), typeof(PlayerCombat), typeof(PlayerPewterAbility))]
public class PlayerActionEffects : MonoBehaviour
{
    [Header("Sword - real blade trail + Slash VFX Pack sparks")]
    public SwordSwingTrail SwordTrail;
    public ParticleSystem SwordSparks;
    public AudioSource SwordAudio;
    public Transform SwordTip;
    [Min(.05f)]
    public float SlashDuration = .28f;
    [Range(.5f, 2)]
    public float SwordPitch = 1.25f;
    [Range(0, .2f)]
    public float SwordPitchVariation = .06f;
    [Header("Successful jump - takeoff burst")]
    public ParticleSystem JumpBurst;
    public AudioSource JumpAudio;
    [Header("Pewter - continuous aura and loop")]
    public ParticleSystem PewterAura;
    public AudioSource PewterAudio;
    private PlayerMovement movement;
    private PlayerCombat combat;
    private PlayerPewterAbility pewter;
    private PlayerHealth health;
    private float slashRemaining;
    private bool pewterPlaying;
    private void OnEnable()
    {
        movement = GetComponent<PlayerMovement>();
        combat = GetComponent<PlayerCombat>();
        pewter = GetComponent<PlayerPewterAbility>();
        health = GetComponent<PlayerHealth>();
        if (SwordTrail)
        {
            SwordTrail.Combat = combat;
        }

        combat.SwingEffectTriggered += PlaySword;
        combat.SwingCancelled += StopSword;
        movement.Jumped += PlayJump;
        RefreshPewter();
    }

    private void OnDisable()
    {
        if (combat)
        {
            combat.SwingEffectTriggered -= PlaySword;
            combat.SwingCancelled -= StopSword;
        }

        if (movement)
        {
            movement.Jumped -= PlayJump;
        }

        StopSword();
        if (JumpBurst)
        {
            JumpBurst.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        if (JumpAudio)
        {
            JumpAudio.Stop();
        }

        SetPewter(false);
    }

    public void PlaySword()
    {
        if (!isActiveAndEnabled || (health && health.IsDead))
        {
            return;
        }

        if (SwordSparks)
        {
            SwordSparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        if (SwordAudio)
        {
            SwordAudio.Stop();
        }

        slashRemaining = Mathf.Max(.05f, SlashDuration);
        UpdateSwordEmitter();
        if (SwordSparks)
        {
            SwordSparks.Clear(true);
            SwordSparks.Play(true);
        }

        if (SwordAudio && SwordAudio.clip)
        {
            SwordAudio.pitch = Random.Range(SwordPitch - SwordPitchVariation, SwordPitch + SwordPitchVariation);
            SwordAudio.Play();
        }
    }

    private void StopSword()
    {
        slashRemaining = 0;
        if (SwordTrail)
        {
            SwordTrail.ClearTrail();
        }

        if (SwordSparks)
        {
            SwordSparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        if (SwordAudio)
        {
            SwordAudio.Stop();
        }
    }

    public void PlayJump()
    {
        if (!isActiveAndEnabled || (health && health.IsDead))
        {
            return;
        }

        if (JumpBurst)
        {
            var collider = GetComponent<Collider>();
            var origin = transform.position;
            if (collider)
            {
                origin.y = collider.bounds.min.y + .04f;
            }

            JumpBurst.transform.position = origin;
            JumpBurst.Clear(true);
            JumpBurst.Play(true);
        }

        if (JumpAudio && JumpAudio.clip)
        {
            JumpAudio.pitch = Random.Range(.96f, 1.04f);
            JumpAudio.Play();
        }
    }

    private void UpdateSwordEmitter()
    {
        if (!SwordTip)
        {
            return;
        }

        if (SwordSparks)
        {
            SwordSparks.transform.SetPositionAndRotation(SwordTip.position, SwordTip.rotation);
        }

        if (SwordAudio)
        {
            SwordAudio.transform.position = SwordTip.position;
        }
    }

    private void LateUpdate()
    {
        if (combat && combat.IsAttacking)
        {
            UpdateSwordEmitter();
        }
    }

    private void SetPewter(bool active)
    {
        pewterPlaying = active;
        if (active)
        {
            if (PewterAura)
            {
                PewterAura.Play(true);
            }

            if (PewterAudio && PewterAudio.clip)
            {
                PewterAudio.loop = true;
                PewterAudio.Play();
            }
        }
        else
        {
            if (PewterAura)
            {
                PewterAura.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            if (PewterAudio)
            {
                PewterAudio.Stop();
            }
        }
    }

    public void RefreshPewter()
    {
        bool active = isActiveAndEnabled && pewter && pewter.IsActive;
        if (active != pewterPlaying)
        {
            SetPewter(active);
        }
    }

    private void Update()
    {
        RefreshPewter();
        if (slashRemaining <= 0)
        {
            return;
        }

        slashRemaining = Mathf.Max(0, slashRemaining - Time.deltaTime);
        if (slashRemaining <= 0 || (health && health.IsDead))
        {
            StopSword();
        }
    }
}
