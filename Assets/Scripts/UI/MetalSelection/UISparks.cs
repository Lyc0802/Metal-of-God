using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// 
/// UI 上的粒子特效（自己寫的小型粒子系統）。
/// Screen Space Overlay 的 Canvas 不能直接顯示 ParticleSystem，
/// 所以用很多個小方塊 Image：每顆有速度、壽命，往外飛、受重力、慢慢變透明、縮小。
/// 選到 / 燃燒金屬時在那個扇形上爆開。
/// 
public class UISparks : MonoBehaviour
{
    class Spark
    {
        public RectTransform rt;
        public Image img;
        public Vector2 velocity;
        public float life, maxLife;
        public Color color;
    }

    readonly List<Spark> alive = new List<Spark>();
    readonly Stack<Spark> pool = new Stack<Spark>();   // 用完的粒子放回池子重複使用，避免一直產生新物件

    public static UISparks Create(Transform parent)
    {
        var go = new GameObject("UISparks", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = Vector2.zero;
        return go.AddComponent<UISparks>();
    }

    /// 在 localPos（這個物件的區域座標）爆出 count 顆粒子。
    public void Burst(Vector2 localPos, Color color, int count = 28, float speed = 260f)
    {
        for (int i = 0; i < count; i++)
        {
            var s = pool.Count > 0 ? pool.Pop() : NewSpark();
            s.rt.gameObject.SetActive(true);
            s.rt.anchoredPosition = localPos;
            float ang = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            s.velocity = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * speed * Random.Range(0.35f, 1f);
            s.maxLife = s.life = Random.Range(0.35f, 0.8f);
            s.color = Color.Lerp(color, Color.white, Random.Range(0f, 0.6f));   // 混一點白色，比較像火花
            s.rt.localRotation = Quaternion.Euler(0, 0, Random.Range(0f, 90f));
            alive.Add(s);
        }
    }

    Spark NewSpark()
    {
        var go = new GameObject("Spark", typeof(RectTransform), typeof(CanvasRenderer));
        go.transform.SetParent(transform, false);
        var img = go.AddComponent<Image>();
        img.raycastTarget = false;
        return new Spark { rt = (RectTransform)go.transform, img = img };
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;     // 選輪盤時遊戲會變慢動作，特效仍用真實時間
        for (int i = alive.Count - 1; i >= 0; i--)
        {
            var s = alive[i];
            s.life -= dt;
            if (s.life <= 0f)
            {
                s.rt.gameObject.SetActive(false);
                alive.RemoveAt(i);
                pool.Push(s);
                continue;
            }
            float k = s.life / s.maxLife;                        // 1 → 0
            s.velocity += Vector2.down * 380f * dt;              // 重力
            s.velocity *= 1f - 1.5f * dt;                         // 空氣阻力
            s.rt.anchoredPosition += s.velocity * dt;
            float size = Mathf.Lerp(2f, 9f, k);
            s.rt.sizeDelta = new Vector2(size, size);
            var c = s.color;
            c.a = k;
            s.img.color = c;
        }
    }
}
