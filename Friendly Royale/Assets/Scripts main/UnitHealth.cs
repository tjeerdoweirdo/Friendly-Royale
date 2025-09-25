using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using TMPro;

public class UnitHealth : MonoBehaviour
{
    [Header("Health")]
    public int maxHealth = 1000;
    public int currentHealth;

    [Header("Events")]
    public UnityEvent onDie;
    public UnityEvent onDamageTaken;

    [Header("UI (optional)")]
    public Slider healthSlider;
    public TextMeshProUGUI healthText;
    public bool smoothUI = true;
    public float uiSmoothSpeed = 8f;


    [Header("Unit Name UI (optional)")]
    public TextMeshProUGUI unitNameText;
    public string unitName;

    [Header("Card Level UI (optional)")]
    public TextMeshProUGUI cardLevelText;
    [Tooltip("Set this externally to display the card's level")] public int cardLevel = 1;

    float displayedHealthValue;
    public bool IsAlive => currentHealth > 0;

    Color playerColor = new Color(0.2f, 0.85f, 0.2f); // green
    Color enemyColor = new Color(0.95f, 0.2f, 0.2f);  // red

    void Awake()
    {
        currentHealth = Mathf.Clamp(currentHealth == 0 ? maxHealth : currentHealth, 0, maxHealth);
        displayedHealthValue = currentHealth;
        SetFactionUI();
        SyncUIImmediate();
    }

    void Start()
    {
        // In case faction is set after Awake (e.g. by spawner)
        SetFactionUI();
        SyncUIImmediate();
    }

    public void RefreshFactionUI()
    {
        SetFactionUI();
        SyncUIImmediate();
    }

    void SetFactionUI()
    {
        Unit unit = GetComponent<Unit>();
        bool isEnemy = false;
        string displayName = unitName;

        if (unit != null)
        {
            isEnemy = (unit.faction == Unit.Faction.Enemy);
            if (string.IsNullOrEmpty(unitName))
                displayName = unit.gameObject.name;
        }
        else
        {
            isEnemy = false;
            if (string.IsNullOrEmpty(unitName))
                displayName = gameObject.name;
        }

        // Set name text
        if (unitNameText != null)
        {
            unitNameText.text = displayName;
            unitNameText.color = isEnemy ? enemyColor : playerColor;
            unitNameText.fontStyle = isEnemy ? FontStyles.Bold : FontStyles.Normal;
        }

        // Set card level text
        if (cardLevelText != null)
        {
            cardLevelText.text = $"Lv. {Mathf.Max(1, cardLevel)}";
            cardLevelText.color = isEnemy ? enemyColor : playerColor;
            cardLevelText.fontStyle = isEnemy ? FontStyles.Bold : FontStyles.Normal;
        }

        // Set health bar color
        if (healthSlider != null && healthSlider.fillRect != null)
        {
            Image fillImg = healthSlider.fillRect.GetComponent<Image>();
            if (fillImg != null)
                fillImg.color = isEnemy ? enemyColor : playerColor;
        }

        // Set health text color
        if (healthText != null)
        {
            healthText.color = isEnemy ? enemyColor : playerColor;
            healthText.fontStyle = isEnemy ? FontStyles.Bold : FontStyles.Normal;
        }
    }

    void Update()
    {
        if (healthSlider == null && healthText == null && unitNameText == null) return;

        if (smoothUI)
        {
            if (!Mathf.Approximately(displayedHealthValue, currentHealth))
            {
                displayedHealthValue = Mathf.MoveTowards(displayedHealthValue, currentHealth, uiSmoothSpeed * Time.deltaTime * maxHealth);
                UpdateUIUsingDisplayedValue();
            }
        }
    }

    public void TakeDamage(int amount, GameObject source = null)
    {
        if (amount <= 0 || currentHealth <= 0) return;

        currentHealth -= amount;
        if (currentHealth < 0) currentHealth = 0;

        onDamageTaken?.Invoke();

        if (!smoothUI)
        {
            displayedHealthValue = currentHealth;
            SyncUIImmediate();
        }

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    public void Heal(int amount)
    {
        if (amount <= 0 || currentHealth <= 0) return;

        currentHealth += amount;
        if (currentHealth > maxHealth) currentHealth = maxHealth;

        if (!smoothUI)
        {
            displayedHealthValue = currentHealth;
            SyncUIImmediate();
        }
    }

    protected virtual void Die()
    {
        onDie?.Invoke();
        Destroy(gameObject);
    }

    public void SyncUIImmediate()
    {
        SetFactionUI(); // Always update colors/styles

        if (healthSlider != null)
        {
            healthSlider.maxValue = Mathf.Max(1, maxHealth);
            healthSlider.value = displayedHealthValue;
        }

        if (healthText != null)
        {
            healthText.text = $"{Mathf.RoundToInt(displayedHealthValue)} / {maxHealth}";
        }

        if (cardLevelText != null)
        {
            cardLevelText.text = $"Lv. {Mathf.Max(1, cardLevel)}";
        }
    }

    void UpdateUIUsingDisplayedValue()
    {
        if (healthSlider != null)
        {
            healthSlider.value = displayedHealthValue;
        }
        if (healthText != null)
        {
            healthText.text = $"{Mathf.RoundToInt(displayedHealthValue)} / {maxHealth}";
        }
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        maxHealth = Mathf.Max(1, maxHealth);
        currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);
        displayedHealthValue = currentHealth;
        if (!Application.isPlaying)
        {
            SetFactionUI();
            if (healthSlider != null)
            {
                healthSlider.maxValue = maxHealth;
                healthSlider.value = currentHealth;
            }
            if (healthText != null)
            {
                healthText.text = $"{currentHealth} / {maxHealth}";
            }
            if (cardLevelText != null)
            {
                cardLevelText.text = $"Lv. {Mathf.Max(1, cardLevel)}";
            }
        }
    }
#endif
}