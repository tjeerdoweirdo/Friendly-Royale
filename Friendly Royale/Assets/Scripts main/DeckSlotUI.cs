using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// UI used to display a card in the current deck placeholders.
/// - shows icon, name, level and rarity background
/// - has an Unequip button (wired here)
/// - exposes a public infoButton so the selector (FullDeckSelector6) can wire the global info panel
/// </summary>
public class CurrentDeckCardUI : MonoBehaviour
{
    [Header("Visuals")]
    public Image iconImage;
    public TMP_Text nameText;
    public TMP_Text levelText;               // shows "Lv X"
    public Image rarityBackground;           // color the background by rarity

    [Header("Buttons")]
    public Button unequipButton;
    public Button infoButton;                // NOTE: selector will wire this to open the global info panel

    [Header("Rarity Colors")]
    public Color commonColor = new Color(0.8f, 0.8f, 0.8f);
    public Color rareColor   = new Color(0.3f, 0.6f, 1f);
    public Color epicColor   = new Color(0.7f, 0.3f, 1f);
    public Color legendaryColor = new Color(1f, 0.6f, 0.1f);

    // runtime
    private Card card;
    private FullDeckSelector6 selector;
    private int slotIndex;

    /// <summary>
    /// Setup the UI for this slot.
    /// </summary>
    public void Setup(Card c, FullDeckSelector6 sel, int slot)
    {
        // clear old unequip listener (we manage unequip here)
        if (unequipButton != null) unequipButton.onClick.RemoveAllListeners();

        // NOTE: we intentionally DO NOT remove or add listeners to infoButton here.
        // FullDeckSelector6 will take responsibility for wiring infoButton to its global info panel.

        card = c;
        selector = sel;
        slotIndex = slot;

        // icon / name
        if (iconImage != null) iconImage.sprite = (card != null) ? card.icon : null;
        if (nameText != null) nameText.text = (card != null) ? card.cardName : "";

        // level (use PlayerProgress if available)
        if (levelText != null)
        {
            int lvl = 1;
            if (card != null && PlayerProgress.Instance != null)
            {
                // Use selected arena if available
                string arenaId = "default";
                if (sel != null && sel.selectedArena != null)
                    arenaId = sel.selectedArena.arenaID;
                lvl = PlayerProgress.Instance.GetCardLevel(card.cardID, arenaId);
            }
            levelText.text = card != null ? $"Lv {lvl}" : "";
            levelText.gameObject.SetActive(card != null);
        }

        // rarity background
        if (rarityBackground != null)
        {
            if (card != null) rarityBackground.color = GetColorForRarity(card.rarity);
            rarityBackground.gameObject.SetActive(card != null);
        }

        // unequip button
        if (unequipButton != null)
            unequipButton.onClick.AddListener(OnUnequipClicked);
    }

    void OnUnequipClicked()
    {
        if (selector == null) return;
        selector.UnequipSlot(slotIndex);
    }

    private Color GetColorForRarity(CardRarity r)
    {
        switch (r)
        {
            case CardRarity.Common: return commonColor;
            case CardRarity.Rare: return rareColor;
            case CardRarity.Epic: return epicColor;
            case CardRarity.Legendary: return legendaryColor;
            default: return Color.white;
        }
    }

    void OnDestroy()
    {
        if (unequipButton != null) unequipButton.onClick.RemoveAllListeners();
        // intentionally do NOT remove listeners on infoButton here — selector manages it
    }
}
