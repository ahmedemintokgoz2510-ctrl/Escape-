using System.Collections;
using UnityEngine;

public class MetalDoor : MonoBehaviour
{
    [SerializeField] bool canOpen;
    [SerializeField] GameObject playerCamera;
    [SerializeField] GameObject metalDoorCamera;
    [Tooltip("Kapı açılınca karakterin donup bekleyeceği süre (sn).")]
    [SerializeField] float freezeSeconds = 1f;
    [Tooltip("Kapı kamerasının gösterileceği süre (sn); bitince korkunç son başlar.")]
    [SerializeField] float doorCutsceneSeconds = 3f;

    bool opening;

    bool IsLocked()
    {
        return PuzzleManager.Instance != null && !PuzzleManager.Instance.AllNotesFound;
    }

    void Update()
    {
        if (canOpen == true && !opening)
        {
            // Klavye devre dışı: E artık telefondan gelen E_TETIKLE komutuyla tetiklenir.
            if (NetworkInputController.ConsumeInteract())
            {
                if (IsLocked())
                {
                    PuzzleManager.Instance.ShowDoorLocked();
                }
                else
                {
                    StartCoroutine(OpeningDoor());
                }
            }
        }
    }

    void OnMouseOver()
    {
        if (opening) return;

        if (PlayerCasting.distanceFromTarget < 5)
        {
            canOpen = true;
            bool locked = IsLocked();
            UIController.actionText = locked ? "Kapı Kilitli" : "Kapıyı Aç";
            UIController.commandText = locked ? "İncele" : "Aç";
            UIController.uiActive = true;
        }
        else
        {
            canOpen = false;
            UIController.actionText = "";
            UIController.commandText = "";
            UIController.uiActive = false;
        }
    }

    void OnMouseExit()
    {
        if (opening) return;

        canOpen = false;
        UIController.actionText = "";
        UIController.commandText = "";
        UIController.uiActive = false;
    }

    IEnumerator OpeningDoor()
    {
        opening = true;
        canOpen = false;
        UIController.uiActive = false;

        // 1) Kapı açıldığı an karakter 1 saniye donup kalır.
        if (PuzzleManager.Instance != null) PuzzleManager.Instance.BeginEnding();
        NetworkInputController.SetFrozen(true);
        yield return new WaitForSeconds(freezeSeconds);

        // 2) Kapı kamerası: tam dışarı çıkılacakken...
        metalDoorCamera.SetActive(true);
        playerCamera.SetActive(false);
        yield return new WaitForSeconds(doorCutsceneSeconds);

        // 3) ...canavar fırlar (JumpscareController), 2 sn sonra oyun kapanır.
        if (PuzzleManager.Instance != null) PuzzleManager.Instance.PlayerEscaped();
        else JumpscareController.Play();
    }
}
