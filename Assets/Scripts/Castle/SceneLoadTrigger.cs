using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 玩家走進這個觸發區就淡出到黑畫面並載入指定場景（例如城堡入口 → Boss 房）。
/// 目標場景必須在 Build Profiles / Build Settings 的場景清單中。新場景載入後會自動淡入。
/// </summary>
[RequireComponent(typeof(Collider))]
public class SceneLoadTrigger : MonoBehaviour
{
    public string SceneName = "BossArena";
    [Min(0)] public float FadeSeconds = .8f;

    private static bool fadeInNext;
    private bool loading;

    private void Reset()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void FadeInAfterLoad()
    {
        if (!fadeInNext)
        {
            return;
        }

        fadeInNext = false;
        var fader = Fader.Create(1f);
        fader.StartCoroutine(fader.Fade(0f, .8f, true));
    }

    private void OnTriggerEnter(Collider other)
    {
        if (loading || !other.GetComponentInParent<PlayerHealth>())
        {
            return;
        }

        loading = true;
        StartCoroutine(Load());
    }

    private IEnumerator Load()
    {
        var fader = Fader.Create(0f);
        yield return fader.Fade(1f, FadeSeconds, false);
        fadeInNext = true;
        SceneManager.LoadScene(SceneName);
    }

    /// <summary>全螢幕黑色遮罩（用不受 timeScale 影響的時間淡入 / 淡出）。</summary>
    private class Fader : MonoBehaviour
    {
        private Image image;

        public static Fader Create(float alpha)
        {
            var go = new GameObject("Scene Fade");
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
