using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;

/// <summary>
/// Networked version of UnitHealth that synchronizes health across all clients.
/// Only the server has authority over health changes to prevent cheating.
/// </summary>
public class NetworkUnitHealth : NetworkBehaviour
{
    [Header("Health")]
    public int maxHealth = 1000;
    
    [Header("Events")]
    public UnityEvent onDie;
    public UnityEvent onDamageTaken;
    public UnityEvent onHealed;

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
    [Tooltip("Set this externally to display the card's level")] 
    public int cardLevel = 1;

    [Header("Visual Effects")]
    [Tooltip("Particle effect to spawn when taking damage")]
    public GameObject damageEffect;
    [Tooltip("Particle effect to spawn when healing")]
    public GameObject healEffect;
    [Tooltip("Particle effect to spawn when dying")]
    public GameObject deathEffect;

    [Header("Audio")]
    public AudioClip damageSound;
    public AudioClip healSound;
    public AudioClip deathSound;
    
    // Network Variables
    private NetworkVariable<int> networkCurrentHealth = new NetworkVariable<int>();
    private NetworkVariable<bool> networkIsAlive = new NetworkVariable<bool>(true);
    
    // Local variables
    private float displayedHealthValue;
    private AudioSource audioSource;
    private Unit.Faction unitFaction;
    
    // Colors
    private Color playerColor = new Color(0.2f, 0.85f, 0.2f); // green
    private Color enemyColor = new Color(0.95f, 0.2f, 0.2f);  // red

    // Events
    public System.Action OnDeath;
    public System.Action<int> OnDamageTaken;
    public System.Action<int> OnHealed;

    // Properties
    public bool IsAlive => networkIsAlive.Value;
    public int CurrentHealth => networkCurrentHealth.Value;
    public float HealthPercentage => (float)networkCurrentHealth.Value / maxHealth;

    private void Awake()
    {
        // Setup audio source
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }
        
        // Try to get faction from parent Unit or NetworkUnit
        NetworkUnit networkUnit = GetComponent<NetworkUnit>();
        if (networkUnit != null)
        {
            unitFaction = (Unit.Faction)(int)networkUnit.faction;
        }
        else
        {
            Unit unit = GetComponent<Unit>();
            if (unit != null)
            {
                unitFaction = unit.faction;
            }
        }
    }

    public override void OnNetworkSpawn()
    {
        // Initialize network variables
        if (IsServer)
        {
            networkCurrentHealth.Value = maxHealth;
            networkIsAlive.Value = true;
        }

        // Subscribe to network variable changes
        networkCurrentHealth.OnValueChanged += OnHealthChanged;
        networkIsAlive.OnValueChanged += OnAliveStateChanged;

        // Initialize display
        displayedHealthValue = networkCurrentHealth.Value;
        SetFactionUI();
        SyncUIImmediate();
    }

    public override void OnNetworkDespawn()
    {
        // Unsubscribe from events
        networkCurrentHealth.OnValueChanged -= OnHealthChanged;
        networkIsAlive.OnValueChanged -= OnAliveStateChanged;
    }

    private void Update()
    {
        // Update UI smoothly
        if (smoothUI && healthSlider != null)
        {
            displayedHealthValue = Mathf.Lerp(displayedHealthValue, networkCurrentHealth.Value, Time.deltaTime * uiSmoothSpeed);
            UpdateHealthUI(displayedHealthValue);
        }
    }

    /// <summary>
    /// Take damage. Only the server can modify health.
    /// </summary>
    public void TakeDamage(int damage)
    {
        if (!IsServer || !networkIsAlive.Value) return;
        
        if (damage <= 0) return;

        int newHealth = Mathf.Max(0, networkCurrentHealth.Value - damage);
        networkCurrentHealth.Value = newHealth;

        // Trigger damage effects on all clients
        OnDamageTakenClientRpc(damage);

        // Check if unit died
        if (newHealth <= 0)
        {
            Die();
        }
    }

    /// <summary>
    /// Heal the unit. Only the server can modify health.
    /// </summary>
    public void Heal(int healAmount)
    {
        if (!IsServer || !networkIsAlive.Value) return;
        
        if (healAmount <= 0) return;

        int newHealth = Mathf.Min(maxHealth, networkCurrentHealth.Value + healAmount);
        int actualHealing = newHealth - networkCurrentHealth.Value;
        
        if (actualHealing > 0)
        {
            networkCurrentHealth.Value = newHealth;
            
            // Trigger heal effects on all clients
            OnHealedClientRpc(actualHealing);
        }
    }

    /// <summary>
    /// Instantly kill the unit. Only the server can do this.
    /// </summary>
    public void Die()
    {
        if (!IsServer || !networkIsAlive.Value) return;

        networkCurrentHealth.Value = 0;
        networkIsAlive.Value = false;

        // Trigger death effects on all clients
        OnDeathClientRpc();
    }

    /// <summary>
    /// Set max health and optionally restore to full health.
    /// </summary>
    public void SetMaxHealth(int newMaxHealth, bool restoreToFull = false)
    {
        if (!IsServer) return;

        maxHealth = newMaxHealth;
        
        if (restoreToFull)
        {
            networkCurrentHealth.Value = maxHealth;
        }
        else
        {
            // Clamp current health to new max
            networkCurrentHealth.Value = Mathf.Min(networkCurrentHealth.Value, maxHealth);
        }
    }

    /// <summary>
    /// Fully restore health.
    /// </summary>
    public void RestoreToFull()
    {
        if (!IsServer) return;
        
        networkCurrentHealth.Value = maxHealth;
        networkIsAlive.Value = true;
    }

    // Network RPC methods
    [ClientRpc]
    private void OnDamageTakenClientRpc(int damage)
    {
        // Play damage sound
        if (damageSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(damageSound);
        }

        // Spawn damage effect
        if (damageEffect != null)
        {
            Instantiate(damageEffect, transform.position, transform.rotation);
        }

        // Trigger local events
        onDamageTaken?.Invoke();
        OnDamageTaken?.Invoke(damage);
    }

    [ClientRpc]
    private void OnHealedClientRpc(int healAmount)
    {
        // Play heal sound
        if (healSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(healSound);
        }

        // Spawn heal effect
        if (healEffect != null)
        {
            Instantiate(healEffect, transform.position, transform.rotation);
        }

        // Trigger local events
        onHealed?.Invoke();
        OnHealed?.Invoke(healAmount);
    }

    [ClientRpc]
    private void OnDeathClientRpc()
    {
        // Play death sound
        if (deathSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(deathSound);
        }

        // Spawn death effect
        if (deathEffect != null)
        {
            Instantiate(deathEffect, transform.position, transform.rotation);
        }

        // Trigger local events
        onDie?.Invoke();
        OnDeath?.Invoke();
    }

    // Network event handlers
    private void OnHealthChanged(int previousValue, int newValue)
    {
        // Update UI immediately if not using smooth UI
        if (!smoothUI)
        {
            UpdateHealthUI(newValue);
        }
    }

    private void OnAliveStateChanged(bool previousValue, bool newValue)
    {
        if (!newValue && previousValue)
        {
            // Unit just died
            // Additional death handling can go here
        }
    }

    // UI methods
    private void SetFactionUI()
    {
        if (healthSlider == null) return;

        // Set colors based on faction
        Image fillImage = healthSlider.fillRect?.GetComponent<Image>();
        if (fillImage != null)
        {
            switch (unitFaction)
            {
                case Unit.Faction.Player:
                    fillImage.color = playerColor;
                    break;
                case Unit.Faction.Enemy:
                    fillImage.color = enemyColor;
                    break;
            }
        }

        // Set unit name
        if (unitNameText != null && !string.IsNullOrEmpty(unitName))
        {
            unitNameText.text = unitName;
        }

        // Set card level
        if (cardLevelText != null)
        {
            cardLevelText.text = $"Lv.{cardLevel}";
        }
    }

    private void SyncUIImmediate()
    {
        UpdateHealthUI(networkCurrentHealth.Value);
        displayedHealthValue = networkCurrentHealth.Value;
    }

    private void UpdateHealthUI(float healthValue)
    {
        // Update slider
        if (healthSlider != null)
        {
            healthSlider.value = healthValue / maxHealth;
        }

        // Update text
        if (healthText != null)
        {
            healthText.text = $"{Mathf.CeilToInt(healthValue)}/{maxHealth}";
        }
    }

    // Public methods for external access
    public void SetUnitName(string name)
    {
        unitName = name;
        if (unitNameText != null)
        {
            unitNameText.text = name;
        }
    }

    public void SetCardLevel(int level)
    {
        cardLevel = level;
        if (cardLevelText != null)
        {
            cardLevelText.text = $"Lv.{level}";
        }
    }

    public void SetFaction(Unit.Faction faction)
    {
        unitFaction = faction;
        SetFactionUI();
    }

    // Damage over time methods
    public void ApplyDamageOverTime(int damagePerSecond, float duration)
    {
        if (IsServer)
        {
            StartCoroutine(DamageOverTimeCoroutine(damagePerSecond, duration));
        }
    }

    private System.Collections.IEnumerator DamageOverTimeCoroutine(int damagePerSecond, float duration)
    {
        float elapsed = 0f;
        float tickInterval = 1f; // 1 second intervals
        
        while (elapsed < duration && networkIsAlive.Value)
        {
            yield return new WaitForSeconds(tickInterval);
            elapsed += tickInterval;
            
            TakeDamage(damagePerSecond);
        }
    }

    // Healing over time methods
    public void ApplyHealingOverTime(int healPerSecond, float duration)
    {
        if (IsServer)
        {
            StartCoroutine(HealingOverTimeCoroutine(healPerSecond, duration));
        }
    }

    private System.Collections.IEnumerator HealingOverTimeCoroutine(int healPerSecond, float duration)
    {
        float elapsed = 0f;
        float tickInterval = 1f; // 1 second intervals
        
        while (elapsed < duration && networkIsAlive.Value)
        {
            yield return new WaitForSeconds(tickInterval);
            elapsed += tickInterval;
            
            Heal(healPerSecond);
        }
    }
}