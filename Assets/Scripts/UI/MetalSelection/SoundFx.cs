using UnityEngine;

/// 
/// 程式合成的音效（不需要音訊檔）。每個音效就是「頻率隨時間變化的波形 × 音量包絡」。
/// 用法：SoundFx.Play(SoundFx.Kind.Burn)
/// 
public class SoundFx : MonoBehaviour
{
    public enum Kind { Open, Close, Tick, Burn, Extinguish, Empty, Hit, Pickup, Push, Pull, Riot, Soothe }

    static SoundFx instance;
    AudioSource source;
    AudioClip[] clips;
    const int Rate = 22050;

    public static void Create(Transform parent)
    {
        var go = new GameObject("SoundFx");
        go.transform.SetParent(parent, false);
        instance = go.AddComponent<SoundFx>();
        instance.source = go.AddComponent<AudioSource>();
        instance.source.playOnAwake = false;
        instance.clips = new[]
        {
            Tone(0.18f, t => Mathf.Lerp(300f, 700f, t), 0.25f, noise: 0.3f),     // Open：往上滑
            Tone(0.15f, t => Mathf.Lerp(600f, 280f, t), 0.2f, noise: 0.3f),      // Close：往下滑
            Tone(0.03f, t => 1500f, 0.12f),                                       // Tick：指到新的扇形
            Tone(0.45f, t => Mathf.Lerp(180f, 520f, t * t), 0.4f, noise: 0.6f),  // Burn：點燃，帶雜訊的上升音
            Tone(0.35f, t => Mathf.Lerp(420f, 120f, t), 0.3f, noise: 0.4f),      // Extinguish：熄滅
            Tone(0.25f, t => 110f, 0.3f, square: true),                           // Empty：低沉的錯誤音
            Tone(0.25f, t => Mathf.Lerp(220f, 70f, t), 0.5f, square: true, noise: 0.5f), // Hit：受傷
            Tone(0.4f, t => t < 0.5f ? 880f : 1320f, 0.3f),                       // Pickup：兩個音的叮咚
            Tone(0.3f, t => Mathf.Lerp(140f, 420f, t), 0.5f, noise: 0.35f),       // Push：往外，音往上
            Tone(0.3f, t => Mathf.Lerp(420f, 140f, t), 0.5f, noise: 0.35f),       // Pull：往內，音往下
            Tone(0.4f, t => 300f + 120f * Mathf.Sin(t * 60f), 0.35f, square: true), // Riot：刺耳的顫音
            Tone(0.5f, t => Mathf.Lerp(520f, 390f, t), 0.3f),                     // Soothe：柔和下降
        };
    }

    public static void Play(Kind k, float volume = 1f)
    {
        if (instance == null) return;
        instance.source.PlayOneShot(instance.clips[(int)k], volume);
    }

    /// 
    /// 產生一段聲音：freqAt(t) 給出 0..1 時間點的頻率；square 用方波；noise 混入白雜訊（燃燒的嘶嘶聲）。
    /// 包絡：一開始很快變大聲，之後二次方衰減，聽起來比較自然、不會爆音。
    /// 
    static AudioClip Tone(float seconds, System.Func<float, float> freqAt, float volume, bool square = false, float noise = 0f)
    {
        int n = Mathf.CeilToInt(seconds * Rate);
        var data = new float[n];
        var rng = new System.Random(n);
        float phase = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / n;
            phase += 2f * Mathf.PI * freqAt(t) / Rate;
            float s = Mathf.Sin(phase);
            if (square) s = Mathf.Sign(s) * 0.6f;
            s = Mathf.Lerp(s, (float)rng.NextDouble() * 2f - 1f, noise);
            float env = Mathf.Min(1f, t * 30f) * (1f - t) * (1f - t);
            data[i] = s * env * volume;
        }
        var clip = AudioClip.Create("sfx", n, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
