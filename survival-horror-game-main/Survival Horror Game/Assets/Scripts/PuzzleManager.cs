using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 3 şifre notu bulmacasının durumunu tutar: bildirimleri gösterir, kapıyı açar,
/// her notta canavarın hız ve fark etme mesafesini artırır, yakalanma/kaçış durumlarını yönetir.
/// </summary>
public class PuzzleManager : MonoBehaviour
{
    public const int TotalNotes = 3;

    public static PuzzleManager Instance { get; private set; }

    [Header("Canavarın agresifleşmesi (her not için)")]
    [SerializeField] private float speedIncreasePerNote = 0.6f;
    [SerializeField] private float detectionIncreasePerNote = 4f;

    [SerializeField] private float restartDelay = 3f;

    public int NotesFound { get; private set; }
    public bool AllNotesFound { get { return NotesFound >= TotalNotes; } }

    private EnemyAI _enemy;
    private bool _ended;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void RegisterEnemy(EnemyAI enemy)
    {
        _enemy = enemy;
        _enemy.PlayerCaught += HandlePlayerCaught;
        ApplyAggression();
    }

    /// <summary>Bir şifre notu toplandığında çağrılır.</summary>
    public void CollectNote(string noteText)
    {
        if (_ended || AllNotesFound) return;

        NotesFound++;
        string title = NotesFound + "/" + TotalNotes + " İpucu Bulundu";
        string subtitle = noteText;

        if (AllNotesFound)
        {
            subtitle = (string.IsNullOrEmpty(noteText) ? "" : noteText + "\n") +
                       "Şifre çözüldü! Çıkış kapısının kilidi açıldı.";
        }

        GameNotifier.Show(title, subtitle, 6f);
        ApplyAggression();
    }

    /// <summary>Kapı kilitliyken etkileşime girilirse çağrılır.</summary>
    public void ShowDoorLocked()
    {
        GameNotifier.Show("Kapı kilitli!", "Şifre notlarını bulmalısın (" + NotesFound + "/" + TotalNotes + ")", 4f);
    }

    /// <summary>Kapı açılma sahnesi başlarken canavarı durdurur (sahne sırasında yakalanılmasın).</summary>
    public void BeginEnding()
    {
        if (_enemy != null) _enemy.Halt();
    }

    /// <summary>Çıkış kapısı açılıp dışarı çıkılacağı an çağrılır: korkunç son (jumpscare) başlar.</summary>
    public void PlayerEscaped()
    {
        if (_ended) return;
        _ended = true;
        if (_enemy != null) _enemy.Halt();
        JumpscareController.Play();
    }

    private void ApplyAggression()
    {
        if (_enemy == null) return;
        _enemy.SetAggression(NotesFound * speedIncreasePerNote, NotesFound * detectionIncreasePerNote);
    }

    private void HandlePlayerCaught()
    {
        if (_ended) return;
        _ended = true;
        GameNotifier.Show("Yakalandın!", "Oyun yeniden başlatılıyor...", restartDelay + 1f);
        StartCoroutine(RestartAfterDelay());
    }

    private IEnumerator RestartAfterDelay()
    {
        yield return new WaitForSecondsRealtime(restartDelay);
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
}
