using TMPro;
using UnityEngine;

public class GoldTrophyUI : MonoBehaviour
{
    public TMP_Text goldText;
    public TMP_Text trophiesText;
    public TMP_Text usernameText;

    private string lastKnownUsername = "";

    void Start()
    {
        // Subscribe to events after PlayerProgress.Instance is initialized
        if (PlayerProgress.Instance != null)
        {
            PlayerProgress.Instance.OnGoldChanged += OnGoldChanged;
            PlayerProgress.Instance.OnTrophiesChanged += OnTrophiesChanged;
            
            // Store initial username
            lastKnownUsername = PlayerProgress.Instance.GetUsername();
        }
        UpdateUI();
    }

    void Update()
    {
        // Check for username changes every frame for immediate updates
        if (PlayerProgress.Instance != null)
        {
            string currentUsername = PlayerProgress.Instance.GetUsername();
            if (currentUsername != lastKnownUsername)
            {
                lastKnownUsername = currentUsername;
                UpdateUsernameDisplay();
            }
        }
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
        
        UpdateUsernameDisplay();
    }
    
    void UpdateUsernameDisplay()
    {
        if (usernameText != null)
        {
            var pp = PlayerProgress.Instance;
            string username = pp != null ? pp.GetUsername() : "";
            if (string.IsNullOrEmpty(username))
            {
                usernameText.text = "Guest Player";
            }
            else
            {
                usernameText.text = username;
            }
        }
    }
}
