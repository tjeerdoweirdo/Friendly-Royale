using UnityEngine;
using UnityEngine.UI;

public class DeckSelectorPanel : MonoBehaviour
{
    [Header("References")]
    public GameObject panelRoot; // assign the panel GameObject in inspector
    public Button toggleButton;   // assign the button that toggles the panel

    void Awake()
    {
        if (panelRoot != null)
            panelRoot.SetActive(false); // start hidden

        if (toggleButton != null)
            toggleButton.onClick.AddListener(TogglePanel);
    }

    /// <summary>
    /// Toggle panel visibility
    /// </summary>
    public void TogglePanel()
    {
        if (panelRoot == null) return;

        panelRoot.SetActive(!panelRoot.activeSelf);
    }
}
