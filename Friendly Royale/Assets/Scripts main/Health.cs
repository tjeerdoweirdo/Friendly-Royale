using UnityEngine;
using System;

using UnityEngine.UI;
using TMPro;

public class Health : MonoBehaviour
{
    [Header("Health")]
    public float maxHealth = 100f;
    public float currentHealth;
    public bool isDead => currentHealth <= 0f;

    [Header("Destroy On Death (optional)")]
    [Tooltip("Any extra GameObjects to destroy when this object dies.")]
    public GameObject[] destroyOnDeath;

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

    // Faction color (optional, for color coding)
    Color playerColor = new Color(0.2f, 0.85f, 0.2f); // green
    Color enemyColor = new Color(0.95f, 0.2f, 0.2f);  // red

    public event Action OnDied;

    void Awake()
    {
        if (maxHealth <= 0f) maxHealth = 100f;
        currentHealth = Mathf.Clamp(currentHealth == 0f ? maxHealth : currentHealth, 0, maxHealth);
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
        else
        {
            if (!Mathf.Approximately(displayedHealthValue, currentHealth))
            {
                displayedHealthValue = currentHealth;
                UpdateUIUsingDisplayedValue();
            }
        }
    }

    public void TakeDamage(float amount)
    {
        if (isDead) return;
        currentHealth -= amount;
        if (currentHealth < 0f) currentHealth = 0f;

        if (!smoothUI)
        {
            displayedHealthValue = currentHealth;
            SyncUIImmediate();
        }
        else
        {
            UpdateUIUsingDisplayedValue();
        }

        if (currentHealth <= 0f)
        {
            currentHealth = 0f;
            Die();
        }
    }

    public void Heal(float amount)
    {
        if (isDead) return;
        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);

        if (!smoothUI)
        {
            displayedHealthValue = currentHealth;
            SyncUIImmediate();
        }
        else
        {
            UpdateUIUsingDisplayedValue();
        }
    }

    void Die()
    {
        OnDied?.Invoke();
        if (destroyOnDeath != null)
        {
            foreach (var go in destroyOnDeath)
            {
                if (go != null)
                    Destroy(go);
            }
        }
        Destroy(gameObject);
    }

    void SetFactionUI()
    {
        // Try to get faction from Building, fallback to green
        bool isEnemy = false;
        string displayName = unitName;
        var building = GetComponent<Building>();
        if (building != null)
        {
            isEnemy = (building.faction == Unit.Faction.Enemy);
            if (string.IsNullOrEmpty(unitName))
                displayName = building.gameObject.name;
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

    public void SyncUIImmediate()
    {
        SetFactionUI();
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
        currentHealth = Mathf.Clamp((int)currentHealth, 0, (int)maxHealth);
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
