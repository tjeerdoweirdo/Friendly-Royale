// ...existing code...
using UnityEngine;


public enum CardType
{
    Troop,
    Building,
    Spell,
    Targeted // e.g., cards that target buildings only or direct-target spells
}

public enum CardUnitRole { Normal, Buffer, Debuffer, Healer }
public enum CardEffectStat { None, AttackSpeed, MoveSpeed, AttackDamage, Health }
public enum CardEffectMode { None, Aura, OnHit }

public enum CardRarity
{
    Common,
    Rare,
    Epic,
    Legendary
}



[CreateAssetMenu(menuName = "CR/Card")]
public class Card : ScriptableObject
{
    // Returns the stat value for a given level (level 1 = base, level N = scaled)
    public float GetHealthForLevel(int level)
    {
        // Example: +10% per level above 1
        return baseHealth * (1f + 0.1f * (level - 1));
    }
    public float GetDamageForLevel(int level)
    {
        return baseDamage * (1f + 0.1f * (level - 1));
    }
    public float GetSpeedForLevel(int level)
    {
        return baseSpeed * (1f + 0.05f * (level - 1));
    }
    public float GetRangeForLevel(int level)
    {
        return baseRange; // Range usually doesn't scale, but you can change this if needed
    }
    public float GetAttackCooldownForLevel(int level)
    {
        return baseAttackCooldown; // Usually doesn't scale
    }
    [Header("Identification")]
    public string cardID; // unique id, e.g., "archer_01"
    public string cardName;
    public Sprite icon;

    [Header("Gameplay")]
    public CardType cardType = CardType.Troop;
    public CardRarity rarity = CardRarity.Common;

    [Tooltip("If non-empty, this card is only unlocked after this arena is unlocked.")]
    public string unlockArenaID = "";

    [Header("Spawn (for Troop/Building)")]
    public GameObject unitPrefab; // prefab spawned when card is played

    [Header("Spell (if CardType is Spell)")]
    public Spell spellAsset; // assign a Spell ScriptableObject here

    // Spell upgradeable fields
    public int spellLevel = 1;
    public int spellMaxLevel = 5;

    // Example: scale spell effects per level
    public float GetSpellEffectForLevel(int level)
    {
        if (spellAsset == null) return 0f;
        // Example: damage scaling for DamageSplashSpell
        if (spellAsset is DamageSplashSpell dmg)
            return dmg.damage * (1f + 0.15f * (level - 1));
        if (spellAsset is PoisonSpell poison)
            return poison.poisonDamagePerSecond * (1f + 0.10f * (level - 1));
        if (spellAsset is FreezeSpell freeze)
            return freeze.duration * (1f + 0.10f * (level - 1));
        // Add more spell types as needed
        return 0f;
    }

    [Header("Match cost")]
    public int coinCost = 1; // match currency spent to play

    [Header("Base stats (used to scale per-level)")]
    public float baseHealth = 50f;
    public float baseDamage = 10f;
    public float baseSpeed = 3f;
    public float baseRange = 1.2f;
    public float baseAttackCooldown = 1f;

    [Header("Upgrade")]
    public int maxLevel = 10;
    [TextArea] public string description;

    // --- Buffer/Debuffer/Healer fields ---
    [Header("Role & Effects (for Buffer/Debuffer/Healer)")]
    public CardUnitRole unitRole = CardUnitRole.Normal;
    public CardEffectStat effectStat = CardEffectStat.None;
    [Tooltip("Positive for buff/heal, negative for debuff")] public float effectAmount = 0f;
    [Tooltip("Duration of buff/debuff/heal in seconds")] public float effectDuration = 3f;
    public CardEffectMode effectMode = CardEffectMode.None;
    [Tooltip("Aura radius for buff/debuff/heal")] public float auraRadius = 3f;
    [Tooltip("Interval for aura effect")] public float auraInterval = 1f;

    // --- Building-specific fields ---
    [Header("Building Settings (only used if CardType is Building)")]
    public Building.BuildingType buildingType = Building.BuildingType.Default;

    [Header("Building Lifetime & Decay")]
    [Tooltip("How long the building lasts after being placed (0 = infinite)")]
    public float buildingLifetime = 30f;
    [Tooltip("HP lost per second (0 = no decay)")]
    public float buildingHpDecayPerSecond = 0f;

    // Spawner settings
    [Header("Spawner Settings (if BuildingType is Spawner)")]
    public GameObject spawnUnitPrefab;
    public float spawnInterval = 2.5f;
    public int maxActive = 3;
    public int maxTotalSpawns = 6;

    // Defense settings
    [Header("Defense Settings (if BuildingType is Defense)")]
    public float defenseAttackRange = 6f;
    public float defenseAttackDamage = 12f;
    public float defenseAttackCooldown = 1.0f;

    public bool IsBuilding() => cardType == CardType.Building;
    public bool IsSpell() => cardType == CardType.Spell;
    public bool RequiresArena() => !string.IsNullOrEmpty(unlockArenaID);
}