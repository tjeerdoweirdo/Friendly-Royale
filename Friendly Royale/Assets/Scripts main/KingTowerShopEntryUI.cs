using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Dedicated shop entry UI for the King Tower upgrade. Keeps layout and logic
/// self-contained so you can design a separate prefab from card entries.
/// </summary>
public class KingTowerShopEntryUI : MonoBehaviour
{
    [Header("UI")]
    public Image iconImage;
    public TMP_Text nameText;
    public TMP_Text levelText;
    public TMP_Text costText;
    public TMP_Text maxLevelText; // optional: show when level is max
    public Button upgradeButton;

    [Header("Config")]
    public string displayName = "King Tower";

    void OnEnable()
    {
        Refresh();
        if (upgradeButton != null)
        {
            upgradeButton.onClick.RemoveAllListeners();
            upgradeButton.onClick.AddListener(OnUpgradeClicked);
        }
    }

    public void Refresh()
    {
        int level = ShopManager.Instance != null ? ShopManager.Instance.GetKingTowerLevel() : 1;
        bool atMax = ShopManager.Instance != null && level >= Mathf.Max(1, ShopManager.Instance.kingTowerMaxLevel);
        int cost = (!atMax && ShopManager.Instance != null) ? ShopManager.Instance.GetKingTowerUpgradeCost() : 0;

        if (nameText != null) nameText.text = displayName;
        if (levelText != null) levelText.text = $"Level: {level}";
        if (costText != null) costText.text = atMax ? "Max Level" : $"Cost: {cost}";
        if (maxLevelText != null) maxLevelText.gameObject.SetActive(atMax);

        if (upgradeButton != null)
        {
            bool canUpgrade = ShopManager.Instance != null && ShopManager.Instance.CanUpgradeKingTower();
            upgradeButton.interactable = !atMax && canUpgrade;
        }
    }

    private void OnUpgradeClicked()
    {
        if (ShopManager.Instance == null) return;
        if (ShopManager.Instance.TryUpgradeKingTower())
        {
            Refresh();
        }
    }
}
