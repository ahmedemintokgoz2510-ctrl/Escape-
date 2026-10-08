using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Tahtanın yerel IP adresini otomatik bulur, http://IP:PORT adresini QR koda çevirip
/// ekranın sağ üstünde gösterir. UI tamamen kodla kurulur (prefab/sahne düzenlemesi gerekmez).
/// </summary>
public class QRCodeGenerator : MonoBehaviour
{
    [Header("QR")]
    [Tooltip("0 = NetworkInputController'ın portunu kullan (varsayılan 8080).")]
    [SerializeField] private int portOverride = 0;
    [SerializeField] private int pixelsPerModule = 8;
    [SerializeField] private int quietZoneModules = 4;

    [Header("Panel")]
    [Tooltip("Telefon bağlanınca paneli gizle, bağlantı kopunca tekrar göster.")]
    [SerializeField] private bool hideWhenConnected = true;
    [SerializeField] private float ipRefreshSeconds = 5f;

    private GameObject _panel;
    private RawImage _qrImage;
    private Text _urlText;
    private Text _statusText;
    private Texture2D _texture;
    private string _currentUrl;
    private string _lastStatus;
    private float _nextRefresh;

    public string CurrentUrl { get { return _currentUrl; } }

    private void Start()
    {
        BuildUi();
        RefreshUrl();
    }

    private void OnDestroy()
    {
        if (_texture != null) Destroy(_texture);
    }

    private void Update()
    {
        if (Time.unscaledTime >= _nextRefresh)
        {
            _nextRefresh = Time.unscaledTime + ipRefreshSeconds;
            RefreshUrl();
        }
        UpdateStatus();
    }

    // ------------------------------------------------------------------ Mantık

    private int GetPort()
    {
        if (portOverride > 0) return portOverride;
        return NetworkInputController.Instance != null ? NetworkInputController.Instance.Port : 8080;
    }

    private void RefreshUrl()
    {
        string url = "http://" + GetLocalIPv4() + ":" + GetPort();
        if (url == _currentUrl) return;

        _currentUrl = url;
        _urlText.text = url;
        RebuildTexture(url);
    }

    private void UpdateStatus()
    {
        NetworkInputController net = NetworkInputController.Instance;
        string status;
        Color color;
        int clients = 0;

        if (net == null) { status = "Ag baslatiliyor..."; color = Color.yellow; }
        else if (!net.IsListening && !string.IsNullOrEmpty(net.ServerError)) { status = net.ServerError; color = new Color(1f, 0.4f, 0.4f); }
        else if (net.ClientCount > 0) { clients = net.ClientCount; status = "Telefon bagli"; color = new Color(0.4f, 1f, 0.5f); }
        else { status = "Telefon bekleniyor..."; color = Color.yellow; }

        if (status != _lastStatus)
        {
            _lastStatus = status;
            _statusText.text = status;
            _statusText.color = color;
        }

        bool visible = !(hideWhenConnected && clients > 0);
        if (_panel.activeSelf != visible) _panel.SetActive(visible);
    }

    private void RebuildTexture(string text)
    {
        bool[,] matrix = QrCodeEncoder.Encode(text);
        if (matrix == null)
        {
            Debug.LogError("[QR] Adres QR koda sigmadi: " + text);
            return;
        }

        int n = matrix.GetLength(0);
        int total = n + quietZoneModules * 2;
        int size = total * pixelsPerModule;

        var pixels = new Color32[size * size];
        var white = new Color32(255, 255, 255, 255);
        var black = new Color32(0, 0, 0, 255);

        for (int py = 0; py < size; py++)
        {
            int my = py / pixelsPerModule - quietZoneModules;
            for (int px = 0; px < size; px++)
            {
                int mx = px / pixelsPerModule - quietZoneModules;
                bool dark = mx >= 0 && mx < n && my >= 0 && my < n && matrix[mx, n - 1 - my];
                pixels[py * size + px] = dark ? black : white;
            }
        }

        if (_texture != null) Destroy(_texture);
        _texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        _texture.filterMode = FilterMode.Point;
        _texture.wrapMode = TextureWrapMode.Clamp;
        _texture.SetPixels32(pixels);
        _texture.Apply(false, true);
        _qrImage.texture = _texture;
    }

    // --------------------------------------------------------------- Yerel IP

    /// <summary>Sanal/VPN adaptörlerini eleyerek tahtanın LAN IPv4 adresini bulur.</summary>
    public static string GetLocalIPv4()
    {
        string best = null;
        int bestScore = -1;

        try
        {
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;

                string desc = (ni.Name + " " + ni.Description).ToLowerInvariant();
                bool virtualAdapter = desc.Contains("virtual") || desc.Contains("vmware") || desc.Contains("vbox") ||
                                      desc.Contains("hyper-v") || desc.Contains("vethernet") || desc.Contains("docker") ||
                                      desc.Contains("wsl") || desc.Contains("tap") || desc.Contains("bluetooth") ||
                                      desc.Contains("tailscale") || desc.Contains("zerotier") || desc.Contains("hamachi");

                IPInterfaceProperties props = ni.GetIPProperties();
                bool hasGateway = false;
                foreach (GatewayIPAddressInformation g in props.GatewayAddresses)
                {
                    if (g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any))
                        hasGateway = true;
                }

                foreach (UnicastIPAddressInformation ua in props.UnicastAddresses)
                {
                    IPAddress ip = ua.Address;
                    if (ip.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(ip)) continue;

                    byte[] b = ip.GetAddressBytes();
                    if (b[0] == 169 && b[1] == 254) continue; // link-local (DHCP alinamamis)

                    int score = 0;
                    if (hasGateway) score += 100;
                    if (!virtualAdapter) score += 50;
                    if (b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168)) score += 20;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                        ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) score += 10;

                    if (score > bestScore) { bestScore = score; best = ip.ToString(); }
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[QR] Ag adaptorleri okunamadi: " + e.Message);
        }

        if (best != null) return best;

        // Yedek: yönlendirme tablosuna göre yerel adres (paket gönderilmez).
        try
        {
            using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                socket.Connect("8.8.8.8", 65530);
                var endPoint = socket.LocalEndPoint as IPEndPoint;
                if (endPoint != null) return endPoint.Address.ToString();
            }
        }
        catch { /* ag yok */ }

        return "127.0.0.1";
    }

    // ---------------------------------------------------------------- UI kurulumu

    private void BuildUi()
    {
        var canvasGo = new GameObject("QR Canvas");
        canvasGo.transform.SetParent(transform, false);

        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        // GraphicRaycaster eklenmez: panel tıklamaları yakalamaz.

        _panel = new GameObject("QR Panel", typeof(RectTransform), typeof(Image));
        _panel.transform.SetParent(canvasGo.transform, false);
        var panelRt = (RectTransform)_panel.transform;
        panelRt.anchorMin = panelRt.anchorMax = panelRt.pivot = new Vector2(1f, 1f);
        panelRt.anchoredPosition = new Vector2(-30f, -30f);
        panelRt.sizeDelta = new Vector2(380f, 540f);
        var bg = _panel.GetComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.8f);
        bg.raycastTarget = false;

        Font font = LoadFont();

        CreateText("Title", "Telefonla Baglan", font, 30, FontStyle.Bold, Color.white,
                   new Vector2(0f, -16f), new Vector2(360f, 44f));

        var qrGo = new GameObject("QR Image", typeof(RectTransform), typeof(RawImage));
        qrGo.transform.SetParent(_panel.transform, false);
        var qrRt = (RectTransform)qrGo.transform;
        qrRt.anchorMin = qrRt.anchorMax = qrRt.pivot = new Vector2(0.5f, 1f);
        qrRt.anchoredPosition = new Vector2(0f, -66f);
        qrRt.sizeDelta = new Vector2(340f, 340f);
        _qrImage = qrGo.GetComponent<RawImage>();
        _qrImage.raycastTarget = false;

        _urlText = CreateText("Url", "", font, 22, FontStyle.Normal, Color.white,
                              new Vector2(0f, -416f), new Vector2(360f, 34f));
        _statusText = CreateText("Status", "", font, 24, FontStyle.Bold, Color.yellow,
                                 new Vector2(0f, -454f), new Vector2(360f, 34f));
        CreateText("Hint", "Telefon ve tahta ayni Wi-Fi aginda olmali", font, 17, FontStyle.Italic,
                   new Color(0.8f, 0.8f, 0.8f), new Vector2(0f, -494f), new Vector2(360f, 30f));
    }

    private Text CreateText(string name, string value, Font font, int size, FontStyle style, Color color,
                            Vector2 anchoredPos, Vector2 sizeDelta)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(_panel.transform, false);
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
        text.text = value;
        return text;
    }

    private static Font LoadFont()
    {
        Font font = null;
        try { font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { /* yok */ }
        if (font == null) font = Font.CreateDynamicFontFromOSFont("Arial", 24);
        return font;
    }
}
