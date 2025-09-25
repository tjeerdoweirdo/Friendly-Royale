using UnityEngine;

public static class CardUIUtils
{
    // colors are examples; tweak to your art style
    public static Color GetColorForRarity(CardRarity r)
    {
        switch (r)
        {
            case CardRarity.Common: return new Color(0.8f, 0.8f, 0.8f);
            case CardRarity.Rare: return new Color(0.3f, 0.6f, 1f);
            case CardRarity.Epic: return new Color(0.7f, 0.3f, 1f);
            case CardRarity.Legendary: return new Color(1f, 0.6f, 0.1f);
            default: return Color.white;
        }
    }
}
