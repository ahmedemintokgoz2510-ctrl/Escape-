using UnityEngine;
using UnityEngine.UI;

/// <summary>Ekranın üst ortasında kısa süreli Türkçe bildirim gösterir (örn. "1/3 İpucu Bulundu").</summary>
public class GameNotifier : MonoBehaviour
{
    private static GameNotifier _instance;

    private CanvasGroup _group;
    private Text _title;
    private Text _subtitle;
    private float _hideAt;

    /// <summary>Bildirimi gösterir. subtitle boş bırakılabilir.</summary>
    public static void Show(string title, string subtitle = null, float seconds = 4f)
    {
        if (_instance == null)
        {
            var go = new GameObject("Game Notifier");
            _instance = go.AddComponent<GameNotifier>();
            _instance.BuildUi();
        }
        _instance.Display(title, subtitle, seconds);
    }

    private void Display(string title, string subtitle, float seconds)
    {
        _title.text = title;
        bool hasSub = !string.IsNullOrEmpty(subtitle);
        _subtitle.gameObject.SetActive(hasSub);
        if (hasSub) _subtitle.text = subtitle;

        _group.alpha = 1f;
        _hideAt = Time.unscaledTime + seconds;
    }

    private void Update()
    {
        if (_group == null || _group.alpha <= 0f) return;
        if (Time.unscaledTime > _hideAt)
            _group.alpha = Mathf.Max(0f, _group.alpha - Time.unscaledDeltaTime * 1.5f);
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    private void BuildUi()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 900;

        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
        panel.transform.SetParent(transform, false);
        var rt = (RectTransform)panel.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -60f);
        rt.sizeDelta = new Vector2(900f, 170f);

        var bg = panel.GetComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.7f);
        bg.raycastTarget = false;

        _group = panel.GetComponent<CanvasGroup>();
        _group.alpha = 0f;
        _group.blocksRaycasts = false;
        _group.interactable = false;

        Font font = UiFont.Get();
        _title = CreateText(panel.transform, "Title", font, 48, FontStyle.Bold, Color.white,
                            new Vector2(0f, -14f), new Vector2(860f, 66f));
        _subtitle = CreateText(panel.transform, "Subtitle", font, 28, FontStyle.Normal, new Color(0.9f, 0.9f, 0.9f),
                               new Vector2(0f, -84f), new Vector2(860f, 76f));
    }

    private static Text CreateText(Transform parent, string name, Font font, int size, FontStyle style, Color color,
                                   Vector2 anchoredPos, Vector2 sizeDelta)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = sizeDelta;

        var text = go.GetComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.fontStyle = style;
        text.color = color;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        return text;
    }
}

/// <summary>
/// Türkçe karakterleri (ğ, ş, ı, İ...) sorunsuz gösteren yerleşik dinamik yazı tipi.
/// (Projedeki TMP varsayılan fontu statik atlas kullandığı için bu karakterlerin bir kısmı eksik olabilir.)
/// </summary>
public static class UiFont
{
    private static Font _font;

    public static Font Get()
    {
        if (_font != null) return _font;
        try { _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { /* yok */ }
        if (_font == null) _font = Font.CreateDynamicFontFromOSFont("Arial", 24);
        return _font;
    }
}
