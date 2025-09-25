using UnityEngine;
using TMPro;

/// <summary>
/// Lightweight UI binder for match UI. Attach to a GameObject in scene and set the TMP fields.
/// It will automatically hook up to the GameManager in the scene.
/// </summary>
public class MatchUI : MonoBehaviour
{
    public TMP_Text timerText;
    public TMP_Text resultText;

    void Start()
    {
        var gm = FindObjectOfType<GameManager>();
        if (gm == null)
        {
            Debug.LogWarning("[MatchUI] GameManager not found in scene.");
            return;
        }

        // Let GameManager own these references so it can update UI directly.
        gm.timerText = timerText;
        gm.resultText = resultText;
    }

    /// <summary>
    /// UI button handler to restart the match.
    /// Hook this to a UI button's OnClick.
    /// </summary>
    public void OnRestartButton()
    {
        var gm = FindObjectOfType<GameManager>();
        if (gm != null)
            gm.RestartMatch();
    }
}