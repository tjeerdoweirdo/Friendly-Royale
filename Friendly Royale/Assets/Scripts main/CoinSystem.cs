using UnityEngine;
using UnityEngine.UI;
using TMPro; // Add this line

public class CoinSystem : MonoBehaviour
{
    // Coin change notification for UI
    public System.Action<int> OnCoinsChanged;
    public static CoinSystem Instance;

    [Header("Coin settings")]
    public int maxCoins = 10;
    public float regenTimePerCoin = 1.2f; // seconds to regenerate one coin
    public int startCoins = 4;

    [HideInInspector] public int currentCoins;
    private float timer = 0f;

    [Header("UI")]
    public Slider coinSlider; // optional, 0..max
    public TMP_Text coinText; // Changed to TMP_Text

    void Awake()
    {
        if (Instance == null) {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        } else {
            Destroy(gameObject);
        }
    }

    void Start()
    {
        currentCoins = startCoins;
        timer = 0f;
        UpdateUI();
    }

    void Update()
    {
        if (currentCoins >= maxCoins) return;
        timer += Time.deltaTime;
        if (timer >= regenTimePerCoin)
        {
            timer -= regenTimePerCoin;
            currentCoins = Mathf.Min(maxCoins, currentCoins + 1);
            UpdateUI();
        }
    }

    /// <summary>
    /// Attempts to spend coins. Returns true if sufficient coins existed and were spent.
    /// </summary>
    public bool SpendCoins(int amount)
    {
        if (currentCoins < amount) return false;
        currentCoins -= amount;
        UpdateUI();
        OnCoinsChanged?.Invoke(currentCoins);
        return true;
    }

    /// <summary>
    /// Add coins (capped by maxCoins).
    /// </summary>
    public void AddCoins(int amount)
    {
        currentCoins = Mathf.Min(maxCoins, currentCoins + amount);
        UpdateUI();
        OnCoinsChanged?.Invoke(currentCoins);
    }

    void UpdateUI()
    {
        if (coinSlider != null)
        {
            coinSlider.maxValue = maxCoins;
            coinSlider.value = currentCoins;
        }
        if (coinText != null)
        {
            coinText.text = $"{currentCoins} / {maxCoins}";
        }
        // Force UI refresh for HandUI if present
        if (DeckManager.Instance != null && DeckManager.Instance.handUI != null)
        {
            DeckManager.Instance.handUI.RefreshHand();
        }
    }
}