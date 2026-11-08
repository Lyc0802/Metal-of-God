using System;
using UnityEngine;

/// <summary>
/// 保存音源與聲音參數，讓 Tin 等感知能力套用統一的音效調整。
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class SoundEmitter : MonoBehaviour
{
    public static event Action<SoundEmitter, Vector3, float> Emitted;
    public AudioSource Source;
    public Vector3 LocalOffset = new Vector3(0, .06f, 0);
    [Min(0)]
    public float Strength = 1;
    [Min(.02f)]
    public float SampleInterval = .06f;
    [Min(0)]
    public float SilenceThreshold = .0005f;
    [Min(.001f)]
    public float ReferenceRms = .08f;
    public Vector3 WorldPosition => (Source ? Source.transform : transform).TransformPoint(LocalOffset);
    public float LastOutputLevel { get; private set; }

    private readonly float[] samples = new float[256];
    private float nextSample;
    private void Reset()
    {
        Source = GetComponent<AudioSource>();
    }

    private void Awake()
    {
        if (!Source)
        {
            Source = GetComponent<AudioSource>();
        }
    }

    private void OnEnable()
    {
        nextSample = 0;
    }

    private void OnDisable()
    {
        LastOutputLevel = 0;
    }

    public void Play(AudioClip clip, float volume = 1)
    {
        if (!isActiveAndEnabled || !Source || !Source.isActiveAndEnabled || !clip || volume <= 0)
        {
            return;
        }

        Source.PlayOneShot(clip, Mathf.Clamp01(volume));
    }

    private void Update()
    {
        if (Time.unscaledTime < nextSample)
        {
            return;
        }

        nextSample = Time.unscaledTime + Mathf.Max(.02f, SampleInterval);
        SamplePlayback();
    }

    public bool SamplePlayback()
    {
        LastOutputLevel = 0;
        if (!isActiveAndEnabled || !Source || !Source.isActiveAndEnabled || !Source.isPlaying || Source.mute
            || Source.volume <= 0
            || AudioListener.volume <= 0
            || (AudioListener.pause
            && !Source.ignoreListenerPause))
        {
            return false;
        }

        // isPlaying alone would also report a silent clip. Inspect a reused buffer of actual output samples.
        Source.GetOutputData(samples, 0);
        float sum = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            sum += samples[i] * samples[i];
        }

        LastOutputLevel = Mathf.Sqrt(sum / samples.Length);
        if (LastOutputLevel <= Mathf.Max(0, SilenceThreshold))
        {
            return false;
        }

        float strength = Mathf.Clamp01(LastOutputLevel / Mathf.Max(.001f, ReferenceRms)) * Mathf.Max(0, Strength);
        if (strength <= 0)
        {
            return false;
        }

        Emitted?.Invoke(this, WorldPosition, strength);
        return true;
    }
}
