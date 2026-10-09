using UnityEngine;
using UnityEngine.UI;

public class UIController : MonoBehaviour
{
    public static string actionText;
    public static string commandText;
    public static bool uiActive;

    [SerializeField] GameObject actionBox;
    [SerializeField] GameObject commandBox;
    [SerializeField] GameObject interactCross;

    // TMP'nin varsayılan fontunda Türkçe karakterler eksik olabileceği için,
    // sahnedeki TMP kutularının üstüne aynı boyutta Türkçe destekli bir Text bindirilir.
    Text actionLabel;
    Text commandLabel;

    void Start()
    {
        actionLabel = CreateOverlay(actionBox);
        commandLabel = CreateOverlay(commandBox);
    }

    void Update()
    {
        if (uiActive == true)
        {
            actionBox.SetActive(true);
            commandBox.SetActive(true);
            interactCross.SetActive(true);

            SetText(actionBox, actionLabel, actionText);
            SetText(commandBox, commandLabel, "[E] " + commandText);
        }
        else
        {
            actionBox.SetActive(false);
            commandBox.SetActive(false);
            interactCross.SetActive(false);
        }
    }

    static void SetText(GameObject box, Text label, string value)
    {
        if (label != null)
        {
            label.text = value;
        }
        else
        {
            box.GetComponent<TMPro.TMP_Text>().text = value;
        }
    }

    static Text CreateOverlay(GameObject box)
    {
        TMPro.TMP_Text tmp = box.GetComponent<TMPro.TMP_Text>();
        if (tmp == null) return null;

        var go = new GameObject("TR Text", typeof(RectTransform));
        go.transform.SetParent(box.transform, false);

        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var text = go.AddComponent<Text>();
        text.font = UiFont.Get();
        text.color = tmp.color;
        text.raycastTarget = false;
        text.alignment = MapAlignment(tmp.alignment.ToString());
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.fontStyle = (tmp.fontStyle & TMPro.FontStyles.Bold) != 0 ? FontStyle.Bold : FontStyle.Normal;

        if (tmp.enableAutoSizing)
        {
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 10;
            text.resizeTextMaxSize = Mathf.Max(10, Mathf.RoundToInt(tmp.fontSizeMax));
        }
        else
        {
            text.fontSize = Mathf.Max(10, Mathf.RoundToInt(tmp.fontSize));
        }

        tmp.enabled = false; // eski TMP yazısını gizle, yerine Türkçe destekli Text kullanılır
        return text;
    }

    static TextAnchor MapAlignment(string a)
    {
        bool left = a.Contains("Left");
        bool right = a.Contains("Right");
        bool top = a.StartsWith("Top");
        bool bottom = a.StartsWith("Bottom");

        if (top) return left ? TextAnchor.UpperLeft : right ? TextAnchor.UpperRight : TextAnchor.UpperCenter;
        if (bottom) return left ? TextAnchor.LowerLeft : right ? TextAnchor.LowerRight : TextAnchor.LowerCenter;
        return left ? TextAnchor.MiddleLeft : right ? TextAnchor.MiddleRight : TextAnchor.MiddleCenter;
    }
}
