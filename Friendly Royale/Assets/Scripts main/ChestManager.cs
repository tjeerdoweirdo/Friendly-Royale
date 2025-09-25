using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class ChestManager : MonoBehaviour
{
    public static ChestManager Instance { get; private set; }

    [Header("References")]
    public DeckManager deckManager; // to look up card list
    public ArenaManager arenaManager;

    [Header("Chest drop weights by rarity (higher => more likely)")]
    public int weightCommon = 60;
    public int weightRare = 25;
    public int weightEpic = 10;
    public int weightLegendary = 5;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public ChestReward OpenChest(ChestSO chest, string arenaID)
    {
        // immediate reward generation (no async wait here).
        // Choose gold
        int gold = Random.Range(chest.minGold, chest.maxGold + 1);

        // choose some cards to give shards for
        var allCards = deckManager.allCards;
        if (allCards == null || allCards.Count == 0)
        {
            // fallback: just give gold
            PlayerProgress.Instance.AddGold(gold);
            return new ChestReward { gold = gold };
        }

        // build weighted list
        List<Card> pool = new List<Card>(allCards);
        // allow only cards which are unlockable in this arena or globally accessible
        // (we'll include cards whose unlockArenaID is empty or the unlockArenaID is unlocked)
        pool = pool.Where(c =>
            string.IsNullOrEmpty(c.unlockArenaID) ||
            PlayerProgress.Instance.IsArenaUnlocked(c.unlockArenaID) ||
            c.unlockArenaID == arenaID // also include cards that unlock in current arena
        ).ToList();

        // generate shard rewards for N cards
        List<ShardReward> shards = new List<ShardReward>();
        for (int i = 0; i < chest.shardCardCount; i++)
        {
            Card chosen = GetWeightedRandomCard(pool);
            if (chosen == null) continue;
            int shardAmount = Random.Range(chest.minShardCount, chest.maxShardCount + 1);
            shards.Add(new ShardReward { cardID = chosen.cardID, shards = shardAmount, cardName = chosen.cardName });
            PlayerProgress.Instance.AddCardShards(chosen.cardID, arenaID, shardAmount);
            // optionally unlock the card globally (so it appears in UI) when first shards obtained
            PlayerProgress.Instance.UnlockCard(chosen.cardID);
        }

        // award gold
        PlayerProgress.Instance.AddGold(gold);

        var reward = new ChestReward { gold = gold, shards = shards };
        return reward;
    }

    Card GetWeightedRandomCard(List<Card> pool)
    {
        if (pool == null || pool.Count == 0) return null;
        // compute weights
        List<int> weights = new List<int>();
        foreach (var c in pool)
        {
            int w = weightCommon;
            switch (c.rarity)
            {
                case CardRarity.Common: w = weightCommon; break;
                case CardRarity.Rare: w = weightRare; break;
                case CardRarity.Epic: w = weightEpic; break;
                case CardRarity.Legendary: w = weightLegendary; break;
            }
            weights.Add(Mathf.Max(1, w));
        }

        int total = weights.Sum();
        int pick = Random.Range(0, total);
        for (int i = 0; i < pool.Count; i++)
        {
            pick -= weights[i];
            if (pick < 0) return pool[i];
        }
        return pool[pool.Count - 1];
    }
}

public class ShardReward
{
    public string cardID;
    public string cardName;
    public int shards;
}

public class ChestReward
{
    public int gold;
    public List<ShardReward> shards = new List<ShardReward>();
}
