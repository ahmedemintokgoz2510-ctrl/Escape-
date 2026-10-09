using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using StarterAssets;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Telefondan (tarayıcıdan) gelen ağ komutlarını FirstPersonController'ın okuduğu
/// StarterAssetsInputs değerlerine enjekte eder. Klavye/fare girdisini devre dışı bırakır.
///
/// Komutlar:
///   MOVE:x,y        D-pad / joystick. x = dönüş (-1 sol, +1 sağ), y = yürüme (+1 ileri, -1 geri). Değerler -1..1.
///   FENER_TETIKLE   El fenerini aç/kapat.
///   E_TETIKLE       Etkileşim (E).
///   PING            Canlılık sinyali.
///   (ILERI_BAS / ILERI_BIRAK eski komutlar da desteklenir.)
/// Telefona gönderilen mesaj: VIBRATE.
/// </summary>
[DefaultExecutionOrder(-50)]
public class NetworkInputController : MonoBehaviour
{
    public static NetworkInputController Instance { get; private set; }

    [Header("Ağ")]
    [SerializeField] private int port = 8080;
    [Tooltip("StreamingAssets içindeki telefon kumanda sayfası.")]
    [SerializeField] private string controllerPageFile = "kumanda.html";

    [Header("Girdi")]
    [Tooltip("Açıkken klavye/fare/gamepad ile hareket tamamen kapatılır; sadece ağ komutları geçerlidir.")]
    [SerializeField] private bool lockLocalInput = true;
    [Tooltip("Bu süre boyunca telefondan hiç paket gelmezse (PING dahil) karakter durdurulur.")]
    [SerializeField] private float heartbeatTimeout = 3f;

    [Header("Dönüş")]
    [Tooltip("D-pad tam yana basılıyken saniyedeki dönüş açısı (derece).")]
    [SerializeField] private float turnDegreesPerSecond = 110f;
    [Tooltip("Dönüş girdisinin 0'dan 1'e çıkma hızı (1/sn). Küçük değer = daha yumuşak dönüş.")]
    [SerializeField] private float turnAcceleration = 5f;

    [Header("E tuşu")]
    [Tooltip("E_TETIKLE komutunun, bir etkileşim nesnesi tarafından tüketilmeyi bekleyeceği süre (sn).")]
    [SerializeField] private float interactBufferSeconds = 0.35f;

    [Header("Fener")]
    [SerializeField] private Light flashlight;
    [Tooltip("Sahnede fener yoksa kameraya bağlı bir spot ışık oluşturur (gölgesiz, düşük maliyetli).")]
    [SerializeField] private bool createFlashlightIfMissing = true;

    public int Port { get { return port; } }
    public bool IsListening { get { return _server != null && _server.IsListening; } }
    public string ServerError { get { return _server != null ? _server.ErrorMessage : null; } }
    public int ClientCount { get { return _server != null ? _server.ClientCount : 0; } }

    public event Action FlashlightToggled;
    public event Action InteractTriggered;

    private readonly ConcurrentQueue<string> _commands = new ConcurrentQueue<string>();
    private LocalCommandServer _server;
    private StarterAssetsInputs _inputs;
    private FirstPersonController _controller;
#if ENABLE_INPUT_SYSTEM
    private PlayerInput _playerInput;
#endif
    private float _nextFindTime;
    private Vector2 _moveInput;     // x = dönüş, y = ileri/geri
    private float _turnCurrent;     // yumuşatılmış dönüş
    private float _lastPacketTime;
    private float _interactExpireTime;
    private int _lastClientCount;
    private bool _frozen;

    /// <summary>Sonlandırma sahnesi gibi anlarda karakteri dondurur (hareket, dönüş ve etkileşim kapanır).</summary>
    public static void SetFrozen(bool frozen)
    {
        if (Instance == null) return;
        Instance._frozen = frozen;
        if (frozen)
        {
            Instance._moveInput = Vector2.zero;
            Instance._turnCurrent = 0f;
            Instance._interactExpireTime = 0f;
        }
    }

    /// <summary>Bağlı telefonlara mesaj gönderir (örn. "VIBRATE").</summary>
    public static void SendToPhones(string message)
    {
        if (Instance != null && Instance._server != null) Instance._server.Broadcast(message);
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;

        Application.runInBackground = true; // pencere odağı kaybolsa da komutlar işlensin
        Cursor.visible = false;             // imleç kilitli kalır (OnMouseOver ekran ortasını kullanır), sadece gizlenir
    }

    private void Start()
    {
        if (Instance != this) return;

        // Application.streamingAssetsPath yalnızca ana iş parçacığında okunabilir; yolu şimdi sabitle.
        string pagePath = Path.Combine(Application.streamingAssetsPath, controllerPageFile);

        _server = new LocalCommandServer(port);
        _server.Log = delegate (string message) { Debug.Log("[Ag] " + message); };
        _server.CommandReceived += delegate (string command) { _commands.Enqueue(command); };
        _server.PageProvider = delegate ()
        {
            return File.Exists(pagePath) ? File.ReadAllText(pagePath) : null;
        };

        if (!File.Exists(pagePath))
            Debug.LogError("[Ag] Kumanda sayfasi bulunamadi: " + pagePath);

        if (!_server.Start())
            Debug.LogError("[Ag] " + _server.ErrorMessage);
        else
            Debug.Log("[Ag] Dinleniyor: 0.0.0.0:" + port);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (_server != null) { _server.Dispose(); _server = null; }
    }

    private void Update()
    {
        if (Instance != this) return;

        EnsureInputs();

        string command;
        while (_commands.TryDequeue(out command))
        {
            _lastPacketTime = Time.unscaledTime;
            Apply(command);
        }

        // Güvenlik: telefon bağlantısı kopar ya da donarsa karakter yürümeye/dönmeye devam etmesin.
        int clients = ClientCount;
        if (clients == 0 && _lastClientCount > 0) _moveInput = Vector2.zero;
        _lastClientCount = clients;

        if (_moveInput != Vector2.zero && Time.unscaledTime - _lastPacketTime > heartbeatTimeout)
            _moveInput = Vector2.zero;

        float forward = _frozen ? 0f : _moveInput.y;
        float turnTarget = _frozen ? 0f : _moveInput.x;
        _turnCurrent = Mathf.MoveTowards(_turnCurrent, turnTarget, turnAcceleration * Time.deltaTime);

        if (_inputs != null)
        {
            _inputs.MoveInput(new Vector2(0f, forward));
            _inputs.LookInput(new Vector2(ComputeLookX(_turnCurrent * turnDegreesPerSecond), 0f));
        }
    }

    /// <summary>
    /// İstenen dönüş hızını (derece/sn) FirstPersonController'ın beklediği look.x değerine çevirir.
    /// Kontrolcü, fare şemasında kare başına, diğer şemalarda Time.deltaTime ile çarparak döner.
    /// </summary>
    private float ComputeLookX(float degreesPerSecond)
    {
        if (Mathf.Abs(degreesPerSecond) < 0.001f) return 0f;

        float rotationSpeed = (_controller != null && _controller.RotationSpeed > 0.0001f) ? _controller.RotationSpeed : 1f;

        bool mouseScheme = false;
#if ENABLE_INPUT_SYSTEM
        mouseScheme = _playerInput != null && _playerInput.currentControlScheme == "KeyboardMouse";
#endif
        float perFrameDegrees = mouseScheme ? degreesPerSecond * Time.deltaTime : degreesPerSecond;
        return perFrameDegrees / rotationSpeed;
    }

    private void Apply(string command)
    {
        if (command.StartsWith("MOVE:", StringComparison.Ordinal))
        {
            ParseMove(command.Substring(5));
            return;
        }

        switch (command)
        {
            case "ILERI_BAS": _moveInput.y = 1f; break;
            case "ILERI_BIRAK": _moveInput.y = 0f; break;
            case "FENER_TETIKLE":
                if (!_frozen) ToggleFlashlight();
                break;
            case "E_TETIKLE":
                if (_frozen) break;
                _interactExpireTime = Time.unscaledTime + interactBufferSeconds;
                if (InteractTriggered != null) InteractTriggered();
                break;
            case "PING": break;
            default: Debug.LogWarning("[Ag] Bilinmeyen komut: " + command); break;
        }
    }

    private void ParseMove(string args)
    {
        string[] parts = args.Split(',');
        float x, y;
        if (parts.Length != 2 ||
            !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x) ||
            !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y))
        {
            Debug.LogWarning("[Ag] Gecersiz MOVE paketi: " + args);
            return;
        }
        _moveInput = new Vector2(Mathf.Clamp(x, -1f, 1f), Mathf.Clamp(y, -1f, 1f));
    }

    /// <summary>
    /// E_TETIKLE ile gelen etkileşimi tek seferlik tüketir (eski Input.GetKeyDown(KeyCode.E) yerine).
    /// </summary>
    public static bool ConsumeInteract()
    {
        NetworkInputController self = Instance;
        if (self == null || self._frozen || self._interactExpireTime <= Time.unscaledTime) return false;
        self._interactExpireTime = 0f;
        return true;
    }

    // -------------------------------------------------------- Girdi bağlama

    private void EnsureInputs()
    {
        if (_inputs != null || Time.unscaledTime < _nextFindTime) return;
        _nextFindTime = Time.unscaledTime + 0.5f;

        _inputs = FindFirstObjectByType<StarterAssetsInputs>();
        if (_inputs == null) return;

        _controller = _inputs.GetComponent<FirstPersonController>();
#if ENABLE_INPUT_SYSTEM
        _playerInput = _inputs.GetComponent<PlayerInput>();
#endif
        if (lockLocalInput) LockLocalInput();
    }

    private void LockLocalInput()
    {
#if ENABLE_INPUT_SYSTEM
        // Klavye/fare/gamepad eylemlerini kapat: OnMove/OnLook/OnJump/OnSprint bir daha çağrılmaz.
        if (_playerInput != null && _playerInput.inputIsActive) _playerInput.DeactivateInput();
#endif
        _inputs.cursorInputForLook = false;
        _inputs.look = Vector2.zero;
        _inputs.jump = false;
        _inputs.sprint = false;
        _inputs.move = Vector2.zero;
    }

    // ---------------------------------------------------------------- Fener

    private void ToggleFlashlight()
    {
        if (flashlight == null && createFlashlightIfMissing) CreateFlashlight();
        if (flashlight == null) return;

        flashlight.enabled = !flashlight.enabled;
        if (FlashlightToggled != null) FlashlightToggled();
    }

    private void CreateFlashlight()
    {
        Camera cam = Camera.main;
        Transform parent = cam != null ? cam.transform : (_inputs != null ? _inputs.transform : null);
        if (parent == null) return;

        var go = new GameObject("Network Flashlight");
        go.transform.SetParent(parent, false);

        flashlight = go.AddComponent<Light>();
        flashlight.type = LightType.Spot;
        flashlight.range = 20f;
        flashlight.spotAngle = 60f;
        flashlight.innerSpotAngle = 30f;
        flashlight.intensity = 4f;
        flashlight.color = new Color(1f, 0.95f, 0.8f);
        flashlight.shadows = LightShadows.None;
        flashlight.enabled = false;
    }
}

/// <summary>Oyun açılınca ağ denetleyicisini ve QR panelini sahne düzenlemeden otomatik kurar.</summary>
public static class NetworkInputBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Init()
    {
        bool hasController = UnityEngine.Object.FindFirstObjectByType<NetworkInputController>() != null;
        bool hasQr = UnityEngine.Object.FindFirstObjectByType<QRCodeGenerator>() != null;
        if (hasController && hasQr) return;

        var root = new GameObject("NetworkInput");
        UnityEngine.Object.DontDestroyOnLoad(root);
        if (!hasController) root.AddComponent<NetworkInputController>();
        if (!hasQr) root.AddComponent<QRCodeGenerator>();
    }
}
