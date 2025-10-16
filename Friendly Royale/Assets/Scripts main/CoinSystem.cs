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
    [Tooltip("If true this CoinSystem persists across scene loads. If false it will reset per scene.")]
    public bool persistAcrossScenes = false;

    [HideInInspector] public int currentCoins;
    private float timer = 0f;

    [Header("UI")]
    public Slider coinSlider; // expected specific slider
    public TMP_Text coinText; // expected specific TMP text
    [Tooltip("Optional: name of the Slider GameObject to auto-bind if reference missing.")]
    public string sliderObjectName = "CoinSlider";
    [Tooltip("Optional: name of the TMP Text GameObject to auto-bind if reference missing.")]
    public string textObjectName = "CoinText";
    [Tooltip("If true, will search inactive objects too when rebinding.")]
    public bool searchInactive = true;
    [Tooltip("If true, will log detailed binding info.")]
    public bool verboseBindingLogs = false;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            if (persistAcrossScenes)
            {
                DontDestroyOnLoad(gameObject);
            }
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        }
        else
        {
            // If existing instance is persistent and new one should take over (e.g., you changed the setting), replace old.
            if (Instance != this)
            {
                if (persistAcrossScenes && !Instance.persistAcrossScenes)
                {
                    // Replace old with new persistent configuration
                    Destroy(Instance.gameObject);
                    Instance = this;
                    DontDestroyOnLoad(gameObject);
                }
                else
                {
                    Destroy(gameObject);
                }
            }
        }
    }

    void Start()
    {
        TryBindUI();
        currentCoins = startCoins;
        timer = 0f;
        UpdateUI();
        OnCoinsChanged?.Invoke(currentCoins);
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
            OnCoinsChanged?.Invoke(currentCoins);
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
        if (coinSlider == null || coinText == null)
        {
            // Attempt lazy rebind if lost (e.g., new scene loaded before listener fired)
            TryBindUI();
        }
        if (coinSlider != null)
        {
            if (coinSlider.maxValue != maxCoins) coinSlider.maxValue = maxCoins;
            if (!Mathf.Approximately(coinSlider.value, currentCoins)) coinSlider.value = currentCoins;
        }
        if (coinText != null)
        {
            string desired = $"{currentCoins} / {maxCoins}";
            if (coinText.text != desired) coinText.text = desired;
        }
        // Force UI refresh for HandUI if present
        if (DeckManager.Instance != null && DeckManager.Instance.handUI != null)
        {
            DeckManager.Instance.handUI.RefreshHand();
        }
    }

    private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        // Rebind UI when entering a gameplay scene with a new HUD
        TryBindUI();
        UpdateUI();
        if (verboseBindingLogs) Debug.Log($"[CoinSystem] SceneLoaded -> Rebound UI in scene {scene.name}");
    }

    public void TryBindUI()
    {
        if (coinSlider == null)
        {
            coinSlider = FindByName<Slider>(sliderObjectName) ?? FindSpecificSliderFallback();
            if (verboseBindingLogs) Debug.Log("[CoinSystem] Slider bound to: " + (coinSlider ? coinSlider.name : "<none>"));
        }
        if (coinText == null)
        {
            coinText = FindByName<TMP_Text>(textObjectName) ?? FindSpecificTextFallback();
            if (verboseBindingLogs) Debug.Log("[CoinSystem] Text bound to: " + (coinText ? coinText.name : "<none>"));
        }
    }

    T FindByName<T>(string objName) where T : Component
    {
        if (string.IsNullOrEmpty(objName)) return null;
        var all = Resources.FindObjectsOfTypeAll<T>();
        foreach (var comp in all)
        {
            if (comp.name == objName && (searchInactive || comp.gameObject.scene.isLoaded))
            {
                // Skip components on prefabs not in scene unless desired
                return comp;
            }
        }
        return null;
    }

    Slider FindSpecificSliderFallback()
    {
        // Fallback: pick the slider whose max matches or whose parent name hints coins/elixir
        Slider best = null;
        foreach (var s in Resources.FindObjectsOfTypeAll<Slider>())
        {
            if (!searchInactive && !s.gameObject.scene.isLoaded) continue;
            string n = s.name.ToLower();
            if (n.Contains("coin") || n.Contains("elixir")) { best = s; break; }
        }
        return best;
    }

    TMP_Text FindSpecificTextFallback()
    {
        TMP_Text best = null;
        foreach (var t in Resources.FindObjectsOfTypeAll<TMP_Text>())
        {
            if (!searchInactive && !t.gameObject.scene.isLoaded) continue;
            string n = t.name.ToLower();
            if (n.Contains("coin") || n.Contains("elixir")) { best = t; break; }
        }
        return best;
    }

    public void ForceRebindAndRefresh()
    {
        coinSlider = null; coinText = null;
        TryBindUI();
        UpdateUI();
        if (verboseBindingLogs) Debug.Log("[CoinSystem] ForceRebindAndRefresh executed");
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        }
    }
}