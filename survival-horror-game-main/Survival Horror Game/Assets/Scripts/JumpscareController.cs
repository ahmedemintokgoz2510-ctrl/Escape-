using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Korkunç son: tam ekran canavar görseli + çığlık, telefona VIBRATE paketi, 2 saniye sonra oyunu kapatır.
/// Görsel ve ses Assets/Resources içindedir (kendi dosyalarınla aynı isimle değiştirebilirsin):
///   monster_jumpscare.png, jumpscare_scream.wav
/// </summary>
public class JumpscareController : MonoBehaviour
{
    private const string ImageResource = "monster_jumpscare";
    private const string AudioResource = "jumpscare_scream";

    [SerializeField] private float showSeconds = 2f;

    private static bool _playing;

    public static void Play()
    {
        if (_playing) return;
        _playing = true;
        new GameObject("Jumpscare").AddComponent<JumpscareController>();
    }

    private IEnumerator Start()
    {
        NetworkInputController.SetFrozen(true);

        // --- Ekran: siyah zemin + tam ekran canavar + beyaz çakma ---
        var canvasGo = new GameObject("Jumpscare Canvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5000;

        var black = CreateImage(canvasGo.transform, "Siyah");
        black.color = Color.black;

        Texture2D monster = Resources.Load<Texture2D>(ImageResource);
        RectTransform monsterRt;
        if (monster != null)
        {
            var raw = new GameObject("Canavar", typeof(RectTransform), typeof(RawImage));
            raw.transform.SetParent(canvasGo.transform, false);
            monsterRt = (RectTransform)raw.transform;
            Stretch(monsterRt);
            var rawImage = raw.GetComponent<RawImage>();
            rawImage.texture = monster;
            rawImage.raycastTarget = false;
        }
        else
        {
            Debug.LogWarning("[Jumpscare] Resources/" + ImageResource + " bulunamadi; kirmizi ekran gosteriliyor.");
            Image red = CreateImage(canvasGo.transform, "KirmiziEkran");
            red.color = new Color(0.6f, 0f, 0f, 1f);
            monsterRt = red.rectTransform;
        }

        Image flash = CreateImage(canvasGo.transform, "Flas");
        flash.color = Color.white;

        // --- Ses ---
        if (FindFirstObjectByType<AudioListener>() == null) gameObject.AddComponent<AudioListener>();
        AudioListener.pause = false;
        AudioListener.volume = 1f;

        AudioClip scream = Resources.Load<AudioClip>(AudioResource);
        if (scream != null)
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.spatialBlend = 0f;
            source.volume = 1f;
            source.ignoreListenerPause = true;
            source.PlayOneShot(scream, 1f);
        }
        else
        {
            Debug.LogWarning("[Jumpscare] Resources/" + AudioResource + " bulunamadi; ses calinmayacak.");
        }

        // --- Telefon titreşimi ---
        NetworkInputController.SendToPhones("VIBRATE");

        // --- Animasyon: ani büyüme + titreme + çakma ---
        float t = 0f;
        while (t < showSeconds)
        {
            t += Time.unscaledDeltaTime;

            float punch = Mathf.Clamp01(t / 0.12f);
            float scale = Mathf.Lerp(0.55f, 1.2f, punch) + 0.04f * Mathf.Sin(t * 55f);
            monsterRt.localScale = new Vector3(scale, scale, 1f);
            monsterRt.anchoredPosition = new Vector2(Random.Range(-18f, 18f), Random.Range(-14f, 14f));

            float f = Mathf.Clamp01(1f - t / 0.25f);
            flash.color = new Color(1f, 0.85f, 0.85f, f);

            yield return null;
        }

        Quit();
    }

    private static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private static Image CreateImage(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        Stretch((RectTransform)go.transform);
        var image = go.GetComponent<Image>();
        image.raycastTarget = false;
        return image;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
