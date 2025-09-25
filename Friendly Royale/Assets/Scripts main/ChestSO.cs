using UnityEngine;

public enum ChestRarity
{
    Bronze,
    Silver,
    Gold,
    Epic,
    Legendary
}

[CreateAssetMenu(menuName = "CR/Chest")]
public class ChestSO : ScriptableObject
{
    public string chestID;
    public string displayName;
    public ChestRarity rarity = ChestRarity.Bronze;
    public Sprite icon;
    public int openTimeSeconds = 30; // seconds required to unlock normally
    public int minGold = 25;
    public int maxGold = 150;
    public int minShardCount = 5;
    public int maxShardCount = 50;
    public int shardCardCount = 2; // number of different cards to give shards for
}
