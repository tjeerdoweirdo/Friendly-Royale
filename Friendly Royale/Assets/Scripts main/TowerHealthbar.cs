using UnityEngine;

using UnityEngine.UI;
using TMPro;

/// <summary>
/// Screen-space health bar that follows a world target using a UI Slider.
/// Assign this prefab to Tower.healthBarPrefab in the inspector.
/// Note: If you use TextMeshPro, replace the 'Text' field with TMP_Text in your project.
/// </summary>
public class TowerHealthBar : MonoBehaviour
{
    [Tooltip("Assign a UI Slider (non-interactable) as the visual health bar.")]
    public Slider slider;

    [Tooltip("Text to display tower info.")]
    public TMP_Text tex;

    [Tooltip("Check if this is an enemy health bar.")]
    public bool isEnemyHealthBar = false;

    Transform target;
    int maxHealth = 1;
    int currentHealth = 1;
    public Vector3 offset = new Vector3(0f, 2f, 0f);
    private Camera cachedCamera;

    /// <summary>
    /// Attach the UI to a world target and configure max HP.
    /// Slider.maxValue will be set to maxHP automatically.
    /// </summary>
    public void AttachTo(Transform followTarget, int maxHP, string towerName = null, bool isEnemy = false)
    {
        target = followTarget;
        maxHealth = Mathf.Max(1, maxHP);
        currentHealth = maxHealth;
        isEnemyHealthBar = isEnemy;

        if (slider != null)
        {
            slider.minValue = 0;
            slider.maxValue = maxHealth;
            slider.value = maxHealth;
            slider.interactable = false;
        }

        UpdateTex();
    }

    /// <summary>
    /// Update slider value. Call from Tower when HP changes.
    /// </summary>
    public void UpdateHealth(int currentHP)
    {
        int oldHealth = currentHealth;
        currentHealth = Mathf.Clamp(currentHP, 0, maxHealth);
        
        Debug.Log($"[TowerHealthBar] UpdateHealth called: {oldHealth} -> {currentHealth}/{maxHealth}");
        
        if (slider != null)
        {
            slider.value = currentHealth;
            Debug.Log($"[TowerHealthBar] Slider updated to: {slider.value}");
        }
        else
        {
            Debug.LogWarning("[TowerHealthBar] Slider is null!");
        }
        
        UpdateTex();
    }

    void UpdateTex()
    {
        if (tex != null)
        {
            string label = (isEnemyHealthBar ? "Enemy Tower" : "Player Tower") + $" {currentHealth}/{maxHealth}";
            tex.text = label;
            Debug.Log($"[TowerHealthBar] Text updated to: {label}");
        }
        else
        {
            Debug.LogWarning("[TowerHealthBar] Text component is null!");
        }
    }

    void LateUpdate()
    {
        if (target == null)
        {
            Debug.Log("[TowerHealthBar] Target is null, destroying health bar");
            Destroy(gameObject);
            return;
        }

        // Continuously sync with target's health (Tower or KingTower)
        var tower = target.GetComponent<Tower>();
        if (tower != null)
        {
            // Force sync if health differs
            if (tower.CurrentHealth != currentHealth)
            {
                Debug.Log($"[TowerHealthBar] Health desync detected! Tower: {tower.CurrentHealth}, UI: {currentHealth}. Forcing sync.");
                UpdateHealth(tower.CurrentHealth);
            }
        }

        // Resolve an active camera: prefer Camera.main, otherwise find any enabled camera
        if (cachedCamera == null || !cachedCamera.isActiveAndEnabled)
        {
            cachedCamera = Camera.main;
            if (cachedCamera == null)
            {
                var cams = FindObjectsByType<Camera>(FindObjectsSortMode.None);
                foreach (var c in cams)
                {
                    if (c != null && c.isActiveAndEnabled)
                    {
                        cachedCamera = c;
                        break;
                    }
                }
            }
        }

        var cam = cachedCamera;
        if (cam == null)
        {
            // no camera, hide UI
            gameObject.SetActive(false);
            return;
        }

        Vector3 worldPos = target.position + offset;
        Vector3 screenPos = cam.WorldToScreenPoint(worldPos);

        // Hide if behind the camera
        if (screenPos.z < 0f)
        {
            if (gameObject.activeSelf) gameObject.SetActive(false);
            return;
        }
        else if (!gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }

        transform.position = screenPos;
    }
}
