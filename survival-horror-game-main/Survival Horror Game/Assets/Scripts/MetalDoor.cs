using System.Collections;
using UnityEngine;

public class MetalDoor : MonoBehaviour
{
    [SerializeField] bool canOpen;
    [SerializeField] GameObject playerCamera;
    [SerializeField] GameObject metalDoorCamera;

    void Update()
    {
        if (canOpen == true)
        {
            // Klavye devre dışı: E artık telefondan gelen E_TETIKLE komutuyla tetiklenir.
            if (NetworkInputController.ConsumeInteract())
            {
                StartCoroutine(OpeningDoor());
            }
        }
    }

    void OnMouseOver()
    {
        if (PlayerCasting.distanceFromTarget < 5)
        {
            canOpen = true;
            UIController.actionText = "Open Door";
            UIController.commandText = "Open";
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
        canOpen = false;
        UIController.actionText = "";
        UIController.commandText = "";
        UIController.uiActive = false;
    }

    IEnumerator OpeningDoor()
    {
        metalDoorCamera.SetActive(true);
        playerCamera.SetActive(false);

        yield return new WaitForSeconds(3);

        playerCamera.SetActive(true);
        metalDoorCamera.SetActive(false);
    }
}
