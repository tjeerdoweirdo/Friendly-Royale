using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Card list entry that exposes only an Equip button.
/// </summary>
public class CardEntryEquipUI : MonoBehaviour
{
    public Image iconImage;
    public TMP_Text nameText;
    public TMP_Text costText;
    public Button equipButton;

    [Header("Info Button")]
    public Button infoButton; // Assign in inspector

    [Header("Rarity Panel")]
    public Image rarityPanel; // Assign in inspector

    // Optional: Expose rarity colors in inspector
    public Color commonColor = Color.gray;
    public Color rareColor = Color.blue;
    public Color epicColor = new Color(0.6f, 0f, 0.8f); // purple
    public Color legendaryColor = new Color(1f, 0.6f, 0f); // orange/gold

    Card card;
    FullDeckSelector6 selector;
    Arena arena;

    public void Setup(Card c, FullDeckSelector6 sel, Arena a)
    {
        card = c;
        selector = sel;
        arena = a;

        if (iconImage != null) iconImage.sprite = c.icon;
        if (nameText != null) nameText.text = c.cardName;
        if (costText != null) costText.text = c.coinCost.ToString();

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

        if (equipButton != null)
        {
            equipButton.onClick.RemoveAllListeners();
            equipButton.onClick.AddListener(OnEquipClicked);
        }

        if (infoButton != null)
        {
            infoButton.onClick.RemoveAllListeners();
            infoButton.onClick.AddListener(OnInfoClicked);
        }
    }

    void OnEquipClicked()
    {
        if (selector == null || card == null) return;
        bool ok = selector.EquipCard(card);
        if (!ok)
        {
            // basic feedback - replace with better UI if needed
            Debug.Log("Cannot equip card (deck full or already equipped).");
        }
    }

    void OnInfoClicked()
    {
        if (card == null || selector == null) return;
        selector.ShowInfoForCard(card);
    }
}