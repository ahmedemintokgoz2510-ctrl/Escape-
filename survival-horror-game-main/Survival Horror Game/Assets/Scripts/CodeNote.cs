using UnityEngine;

/// <summary>
/// Haritaya gizlenmiş "Şifre Notu". Oyuncu yaklaşıp nişan aldığında E_TETIKLE ile toplanır.
/// Etkileşim, MetalDoor ile aynı yöntemi kullanır (OnMouseOver + PlayerCasting mesafesi).
/// </summary>
public class CodeNote : MonoBehaviour
{
    private const string ActionLabel = "Şifre Notu";

    [TextArea] public string noteText = "";
    [SerializeField] private float maxDistance = 3.5f;

    private bool _canTake;

    private void Update()
    {
        if (_canTake && NetworkInputController.ConsumeInteract())
            Collect();
    }

    private void OnMouseOver()
    {
        if (PlayerCasting.distanceFromTarget < maxDistance)
        {
            _canTake = true;
            UIController.actionText = ActionLabel;
            UIController.commandText = "Oku";
            UIController.uiActive = true;
        }
        else
        {
            ClearPrompt();
        }
    }

    private void OnMouseExit()
    {
        ClearPrompt();
    }

    private void ClearPrompt()
    {
        _canTake = false;
        // Başka bir nesnenin (örn. kapı) ipucu yazısını silmemek için sadece kendi yazımızı temizle.
        if (UIController.actionText == ActionLabel)
        {
            UIController.actionText = "";
            UIController.commandText = "";
            UIController.uiActive = false;
        }
    }

    private void Collect()
    {
        _canTake = false;
        ClearPrompt();

        if (PuzzleManager.Instance != null)
            PuzzleManager.Instance.CollectNote(noteText);

        Destroy(gameObject);
    }
}
