using TMPro;
using UnityEngine;

public class GoldTrophyUI : MonoBehaviour
{
    public TMP_Text goldText;
    public TMP_Text trophiesText;

    void Start()
    {
        // Subscribe to events after PlayerProgress.Instance is initialized
        if (PlayerProgress.Instance != null)
        {
            PlayerProgress.Instance.OnGoldChanged += OnGoldChanged;
            PlayerProgress.Instance.OnTrophiesChanged += OnTrophiesChanged;
        }
        UpdateUI();
    }

    void OnDestroy()
    {
        if (PlayerProgress.Instance != null)
        {
            PlayerProgress.Instance.OnGoldChanged -= OnGoldChanged;
            PlayerProgress.Instance.OnTrophiesChanged -= OnTrophiesChanged;
        }
    }

    private void OnGoldChanged(int newGold)
    {
        UpdateUI();
    }

    private void OnTrophiesChanged(int newTrophies)
    {
        UpdateUI();
    }

    void UpdateUI()
    {
        var pp = PlayerProgress.Instance;
        if (goldText != null)
            goldText.text = pp != null ? $"Gold: {pp.gold}" : "Gold: 0";
        if (trophiesText != null)
            trophiesText.text = pp != null ? $"Trophies: {pp.trophies}" : "Trophies: 0";
    }
}
