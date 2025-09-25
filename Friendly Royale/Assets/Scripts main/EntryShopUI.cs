using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Shop entry UI for upgrading a card in the shop.
/// </summary>
public class EntryShopUI : MonoBehaviour
{
    public Image iconImage;
    public TMP_Text nameText;
    public TMP_Text upgradeLimitText;
    public TMP_Text upgradeCostText;
    public TMP_Text levelText;
    public Button upgradeButton;

    [Header("Rarity Panel")]
    public Image rarityPanel; // Assign in inspector

    // Optional: Expose rarity colors in inspector
    public Color commonColor = Color.gray;
    public Color rareColor = Color.blue;
    public Color epicColor = new Color(0.6f, 0f, 0.8f); // purple
    public Color legendaryColor = new Color(1f, 0.6f, 0f); // orange/gold

    private Card card;
    private int upgradesBought;
    private int upgradeLimit;
    private int upgradeCost;
    private int cardLevel;
    private System.Action onUpgrade;

    public void Setup(Card c, int bought, int limit, int cost, System.Action upgradeAction, int level)
    {
        card = c;
        upgradesBought = bought;
        upgradeLimit = limit;
        upgradeCost = cost;
        cardLevel = level;
        onUpgrade = upgradeAction;

        if (iconImage != null) iconImage.sprite = c.icon;
        if (nameText != null) nameText.text = c.cardName;
        if (upgradeLimitText != null) upgradeLimitText.text = $"Upgrades: {upgradesBought}/{upgradeLimit}";
        if (upgradeCostText != null) upgradeCostText.text = $"Cost: {upgradeCost}";
        if (levelText != null) levelText.text = $"Level: {cardLevel}";

        // Set rarity panel color
        if (rarityPanel != null)
        {
            switch (c.rarity)
            {
                case CardRarity.Common:
                    rarityPanel.color = commonColor;
                    break;
                case CardRarity.Rare:
                    rarityPanel.color = rareColor;
                    break;
                case CardRarity.Epic:
                    rarityPanel.color = epicColor;
                    break;
                case CardRarity.Legendary:
                    rarityPanel.color = legendaryColor;
                    break;
            }
        }

        if (upgradeButton != null)
        {
            upgradeButton.onClick.RemoveAllListeners();
            upgradeButton.onClick.AddListener(OnUpgradeClicked);
            upgradeButton.interactable = upgradesBought < upgradeLimit;
        }
    }

    void OnUpgradeClicked()
    {
        onUpgrade?.Invoke();
    }
}
