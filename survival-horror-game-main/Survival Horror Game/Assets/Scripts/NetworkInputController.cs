using System;
using System.Collections.Concurrent;
using StarterAssets;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Telefondan (tarayıcıdan) gelen ağ komutlarını FirstPersonController'ın okuduğu
/// StarterAssetsInputs değerlerine enjekte eder. Klavye/fare girdisini devre dışı bırakır.
///
/// Komutlar: ILERI_BAS, ILERI_BIRAK, FENER_TETIKLE, E_TETIKLE (PING = canlılık sinyali).
/// Sahneye elle eklemek zorunda değilsin: NetworkInputBootstrap oyun açılınca otomatik kurar.
/// </summary>
[DefaultExecutionOrder(-50)]
public class NetworkInputController : MonoBehaviour
{
    public static NetworkInputController Instance { get; private set; }

    [Header("Ağ")]
    [SerializeField] private int port = 8080;

    [Header("Girdi")]
    [Tooltip("Açıkken klavye/fare/gamepad ile hareket tamamen kapatılır; sadece ağ komutları geçerlidir.")]
    [SerializeField] private bool lockLocalInput = true;
    [Tooltip("Bu süre boyunca telefondan hiç paket gelmezse (PING dahil) karakter durdurulur.")]
    [SerializeField] private float heartbeatTimeout = 3f;

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
    private float _nextFindTime;
    private bool _forwardHeld;
    private float _lastPacketTime;
    private float _interactExpireTime;
    private int _lastClientCount;

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

        _server = new LocalCommandServer(port);
        _server.Log = delegate (string message) { Debug.Log("[Ag] " + message); };
        _server.CommandReceived += delegate (string command) { _commands.Enqueue(command); };

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

        // Güvenlik: telefon bağlantısı kopar ya da donarsa karakter yürümeye devam etmesin.
        int clients = ClientCount;
        if (clients == 0 && _lastClientCount > 0) _forwardHeld = false;
        _lastClientCount = clients;

        if (_forwardHeld && Time.unscaledTime - _lastPacketTime > heartbeatTimeout)
            _forwardHeld = false;

        if (_inputs != null)
            _inputs.MoveInput(_forwardHeld ? Vector2.up : Vector2.zero);
    }

    private void Apply(string command)
    {
        switch (command)
        {
            case "ILERI_BAS": _forwardHeld = true; break;
            case "ILERI_BIRAK": _forwardHeld = false; break;
            case "FENER_TETIKLE": ToggleFlashlight(); break;
            case "E_TETIKLE":
                _interactExpireTime = Time.unscaledTime + interactBufferSeconds;
                if (InteractTriggered != null) InteractTriggered();
                break;
            case "PING": break;
            default: Debug.LogWarning("[Ag] Bilinmeyen komut: " + command); break;
        }
    }

    /// <summary>
    /// E_TETIKLE ile gelen etkileşimi tek seferlik tüketir (eski Input.GetKeyDown(KeyCode.E) yerine).
    /// </summary>
    public static bool ConsumeInteract()
    {
        NetworkInputController self = Instance;
        if (self == null || self._interactExpireTime <= Time.unscaledTime) return false;
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

        if (lockLocalInput) LockLocalInput();
    }

    private void LockLocalInput()
    {
#if ENABLE_INPUT_SYSTEM
        // Klavye/fare/gamepad eylemlerini kapat: OnMove/OnLook/OnJump/OnSprint bir daha çağrılmaz.
        PlayerInput playerInput = _inputs.GetComponent<PlayerInput>();
        if (playerInput != null && playerInput.inputIsActive) playerInput.DeactivateInput();
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
