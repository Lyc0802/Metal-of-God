using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 玩家死亡後：畫面慢慢淡出成全黑，然後重新載入目前的場景，新場景再從黑畫面淡入。
/// （沒有這個的話，死亡瞬間畫面暗掉、重新載入後又突然變亮，轉換很突兀。）
/// </summary>
[RequireComponent(typeof(PlayerHealth))]
public class DeathReload : MonoBehaviour
{
    [Tooltip("死亡後先等多久才開始淡出（讓死亡動作播完）。")]
    [Min(0)] public float Delay = 1.4f;
    [Min(.1f)] public float FadeOut = 1.2f;
    [Min(.1f)] public float FadeIn = 1.2f;

    private PlayerHealth health;
    private bool started;

    private void OnEnable()
    {
        health = GetComponent<PlayerHealth>();
        health.Died += OnDied;
        if (health.IsDead)
        {
            OnDied();
        }
    }

    private void OnDisable()
    {
        if (health)
        {
            health.Died -= OnDied;
        }
    }

    private void OnDied()
    {
        if (started)
        {
            return;
        }

        started = true;
        // 協程掛在獨立物件上，玩家物件之後被停用也不會中斷。
        var fader = Fader.Create(0f);
        fader.StartCoroutine(Run(fader));
    }

    private IEnumerator Run(Fader fader)
    {
        float t = 0;
        while (t < Delay)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }

        yield return fader.Fade(1f, FadeOut, false);
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        // 遮罩物件在場景切換後仍然存在（DontDestroyOnLoad），這個協程會繼續：等新場景起來再淡入。
        yield return null;
        yield return null;
        yield return fader.Fade(0f, FadeIn, true);
    }

    /// <summary>全螢幕黑色遮罩（用不受 timeScale 影響的時間淡入 / 淡出）。</summary>
    private class Fader : MonoBehaviour
    {
        private Image image;

        public static Fader Create(float alpha)
        {
            var go = new GameObject("Death Fade");
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5000;
            var fader = go.AddComponent<Fader>();
            var imageObject = new GameObject("Black", typeof(RectTransform));
            imageObject.transform.SetParent(go.transform, false);
            var rect = (RectTransform)imageObject.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            fader.image = imageObject.AddComponent<Image>();
            fader.image.color = new Color(0, 0, 0, alpha);
            fader.image.raycastTarget = false;
            if (Application.isPlaying)
            {
                DontDestroyOnLoad(go);
            }

            return fader;
        }

        public IEnumerator Fade(float to, float seconds, bool destroyWhenDone)
        {
            float from = image.color.a;
            for (float t = 0; t < seconds; t += Time.unscaledDeltaTime)
            {
                image.color = new Color(0, 0, 0, Mathf.Lerp(from, to, t / seconds));
                yield return null;
            }

            image.color = new Color(0, 0, 0, to);
            if (destroyWhenDone)
            {
                Destroy(gameObject);
            }
        }
    }
}
