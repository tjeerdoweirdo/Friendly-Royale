using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Simple prefab for the drag preview that follows the mouse cursor during card dragging.
/// This gives visual feedback to the player about what card they're placing.
/// </summary>
public class CardDragPreview : MonoBehaviour
{
    [Header("Visual Elements")]
    public Image cardIcon;
    public TMP_Text costText;
    public Image backgroundImage;
    
    [Header("Animation")]
    public float pulseSpeed = 2f;
    public float pulseAmount = 0.1f;
    
    private Vector3 baseScale;
    private Card currentCard;
    
    void Awake()
    {
        baseScale = transform.localScale;
    }
    
    void Update()
    {
        // Gentle pulsing animation to make the preview more noticeable
        float pulse = 1f + Mathf.Sin(Time.time * pulseSpeed) * pulseAmount;
        transform.localScale = baseScale * pulse;
    }
    
    /// <summary>
    /// Update the preview with card data
    /// </summary>
    public void SetCard(Card card)
    {
        currentCard = card;
        
        if (cardIcon != null && card.icon != null)
        {
            cardIcon.sprite = card.icon;
        }
        
        if (costText != null)
        {
            costText.text = card.coinCost.ToString();
        }
        
        // Set background color based on card rarity
        if (backgroundImage != null)
        {
            Color rarityColor = GetRarityColor(card.rarity);
            backgroundImage.color = rarityColor;
        }
    }
    
    /// <summary>
    /// Update the tint color based on placement validity
    /// </summary>
    public void SetValidityTint(bool isValid)
    {
        Color tintColor = isValid ? Color.white : Color.red;
        
        if (cardIcon != null)
        {
            cardIcon.color = tintColor;
        }
        
        if (backgroundImage != null)
        {
            Color currentColor = backgroundImage.color;
            currentColor.r *= tintColor.r;
            currentColor.g *= tintColor.g;
            currentColor.b *= tintColor.b;
            backgroundImage.color = currentColor;
        }
    }
    
    private Color GetRarityColor(CardRarity rarity)
    {
        switch (rarity)
        {
            case CardRarity.Common: return new Color(0.85f, 0.85f, 0.85f, 0.8f); // light gray
            case CardRarity.Rare: return new Color(0.2f, 0.6f, 1f, 0.8f); // blue
            case CardRarity.Epic: return new Color(0.7f, 0.2f, 0.9f, 0.8f); // purple
            case CardRarity.Legendary: return new Color(1f, 0.7f, 0.2f, 0.8f); // orange/gold
            default: return new Color(1f, 1f, 1f, 0.8f);
        }
    }
}